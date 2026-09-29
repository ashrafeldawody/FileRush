using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using FileRush.Core.Models;
using FileRush.Core.Services;

namespace FileRush.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly DownloadManager _manager;
    private readonly DispatcherTimer _refreshTimer;
    private CategoryNode? _selectedNode;
    private string _sortProperty = string.Empty;
    private bool _sortDescending;
    private bool _refreshPending;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(DownloadManager manager)
    {
        _manager = manager;
        View = new ListCollectionView(manager.Items) { Filter = FilterItem };
        Nodes = new ObservableCollection<CategoryNode>();
        BuildTree();
        foreach (var item in manager.Items) Attach(item);
        manager.Items.CollectionChanged += OnItemsChanged;
        manager.Queues.CollectionChanged += (_, _) => BuildTree();
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            if (_refreshPending)
            {
                _refreshPending = false;
                View.Refresh();
            }
        };
    }

    public ListCollectionView View { get; }

    public ObservableCollection<CategoryNode> Nodes { get; }

    public CategoryNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            _selectedNode = value;
            OnPropertyChanged(nameof(SelectedNode));
            View.Refresh();
        }
    }

    public string SortProperty => _sortProperty;

    public bool SortDescending => _sortDescending;

    public void SortBy(string property, bool? descending = null)
    {
        if (string.IsNullOrEmpty(property)) return;
        if (descending is null)
        {
            _sortDescending = _sortProperty == property && !_sortDescending;
        }
        else
        {
            _sortDescending = descending.Value;
        }
        _sortProperty = property;
        View.SortDescriptions.Clear();
        View.SortDescriptions.Add(new SortDescription(property, _sortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending));
        OnPropertyChanged(nameof(SortProperty));
        OnPropertyChanged(nameof(SortDescending));
    }

    public void BuildTree()
    {
        var selectedKind = _selectedNode?.Kind;
        var selectedName = _selectedNode?.Name;
        Nodes.Clear();
        var all = new CategoryNode { Name = "All downloads", Kind = NodeKind.AllDownloads, Glyph = "", GlyphBrush = Brushes.Goldenrod };
        foreach (var category in _manager.Settings.Categories)
        {
            all.Children.Add(new CategoryNode
            {
                Name = category.Name,
                Kind = NodeKind.Category,
                Category = category,
                Glyph = GlyphFor(category.Name),
                GlyphBrush = BrushFor(category.Name)
            });
        }
        Nodes.Add(all);
        Nodes.Add(new CategoryNode { Name = "Unfinished", Kind = NodeKind.Unfinished, Glyph = "", GlyphBrush = Brushes.SteelBlue });
        Nodes.Add(new CategoryNode { Name = "Finished", Kind = NodeKind.Finished, Glyph = "", GlyphBrush = Brushes.ForestGreen });
        Nodes.Add(new CategoryNode { Name = "Grabber Projects", Kind = NodeKind.Grabber, Glyph = "", GlyphBrush = Brushes.DarkOrange });
        var queues = new CategoryNode { Name = "Queues", Kind = NodeKind.Queues, Glyph = "", GlyphBrush = Brushes.SlateGray };
        foreach (var queue in _manager.Queues)
        {
            queues.Children.Add(new CategoryNode { Name = queue.Name, Kind = NodeKind.Queue, Queue = queue, Glyph = "", GlyphBrush = Brushes.MediumPurple });
        }
        Nodes.Add(queues);
        CategoryNode? reselect = null;
        foreach (var node in Flatten(Nodes))
        {
            if (node.Kind == selectedKind && node.Name == selectedName) reselect = node;
        }
        reselect ??= all;
        reselect.IsSelected = true;
        _selectedNode = reselect;
        OnPropertyChanged(nameof(SelectedNode));
        View.Refresh();
    }

    public static IEnumerable<CategoryNode> Flatten(IEnumerable<CategoryNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }

    private static string GlyphFor(string category) => category.ToLowerInvariant() switch
    {
        "compressed" => "",
        "documents" => "",
        "music" => "",
        "programs" => "",
        "video" => "",
        _ => ""
    };

    private static Brush BrushFor(string category) => category.ToLowerInvariant() switch
    {
        "compressed" => Brushes.Peru,
        "documents" => Brushes.RoyalBlue,
        "music" => Brushes.MediumVioletRed,
        "programs" => Brushes.DarkSlateBlue,
        "video" => Brushes.Crimson,
        _ => Brushes.Goldenrod
    };

    private bool FilterItem(object obj)
    {
        if (obj is not DownloadItem item) return false;
        return _selectedNode?.Matches(item) ?? true;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null) foreach (DownloadItem item in e.NewItems) Attach(item);
        if (e.OldItems is not null) foreach (DownloadItem item in e.OldItems) item.PropertyChanged -= OnItemPropertyChanged;
    }

    private void Attach(DownloadItem item)
    {
        item.PropertyChanged -= OnItemPropertyChanged;
        item.PropertyChanged += OnItemPropertyChanged;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadItem.Status) or nameof(DownloadItem.Category) or nameof(DownloadItem.QueueId))
        {
            _refreshPending = true;
            var dispatcher = _refreshTimer.Dispatcher;
            if (dispatcher.CheckAccess())
            {
                if (!_refreshTimer.IsEnabled) _refreshTimer.Start();
            }
            else
            {
                dispatcher.BeginInvoke(() => { if (!_refreshTimer.IsEnabled) _refreshTimer.Start(); });
            }
        }
    }

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
