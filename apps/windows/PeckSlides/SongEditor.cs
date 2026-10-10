using System.Windows;
using System.Windows.Controls;

namespace PeckSlides;

public sealed class SongEditor : Window
{
    private readonly TextBox _title = new() { Margin = new Thickness(0, 4, 0, 12), FontSize = 17 };
    private readonly TextBox _lyrics = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontSize = 16
    };

    public string SongTitle => _title.Text;
    public string Lyrics => _lyrics.Text;

    public SongEditor(string title = "", string lyrics = "")
    {
        _title.Text = title;
        _lyrics.Text = lyrics;
        Title = "Add song";
        Width = 610;
        Height = 570;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new DockPanel { Margin = new Thickness(18) };
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 8, 8, 0) };
        cancel.Click += (_, _) => Close();
        var save = new Button { Content = "Add song", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 8, 0, 0) };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(SongTitle) || string.IsNullOrWhiteSpace(Lyrics))
            {
                MessageBox.Show(this, "Enter a title and lyrics before saving.");
                return;
            }
            DialogResult = true;
        };
        bottom.Children.Add(cancel);
        bottom.Children.Add(save);
        DockPanel.SetDock(bottom, Dock.Bottom);
        layout.Children.Add(bottom);
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "Song title" });
        heading.Children.Add(_title);
        heading.Children.Add(new TextBlock { Text = "Lyrics — separate slides with blank lines; use [Chorus] or [Verse] headings." });
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);
        layout.Children.Add(_lyrics);
        Content = layout;
        Loaded += (_, _) => _title.Focus();
    }
}
