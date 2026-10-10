using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PeckSlides.Core;

namespace PeckSlides;

public sealed class SlideSurface : Grid
{
    private MediaElement? _video;
    private readonly TextBlock _text = new()
    {
        Foreground = Brushes.White,
        FontFamily = new FontFamily("Segoe UI"),
        FontWeight = FontWeights.SemiBold,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Margin = new Thickness(70, 30, 70, 30)
    };

    public SlideSurface() => Show(null, false);

    public void Show(Slide? slide, bool black)
    {
        _video?.Stop();
        _video = null;
        Children.Clear();
        Background = Brush(slide?.Background ?? "#0B1220");
        if (black)
        {
            Background = Brushes.Black;
            return;
        }
        if (slide?.MediaPath is { Length: > 0 } path && File.Exists(path))
        {
            var uri = new Uri(Path.GetFullPath(path));
            if (new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" }
                .Contains(Path.GetExtension(path).ToLowerInvariant()))
            {
                Children.Add(new Image
                {
                    Source = new BitmapImage(uri),
                    Stretch = Stretch.UniformToFill
                });
            }
            else
            {
                _video = new MediaElement
                {
                    Source = uri,
                    LoadedBehavior = MediaState.Manual,
                    UnloadedBehavior = MediaState.Stop,
                    Stretch = Stretch.UniformToFill
                };
                _video.MediaEnded += (_, _) => { _video.Position = TimeSpan.Zero; _video.Play(); };
                Children.Add(_video);
                _video.Play();
            }
        }
        _text.Text = slide?.Text ?? "Select a slide";
        _text.FontSize = slide is null ? 36 : 54;
        Children.Add(_text);
    }

    private static Brush Brush(string color)
    {
        try
        {
            if (color.Length == 9 && color[0] == '#')
            {
                // MxU's schema stores colors as #RRGGBBAA; WPF expects #AARRGGBB.
                color = "#" + color[7..9] + color[1..7];
            }
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }
        catch (FormatException) { return Brushes.Black; }
    }
}
