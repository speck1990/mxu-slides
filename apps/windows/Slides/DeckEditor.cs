using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Slides.Core;

namespace Slides;

public sealed class DeckEditor : Window
{
    private readonly Presentation _draft;
    private readonly ListBox _slides = new() { Width = 190, DisplayMemberPath = nameof(Slide.Name) };
    private readonly TextBox _title = Field();
    private readonly TextBox _slideName = Field();
    private readonly TextBox _body = Field();
    private readonly TextBox _background = Field();
    private readonly TextBox _fontName = Field();
    private readonly TextBox _fontSize = Field();
    private readonly TextBox _textColor = Field();
    private readonly ComboBox _alignment = new() { Width = 150, Margin = new Thickness(0, 4, 0, 12) };
    private readonly ComboBox _weight = new() { Width = 150, Margin = new Thickness(0, 4, 0, 12) };
    private readonly ComboBox _themes = new() { Width = 190, DisplayMemberPath = nameof(SlideTheme.Name) };
    private readonly TextBlock _media = new() { Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap };
    private readonly SlideSurface _preview = new() { Width = 960, Height = 540 };
    private bool _loading;

    public Presentation EditedPresentation => _draft;

    public DeckEditor(Presentation presentation)
    {
        _draft = PresentationEditing.Copy(presentation);
        Title = "Edit slides";
        Width = 900;
        Height = 690;
        MinWidth = 760;
        MinHeight = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C1C1E"));
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");

        var layout = new DockPanel { Margin = new Thickness(18) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(Label("Presentation title"));
        _title.Text = _draft.Name;
        header.Children.Add(_title);
        var themeRow = new WrapPanel();
        themeRow.Children.Add(new TextBlock { Text = "Starter look", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        _themes.ItemsSource = SlideThemes.All;
        themeRow.Children.Add(_themes);
        themeRow.Children.Add(ActionButton("Apply to all slides", ApplyTheme));
        header.Children.Add(themeRow);
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        footer.Children.Add(ActionButton("Cancel", () => Close()));
        footer.Children.Add(ActionButton("Save presentation", Save));
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new DockPanel { Margin = new Thickness(0, 0, 15, 0) };
        var slideLabel = Label("Slides");
        DockPanel.SetDock(slideLabel, Dock.Top);
        left.Children.Add(slideLabel);
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(ActionButton("+ Text", AddText));
        actions.Children.Add(ActionButton("+ Media", AddMedia));
        actions.Children.Add(ActionButton("Duplicate", Duplicate));
        actions.Children.Add(ActionButton("Remove", Remove));
        actions.Children.Add(ActionButton("↑", () => Move(-1)));
        actions.Children.Add(ActionButton("↓", () => Move(1)));
        DockPanel.SetDock(actions, Dock.Bottom);
        left.Children.Add(actions);
        _slides.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#242426"));
        _slides.Foreground = Brushes.White;
        _slides.ItemsSource = _draft.Slides;
        _slides.SelectionChanged += (_, _) => LoadSelected();
        left.Children.Add(_slides);
        columns.Children.Add(left);

        var right = new DockPanel();
        Grid.SetColumn(right, 1);
        columns.Children.Add(right);
        var previewFrame = new Border
        {
            Height = 190,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brushes.Black,
            CornerRadius = new CornerRadius(6),
            Child = new Viewbox { Width = 338, Height = 190, Child = _preview },
            Margin = new Thickness(0, 0, 0, 14)
        };
        DockPanel.SetDock(previewFrame, Dock.Top);
        right.Children.Add(previewFrame);
        var fields = new StackPanel();
        fields.Children.Add(Label("Slide name"));
        fields.Children.Add(_slideName);
        fields.Children.Add(Label("Text"));
        _body.AcceptsReturn = true;
        _body.TextWrapping = TextWrapping.Wrap;
        _body.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _body.Height = 120;
        fields.Children.Add(_body);
        fields.Children.Add(Label("Background color (#RRGGBB or #RRGGBBAA)"));
        fields.Children.Add(_background);
        fields.Children.Add(Label("Font family"));
        fields.Children.Add(_fontName);
        fields.Children.Add(Label("Font size (12–160)"));
        fields.Children.Add(_fontSize);
        fields.Children.Add(Label("Text color (#RRGGBB or #RRGGBBAA)"));
        fields.Children.Add(_textColor);
        fields.Children.Add(Label("Text alignment"));
        _alignment.ItemsSource = new[] { "left", "center", "right" };
        fields.Children.Add(_alignment);
        fields.Children.Add(Label("Text weight"));
        _weight.ItemsSource = new[] { "normal", "semibold", "bold" };
        fields.Children.Add(_weight);
        fields.Children.Add(Label("Image or video"));
        fields.Children.Add(_media);
        var mediaActions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        mediaActions.Children.Add(ActionButton("Choose media", ChooseMedia));
        mediaActions.Children.Add(ActionButton("Clear media", ClearMedia));
        fields.Children.Add(mediaActions);
        right.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        layout.Children.Add(columns);
        Content = layout;

        _title.TextChanged += (_, _) => { if (!_loading) _draft.Name = _title.Text; };
        _slideName.TextChanged += (_, _) => UpdateSlide();
        _slideName.LostFocus += (_, _) => RefreshSlideList();
        _body.TextChanged += (_, _) => UpdateSlide();
        _background.TextChanged += (_, _) => UpdateSlide();
        _fontName.TextChanged += (_, _) => UpdateTextStyle();
        _fontSize.TextChanged += (_, _) => UpdateTextStyle();
        _textColor.TextChanged += (_, _) => UpdateTextStyle();
        _alignment.SelectionChanged += (_, _) => UpdateTextStyle();
        _weight.SelectionChanged += (_, _) => UpdateTextStyle();
        _slides.SelectedIndex = 0;
    }

    private static TextBox Field() => new()
    {
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333337")),
        Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#48484D")),
        Padding = new Thickness(8, 5, 8, 5),
        Margin = new Thickness(0, 4, 0, 12)
    };

    private static TextBlock Label(string text) => new() { Text = text, Foreground = Brushes.White };

    private static Button ActionButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#343438")),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#48484D")),
            Padding = new Thickness(9, 5, 9, 5),
            Margin = new Thickness(0, 0, 6, 6)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void LoadSelected()
    {
        _loading = true;
        var slide = _slides.SelectedItem as Slide;
        _slideName.IsEnabled = _body.IsEnabled = _background.IsEnabled = slide is not null;
        _fontName.IsEnabled = _fontSize.IsEnabled = _textColor.IsEnabled =
            _alignment.IsEnabled = _weight.IsEnabled = slide is not null;
        _slideName.Text = slide?.Name ?? "";
        _body.Text = slide?.Text ?? "";
        _background.Text = slide?.Background ?? "";
        var style = slide is null ? new SlideTextStyle() : SlideThemes.Effective(slide);
        _fontName.Text = style.FontName;
        _fontSize.Text = style.FontSize.ToString(CultureInfo.InvariantCulture);
        _textColor.Text = style.ColorHex;
        _alignment.SelectedItem = style.HorizontalAlignment;
        _weight.SelectedItem = style.Weight;
        _media.Text = slide?.MediaPath ?? "No media";
        _preview.Show(slide, false);
        _loading = false;
    }

    private void UpdateSlide()
    {
        if (_loading || _slides.SelectedItem is not Slide slide) return;
        slide.Name = _slideName.Text;
        slide.Text = _body.Text;
        slide.Background = _background.Text;
        _preview.Show(slide, false);
    }

    private void UpdateTextStyle()
    {
        if (_loading || _slides.SelectedItem is not Slide slide) return;
        var style = slide.TextStyle ??= new SlideTextStyle();
        style.FontName = _fontName.Text;
        style.FontSize = double.TryParse(_fontSize.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
            ? size : double.NaN;
        style.ColorHex = _textColor.Text;
        style.HorizontalAlignment = _alignment.SelectedItem as string ?? "center";
        style.Weight = _weight.SelectedItem as string ?? "semibold";
        if (double.IsFinite(style.FontSize) && style.FontSize is >= 12 and <= 160)
            _preview.Show(slide, false);
    }

    private void ApplyTheme()
    {
        if (_themes.SelectedItem is not SlideTheme theme) return;
        SlideThemes.Apply(_draft, theme);
        LoadSelected();
    }

    private void RefreshSlideList()
    {
        var selected = _slides.SelectedItem;
        _slides.Items.Refresh();
        _slides.SelectedItem = selected;
    }

    private void AddText()
    {
        var slide = PresentationEditing.AddSlide(_draft);
        _slides.Items.Refresh();
        _slides.SelectedItem = slide;
    }

    private void AddMedia()
    {
        var path = PickMedia();
        if (path is null) return;
        var slide = PresentationEditing.AddSlide(_draft, path);
        _slides.Items.Refresh();
        _slides.SelectedItem = slide;
    }

    private void ChooseMedia()
    {
        if (_slides.SelectedItem is not Slide slide) return;
        var path = PickMedia();
        if (path is null) return;
        slide.MediaPath = path;
        LoadSelected();
    }

    private string? PickMedia()
    {
        var picker = new OpenFileDialog
        {
            Title = "Choose an image or video",
            Filter = "Images and videos|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.mp4;*.wmv;*.avi;*.mov|All files|*.*"
        };
        return picker.ShowDialog(this) == true ? picker.FileName : null;
    }

    private void ClearMedia()
    {
        if (_slides.SelectedItem is not Slide slide) return;
        slide.MediaPath = null;
        LoadSelected();
    }

    private void Duplicate()
    {
        if (_slides.SelectedIndex < 0) return;
        var slide = PresentationEditing.DuplicateSlide(_draft, _slides.SelectedIndex);
        _slides.Items.Refresh();
        _slides.SelectedItem = slide;
    }

    private void Remove()
    {
        var index = _slides.SelectedIndex;
        if (index < 0 || _draft.Slides.Count == 1) return;
        _draft.Slides.RemoveAt(index);
        _slides.Items.Refresh();
        _slides.SelectedIndex = Math.Min(index, _draft.Slides.Count - 1);
    }

    private void Move(int delta)
    {
        var index = _slides.SelectedIndex;
        if (index < 0 || index + delta < 0 || index + delta >= _draft.Slides.Count) return;
        PresentationEditing.MoveSlide(_draft, index, index + delta);
        _slides.Items.Refresh();
        _slides.SelectedIndex = index + delta;
    }

    private void Save()
    {
        try { PresentationEditing.Validate(_draft); DialogResult = true; }
        catch (Exception error) when (error is ArgumentException or FileNotFoundException)
        { MessageBox.Show(this, error.Message, "Slides", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
