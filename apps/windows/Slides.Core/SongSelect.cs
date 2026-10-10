using System.Text;
using System.Text.Json;

namespace Slides.Core;

public sealed record SongSelectResult(string Id, string Title, string SongNumber, string Authors, bool CanImport)
{
    public override string ToString() => $"{Title}  ·  CCLI {SongNumber}  ·  {Authors}";
}

public static class SongSelect
{
    public const string ApiBase = "https://api.ccli.com/ss/v2";

    public static IReadOnlyList<SongSelectResult> ReadResults(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("SongSelect returned an unexpected search response.");
        return results.EnumerateArray().Select(item => new SongSelectResult(
            Text(item, "id"), Text(item, "title"), Text(item, "songNumber"),
            Authors(item), item.TryGetProperty("content", out var content)
                && content.TryGetProperty("lyrics", out var lyrics)
                && lyrics.TryGetProperty("exists", out var exists) && exists.ValueKind == JsonValueKind.True
                && lyrics.TryGetProperty("isAuthorized", out var authorized) && authorized.ValueKind == JsonValueKind.True
        )).Where(item => item.Id.Length > 0 && item.Title.Length > 0).ToArray();
    }

    public static Presentation ReadLyrics(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var data))
            throw new InvalidDataException("SongSelect returned no song data.");
        var title = Text(data, "title");
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidDataException("SongSelect returned no title.");
        if (!data.TryGetProperty("lyricParts", out var parts) || parts.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("SongSelect returned no lyric parts.");
        var body = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            var lyrics = Text(part, "lyrics").Trim();
            if (lyrics.Length == 0) continue;
            if (body.Length > 0) body.Append("\n\n");
            var label = Text(part, "partLabel");
            if (label.Length > 0) body.Append('[').Append(label).Append("]\n");
            body.Append(lyrics);
        }
        if (body.Length == 0) throw new InvalidDataException("This song has no importable lyrics.");
        var presentation = SongParser.Parse(title, body.ToString());
        if (int.TryParse(Text(data, "songNumber"), out var number)) presentation.CcliSongNumber = number;
        if (data.TryGetProperty("copyrights", out var copyrights) && copyrights.ValueKind == JsonValueKind.Array)
            presentation.Copyright = string.Join("\n", copyrights.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)));
        return presentation;
    }

    private static string Text(JsonElement value, string key)
    {
        if (!value.TryGetProperty(key, out var property)) return "";
        return property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : property.ToString();
    }

    private static string Authors(JsonElement item)
    {
        if (!item.TryGetProperty("authors", out var authors) || authors.ValueKind != JsonValueKind.Array) return "";
        return string.Join(", ", authors.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String
            ? x.GetString() : Text(x, "label")).Where(x => !string.IsNullOrWhiteSpace(x)));
    }
}
