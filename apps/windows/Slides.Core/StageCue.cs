namespace Slides.Core;

public sealed record StageCue(string CurrentHeading, string CurrentText, string NextHeading,
    string NextText, string OperatorNotes, int? PlannedMinutes)
{
    public static readonly StageCue Empty = new("No slide selected", "", "Up next", "", "", null);

    public static StageCue For(Show show, int serviceIndex, int slideIndex)
    {
        show.EnsureServiceItems();
        if (serviceIndex < 0 || serviceIndex >= show.ServiceItems!.Count) return Empty;
        var byId = show.Presentations.GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First());
        if (!byId.TryGetValue(show.ServiceItems[serviceIndex], out var presentation)) return Empty;
        var details = show.ServiceDetails![serviceIndex];
        var slides = PresentationArrangements.Slides(presentation, details.ArrangementId);
        var current = slideIndex >= 0 && slideIndex < slides.Count
            ? slides[slideIndex] : null;
        Slide? next = null;
        Presentation? nextPresentation = null;
        if (slideIndex + 1 < slides.Count)
        {
            next = slides[slideIndex + 1];
            nextPresentation = presentation;
        }
        else
        {
            for (var index = serviceIndex + 1; index < show.ServiceItems.Count; index++)
            {
                if (!byId.TryGetValue(show.ServiceItems[index], out var candidate))
                    continue;
                var candidateSlides = PresentationArrangements.Slides(candidate, show.ServiceDetails[index].ArrangementId);
                if (candidateSlides.Count == 0) continue;
                next = candidateSlides[0];
                nextPresentation = candidate;
                break;
            }
        }
        return new StageCue(
            current is null ? presentation.Name : $"{presentation.Name} · {current.Name}",
            current is null ? "No slide" : VisibleText(current),
            next is null ? "End of service" : $"{nextPresentation!.Name} · {next.Name}",
            next is null ? "" : VisibleText(next),
            details.Notes, details.PlannedMinutes);
    }

    private static string VisibleText(Slide slide) => !string.IsNullOrWhiteSpace(slide.Text)
        ? slide.Text : slide.MediaPath is { Length: > 0 } ? "[Media]" : slide.Name;
}
