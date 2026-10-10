using System.Text.RegularExpressions;

namespace PeckSlides.Core;

public static partial class SongParser
{
    [GeneratedRegex(@"^\s*\[([^\]]+)\]\s*$", RegexOptions.Compiled)]
    private static partial Regex SectionHeader();

    public static Presentation Parse(string title, string lyrics)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Give the song a title.", nameof(title));

        var presentation = new Presentation { Name = title.Trim(), Kind = "song" };
        var lines = lyrics.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var stanza = new List<string>();
        var section = "Verse";
        var count = 0;

        void Flush()
        {
            if (stanza.Count == 0) return;
            count++;
            presentation.Slides.Add(new Slide
            {
                Name = $"{section} {count}",
                Text = string.Join("\n", stanza)
            });
            stanza.Clear();
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            var header = SectionHeader().Match(line);
            if (header.Success)
            {
                Flush();
                section = header.Groups[1].Value.Trim();
                count = 0;
            }
            else if (line.Length == 0) Flush();
            else stanza.Add(line);
        }
        Flush();
        if (presentation.Slides.Count == 0)
            throw new ArgumentException("Add at least one line of lyrics.", nameof(lyrics));
        return presentation;
    }
}
