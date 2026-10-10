using System.Text.Json;

namespace Slides.Core;

public sealed record SlideTheme(
    string Id, string Name, string FontName, double FontSize,
    string TextColorHex, string BackgroundColorHex, string Weight);

public static class SlideThemes
{
    // Colors follow the macOS StarterPack looks. Fonts are Windows-installed substitutes.
    public static IReadOnlyList<SlideTheme> All { get; } =
    [
        new("cleanGeometric", "Clean Geometric", "Segoe UI", 54, "#FFFFFFFF", "#0B1220FF", "semibold"),
        new("editorialSerif", "Editorial Serif", "Georgia", 50, "#F7F3EAFF", "#14120FFF", "normal"),
        new("boldBlocks", "Bold Blocks", "Segoe UI Black", 56, "#FFFFFFFF", "#111111FF", "bold"),
        new("digital", "Digital", "Consolas", 48, "#E6FBFFFF", "#05080CFF", "normal")
    ];

    public static SlideTextStyle Effective(Slide slide) => slide.TextStyle ?? new SlideTextStyle();

    public static void Apply(Presentation presentation, SlideTheme theme)
    {
        foreach (var slide in presentation.Slides)
        {
            slide.Background = theme.BackgroundColorHex;
            slide.TextStyle = new SlideTextStyle
            {
                FontName = theme.FontName,
                FontSize = theme.FontSize,
                ColorHex = theme.TextColorHex,
                Weight = theme.Weight
            };
            if (slide.Elements is { } elements)
                foreach (var element in elements.Where(item => item.Kind == "text"))
                    element.TextStyle = new SlideTextStyle
                    {
                        FontName = theme.FontName,
                        FontSize = theme.FontSize,
                        ColorHex = theme.TextColorHex,
                        Weight = theme.Weight
                    };
        }
    }

    public static SlideTextStyle? FromMxuTextStyle(JsonElement source)
    {
        if (source.ValueKind != JsonValueKind.Object) return null;
        var style = new SlideTextStyle();
        var font = String(source, "fontName");
        if (!string.IsNullOrWhiteSpace(font) && font.Length <= 100) style.FontName = WindowsFont(font);
        if (source.TryGetProperty("fontSize", out var size) && size.ValueKind == JsonValueKind.Number &&
            size.TryGetDouble(out var points) &&
            double.IsFinite(points) && points is >= 24 and <= 320)
            style.FontSize = points / 2; // macOS documents use a 1920 x 1080 canvas.
        if (String(source, "colorHex") is { } color && IsColor(color)) style.ColorHex = color;
        if (String(source, "horizontalAlignment") is { } alignment &&
            alignment.ToLowerInvariant() is "left" or "center" or "right")
            style.HorizontalAlignment = alignment.ToLowerInvariant();
        if (font is not null)
            style.Weight = font.Contains("DemiBold", StringComparison.OrdinalIgnoreCase) ? "semibold" :
                font.Contains("Bold", StringComparison.OrdinalIgnoreCase) ||
                font.Contains("Heavy", StringComparison.OrdinalIgnoreCase) ? "bold" : "normal";
        return style;
    }

    private static string? String(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static bool IsColor(string color) =>
        (color.Length is 7 or 9) && color[0] == '#' && color[1..].All(Uri.IsHexDigit);

    private static string WindowsFont(string name)
    {
        if (name.StartsWith("HelveticaNeue", StringComparison.OrdinalIgnoreCase)) return "Segoe UI";
        if (name.StartsWith("AvenirNext", StringComparison.OrdinalIgnoreCase)) return "Segoe UI";
        if (name.StartsWith("Menlo", StringComparison.OrdinalIgnoreCase)) return "Consolas";
        if (name.StartsWith("Georgia", StringComparison.OrdinalIgnoreCase)) return "Georgia";
        return name;
    }
}
