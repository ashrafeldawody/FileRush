using System.Windows;
using FileRush.App.Infrastructure;
using FileRush.Core.Utils;

namespace FileRush.App.Dialogs;

public partial class AddUrlWindow : Window
{
    private static readonly List<string> History = new();

    public AddUrlWindow(string? initialUrl = null)
    {
        InitializeComponent();
        AppIcons.Apply(this);
        foreach (var url in History) UrlBox.Items.Add(url);
        var candidate = initialUrl;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            try
            {
                var text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
                if (UrlUtils.IsDownloadableUrl(text, out var uri)) candidate = uri.AbsoluteUri;
            }
            catch
            {
            }
        }
        UrlBox.Text = candidate ?? string.Empty;
        Loaded += (_, _) => UrlBox.Focus();
    }

    public string Url { get; private set; } = string.Empty;

    public string? Username { get; private set; }

    public string? Password { get; private set; }

    private void AuthCheck_Changed(object sender, RoutedEventArgs e)
    {
        AuthPanel.IsEnabled = AuthCheck.IsChecked == true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var text = UrlBox.Text.Trim();
        if (text.Length == 0)
        {
            MessageBox.Show(this, "Please enter an address to download.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var normalized = UrlUtils.NormalizeUrl(text);
        if (!UrlUtils.IsDownloadableUrl(normalized, out var uri))
        {
            MessageBox.Show(this, "The address is not a valid HTTP, HTTPS or FTP URL.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Url = uri.AbsoluteUri;
        if (AuthCheck.IsChecked == true)
        {
            Username = LoginBox.Text;
            Password = PasswordBox.Password;
        }
        History.Remove(Url);
        History.Insert(0, Url);
        if (History.Count > 20) History.RemoveAt(History.Count - 1);
        DialogResult = true;
    }
}
