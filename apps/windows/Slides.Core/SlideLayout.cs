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
            if (item.Kind is not ("text" or "shape"))
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
            if (!double.IsFinite(item.CornerRadius) || item.CornerRadius is < 0 or > 200)
                throw new ArgumentException($"Enter a corner radius from 0 to 200 for {item.Name}.");
            if (item.TextStyle is { } style)
                PresentationEditing.ValidateTextStyle(style, item.Name);
        }
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$")]
    private static partial Regex ColorHex();
}
