using FileRush.Core.Engine;
using FileRush.Core.Models;
using FileRush.Core.Services;
using Xunit;

namespace FileRush.Tests;

public sealed class DownloadManagerTests : IDisposable
{
    private readonly string _root = TestUtil.NewTempDir();
    private readonly TestHttpServer _server = new();
    private readonly List<DownloadManager> _managers = new();

    private string DownloadsRoot => Path.Combine(_root, "Downloads");

    private DownloadManager NewManager(Action<AppSettings>? configure = null)
    {
        var settings = AppSettings.CreateDefault();
        settings.DownloadsRoot = DownloadsRoot;
        settings.TempDirectory = Path.Combine(_root, "Temp");
        settings.Categories = Category.CreateDefaults(DownloadsRoot);
        settings.MaxConnectionsPerFile = 2;
        settings.MaxRetries = 1;
        settings.RetryDelaySeconds = 1;
        settings.TimeoutSeconds = 10;
        settings.MinSegmentSizeKb = 64;
        configure?.Invoke(settings);
        var store = new DownloadStore(Path.Combine(_root, "downloads.json"));
        var manager = new DownloadManager(settings, store, new RangeSourceFactory());
        manager.Load();
        _managers.Add(manager);
        return manager;
    }

    private static bool IsActive(DownloadItem item) => item.Status is DownloadStatus.Connecting or DownloadStatus.Downloading;

    [Fact]
    public void Add_ResolvesCategoryDirectoryAndFileName()
    {
        var manager = NewManager();
        var item = manager.Add(new NewDownloadRequest { Url = "http://example.com/files/archive.zip?x=1" }, false);

        Assert.Equal("Compressed", item.Category);
        Assert.Equal(Path.Combine(DownloadsRoot, "Compressed"), item.SaveDirectory);
        Assert.Equal("archive.zip", item.FileName);
        Assert.Equal(DownloadStatus.Pending, item.Status);
        Assert.Contains(item, manager.Items);
        Assert.Null(item.QueueId);
    }

    [Fact]
    public void Add_HonorsExplicitValuesAndQueue()
    {
        var manager = NewManager();
        var queue = manager.CreateQueue("Custom");
        var item = manager.Add(new NewDownloadRequest
        {
            Url = "example.com/song.mp3",
            Category = "Documents",
            SaveDirectory = Path.Combine(_root, "elsewhere"),
            FileName = "renamed.mp3",
            Description = "desc",
            QueueId = queue.Id,
            KnownInfo = new RemoteFileInfo { Size = 12345, SupportsRange = true, ETag = "\"e\"" }
        }, false);

        Assert.Equal("http://example.com/song.mp3", item.Url);
        Assert.Equal("Documents", item.Category);
        Assert.Equal(Path.Combine(_root, "elsewhere"), item.SaveDirectory);
        Assert.Equal("renamed.mp3", item.FileName);
        Assert.Equal("desc", item.Description);
        Assert.Equal(queue.Id, item.QueueId);
        Assert.Contains(item.Id, queue.ItemIds);
        Assert.Equal(12345, item.TotalSize);
        Assert.True(item.SupportsResume);
        Assert.Single(manager.ItemsInQueue(queue));
    }

    [Fact]
    public void Load_CreatesDefaultQueues()
    {
        var manager = NewManager();
        Assert.Contains(manager.Queues, q => q.Name == DownloadManager.MainQueueName && q.IsBuiltIn);
        Assert.Contains(manager.Queues, q => q.Name == DownloadManager.SyncQueueName && q.IsBuiltIn);
        Assert.Same(manager.Queues[0], manager.MainQueue);
    }

    [Fact]
    public async Task Start_RespectsMaxSimultaneousDownloads()
    {
        _server.DelayPerChunkMs = 100;
        var data = TestUtil.RandomBytes(1024 * 1024, 51);
        var url1 = _server.AddFile("/one.bin", data);
        var url2 = _server.AddFile("/two.bin", data);
        var manager = NewManager(s => s.MaxSimultaneousDownloads = 1);
        var completed = new List<DownloadItem>();
        manager.DownloadCompleted += (_, i) => { lock (completed) completed.Add(i); };

        var first = manager.Add(new NewDownloadRequest { Url = url1 }, true);
        var second = manager.Add(new NewDownloadRequest { Url = url2 }, true);

        Assert.True(IsActive(first));
        Assert.Equal(DownloadStatus.Queued, second.Status);
        Assert.Equal(1, manager.ActiveJobCount);

        await TestUtil.WaitUntilAsync(() => first.Status == DownloadStatus.Completed, "first completes");
        await TestUtil.WaitUntilAsync(() => second.Status == DownloadStatus.Completed, "second completes");
        Assert.Equal(2, completed.Count);
        Assert.True(File.Exists(first.FullPath));
        Assert.True(File.Exists(second.FullPath));
        Assert.Equal(TestUtil.Sha256(data), TestUtil.Sha256File(second.FullPath));
    }

