namespace Slides.Core;

public sealed record SongBlock(string Id, string Name, IReadOnlyList<Slide> Slides);

public static class PresentationArrangements
{
    public static IReadOnlyList<SongBlock> Blocks(Presentation presentation)
    {
        var order = new List<string>();
        var groups = new Dictionary<string, List<Slide>>();
        foreach (var slide in presentation.Slides)
        {
            var id = string.IsNullOrWhiteSpace(slide.SectionId) ? slide.Id : slide.SectionId;
            if (!groups.TryGetValue(id, out var group))
            {
                group = [];
                groups.Add(id, group);
                order.Add(id);
            }
            group.Add(slide);
        }
        return order.Select(id => new SongBlock(id, groups[id][0].Name, groups[id])).ToArray();
    }

    public static IReadOnlyList<Slide> Slides(Presentation presentation, string? arrangementId)
    {
        if (string.IsNullOrWhiteSpace(arrangementId)) return presentation.Slides;
        var arrangement = presentation.Arrangements?.FirstOrDefault(item => item.Id == arrangementId);
        if (arrangement?.BlockIds is not { Count: > 0 }) return presentation.Slides;
        var blocks = Blocks(presentation).ToDictionary(block => block.Id);
        var arranged = arrangement.BlockIds.Where(blocks.ContainsKey)
            .SelectMany(id => blocks[id].Slides).ToArray();
        return arranged.Length > 0 ? arranged : presentation.Slides;
    }

    public static SlideArrangement Add(Presentation presentation, string name)
    {
        if (presentation.Kind != "song")
            throw new ArgumentException("Arrangements are available for songs.");
        if (presentation.Slides.Count == 0)
            throw new ArgumentException("Add a song section before creating an arrangement.");
        var arrangement = new SlideArrangement
        {
            Name = ValidName(name),
            BlockIds = Blocks(presentation).Select(block => block.Id).ToList()
        };
        presentation.Arrangements.Add(arrangement);
        return arrangement;
    }

    public static void PruneMissingBlocks(Presentation presentation)
    {
        var valid = Blocks(presentation).Select(block => block.Id).ToArray();
        foreach (var arrangement in presentation.Arrangements)
        {
            arrangement.BlockIds.RemoveAll(id => !valid.Contains(id));
            if (arrangement.BlockIds.Count == 0) arrangement.BlockIds.AddRange(valid);
        }
    }

    public static void Validate(Presentation presentation)
    {
        if (presentation.Arrangements is null)
            throw new InvalidDataException("The arrangement list is missing.");
        var validBlocks = Blocks(presentation).Select(block => block.Id).ToHashSet();
        if (presentation.Arrangements.Any(item => item is null) ||
            presentation.Arrangements.Select(item => item.Id).Distinct().Count() != presentation.Arrangements.Count)
            throw new InvalidDataException("Arrangement IDs must be unique.");
        foreach (var arrangement in presentation.Arrangements)
        {
            if (string.IsNullOrWhiteSpace(arrangement.Id) || arrangement.BlockIds is null ||
                arrangement.BlockIds.Count == 0 || arrangement.BlockIds.Any(id => !validBlocks.Contains(id)))
                throw new InvalidDataException("An arrangement references a missing song section.");
            ValidName(arrangement.Name);
        }
    }

    private static string ValidName(string name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length is < 1 or > 80)
            throw new ArgumentException("Use an arrangement name from 1 to 80 characters.");
        return trimmed;
    }
}
