using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Slides.Core;

namespace Slides;

internal sealed class SongSelectWindow : Window
{
    private readonly TextBox _clientId = new() { Text = Environment.GetEnvironmentVariable("SLIDES_CCLI_CLIENT_ID") ?? "" };
    private readonly PasswordBox _key = new();
    private readonly PasswordBox _secret = new();
    private readonly TextBox _query = new();
    private readonly ListBox _results = new() { Height = 280 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private SongSelectClient? _client;
    internal Presentation? ImportedSong { get; private set; }

    internal SongSelectWindow()
    {
        Title = "Import from SongSelect";
        Width = 650;
        Height = 610;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _key.Password = Environment.GetEnvironmentVariable("SLIDES_CCLI_SUBSCRIPTION_KEY") ?? "";
        _secret.Password = Environment.GetEnvironmentVariable("SLIDES_CCLI_CLIENT_SECRET") ?? "";
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "CCLI partner client ID" });
        panel.Children.Add(_clientId);
        panel.Children.Add(new TextBlock { Text = "CCLI partner subscription key", Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(_key);
        panel.Children.Add(new TextBlock { Text = "Client secret, if CCLI issued one", Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(_secret);
        panel.Children.Add(new TextBlock { Text = "Register http://127.0.0.1:8765/callback/ as the OAuth redirect URI with CCLI.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        var signIn = new Button { Content = "Sign in with SongSelect", Margin = new Thickness(0, 8, 0, 0) };
        signIn.Click += async (_, _) =>
        {
            signIn.IsEnabled = false;
            try
            {
                _client?.Dispose();
                _client = new SongSelectClient(_clientId.Text, _key.Password, _secret.Password);
                _status.Text = "Complete sign-in in your browser…";
                await _client.SignInAsync(CancellationToken.None);
                _status.Text = "Signed in. Search by title or CCLI song number.";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { signIn.IsEnabled = true; }
        };
        panel.Children.Add(signIn);
        panel.Children.Add(new TextBlock { Text = "Song title or CCLI number", Margin = new Thickness(0, 10, 0, 0) });
        panel.Children.Add(_query);
        var search = new Button { Content = "Search", Margin = new Thickness(0, 8, 0, 0) };
        search.Click += async (_, _) =>
        {
            search.IsEnabled = false;
            try
            {
                if (_client is null) throw new InvalidOperationException("Sign in first.");
                _status.Text = "Searching…";
                _results.ItemsSource = await _client.SearchAsync(_query.Text, CancellationToken.None);
                _status.Text = $"{_results.Items.Count} songs found. Select one to import.";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { search.IsEnabled = true; }
        };
        panel.Children.Add(search);
        panel.Children.Add(_results);
        var import = new Button { Content = "Import selected song", Margin = new Thickness(0, 8, 0, 0) };
        import.Click += async (_, _) =>
        {
            import.IsEnabled = false;
            try
            {
                if (_client is null) throw new InvalidOperationException("Sign in first.");
                if (_results.SelectedItem is not SongSelectResult song) throw new InvalidOperationException("Select a song.");
                if (!song.CanImport) throw new InvalidOperationException("Lyrics are unavailable for this account.");
                _status.Text = "Importing…";
                ImportedSong = await _client.ImportAsync(song.Id, CancellationToken.None);
                DialogResult = true;
            }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { import.IsEnabled = true; }
        };
        panel.Children.Add(import);
        panel.Children.Add(_status);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Closed += (_, _) => _client?.Dispose();
    }
}
