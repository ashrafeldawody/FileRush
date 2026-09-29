using System.Collections.ObjectModel;
using System.Windows;
using FileRush.App.Infrastructure;
using FileRush.Core.Services;
using FileRush.Core.Utils;

namespace FileRush.App.Dialogs;

public partial class GrabberWindow : Window
{
    private readonly ObservableCollection<string> _results = new();
    private CancellationTokenSource? _cts;

    public GrabberWindow(string? startUrl = null)
    {
        InitializeComponent();
        AppIcons.Apply(this);
        ResultList.ItemsSource = _results;
        UrlBox.Text = startUrl ?? string.Empty;
        Closed += (_, _) => _cts?.Cancel();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlUtils.NormalizeUrl(UrlBox.Text);
        if (!UrlUtils.IsDownloadableUrl(url, out _))
        {
            MessageBox.Show(this, "Enter a valid start page address.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        int.TryParse(MaxPagesBox.Text.Trim(), out var maxPages);
        var options = new SiteGrabberOptions
        {
            StartUrl = url,
            MaxDepth = Math.Max(0, DepthBox.SelectedIndex),
            MaxPages = Math.Clamp(maxPages <= 0 ? 200 : maxPages, 1, 5000),
            SameHostOnly = SameHostCheck.IsChecked == true,
            Extensions = TypesBox.Text.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim().TrimStart('.')).ToHashSet(StringComparer.OrdinalIgnoreCase),
            UserAgent = App.Services.Settings.UserAgent
        };
        _results.Clear();
        _cts = new CancellationTokenSource();
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        DownloadButton.IsEnabled = false;
        var progress = new Progress<SiteGrabberProgress>(p => ProgressText.Text = $"Pages: {p.PagesScanned}   Files: {p.FilesFound}   {p.CurrentUrl}");
        try
        {
            var files = await new SiteGrabber().GrabAsync(options, progress, _cts.Token);
            foreach (var file in files) _results.Add(file);
            ProgressText.Text = $"Done. {files.Count} file(s) found.";
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = "Stopped.";
        }
        catch (Exception ex)
        {
            ProgressText.Text = "Error: " + ex.Message;
        }
        finally
        {
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            DownloadButton.IsEnabled = _results.Count > 0;
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        var picker = new UrlListWindow("Site Grabber - download files", _results, UrlUtils.NormalizeUrl(UrlBox.Text)) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            ProgressText.Text = $"{picker.AddedCount} file(s) added to the download list.";
        }
    }
}
