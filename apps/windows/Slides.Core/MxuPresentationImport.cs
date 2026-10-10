using System.Text.Json;

namespace Slides.Core;

// Reads the portable Presentation/Slide/SlideObject fields defined in
// packages/schema/documents.json. Unknown macOS-only fields are ignored.
public static class MxuPresentationImport
{
    public static Presentation Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("slides", out var slides) || slides.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Expected an MxU Presentation JSON object with slides.");

        var presentation = new Presentation
        {
            Name = Text(root, "name") ?? Path.GetFileNameWithoutExtension(path),
            Kind = Text(root, "presentationKind") ?? "deck"
        };
        foreach (var source in slides.EnumerateArray())
        {
            var lines = new List<string>();
            var elements = new List<SlideElement>();
            var positionedText = true;
            SlideTextStyle? textStyle = null;
            if (source.TryGetProperty("objects", out var objects) && objects.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in objects.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object ||
                        (item.TryGetProperty("hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True))
                        continue;
                    var kind = Text(item, "objectKind");
                    if (kind == "shape")
                    {
                        if (Positioned(item, "shape") is { } shape) elements.Add(shape);
                        continue;
                    }
                    if (kind != "text") continue;
                    var value = Text(item, "text");
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        lines.Add(value);
                        if (textStyle is null && item.TryGetProperty("textStyle", out var style))
                            textStyle = SlideThemes.FromMxuTextStyle(style);
                        if (Positioned(item, "text") is { } textElement) elements.Add(textElement);
                        else positionedText = false;
                    }
                }
            }
            presentation.Slides.Add(new Slide
            {
                Name = Text(source, "name") ?? "Slide",
                Text = string.Join("\n", lines),
                Background = Color(source) ?? Color(root) ?? "#0B1220",
                TextStyle = textStyle,
                Elements = positionedText && elements.Count > 0 ? elements : null
            });
        }
        if (presentation.Slides.Count == 0)
            throw new InvalidDataException("The presentation has no slides.");
        return presentation;
    }

    private static string? Text(JsonElement item, string property) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Color(JsonElement item) =>
        item.TryGetProperty("backgroundFill", out var fill) ? Text(fill, "colorHex") : null;

    private static SlideElement? Positioned(JsonElement item, string kind)
    {
        if (!Number(item, "x", out var x) || !Number(item, "y", out var y) ||
            !Number(item, "width", out var width) || !Number(item, "height", out var height))
            return null;
        if (kind == "shape" && Text(item, "shapeKind") is not ("rectangle" or "roundedRectangle"))
            return null;
        var element = new SlideElement
        {
            Id = Text(item, "id") ?? Guid.NewGuid().ToString("N"),
            Kind = kind,
            Name = string.IsNullOrWhiteSpace(Text(item, "name"))
                ? (kind == "text" ? "Text" : "Rectangle") : Text(item, "name")!,
            Text = Text(item, "text") ?? "",
            X = x / 2, Y = y / 2, Width = width / 2, Height = height / 2,
            TextStyle = item.TryGetProperty("textStyle", out var style)
                ? SlideThemes.FromMxuTextStyle(style) : null
        };
        if (kind == "shape")
        {
            if (!item.TryGetProperty("fill", out var fill) ||
                Text(fill, "fillKind") is not ("solid" or null) ||
                Text(fill, "colorHex") is not { } color)
                return null;
            element.FillColorHex = color;
            if (Number(item, "cornerRadius", out var radius)) element.CornerRadius = radius / 2;
        }
        try { SlideLayout.Validate([element]); return element; }
        catch (ArgumentException) { return null; }
    }

    private static bool Number(JsonElement item, string name, out double value)
    {
        value = 0;
        return item.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number &&
            field.TryGetDouble(out value) && double.IsFinite(value);
    }
}
