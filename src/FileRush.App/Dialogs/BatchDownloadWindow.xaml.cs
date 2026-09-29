using System.Windows;
using FileRush.App.Infrastructure;
using FileRush.Core.Services;
using FileRush.Core.Utils;

namespace FileRush.App.Dialogs;

public partial class BatchDownloadWindow : Window
{
    private IReadOnlyList<string> _urls = Array.Empty<string>();

    public BatchDownloadWindow(string? pattern = null)
    {
        InitializeComponent();
        AppIcons.Apply(this);
        PatternBox.Text = pattern ?? string.Empty;
        Loaded += (_, _) =>
        {
            PatternBox.Focus();
            UpdatePreview();
        };
    }

    public IReadOnlyList<string> Urls => _urls;

    private void Input_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) UpdatePreview();
    }

    private void UpdatePreview()
    {
        var pattern = PatternBox.Text.Trim();
        if (pattern.Length == 0)
        {
            _urls = Array.Empty<string>();
            PreviewList.ItemsSource = null;
            PreviewHeader.Text = "Preview:";
            return;
        }
        int.TryParse(DigitsBox.Text.Trim(), out var digits);
        var expanded = pattern.Contains('[')
            ? BatchUrlExpander.ExpandBrackets(pattern)
            : BatchUrlExpander.Expand(pattern, FromBox.Text.Trim(), ToBox.Text.Trim(), LettersRadio.IsChecked == true, digits);
        _urls = expanded.Where(u => UrlUtils.IsDownloadableUrl(UrlUtils.NormalizeUrl(u), out _)).Select(UrlUtils.NormalizeUrl).ToList();
        PreviewList.ItemsSource = _urls.Take(200).ToList();
        PreviewHeader.Text = _urls.Count > 200 ? $"Preview (first 200 of {_urls.Count}):" : $"Preview ({_urls.Count} addresses):";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        UpdatePreview();
        if (_urls.Count == 0)
        {
            MessageBox.Show(this, "The pattern does not produce any valid addresses.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}