    [Fact]
    public async Task StartQueue_HonorsMaxConcurrent_AndFiresQueueFinished()
    {
        _server.DelayPerChunkMs = 30;
        var data = TestUtil.RandomBytes(512 * 1024, 53);
        var manager = NewManager();
        var queue = manager.CreateQueue("Serial");
        queue.MaxConcurrent = 1;
        var items = new List<DownloadItem>();
        for (int i = 0; i < 3; i++)
        {
            var url = _server.AddFile($"/q{i}.bin", data);
            items.Add(manager.Add(new NewDownloadRequest { Url = url, QueueId = queue.Id }, false));
        }
        var finished = 0;
        manager.QueueFinished += (_, q) => { if (q == queue) Interlocked.Increment(ref finished); };

        manager.StartQueue(queue);
        Assert.True(queue.IsRunning);
        var maxActive = 0;
        while (Volatile.Read(ref finished) == 0)
        {
            maxActive = Math.Max(maxActive, items.Count(IsActive));
            await Task.Delay(10);
        }

        Assert.Equal(1, maxActive);
        Assert.All(items, i => Assert.Equal(DownloadStatus.Completed, i.Status));
        Assert.False(queue.IsRunning);
        Assert.Equal(1, finished);
    }

    [Fact]
    public async Task StopQueue_PausesRunningItems()
    {
        _server.DelayPerChunkMs = 100;
        var data = TestUtil.RandomBytes(1024 * 1024, 57);
        var manager = NewManager();
        var queue = manager.CreateQueue("Parallel");
        queue.MaxConcurrent = 2;
        var a = manager.Add(new NewDownloadRequest { Url = _server.AddFile("/a.bin", data), QueueId = queue.Id }, false);
        var b = manager.Add(new NewDownloadRequest { Url = _server.AddFile("/b.bin", data), QueueId = queue.Id }, false);

        manager.StartQueue(queue);
        await TestUtil.WaitUntilAsync(() => IsActive(a) && IsActive(b), "both active");
        manager.StopQueue(queue);

        await TestUtil.WaitUntilAsync(() => a.Status == DownloadStatus.Paused && b.Status == DownloadStatus.Paused, "both paused");
        Assert.False(queue.IsRunning);
        await TestUtil.WaitUntilAsync(() => manager.ActiveJobCount == 0, "jobs released");
    }

    [Fact]
    public async Task Pause_QueuedItem_BecomesPaused()
    {
        _server.DelayPerChunkMs = 100;
        var data = TestUtil.RandomBytes(1024 * 1024, 59);
        var manager = NewManager(s => s.MaxSimultaneousDownloads = 1);
        var first = manager.Add(new NewDownloadRequest { Url = _server.AddFile("/p1.bin", data) }, true);
        var second = manager.Add(new NewDownloadRequest { Url = _server.AddFile("/p2.bin", data) }, true);
        Assert.Equal(DownloadStatus.Queued, second.Status);

        manager.Pause(second);
        Assert.Equal(DownloadStatus.Paused, second.Status);
        await manager.PauseAsync(first);
        Assert.Equal(DownloadStatus.Paused, first.Status);
        Assert.Equal(0, manager.ActiveJobCount);
    }

    [Fact]
    public async Task RemoveAsync_DeletesPartialFile()
    {
        _server.DelayPerChunkMs = 100;
        var data = TestUtil.RandomBytes(1024 * 1024, 61);
        var manager = NewManager();
        var item = manager.Add(new NewDownloadRequest { Url = _server.AddFile("/rm.bin", data) }, true);
        await TestUtil.WaitUntilAsync(() => item.PartialPath is not null && File.Exists(item.PartialPath), "partial exists");
        var partial = item.PartialPath!;
        var removed = new List<DownloadItem>();
        manager.ItemRemoved += (_, i) => removed.Add(i);

        await manager.RemoveAsync(item, false);

        Assert.DoesNotContain(item, manager.Items);
        Assert.False(File.Exists(partial));
        Assert.Contains(item, removed);
        Assert.Equal(0, manager.ActiveJobCount);
    }

