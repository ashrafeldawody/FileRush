using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FileRush.App.Controls;
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
    public string Route { get; init; } = string.Empty;
    public string Part { get; init; } = string.Empty;
    public string Info { get; init; } = string.Empty;
}

public sealed class PartRow
{
    public int Number { get; init; }
    public string Range { get; init; } = string.Empty;
    public string Size { get; init; } = string.Empty;
    public string Done { get; init; } = string.Empty;
    public string Connection { get; init; } = string.Empty;
    public string Route { get; init; } = string.Empty;
}

public sealed record LegendEntry(Brush Brush, string Text);

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
        var pieces = job?.GetPieces();
        var routeBrushes = job is null ? null : BuildRouteBrushes(job);
        Bar.TotalSize = _item.TotalSize;
        Bar.Segments = segments;
        Bar.Pieces = pieces;
        Bar.RouteBrushes = routeBrushes;
        Bar.IndeterminateBytes = _item.DownloadedBytes;
        Legend.ItemsSource = pieces is not null && routeBrushes is not null && _item.TotalSize > 0
            ? BuildLegend(pieces, routeBrushes, segments.Sum(s => Math.Max(0, s.Downloaded)))
            : null;

        var rejectedRoutes = job?.GetRejectedRoutes() ?? Array.Empty<string>();
        RejectedWarning.Visibility = rejectedRoutes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RejectedWarningText.Text = rejectedRoutes.Count > 0 ? BuildRejectedWarning(rejectedRoutes, job!) : string.Empty;

        if (_detailsVisible)
        {
            ElapsedText.Text = job is not null ? "Elapsed: " + ByteFormatter.FormatTime(job.Elapsed) : string.Empty;
            ConnectionsText.Text = job is not null ? $"Connections: {_item.ActiveConnections} of {job.Connections.Count}" : string.Empty;
            var ordered = segments.OrderBy(s => s.Start).ToList();
            var partByConnection = ordered
                .Select((s, i) => (Segment: s, Number: i + 1))
                .Where(p => p.Segment.Connection > 0)
                .ToDictionary(p => p.Segment.Connection, p => p.Number);
            ConnectionList.ItemsSource = job?.Connections.Select(c => new ConnectionRow
            {
                Number = c.Number,
                Downloaded = c.Downloaded,
                Route = RouteName(c.Route),
                Part = partByConnection.TryGetValue(c.Number, out var part) ? part.ToString() : "-",
                Info = c.Info
            }).ToList() ?? new List<ConnectionRow>();
            PartList.ItemsSource = ordered.Select((s, i) => BuildPartRow(s, i + 1, pieces)).ToList();
        }

        PauseButton.Content = _item.IsActive ? "Pause" : "Resume";
        PauseButton.IsEnabled = _item.Status != DownloadStatus.Completed;
        if (_item.Status == DownloadStatus.Completed)
        {
            CancelButton.Content = "Close";
            AfterBox.IsEnabled = false;
        }
    }

    private static string RouteName(string route) => string.IsNullOrEmpty(route) ? "Default" : route;

    private static string BuildRejectedWarning(IReadOnlyList<string> rejectedRoutes, DownloadJob job)
    {
        var refused = string.Join(" and ", rejectedRoutes);
        var working = job.Connections.Select(c => c.Route).Distinct().Where(r => !rejectedRoutes.Contains(r)).ToList();
        var continuing = working.Count > 0 ? $" The download continues over {string.Join(" and ", working)} only." : string.Empty;
        return $"⚠ This server refused {refused}. The download link is probably locked to the IP address that opened it, so combining adapters cannot speed up this file.{continuing}";
    }

    private static Dictionary<string, Brush> BuildRouteBrushes(DownloadJob job)
    {
        var brushes = new Dictionary<string, Brush>();
        foreach (var route in job.Connections.Select(c => c.Route).Distinct())
        {
            brushes[route] = SegmentBar.RoutePalette[brushes.Count % SegmentBar.RoutePalette.Count];
        }
        return brushes;
    }

    private List<LegendEntry> BuildLegend(IReadOnlyList<DownloadPiece> pieces, Dictionary<string, Brush> routeBrushes, long downloaded)
    {
        var total = _item.TotalSize;
        var entries = routeBrushes.Select(r =>
        {
            var bytes = pieces.Where(p => p.Route == r.Key).Sum(p => p.Length);
            return new LegendEntry(r.Value, $"{RouteName(r.Key)}: {ByteFormatter.Format(bytes)} ({bytes * 100.0 / total:F1} %)");
        }).ToList();
        var earlier = downloaded - pieces.Sum(p => p.Length);
        if (earlier > 0)
        {
            entries.Add(new LegendEntry(SegmentBar.PreviousSession, $"Before resume: {ByteFormatter.Format(earlier)}"));
        }
        return entries;
    }

    private static PartRow BuildPartRow(SegmentSnapshot segment, int number, IReadOnlyList<DownloadPiece>? pieces)
    {
        var size = segment.End >= 0 ? segment.End - segment.Start + 1 : -1;
        string route;
        if (segment.Connection > 0)
        {
            route = RouteName(segment.Route);
        }
        else
        {
            var routes = pieces?
                .Where(p => p.Start <= (segment.End < 0 ? long.MaxValue : segment.End) && p.End >= segment.Start)
                .Select(p => RouteName(p.Route))
                .Distinct()
                .ToList() ?? new List<string>();
            route = routes.Count > 0 ? string.Join(" + ", routes) : segment.Downloaded > 0 ? "Before resume" : "-";
        }
        return new PartRow
        {
            Number = number,
            Range = $"{ByteFormatter.Format(segment.Start)} - {(segment.End >= 0 ? ByteFormatter.Format(segment.End + 1) : "end")}",
            Size = size >= 0 ? ByteFormatter.Format(size) : "Unknown",
            Done = size > 0 ? $"{Math.Min(100, segment.Downloaded * 100.0 / size):F0} %" : ByteFormatter.Format(segment.Downloaded),
            Connection = segment.Connection > 0 ? $"#{segment.Connection}" : segment.IsComplete ? "Done" : "Waiting",
            Route = route
        };
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
