using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace Slides.Core;

public sealed record PowerPointImportResult(Presentation Presentation, int SkippedObjects);

// Imports the PowerPoint parts that Slides can render without Office or a network connection.
public static class PowerPointImport
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageR = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const long MaximumXmlBytes = 16L * 1024 * 1024;

    public static PowerPointImportResult Load(string path, string? mediaCacheRoot = null)
    {
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count > 2000 || archive.Entries.Sum(entry => entry.Length) > 512L * 1024 * 1024)
            throw new InvalidDataException("The PowerPoint file is too large to import.");
        var presentationXml = Xml(archive, "ppt/presentation.xml");
        var size = presentationXml.Root?.Element(P + "sldSz")
            ?? throw new InvalidDataException("The PowerPoint file has no slide size.");
        var slideWidth = Positive(size.Attribute("cx"));
        var slideHeight = Positive(size.Attribute("cy"));
        var relationships = Relationships(archive, "ppt/_rels/presentation.xml.rels");
        mediaCacheRoot ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Slides", "imports");
        var presentation = new Presentation
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Kind = "deck"
        };
        var skipped = 0;
        var slideIds = presentationXml.Root?.Element(P + "sldIdLst")?.Elements(P + "sldId").ToArray()
            ?? throw new InvalidDataException("The PowerPoint file has no slide list.");
        foreach (var slideId in slideIds)
        {
            var id = (string?)slideId.Attribute(R + "id");
            if (id is null || !relationships.TryGetValue(id, out var target))
                throw new InvalidDataException("A PowerPoint slide relationship is missing.");
            var slidePath = Resolve("ppt/presentation.xml", target);
            var source = Xml(archive, slidePath);
            var slideRelationships = OptionalRelationships(archive, RelationshipPart(slidePath));
            var layoutTarget = slideRelationships.Values.FirstOrDefault(value =>
                value.Contains("slideLayout", StringComparison.OrdinalIgnoreCase));
            var layout = layoutTarget is null ? null : Xml(archive, Resolve(slidePath, layoutTarget));
            var layoutShapes = layout?.Root?.Element(P + "cSld")?.Element(P + "spTree")?.Elements(P + "sp")
                .ToArray() ?? [];
            var slide = new Slide
            {
                Name = $"Slide {presentation.Slides.Count + 1}",
                Background = Color(source.Root?.Element(P + "cSld")?.Element(P + "bg")?.Element(P + "bgPr"))
                    ?? Color(layout?.Root?.Element(P + "cSld")?.Element(P + "bg")?.Element(P + "bgPr"))
                    ?? "#FFFFFFFF",
                TextStyle = new SlideTextStyle { ColorHex = "#000000FF" }
            };
            var elements = new List<SlideElement>();
            var summary = new List<string>();
            foreach (var child in source.Root?.Element(P + "cSld")?.Element(P + "spTree")?.Elements()
                         ?? Enumerable.Empty<XElement>())
            {
                if (child.Name == P + "sp")
                {
                    var text = string.Join("\n", child.Element(P + "txBody")?.Elements(A + "p")
                        .Select(paragraph => string.Concat(paragraph.Descendants(A + "t").Select(value => value.Value)))
                        .Where(line => !string.IsNullOrWhiteSpace(line)) ?? []);
                    var geometry = Geometry(child.Element(P + "spPr"), slideWidth, slideHeight)
                        ?? PlaceholderGeometry(child, layoutShapes, slideWidth, slideHeight);
                    var fill = Color(child.Element(P + "spPr"));
                    var shape = (string?)child.Element(P + "spPr")?.Element(A + "prstGeom")?.Attribute("prst");
                    var name = SafeName((string?)child.Element(P + "nvSpPr")?.Element(P + "cNvPr")?.Attribute("name"), "Object");
                    var transform = child.Element(P + "spPr")?.Element(A + "xfrm");
                    if (fill is not null && shape is ("rect" or "roundRect" or "ellipse") && geometry is { } shapeBox)
                        elements.Add(new SlideElement
                        {
                            Kind = "shape", Name = name, ShapeKind = shape == "ellipse" ? "ellipse" : "rectangle",
                            FillColorHex = fill, X = shapeBox.X, Y = shapeBox.Y,
                            Width = shapeBox.Width, Height = shapeBox.Height,
                            RotationDegrees = Rotation(transform), FlipHorizontal = Flip(transform, "flipH"),
                            FlipVertical = Flip(transform, "flipV")
                        });
                    if (text.Length > 0)
                    {
                        summary.Add(text);
                        var box = geometry ?? (70d, 30d, 820d, 480d);
                        elements.Add(new SlideElement
                        {
                            Kind = "text", Name = name, Text = text,
                            X = box.X, Y = box.Y, Width = box.Width, Height = box.Height,
                            TextStyle = TextStyle(child, "#000000FF"),
                            RotationDegrees = Rotation(transform), FlipHorizontal = Flip(transform, "flipH"),
                            FlipVertical = Flip(transform, "flipV")
                        });
                        if (geometry is null) skipped++;
                    }
                    else if (fill is not null && geometry is null) skipped++;
                }
                else if (child.Name == P + "pic")
                {
                    var box = Geometry(child.Element(P + "spPr"), slideWidth, slideHeight);
                    var embed = (string?)child.Element(P + "blipFill")?.Element(A + "blip")?.Attribute(R + "embed");
                    if (box is null || embed is null) { skipped++; continue; }
                    if (!slideRelationships.TryGetValue(embed, out var imageTarget)) { skipped++; continue; }
                    var imagePath = Resolve(slidePath, imageTarget);
                    var extension = Path.GetExtension(imagePath).ToLowerInvariant();
                    if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif"))
                    { skipped++; continue; }
                    var localPath = CacheImage(archive, imagePath, mediaCacheRoot);
                    var name = SafeName((string?)child.Element(P + "nvPicPr")?.Element(P + "cNvPr")?.Attribute("name"),
                        Path.GetFileNameWithoutExtension(imagePath));
                    var transform = child.Element(P + "spPr")?.Element(A + "xfrm");
                    elements.Add(new SlideElement
                    {
                        Kind = "image", Name = name, MediaPath = localPath,
                        X = box.Value.X, Y = box.Value.Y, Width = box.Value.Width, Height = box.Value.Height,
                        RotationDegrees = Rotation(transform), FlipHorizontal = Flip(transform, "flipH"),
                        FlipVertical = Flip(transform, "flipV")
                    });
                }
                else if (child.Name != P + "nvGrpSpPr" && child.Name != P + "grpSpPr")
                    skipped++;
            }
            slide.Text = string.Join("\n", summary);
            if (elements.Count > 0) slide.Elements = elements;
            presentation.Slides.Add(slide);
        }
        if (presentation.Slides.Count == 0)
            throw new InvalidDataException("The PowerPoint presentation contains no slides.");
        return new PowerPointImportResult(presentation, skipped);
    }

    private static XDocument Xml(ZipArchive archive, string name)
    {
        var matches = archive.Entries.Where(entry => entry.FullName == name).Take(2).ToArray();
        if (matches.Length != 1 || matches[0].Length > MaximumXmlBytes)
            throw new InvalidDataException($"The PowerPoint file is missing a valid {name} part.");
        using var stream = matches[0].Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumXmlBytes
        });
        return XDocument.Load(reader);
    }

    private static Dictionary<string, string> Relationships(ZipArchive archive, string name) =>
        Xml(archive, name).Root?.Elements(PackageR + "Relationship")
            .Where(item => (string?)item.Attribute("TargetMode") != "External")
            .ToDictionary(item => (string?)item.Attribute("Id")
                ?? throw new InvalidDataException("A PowerPoint relationship has no ID."),
                item => (string?)item.Attribute("Target")
                    ?? throw new InvalidDataException("A PowerPoint relationship has no target."))
        ?? throw new InvalidDataException("The PowerPoint relationship list is missing.");

    private static Dictionary<string, string> OptionalRelationships(ZipArchive archive, string name) =>
        archive.Entries.Any(entry => entry.FullName == name) ? Relationships(archive, name) : [];

    private static (double X, double Y, double Width, double Height)? PlaceholderGeometry(
        XElement shape, XElement[] layoutShapes, double slideWidth, double slideHeight)
    {
        var placeholder = shape.Element(P + "nvSpPr")?.Element(P + "nvPr")?.Element(P + "ph");
        if (placeholder is null) return null;
        var index = (string?)placeholder.Attribute("idx");
        var type = (string?)placeholder.Attribute("type") ?? "obj";
        var candidates = layoutShapes.Where(item =>
        {
            var layoutPlaceholder = item.Element(P + "nvSpPr")?.Element(P + "nvPr")?.Element(P + "ph");
            return layoutPlaceholder is not null &&
                (index is null || (string?)layoutPlaceholder.Attribute("idx") == index) &&
                ((string?)layoutPlaceholder.Attribute("type") ?? "obj") == type;
        }).ToArray();
        return candidates.Length == 1
            ? Geometry(candidates[0].Element(P + "spPr"), slideWidth, slideHeight) : null;
    }

    private static string RelationshipPart(string partPath)
    {
        var slash = partPath.LastIndexOf('/');
        return partPath[..(slash + 1)] + "_rels/" + partPath[(slash + 1)..] + ".rels";
    }

    private static string CacheImage(ZipArchive archive, string entryPath, string cacheRoot)
    {
        var matches = archive.Entries.Where(entry => entry.FullName == entryPath).Take(2).ToArray();
        if (matches.Length != 1 || matches[0].Length > 64L * 1024 * 1024)
            throw new InvalidDataException("An embedded PowerPoint image is missing or too large.");
        using var data = new MemoryStream();
        using (var input = matches[0].Open())
        {
            var buffer = new byte[64 * 1024];
            int count;
            while ((count = input.Read(buffer)) > 0)
            {
                if (count > 64L * 1024 * 1024 - data.Length)
                    throw new InvalidDataException("An embedded PowerPoint image is too large.");
                data.Write(buffer, 0, count);
            }
        }
        var extension = Path.GetExtension(entryPath).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(data.GetBuffer().AsSpan(0, (int)data.Length))).ToLowerInvariant();
        var destination = Path.Combine(cacheRoot, hash + extension);
        if (File.Exists(destination)) return destination;
        Directory.CreateDirectory(cacheRoot);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = File.Create(temporary))
                data.WriteTo(output);
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return destination;
    }

    public static string Resolve(string source, string target)
    {
        if (target.StartsWith('/') || target.Contains('\\') || Uri.TryCreate(target, UriKind.Absolute, out _))
            throw new InvalidDataException("The PowerPoint file contains an unsafe relationship.");
        var parts = source.Split('/').SkipLast(1).ToList();
        foreach (var part in target.Split('/'))
        {
            if (part is "" or ".") continue;
            if (part == "..")
            {
                if (parts.Count == 0) throw new InvalidDataException("A PowerPoint relationship leaves the package.");
                parts.RemoveAt(parts.Count - 1);
            }
            else parts.Add(part);
        }
        return string.Join("/", parts);
    }

    private static double Positive(XAttribute? attribute) =>
        long.TryParse(attribute?.Value, out var value) && value > 0
            ? value : throw new InvalidDataException("The PowerPoint slide size is invalid.");

    private static (double X, double Y, double Width, double Height)? Geometry(
        XElement? properties, double slideWidth, double slideHeight)
    {
        var transform = properties?.Element(A + "xfrm");
        var offset = transform?.Element(A + "off");
        var extent = transform?.Element(A + "ext");
        if (!long.TryParse((string?)offset?.Attribute("x"), out var x) ||
            !long.TryParse((string?)offset?.Attribute("y"), out var y) ||
            !long.TryParse((string?)extent?.Attribute("cx"), out var width) ||
            !long.TryParse((string?)extent?.Attribute("cy"), out var height) ||
            width <= 0 || height <= 0) return null;
        var box = (X: x * 960d / slideWidth, Y: y * 540d / slideHeight,
            Width: width * 960d / slideWidth, Height: height * 540d / slideHeight);
        return box.X is < -960 or > 1920 || box.Y is < -540 or > 1080 ||
            box.Width is <= 0 or > 1920 || box.Height is <= 0 or > 1080 ? null : box;
    }

    private static string? Color(XElement? source)
    {
        var hex = (string?)source?.Element(A + "solidFill")?.Element(A + "srgbClr")?.Attribute("val");
        return hex is { Length: 6 } && hex.All(Uri.IsHexDigit) ? "#" + hex.ToUpperInvariant() + "FF" : null;
    }

    private static string SafeName(string? name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? string.IsNullOrWhiteSpace(fallback) ? "Object" : fallback
            : name.Length <= 120 ? name : name[..120];

    private static double Rotation(XElement? transform) =>
        long.TryParse((string?)transform?.Attribute("rot"), out var value)
            ? Math.Clamp(value / 60000d, -3600, 3600) : 0;

    private static bool Flip(XElement? transform, string name) =>
        (string?)transform?.Attribute(name) is "1" or "true";

    private static SlideTextStyle TextStyle(XElement shape, string fallbackColor)
    {
        var run = shape.Element(P + "txBody")?.Descendants(A + "rPr").FirstOrDefault();
        var end = shape.Element(P + "txBody")?.Descendants(A + "endParaRPr").FirstOrDefault();
        var properties = run ?? end;
        var fontSize = double.TryParse((string?)properties?.Attribute("sz"), out var size)
            ? Math.Clamp(size / 100d * 540d / 720d, 12, 120) : 36;
        var font = (string?)properties?.Element(A + "latin")?.Attribute("typeface");
        return new SlideTextStyle
        {
            FontSize = fontSize,
            FontName = string.IsNullOrWhiteSpace(font) || font.Length > 100 || font.StartsWith('+')
                ? "Segoe UI" : font,
            ColorHex = Color(properties) ?? fallbackColor,
            Weight = (string?)properties?.Attribute("b") == "1" ? "bold" : "normal",
            HorizontalAlignment = (string?)shape.Element(P + "txBody")?.Element(A + "p")
                ?.Element(A + "pPr")?.Attribute("algn") switch
                { "l" => "left", "r" => "right", _ => "center" }
        };
    }
}
