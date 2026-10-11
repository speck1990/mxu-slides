using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Slides.Core;

namespace Slides;

public sealed class ArrangementEditor : Window
{
    private readonly Presentation _draft;
    private readonly ListBox _arrangements = new();
    private readonly ListBox _sequence = new();
    private readonly ComboBox _available = new();
    private readonly TextBox _name = new();

    public Presentation EditedPresentation => _draft;
    private SlideArrangement? Selected => _arrangements.SelectedItem as SlideArrangement;

    public ArrangementEditor(Presentation source)
    {
        _draft = PresentationEditing.Copy(source);
        Title = $"Arrangements · {source.Name}";
        Width = 700;
        Height = 510;
        MinWidth = 600;
        MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(28, 28, 30));
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");

        var root = new DockPanel { Margin = new Thickness(18) };
        Content = root;
        var help = new TextBlock
        {
            Text = "Build a song order from sections. Add a section again to repeat it without duplicating its slides.",
            Foreground = Brushes.LightGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        };
        DockPanel.SetDock(help, Dock.Top);
        root.Children.Add(help);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        footer.Children.Add(Button("Cancel", () => Close()));
        footer.Children.Add(Button("Save", Save));
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var columns = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(columns);

        var left = new DockPanel { Margin = new Thickness(0, 0, 18, 0) };
        columns.Children.Add(left);
        var leftTitle = Label("Saved arrangements");
        DockPanel.SetDock(leftTitle, Dock.Top);
        left.Children.Add(leftTitle);
        var leftButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        leftButtons.Children.Add(Button("New", AddArrangement));
        leftButtons.Children.Add(Button("Delete", DeleteArrangement));
        DockPanel.SetDock(leftButtons, Dock.Bottom);
        left.Children.Add(leftButtons);
        _arrangements.DisplayMemberPath = "Name";
        _arrangements.Background = new SolidColorBrush(Color.FromRgb(38, 38, 41));
        _arrangements.Foreground = Brushes.White;
        _sequence.Background = new SolidColorBrush(Color.FromRgb(38, 38, 41));
        _sequence.Foreground = Brushes.White;
        _name.Background = new SolidColorBrush(Color.FromRgb(51, 51, 55));
        _name.Foreground = Brushes.White;
        _arrangements.SelectionChanged += (_, _) => RefreshSequence();
        left.Children.Add(_arrangements);

        var right = new DockPanel();
        Grid.SetColumn(right, 1);
        columns.Children.Add(right);
        var fields = new StackPanel();
        DockPanel.SetDock(fields, Dock.Top);
        fields.Children.Add(Label("Arrangement name"));
        var rename = new StackPanel { Orientation = Orientation.Horizontal };
        _name.MinWidth = 250;
        _name.Margin = new Thickness(0, 0, 8, 0);
        rename.Children.Add(_name);
        rename.Children.Add(Button("Rename", RenameArrangement));
        fields.Children.Add(rename);
        fields.Children.Add(Label("Add a section", new Thickness(0, 15, 0, 5)));
        var add = new StackPanel { Orientation = Orientation.Horizontal };
        _available.MinWidth = 260;
        _available.DisplayMemberPath = "Name";
        _available.Background = Brushes.White;
        _available.Foreground = Brushes.Black;
        _available.Margin = new Thickness(0, 0, 8, 0);
        _available.ItemsSource = PresentationArrangements.Blocks(_draft)
            .Select(block => new BlockChoice(block.Id, $"{block.Name} ({block.Slides.Count} slides)")).ToArray();
        _available.SelectedIndex = 0;
        add.Children.Add(_available);
        add.Children.Add(Button("Add", AddBlock));
        fields.Children.Add(add);
        fields.Children.Add(Label("Section order", new Thickness(0, 15, 0, 5)));
        right.Children.Add(fields);
        var sequenceButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        sequenceButtons.Children.Add(Button("Remove", RemoveBlock));
        sequenceButtons.Children.Add(Button("↑", () => MoveBlock(-1)));
        sequenceButtons.Children.Add(Button("↓", () => MoveBlock(1)));
        DockPanel.SetDock(sequenceButtons, Dock.Bottom);
        right.Children.Add(sequenceButtons);
        right.Children.Add(_sequence);

