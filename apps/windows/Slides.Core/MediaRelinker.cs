namespace Slides.Core;

public readonly record struct MediaRelinkResult(int Updated, int Unresolved, int Ambiguous);

public static class MediaRelinker
{
    public static MediaRelinkResult Relink(IEnumerable<Presentation> presentations, string searchFolder)
    {
        if (!Directory.Exists(searchFolder))
            throw new DirectoryNotFoundException("Choose a folder containing the missing media files.");

        var missing = MediaPaths.All(presentations)
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Path) && !File.Exists(reference.Path))
            .ToArray();
        var neededNames = missing.Select(reference => FileName(reference.Path!))
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
        foreach (var reference in missing)
        {
            var name = FileName(reference.Path!);
            if (!matches.TryGetValue(name, out var paths)) unresolved++;
            else if (paths.Count != 1) ambiguous++;
            else
            {
                reference.Path = paths[0];
                updated++;
            }
        }
        return new MediaRelinkResult(updated, unresolved, ambiguous);
    }

    private static string FileName(string path) => Path.GetFileName(path.Replace('\\', '/'));
}
