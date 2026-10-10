using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PeckSlides.Core;
using Forms = System.Windows.Forms;

namespace PeckSlides;

public partial class MainWindow : Window
{
    private readonly string _autosavePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PeckSlides", "autosave.pshow");
    private Show _show = new();
    private ProjectionWindow? _projection;
    private bool _black;

    public MainWindow()
    {
        InitializeComponent();
        foreach (var screen in Forms.Screen.AllScreens)
            DisplaySelector.Items.Add($"{screen.DeviceName} ({screen.Bounds.Width} × {screen.Bounds.Height})"
                + (screen.Primary ? " Primary" : ""));
        var secondary = Array.FindIndex(Forms.Screen.AllScreens, screen => !screen.Primary);
        DisplaySelector.SelectedIndex = secondary >= 0 ? secondary : 0;
        try
        {
            if (File.Exists(_autosavePath)) _show = ShowFiles.Load(_autosavePath);
        }
        catch (Exception ex) { MessageBox.Show($"Could not restore autosave: {ex.Message}"); }
        RefreshPresentations();
    }

    private void RefreshPresentations(int selection = 0)
    {
        Presentations.ItemsSource = null;
        Presentations.ItemsSource = _show.Presentations;
        if (_show.Presentations.Count > 0)
            Presentations.SelectedIndex = Math.Clamp(selection, 0, _show.Presentations.Count - 1);
        else Render();
    }

    private void AddSong(object sender, RoutedEventArgs e)
    {
        var editor = new SongEditor { Owner = this };
        if (editor.ShowDialog() != true) return;
        Try(() =>
        {
            _show.Presentations.Add(SongParser.Parse(editor.SongTitle, editor.Lyrics));
            Changed(_show.Presentations.Count - 1);
        });
    }

    private void EditSong(object sender, RoutedEventArgs e)
    {
        if (Presentations.SelectedItem is not Presentation selected || selected.Kind != "song") return;
        var editor = new SongEditor(selected.Name, string.Join("\n\n", selected.Slides.Select(slide => slide.Text))) { Owner = this };
        if (editor.ShowDialog() != true) return;
        Try(() =>
        {
            var replacement = SongParser.Parse(editor.SongTitle, editor.Lyrics);
            replacement.Id = selected.Id;
            var index = Presentations.SelectedIndex;
            _show.Presentations[index] = replacement;
            Changed(index);
        });
    }

    private void RemovePresentation(object sender, RoutedEventArgs e)
    {
        if (Presentations.SelectedIndex < 0) return;
        var index = Presentations.SelectedIndex;
        if (MessageBox.Show(this, "Remove this song or deck from the show?", "Peck Slides", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _show.Presentations.RemoveAt(index);
        Changed(Math.Max(0, index - 1));
    }

    private void AddMedia(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "Choose an image or video",
            Filter = "Images and videos|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.mp4;*.wmv;*.avi;*.mov|All files|*.*"
        };
        if (picker.ShowDialog(this) != true) return;
        _show.Presentations.Add(new Presentation
        {
            Name = Path.GetFileNameWithoutExtension(picker.FileName),
            Kind = "deck",
            Slides = [new Slide { Name = "Media", MediaPath = picker.FileName }]
        });
        Changed(_show.Presentations.Count - 1);
    }

    private void ImportMxu(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Import MxU Presentation JSON", Filter = "JSON|*.json" };
        if (picker.ShowDialog(this) != true) return;
        Try(() =>
        {
            _show.Presentations.Add(MxuPresentationImport.Load(picker.FileName));
            Changed(_show.Presentations.Count - 1);
        });
    }

    private void OpenShow(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Open show", Filter = "Peck Slides show|*.pshow|JSON|*.json" };
        if (picker.ShowDialog(this) != true) return;
        Try(() => { _show = ShowFiles.Load(picker.FileName); Changed(0); });
    }

    private void SaveShowAs(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog { Title = "Save show", Filter = "Peck Slides show|*.pshow" };
        if (picker.ShowDialog(this) != true) return;
        Try(() => ShowFiles.Save(picker.FileName, _show));
    }

    private void Changed(int selection)
    {
        ShowFiles.Save(_autosavePath, _show);
        RefreshPresentations(selection);
    }

    private void OnPresentationChanged(object sender, SelectionChangedEventArgs e)
    {
        Slides.ItemsSource = null;
        Slides.ItemsSource = (Presentations.SelectedItem as Presentation)?.Slides;
        if (Slides.Items.Count > 0) Slides.SelectedIndex = 0;
        else Render();
    }

    private void OnSlideChanged(object sender, SelectionChangedEventArgs e) => Render();

    private void Render()
    {
        var slide = Slides.SelectedItem as Slide;
        Preview.Show(slide, _black);
        _projection?.ShowSlide(slide, _black);
        Status.Text = slide is null ? "No slide selected" : $"{Slides.SelectedIndex + 1} / {Slides.Items.Count}";
    }

    private void ToggleProjection(object sender, RoutedEventArgs e)
    {
        if (_projection is not null) { _projection.Close(); return; }
        var screens = Forms.Screen.AllScreens;
        if (screens.Length == 0) { MessageBox.Show("No display detected."); return; }
        var index = Math.Clamp(DisplaySelector.SelectedIndex, 0, screens.Length - 1);
        _projection = new ProjectionWindow(screens[index]);
        _projection.Closed += (_, _) =>
        {
            _projection = null;
            ProjectButton.Content = "Start projection  (F5)";
            Activate();
        };
        _projection.Show();
        ProjectButton.Content = "Stop projection  (F5)";
        Render();
        Activate();
    }

    private void ToggleBlack(object sender, RoutedEventArgs e)
    {
        _black = !_black;
        Render();
    }

    private void Previous(object sender, RoutedEventArgs e)
    {
        if (Slides.SelectedIndex > 0) Slides.SelectedIndex--;
    }

    private void Next(object sender, RoutedEventArgs e)
    {
        if (Slides.SelectedIndex < Slides.Items.Count - 1) Slides.SelectedIndex++;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        if (e.Key == Key.F5) { ToggleProjection(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.B) { ToggleBlack(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Right || e.Key == Key.Space)
        { Next(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Left)
        { Previous(this, new RoutedEventArgs()); e.Handled = true; }
    }

    private void OnClosing(object? sender, CancelEventArgs e) => _projection?.Close();

    private void Try(Action operation)
    {
        try { operation(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Peck Slides", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
