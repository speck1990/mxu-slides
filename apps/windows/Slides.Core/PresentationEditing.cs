using System.Text.Json;
using System.Text.RegularExpressions;

namespace Slides.Core;

public static partial class PresentationEditing
{
    public static Presentation NewDeck() => new()
    {
        Name = "New deck",
        Kind = "deck",
        Slides = [new Slide { Name = "Slide 1" }]
    };

    public static Presentation Copy(Presentation presentation) =>
        JsonSerializer.Deserialize<Presentation>(JsonSerializer.Serialize(presentation, ShowFiles.Json), ShowFiles.Json)
        ?? throw new InvalidDataException("The presentation could not be copied.");

    public static Slide AddSlide(Presentation presentation, string? mediaPath = null)
    {
        var previous = presentation.Slides.LastOrDefault();
        var slide = new Slide
        {
            Name = mediaPath is null ? $"Slide {presentation.Slides.Count + 1}" : Path.GetFileNameWithoutExtension(mediaPath),
            MediaPath = mediaPath,
            Background = previous?.Background ?? "#0B1220",
            TextStyle = previous?.TextStyle is { } style ? CopyStyle(style) : null
        };
        presentation.Slides.Add(slide);
        return slide;
    }

    public static Slide DuplicateSlide(Presentation presentation, int index)
    {
        var copy = JsonSerializer.Deserialize<Slide>(JsonSerializer.Serialize(presentation.Slides[index], ShowFiles.Json), ShowFiles.Json)
            ?? throw new InvalidDataException("The slide could not be copied.");
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name += " copy";
        presentation.Slides.Insert(index + 1, copy);
        return copy;
    }

    public static void MoveSlide(Presentation presentation, int from, int to)
    {
        if (from < 0 || from >= presentation.Slides.Count || to < 0 || to >= presentation.Slides.Count)
            throw new ArgumentOutOfRangeException(nameof(from), "Choose a slide in this presentation.");
        var slide = presentation.Slides[from];
        presentation.Slides.RemoveAt(from);
        presentation.Slides.Insert(to, slide);
    }

    public static void PreserveSongDesign(Presentation original, Presentation replacement)
    {
        for (var index = 0; index < replacement.Slides.Count && original.Slides.Count > 0; index++)
        {
            var source = original.Slides[Math.Min(index, original.Slides.Count - 1)];
            var slide = replacement.Slides[index];
            slide.Background = source.Background;
            slide.TextStyle = source.TextStyle is { } style ? CopyStyle(style) : null;
            if (index < original.Slides.Count)
            {
                slide.Id = source.Id;
                slide.MediaPath = source.MediaPath;
            }
        }
    }

    public static void Validate(Presentation presentation)
    {
        if (string.IsNullOrWhiteSpace(presentation.Name))
            throw new ArgumentException("Enter a presentation title.");
        if (presentation.Slides.Count == 0)
            throw new ArgumentException("Add at least one slide.");
        foreach (var slide in presentation.Slides)
        {
            if (string.IsNullOrWhiteSpace(slide.Name))
                throw new ArgumentException("Enter a name for every slide.");
            if (slide.Background is null || !ColorHex().IsMatch(slide.Background))
                throw new ArgumentException($"Use #RRGGBB or #RRGGBBAA for the background on {slide.Name}.");
            if (slide.TextStyle is { } style) ValidateTextStyle(style, slide.Name);
            if (slide.Elements is { Count: > 0 } elements) SlideLayout.Validate(elements);
            if (slide.MediaPath is { Length: > 0 } && !File.Exists(slide.MediaPath))
                throw new FileNotFoundException($"Media is missing for {slide.Name}.", slide.MediaPath);
        }
    }

    internal static void ValidateTextStyle(SlideTextStyle style, string name)
    {
        if (string.IsNullOrWhiteSpace(style.FontName) || style.FontName.Length > 100)
            throw new ArgumentException($"Choose a font for {name}.");
        if (!double.IsFinite(style.FontSize) || style.FontSize is < 12 or > 160)
            throw new ArgumentException($"Use a font size from 12 to 160 for {name}.");
        if (style.ColorHex is null || !ColorHex().IsMatch(style.ColorHex))
            throw new ArgumentException($"Use #RRGGBB or #RRGGBBAA for the text color on {name}.");
        if (style.HorizontalAlignment is not ("left" or "center" or "right"))
            throw new ArgumentException($"Choose Left, Center, or Right alignment for {name}.");
        if (style.Weight is not ("normal" or "semibold" or "bold"))
            throw new ArgumentException($"Choose a text weight for {name}.");
    }

    private static SlideTextStyle CopyStyle(SlideTextStyle style) =>
        JsonSerializer.Deserialize<SlideTextStyle>(JsonSerializer.Serialize(style, ShowFiles.Json), ShowFiles.Json)
        ?? throw new InvalidDataException("The text style could not be copied.");

    [GeneratedRegex("^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$")]
    private static partial Regex ColorHex();
}
