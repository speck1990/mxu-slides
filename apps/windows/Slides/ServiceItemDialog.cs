using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Slides.Core;

namespace Slides;

public sealed class ServiceItemDialog : Window
{
    private readonly ComboBox _section = new()
    {
        ItemsSource = ServiceItemDetails.Sections,
        Background = Brushes.White,
        Foreground = Brushes.Black
    };
    private readonly TextBox _minutes = new() { Margin = new Thickness(0, 6, 0, 14) };
    private readonly TextBox _notes = new()
    {
        Height = 110,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Margin = new Thickness(0, 6, 0, 16)
    };

    public string Section => _section.SelectedItem as string ?? "Service";
    public string Notes => _notes.Text.Trim();
    public int? PlannedMinutes { get; private set; }

    public ServiceItemDialog(ServiceItemDetails details)
    {
        Title = "Plan service item";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(28, 28, 30));
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");

        _section.SelectedItem = details.Section;
        _minutes.Text = details.PlannedMinutes?.ToString() ?? "";
        _notes.Text = details.Notes;
        foreach (var field in new[] { _minutes, _notes })
        {
            field.Background = new SolidColorBrush(Color.FromRgb(51, 51, 55));
            field.Foreground = Brushes.White;
            field.Padding = new Thickness(8, 5, 8, 5);
        }

        var content = new StackPanel { Margin = new Thickness(18) };
        content.Children.Add(new TextBlock { Text = "Section", Margin = new Thickness(0, 0, 0, 6) });
        content.Children.Add(_section);
        content.Children.Add(new TextBlock { Text = "Planned minutes (optional)", Margin = new Thickness(0, 14, 0, 0) });
        content.Children.Add(_minutes);
        content.Children.Add(new TextBlock { Text = "Operator notes (up to 2,000 characters)" });
        content.Children.Add(_notes);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => Close();
        var save = new Button { Content = "Save", Padding = new Thickness(12, 6, 12, 6) };
        save.Click += (_, _) => Save();
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        content.Children.Add(buttons);
        Content = content;
    }

    private void Save()
    {
        var rawMinutes = _minutes.Text.Trim();
        int? plannedMinutes = null;
        if (_section.SelectedItem is not string || Notes.Length > 2000 ||
            rawMinutes.Length > 0 && (!int.TryParse(rawMinutes, out var minutes) || minutes is < 0 or > 480))
        {
            MessageBox.Show(this, "Choose a section, enter up to 2,000 characters of notes, and use 0 to 480 minutes.",
                "Slides", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (rawMinutes.Length > 0) plannedMinutes = int.Parse(rawMinutes);
        PlannedMinutes = plannedMinutes;
        DialogResult = true;
    }
}
