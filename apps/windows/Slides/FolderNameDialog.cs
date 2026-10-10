using System.Windows;
using System.Windows.Controls;

namespace Slides;

public sealed class FolderNameDialog : Window
{
    private readonly TextBox _name = new() { Margin = new Thickness(0, 8, 0, 16), MinWidth = 260 };

    public string FolderName => _name.Text.Trim();

    public FolderNameDialog(string title, string initialName = "")
    {
        Title = title;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.Black;
        Foreground = System.Windows.Media.Brushes.White;
        _name.Text = initialName;

        var content = new StackPanel { Margin = new Thickness(18) };
        content.Children.Add(new TextBlock { Text = "Folder name" });
        content.Children.Add(_name);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 6), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => Close();
        var save = new Button { Content = "Save", Padding = new Thickness(12, 6) };
        save.Click += (_, _) =>
        {
            if (FolderName.Length == 0)
            {
                MessageBox.Show(this, "Give the folder a name.", "Slides");
                return;
            }
            DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        content.Children.Add(buttons);
        Content = content;
        Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }
}