    [Fact]
    public async Task RemoveAsync_CompletedWithDeleteFile_RemovesTarget()
    {
        var data = TestUtil.RandomBytes(64 * 1024, 63);
        var manager = NewManager();
        var item = manager.Add(new NewDownloadRequest { Url = _server.AddFile("/done.bin", data) }, true);
        await TestUtil.WaitUntilAsync(() => item.Status == DownloadStatus.Completed, "completed");
        Assert.True(File.Exists(item.FullPath));

        await manager.RemoveAsync(item, true);

        Assert.False(File.Exists(item.FullPath));
        Assert.DoesNotContain(item, manager.Items);
    }

    [Fact]
    public async Task SchedulerTick_StartsDueQueue()
    {
        _server.DelayPerChunkMs = 100;
        var data = TestUtil.RandomBytes(1024 * 1024, 67);
        var manager = NewManager();
        var queue = manager.CreateQueue("Scheduled");
        queue.Schedule.StartEnabled = true;
        queue.Schedule.StartTime = new TimeSpan(10, 0, 0);
        queue.Schedule.Days = ScheduleDays.All;
        var item = manager.Add(new NewDownloadRequest { Url = _server.AddFile("/sched.bin", data), QueueId = queue.Id }, false);
        var due = new DateTime(2026, 1, 5, 10, 1, 0);

        manager.SchedulerTick(due.AddHours(-2));
        Assert.False(queue.IsRunning);
        Assert.Equal(DownloadStatus.Pending, item.Status);

        manager.SchedulerTick(due);
        Assert.True(queue.IsRunning);
        Assert.Equal(due, queue.Schedule.LastStarted);
        await TestUtil.WaitUntilAsync(() => IsActive(item), "item started");

        queue.Schedule.StopEnabled = true;
        queue.Schedule.StopTime = new TimeSpan(10, 30, 0);
        manager.SchedulerTick(due.AddMinutes(30));
        Assert.False(queue.IsRunning);
        await TestUtil.WaitUntilAsync(() => item.Status == DownloadStatus.Paused, "item paused");
    }

    [Fact]
    public void ApplySettings_UpdatesLimiter()
    {
        var manager = NewManager();
        Assert.Equal(0, manager.Limiter.BytesPerSecond);
        manager.Settings.SpeedLimit.Enabled = true;
        manager.Settings.SpeedLimit.MaxKilobytesPerSecond = 100;
        manager.ApplySettings(manager.Settings);
        Assert.Equal(100 * 1024, manager.Limiter.BytesPerSecond);
        manager.SetSpeedLimit(false, 100);
        Assert.Equal(0, manager.Limiter.BytesPerSecond);
        manager.SetSpeedLimit(true, 5);
        Assert.Equal(5 * 1024, manager.Limiter.BytesPerSecond);
    }

    [Fact]
    public void BuildOptions_ReflectsSettingsAndProxy()
    {
        var manager = NewManager(s =>
        {
            s.MaxConnectionsPerFile = 6;
            s.TimeoutSeconds = 45;
            s.Proxy.Mode = ProxyMode.Manual;
            s.Proxy.Address = "127.0.0.1";
            s.Proxy.Port = 3128;
            s.Proxy.Username = "user";
            s.Proxy.Password = "pass";
            s.Proxy.Bypass = "*.local;intranet";
        });
        var options = manager.BuildOptions(new DownloadItem { Url = "http://x/y" });
        Assert.Equal(6, options.MaxConnections);
        Assert.Equal(TimeSpan.FromSeconds(45), options.Timeout);
        Assert.NotNull(options.Proxy);
        Assert.False(options.UseSystemProxy);
        var proxy = Assert.IsType<System.Net.WebProxy>(options.Proxy);
        Assert.Equal(new Uri("http://127.0.0.1:3128"), proxy.Address);
        Assert.NotNull(proxy.Credentials);

        var none = new DownloadEngineOptions();
        DownloadManager.ApplyProxy(none, new ProxySettings { Mode = ProxyMode.None });
        Assert.Null(none.Proxy);
        Assert.False(none.UseSystemProxy);
        var system = new DownloadEngineOptions();
        DownloadManager.ApplyProxy(system, new ProxySettings { Mode = ProxyMode.System });
        Assert.Null(system.Proxy);
        Assert.True(system.UseSystemProxy);
    }

    [Fact]
    public void QueueMembership_AddRemoveMove()
    {
        var manager = NewManager();
        var queue = manager.CreateQueue("Q");
        var a = manager.Add(new NewDownloadRequest { Url = "http://x/a.zip" }, false);
        var b = manager.Add(new NewDownloadRequest { Url = "http://x/b.zip" }, false);
        manager.AddToQueue(a, queue);
        manager.AddToQueue(b, queue);
        Assert.Equal(new[] { a.Id, b.Id }, queue.ItemIds);
        manager.MoveInQueue(b, -1);
        Assert.Equal(new[] { b.Id, a.Id }, queue.ItemIds);
        manager.RemoveFromQueue(a);
        Assert.Null(a.QueueId);
        Assert.Equal(new[] { b.Id }, queue.ItemIds);
        manager.AddToQueue(b, manager.MainQueue);
        Assert.Empty(queue.ItemIds);
        Assert.Equal(manager.MainQueue.Id, b.QueueId);
        manager.DeleteQueue(queue);
        Assert.DoesNotContain(queue, manager.Queues);
        manager.DeleteQueue(manager.MainQueue);
        Assert.Contains(manager.MainQueue, manager.Queues);
    }

