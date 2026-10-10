using System.Text.RegularExpressions;

namespace Slides.Core;

/// <summary>Imports plain-text lyrics downloaded by the user from SongSelect.</summary>
public static partial class SongSelectText
{
    [GeneratedRegex(@"^\s*CCLI Song #\s*(\d+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SongNumber();

    [GeneratedRegex(@"^(Verse|Chorus|Pre-Chorus|Bridge|Intro|Outro|Tag|Refrain|Ending|Instrumental)(\s+\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex SectionLabel();

    public static Presentation Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var footerIndex = Array.FindIndex(lines, line => SongNumber().IsMatch(line));
        if (footerIndex < 0)
            throw new InvalidDataException("This is not a SongSelect text export: CCLI Song # is missing.");

        var numberMatch = SongNumber().Match(lines[footerIndex]);
        if (!int.TryParse(numberMatch.Groups[1].Value, out var number))
            throw new InvalidDataException("The CCLI song number is invalid.");

        var titleIndex = Array.FindIndex(lines, 0, footerIndex, line => !string.IsNullOrWhiteSpace(line));
        if (titleIndex < 0) throw new InvalidDataException("The SongSelect export has no title.");
        var title = lines[titleIndex].Trim();
        var lyrics = string.Join("\n", lines[(titleIndex + 1)..footerIndex]
            .Select(line => SectionLabel().IsMatch(line.Trim()) ? $"[{line.Trim()}]" : line)).Trim();
        if (lyrics.Length == 0) throw new InvalidDataException("The SongSelect export has no lyrics.");

        var authors = lines.Skip(footerIndex + 1).Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0 && !line.StartsWith('©')
                && !line.StartsWith("CCLI", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("SongSelect", StringComparison.OrdinalIgnoreCase));
        var copyright = string.Join("\n", lines.Skip(footerIndex + 1)
            .Select(line => line.Trim()).Where(line => line.StartsWith('©')));

        var song = SongParser.Parse(title, lyrics);
        song.CcliSongNumber = number;
        song.CcliAuthors = authors;
        song.Copyright = copyright.Length == 0 ? null : copyright;
        return song;
    }
}
