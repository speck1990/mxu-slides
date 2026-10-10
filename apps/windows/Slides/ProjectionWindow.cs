using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Slides;

public sealed class ProjectionWindow : Window
{
    private readonly SlideSurface _surface = new() { Width = 960, Height = 540 };

    public ProjectionWindow(Forms.Screen display)
    {
        Title = "Slides Audience";
        Content = new Viewbox { Child = _surface, Stretch = Stretch.Uniform };
        Background = System.Windows.Media.Brushes.Black;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        Topmost = true;
        Left = display.Bounds.Left;
        Top = display.Bounds.Top;
        Width = display.Bounds.Width;
        Height = display.Bounds.Height;
        KeyDown += (_, args) => { if (args.Key == Key.Escape) Close(); };
    }

    public void ShowSlide(Slides.Core.Slide? slide, bool black) => _surface.Show(slide, black);
}
