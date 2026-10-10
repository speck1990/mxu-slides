using System.Text.Json;
using System.Text.Json.Serialization;

namespace Slides.Core;

public sealed class Show
{
    public int Version { get; set; } = 1;
    public List<Presentation> Presentations { get; set; } = [];
    // Null identifies shows saved before the library and service were separated.
    public List<string>? ServiceItems { get; set; }

    public void EnsureServiceItems() =>
        ServiceItems ??= Presentations.Select(presentation => presentation.Id).ToList();

    public void AddToService(string presentationId)
    {
        EnsureServiceItems();
        if (!Presentations.Any(presentation => presentation.Id == presentationId))
            throw new ArgumentException("Choose a presentation from the library.", nameof(presentationId));
        ServiceItems!.Add(presentationId);
    }

    public void RemoveFromService(int index)
    {
        EnsureServiceItems();
        ServiceItems!.RemoveAt(index);
    }

    public void MoveServiceItem(int from, int to)
    {
        EnsureServiceItems();
        var items = ServiceItems!;
        if (from < 0 || from >= items.Count || to < 0 || to >= items.Count) return;
        var id = items[from];
        items.RemoveAt(from);
        items.Insert(to, id);
    }
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
    // Null keeps the appearance of shows created before typography was editable.
    public SlideTextStyle? TextStyle { get; set; }
    // Optional positioned objects on the Windows 960 x 540 design canvas.
    public List<SlideElement>? Elements { get; set; }
}

public sealed class SlideElement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "text";
    public string Name { get; set; } = "Text";
    public string Text { get; set; } = "";
    public double X { get; set; } = 70;
    public double Y { get; set; } = 30;
    public double Width { get; set; } = 820;
    public double Height { get; set; } = 480;
    public string FillColorHex { get; set; } = "#3B82F6FF";
    public string ShapeKind { get; set; } = "rectangle";
    public double CornerRadius { get; set; }
    public SlideTextStyle? TextStyle { get; set; }
}

public sealed class SlideTextStyle
{
    public string FontName { get; set; } = "Segoe UI";
    // Font sizes are measured on the Windows 960 x 540 design canvas.
    public double FontSize { get; set; } = 54;
    public string ColorHex { get; set; } = "#FFFFFFFF";
    public string HorizontalAlignment { get; set; } = "center";
    public string Weight { get; set; } = "semibold";
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

    public static Show Load(string path) => Parse(File.ReadAllText(path));

    internal static Show Parse(string json)
    {
        var show = JsonSerializer.Deserialize<Show>(json, Json)
            ?? throw new InvalidDataException("The show file is empty.");
        if (show.Version != 1 || show.Presentations is null)
            throw new InvalidDataException("Unsupported show file version.");
        foreach (var presentation in show.Presentations)
            SongParser.RepairLegacyHeadings(presentation);
        show.EnsureServiceItems();
        return show;
    }

    public static void Save(string path, Show show)
    {
        show.EnsureServiceItems();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(show, Json));
        File.Move(temporary, path, true);
    }
}
