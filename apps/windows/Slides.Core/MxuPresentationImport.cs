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
            SlideTextStyle? textStyle = null;
            if (source.TryGetProperty("objects", out var objects) && objects.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in objects.EnumerateArray())
                {
                    if (Text(item, "objectKind") != "text" ||
                        (item.TryGetProperty("hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True))
                        continue;
                    var value = Text(item, "text");
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        lines.Add(value);
                        if (textStyle is null && item.TryGetProperty("textStyle", out var style))
                            textStyle = SlideThemes.FromMxuTextStyle(style);
                    }
                }
            }
            presentation.Slides.Add(new Slide
            {
                Name = Text(source, "name") ?? "Slide",
                Text = string.Join("\n", lines),
                Background = Color(source) ?? Color(root) ?? "#0B1220",
                TextStyle = textStyle
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
}
