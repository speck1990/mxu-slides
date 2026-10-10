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

    private sealed class FolderChoice(string? id, string name)
    {
        public string? Id { get; } = id;
        public string Name { get; } = name;
    }

    private readonly string _autosavePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Slides", "autosave.pshow");
    private readonly string _libraryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Slides", "library.json");
    private readonly string _packageCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Slides", "packages");
    private Show _show = new();
    private PresentationLibrary _library = new();
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
            _library = LibraryFiles.Load(_libraryPath);
            if (File.Exists(_autosavePath)) _show = ShowFiles.Load(_autosavePath);
            if (_library.ImportMissing(_show.Presentations) || !File.Exists(_libraryPath))
                LibraryFiles.Save(_libraryPath, _library);
        }
        catch (Exception ex) { MessageBox.Show($"Could not restore Slides data: {ex.Message}"); }
        RefreshFolderChoices();
        RefreshPresentations();
    }

    private void RefreshFolderChoices(string? selectedId = null)
    {
        var choices = new[] { new FolderChoice(null, "All Slides"), new FolderChoice("", "Top Level") }
            .Concat(_library.Folders.OrderBy(folder => folder.Name)
                .Select(folder => new FolderChoice(folder.Id, folder.Name))).ToArray();
        FolderFilter.ItemsSource = choices;
        FolderFilter.SelectedItem = choices.FirstOrDefault(choice => choice.Id == selectedId) ?? choices[0];
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
        var folderId = (FolderFilter?.SelectedItem as FolderChoice)?.Id;
        var visible = _library.Presentations.Where(presentation =>
            (folderId is null || (folderId.Length == 0
                ? _library.FolderFor(presentation.Id) is null
                : _library.FolderFor(presentation.Id) == folderId)) &&
            (query.Length == 0 || presentation.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            .OrderBy(presentation => presentation.Name).ToArray();
        LibraryList.ItemsSource = visible;
        LibraryList.SelectedItem = visible.FirstOrDefault(presentation => presentation.Id == selectedId)
            ?? visible.FirstOrDefault();
    }

    private void OnLibrarySearchChanged(object sender, TextChangedEventArgs e) => RefreshLibrary();

    private void OnFolderFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        var isFolder = (FolderFilter.SelectedItem as FolderChoice)?.Id is { Length: > 0 };
        RenameFolderButton.IsEnabled = isFolder;
        DeleteFolderButton.IsEnabled = isFolder;
        RefreshLibrary();
    }

    private void NewFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new FolderNameDialog("New folder") { Owner = this };
        if (dialog.ShowDialog() != true) return;
        Try(() =>
        {
            var folder = _library.CreateFolder(dialog.FolderName);
            LibraryFiles.Save(_libraryPath, _library);
            RefreshFolderChoices(folder.Id);
        });
    }

    private void RenameFolder(object sender, RoutedEventArgs e)
    {
        var folderId = (FolderFilter.SelectedItem as FolderChoice)?.Id;
        var folder = _library.Folders.FirstOrDefault(item => item.Id == folderId);
        if (folder is null) return;
        var dialog = new FolderNameDialog("Rename folder", folder.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        Try(() =>
        {
            _library.RenameFolder(folder.Id, dialog.FolderName);
            LibraryFiles.Save(_libraryPath, _library);
            RefreshFolderChoices(folder.Id);
        });
    }

    private void DeleteFolder(object sender, RoutedEventArgs e)
    {
        var folderId = (FolderFilter.SelectedItem as FolderChoice)?.Id;
        if (folderId is null || folderId.Length == 0) return;
        if (MessageBox.Show(this, "Delete this folder? Its items will move to Top Level.",
                "Slides", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        Try(() =>
        {
            _library.DeleteFolder(folderId);
            LibraryFiles.Save(_libraryPath, _library);
            RefreshFolderChoices("");
        });
    }

    private void MoveLibraryItem(object sender, RoutedEventArgs e)
    {
        if (LibraryList.SelectedItem is not Presentation presentation)
        {
            Status.Text = "Choose a library item to move.";
            return;
        }
        var menu = new ContextMenu();
        void AddChoice(string label, string? folderId)
        {
            var item = new MenuItem
            {
                Header = label,
                IsChecked = _library.FolderFor(presentation.Id) == folderId
            };
            item.Click += (_, _) => Try(() =>
            {
                _library.MoveToFolder(presentation.Id, folderId);
                LibraryFiles.Save(_libraryPath, _library);
                RefreshLibrary();
            });
            menu.Items.Add(item);
        }
        AddChoice("Top Level", null);
        foreach (var folder in _library.Folders.OrderBy(folder => folder.Name))
            AddChoice(folder.Name, folder.Id);
        menu.PlacementTarget = (Button)sender;
        menu.IsOpen = true;
    }

    private void RelinkMedia(object sender, RoutedEventArgs e)
    {
        using var picker = new Forms.FolderBrowserDialog
        {
            Description = "Choose the folder containing your moved images and videos",
            ShowNewFolderButton = false
        };
        if (picker.ShowDialog() != Forms.DialogResult.OK) return;
        Try(() =>
        {
            var result = MediaRelinker.Relink(_library.Presentations.Concat(_show.Presentations), picker.SelectedPath);
            if (result.Updated > 0) Changed(Presentations.SelectedIndex);
            MessageBox.Show(this,
                $"Relinked {result.Updated} media references. {result.Unresolved} not found; {result.Ambiguous} had more than one match.",
                "Slides");
        });
    }

    private void AddSong(object sender, RoutedEventArgs e)
    {
        var editor = new SongEditor { Owner = this };
        if (editor.ShowDialog() != true) return;
        Try(() => AddNewPresentation(SongParser.Parse(editor.SongTitle, editor.Lyrics)));
    }

    private void AddDeck(object sender, RoutedEventArgs e)
    {
        var editor = new DeckEditor(PresentationEditing.NewDeck()) { Owner = this };
        if (editor.ShowDialog() != true) return;
        Try(() => AddNewPresentation(editor.EditedPresentation));
    }

    private void AddNewPresentation(Presentation presentation)
    {
        _library.Upsert(presentation);
        var folderId = (FolderFilter.SelectedItem as FolderChoice)?.Id;
        if (!string.IsNullOrEmpty(folderId)) _library.MoveToFolder(presentation.Id, folderId);
        _library.AddToShow(_show, presentation.Id);
        Changed(_show.ServiceItems!.Count - 1);
        LibraryList.SelectedItem = _library.Presentations.First(item => item.Id == presentation.Id);
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
            _library.AddToShow(_show, presentation.Id);
            Changed(_show.ServiceItems!.Count - 1);
        });
    }

    private void OnLibraryDoubleClick(object sender, MouseButtonEventArgs e) =>
        AddLibrarySelection(sender, e);

    private void EditSong(object sender, RoutedEventArgs e)
    {
        if (SelectedPresentation is not { Kind: "song" } selected) return;
        if (selected.Slides.Any(slide => slide.Elements is { Count: > 0 }))
        {
            MessageBox.Show(this, "This song has positioned slide objects. Use Edit Slides, then Edit Layout to change its text without losing the layout.", "Slides");
            return;
        }
        var editor = new SongEditor(selected.Name, SongParser.ToEditableLyrics(selected)) { Owner = this };
        if (editor.ShowDialog() != true) return;
        Try(() =>
        {
            var replacement = SongParser.Parse(editor.SongTitle, editor.Lyrics);
            replacement.Id = selected.Id;
            PresentationEditing.PreserveSongDesign(selected, replacement);
            var index = _show.Presentations.FindIndex(presentation => presentation.Id == selected.Id);
            if (index < 0) throw new InvalidOperationException("Song is missing from the library.");
            _show.Presentations[index] = replacement;
            _library.Upsert(replacement);
            Changed(Presentations.SelectedIndex);
        });
    }

    private void EditSlides(object sender, RoutedEventArgs e)
    {
        if (SelectedPresentation is not { } selected) return;
        var editor = new DeckEditor(selected) { Owner = this };
        if (editor.ShowDialog() != true) return;
        Try(() =>
        {
            var index = _show.Presentations.FindIndex(presentation => presentation.Id == selected.Id);
            if (index < 0) throw new InvalidOperationException("Presentation is missing from the show.");
            _show.Presentations[index] = editor.EditedPresentation;
            _library.Upsert(editor.EditedPresentation);
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
        var picker = new OpenFileDialog
        {
            Title = "Open show",
            Filter = "Slides shows|*.pshow;*.slidespack|Portable show|*.slidespack|Show JSON|*.pshow;*.json"
        };
        if (picker.ShowDialog(this) != true) return;
        Try(() =>
        {
            var opened = Path.GetExtension(picker.FileName).Equals(".slidespack", StringComparison.OrdinalIgnoreCase)
                ? ShowPackage.Open(picker.FileName, _packageCachePath)
                : ShowFiles.Load(picker.FileName);
            _library.ImportMissing(opened.Presentations);
            _library.RepairMissingMediaFrom(opened.Presentations);
            _show = opened;
            Changed(0);
        });
    }

    private void PackShow(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog
        {
            Title = "Save portable show with media",
            Filter = "Slides portable show|*.slidespack",
            DefaultExt = ".slidespack",
            AddExtension = true
        };
        if (picker.ShowDialog(this) != true) return;
        Try(() =>
        {
            ShowPackage.Save(picker.FileName, _show);
            MessageBox.Show(this, "Portable show saved with its media.", "Slides");
        });
    }

    private void NewShow(object sender, RoutedEventArgs e)
    {
        if (_show.ServiceItems is { Count: > 0 } &&
            MessageBox.Show(this, "Start a new show? Use Save As first if you want to keep this service.",
                "Slides", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        Try(() => { _show = new Show { ServiceItems = [] }; Changed(0); });
    }

    private void SaveShowAs(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog { Title = "Save show", Filter = "Slides show|*.pshow" };
        if (picker.ShowDialog(this) != true) return;
        Try(() => ShowFiles.Save(picker.FileName, _show));
    }

    private void Changed(int selection)
    {
        LibraryFiles.Save(_libraryPath, _library);
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
