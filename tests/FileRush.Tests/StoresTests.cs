using FileRush.Core.Models;
using FileRush.Core.Services;
using Xunit;

namespace FileRush.Tests;

public sealed class StoresTests : IDisposable
{
    private readonly string _root = TestUtil.NewTempDir();

    [Fact]
    public void DownloadStore_RoundTrips()
    {
        var path = Path.Combine(_root, "downloads.json");
        var store = new DownloadStore(path);
        var queue = new DownloadQueue
        {
            Name = "Night",
            MaxConcurrent = 3,
            OnComplete = QueueCompletionAction.Hibernate,
            Schedule = new QueueSchedule { StartEnabled = true, StartTime = new TimeSpan(2, 30, 0), Days = ScheduleDays.Weekdays, Mode = ScheduleMode.Daily }
        };
        var item = new DownloadItem
        {
            Url = "http://a.com/f.zip",
            FileName = "f.zip",
            SaveDirectory = "C:\\d",
            Category = "Compressed",
            Status = DownloadStatus.Paused,
            TotalSize = 1000,
            SupportsResume = true,
            ETag = "\"abc\"",
            PartialPath = "C:\\t\\f.part",
            QueueId = queue.Id,
            Headers = new Dictionary<string, string> { ["Cookie"] = "a=b" },
            Segments = new List<Segment> { new(0, 499) { Position = 200 }, new(500, 999) { Position = 1000 } }
        };
        queue.ItemIds.Add(item.Id);

        store.Save(new DownloadStoreData { Items = { item }, Queues = { queue } });
        var json = File.ReadAllText(path);
        Assert.Contains("\"Paused\"", json);
        Assert.Contains("\"Hibernate\"", json);
        Assert.Contains("02:30:00", json);

        var loaded = store.Load();
        var loadedItem = Assert.Single(loaded.Items);
        var loadedQueue = Assert.Single(loaded.Queues);
        Assert.Equal(item.Id, loadedItem.Id);
        Assert.Equal(DownloadStatus.Paused, loadedItem.Status);
        Assert.Equal(1000, loadedItem.TotalSize);
        Assert.True(loadedItem.SupportsResume);
        Assert.Equal("\"abc\"", loadedItem.ETag);
        Assert.Equal("C:\\t\\f.part", loadedItem.PartialPath);
        Assert.Equal(queue.Id, loadedItem.QueueId);
        Assert.Equal("a=b", loadedItem.Headers["Cookie"]);
        Assert.Equal(2, loadedItem.Segments.Count);
        Assert.Equal(200, loadedItem.Segments[0].Position);
        Assert.True(loadedItem.Segments[1].IsComplete);
        Assert.Equal("Night", loadedQueue.Name);
        Assert.Equal(3, loadedQueue.MaxConcurrent);
        Assert.Equal(QueueCompletionAction.Hibernate, loadedQueue.OnComplete);
        Assert.Equal(new TimeSpan(2, 30, 0), loadedQueue.Schedule.StartTime);
        Assert.Equal(ScheduleDays.Weekdays, loadedQueue.Schedule.Days);
        Assert.Contains(item.Id, loadedQueue.ItemIds);
    }

    [Fact]
    public void DownloadStore_MissingOrCorrupt_ReturnsEmpty()
    {
        var path = Path.Combine(_root, "none.json");
        Assert.Empty(new DownloadStore(path).Load().Items);
        File.WriteAllText(path, "{{{ not json");
        Assert.Empty(new DownloadStore(path).Load().Items);
    }

    [Fact]
    public void SettingsStore_RoundTrips_AndDefaults()
    {
        var path = Path.Combine(_root, "settings.json");
        var store = new SettingsStore(path);
        var defaults = store.Load();
        Assert.NotEmpty(defaults.Categories);
        Assert.False(string.IsNullOrEmpty(defaults.DownloadsRoot));

        defaults.MaxConnectionsPerFile = 16;
        defaults.Proxy.Mode = ProxyMode.Manual;
        defaults.Proxy.Address = "proxy.local";
        defaults.SiteLogins.Add(new SiteLogin { Host = "example.com", Username = "u", Password = "p" });
        defaults.SpeedLimit.Enabled = true;
        defaults.SpeedLimit.MaxKilobytesPerSecond = 77;
        store.Save(defaults);

        var loaded = store.Load();
        Assert.Equal(16, loaded.MaxConnectionsPerFile);
        Assert.Equal(ProxyMode.Manual, loaded.Proxy.Mode);
        Assert.Equal("proxy.local", loaded.Proxy.Address);
        Assert.Single(loaded.SiteLogins);
        Assert.True(loaded.SpeedLimit.Enabled);
        Assert.Equal(77, loaded.SpeedLimit.MaxKilobytesPerSecond);
        Assert.Equal(defaults.Categories.Count, loaded.Categories.Count);
    }

    [Fact]
    public void AtomicFile_KeepsBackupAndFallsBack()
    {
        var path = Path.Combine(_root, "atomic.txt");
        AtomicFile.Write(path, "v1");
        AtomicFile.Write(path, "v2");
        Assert.Equal("v2", AtomicFile.Read(path));
        Assert.Equal("v1", File.ReadAllText(path + ".bak"));
        File.Delete(path);
        Assert.Equal("v1", AtomicFile.Read(path));
        File.Delete(path + ".bak");
        Assert.Null(AtomicFile.Read(path));
    }

    [Fact]
    public void DataPaths_UseRoot()
    {
        Assert.EndsWith("settings.json", DataPaths.SettingsFile);
        Assert.EndsWith("downloads.json", DataPaths.DownloadsFile);
        Assert.StartsWith(DataPaths.Root, DataPaths.SettingsFile);
    }

    [Fact]
    public void SiteLogin_MatchesHostAndSubdomains()
    {
        var login = new SiteLogin { Host = "https://example.com/path" };
        Assert.True(login.Matches(new Uri("http://example.com/a")));
        Assert.True(login.Matches(new Uri("http://files.example.com/a")));
        Assert.False(login.Matches(new Uri("http://notexample.com/a")));
        Assert.False(new SiteLogin().Matches(new Uri("http://example.com/")));
    }

    public void Dispose() => TestUtil.DeleteDir(_root);
}
