using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Slides.Core;
using Forms = System.Windows.Forms;

namespace Slides;

public partial class MainWindow : Window
{
    private sealed class ServiceRow(int serviceIndex, string presentationId, Presentation presentation,
        ServiceItemDetails details, bool startsSection)
    {
        public int ServiceIndex { get; } = serviceIndex;
        public string PresentationId { get; } = presentationId;
        public string Name => Presentation.Name;
        public Presentation Presentation { get; } = presentation;
        public ServiceItemDetails Details { get; } = details;
        public string SectionHeader => details.Section.ToUpperInvariant();
        public Visibility HeaderVisibility => startsSection ? Visibility.Visible : Visibility.Collapsed;
        public string Subtitle => string.Join(" · ", new[]
        {
            details.PlannedMinutes is { } minutes ? $"{minutes} min" : "",
            details.Notes.Split('\n')[0].Trim()
        }.Where(value => value.Length > 0));
        public Visibility SubtitleVisibility => Subtitle.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
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
    private Show _show = new() { ServiceDate = DateOnly.FromDateTime(DateTime.Today) };
    private readonly ServiceRun _run = new();
    private readonly DispatcherTimer _runDisplayTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _videoDisplayTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private PresentationLibrary _library = new();
    private ProjectionWindow? _projection;
    private bool _black;
    private bool _ready;
    private bool _updatingDate;
    private bool _updatingVideoSlider;
    private bool _videoPaused;
    private Slide? _lastLiveSlide;

    private Presentation? SelectedPresentation => (Presentations.SelectedItem as ServiceRow)?.Presentation;
    private int SelectedServiceIndex => (Presentations.SelectedItem as ServiceRow)?.ServiceIndex ?? -1;

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
            _library = LibraryFiles.Load(_libraryPath);
            if (File.Exists(_autosavePath)) _show = ShowFiles.Load(_autosavePath);
            if (_library.ImportMissing(_show.Presentations) || !File.Exists(_libraryPath))
                LibraryFiles.Save(_libraryPath, _library);
        }
        catch (Exception ex) { MessageBox.Show($"Could not restore Slides data: {ex.Message}"); }
        RefreshFolderChoices();
        RefreshPresentations();
        _runDisplayTimer.Tick += (_, _) => RefreshRunDisplay();
        _runDisplayTimer.Start();
        _videoDisplayTimer.Tick += (_, _) => RefreshVideoTransport();
        _videoDisplayTimer.Start();
        Preview.VideoStateChanged += (_, _) => RefreshVideoTransport();
        _ready = true;
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
        var rows = new List<ServiceRow>();
        for (var index = 0; index < _show.ServiceItems!.Count; index++)
        {
            var id = _show.ServiceItems[index];
            if (!byId.TryGetValue(id, out var presentation)) continue;
            var details = _show.ServiceDetails![index];
            rows.Add(new ServiceRow(index, id, presentation, details,
                rows.Count == 0 || rows[^1].Details.Section != details.Section));
        }
        Presentations.ItemsSource = rows;
        RefreshServicePlanHeader();
        RefreshLibrary();
        if (rows.Count > 0)
        {
            var selectedRow = rows.FindIndex(row => row.ServiceIndex == selection);
            Presentations.SelectedIndex = selectedRow >= 0 ? selectedRow : Math.Clamp(selection, 0, rows.Count - 1);
        }
        else
        {
            SelectionTitle.Text = "Select an item in Service Flow";
            Slides.ItemsSource = null;
            Render();
        }
    }

    private void RefreshServicePlanHeader()
    {
        _updatingDate = true;
        ServiceDatePicker.SelectedDate = _show.ServiceDate?.ToDateTime(TimeOnly.MinValue);
        _updatingDate = false;
        var totalMinutes = _show.ServiceDetails!.Sum(item => item.PlannedMinutes ?? 0);
        ServiceSummary.Text = $"{_show.ServiceItems!.Count} items" +
            (totalMinutes > 0 ? $" · {totalMinutes} planned min" : "");
    }

    private void OnServiceDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updatingDate) return;
        Try(() =>
        {
            _show.ServiceDate = ServiceDatePicker.SelectedDate is { } date
                ? DateOnly.FromDateTime(date) : null;
            Changed(SelectedServiceIndex);
        });
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
            if (result.Updated > 0) Changed(SelectedServiceIndex);
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
            Changed(SelectedServiceIndex);
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
            Changed(SelectedServiceIndex);
        });
    }

    private void RemovePresentation(object sender, RoutedEventArgs e)
    {
        var index = SelectedServiceIndex;
        if (index < 0) return;
        if (MessageBox.Show(this, "Remove this item from the service? It will stay in the library.",
                "Slides", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _show.RemoveFromService(index);
        Changed(Math.Max(0, index - 1));
    }

    private void PlanServiceItem(object sender, RoutedEventArgs e)
    {
        var index = SelectedServiceIndex;
        if (index < 0) return;
        var dialog = new ServiceItemDialog(_show.ServiceDetails![index]) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        Try(() =>
        {
            _show.UpdateServiceItem(index, dialog.Section, dialog.Notes, dialog.PlannedMinutes);
            Changed(index);
        });
    }

    private void MoveServiceUp(object sender, RoutedEventArgs e) => MoveService(-1);
    private void MoveServiceDown(object sender, RoutedEventArgs e) => MoveService(1);

    private void MoveService(int delta)
    {
        var from = SelectedServiceIndex;
        if (from < 0 || from + delta < 0 || from + delta >= _show.ServiceItems!.Count) return;
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
            _run.Reset();
            _run.Select(null);
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
        Try(() =>
        {
            _show = new Show { ServiceItems = [], ServiceDate = DateOnly.FromDateTime(DateTime.Today) };
            _run.Reset();
            _run.Select(null);
            Changed(0);
        });
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
        _run.Select((Presentations.SelectedItem as ServiceRow)?.Details.Id);
        RefreshRunDisplay();
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
        if (!ReferenceEquals(slide, _lastLiveSlide))
        {
            _lastLiveSlide = slide;
            _videoPaused = false;
        }
        Preview.Show(slide, _black);
        _projection?.ShowSlide(slide, _black);
        if (_videoPaused)
        {
            Preview.PauseVideo();
            _projection?.PauseVideo();
        }
        RefreshVideoTransport();
        Status.Text = slide is null ? "No slide selected" : $"Slide {Slides.SelectedIndex + 1} of {Slides.Items.Count}";
    }

    private void ToggleVideo(object sender, RoutedEventArgs e)
    {
        if (!Preview.HasVideo) return;
        _videoPaused = !_videoPaused;
        if (_videoPaused)
        {
            Preview.PauseVideo();
            _projection?.PauseVideo();
        }
        else
        {
            Preview.PlayVideo();
            _projection?.PlayVideo();
        }
        RefreshVideoTransport();
    }

    private void SeekVideo(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _updatingVideoSlider || !Preview.HasVideo) return;
        var position = TimeSpan.FromSeconds(VideoSlider.Value);
        Preview.SeekVideo(position);
        _projection?.SeekVideo(position);
        RefreshVideoTransport();
    }

    private void RefreshVideoTransport()
    {
        if (VideoSlider is null) return;
        var duration = Preview.VideoDuration;
        var position = Preview.VideoPosition;
        VideoName.Text = Preview.HasVideo
            ? (Slides.SelectedItem as SlideCard)?.Name ?? "Video"
            : "No video playing";
        VideoButton.IsEnabled = Preview.HasVideo;
        VideoButton.Content = _videoPaused ? "Play" : "Pause";
        VideoSlider.IsEnabled = Preview.HasVideo && duration > TimeSpan.Zero;
        _updatingVideoSlider = true;
        VideoSlider.Maximum = duration > TimeSpan.Zero ? duration.TotalSeconds : 1;
        VideoSlider.Value = Math.Clamp(position.TotalSeconds, 0, VideoSlider.Maximum);
        _updatingVideoSlider = false;
        VideoTime.Text = $"{FormatTime(position)} / {FormatTime(duration)}";
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
        else PreviousItem(sender, e);
    }

    private void Next(object sender, RoutedEventArgs e)
    {
        if (Slides.SelectedIndex < Slides.Items.Count - 1) Slides.SelectedIndex++;
        else NextItem(sender, e);
    }

    private void PreviousItem(object sender, RoutedEventArgs e)
    {
        if (Presentations.SelectedIndex <= 0) return;
        Presentations.SelectedIndex--;
        if (Slides.Items.Count > 0) Slides.SelectedIndex = Slides.Items.Count - 1;
    }

    private void NextItem(object sender, RoutedEventArgs e)
    {
        if (Presentations.SelectedIndex < 0 || Presentations.SelectedIndex >= Presentations.Items.Count - 1) return;
        Presentations.SelectedIndex++;
    }

    private void ToggleRun(object sender, RoutedEventArgs e)
    {
        if (_run.IsRunning) _run.Pause();
        else _run.Start();
        RefreshRunDisplay();
    }

    private void ResetRun(object sender, RoutedEventArgs e)
    {
        if (_run.ServiceElapsed > TimeSpan.Zero &&
            MessageBox.Show(this, "Reset the live service timer and all item times?", "Slides",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _run.Reset();
        RefreshRunDisplay();
    }

    private void RefreshRunDisplay()
    {
        var row = Presentations.SelectedItem as ServiceRow;
        var plannedTotal = _show.ServiceDetails?.Sum(item => item.PlannedMinutes ?? 0) ?? 0;
        RunButton.Content = _run.IsRunning ? "Pause Run" : _run.ServiceElapsed > TimeSpan.Zero ? "Resume Run" : "Start Run";
        RunButton.IsEnabled = row is not null;
        RunStatus.Text = $"Service elapsed: {FormatTime(_run.ServiceElapsed)}" +
            (plannedTotal > 0 ? $" / {plannedTotal} min planned" : "");
        ItemRunStatus.Text = row is null ? "Select a service item to start." :
            $"This item: {FormatTime(_run.CurrentItemElapsed)}" +
            (row.Details.PlannedMinutes is { } minutes ? $" / {minutes} min planned" : "");
        RemainingStatus.Text = row?.Details.PlannedMinutes is { } planned
            ? _run.CurrentItemElapsed <= TimeSpan.FromMinutes(planned)
                ? $"Remaining: {FormatTime(TimeSpan.FromMinutes(planned) - _run.CurrentItemElapsed)}"
                : $"Over by: {FormatTime(_run.CurrentItemElapsed - TimeSpan.FromMinutes(planned))}"
            : "";
        var note = row?.Details.Notes;
        OperatorNote.Text = string.IsNullOrEmpty(note) ? "" : "Operator notes: " + note;
    }

    private static string FormatTime(TimeSpan time) =>
        $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None ||
            Keyboard.FocusedElement is TextBox or ComboBox or DatePicker ||
            e.Key == Key.Space && Keyboard.FocusedElement is ButtonBase) return;
        if (e.Key == Key.F5) { ToggleProjection(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.B) { ToggleBlack(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Right || e.Key == Key.Space)
        { Next(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Left)
        { Previous(this, new RoutedEventArgs()); e.Handled = true; }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _runDisplayTimer.Stop();
        _videoDisplayTimer.Stop();
        _projection?.Close();
    }

    private void Try(Action operation)
    {
        try { operation(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Slides", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
