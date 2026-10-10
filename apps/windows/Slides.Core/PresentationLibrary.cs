using System.Text.Json;

namespace Slides.Core;

public sealed class PresentationLibrary
{
    public int Version { get; set; } = 1;
    public List<Presentation> Presentations { get; set; } = [];

    public bool ImportMissing(IEnumerable<Presentation> presentations)
    {
        var known = Presentations.Select(presentation => presentation.Id).ToHashSet();
        var added = false;
        foreach (var presentation in presentations)
        {
            if (!known.Add(presentation.Id)) continue;
            Presentations.Add(Copy(presentation));
            added = true;
        }
        return added;
    }

    public void Upsert(Presentation presentation)
    {
        var copy = Copy(presentation);
        var index = Presentations.FindIndex(item => item.Id == presentation.Id);
        if (index < 0) Presentations.Add(copy);
        else Presentations[index] = copy;
    }

    public void AddToShow(Show show, string presentationId)
    {
        var source = Presentations.FirstOrDefault(item => item.Id == presentationId)
            ?? throw new ArgumentException("Choose a presentation from the library.", nameof(presentationId));
        if (!show.Presentations.Any(item => item.Id == presentationId))
            show.Presentations.Add(Copy(source));
        show.AddToService(presentationId);
    }

    private static Presentation Copy(Presentation source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Kind = source.Kind,
        Slides = source.Slides.Select(slide => new Slide
        {
            Id = slide.Id,
            Name = slide.Name,
            Text = slide.Text,
            MediaPath = slide.MediaPath,
            Background = slide.Background
        }).ToList()
    };
}

public static class LibraryFiles
{
    public static PresentationLibrary Load(string path)
    {
        if (!File.Exists(path)) return new PresentationLibrary();
        var library = JsonSerializer.Deserialize<PresentationLibrary>(File.ReadAllText(path), ShowFiles.Json)
            ?? throw new InvalidDataException("The library file is empty.");
        if (library.Version != 1 || library.Presentations is null)
            throw new InvalidDataException("Unsupported library file version.");
        if (library.Presentations.Select(item => item.Id).Distinct().Count() != library.Presentations.Count)
            throw new InvalidDataException("The library contains duplicate presentation IDs.");
        return library;
    }

    public static void Save(string path, PresentationLibrary library)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(library, ShowFiles.Json));
        File.Move(temporary, path, true);
    }
}
