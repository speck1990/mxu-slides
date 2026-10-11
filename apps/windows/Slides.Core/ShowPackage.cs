using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Slides.Core;

public static partial class ShowPackage
{
    private const string Format = "Slides portable show 1\n";
    private const long MaximumShowBytes = 64L * 1024 * 1024;
    private const long MaximumMediaBytes = 50L * 1024 * 1024 * 1024;

    [GeneratedRegex(@"^media/[0-9a-f]{64}\.[a-z0-9]{1,12}$")]
    private static partial Regex MediaEntryPath();

    public static void Save(string path, Show show)
    {
        // Clone before replacing media paths so the open show and library keep their local paths.
        var copy = ShowFiles.Parse(JsonSerializer.Serialize(show, ShowFiles.Json));
        var media = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var reference in MediaPaths.All(copy.Presentations))
        {
            if (string.IsNullOrWhiteSpace(reference.Path))
            {
                if (reference.IsPositionedImage)
                    throw new FileNotFoundException("A positioned image is missing. Use Relink Media before packing this show.");
                continue;
            }
            var source = Path.GetFullPath(reference.Path);
            if (!File.Exists(source))
                throw new FileNotFoundException("Media is missing. Use Relink Media before packing this show.", source);
            if (!media.TryGetValue(source, out var entryPath))
            {
                var extension = SafeExtension(source);
                entryPath = $"media/{HashFile(source)}.{extension}";
                media[source] = entryPath;
                entries.TryAdd(entryPath, source);
            }
            reference.Path = entryPath;
        }

        var showJson = JsonSerializer.Serialize(copy, ShowFiles.Json);
        if (Encoding.UTF8.GetByteCount(showJson) > MaximumShowBytes)
            throw new InvalidDataException("The show is too large to pack.");
        long totalBytes = 0;
        foreach (var source in entries.Values)
        {
            var length = new FileInfo(source).Length;
            if (length > MaximumMediaBytes - totalBytes)
                throw new InvalidDataException("The show contains too much media to pack.");
            totalBytes += length;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                WriteText(archive, "format.txt", Format);
                WriteText(archive, "show.json", showJson);
                foreach (var (entryPath, source) in entries)
                {
                    var entry = archive.CreateEntry(entryPath, CompressionLevel.NoCompression);
                    using var input = File.OpenRead(source);
                    using var output = entry.Open();
                    if (CopyAndHash(input, output, MaximumMediaBytes) != entryPath[6..70])
                        throw new IOException("A media file changed while the show was being packed. Try again.");
                }
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static Show Open(string path, string cacheRoot)
    {
        var packageHash = HashFile(path);
        using var archive = ZipFile.OpenRead(path);
        var format = OnlyEntry(archive, "format.txt", 128);
        using (var reader = new StreamReader(format.Open(), Encoding.UTF8))
            if (reader.ReadToEnd() != Format)
                throw new InvalidDataException("Unsupported portable show format.");
        var showEntry = OnlyEntry(archive, "show.json", MaximumShowBytes);
        Show show;
        using (var reader = new StreamReader(showEntry.Open(), Encoding.UTF8))
            show = ShowFiles.Parse(reader.ReadToEnd());

        var references = MediaPaths.All(show.Presentations).ToArray();
        if (references.Any(reference => reference.IsPositionedImage && string.IsNullOrWhiteSpace(reference.Path)))
            throw new InvalidDataException("The package contains a positioned image without media.");
        var paths = references
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Path))
            .Select(reference => reference.Path!).Distinct(StringComparer.Ordinal).ToArray();
        long totalBytes = 0;
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entryPath in paths)
        {
            if (!MediaEntryPath().IsMatch(entryPath))
                throw new InvalidDataException("The package contains an invalid media reference.");
            var entry = OnlyEntry(archive, entryPath, MaximumMediaBytes);
            if (entry.Length > MaximumMediaBytes - totalBytes)
                throw new InvalidDataException("The package contains too much media.");
            totalBytes += entry.Length;
            var destination = Path.Combine(cacheRoot, packageHash, entryPath.Replace('/', Path.DirectorySeparatorChar));
            var expectedHash = entryPath[6..70];
            if (!File.Exists(destination) || HashFile(destination) != expectedHash)
                ExtractVerified(entry, destination, expectedHash);
            resolved[entryPath] = destination;
        }
        foreach (var reference in references)
            if (reference.Path is { Length: > 0 } entryPath)
                reference.Path = resolved[entryPath];
        return show;
    }

    private static ZipArchiveEntry OnlyEntry(ZipArchive archive, string name, long maximumBytes)
    {
        var entries = archive.Entries.Where(entry => entry.FullName == name).Take(2).ToArray();
        if (entries.Length != 1 || entries[0].Length > maximumBytes)
            throw new InvalidDataException($"The package is missing a valid {name} entry.");
        return entries[0];
    }

    private static void ExtractVerified(ZipArchiveEntry entry, string destination, string expectedHash)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var input = entry.Open())
            using (var output = File.Create(temporary))
                if (CopyAndHash(input, output, entry.Length) != expectedHash)
                    throw new InvalidDataException("A packaged media file failed its integrity check.");
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void WriteText(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string CopyAndHash(Stream input, Stream output, long maximumBytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long total = 0;
        int count;
        while ((count = input.Read(buffer)) > 0)
        {
            if (count > maximumBytes - total)
                throw new InvalidDataException("A packaged media file exceeds its declared size.");
            hash.AppendData(buffer, 0, count);
            output.Write(buffer, 0, count);
            total += count;
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string SafeExtension(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return extension is { Length: > 0 and <= 12 } && extension.All(char.IsAsciiLetterOrDigit)
            ? extension : "bin";
    }
}
