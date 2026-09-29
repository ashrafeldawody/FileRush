using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace FileRush.Core.Models;

public enum QueueCompletionAction
{
    None,
    ExitApplication,
    TurnOffComputer,
    Hibernate,
    Standby
}

public enum ScheduleMode
{
    Daily,
    Once
}

[Flags]
public enum ScheduleDays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    All = Weekdays | Saturday | Sunday
}

public sealed class QueueSchedule
{
    public bool StartEnabled { get; set; }
    public TimeSpan StartTime { get; set; } = new(2, 0, 0);
    public bool StopEnabled { get; set; }
    public TimeSpan StopTime { get; set; } = new(7, 0, 0);
    public ScheduleMode Mode { get; set; } = ScheduleMode.Daily;
    public ScheduleDays Days { get; set; } = ScheduleDays.All;
    public DateTime OnceDate { get; set; } = DateTime.Today;
    public DateTime? LastStarted { get; set; }
    public DateTime? LastStopped { get; set; }

    public QueueSchedule Clone() => (QueueSchedule)MemberwiseClone();
}

public sealed class DownloadQueue : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private bool _isRunning;
    private int _maxConcurrent = 1;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get => _name; set => Set(ref _name, value); }

    public bool IsBuiltIn { get; set; }

    public List<Guid> ItemIds { get; set; } = new();

    public int MaxConcurrent { get => _maxConcurrent; set => Set(ref _maxConcurrent, Math.Max(1, value)); }

    public QueueSchedule Schedule { get; set; } = new();

    public QueueCompletionAction OnComplete { get; set; }

    public bool ForceShutdown { get; set; }

    [JsonIgnore]
    public bool IsRunning { get => _isRunning; set => Set(ref _isRunning, value); }

    [JsonIgnore]
    public DateTime? StartedAt { get; set; }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
