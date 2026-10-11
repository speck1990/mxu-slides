using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;
using Slides.Core;

namespace Slides;

public sealed class SlideLayoutEditor : Window
{
    private readonly List<SlideElement> _elements;
    private readonly Slide _previewSlide;
    private readonly ListBox _objects = new() { Width = 185, DisplayMemberPath = nameof(SlideElement.Name) };
    private readonly SlideSurface _preview = new() { Width = 960, Height = 540 };
    private readonly Canvas _handles = new() { Width = 960, Height = 540, ClipToBounds = true };
    private readonly TextBox _name = Field();
    private readonly TextBox _text = Field();
    private readonly TextBox _x = Field();
    private readonly TextBox _y = Field();
    private readonly TextBox _width = Field();
    private readonly TextBox _height = Field();
    private readonly TextBox _fill = Field();
    private readonly TextBox _corner = Field();
    private readonly TextBlock _imagePath = Label("");
    private readonly ComboBox _imageMode = new();
    private readonly Button _replaceImage;
    private bool _loading;
    private bool _dragging;

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
            Text = "Drag objects to move them. Drag the green corner to resize. Objects lower in the list draw in front.",
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
        actions.Children.Add(Button("+ Ellipse", AddEllipse));
        actions.Children.Add(Button("+ Image", AddImage));
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
        var stage = new Grid { Width = 960, Height = 540 };
        stage.Children.Add(_preview);
        stage.Children.Add(_handles);
        var preview = new Border
        {
            Height = 270, HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brushes.Black, CornerRadius = new CornerRadius(6),
            Child = new Viewbox { Width = 480, Height = 270, Child = stage },
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
        fields.Children.Add(Label("Shape fill (#RRGGBB or #RRGGBBAA)"));
        fields.Children.Add(_fill);
        fields.Children.Add(Label("Corner radius"));
        fields.Children.Add(_corner);
        fields.Children.Add(Label("Image"));
        _imagePath.Margin = new Thickness(0, 4, 0, 8);
        fields.Children.Add(_imagePath);
        _replaceImage = Button("Replace image", ReplaceImage);
        fields.Children.Add(_replaceImage);
        fields.Children.Add(Label("Image scaling"));
        _imageMode.ItemsSource = new[] { "fit", "fill", "stretch" };
        _imageMode.Background = Brushes.White;
        _imageMode.Foreground = Brushes.Black;
        _imageMode.Margin = new Thickness(0, 4, 0, 12);
        fields.Children.Add(_imageMode);
        right.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        layout.Children.Add(columns);
        Content = layout;

        foreach (var field in new[] { _name, _text, _x, _y, _width, _height, _fill, _corner })
            field.TextChanged += (_, _) => UpdateObject();
        _imageMode.SelectionChanged += (_, _) => UpdateObject();
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
        _imagePath.IsEnabled = _imageMode.IsEnabled = _replaceImage.IsEnabled = item?.Kind == "image";
        _name.Text = item?.Name ?? "";
        _text.Text = item?.Text ?? "";
        _x.Text = Number(item?.X);
        _y.Text = Number(item?.Y);
        _width.Text = Number(item?.Width);
        _height.Text = Number(item?.Height);
        _fill.Text = item?.FillColorHex ?? "";
        _corner.Text = Number(item?.CornerRadius);
        _imagePath.Text = item?.Kind == "image" ? Path.GetFileName(item.MediaPath) : "No image selected";
        _imagePath.ToolTip = item?.MediaPath;
        _imageMode.SelectedItem = item?.MediaScaleMode ?? "fit";
        _loading = false;
        if (!_dragging) RefreshHandles();
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
        if (item.Kind == "image" && _imageMode.SelectedItem is string mode)
            item.MediaScaleMode = mode;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        try
        {
            SlideLayout.Validate(_elements);
            _preview.Show(_previewSlide, false);
            if (!_dragging) RefreshHandles();
        }
        catch (ArgumentException) { }
    }

