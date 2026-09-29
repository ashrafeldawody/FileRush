using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using FileRush.Core.Models;

namespace FileRush.App.ViewModels;

public enum NodeKind
{
    AllDownloads,
    Category,
    Unfinished,
    Finished,
    Grabber,
    Queues,
    Queue
}

public sealed class CategoryNode : INotifyPropertyChanged
{
    private bool _isExpanded = true;
    private bool _isSelected;
    private string _name = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }

    public string Glyph { get; init; } = "";

    public Brush GlyphBrush { get; init; } = Brushes.Goldenrod;

    public NodeKind Kind { get; init; }

    public Category? Category { get; init; }

    public DownloadQueue? Queue { get; init; }

    public ObservableCollection<CategoryNode> Children { get; } = new();

    public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnPropertyChanged(); } }

    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }

    public bool Matches(DownloadItem item)
    {
        return Kind switch
        {
            NodeKind.AllDownloads => true,
            NodeKind.Category => string.Equals(item.Category, Name, StringComparison.OrdinalIgnoreCase),
            NodeKind.Unfinished => !item.IsCompleted,
            NodeKind.Finished => item.IsCompleted,
            NodeKind.Queues => item.IsInQueue,
            NodeKind.Queue => Queue is not null && item.QueueId == Queue.Id,
            _ => false
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
