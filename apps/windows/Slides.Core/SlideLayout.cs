using System.Text.Json;
using System.Text.RegularExpressions;

namespace Slides.Core;

public static partial class SlideLayout
{
    public const double Width = 960;
    public const double Height = 540;

    public static List<SlideElement> BeginEdit(Slide slide)
    {
        if (slide.Elements is { Count: > 0 }) return Copy(slide.Elements);
        return string.IsNullOrEmpty(slide.Text) ? [] :
        [new SlideElement { Name = "Main text", Text = slide.Text }];
    }

    public static List<SlideElement> Copy(List<SlideElement> elements) =>
        JsonSerializer.Deserialize<List<SlideElement>>(JsonSerializer.Serialize(elements, ShowFiles.Json), ShowFiles.Json)
        ?? throw new InvalidDataException("The slide layout could not be copied.");

    public static SlideElement AddText(List<SlideElement> elements)
    {
        var element = new SlideElement { Name = $"Text {elements.Count + 1}" };
        elements.Add(element);
        return element;
    }

    public static SlideElement AddShape(List<SlideElement> elements)
    {
        var element = new SlideElement
        {
            Kind = "shape", Name = $"Rectangle {elements.Count + 1}",
            X = 280, Y = 180, Width = 400, Height = 180
        };
        elements.Add(element);
        return element;
    }

    public static SlideElement AddEllipse(List<SlideElement> elements)
    {
        var element = AddShape(elements);
        element.Name = $"Ellipse {elements.Count}";
        element.ShapeKind = "ellipse";
        return element;
    }

    public static SlideElement AddImage(List<SlideElement> elements, string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Choose an existing image.", path);
        if (!IsImage(path)) throw new ArgumentException("Choose a PNG, JPEG, BMP, or GIF image.", nameof(path));
        var element = new SlideElement
        {
            Kind = "image", Name = Path.GetFileNameWithoutExtension(path), MediaPath = Path.GetFullPath(path),
            X = 240, Y = 135, Width = 480, Height = 270
        };
        elements.Add(element);
        return element;
    }

    public static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant()
        is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";

    public static void Translate(SlideElement element, double dx, double dy)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy))
            throw new ArgumentException("Enter a valid movement.");
        element.X = Math.Clamp(element.X + dx, -960, 1920);
        element.Y = Math.Clamp(element.Y + dy, -540, 1080);
    }

    public static void Resize(SlideElement element, double dw, double dh)
    {
        if (!double.IsFinite(dw) || !double.IsFinite(dh))
            throw new ArgumentException("Enter a valid size change.");
        element.Width = Math.Clamp(element.Width + dw, 12, 1920);
        element.Height = Math.Clamp(element.Height + dh, 12, 1080);
    }

    public static SlideElement Duplicate(List<SlideElement> elements, int index)
    {
        var copy = Copy([elements[index]])[0];
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name += " copy";
        copy.X += 20;
        copy.Y += 20;
        elements.Insert(index + 1, copy);
        return copy;
    }

    public static void Move(List<SlideElement> elements, int from, int to)
    {
        if (from < 0 || from >= elements.Count || to < 0 || to >= elements.Count)
            throw new ArgumentOutOfRangeException(nameof(from), "Choose an object in this slide.");
        var element = elements[from];
        elements.RemoveAt(from);
        elements.Insert(to, element);
    }

    public static void Apply(Slide slide, List<SlideElement> elements)
    {
        Validate(elements);
        slide.Elements = elements.Count == 0 ? null : Copy(elements);
        slide.Text = string.Join("\n", elements.Where(item => item.Kind == "text")
            .Select(item => item.Text).Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    public static void Validate(IEnumerable<SlideElement> elements)
    {
        foreach (var item in elements)
        {
            if (item.Kind is not ("text" or "shape" or "image"))
                throw new ArgumentException("The layout contains an unsupported object.");
            if (string.IsNullOrWhiteSpace(item.Name))
                throw new ArgumentException("Name every slide object.");
            if (!double.IsFinite(item.X) || !double.IsFinite(item.Y) ||
                !double.IsFinite(item.Width) || !double.IsFinite(item.Height) ||
                item.X is < -960 or > 1920 || item.Y is < -540 or > 1080 ||
                item.Width is <= 0 or > 1920 || item.Height is <= 0 or > 1080)
                throw new ArgumentException($"Enter valid position and size for {item.Name}.");
            if (item.Kind == "shape" && (item.FillColorHex is null || !ColorHex().IsMatch(item.FillColorHex)))
                throw new ArgumentException($"Use #RRGGBB or #RRGGBBAA for {item.Name}'s fill.");
            if (item.Kind == "shape" && item.ShapeKind is not ("rectangle" or "ellipse"))
                throw new ArgumentException($"Choose a supported shape for {item.Name}.");
            if (item.Kind == "image" && (string.IsNullOrWhiteSpace(item.MediaPath) ||
                !IsImage(item.MediaPath) || !File.Exists(item.MediaPath)))
                throw new ArgumentException($"Image is missing for {item.Name}. Use Relink Media or choose another image.");
            if (item.Kind == "image" && item.MediaScaleMode is not ("fit" or "fill" or "stretch"))
                throw new ArgumentException($"Choose fit, fill, or stretch for {item.Name}.");
            if (!double.IsFinite(item.CornerRadius) || item.CornerRadius is < 0 or > 200)
                throw new ArgumentException($"Enter a corner radius from 0 to 200 for {item.Name}.");
            if (item.TextStyle is { } style)
                PresentationEditing.ValidateTextStyle(style, item.Name);
        }
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$")]
    private static partial Regex ColorHex();
}
