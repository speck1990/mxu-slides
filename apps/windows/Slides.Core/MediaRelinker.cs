namespace Slides.Core;

public readonly record struct MediaRelinkResult(int Updated, int Unresolved, int Ambiguous);

public static class MediaRelinker
{
    public static MediaRelinkResult Relink(IEnumerable<Presentation> presentations, string searchFolder)
    {
        if (!Directory.Exists(searchFolder))
            throw new DirectoryNotFoundException("Choose a folder containing the missing media files.");

        var missing = presentations.SelectMany(presentation => presentation.Slides)
            .Where(slide => !string.IsNullOrWhiteSpace(slide.MediaPath) && !File.Exists(slide.MediaPath))
            .ToArray();
        var neededNames = missing.Select(slide => FileName(slide.MediaPath!))
            .Where(name => name.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (neededNames.Count > 0)
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            foreach (var path in Directory.EnumerateFiles(searchFolder, "*", options))
            {
                var name = Path.GetFileName(path);
                if (!neededNames.Contains(name)) continue;
                if (!matches.TryGetValue(name, out var paths)) matches[name] = paths = [];
                paths.Add(path);
            }
        }

        var updated = 0;
        var unresolved = 0;
        var ambiguous = 0;
        foreach (var slide in missing)
        {
            var name = FileName(slide.MediaPath!);
            if (!matches.TryGetValue(name, out var paths)) unresolved++;
            else if (paths.Count != 1) ambiguous++;
            else
            {
                slide.MediaPath = paths[0];
                updated++;
            }
        }
        return new MediaRelinkResult(updated, unresolved, ambiguous);
    }

    private static string FileName(string path) => Path.GetFileName(path.Replace('\\', '/'));
}
