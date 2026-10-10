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
        var slide = new Slide
        {
            Name = mediaPath is null ? $"Slide {presentation.Slides.Count + 1}" : Path.GetFileNameWithoutExtension(mediaPath),
            MediaPath = mediaPath
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
            if (!ColorHex().IsMatch(slide.Background))
                throw new ArgumentException($"Use #RRGGBB or #RRGGBBAA for the background on {slide.Name}.");
            if (slide.MediaPath is { Length: > 0 } && !File.Exists(slide.MediaPath))
                throw new FileNotFoundException($"Media is missing for {slide.Name}.", slide.MediaPath);
        }
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$")]
    private static partial Regex ColorHex();
}
