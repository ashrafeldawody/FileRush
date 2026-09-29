using System.Collections.ObjectModel;
using System.Net;
using FileRush.Core.Engine;
using FileRush.Core.Models;
using FileRush.Core.Utils;

namespace FileRush.Core.Services;

public sealed class DownloadManager : IDisposable
{
    public const string MainQueueName = "Main download queue";
    public const string SyncQueueName = "Synchronization queue";

    private readonly Dictionary<Guid, DownloadJob> _jobs = new();
    private readonly DownloadStore _store;
    private readonly IRangeSourceFactory _factory;
    private readonly Timer _saveTimer;
    private readonly Timer _schedulerTimer;
    private readonly object _saveGate = new();
    private int _savePending;
    private bool _disposed;

    public DownloadManager(AppSettings settings, DownloadStore store, IRangeSourceFactory? factory = null)
    {
        Settings = settings;
        _store = store;
        _factory = factory ?? new RangeSourceFactory(ResolveSiteLogin);
        _saveTimer = new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
        _schedulerTimer = new Timer(_ => Dispatch(() => SchedulerTick(DateTime.Now)), null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));
        ApplySettings(settings);
    }

    public AppSettings Settings { get; private set; }

    public ObservableCollection<DownloadItem> Items { get; } = new();

    public ObservableCollection<DownloadQueue> Queues { get; } = new();

    public SpeedLimiter Limiter { get; } = new();

    public Action<Action> Dispatch { get; set; } = action => action();

    public int ActiveJobCount => _jobs.Count;

    public event EventHandler<DownloadItem>? DownloadStarted;

    public event EventHandler<DownloadItem>? DownloadCompleted;

    public event EventHandler<DownloadItem>? DownloadFailed;

    public event EventHandler<DownloadItem>? ItemAdded;

    public event EventHandler<DownloadItem>? ItemRemoved;

    public event EventHandler<DownloadQueue>? QueueFinished;

    public event EventHandler? StateChanged;

    public void Load()
    {
        var data = _store.Load();
        Items.Clear();
        Queues.Clear();
        foreach (var item in data.Items)
        {
            if (item.Status is DownloadStatus.Connecting or DownloadStatus.Downloading or DownloadStatus.Queued)
            {
                item.Status = DownloadStatus.Paused;
                item.StatusText = "Paused";
            }
            else if (item.Status == DownloadStatus.Completed)
            {
                item.StatusText = "Complete";
            }
            else if (item.Status == DownloadStatus.Error)
            {
                item.StatusText = "Error: " + item.ErrorMessage;
            }
            else if (item.Status == DownloadStatus.Paused)
            {
                item.StatusText = "Paused";
            }
            if (item.TotalSize > 0 && item.Status != DownloadStatus.Completed)
            {
                item.DownloadedBytes = item.Segments.Sum(s => s.Downloaded);
            }
            Items.Add(item);
        }
        foreach (var queue in data.Queues)
        {
            queue.ItemIds.RemoveAll(id => Items.All(i => i.Id != id));
            Queues.Add(queue);
        }
        EnsureDefaultQueues();
        foreach (var item in Items)
        {
            if (item.QueueId is { } qid && Queues.All(q => q.Id != qid)) item.QueueId = null;
        }
    }

    public void EnsureDefaultQueues()
    {
        if (Queues.All(q => q.Name != MainQueueName))
        {
            Queues.Insert(0, new DownloadQueue { Name = MainQueueName, IsBuiltIn = true, MaxConcurrent = 1 });
        }
        if (Queues.All(q => q.Name != SyncQueueName))
        {
            Queues.Add(new DownloadQueue { Name = SyncQueueName, IsBuiltIn = true, MaxConcurrent = 1 });
        }
    }

    public DownloadQueue MainQueue => Queues.First(q => q.Name == MainQueueName);

    public void ApplySettings(AppSettings settings)
    {
        Settings = settings;
        settings.EnsureCategories();
        Limiter.BytesPerSecond = settings.SpeedLimit.Enabled ? settings.SpeedLimit.MaxKilobytesPerSecond * 1024L : 0;
        PumpWaiting();
    }

    public void SetSpeedLimit(bool enabled, int kilobytesPerSecond)
    {
        Settings.SpeedLimit.Enabled = enabled;
        Settings.SpeedLimit.MaxKilobytesPerSecond = Math.Max(1, kilobytesPerSecond);
        Limiter.BytesPerSecond = enabled ? Settings.SpeedLimit.MaxKilobytesPerSecond * 1024L : 0;
    }

    public DownloadEngineOptions BuildOptions(DownloadItem item)
    {
        var s = Settings;
        var options = new DownloadEngineOptions
        {
            MaxConnections = Math.Clamp(s.MaxConnectionsPerFile, 1, 32),
            Timeout = TimeSpan.FromSeconds(Math.Clamp(s.TimeoutSeconds, 5, 600)),
            MaxRetries = Math.Clamp(s.MaxRetries, 0, 999),
            RetryDelay = TimeSpan.FromSeconds(Math.Clamp(s.RetryDelaySeconds, 1, 600)),
            MinSegmentSize = Math.Max(64 * 1024L, s.MinSegmentSizeKb * 1024L),
            UserAgent = string.IsNullOrWhiteSpace(s.UserAgent) ? "FileRush/1.0" : s.UserAgent,
            IgnoreModificationTime = s.IgnoreModificationTimeOnResume,
            TempDirectory = string.IsNullOrWhiteSpace(s.TempDirectory) ? null : s.TempDirectory,
            FtpPassive = s.Proxy.UseFtpPassive
        };
        ApplyProxy(options, s.Proxy);
        return options;
    }

    public static void ApplyProxy(DownloadEngineOptions options, ProxySettings proxy)
    {
        switch (proxy.Mode)
        {
            case ProxyMode.None:
                options.Proxy = null;
                options.UseSystemProxy = false;
                break;
            case ProxyMode.System:
                options.Proxy = null;
                options.UseSystemProxy = true;
                break;
            case ProxyMode.Manual:
                if (string.IsNullOrWhiteSpace(proxy.Address))
                {
                    options.Proxy = null;
                    options.UseSystemProxy = false;
                    break;
                }
                var scheme = proxy.Kind switch
                {
                    ProxyKind.Socks4 => "socks4",
                    ProxyKind.Socks5 => "socks5",
                    _ => "http"
                };
                var address = proxy.Address.Contains("://", StringComparison.Ordinal) ? proxy.Address : $"{scheme}://{proxy.Address}:{proxy.Port}";
                var webProxy = new WebProxy(new Uri(address));
                if (!string.IsNullOrEmpty(proxy.Username))
                {
                    webProxy.Credentials = new NetworkCredential(proxy.Username, proxy.Password);
                }
                var bypass = proxy.Bypass.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (bypass.Length > 0)
                {
                    webProxy.BypassList = bypass.Select(b => System.Text.RegularExpressions.Regex.Escape(b).Replace("\\*", ".*")).ToArray();
                }
                webProxy.BypassProxyOnLocal = true;
                options.Proxy = webProxy;
                options.UseSystemProxy = false;
                break;
        }
    }

    private NetworkCredential? ResolveSiteLogin(Uri uri)
    {
        var login = Settings.SiteLogins.FirstOrDefault(l => l.Matches(uri));
        return login is null ? null : new NetworkCredential(login.Username, login.Password);
    }

    public Task<RemoteFileInfo> ProbeAsync(string url, string? username, string? password, string? referrer, CancellationToken ct)
    {
        var item = new DownloadItem { Url = url, Username = username, Password = password, Referrer = referrer };
        return UrlProber.ProbeAsync(item, BuildOptions(item), _factory, ct);
    }

    public Category ResolveCategory(string fileNameOrUrl) => CategoryResolver.Resolve(Settings.Categories, fileNameOrUrl);

    public string DirectoryForCategory(string categoryName)
    {
        var category = CategoryResolver.Find(Settings.Categories, categoryName);
        var dir = category?.SaveDirectory;
        if (string.IsNullOrWhiteSpace(dir)) dir = Settings.DownloadsRoot;
        if (string.IsNullOrWhiteSpace(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return dir;
    }

    public DownloadItem Add(NewDownloadRequest request, bool start)
    {
        var url = UrlUtils.NormalizeUrl(request.Url);
        var fileName = request.FileName;
        if (string.IsNullOrWhiteSpace(fileName)) fileName = request.KnownInfo?.FileName;
        if (string.IsNullOrWhiteSpace(fileName)) fileName = FileNameResolver.FromUrl(url);
        if (!string.IsNullOrWhiteSpace(fileName)) fileName = FileNameResolver.Sanitize(fileName);
        var categoryName = request.Category;
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            categoryName = ResolveCategory(string.IsNullOrWhiteSpace(fileName) ? url : fileName).Name;
        }
        var directory = request.SaveDirectory;
        if (string.IsNullOrWhiteSpace(directory)) directory = DirectoryForCategory(categoryName);

        var item = new DownloadItem
        {
            Url = url,
            FileName = fileName ?? string.Empty,
            SaveDirectory = directory,
            Category = categoryName,
            Description = request.Description,
            Referrer = request.Referrer,
            UserAgent = request.UserAgent,
            Username = request.Username,
            Password = request.Password,
            Headers = request.Headers,
            MaxConnections = request.MaxConnections,
            QueueId = request.QueueId,
            Status = DownloadStatus.Pending,
            StatusText = string.Empty
        };
        if (request.KnownInfo is { } info)
        {
            item.TotalSize = info.Size;
            item.SupportsResume = info.SupportsRange;
            item.ETag = info.ETag;
            item.LastModified = info.LastModified;
            item.ContentType = info.ContentType;
            item.FinalUrl = info.FinalUrl;
        }
        Items.Add(item);
        if (request.QueueId is { } queueId)
        {
            var queue = Queues.FirstOrDefault(q => q.Id == queueId);
            if (queue is not null) queue.ItemIds.Add(item.Id);
            else item.QueueId = null;
        }
        ItemAdded?.Invoke(this, item);
        ScheduleSave();
        if (start) Start(item);
        return item;
    }

    public DownloadJob? GetJob(DownloadItem item) => _jobs.GetValueOrDefault(item.Id);

    public bool IsRunning(DownloadItem item) => _jobs.ContainsKey(item.Id);

    public void Start(DownloadItem item)
    {
        if (item.Status == DownloadStatus.Completed) return;
        if (_jobs.ContainsKey(item.Id)) return;
        if (_jobs.Count >= Math.Max(1, Settings.MaxSimultaneousDownloads))
        {
            item.Status = DownloadStatus.Queued;
            item.StatusText = "Waiting...";
            return;
        }
        StartJob(item);
    }

    private void StartJob(DownloadItem item)
    {
        var job = new DownloadJob(item, BuildOptions(item), Limiter, _factory);
        _jobs[item.Id] = job;
        job.StateChanged += _ => ScheduleSave();
        DownloadStarted?.Invoke(this, item);
        _ = RunJobAsync(job);
    }

    private async Task RunJobAsync(DownloadJob job)
    {
        try
        {
            await job.RunAsync().ConfigureAwait(false);
        }
        catch
        {
        }
        Dispatch(() => OnJobFinished(job));
    }

    private void OnJobFinished(DownloadJob job)
    {
        _jobs.Remove(job.Item.Id);
        var item = job.Item;
        ScheduleSave();
        if (item.Status == DownloadStatus.Completed) DownloadCompleted?.Invoke(this, item);
        else if (item.Status == DownloadStatus.Error) DownloadFailed?.Invoke(this, item);
        StateChanged?.Invoke(this, EventArgs.Empty);
        PumpQueues();
        PumpWaiting();
    }

    private void PumpWaiting()
    {
        var max = Math.Max(1, Settings.MaxSimultaneousDownloads);
        while (_jobs.Count < max)
        {
            var next = Items.FirstOrDefault(i => i.Status == DownloadStatus.Queued && !_jobs.ContainsKey(i.Id));
            if (next is null) break;
            StartJob(next);
        }
    }

    private void PumpQueues()
    {
        foreach (var queue in Queues.ToList())
        {
            if (queue.IsRunning) PumpQueue(queue);
        }
    }

    private void PumpQueue(DownloadQueue queue)
    {
        var running = Items.Count(i => i.QueueId == queue.Id && (i.Status is DownloadStatus.Queued or DownloadStatus.Connecting or DownloadStatus.Downloading));
        foreach (var id in queue.ItemIds.ToList())
        {
            if (running >= queue.MaxConcurrent) break;
            var item = Items.FirstOrDefault(i => i.Id == id);
            if (item is null || _jobs.ContainsKey(item.Id)) continue;
            if (item.Status is DownloadStatus.Pending or DownloadStatus.Paused)
            {
                Start(item);
                running++;
            }
        }
        if (running == 0)
        {
            queue.IsRunning = false;
            QueueFinished?.Invoke(this, queue);
        }
    }

    public void StartQueue(DownloadQueue queue)
    {
        queue.IsRunning = true;
        queue.StartedAt = DateTime.Now;
        PumpQueue(queue);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StopQueue(DownloadQueue queue)
    {
        queue.IsRunning = false;
        foreach (var item in Items.Where(i => i.QueueId == queue.Id).ToList())
        {
            if (item.IsActive) Pause(item);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause(DownloadItem item)
    {
        if (_jobs.TryGetValue(item.Id, out var job))
        {
            job.Pause();
            return;
        }
        if (item.Status == DownloadStatus.Queued)
        {
            item.Status = DownloadStatus.Paused;
            item.StatusText = "Paused";
            ScheduleSave();
        }
    }

    public async Task PauseAsync(DownloadItem item)
    {
        if (_jobs.TryGetValue(item.Id, out var job))
        {
            job.Pause();
            await job.Completion.ConfigureAwait(false);
            return;
        }
        Pause(item);
    }

    public void StopAll()
    {
        foreach (var queue in Queues) queue.IsRunning = false;
        foreach (var item in Items.ToList())
        {
            if (item.Status == DownloadStatus.Queued)
            {
                item.Status = DownloadStatus.Paused;
                item.StatusText = "Paused";
            }
        }
        foreach (var job in _jobs.Values.ToList()) job.Pause();
        ScheduleSave();
    }

    public void ResumeAll()
    {
        foreach (var item in Items.ToList())
        {
            if (item.CanResume) Start(item);
        }
    }

    public async Task RemoveAsync(DownloadItem item, bool deleteFile)
    {
        if (_jobs.TryGetValue(item.Id, out var job))
        {
            job.Pause();
            await job.Completion.ConfigureAwait(false);
        }
        Dispatch(() =>
        {
            _jobs.Remove(item.Id);
            Items.Remove(item);
            foreach (var queue in Queues) queue.ItemIds.Remove(item.Id);
            ItemRemoved?.Invoke(this, item);
            ScheduleSave();
            PumpQueues();
            PumpWaiting();
        });
        TryDelete(item.PartialPath);
        if (deleteFile && item.Status == DownloadStatus.Completed) TryDelete(item.FullPath);
    }

    private static void TryDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    public void UpdateUrl(DownloadItem item, string newUrl)
    {
        if (_jobs.ContainsKey(item.Id)) return;
        item.Url = UrlUtils.NormalizeUrl(newUrl);
        ScheduleSave();
    }

    public async Task MoveAsync(DownloadItem item, string newDirectory, string? newFileName = null)
    {
        if (_jobs.ContainsKey(item.Id)) return;
        var name = string.IsNullOrWhiteSpace(newFileName) ? item.FileName : FileNameResolver.Sanitize(newFileName);
        if (item.Status == DownloadStatus.Completed && File.Exists(item.FullPath))
        {
            var source = item.FullPath;
            Directory.CreateDirectory(newDirectory);
            var unique = FileNameResolver.MakeUnique(newDirectory, name);
            var target = Path.Combine(newDirectory, unique);
            await Task.Run(() => File.Move(source, target, false)).ConfigureAwait(false);
            name = unique;
        }
        Dispatch(() =>
        {
            item.SaveDirectory = newDirectory;
            item.FileName = name;
            ScheduleSave();
        });
    }

    public DownloadQueue CreateQueue(string name)
    {
        var queue = new DownloadQueue { Name = name };
        Queues.Add(queue);
        ScheduleSave();
        return queue;
    }

    public void DeleteQueue(DownloadQueue queue)
    {
        if (queue.IsBuiltIn) return;
        StopQueue(queue);
        foreach (var item in Items.Where(i => i.QueueId == queue.Id)) item.QueueId = null;
        Queues.Remove(queue);
        ScheduleSave();
    }

    public void AddToQueue(DownloadItem item, DownloadQueue queue)
    {
        foreach (var q in Queues) q.ItemIds.Remove(item.Id);
        queue.ItemIds.Add(item.Id);
        item.QueueId = queue.Id;
        ScheduleSave();
        if (queue.IsRunning) PumpQueue(queue);
    }

    public void RemoveFromQueue(DownloadItem item)
    {
        foreach (var q in Queues) q.ItemIds.Remove(item.Id);
        item.QueueId = null;
        ScheduleSave();
    }

    public void MoveInQueue(DownloadItem item, int offset)
    {
        var queue = Queues.FirstOrDefault(q => q.Id == item.QueueId);
        if (queue is null) return;
        var index = queue.ItemIds.IndexOf(item.Id);
        if (index < 0) return;
        var target = Math.Clamp(index + offset, 0, queue.ItemIds.Count - 1);
        if (target == index) return;
        queue.ItemIds.RemoveAt(index);
        queue.ItemIds.Insert(target, item.Id);
        ScheduleSave();
    }

    public IReadOnlyList<DownloadItem> ItemsInQueue(DownloadQueue queue)
    {
        var byId = Items.ToDictionary(i => i.Id);
        var list = new List<DownloadItem>();
        foreach (var id in queue.ItemIds)
        {
            if (byId.TryGetValue(id, out var item)) list.Add(item);
        }
        return list;
    }

    public void SchedulerTick(DateTime now)
    {
        var changed = false;
        foreach (var queue in Queues.ToList())
        {
            if (!queue.IsRunning && QueueScheduler.ShouldStart(queue.Schedule, now))
            {
                queue.Schedule.LastStarted = now;
                StartQueue(queue);
                changed = true;
            }
            else if (queue.IsRunning && QueueScheduler.ShouldStop(queue.Schedule, now))
            {
                queue.Schedule.LastStopped = now;
                StopQueue(queue);
                changed = true;
            }
        }
        if (changed) ScheduleSave();
    }

    public double TotalSpeed()
    {
        double total = 0;
        foreach (var item in Items) total += item.Speed;
        return total;
    }

    public void ScheduleSave()
    {
        if (_disposed) return;
        if (Interlocked.Exchange(ref _savePending, 1) == 1) return;
        _saveTimer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
    }

    public void Save()
    {
        if (_disposed && _savePending == 0) return;
        Interlocked.Exchange(ref _savePending, 0);
        List<DownloadItem> items = new();
        List<DownloadQueue> queues = new();
        Dispatch(() =>
        {
            items = Items.ToList();
            queues = Queues.ToList();
        });
        lock (_saveGate)
        {
            try
            {
                _store.Save(new DownloadStoreData { Items = items, Queues = queues });
            }
            catch
            {
            }
        }
    }

    public async Task ShutdownAsync()
    {
        foreach (var job in _jobs.Values.ToList()) job.Pause();
        var tasks = _jobs.Values.Select(j => j.Completion).ToList();
        try
        {
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch
        {
        }
        Save();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _saveTimer.Dispose();
        _schedulerTimer.Dispose();
    }
}
