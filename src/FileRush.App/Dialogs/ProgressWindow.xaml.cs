using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FileRush.App.Infrastructure;
using FileRush.Core.Engine;
using FileRush.Core.Models;
using FileRush.Core.Services;
using FileRush.Core.Utils;

namespace FileRush.App.Dialogs;

public enum PostDownloadAction
{
    Nothing,
    OpenFile,
    OpenFolder,
    Exit,
    Shutdown
}

public sealed class ConnectionRow
{
    public int Number { get; init; }
    public long Downloaded { get; init; }
    public string Info { get; init; } = string.Empty;
}

public partial class ProgressWindow : Window
{
    public static readonly ConcurrentDictionary<Guid, PostDownloadAction> PostActions = new();

    private readonly DownloadItem _item;
    private readonly DownloadManager _manager = App.Services.Manager;
    private readonly DispatcherTimer _timer;
    private bool _detailsVisible;

    public ProgressWindow(DownloadItem item)
    {
        InitializeComponent();
        AppIcons.Apply(this);
        _item = item;
        UrlText.Text = item.Url;
        UrlText.ToolTip = item.Url;
        if (PostActions.TryGetValue(item.Id, out var action)) AfterBox.SelectedIndex = (int)action;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            Refresh();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    public DownloadItem Item => _item;

    private void Refresh()
    {
        var job = _manager.GetJob(_item);
        var percent = _item.TotalSize > 0 ? _item.Progress : 0;
        Title = _item.TotalSize > 0 ? $"{percent:F1}% {_item.FileName}" : _item.FileName;
        StatusText.Text = _item.StatusText;
        SizeText.Text = _item.TotalSize >= 0 ? ByteFormatter.Format(_item.TotalSize) : "Unknown";
        DownloadedText.Text = _item.TotalSize > 0
            ? $"{ByteFormatter.Format(_item.DownloadedBytes)} ({percent:F2} %)"
            : ByteFormatter.Format(_item.DownloadedBytes);
        SpeedText.Text = _item.IsActive ? ByteFormatter.FormatSpeed(_item.Speed) : string.Empty;
        TimeLeftText.Text = _item.IsActive ? ByteFormatter.FormatTime(_item.TimeLeft) : string.Empty;
        ResumeText.Text = _item.Status is DownloadStatus.Pending or DownloadStatus.Connecting ? "Checking..." : (_item.SupportsResume ? "Yes" : "No");
        SavePathText.Text = "Save to: " + _item.FullPath;
        SavePathText.ToolTip = _item.FullPath;

        IReadOnlyList<SegmentSnapshot> segments = job?.GetSegments() ?? _item.Segments.Select(s => new SegmentSnapshot(s.Start, s.End, s.Position, false)).ToList();
        if (_item.Status == DownloadStatus.Completed && _item.TotalSize > 0)
        {
            segments = new[] { new SegmentSnapshot(0, _item.TotalSize - 1, _item.TotalSize, false) };
        }
        Bar.TotalSize = _item.TotalSize;
        Bar.Segments = segments;
        Bar.IndeterminateBytes = _item.DownloadedBytes;

        if (_detailsVisible)
        {
            ElapsedText.Text = job is not null ? "Elapsed: " + ByteFormatter.FormatTime(job.Elapsed) : string.Empty;
            ConnectionsText.Text = job is not null ? $"Connections: {_item.ActiveConnections} of {job.Connections.Count}" : string.Empty;
            var rows = job?.Connections.Select(c => new ConnectionRow { Number = c.Number, Downloaded = c.Downloaded, Info = c.Info }).ToList() ?? new List<ConnectionRow>();
            ConnectionList.ItemsSource = rows;
        }

        PauseButton.Content = _item.IsActive ? "Pause" : "Resume";
        PauseButton.IsEnabled = _item.Status != DownloadStatus.Completed;
        if (_item.Status == DownloadStatus.Completed)
        {
            CancelButton.Content = "Close";
            AfterBox.IsEnabled = false;
        }
    }

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        _detailsVisible = !_detailsVisible;
        DetailsPanel.Visibility = _detailsVisible ? Visibility.Visible : Visibility.Collapsed;
        DetailsButton.Content = _detailsVisible ? "Hide details <<" : "Show details >>";
        Refresh();
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_item.IsActive) _manager.Pause(_item);
        else if (_item.CanResume) _manager.Start(_item);
        Refresh();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_item.IsActive) _manager.Pause(_item);
        Close();
    }

    private void AfterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_item is null) return;
        var action = (PostDownloadAction)Math.Max(0, AfterBox.SelectedIndex);
        if (action == PostDownloadAction.Nothing) PostActions.TryRemove(_item.Id, out _);
        else PostActions[_item.Id] = action;
    }
}
