using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FileRush.Core.Models;
using FileRush.Core.Utils;
using Microsoft.Win32.SafeHandles;

namespace FileRush.Core.Engine;

public sealed class DownloadJob
{
    private readonly DownloadItem _item;
    private readonly DownloadEngineOptions _options;
    private readonly SpeedLimiter _limiter;
    private readonly IRangeSourceFactory _factory;
    private readonly object _gate = new();
    private readonly List<Segment> _segments = new();
    private readonly HashSet<Segment> _assigned = new();
    private readonly List<ConnectionInfo> _connections = new();
    private readonly SpeedMeter _meter = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _cts;
    private SafeFileHandle? _file;
    private Exception? _fatal;
    private volatile bool _pauseRequested;
    private bool _rangeDisabled;
    private long _startedTicks;

    public DownloadJob(DownloadItem item, DownloadEngineOptions options, SpeedLimiter limiter, IRangeSourceFactory? factory = null)
    {
        _item = item;
        _options = options;
        _limiter = limiter;
        _factory = factory ?? new RangeSourceFactory();
    }

    public event Action<DownloadJob>? ProgressChanged;

    public event Action<DownloadJob>? StateChanged;

    public DownloadItem Item => _item;

    public Task Completion => _completion.Task;

    public bool IsRunning => _cts is not null && !_completion.Task.IsCompleted;

    public IReadOnlyList<ConnectionInfo> Connections => _connections;