    private void RefreshHandles()
    {
        _handles.Children.Clear();
        foreach (var item in _elements)
        {
            if (!double.IsFinite(item.X) || !double.IsFinite(item.Y) ||
                !double.IsFinite(item.Width) || !double.IsFinite(item.Height)) continue;
            var selected = ReferenceEquals(_objects.SelectedItem, item);
            var move = Handle(Math.Max(12, item.Width), Math.Max(12, item.Height),
                selected ? Brush("#30D158") : Brush("#66FFFFFF"), 2);
            Canvas.SetLeft(move, item.X);
            Canvas.SetTop(move, item.Y);
            move.ToolTip = "Drag to move";
            Thumb? resize = null;
            move.DragStarted += (_, _) =>
            {
                _dragging = true;
                _objects.SelectedItem = item;
            };
            move.DragDelta += (_, args) =>
            {
                Drag(item, args.HorizontalChange, args.VerticalChange, false, move);
                if (resize is not null)
                {
                    Canvas.SetLeft(resize, item.X + item.Width - 9);
                    Canvas.SetTop(resize, item.Y + item.Height - 9);
                }
            };
            move.DragCompleted += (_, _) => { _dragging = false; RefreshPreview(); };
            _handles.Children.Add(move);
            if (!selected) continue;
            resize = Handle(18, 18, Brush("#30D158"), 2);
            Canvas.SetLeft(resize, item.X + item.Width - 9);
            Canvas.SetTop(resize, item.Y + item.Height - 9);
            resize.ToolTip = "Drag to resize";
            resize.DragStarted += (_, _) => _dragging = true;
            resize.DragDelta += (_, args) =>
            {
                Drag(item, args.HorizontalChange, args.VerticalChange, true, resize!);
                move.Width = item.Width;
                move.Height = item.Height;
            };
            resize.DragCompleted += (_, _) => { _dragging = false; RefreshPreview(); };
            _handles.Children.Add(resize);
        }
    }

    private static Thumb Handle(double width, double height, Brush outline, double thickness)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.BorderBrushProperty, outline);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(thickness));
        return new Thumb
        {
            Width = width, Height = height,
            Template = new ControlTemplate(typeof(Thumb)) { VisualTree = border }
        };
    }

    private void Drag(SlideElement item, double dx, double dy, bool resizing, Thumb handle)
    {
        if (resizing)
        {
            SlideLayout.Resize(item, dx, dy);
            Canvas.SetLeft(handle, item.X + item.Width - 9);
            Canvas.SetTop(handle, item.Y + item.Height - 9);
        }
        else
        {
            SlideLayout.Translate(item, dx, dy);
            Canvas.SetLeft(handle, item.X);
            Canvas.SetTop(handle, item.Y);
        }
        _loading = true;
        _x.Text = Number(item.X);
        _y.Text = Number(item.Y);
        _width.Text = Number(item.Width);
        _height.Text = Number(item.Height);
        _loading = false;
        _preview.Show(_previewSlide, false);
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

    private void AddEllipse()
    {
        var item = SlideLayout.AddEllipse(_elements);
        _objects.Items.Refresh();
        _objects.SelectedItem = item;
        RefreshPreview();
    }

    private void AddImage()
    {
        var path = PickImage();
        if (path is null) return;
        try
        {
            var item = SlideLayout.AddImage(_elements, path);
            _objects.Items.Refresh();
            _objects.SelectedItem = item;
            RefreshPreview();
        }
        catch (Exception error) when (error is ArgumentException or FileNotFoundException) { ShowError(error.Message); }
    }

    private void ReplaceImage()
    {
        if (_objects.SelectedItem is not SlideElement { Kind: "image" } item) return;
        var path = PickImage();
        if (path is null) return;
        if (!SlideLayout.IsImage(path)) { ShowError("Choose a PNG, JPEG, BMP, or GIF image."); return; }
        item.MediaPath = Path.GetFullPath(path);
        LoadSelected();
        RefreshPreview();
    }

    private string? PickImage()
    {
        var picker = new OpenFileDialog
        {
            Title = "Choose a slide image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
        };
        return picker.ShowDialog(this) == true ? picker.FileName : null;
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
