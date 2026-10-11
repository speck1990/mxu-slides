using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Slides.Core;

namespace Slides;

public sealed class OverlayDialog : Window
{
    private readonly Overlay _source;
    private readonly TextBox _name = new();
    private readonly TextBox _text = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        Height = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ComboBox _layout = new() { ItemsSource = new[] { "Lower Third", "Full Screen" } };
    private readonly ComboBox _theme = new() { ItemsSource = new[] { "Dark", "Blue", "Gold", "Light" } };

    public Overlay? EditedOverlay { get; private set; }

    public OverlayDialog(Overlay? source = null)
    {
        _source = source is null ? new Overlay() : Overlay.Copy(source);
        Title = source is null ? "New overlay" : "Edit overlay";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(28, 28, 30));
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");
        _name.Text = _source.Name;
        _text.Text = _source.Text;
        _layout.SelectedIndex = _source.Layout == "fullScreen" ? 1 : 0;
        _theme.SelectedIndex = _source.BackgroundColor switch
        {
            "#0B3169E6" => 1, "#4A3411E6" => 2, "#F7F7F2F2" => 3, _ => 0
        };
        var content = new StackPanel { Margin = new Thickness(18) };
        AddField(content, "Name", _name);
        AddField(content, "Text", _text);
        AddField(content, "Placement", _layout);
        AddField(content, "Look", _theme);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => Close();
        var save = new Button { Content = "Save", Padding = new Thickness(12, 6, 12, 6) };
        save.Click += (_, _) => Save();
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        content.Children.Add(buttons);
        Content = content;
    }

    private static void AddField(StackPanel parent, string label, Control field)
    {
        parent.Children.Add(new TextBlock { Text = label, Foreground = Brushes.White,
            Margin = new Thickness(0, 10, 0, 5) });
        field.Background = new SolidColorBrush(Color.FromRgb(51, 51, 55));
        field.Foreground = Brushes.White;
        parent.Children.Add(field);
    }

    private void Save()
    {
        var name = _name.Text.Trim();
        var text = _text.Text.Trim();
        if (name.Length is < 1 or > 120 || text.Length is < 1 or > 2000)
        {
            MessageBox.Show(this, "Enter a name up to 120 characters and text up to 2,000 characters.",
                "Slides", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var (background, foreground) = _theme.SelectedIndex switch
        {
            1 => ("#0B3169E6", "#FFFFFFFF"),
            2 => ("#4A3411E6", "#FFFFFFFF"),
            3 => ("#F7F7F2F2", "#171717FF"),
            _ => ("#101828DD", "#FFFFFFFF")
        };
        _source.Name = name;
        _source.Text = text;
        _source.Layout = _layout.SelectedIndex == 1 ? "fullScreen" : "lowerThird";
        _source.BackgroundColor = background;
        _source.TextColor = foreground;
        EditedOverlay = _source;
        DialogResult = true;
    }
}