    [Fact]
    public void SaveAndLoad_RestoresItemsAndNormalizesStatus()
    {
        var first = NewManager();
        var pending = first.Add(new NewDownloadRequest { Url = "http://x/p.zip" }, false);
        var running = first.Add(new NewDownloadRequest { Url = "http://x/r.zip" }, false);
        running.Status = DownloadStatus.Downloading;
        first.AddToQueue(running, first.MainQueue);
        first.Save();

        var second = NewManager();
        Assert.Equal(2, second.Items.Count);
        var loadedPending = second.Items.First(i => i.Id == pending.Id);
        var loadedRunning = second.Items.First(i => i.Id == running.Id);
        Assert.Equal(DownloadStatus.Pending, loadedPending.Status);
        Assert.Equal(DownloadStatus.Paused, loadedRunning.Status);
        Assert.Equal(second.MainQueue.Id, loadedRunning.QueueId);
        Assert.Contains(running.Id, second.MainQueue.ItemIds);
    }

    [Fact]
    public void UpdateUrl_NormalizesAndOnlyWhenIdle()
    {
        var manager = NewManager();
        var item = manager.Add(new NewDownloadRequest { Url = "http://x/a.zip" }, false);
        manager.UpdateUrl(item, "y.com/b.zip");
        Assert.Equal("http://y.com/b.zip", item.Url);
    }

    [Fact]
    public async Task MoveAsync_MovesCompletedFile()
    {
        var data = TestUtil.RandomBytes(32 * 1024, 71);
        var manager = NewManager();
        var item = manager.Add(new NewDownloadRequest { Url = _server.AddFile("/mv.bin", data) }, true);
        await TestUtil.WaitUntilAsync(() => item.Status == DownloadStatus.Completed, "completed");
        var target = Path.Combine(_root, "moved");

        await manager.MoveAsync(item, target, "renamed.bin");

        Assert.Equal(target, item.SaveDirectory);
        Assert.Equal("renamed.bin", item.FileName);
        Assert.True(File.Exists(Path.Combine(target, "renamed.bin")));
        Assert.Equal(TestUtil.Sha256(data), TestUtil.Sha256File(item.FullPath));
    }

    [Fact]
    public async Task StopAll_PausesEverything()
    {
        _server.DelayPerChunkMs = 100;
        var data = TestUtil.RandomBytes(1024 * 1024, 73);
        var manager = NewManager(s => s.MaxSimultaneousDownloads = 1);
        var queue = manager.CreateQueue("Q");
        var a = manager.Add(new NewDownloadRequest { Url = _server.AddFile("/s1.bin", data) }, true);
        var b = manager.Add(new NewDownloadRequest { Url = _server.AddFile("/s2.bin", data) }, true);
        manager.StartQueue(queue);
        Assert.Equal(DownloadStatus.Queued, b.Status);

        manager.StopAll();

        Assert.Equal(DownloadStatus.Paused, b.Status);
        Assert.False(queue.IsRunning);
        await TestUtil.WaitUntilAsync(() => a.Status == DownloadStatus.Paused, "a paused");
        manager.ResumeAll();
        await TestUtil.WaitUntilAsync(() => a.Status == DownloadStatus.Completed && b.Status == DownloadStatus.Completed, TimeSpan.FromSeconds(30), "all completed");
    }

    [Fact]
    public async Task ProbeAsync_ReturnsRemoteInfo()
    {
        var data = TestUtil.RandomBytes(16 * 1024, 79);
        var manager = NewManager();
        var info = await manager.ProbeAsync(_server.AddFile("/probe.bin", data, "attachment; filename=named.bin"), null, null, null, CancellationToken.None);
        Assert.Equal(data.Length, info.Size);
        Assert.True(info.SupportsRange);
        Assert.Equal("named.bin", info.FileName);
    }

    public void Dispose()
    {
        foreach (var manager in _managers)
        {
            try
            {
                manager.StopAll();
                manager.ShutdownAsync().Wait(15000);
            }
            catch
            {
            }
            manager.Dispose();
        }
        _server.Dispose();
        TestUtil.DeleteDir(_root);
    }
}
