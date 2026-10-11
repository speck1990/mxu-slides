namespace Slides.Core;

public static class MediaDeckImport
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".mp4", ".wmv", ".avi", ".mov"
    };

    public static Presentation FromFiles(IEnumerable<string> paths)
    {
        var files = paths.ToArray();
        if (files.Length == 0) throw new ArgumentException("Choose at least one image or video.", nameof(paths));
        foreach (var file in files)
        {
            if (!SupportedExtensions.Contains(Path.GetExtension(file)))
                throw new InvalidDataException($"Unsupported image or video: {Path.GetFileName(file)}");
            if (!File.Exists(file)) throw new FileNotFoundException("The selected media file is missing.", file);
        }
        return new Presentation
        {
            Name = files.Length == 1 ? Path.GetFileNameWithoutExtension(files[0]) : "Media Deck",
            Kind = "deck",
            Slides = files.Select(file => new Slide
            {
                Name = Path.GetFileNameWithoutExtension(file),
                MediaPath = Path.GetFullPath(file)
            }).ToList()
        };
    }
}
