using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Slides.Core;
using Forms = System.Windows.Forms;

namespace Slides;

public sealed class StageWindow : Window
{
    private readonly TextBlock _currentHeading = Label(25, "#53D675");
    private readonly TextBlock _currentText = Label(48, "#FFFFFF");
    private readonly TextBlock _nextHeading = Label(21, "#B3BAC5");
    private readonly TextBlock _nextText = Label(30, "#E5E8ED");
    private readonly TextBlock _notes = Label(22, "#FFD88A");
    private readonly TextBlock _timing = Label(23, "#C3C9D2");
    private readonly TextBlock _countdown = Label(23, "#53D675");

    public string? DisplayDeviceName { get; }

    public StageWindow(Forms.Screen? display)
    {
        DisplayDeviceName = display?.DeviceName;
        Title = "Slides Stage Display";
        Background = Brushes.Black;
        MinWidth = 620;
        MinHeight = 400;
        Width = 1000;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (display is not null)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowInTaskbar = false;
            Topmost = true;
            Left = display.Bounds.Left;
            Top = display.Bounds.Top;
            Width = display.Bounds.Width;
            Height = display.Bounds.Height;
        }
        var canvas = new Grid { Width = 960, Height = 540, Background = new SolidColorBrush(Color.FromRgb(10, 16, 24)) };
        canvas.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        canvas.RowDefinitions.Add(new RowDefinition { Height = new GridLength(175) });

        var current = new StackPanel { Margin = new Thickness(36, 25, 36, 20) };
        current.Children.Add(_currentHeading);
        _currentText.Margin = new Thickness(0, 24, 0, 0);
        current.Children.Add(_currentText);
        canvas.Children.Add(current);

        var lower = new Grid { Background = new SolidColorBrush(Color.FromRgb(27, 34, 46)) };
        lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var next = new StackPanel { Margin = new Thickness(36, 17, 20, 16) };
        next.Children.Add(_nextHeading);
        _nextText.Margin = new Thickness(0, 9, 0, 0);
        next.Children.Add(_nextText);
        lower.Children.Add(next);
        var side = new StackPanel { Margin = new Thickness(20, 17, 25, 16) };
        side.Children.Add(_timing);
        _countdown.Margin = new Thickness(0, 6, 0, 0);
        side.Children.Add(_countdown);
        _notes.Margin = new Thickness(0, 10, 0, 0);
        side.Children.Add(_notes);
        Grid.SetColumn(side, 1);
        lower.Children.Add(side);
        Grid.SetRow(lower, 1);
        canvas.Children.Add(lower);
        Content = new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
        KeyDown += (_, args) => { if (args.Key == Key.Escape) Close(); };
    }

    public void Update(StageCue cue, TimeSpan serviceElapsed, TimeSpan itemElapsed,
        TimeSpan countdownRemaining, bool countdownComplete)
    {
        _currentHeading.Text = cue.CurrentHeading;
        _currentText.Text = cue.CurrentText;
        _nextHeading.Text = "NEXT · " + cue.NextHeading;
        _nextText.Text = cue.NextText;
        _notes.Text = cue.OperatorNotes.Length == 0 ? "" : "NOTES · " + cue.OperatorNotes;
        var remaining = cue.PlannedMinutes is { } minutes
            ? itemElapsed <= TimeSpan.FromMinutes(minutes)
                ? $" · {Format(TimeSpan.FromMinutes(minutes) - itemElapsed)} left"
                : $" · {Format(itemElapsed - TimeSpan.FromMinutes(minutes))} over"
            : "";
        _timing.Text = $"SERVICE {Format(serviceElapsed)}\nITEM {Format(itemElapsed)}{remaining}";
        _countdown.Text = countdownComplete ? "TIMER 00:00 · TIME" :
            $"TIMER {Format(countdownRemaining)}";
        _countdown.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            countdownComplete ? "#FF6B6B" : "#53D675"));
    }

    private static TextBlock Label(int size, string color) => new()
    {
        FontFamily = new FontFamily("Segoe UI"), FontSize = size, Foreground =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
        TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis
    };

    private static string Format(TimeSpan time) =>
        $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
}
