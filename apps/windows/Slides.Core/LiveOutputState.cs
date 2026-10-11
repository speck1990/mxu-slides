namespace Slides.Core;

// Operator output controls are live state and are never written to show files.
public sealed class LiveOutputState
{
    public bool IsBlack { get; private set; }
    public bool SlidesCleared { get; private set; }
    public string? ActiveOverlayId { get; private set; }

    public void FireSlide() => SlidesCleared = false;
    public void FireOverlay(string overlayId) => ActiveOverlayId = overlayId;
    public void ClearSlides() => SlidesCleared = true;
    public void ClearOverlay() => ActiveOverlayId = null;
    public void ToggleBlack() => IsBlack = !IsBlack;

    public void ClearAll()
    {
        SlidesCleared = true;
        ActiveOverlayId = null;
        IsBlack = false;
    }

    public void Reset()
    {
        SlidesCleared = false;
        ActiveOverlayId = null;
        IsBlack = false;
    }
}