        RefreshArrangements();
    }

    private sealed record BlockChoice(string Id, string Name);

    private static TextBlock Label(string text, Thickness? margin = null) => new()
    {
        Text = text, FontWeight = FontWeights.SemiBold,
        Margin = margin ?? new Thickness(0, 0, 0, 5)
    };

    private static Button Button(string title, Action action)
    {
        var button = new Button { Content = title, Padding = new Thickness(11, 6, 11, 6), Margin = new Thickness(0, 0, 7, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    private void RefreshArrangements(string? selectedId = null)
    {
        selectedId ??= Selected?.Id;
        _arrangements.ItemsSource = null;
        _arrangements.ItemsSource = _draft.Arrangements;
        _arrangements.SelectedItem = _draft.Arrangements.FirstOrDefault(item => item.Id == selectedId)
            ?? _draft.Arrangements.FirstOrDefault();
        RefreshSequence();
    }

    private void RefreshSequence(int selectedIndex = -1)
    {
        _name.Text = Selected?.Name ?? "";
        var byId = PresentationArrangements.Blocks(_draft).ToDictionary(block => block.Id);
        _sequence.ItemsSource = Selected?.BlockIds.Select((id, index) =>
            $"{index + 1}. {(byId.TryGetValue(id, out var block) ? block.Name : "Missing section")}").ToArray();
        if (_sequence.Items.Count > 0)
            _sequence.SelectedIndex = Math.Clamp(selectedIndex, 0, _sequence.Items.Count - 1);
    }

    private void AddArrangement()
    {
        var created = PresentationArrangements.Add(_draft, $"Arrangement {_draft.Arrangements.Count + 1}");
        RefreshArrangements(created.Id);
    }

    private void DeleteArrangement()
    {
        if (Selected is not { } arrangement) return;
        _draft.Arrangements.Remove(arrangement);
        RefreshArrangements();
    }

    private void RenameArrangement()
    {
        if (Selected is not { } arrangement || !ApplyName()) return;
        RefreshArrangements(arrangement.Id);
    }

    private bool ApplyName()
    {
        if (Selected is not { } arrangement) return true;
        var name = _name.Text.Trim();
        if (name.Length is < 1 or > 80) { Warn("Use a name from 1 to 80 characters."); return false; }
        arrangement.Name = name;
        return true;
    }

    private void AddBlock()
    {
        if (Selected is not { } arrangement || _available.SelectedItem is not BlockChoice block) return;
        arrangement.BlockIds.Add(block.Id);
        RefreshSequence(arrangement.BlockIds.Count - 1);
    }

    private void RemoveBlock()
    {
        if (Selected is not { } arrangement || _sequence.SelectedIndex < 0) return;
        if (arrangement.BlockIds.Count == 1) { Warn("Keep at least one section in an arrangement."); return; }
        var index = _sequence.SelectedIndex;
        arrangement.BlockIds.RemoveAt(index);
        RefreshSequence(index);
    }

    private void MoveBlock(int delta)
    {
        if (Selected is not { } arrangement) return;
        var index = _sequence.SelectedIndex;
        if (index < 0 || index + delta < 0 || index + delta >= arrangement.BlockIds.Count) return;
        (arrangement.BlockIds[index], arrangement.BlockIds[index + delta]) =
            (arrangement.BlockIds[index + delta], arrangement.BlockIds[index]);
        RefreshSequence(index + delta);
    }

    private void Save()
    {
        if (!ApplyName()) return;
        try { PresentationArrangements.Validate(_draft); DialogResult = true; }
        catch (Exception error) when (error is ArgumentException or InvalidDataException) { Warn(error.Message); }
    }

    private void Warn(string message) => MessageBox.Show(this, message, "Slides", MessageBoxButton.OK, MessageBoxImage.Warning);
}
