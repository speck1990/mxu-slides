namespace Slides.Core;

// Keeps packaging and relinking in sync for slide backgrounds and positioned images.
internal static class MediaPaths
{
    internal sealed class Reference
    {
        private readonly Slide? _slide;
        private readonly SlideElement? _element;

        public Reference(Slide slide) => _slide = slide;
        public Reference(SlideElement element) => _element = element;
        public bool IsPositionedImage => _element is not null;

        public string? Path
        {
            get => _slide is not null ? _slide.MediaPath : _element!.MediaPath;
            set
            {
                if (_slide is not null) _slide.MediaPath = value;
                else _element!.MediaPath = value;
            }
        }
    }

    internal static IEnumerable<Reference> All(IEnumerable<Presentation> presentations) =>
        presentations.SelectMany(presentation => presentation.Slides).SelectMany(ForSlide);

    internal static IEnumerable<Reference> ForSlide(Slide slide)
    {
        yield return new Reference(slide);
        if (slide.Elements is null) yield break;
        foreach (var element in slide.Elements.Where(item => item.Kind == "image"))
            yield return new Reference(element);
    }
}