    public TimeSpan Elapsed => TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - _startedTicks) / (double)Stopwatch.Frequency);

    public IReadOnlyList<SegmentSnapshot> GetSegments()
    {
        lock (_gate)
        {
            return _segments.Select(s => new SegmentSnapshot(s.Start, s.End, s.Position, _assigned.Contains(s))).ToList();
        }
    }

    public void Pause()
    {
        _pauseRequested = true;
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    public async Task RunAsync(CancellationToken external = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(external);
        var ct = _cts.Token;
        _startedTicks = Stopwatch.GetTimestamp();
        _item.Status = DownloadStatus.Connecting;
        _item.StatusText = "Sending request...";
        _item.ErrorMessage = null;
        _item.LastTryAt = DateTime.Now;
        _item.Speed = 0;
        _item.TimeLeft = null;
        try
        {
            await ExecuteWithRestartsAsync(ct).ConfigureAwait(false);
            _item.Status = DownloadStatus.Completed;
            _item.StatusText = "Complete";
            _item.CompletedAt = DateTime.Now;
        }
        catch (OperationCanceledException) when (_pauseRequested || external.IsCancellationRequested)
        {
            _item.Status = DownloadStatus.Paused;
            _item.StatusText = "Paused";
        }
        catch (Exception ex)
        {
            _item.Status = DownloadStatus.Error;
            _item.ErrorMessage = Describe(ex);
            _item.StatusText = "Error: " + _item.ErrorMessage;
        }
        finally
        {
            CloseFile();
            _item.Speed = 0;
            _item.TimeLeft = null;
            _item.ActiveConnections = 0;
            foreach (var c in _connections) c.Active = false;
            SyncSegmentsToItem();
            _cts.Dispose();
            StateChanged?.Invoke(this);
            ProgressChanged?.Invoke(this);
            _completion.TrySetResult();
        }
    }

    private async Task ExecuteWithRestartsAsync(CancellationToken ct)
    {
        var restartedForChange = false;
        while (true)
        {
            try
            {
                await DownloadAsync(ct).ConfigureAwait(false);
                return;
            }
            catch (RangeNotSupportedException) when (!_rangeDisabled)
            {
                _rangeDisabled = true;
                ResetForRestart();
            }
            catch (RemoteFileChangedException) when (!restartedForChange)
            {
                restartedForChange = true;
                ResetForRestart();
            }
        }
    }

    private void ResetForRestart()
    {
        lock (_gate)
        {
            _segments.Clear();
            _assigned.Clear();
            _item.SupportsResume = false;
            _item.ResetProgress();
        }
        CloseFile();
        DeletePartial();
    }

    private async Task DownloadAsync(CancellationToken ct)
    {
        using var source = _factory.Create(_item, _options);
        _item.StatusText = "Sending request...";

        long probeStart = 0, probeEnd = -1;
        var candidate = ResumeCandidate();
        if (candidate is not null)
        {
            probeStart = candidate.Position;
            probeEnd = candidate.End;
        }

        RangeResponse? probe = await OpenWithRetryAsync(source, probeStart, probeEnd, ct).ConfigureAwait(false);
        try
        {
            var info = UrlProber.FromResponse(_item.Url, probe);
            var resuming = TryResume(info);
            if (!resuming)
            {
                if (probeStart > 0)
                {
                    probe.Dispose();
                    probe = await OpenWithRetryAsync(source, 0, -1, ct).ConfigureAwait(false);
                    info = UrlProber.FromResponse(_item.Url, probe);
                }
                StartFresh(info);
            }
            OpenFile();

            var connectionCount = _item.SupportsResume && _item.TotalSize > 0 ? EffectiveMaxConnections() : 1;
            _connections.Clear();
            for (int i = 0; i < connectionCount; i++)
            {
                _connections.Add(new ConnectionInfo { Number = i + 1 });
            }

            _item.Status = DownloadStatus.Downloading;
            _item.StatusText = "Receiving data...";
            _meter.Reset();
            _meter.Update(TotalDownloaded());

            using var progressCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var progressTask = ProgressLoopAsync(progressCts.Token);
            var workers = new Task[connectionCount];
            for (int i = 0; i < connectionCount; i++)
            {
                var initial = i == 0 ? probe : null;
                probe = null;
                workers[i] = WorkerAsync(i, source, initial, ct);
            }
            try
            {
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                progressCts.Cancel();
                try { await progressTask.ConfigureAwait(false); } catch { }
            }

            var fatal = Interlocked.Exchange(ref _fatal, null);
            if (fatal is not null)
            {
                if (fatal is RangeNotSupportedException or RemoteFileChangedException or DownloadFailedException) throw fatal;
                throw new DownloadFailedException(Describe(fatal), fatal);
            }
            ct.ThrowIfCancellationRequested();
            foreach (var w in workers)
            {
                if (w.IsFaulted) throw w.Exception!.GetBaseException();
            }

            FinishFile();
        }
        finally
        {
            probe?.Dispose();
        }
    }

    private Segment? ResumeCandidate()
    {
        lock (_gate)
        {
            if (!_item.SupportsResume || _item.Segments.Count == 0) return null;
            if (string.IsNullOrEmpty(_item.PartialPath) || !File.Exists(_item.PartialPath)) return null;
            return _item.Segments.FirstOrDefault(s => !s.IsComplete);
        }
    }

    private bool TryResume(RemoteFileInfo info)
    {
        lock (_gate)
        {
            if (_rangeDisabled) return false;
            if (!_item.SupportsResume || _item.Segments.Count == 0) return false;
            if (string.IsNullOrEmpty(_item.PartialPath) || !File.Exists(_item.PartialPath)) return false;
            if (!ResumeIsValid(info)) return false;
            _segments.Clear();
            _assigned.Clear();
            foreach (var seg in _item.Segments)
            {
                _segments.Add(seg.Clone());
            }
            return true;
        }
    }

    private void StartFresh(RemoteFileInfo info)
    {
        lock (_gate)
        {
            _item.ResetProgress();
            _item.TotalSize = info.Size;
            _item.ETag = info.ETag;
            _item.LastModified = info.LastModified;
            _item.ContentType = info.ContentType;
            _item.FinalUrl = info.FinalUrl;
            _item.SupportsResume = info.SupportsRange && !_rangeDisabled;
            if (string.IsNullOrWhiteSpace(_item.FileName)) _item.FileName = info.FileName;
            _segments.Clear();
            _assigned.Clear();
            if (_item.SupportsResume && info.Size > 0)
            {
                _segments.AddRange(CreateInitialSegments(info.Size, EffectiveMaxConnections()));
            }
            else
            {
                _segments.Add(new Segment(0, info.Size > 0 ? info.Size - 1 : -1));
            }
            _item.Segments = _segments.Select(s => s.Clone()).ToList();
            _item.PartialPath = BuildPartialPath();
            _item.DownloadedBytes = 0;
        }
    }

    private bool ResumeIsValid(RemoteFileInfo info)
    {
        if (!info.SupportsRange) return false;
        if (info.Size != _item.TotalSize) return false;
        if (_options.IgnoreModificationTime) return true;
        if (!string.IsNullOrEmpty(_item.ETag) && !string.IsNullOrEmpty(info.ETag))
        {
            return string.Equals(_item.ETag, info.ETag, StringComparison.Ordinal);
        }
        if (!string.IsNullOrEmpty(_item.LastModified) && !string.IsNullOrEmpty(info.LastModified))
        {
            return string.Equals(_item.LastModified, info.LastModified, StringComparison.Ordinal);
        }
        return true;
    }

    private int EffectiveMaxConnections()
    {
        var max = _item.MaxConnections > 0 ? _item.MaxConnections : _options.MaxConnections;
        return Math.Clamp(max, 1, 32);
    }

    public IEnumerable<Segment> CreateInitialSegments(long size, int connections)
    {
        var count = (int)Math.Clamp(Math.Min(connections, size / Math.Max(1, _options.MinSegmentSize)), 1, 32);
        var part = size / count;
        long start = 0;
        for (int i = 0; i < count; i++)
        {
            var end = i == count - 1 ? size - 1 : start + part - 1;
            yield return new Segment(start, end);
            start = end + 1;
        }
    }

    private string BuildPartialPath()
    {
        var name = string.IsNullOrWhiteSpace(_item.FileName) ? "download" : _item.FileName;
        if (!string.IsNullOrWhiteSpace(_options.TempDirectory))
        {
            return Path.Combine(_options.TempDirectory, $"{_item.Id:N}_{name}.part");
        }
        return Path.Combine(_item.SaveDirectory, name + ".part");
    }

    private void OpenFile()
    {
        var path = _item.PartialPath!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _file = File.OpenHandle(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, FileOptions.Asynchronous);
        if (_item.TotalSize > 0 && RandomAccess.GetLength(_file) != _item.TotalSize)
        {
            RandomAccess.SetLength(_file, _item.TotalSize);
        }
    }

    private void CloseFile()
    {
        var file = _file;
        _file = null;
        file?.Dispose();
    }

    private void DeletePartial()
    {
        try
        {
            if (!string.IsNullOrEmpty(_item.PartialPath) && File.Exists(_item.PartialPath)) File.Delete(_item.PartialPath);
        }
        catch
        {
        }
    }

    private void FinishFile()
    {
        long total;
        lock (_gate)
        {
            total = _segments.Sum(s => s.Downloaded);
            if (_item.TotalSize > 0 && (_segments.Any(s => !s.IsComplete) || total != _item.TotalSize))
            {
                throw new DownloadFailedException($"Download incomplete: {total} of {_item.TotalSize} bytes received.");
            }
            if (_item.TotalSize <= 0) _item.TotalSize = total;
        }
        CloseFile();
        var partial = _item.PartialPath!;
        if (new FileInfo(partial).Length != total)
        {
            using var fs = new FileStream(partial, FileMode.Open, FileAccess.Write, FileShare.Read);
            fs.SetLength(total);
        }
        Directory.CreateDirectory(_item.SaveDirectory);
        var name = FileNameResolver.MakeUnique(_item.SaveDirectory, _item.FileName);
        if (name != _item.FileName) _item.FileName = name;
        File.Move(partial, _item.FullPath, false);
        _item.PartialPath = null;
        _item.DownloadedBytes = total;
        _item.Segments = new List<Segment>();
    }

    private async Task WorkerAsync(int index, IRangeSource source, RangeResponse? initial, CancellationToken ct)
    {
        var connection = _connections[index];
        Segment? segment = null;
        if (initial is not null)
        {
            lock (_gate)
            {
                segment = _segments.FirstOrDefault(s => !s.IsComplete && !_assigned.Contains(s));
                if (segment is not null) _assigned.Add(segment);
            }
            if (segment is null)
            {
                initial.Dispose();
                initial = null;
            }
        }
        try
        {
            while (!ct.IsCancellationRequested)
            {
                segment ??= AcquireSegment();
                if (segment is null) break;
                connection.Active = true;
                var yielded = false;
                try
                {
                    await DownloadSegmentAsync(segment, connection, source, initial, ct).ConfigureAwait(false);
                    initial = null;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (ConnectionYieldException)
                {
                    yielded = true;
                    initial = null;
                }
                catch (Exception ex)
                {
                    connection.Info = "Error: " + Describe(ex);
                    Fail(ex);
                    throw;
                }
                finally
                {
                    lock (_gate) _assigned.Remove(segment);
                    connection.Active = false;
                }
                segment = null;
                if (yielded) break;
                connection.Info = "Completed";
            }
        }
        finally
        {
            initial?.Dispose();
            connection.Active = false;
        }
    }

    private void Fail(Exception ex)
    {
        Interlocked.CompareExchange(ref _fatal, ex, null);
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private Segment? AcquireSegment()
    {
        lock (_gate)
        {
            var free = _segments.FirstOrDefault(s => !s.IsComplete && !_assigned.Contains(s));
            if (free is not null)
            {
                _assigned.Add(free);
                return free;
            }
            if (!_item.SupportsResume) return null;
            Segment? largest = null;
            foreach (var s in _segments)
            {
                if (s.IsComplete || s.IsOpenEnded) continue;
                if (largest is null || s.Remaining > largest.Remaining) largest = s;
            }
            if (largest is null || largest.Remaining < 2 * _options.MinSegmentSize) return null;
            var mid = largest.Position + largest.Remaining / 2;
            var created = new Segment(mid, largest.End);
            largest.End = mid - 1;
            _segments.Insert(_segments.IndexOf(largest) + 1, created);
            _assigned.Add(created);
            return created;
        }
    }

    private async Task DownloadSegmentAsync(Segment segment, ConnectionInfo connection, IRangeSource source, RangeResponse? initial, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            long start, end;
            lock (_gate)
            {
                start = segment.Position;
                end = segment.End;
            }
            if (end >= 0 && start > end)
            {
                initial?.Dispose();
                return;
            }

            RangeResponse? response = initial;
            initial = null;
            try
            {
                if (response is null)
                {
                    connection.Info = "Connecting...";
                    response = await source.OpenAsync(start, end, ct).ConfigureAwait(false);
                }
                ValidateResponse(response, start);
                connection.Info = "Receiving data...";
                await CopyAsync(segment, connection, response.Stream, ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (RangeNotSupportedException)
            {
                throw;
            }
            catch (RemoteFileChangedException)
            {
                throw;
            }
            catch (Exception ex) when (IsRetryable(ex))
            {
                if (ShouldYieldConnection(ex))
                {
                    connection.Info = $"Server limits connections ({Describe(ex)}); connection released";
                    throw new ConnectionYieldException();
                }
                attempt++;
                if (attempt > _options.MaxRetries)
                {
                    throw new DownloadFailedException(Describe(ex), ex);
                }
                var delay = RetryDelayFor(ex, attempt);
                connection.Info = $"Error: {Describe(ex)}. Retry {attempt}/{_options.MaxRetries} in {delay.TotalSeconds:F0}s";
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            finally
            {
                response?.Dispose();
            }
        }
    }

    private void ValidateResponse(RangeResponse response, long start)
    {
        if (!response.IsPartial && start > 0) throw new RangeNotSupportedException();
        if (_options.IgnoreModificationTime) return;
        if (!string.IsNullOrEmpty(_item.ETag) && !string.IsNullOrEmpty(response.ETag) && !string.Equals(_item.ETag, response.ETag, StringComparison.Ordinal))
        {
            throw new RemoteFileChangedException();
        }
        if (_item.TotalSize > 0 && response.TotalLength > 0 && response.TotalLength != _item.TotalSize)
        {
            throw new RemoteFileChangedException();
        }
    }

    private async Task CopyAsync(Segment segment, ConnectionInfo connection, Stream stream, CancellationToken ct)
    {
        var bufferSize = _options.BufferSize;
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            while (true)
            {
                readCts.CancelAfter(_options.Timeout);
                int read;
                try
                {
                    read = await stream.ReadAsync(buffer.AsMemory(0, bufferSize), readCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException($"Connection timed out after {_options.Timeout.TotalSeconds:F0} seconds of inactivity.");
                }
                if (read == 0)
                {
                    lock (_gate)
                    {
                        if (segment.IsOpenEnded)
                        {
                            segment.End = segment.Position - 1;
                            _item.TotalSize = segment.Position;
                            return;
                        }
                        if (segment.IsComplete) return;
                    }
                    throw new IOException("Connection closed before the segment was fully received.");
                }
                long position, end;
                lock (_gate)
                {
                    position = segment.Position;
                    end = segment.End;
                }
                var allowed = end < 0 ? read : (int)Math.Min(read, end - position + 1);
                if (allowed <= 0) return;
                await _limiter.WaitAsync(allowed, ct).ConfigureAwait(false);
                await RandomAccess.WriteAsync(_file!, buffer.AsMemory(0, allowed), position, ct).ConfigureAwait(false);
                bool done;
                lock (_gate)
                {
                    segment.Position = position + allowed;
                    if (!segment.IsOpenEnded && segment.Position > segment.End + 1)
                    {
                        segment.Position = segment.End + 1;
                    }
                    done = segment.IsComplete;
                }
                connection.Downloaded += allowed;
                if (done) return;
            }
        }
        finally
        {
            readCts.CancelAfter(Timeout.InfiniteTimeSpan);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<RangeResponse> OpenWithRetryAsync(IRangeSource source, long start, long end, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await source.OpenAsync(start, end, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (RangeNotSupportedException)
            {
                throw;
            }
            catch (Exception ex) when (IsRetryable(ex))
            {
                attempt++;
                if (attempt > _options.MaxRetries) throw new DownloadFailedException(Describe(ex), ex);
                _item.StatusText = $"Error: {Describe(ex)}. Retrying ({attempt}/{_options.MaxRetries})...";
                await Task.Delay(RetryDelayFor(ex, attempt), ct).ConfigureAwait(false);
            }
        }
    }

    private TimeSpan RetryDelayFor(Exception ex, int attempt)
    {
        var throttled = ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable };
        if (!throttled) return _options.RetryDelay;
        var factor = Math.Min(attempt, 6);
        var delay = _options.RetryDelay * factor;
        return delay > TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60) : delay;
    }

    private bool ShouldYieldConnection(Exception ex)
    {
        if (ex is not HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable }) return false;
        var active = 0;
        foreach (var c in _connections) if (c.Active) active++;
        return active > 1;
    }

    private static bool IsRetryable(Exception ex)
    {
        return ex switch
        {
            DownloadFailedException => false,
            HttpRequestException { StatusCode: { } code } => (int)code is 408 or 425 or 429 or >= 500,
            HttpRequestException => true,
            IOException => true,
            SocketException => true,
            TimeoutException => true,
            WebException => true,
            OperationCanceledException => true,
            _ => false
        };
    }

    private static string Describe(Exception ex)
    {
        var e = ex;
        while (e is AggregateException { InnerException: { } inner }) e = inner;
        if (e is DownloadFailedException && e.InnerException is not null && e.Message == e.InnerException.Message) e = e.InnerException;
        return e switch
        {
            HttpRequestException { StatusCode: { } code } => $"HTTP {(int)code} {code}",
            HttpRequestException { InnerException: SocketException se } => se.Message,
            SocketException se => se.Message,
            _ => e.Message
        };
    }

    private long TotalDownloaded()
    {
        lock (_gate)
        {
            long total = 0;
            foreach (var s in _segments) total += s.Downloaded;
            return total;
        }
    }

    private void SyncSegmentsToItem()
    {
        lock (_gate)
        {
            if (_item.Status == DownloadStatus.Completed) return;
            _item.Segments = _segments.Select(s => s.Clone()).ToList();
            _item.DownloadedBytes = _segments.Sum(s => s.Downloaded);
        }
    }

    private async Task ProgressLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_options.ProgressInterval);
        var lastSave = Stopwatch.GetTimestamp();
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                PublishProgress();
                var now = Stopwatch.GetTimestamp();
                if ((now - lastSave) / (double)Stopwatch.Frequency >= _options.StateSaveInterval.TotalSeconds)
                {
                    lastSave = now;
                    SyncSegmentsToItem();
                    StateChanged?.Invoke(this);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void PublishProgress()
    {
        var downloaded = TotalDownloaded();
        _meter.Update(downloaded);
        var rate = _meter.Rate;
        _item.DownloadedBytes = downloaded;
        _item.Speed = rate;
        var total = _item.TotalSize;
        _item.TimeLeft = total > 0 && rate > 1 ? TimeSpan.FromSeconds((total - downloaded) / rate) : null;
        var active = 0;
        foreach (var c in _connections) if (c.Active) active++;
        _item.ActiveConnections = active;
        _item.Status = DownloadStatus.Downloading;
        _item.StatusText = total > 0 ? $"{_item.Progress:F2}%" : "Receiving data...";
        ProgressChanged?.Invoke(this);
    }
}
