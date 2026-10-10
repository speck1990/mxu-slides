using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Slides.Core;
using Forms = System.Windows.Forms;

namespace Slides;

public partial class MainWindow : Window
{
    private sealed class ServiceRow(string presentationId, Presentation presentation)
    {
        public string PresentationId { get; } = presentationId;
        public string Name => Presentation.Name;
        public Presentation Presentation { get; } = presentation;
    }

    private sealed class SlideCard(int number, Slide slide)
    {
        public int Number { get; } = number;
        public string Name => Slide.Name;
        public Slide Slide { get; } = slide;
    }

    private readonly string _autosavePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Slides", "autosave.pshow");
    private Show _show = new();
    private ProjectionWindow? _projection;
    private bool _black;

    private Presentation? SelectedPresentation => (Presentations.SelectedItem as ServiceRow)?.Presentation;

    public MainWindow()
    {
        InitializeComponent();
        ServiceDate.Text = "Current show · " + DateTime.Now.ToString("dddd, MMMM d");
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
        _show.EnsureServiceItems();
        var byId = _show.Presentations.GroupBy(presentation => presentation.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var rows = _show.ServiceItems!.Select(id => byId.TryGetValue(id, out var presentation)
            ? new ServiceRow(id, presentation) : null)
            .Where(row => row is not null).ToArray();
        Presentations.ItemsSource = rows;
        RefreshLibrary();
        if (rows.Length > 0)
            Presentations.SelectedIndex = Math.Clamp(selection, 0, rows.Length - 1);
        else
        {
            SelectionTitle.Text = "Select an item in Service Flow";
            Slides.ItemsSource = null;
            Render();
        }
    }

    private void RefreshLibrary()
    {
        if (LibraryList is null) return;
        var selectedId = (LibraryList.SelectedItem as Presentation)?.Id;
        var query = LibrarySearch?.Text.Trim() ?? "";
        var visible = _show.Presentations.Where(presentation =>
            query.Length == 0 || presentation.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(presentation => presentation.Name).ToArray();
        LibraryList.ItemsSource = visible;
        LibraryList.SelectedItem = visible.FirstOrDefault(presentation => presentation.Id == selectedId)
            ?? visible.FirstOrDefault();
    }

    private void OnLibrarySearchChanged(object sender, TextChangedEventArgs e) => RefreshLibrary();

    private void AddSong(object sender, RoutedEventArgs e)
    {
        var editor = new SongEditor { Owner = this };
        if (editor.ShowDialog() != true) return;
        Try(() => AddNewPresentation(SongParser.Parse(editor.SongTitle, editor.Lyrics)));
    }

    private void AddNewPresentation(Presentation presentation)
    {
        _show.EnsureServiceItems();
        _show.Presentations.Add(presentation);
        _show.AddToService(presentation.Id);
        Changed(_show.ServiceItems!.Count - 1);
        LibraryList.SelectedItem = presentation;
    }

    private void AddLibrarySelection(object sender, RoutedEventArgs e)
    {
        if (LibraryList.SelectedItem is not Presentation presentation)
        {
            Status.Text = "Choose a library item to add to the service.";
            return;
        }
        Try(() =>
        {
            _show.AddToService(presentation.Id);
            Changed(_show.ServiceItems!.Count - 1);
        });
    }

    private void OnLibraryDoubleClick(object sender, MouseButtonEventArgs e) =>
        AddLibrarySelection(sender, e);

    private void EditSong(object sender, RoutedEventArgs e)
    {
        if (SelectedPresentation is not { Kind: "song" } selected) return;
        var editor = new SongEditor(selected.Name, SongParser.ToEditableLyrics(selected)) { Owner = this };
        if (editor.ShowDialog() != true) return;
        Try(() =>
        {
            var replacement = SongParser.Parse(editor.SongTitle, editor.Lyrics);
            replacement.Id = selected.Id;
            var index = _show.Presentations.FindIndex(presentation => presentation.Id == selected.Id);
            if (index < 0) throw new InvalidOperationException("Song is missing from the library.");
            _show.Presentations[index] = replacement;
            Changed(Presentations.SelectedIndex);
        });
    }

    private void RemovePresentation(object sender, RoutedEventArgs e)
    {
        if (Presentations.SelectedIndex < 0) return;
        var index = Presentations.SelectedIndex;
        if (MessageBox.Show(this, "Remove this item from the service? It will stay in the library.",
                "Slides", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _show.RemoveFromService(index);
        Changed(Math.Max(0, index - 1));
    }

    private void MoveServiceUp(object sender, RoutedEventArgs e) => MoveService(-1);
    private void MoveServiceDown(object sender, RoutedEventArgs e) => MoveService(1);

    private void MoveService(int delta)
    {
        var from = Presentations.SelectedIndex;
        if (from < 0 || from + delta < 0 || from + delta >= Presentations.Items.Count) return;
        _show.MoveServiceItem(from, from + delta);
        Changed(from + delta);
    }

    private void AddMedia(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "Choose an image or video",
            Filter = "Images and videos|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.mp4;*.wmv;*.avi;*.mov|All files|*.*"
        };
        if (picker.ShowDialog(this) != true) return;
        Try(() => AddNewPresentation(new Presentation
        {
            Name = Path.GetFileNameWithoutExtension(picker.FileName),
            Kind = "deck",
            Slides = [new Slide { Name = "Media", MediaPath = picker.FileName }]
        }));
    }

    private void ImportMxu(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Import MxU Presentation JSON", Filter = "JSON|*.json" };
        if (picker.ShowDialog(this) != true) return;
        Try(() => AddNewPresentation(MxuPresentationImport.Load(picker.FileName)));
    }

    private void OpenShow(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Open show", Filter = "Slides show|*.pshow|JSON|*.json" };
        if (picker.ShowDialog(this) != true) return;
        Try(() => { _show = ShowFiles.Load(picker.FileName); Changed(0); });
    }

    private void SaveShowAs(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog { Title = "Save show", Filter = "Slides show|*.pshow" };
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
        var presentation = SelectedPresentation;
        SelectionTitle.Text = presentation?.Name ?? "Select an item in Service Flow";
        Slides.ItemsSource = presentation?.Slides.Select((slide, index) =>
            new SlideCard(index + 1, slide)).ToArray();
        if (Slides.Items.Count > 0) Slides.SelectedIndex = 0;
        else Render();
    }

    private void OnSlideChanged(object sender, SelectionChangedEventArgs e) => Render();

    private void Render()
    {
        var slide = (Slides.SelectedItem as SlideCard)?.Slide;
        Preview.Show(slide, _black);
        _projection?.ShowSlide(slide, _black);
        Status.Text = slide is null ? "No slide selected" : $"Slide {Slides.SelectedIndex + 1} of {Slides.Items.Count}";
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
            ProjectButton.Content = "Start Projection  (F5)";
            Activate();
        };
        _projection.Show();
        ProjectButton.Content = "Stop Projection  (F5)";
        Render();
        Activate();
    }

    private void ToggleBlack(object sender, RoutedEventArgs e)
    {
        _black = !_black;
        BlackButton.Content = _black ? "Restore Screen  (B)" : "Black Screen  (B)";
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
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Slides", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
