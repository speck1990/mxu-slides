using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Slides.Core;

namespace Slides;

public sealed class SlideLayoutEditor : Window
{
    private readonly List<SlideElement> _elements;
    private readonly Slide _previewSlide;
    private readonly ListBox _objects = new() { Width = 185, DisplayMemberPath = nameof(SlideElement.Name) };
    private readonly SlideSurface _preview = new() { Width = 960, Height = 540 };
    private readonly TextBox _name = Field();
    private readonly TextBox _text = Field();
    private readonly TextBox _x = Field();
    private readonly TextBox _y = Field();
    private readonly TextBox _width = Field();
    private readonly TextBox _height = Field();
    private readonly TextBox _fill = Field();
    private readonly TextBox _corner = Field();
    private bool _loading;

    public List<SlideElement> EditedElements => _elements;

    public SlideLayoutEditor(Slide slide)
    {
        _elements = SlideLayout.BeginEdit(slide);
        _previewSlide = new Slide
        {
            Background = slide.Background, MediaPath = slide.MediaPath,
            TextStyle = slide.TextStyle, Elements = _elements
        };
        Title = "Edit slide layout";
        Width = 950;
        Height = 760;
        MinWidth = 800;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#1C1C1E");
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");

        var layout = new DockPanel { Margin = new Thickness(18) };
        var header = new TextBlock
        {
            Text = "Position text and rectangles on the 960 × 540 slide canvas. Objects lower in the list draw in front.",
            Foreground = Brushes.LightGray, Margin = new Thickness(0, 0, 0, 14)
        };
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        footer.Children.Add(Button("Cancel", () => Close()));
        footer.Children.Add(Button("Save layout", Save));
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new DockPanel { Margin = new Thickness(0, 0, 15, 0) };
        var heading = Label("Objects");
        DockPanel.SetDock(heading, Dock.Top);
        left.Children.Add(heading);
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(Button("+ Text", AddText));
        actions.Children.Add(Button("+ Rectangle", AddShape));
        actions.Children.Add(Button("Duplicate", Duplicate));
        actions.Children.Add(Button("Remove", Remove));
        actions.Children.Add(Button("Back", () => Move(-1)));
        actions.Children.Add(Button("Forward", () => Move(1)));
        DockPanel.SetDock(actions, Dock.Bottom);
        left.Children.Add(actions);
        _objects.Background = Brush("#242426");
        _objects.Foreground = Brushes.White;
        _objects.ItemsSource = _elements;
        _objects.SelectionChanged += (_, _) => LoadSelected();
        left.Children.Add(_objects);
        columns.Children.Add(left);

        var right = new DockPanel();
        Grid.SetColumn(right, 1);
        columns.Children.Add(right);
        var preview = new Border
        {
            Height = 270, HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brushes.Black, CornerRadius = new CornerRadius(6),
            Child = new Viewbox { Width = 480, Height = 270, Child = _preview },
            Margin = new Thickness(0, 0, 0, 14)
        };
        DockPanel.SetDock(preview, Dock.Top);
        right.Children.Add(preview);
        var fields = new StackPanel();
        fields.Children.Add(Label("Object name"));
        fields.Children.Add(_name);
        fields.Children.Add(Label("Text"));
        _text.AcceptsReturn = true;
        _text.TextWrapping = TextWrapping.Wrap;
        _text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _text.Height = 85;
        fields.Children.Add(_text);
        var position = new UniformGrid { Columns = 4 };
        AddNumber(position, "X", _x);
        AddNumber(position, "Y", _y);
        AddNumber(position, "Width", _width);
        AddNumber(position, "Height", _height);
        fields.Children.Add(position);
        fields.Children.Add(Label("Rectangle fill (#RRGGBB or #RRGGBBAA)"));
        fields.Children.Add(_fill);
        fields.Children.Add(Label("Corner radius"));
        fields.Children.Add(_corner);
        right.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        layout.Children.Add(columns);
        Content = layout;

        foreach (var field in new[] { _name, _text, _x, _y, _width, _height, _fill, _corner })
            field.TextChanged += (_, _) => UpdateObject();
        _name.LostFocus += (_, _) => RefreshObjectList();
        _objects.SelectedIndex = _elements.Count > 0 ? 0 : -1;
        RefreshPreview();
    }

