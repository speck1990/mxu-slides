using System.Text.Json;

namespace Slides.Core;

public sealed class PresentationLibrary
{
    public int Version { get; set; } = 1;
    public List<Presentation> Presentations { get; set; } = [];
    public List<LibraryFolder> Folders { get; set; } = [];
    // Missing assignment means the presentation is at the top level.
    public Dictionary<string, string> FolderAssignments { get; set; } = [];

    public LibraryFolder CreateFolder(string name)
    {
        var folder = new LibraryFolder { Name = ValidFolderName(name) };
        Folders.Add(folder);
        return folder;
    }

    public void RenameFolder(string folderId, string name)
    {
        var folder = Folders.FirstOrDefault(item => item.Id == folderId)
            ?? throw new ArgumentException("Choose a library folder.", nameof(folderId));
        folder.Name = ValidFolderName(name, folderId);
    }

    public void DeleteFolder(string folderId)
    {
        var folder = Folders.FirstOrDefault(item => item.Id == folderId)
            ?? throw new ArgumentException("Choose a library folder.", nameof(folderId));
        Folders.Remove(folder);
        foreach (var presentationId in FolderAssignments.Where(pair => pair.Value == folderId)
                     .Select(pair => pair.Key).ToArray())
            FolderAssignments.Remove(presentationId);
    }

    public string? FolderFor(string presentationId) =>
        FolderAssignments.TryGetValue(presentationId, out var folderId) ? folderId : null;

    public void MoveToFolder(string presentationId, string? folderId)
    {
        if (!Presentations.Any(item => item.Id == presentationId))
            throw new ArgumentException("Choose a presentation from the library.", nameof(presentationId));
        if (folderId is null) FolderAssignments.Remove(presentationId);
        else
        {
            if (!Folders.Any(item => item.Id == folderId))
                throw new ArgumentException("Choose a library folder.", nameof(folderId));
            FolderAssignments[presentationId] = folderId;
        }
    }

    private string ValidFolderName(string name, string? currentId = null)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) throw new ArgumentException("Give the folder a name.", nameof(name));
        if (trimmed.Equals("All Slides", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Top Level", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a name other than All Slides or Top Level.", nameof(name));
        if (Folders.Any(item => item.Id != currentId &&
            string.Equals(item.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A folder with that name already exists.", nameof(name));
        return trimmed;
    }

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

    public bool RepairMissingMediaFrom(IEnumerable<Presentation> presentations)
    {
        var changed = false;
        var byId = Presentations.ToDictionary(presentation => presentation.Id);
        foreach (var source in presentations)
        {
            if (!byId.TryGetValue(source.Id, out var target)) continue;
            var slides = target.Slides.ToDictionary(slide => slide.Id);
            foreach (var sourceSlide in source.Slides)
            {
                if (!slides.TryGetValue(sourceSlide.Id, out var targetSlide)) continue;
                if (CanRepair(sourceSlide.MediaPath, targetSlide.MediaPath))
                {
                    targetSlide.MediaPath = sourceSlide.MediaPath;
                    changed = true;
                }
                if (sourceSlide.Elements is null || targetSlide.Elements is null) continue;
                foreach (var sourceElement in sourceSlide.Elements.Where(item => item.Kind == "image"))
                {
                    var targetElement = targetSlide.Elements.FirstOrDefault(item => item.Id == sourceElement.Id && item.Kind == "image");
                    if (targetElement is null || !CanRepair(sourceElement.MediaPath, targetElement.MediaPath)) continue;
                    targetElement.MediaPath = sourceElement.MediaPath;
                    changed = true;
                }
            }
        }
        return changed;
    }

    private static bool CanRepair(string? source, string? target) =>
        !string.IsNullOrWhiteSpace(source) && File.Exists(source) &&
        (string.IsNullOrWhiteSpace(target) || !File.Exists(target));

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

    private static Presentation Copy(Presentation source) => PresentationEditing.Copy(source);
}

public sealed class LibraryFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Folder";
}

public static class LibraryFiles
{
    public static PresentationLibrary Load(string path)
    {
        if (!File.Exists(path)) return new PresentationLibrary();
        var library = JsonSerializer.Deserialize<PresentationLibrary>(File.ReadAllText(path), ShowFiles.Json)
            ?? throw new InvalidDataException("The library file is empty.");
        if (library.Version != 1 || library.Presentations is null || library.Folders is null ||
            library.FolderAssignments is null)
            throw new InvalidDataException("Unsupported library file version.");
        if (library.Presentations.Select(item => item.Id).Distinct().Count() != library.Presentations.Count)
            throw new InvalidDataException("The library contains duplicate presentation IDs.");
        if (library.Folders.Select(item => item.Id).Distinct().Count() != library.Folders.Count ||
            library.Folders.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != library.Folders.Count)
            throw new InvalidDataException("The library contains duplicate folders.");
        if (library.FolderAssignments.Any(pair => !library.Presentations.Any(item => item.Id == pair.Key) ||
            !library.Folders.Any(item => item.Id == pair.Value)))
            throw new InvalidDataException("The library contains an invalid folder assignment.");
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
