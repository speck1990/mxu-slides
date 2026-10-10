using System.Text.Json;
using System.Text.Json.Serialization;

namespace Slides.Core;

public sealed class Show
{
    public int Version { get; set; } = 1;
    public List<Presentation> Presentations { get; set; } = [];
}

public sealed class Presentation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled";
    public string Kind { get; set; } = "song";
    public List<Slide> Slides { get; set; } = [];
}

public sealed class Slide
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Slide";
    public string Text { get; set; } = "";
    public string? MediaPath { get; set; }
    public string Background { get; set; } = "#0B1220";
}

public static class ShowFiles
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static Show Load(string path)
    {
        var show = JsonSerializer.Deserialize<Show>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("The show file is empty.");
        if (show.Version != 1 || show.Presentations is null)
            throw new InvalidDataException("Unsupported show file version.");
        return show;
    }

    public static void Save(string path, Show show)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(show, Json));
        File.Move(temporary, path, true);
    }
}
