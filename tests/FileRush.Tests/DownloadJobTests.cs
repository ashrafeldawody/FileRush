using System.Diagnostics;
using FileRush.Core.Engine;
using FileRush.Core.Models;
using Xunit;

namespace FileRush.Tests;

public sealed class DownloadJobTests : IDisposable
{
    private readonly string _root = TestUtil.NewTempDir();
    private readonly TestHttpServer _server = new();
    private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(30));

    public DownloadJobTests()
    {
        Directory.CreateDirectory(SaveDir);
        Directory.CreateDirectory(TempDir);
    }

    private string SaveDir => Path.Combine(_root, "save");

    private string TempDir => Path.Combine(_root, "temp");

    private DownloadItem NewItem(string url, string? fileName = null)
    {
        return new DownloadItem { Url = url, SaveDirectory = SaveDir, FileName = fileName ?? string.Empty };
    }

    private DownloadJob NewJob(DownloadItem item, DownloadEngineOptions? options = null, SpeedLimiter? limiter = null)
    {
        return new DownloadJob(item, options ?? TestUtil.Options(TempDir), limiter ?? new SpeedLimiter(), new RangeSourceFactory());
    }

    private static long SumDownloaded(DownloadJob job) => job.GetSegments().Sum(s => s.Downloaded);

    [Fact]
    public async Task MultiConnection_DownloadsIdenticalBytes_AndUsesContentDispositionName()
    {
        var data = TestUtil.RandomBytes(3 * 1024 * 1024);
        var url = _server.AddFile("/files/data.bin", data, "attachment; filename=\"report.zip\"");
        var item = NewItem(url);
        var job = NewJob(item);

        await job.RunAsync(_cts.Token);

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.Equal("report.zip", item.FileName);
        Assert.Equal(data.Length, item.TotalSize);
        Assert.Equal(item.TotalSize, item.DownloadedBytes);
        Assert.True(item.SupportsResume);
        Assert.Equal(4, job.Connections.Count);
        Assert.True(File.Exists(item.FullPath));
        Assert.Equal(TestUtil.Sha256(data), TestUtil.Sha256File(item.FullPath));
        Assert.Null(item.PartialPath);
        Assert.Empty(Directory.GetFiles(TempDir));
    }

    [Fact]
    public async Task PauseThenResume_WithNewJob_ResumesFromSavedPositions()
    {
        var data = TestUtil.RandomBytes(3 * 1024 * 1024, 7);
        _server.DelayPerChunkMs = 15;
        var url = _server.AddFile("/big.bin", data);
        var item = NewItem(url);
        var first = NewJob(item);

        var run = first.RunAsync(_cts.Token);
        await TestUtil.WaitUntilAsync(() =>
        {
            var segments = first.GetSegments();
            return segments.Count > 0 && segments[0].Downloaded > 0 && segments.Sum(s => s.Downloaded) > 256 * 1024;
        }, "partial progress");
        first.Pause();
        await run;

        Assert.Equal(DownloadStatus.Paused, item.Status);
        Assert.NotEmpty(item.Segments);
        var savedBytes = item.Segments.Sum(s => s.Downloaded);
        Assert.True(savedBytes > 0);
        Assert.True(savedBytes < data.Length);
        Assert.True(File.Exists(item.PartialPath));
        var headersBefore = _server.RangeHeaders.Count;

        _server.DelayPerChunkMs = 0;
        var second = NewJob(item);
        await second.RunAsync(_cts.Token);

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.Equal(TestUtil.Sha256(data), TestUtil.Sha256File(item.FullPath));
        var laterHeaders = _server.RangeHeaders.Skip(headersBefore).ToList();
        Assert.NotEmpty(laterHeaders);
        Assert.True(TestHttpServer.ParseRangeStart(laterHeaders[0]) > 0);
        Assert.Contains(laterHeaders, h => TestHttpServer.ParseRangeStart(h) > 0);
        Assert.True(_server.BytesServed < 2L * data.Length);
    }

    [Fact]
    public async Task ServerWithoutRangeSupport_UsesSingleConnection()
    {
        _server.SupportRanges = false;
        var data = TestUtil.RandomBytes(1024 * 1024, 3);
        var url = _server.AddFile("/norange.bin", data);
        var item = NewItem(url);
        var job = NewJob(item);

        await job.RunAsync(_cts.Token);

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.False(item.SupportsResume);
        Assert.Single(job.Connections);
        Assert.Equal(1, _server.RequestCount);
        Assert.Equal(TestUtil.Sha256(data), TestUtil.Sha256File(item.FullPath));
    }

    [Fact]
    public async Task UnknownLengthChunkedResponse_Completes()
    {
        _server.SupportRanges = false;
        _server.Chunked = true;
        var data = TestUtil.RandomBytes(1024 * 1024 + 12345, 5);
        var url = _server.AddFile("/chunked.bin", data);
        var item = NewItem(url);
        var job = NewJob(item);

        await job.RunAsync(_cts.Token);

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.Equal(data.Length, item.TotalSize);
        Assert.Equal(data.Length, item.DownloadedBytes);
        Assert.Equal(TestUtil.Sha256(data), TestUtil.Sha256File(item.FullPath));
    }

    [Fact]
    public async Task TransientServerErrors_AreRetried()
    {
        _server.FailFirstRequests = 2;
        var data = TestUtil.RandomBytes(1024 * 1024, 9);
        var url = _server.AddFile("/flaky.bin", data);
        var item = NewItem(url);
        var job = NewJob(item);

        await job.RunAsync(_cts.Token);

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.True(_server.RequestCount >= 3);
        Assert.Equal(TestUtil.Sha256(data), TestUtil.Sha256File(item.FullPath));
    }

    [Fact]
    public async Task DroppedConnectionMidBody_IsRetried()
    {
        _server.DropAfterBytes = 100 * 1024;
        _server.DropResponses = 1;
        var data = TestUtil.RandomBytes(1024 * 1024, 11);
        var url = _server.AddFile("/drop.bin", data);
        var item = NewItem(url);
        var job = NewJob(item, TestUtil.Options(TempDir, 2));

        await job.RunAsync(_cts.Token);

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.True(_server.RequestCount >= 3);
        Assert.Equal(TestUtil.Sha256(data), TestUtil.Sha256File(item.FullPath));
    }

    [Fact]
    public async Task NotFound_SetsErrorStatus()
    {
        _server.ResponseStatusOverride = 404;
        var url = _server.AddFile("/missing.bin", TestUtil.RandomBytes(1024));
        var item = NewItem(url);
        var job = NewJob(item);

        await job.RunAsync(_cts.Token);

        Assert.Equal(DownloadStatus.Error, item.Status);
        Assert.NotNull(item.ErrorMessage);
        Assert.Contains("404", item.ErrorMessage);
        Assert.Equal(1, _server.RequestCount);
    }

    [Fact]
    public async Task ETagChangedBetweenPauseAndResume_RestartsFromScratch()
    {
        var data = TestUtil.RandomBytes(2 * 1024 * 1024, 13);
        _server.ETag = "\"v1\"";
        _server.DelayPerChunkMs = 15;
        var url = _server.AddFile("/etag.bin", data);
        var item = NewItem(url);
        var first = NewJob(item);

        var run = first.RunAsync(_cts.Token);
        await TestUtil.WaitUntilAsync(() => SumDownloaded(first) > 128 * 1024, "partial progress");
        first.Pause();
        await run;
        Assert.Equal(DownloadStatus.Paused, item.Status);
        Assert.Equal("\"v1\"", item.ETag);
        var headersBefore = _server.RangeHeaders.Count;

        _server.ETag = "\"v2\"";
        _server.DelayPerChunkMs = 0;
        var second = NewJob(item);
        await second.RunAsync(_cts.Token);

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.Equal("\"v2\"", item.ETag);
        Assert.Equal(TestUtil.Sha256(data), TestUtil.Sha256File(item.FullPath));
        var laterHeaders = _server.RangeHeaders.Skip(headersBefore).ToList();
        Assert.True(TestHttpServer.ParseRangeStart(laterHeaders[0]) > 0);
        Assert.Contains(laterHeaders, h => h == "bytes=0-");
    }

    [Fact]
    public async Task DynamicSegmentation_SplitsSlowSegments_AndCoversWholeFile()
    {
        var data = TestUtil.RandomBytes(2 * 1024 * 1024, 17);
        _server.SlowRangeStart = 0;
        _server.SlowChunkDelayMs = 60;
        var url = _server.AddFile("/dyn.bin", data);
        var item = NewItem(url);
        var job = NewJob(item, TestUtil.Options(TempDir, 8));

        await job.RunAsync(_cts.Token);

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.Equal(TestUtil.Sha256(data), TestUtil.Sha256File(item.FullPath));
        var segments = job.GetSegments().OrderBy(s => s.Start).ToList();
        Assert.True(segments.Count > 8, $"expected dynamic splits, got {segments.Count} segments");
        Assert.Equal(0, segments[0].Start);
        Assert.Equal(data.Length - 1, segments[^1].End);
        for (int i = 1; i < segments.Count; i++)
        {
            Assert.Equal(segments[i - 1].End + 1, segments[i].Start);
        }
        Assert.All(segments, s => Assert.True(s.IsComplete));
        Assert.Equal(data.Length, segments.Sum(s => s.Downloaded));
    }

    [Fact]
    public async Task SpeedLimiter_ThrottlesDownload()
    {
        var data = TestUtil.RandomBytes(600 * 1024, 19);
        var url = _server.AddFile("/limited.bin", data);
        var item = NewItem(url);
        var limiter = new SpeedLimiter(200 * 1024);
        var job = NewJob(item, TestUtil.Options(TempDir, 2), limiter);

        var watch = Stopwatch.StartNew();
        await job.RunAsync(_cts.Token);
        watch.Stop();

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.True(watch.Elapsed >= TimeSpan.FromSeconds(1.8), $"took only {watch.Elapsed.TotalSeconds:F2}s");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15));
        Assert.Equal(TestUtil.Sha256(data), TestUtil.Sha256File(item.FullPath));
    }

    [Fact]
    public async Task WithoutTempDirectory_PartialFileLivesNextToTarget()
    {
        var data = TestUtil.RandomBytes(1024 * 1024, 23);
        _server.DelayPerChunkMs = 40;
        var url = _server.AddFile("/slow.bin", data);
        var item = NewItem(url);
        var job = NewJob(item, TestUtil.Options(null, 2));
        var partial = Path.Combine(SaveDir, "slow.bin.part");

        var run = job.RunAsync(_cts.Token);
        await TestUtil.WaitUntilAsync(() => File.Exists(partial), "partial file");
        Assert.Equal(partial, item.PartialPath);
        await run;

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.False(File.Exists(partial));
        Assert.True(File.Exists(Path.Combine(SaveDir, "slow.bin")));
        Assert.Equal(TestUtil.Sha256(data), TestUtil.Sha256File(item.FullPath));
    }

    [Fact]
    public async Task ExistingFileWithSameName_GetsUniqueName()
    {
        var data = TestUtil.RandomBytes(256 * 1024, 29);
        var existing = Path.Combine(SaveDir, "dup.bin");
        var old = new byte[] { 1, 2, 3 };
        File.WriteAllBytes(existing, old);
        var url = _server.AddFile("/dup.bin", data);
        var item = NewItem(url);
        var job = NewJob(item);

        await job.RunAsync(_cts.Token);

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.Equal("dup_2.bin", item.FileName);
        Assert.Equal(old, File.ReadAllBytes(existing));
        Assert.Equal(TestUtil.Sha256(data), TestUtil.Sha256File(Path.Combine(SaveDir, "dup_2.bin")));
    }

    [Fact]
    public async Task ExplicitFileName_IsKept()
    {
        var data = TestUtil.RandomBytes(128 * 1024, 31);
        var url = _server.AddFile("/whatever.bin", data, "attachment; filename=\"server.bin\"");
        var item = NewItem(url, "chosen.bin");
        var job = NewJob(item);

        await job.RunAsync(_cts.Token);

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.Equal("chosen.bin", item.FileName);
        Assert.True(File.Exists(Path.Combine(SaveDir, "chosen.bin")));
    }

    [Fact]
    public async Task ProgressEvents_ReportSpeedAndBytes()
    {
        var data = TestUtil.RandomBytes(1024 * 1024, 37);
        _server.DelayPerChunkMs = 30;
        var url = _server.AddFile("/progress.bin", data);
        var item = NewItem(url);
        var job = NewJob(item, TestUtil.Options(TempDir, 2));
        var events = 0;
        var sawSpeed = false;
        job.ProgressChanged += j =>
        {
            Interlocked.Increment(ref events);
            if (j.Item.Speed > 0) sawSpeed = true;
        };

        await job.RunAsync(_cts.Token);

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.True(events > 2);
        Assert.True(sawSpeed);
        Assert.Equal(0, item.Speed);
        Assert.Equal(100, item.Progress);
    }

    [Fact]
    public async Task Prober_ReportsSizeAndRangeSupport()
    {
        var data = TestUtil.RandomBytes(64 * 1024, 41);
        var url = _server.AddFile("/probe.bin", data, "attachment; filename=probed.dat");
        var item = NewItem(url);

        var info = await UrlProber.ProbeAsync(item, TestUtil.Options(TempDir), new RangeSourceFactory(), _cts.Token);

        Assert.Equal(data.Length, info.Size);
        Assert.True(info.SupportsRange);
        Assert.Equal("probed.dat", info.FileName);
    }

    public void Dispose()
    {
        _cts.Dispose();
        _server.Dispose();
        TestUtil.DeleteDir(_root);
    }
}
