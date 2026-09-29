using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace FileRush.Core.Models;

public sealed class DownloadItem : INotifyPropertyChanged
{
    private string _url = string.Empty;
    private string _fileName = string.Empty;
    private string _saveDirectory = string.Empty;
    private string _category = "General";
    private string _description = string.Empty;
    private Guid? _queueId;
    private DownloadStatus _status = DownloadStatus.Pending;
    private long _totalSize = -1;
    private long _downloadedBytes;
    private bool _supportsResume;
    private string? _errorMessage;
    private DateTime? _lastTryAt;
    private DateTime? _completedAt;
    private double _speed;
    private TimeSpan? _timeLeft;
    private string _statusText = string.Empty;
    private int _activeConnections;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Url { get => _url; set => Set(ref _url, value); }

    public string? FinalUrl { get; set; }

    public string? Referrer { get; set; }

    public string? UserAgent { get; set; }

    public Dictionary<string, string> Headers { get; set; } = new();

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string FileName { get => _fileName; set { if (Set(ref _fileName, value)) OnPropertyChanged(nameof(FullPath)); } }

    public string SaveDirectory { get => _saveDirectory; set { if (Set(ref _saveDirectory, value)) OnPropertyChanged(nameof(FullPath)); } }

    [JsonIgnore]
    public string FullPath => Path.Combine(SaveDirectory, FileName);

    public string Category { get => _category; set => Set(ref _category, value); }

    public string Description { get => _description; set => Set(ref _description, value); }

    public Guid? QueueId { get => _queueId; set { if (Set(ref _queueId, value)) OnPropertyChanged(nameof(IsInQueue)); } }

    [JsonIgnore]
    public bool IsInQueue => _queueId.HasValue;

    public DownloadStatus Status
    {
        get => _status;
        set
        {
            if (Set(ref _status, value))
            {
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(CanResume));
                OnPropertyChanged(nameof(Progress));
            }
        }
    }

    [JsonIgnore]
    public bool IsCompleted => _status == DownloadStatus.Completed;

    [JsonIgnore]
    public bool IsActive => _status is DownloadStatus.Connecting or DownloadStatus.Downloading or DownloadStatus.Queued;

    [JsonIgnore]
    public bool CanResume => _status is DownloadStatus.Pending or DownloadStatus.Paused or DownloadStatus.Error;

    public long TotalSize { get => _totalSize; set { if (Set(ref _totalSize, value)) OnPropertyChanged(nameof(Progress)); } }

    public long DownloadedBytes { get => _downloadedBytes; set { if (Set(ref _downloadedBytes, value)) OnPropertyChanged(nameof(Progress)); } }

    [JsonIgnore]
    public double Progress => _totalSize > 0 ? Math.Clamp(_downloadedBytes * 100.0 / _totalSize, 0, 100) : (_status == DownloadStatus.Completed ? 100 : 0);

    public bool SupportsResume { get => _supportsResume; set => Set(ref _supportsResume, value); }

    public string? ETag { get; set; }

    public string? LastModified { get; set; }

    public string? ContentType { get; set; }

    public string? PartialPath { get; set; }

    public List<Segment> Segments { get; set; } = new();

    public int MaxConnections { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.Now;

    public DateTime? LastTryAt { get => _lastTryAt; set => Set(ref _lastTryAt, value); }

    public DateTime? CompletedAt { get => _completedAt; set => Set(ref _completedAt, value); }

    public string? ErrorMessage { get => _errorMessage; set => Set(ref _errorMessage, value); }

    public bool VirusChecked { get; set; }

    [JsonIgnore]
    public double Speed { get => _speed; set => Set(ref _speed, value); }

    [JsonIgnore]
    public TimeSpan? TimeLeft { get => _timeLeft; set => Set(ref _timeLeft, value); }

    [JsonIgnore]
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    [JsonIgnore]
    public int ActiveConnections { get => _activeConnections; set => Set(ref _activeConnections, value); }

    public void ResetProgress()
    {
        Segments.Clear();
        DownloadedBytes = 0;
        ETag = null;
        LastModified = null;
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
