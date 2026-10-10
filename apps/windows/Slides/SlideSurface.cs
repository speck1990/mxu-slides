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
                if (IsThumbnail)
                {
                    Children.Add(new TextBlock
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
                    _video.MediaEnded += (_, _) => { _video.Position = TimeSpan.Zero; _video.Play(); };
                    Children.Add(_video);
                    _video.Play();
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
                    visual = new Border
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
                else continue;
                Canvas.SetLeft(visual, element.X);
                Canvas.SetTop(visual, element.Y);
                canvas.Children.Add(visual);
            }
            Children.Add(canvas);
            return;
        }
        _text.Text = slide?.Text ?? "Select a slide";
        var style = slide is null ? new SlideTextStyle { FontSize = 36 } : SlideThemes.Effective(slide);
        ApplyTextStyle(_text, style);
        Children.Add(_text);
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
