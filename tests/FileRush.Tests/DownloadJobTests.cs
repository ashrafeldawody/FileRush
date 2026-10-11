using System.Diagnostics;
using System.Net;
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

    [Fact]
    public async Task MultipleRoutes_BindEachConnectionToItsAdapter_AndBothCarryData()
    {
        var data = TestUtil.RandomBytes(3 * 1024 * 1024, 51);
        var url = _server.AddFile("/combined.bin", data);
        var item = NewItem(url);
        var options = TestUtil.Options(TempDir);
        var first = IPAddress.Parse("127.0.0.2");
        var second = IPAddress.Parse("127.0.0.3");
        options.Routes = new[]
        {
            new NetworkRoute("eth", "Ethernet", new[] { first }),
            new NetworkRoute("wifi", "Wi-Fi", new[] { second })
        };
        var job = NewJob(item, options);

        await job.RunAsync(_cts.Token);

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.Equal(TestUtil.Sha256(data), TestUtil.Sha256File(item.FullPath));
        Assert.Equal(new[] { "Ethernet", "Wi-Fi", "Ethernet", "Wi-Fi" }, job.Connections.Select(c => c.Route));
        Assert.True(job.Connections.Where(c => c.Route == "Ethernet").Sum(c => c.Downloaded) > 0);
        Assert.True(job.Connections.Where(c => c.Route == "Wi-Fi").Sum(c => c.Downloaded) > 0);
        var pieces = job.GetPieces().OrderBy(p => p.Start).ToList();
        Assert.Equal(data.Length, pieces.Sum(p => p.Length));
        for (int i = 1; i < pieces.Count; i++) Assert.True(pieces[i].Start > pieces[i - 1].End);
        Assert.All(pieces, p => Assert.Equal(job.Connections[p.Connection - 1].Route, p.Route));
        Assert.Equal(new[] { "Ethernet", "Wi-Fi" }, pieces.Select(p => p.Route).Distinct().OrderBy(r => r));
        Assert.All(job.GetSegments(), s => Assert.Equal(0, s.Connection));
        Assert.Empty(job.GetRejectedRoutes());
        var clients = _server.ClientAddresses.ToHashSet();
        Assert.Contains(first, clients);
        Assert.Contains(second, clients);
        Assert.DoesNotContain(IPAddress.Loopback, clients);
    }

    [Fact]
    public async Task WhileDownloading_SegmentsReportTheirConnectionAndRoute()
    {
        var data = TestUtil.RandomBytes(2 * 1024 * 1024, 55);
        _server.DelayPerChunkMs = 20;
        var url = _server.AddFile("/owners.bin", data);
        var item = NewItem(url);
        var options = TestUtil.Options(TempDir, connections: 2);
        options.Routes = new[]
        {
            new NetworkRoute("eth", "Ethernet", new[] { IPAddress.Parse("127.0.0.6") }),
            new NetworkRoute("wifi", "Wi-Fi", new[] { IPAddress.Parse("127.0.0.7") })
        };
        var job = NewJob(item, options);

        var run = job.RunAsync(_cts.Token);
        await TestUtil.WaitUntilAsync(() => job.GetSegments().Count(s => s.Connection > 0) == 2, "both connections to own a part");
        var owned = job.GetSegments().Where(s => s.Connection > 0).OrderBy(s => s.Connection).ToList();
        Assert.Equal(new[] { 1, 2 }, owned.Select(s => s.Connection));
        Assert.Equal(new[] { "Ethernet", "Wi-Fi" }, owned.Select(s => s.Route));
        await run;

        Assert.Equal(DownloadStatus.Completed, item.Status);
    }

    [Fact]
    public async Task MoreRoutesThanConnections_StillUsesEveryRoute()
    {
        var data = TestUtil.RandomBytes(1024 * 1024, 52);
        var url = _server.AddFile("/routes.bin", data);
        var item = NewItem(url);
        var options = TestUtil.Options(TempDir, connections: 1);
        options.Routes = new[]
        {
            new NetworkRoute("a", "A", new[] { IPAddress.Parse("127.0.0.4") }),
            new NetworkRoute("b", "B", new[] { IPAddress.Parse("127.0.0.5") })
        };
        var job = NewJob(item, options);

        await job.RunAsync(_cts.Token);

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.Equal(TestUtil.Sha256(data), TestUtil.Sha256File(item.FullPath));
        Assert.Equal(2, job.Connections.Count);
    }

    [Fact]
    public async Task RouteWithoutMatchingAddressFamily_FallsBackToDefaultRoute()
    {
        var data = TestUtil.RandomBytes(512 * 1024, 53);
        var url = _server.AddFile("/fallback.bin", data);
        var item = NewItem(url);
        var options = TestUtil.Options(TempDir, connections: 2);
        options.Routes = new[] { new NetworkRoute("v6", "IPv6 only", new[] { IPAddress.IPv6Loopback }) };
        var job = NewJob(item, options);

        await job.RunAsync(_cts.Token);

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.Equal(TestUtil.Sha256(data), TestUtil.Sha256File(item.FullPath));
        Assert.All(job.Connections, c => Assert.Equal("IPv6 only", c.Route));
    }

    [Fact]
    public async Task ResumedDownload_SurvivesAStrayFullResponse_WithoutLosingProgress()
    {
        var data = TestUtil.RandomBytes(3 * 1024 * 1024, 61);
        _server.DelayPerChunkMs = 15;
        var url = _server.AddFile("/stray.bin", data);
        var item = NewItem(url);
        var first = NewJob(item);
        var run = first.RunAsync(_cts.Token);
        await TestUtil.WaitUntilAsync(() => SumDownloaded(first) > 512 * 1024, "some progress");
        first.Pause();
        await run;
        var kept = item.DownloadedBytes;
        Assert.Equal(DownloadStatus.Paused, item.Status);
        _server.DelayPerChunkMs = 0;
        var next = _server.RequestCount + 1;
        for (int i = next + 1; i <= next + 3; i++) _server.IgnoreRangeOnRequests[i] = true;
        var options = TestUtil.Options(TempDir);

        await NewJob(item, options).RunAsync(_cts.Token);

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.Equal(TestUtil.Sha256(data), TestUtil.Sha256File(item.FullPath));
        Assert.True(_server.BytesServed < 2L * data.Length + kept, "progress was thrown away and the file re-downloaded");
    }

    [Fact]
    public async Task AdapterTheServerRejects_GivesUpItsParts_AndOthersFinishTheFile()
    {
        var data = TestUtil.RandomBytes(3 * 1024 * 1024, 63);
        var url = _server.AddFile("/ipbound.bin", data);
        var good = IPAddress.Parse("127.0.0.8");
        var rejected = IPAddress.Parse("127.0.0.9");
        _server.IgnoreRangeForClient = rejected;
        _server.DelayPerChunkMs = 5;
        var item = NewItem(url);
        var options = TestUtil.Options(TempDir);
        options.Routes = new[]
        {
            new NetworkRoute("eth", "Ethernet", new[] { good }),
            new NetworkRoute("wifi", "Wi-Fi", new[] { rejected })
        };
        var job = NewJob(item, options);

        await job.RunAsync(_cts.Token);

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.Equal(TestUtil.Sha256(data), TestUtil.Sha256File(item.FullPath));
        Assert.All(job.Connections.Where(c => c.Route == "Wi-Fi"), c => Assert.StartsWith("Gave up", c.Info));
        Assert.Equal(new[] { "Ethernet" }, job.GetPieces().Select(p => p.Route).Distinct());
        Assert.Equal(new[] { "Wi-Fi" }, job.GetRejectedRoutes());
    }

    [Fact]
    public async Task RangesRefusedAfterProbe_RestartsWithOneConnection_InsteadOfCancelling()
    {
        var data = TestUtil.RandomBytes(2 * 1024 * 1024, 62);
        var url = _server.AddFile("/norange.bin", data);
        _server.IgnoreRangeFromRequest = 2;
        var item = NewItem(url);

        await NewJob(item).RunAsync(_cts.Token);

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.Equal(TestUtil.Sha256(data), TestUtil.Sha256File(item.FullPath));
    }

    [Fact]
    public async Task MissingReferrer_SendsTheUrlOrigin_AndExplicitReferrerWins()
    {
        var data = TestUtil.RandomBytes(64 * 1024, 54);
        var url = _server.AddFile("/ref.bin", data);
        var implicitItem = NewItem(url, "implicit.bin");
        await NewJob(implicitItem).RunAsync(_cts.Token);
        Assert.Equal(DownloadStatus.Completed, implicitItem.Status);
        Assert.All(_server.Referers, r => Assert.Equal(_server.BaseUrl, r));

        _server.Referers.Clear();
        var explicitItem = NewItem(url, "explicit.bin");
        explicitItem.Referrer = "https://example.com/page";
        await NewJob(explicitItem).RunAsync(_cts.Token);
        Assert.Equal(DownloadStatus.Completed, explicitItem.Status);
        Assert.All(_server.Referers, r => Assert.Equal("https://example.com/page", r));
    }

    public void Dispose()
    {
        _cts.Dispose();
        _server.Dispose();
        TestUtil.DeleteDir(_root);
    }
}
