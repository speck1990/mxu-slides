using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Slides.Core;

namespace Slides;

public sealed class SlideSurface : Grid
{
    public static readonly DependencyProperty CurrentSlideProperty = DependencyProperty.Register(
        nameof(CurrentSlide), typeof(Slide), typeof(SlideSurface),
        new PropertyMetadata(null, (target, _) => ((SlideSurface)target).Refresh()));

    public static readonly DependencyProperty IsThumbnailProperty = DependencyProperty.Register(
        nameof(IsThumbnail), typeof(bool), typeof(SlideSurface),
        new PropertyMetadata(false, (target, _) => ((SlideSurface)target).Refresh()));

    public Slide? CurrentSlide
    {
        get => (Slide?)GetValue(CurrentSlideProperty);
        set => SetValue(CurrentSlideProperty, value);
    }

    public bool IsThumbnail
    {
        get => (bool)GetValue(IsThumbnailProperty);
        set => SetValue(IsThumbnailProperty, value);
    }

    private MediaElement? _video;
    private bool _black;
    private bool _surfaceReady;
    private bool _renderedAsThumbnail;
    private Slide? _displayedSlide;
    private bool _videoPlaying;
    private readonly Border _blackCover = new() { Background = Brushes.Black, Visibility = Visibility.Collapsed };
    public event EventHandler? VideoStateChanged;

    public bool HasVideo => _video is not null;
    public bool VideoPlaying => _video is not null && _videoPlaying;
    public TimeSpan VideoPosition => _video?.Position ?? TimeSpan.Zero;
    public TimeSpan VideoDuration => _video?.NaturalDuration.HasTimeSpan == true
        ? _video.NaturalDuration.TimeSpan : TimeSpan.Zero;

    public void PlayVideo()
    {
        if (_video is null) return;
        _video.Play();
        _videoPlaying = true;
        VideoStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PauseVideo()
    {
        if (_video is null) return;
        _video.Pause();
        _videoPlaying = false;
        VideoStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SeekVideo(TimeSpan position)
    {
        if (_video is null) return;
        _video.Position = position < TimeSpan.Zero ? TimeSpan.Zero :
            VideoDuration > TimeSpan.Zero && position > VideoDuration ? VideoDuration : position;
        VideoStateChanged?.Invoke(this, EventArgs.Empty);
    }
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

    private void Refresh() => Show(CurrentSlide, _black);

    public void Show(Slide? slide, bool black)
    {
        _black = black;
        if (!_surfaceReady || !ReferenceEquals(_displayedSlide, slide) || _renderedAsThumbnail != IsThumbnail)
        {
            _displayedSlide = slide;
            _renderedAsThumbnail = IsThumbnail;
            Rebuild(slide);
            _surfaceReady = true;
        }
        _blackCover.Visibility = black ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Rebuild(Slide? slide)
    {
        _video?.Stop();
        _video = null;
        _videoPlaying = false;
        Children.Clear();
        Background = Brush(slide?.Background ?? "#0B1220");
        Children.Add(_blackCover);
        if (slide?.MediaPath is { Length: > 0 } path && File.Exists(path))
        {
            var uri = new Uri(Path.GetFullPath(path));
            if (new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" }
                .Contains(Path.GetExtension(path).ToLowerInvariant()))
            {
                Children.Insert(Children.Count - 1, new Image
                {
                    Source = new BitmapImage(uri),
                    Stretch = Stretch.UniformToFill
                });
            }
            else
            {
                if (IsThumbnail)
                {
                    Children.Insert(Children.Count - 1, new TextBlock
                    {
                        Text = "▶",
                        FontSize = 110,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
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
                    var video = _video;
                    video.MediaEnded += (_, _) => { video.Position = TimeSpan.Zero; video.Play(); };
                    _video.MediaOpened += (_, _) => VideoStateChanged?.Invoke(this, EventArgs.Empty);
                    Children.Insert(Children.Count - 1, _video);
                    _video.Play();
                    _videoPlaying = true;
                }
            }
        }
        if (slide?.Elements is { Count: > 0 } elements)
        {
            var canvas = new Canvas { Width = SlideLayout.Width, Height = SlideLayout.Height, ClipToBounds = true };
            foreach (var element in elements)
            {
                FrameworkElement visual;
                if (element.Kind == "shape")
                {
                    visual = element.ShapeKind == "ellipse"
                        ? new System.Windows.Shapes.Ellipse { Width = element.Width, Height = element.Height, Fill = Brush(element.FillColorHex) }
                        : new Border
                        {
                            Width = element.Width, Height = element.Height,
                            Background = Brush(element.FillColorHex),
                            CornerRadius = new CornerRadius(Math.Max(0, element.CornerRadius))
                        };
                }
                else if (element.Kind == "text")
                {
                    var holder = new Grid { Width = element.Width, Height = element.Height, ClipToBounds = true };
                    var block = new TextBlock
                    {
                        Text = element.Text,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    ApplyTextStyle(block, element.TextStyle ?? SlideThemes.Effective(slide));
                    holder.Children.Add(block);
                    visual = holder;
                }
                else if (element.Kind == "image" && element.MediaPath is { Length: > 0 } imagePath && File.Exists(imagePath))
                {
                    var holder = new Grid { Width = element.Width, Height = element.Height, ClipToBounds = true };
                    holder.Children.Add(new Image
                    {
                        Source = new BitmapImage(new Uri(Path.GetFullPath(imagePath))),
                        Stretch = element.MediaScaleMode switch
                        {
                            "fill" => Stretch.UniformToFill,
                            "stretch" => Stretch.Fill,
                            _ => Stretch.Uniform
                        }
                    });
                    visual = holder;
                }
                else continue;
                visual.Opacity = element.Opacity;
                visual.RenderTransformOrigin = new Point(0.5, 0.5);
                var transform = new TransformGroup();
                if (element.FlipHorizontal || element.FlipVertical)
                    transform.Children.Add(new ScaleTransform(element.FlipHorizontal ? -1 : 1, element.FlipVertical ? -1 : 1));
                if (element.RotationDegrees != 0)
                    transform.Children.Add(new RotateTransform(element.RotationDegrees));
                visual.RenderTransform = transform;
                Canvas.SetLeft(visual, element.X);
                Canvas.SetTop(visual, element.Y);
                canvas.Children.Add(visual);
            }
            Children.Insert(Children.Count - 1, canvas);
            VideoStateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        _text.Text = slide?.Text ?? "Select a slide";
        var style = slide is null ? new SlideTextStyle { FontSize = 36 } : SlideThemes.Effective(slide);
        ApplyTextStyle(_text, style);
        Children.Insert(Children.Count - 1, _text);
        VideoStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void ApplyTextStyle(TextBlock block, SlideTextStyle style)
    {
        block.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(style.FontName) ? "Segoe UI" : style.FontName);
        block.FontSize = double.IsFinite(style.FontSize) && style.FontSize > 0 ? style.FontSize : 54;
        block.FontWeight = style.Weight switch
        {
            "normal" => FontWeights.Normal,
            "bold" => FontWeights.Bold,
            _ => FontWeights.SemiBold
        };
        block.Foreground = Brush(style.ColorHex);
        block.TextAlignment = style.HorizontalAlignment switch
        {
            "left" => TextAlignment.Left,
            "right" => TextAlignment.Right,
            _ => TextAlignment.Center
        };
    }

    private static Brush Brush(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return Brushes.Black;
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
