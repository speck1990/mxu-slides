using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Slides.Core;

namespace Slides;

internal sealed class SongSelectWindow : Window
{
    private readonly TextBox _query = new();
    private readonly TextBox _lyrics = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 220,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    internal Presentation? ImportedSong { get; private set; }

    internal SongSelectWindow()
    {
        Title = "Import from SongSelect";
        Width = 620;
        Height = 510;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Search and download lyrics in SongSelect using your browser and your CCLI account. Then load the downloaded text file or paste its contents here.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Song title or CCLI number (optional)", Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(_query);
        var open = new Button { Content = "Open SongSelect in browser", Margin = new Thickness(0, 8, 0, 0) };
        open.Click += (_, _) =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_query.Text)) Clipboard.SetText(_query.Text.Trim());
                Process.Start(new ProcessStartInfo("https://songselect.ccli.com") { UseShellExecute = true });
                _status.Text = string.IsNullOrWhiteSpace(_query.Text)
                    ? "Sign in, search, and download the song's text lyrics."
                    : "Your search is copied. Paste it into SongSelect, then download the song's text lyrics.";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        panel.Children.Add(open);
        var load = new Button { Content = "Load downloaded .txt file…", Margin = new Thickness(0, 12, 0, 0) };
        load.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Title = "Choose SongSelect lyrics", Filter = "Text lyrics|*.txt|All files|*.*" };
            if (picker.ShowDialog(this) != true) return;
            try { _lyrics.Text = File.ReadAllText(picker.FileName); _status.Text = "Loaded " + Path.GetFileName(picker.FileName); }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        panel.Children.Add(load);
        panel.Children.Add(new TextBlock { Text = "Downloaded lyrics", Margin = new Thickness(0, 10, 0, 0) });
        panel.Children.Add(_lyrics);
        var import = new Button { Content = "Import song", Margin = new Thickness(0, 10, 0, 0) };
        import.Click += (_, _) =>
        {
            try { ImportedSong = SongSelectText.Parse(_lyrics.Text); DialogResult = true; }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        panel.Children.Add(import);
        panel.Children.Add(_status);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