    private static void AddNumber(Panel panel, string caption, TextBox field)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        stack.Children.Add(Label(caption));
        stack.Children.Add(field);
        panel.Children.Add(stack);
    }

    private static TextBox Field() => new()
    {
        Background = Brush("#333337"), Foreground = Brushes.White,
        BorderBrush = Brush("#48484D"), Padding = new Thickness(8, 5, 8, 5),
        Margin = new Thickness(0, 4, 0, 12)
    };

    private static Brush Brush(string color) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

    private static TextBlock Label(string caption) => new() { Text = caption, Foreground = Brushes.White };

    private static Button Button(string caption, Action action)
    {
        var button = new Button
        {
            Content = caption, Background = Brush("#343438"), Foreground = Brushes.White,
            BorderBrush = Brush("#48484D"), Padding = new Thickness(9, 5, 9, 5),
            Margin = new Thickness(0, 0, 6, 6)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void LoadSelected()
    {
        _loading = true;
        var item = _objects.SelectedItem as SlideElement;
        foreach (var field in new[] { _name, _text, _x, _y, _width, _height, _fill, _corner })
            field.IsEnabled = item is not null;
        _text.IsEnabled = item?.Kind == "text";
        _fill.IsEnabled = _corner.IsEnabled = item?.Kind == "shape";
        _name.Text = item?.Name ?? "";
        _text.Text = item?.Text ?? "";
        _x.Text = Number(item?.X);
        _y.Text = Number(item?.Y);
        _width.Text = Number(item?.Width);
        _height.Text = Number(item?.Height);
        _fill.Text = item?.FillColorHex ?? "";
        _corner.Text = Number(item?.CornerRadius);
        _loading = false;
    }

    private static string Number(double? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static double Parse(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : double.NaN;

    private void UpdateObject()
    {
        if (_loading || _objects.SelectedItem is not SlideElement item) return;
        item.Name = _name.Text;
        if (item.Kind == "text") item.Text = _text.Text;
        item.X = Parse(_x.Text);
        item.Y = Parse(_y.Text);
        item.Width = Parse(_width.Text);
        item.Height = Parse(_height.Text);
        if (item.Kind == "shape")
        {
            item.FillColorHex = _fill.Text;
            item.CornerRadius = Parse(_corner.Text);
        }
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        try { SlideLayout.Validate(_elements); _preview.Show(_previewSlide, false); }
        catch (ArgumentException) { }
    }

    private void RefreshObjectList()
    {
        var selected = _objects.SelectedItem;
        _objects.Items.Refresh();
        _objects.SelectedItem = selected;
    }

    private void AddText()
    {
        var item = SlideLayout.AddText(_elements);
        _objects.Items.Refresh();
        _objects.SelectedItem = item;
        RefreshPreview();
    }

    private void AddShape()
    {
        var item = SlideLayout.AddShape(_elements);
        _objects.Items.Refresh();
        _objects.SelectedItem = item;
        RefreshPreview();
    }

    private void Duplicate()
    {
        if (_objects.SelectedIndex < 0) return;
        try
        {
            SlideLayout.Validate(_elements);
            var item = SlideLayout.Duplicate(_elements, _objects.SelectedIndex);
            _objects.Items.Refresh();
            _objects.SelectedItem = item;
            RefreshPreview();
        }
        catch (ArgumentException error) { ShowError(error.Message); }
    }

    private void Remove()
    {
        var index = _objects.SelectedIndex;
        if (index < 0) return;
        _elements.RemoveAt(index);
        _objects.Items.Refresh();
        _objects.SelectedIndex = Math.Min(index, _elements.Count - 1);
        RefreshPreview();
    }

    private void Move(int delta)
    {
        var index = _objects.SelectedIndex;
        if (index < 0 || index + delta < 0 || index + delta >= _elements.Count) return;
        SlideLayout.Move(_elements, index, index + delta);
        _objects.Items.Refresh();
        _objects.SelectedIndex = index + delta;
        RefreshPreview();
    }

    private void Save()
    {
        try { SlideLayout.Validate(_elements); DialogResult = true; }
        catch (ArgumentException error) { ShowError(error.Message); }
    }

    private void ShowError(string message)
    {
        MessageBox.Show(this, message, "Slides", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
