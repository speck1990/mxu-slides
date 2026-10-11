namespace Slides.Core;

public sealed class Overlay
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Overlay";
    public string Text { get; set; } = "";
    public string Layout { get; set; } = "lowerThird";
    public string BackgroundColor { get; set; } = "#101828DD";
    public string TextColor { get; set; } = "#FFFFFFFF";

    public static Overlay Copy(Overlay source) => new()
    {
        Id = source.Id, Name = source.Name, Text = source.Text, Layout = source.Layout,
        BackgroundColor = source.BackgroundColor, TextColor = source.TextColor
    };

    public static void ValidateList(List<Overlay>? overlays)
    {
        if (overlays is null || overlays.Any(item => item is null ||
            string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 120 ||
            item.Text is null || item.Text.Length > 2000 || item.Layout is not ("lowerThird" or "fullScreen") ||
            !ValidColor(item.BackgroundColor) || !ValidColor(item.TextColor)) ||
            overlays.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != overlays.Count)
            throw new InvalidDataException("The show contains invalid overlays.");
    }

    private static bool ValidColor(string? color) => color is { Length: 9 } && color[0] == '#' &&
        color.Skip(1).All(char.IsAsciiHexDigit);
}
