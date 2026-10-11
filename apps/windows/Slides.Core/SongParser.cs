using System.Text.RegularExpressions;

namespace Slides.Core;

public static partial class SongParser
{
    [GeneratedRegex(@"^\s*\[([^\]]+)\]\s*$", RegexOptions.Compiled)]
    private static partial Regex SectionHeader();

    [GeneratedRegex(@"^(?:Verse|Chorus|Pre[- ]?Chorus|Bridge|Intro|Outro|Tag|Refrain|Ending|Instrumental|Interlude|Vamp|Turnaround)(?:\s+[1-9]\d*)?:?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BareSectionHeader();

    [GeneratedRegex(@"^Verse [1-9]\d*$", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyVerseName();

    private static string? HeaderName(string line)
    {
        var bracketed = SectionHeader().Match(line);
        if (bracketed.Success) return bracketed.Groups[1].Value.Trim();
        return BareSectionHeader().IsMatch(line) ? line.TrimEnd(':') : null;
    }

    public static Presentation Parse(string title, string lyrics)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Give the song a title.", nameof(title));

        var presentation = new Presentation { Name = title.Trim(), Kind = "song" };
        var lines = lyrics.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var stanza = new List<string>();
        var section = "Verse";
        var sectionSlide = 0;
        var unlabelledVerse = 0;
        var hasHeading = false;
        var sectionId = Guid.NewGuid().ToString("N");

        void Flush()
        {
            if (stanza.Count == 0) return;
            sectionSlide++;
            presentation.Slides.Add(new Slide
            {
                Name = hasHeading
                    ? sectionSlide == 1 ? section : $"{section} ({sectionSlide})"
                    : $"Verse {++unlabelledVerse}",
                Text = string.Join("\n", stanza),
                SectionId = hasHeading ? sectionId : Guid.NewGuid().ToString("N")
            });
            stanza.Clear();
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            var header = HeaderName(line);
            if (header is not null)
            {
                Flush();
                section = header;
                sectionSlide = 0;
                hasHeading = true;
                sectionId = Guid.NewGuid().ToString("N");
            }
            else if (line.Length == 0) Flush();
            else stanza.Add(line);
        }
        Flush();
        if (presentation.Slides.Count == 0)
            throw new ArgumentException("Add at least one line of lyrics.", nameof(lyrics));
        return presentation;
    }

    public static string ToEditableLyrics(Presentation song)
    {
        var parts = new List<string>();
        string? previousSectionId = null;
        foreach (var slide in song.Slides)
        {
            var sameSection = slide.SectionId is { Length: > 0 } && slide.SectionId == previousSectionId;
            parts.Add(sameSection ? slide.Text : $"[{slide.Name}]\n{slide.Text}");
            previousSectionId = slide.SectionId;
        }
        return string.Join("\n\n", parts);
    }

    // Early Windows builds treated bare headings as lyrics and numbered every slide Verse N.
    // Repair only that exact legacy shape when an existing show is opened.
    public static void RepairLegacyHeadings(Presentation song)
    {
        if (song.Kind != "song") return;
        foreach (var slide in song.Slides)
        {
            if (!LegacyVerseName().IsMatch(slide.Name)) continue;
            var newline = slide.Text.IndexOf('\n');
            if (newline < 0) continue;
            var heading = HeaderName(slide.Text[..newline].Trim());
            var lyrics = slide.Text[(newline + 1)..].Trim();
            if (heading is null || lyrics.Length == 0) continue;
            slide.Name = heading;
            slide.Text = lyrics;
        }
    }
}
