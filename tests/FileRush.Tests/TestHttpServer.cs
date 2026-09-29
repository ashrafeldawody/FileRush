using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace FileRush.Tests;

public sealed class ServedFile
{
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public string ContentType { get; init; } = "application/octet-stream";
    public string? ContentDisposition { get; init; }
}

public sealed class TestHttpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<string, ServedFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private int _requestCount;
    private int _bodyResponses;
    private long _bytesServed;

    public TestHttpServer()
    {
        Port = GetFreePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    public string BaseUrl => $"http://127.0.0.1:{Port}/";

    public bool SupportRanges { get; set; } = true;

    public bool Chunked { get; set; }

    public int FailFirstRequests { get; set; }

    public long DropAfterBytes { get; set; } = -1;

    public int DropResponses { get; set; } = 1;

    public int DelayPerChunkMs { get; set; }

    public long? SlowRangeStart { get; set; }

    public int SlowChunkDelayMs { get; set; }

    public int? ResponseStatusOverride { get; set; }

    public string? ETag { get; set; }

    public string? LastModified { get; set; }

    public int ChunkSize { get; set; } = 64 * 1024;

    public ConcurrentQueue<string?> RangeHeaders { get; } = new();

    public int RequestCount => Volatile.Read(ref _requestCount);

    public long BytesServed => Interlocked.Read(ref _bytesServed);

    public string AddFile(string path, byte[] data, string? contentDisposition = null, string contentType = "application/octet-stream")
    {
        var normalized = "/" + path.TrimStart('/');
        _files[normalized] = new ServedFile { Data = data, ContentType = contentType, ContentDisposition = contentDisposition };
        return UrlFor(normalized);
    }

    public string UrlFor(string path) => BaseUrl.TrimEnd('/') + "/" + path.TrimStart('/');

    public static long ParseRangeStart(string? header)
    {
        if (string.IsNullOrEmpty(header) || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return -1;
        var spec = header[6..];
        var dash = spec.IndexOf('-');
        if (dash <= 0) return -1;
        return long.TryParse(spec[..dash], out var start) ? start : -1;
    }

    private static int GetFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch
            {
                if (_cts.IsCancellationRequested) return;
                continue;
            }
            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var number = Interlocked.Increment(ref _requestCount);
        var request = context.Request;
        var response = context.Response;
        var rangeHeader = request.Headers["Range"];
        RangeHeaders.Enqueue(rangeHeader);
        try
        {
            if (ResponseStatusOverride is { } status)
            {
                response.StatusCode = status;
                response.Close();
                return;
            }
            if (number <= FailFirstRequests)
            {
                response.StatusCode = 500;
                response.Close();
                return;
            }
            if (!_files.TryGetValue(request.Url!.AbsolutePath, out var file))
            {
                response.StatusCode = 404;
                response.Close();
                return;
            }
            var data = file.Data;
            long start = 0;
            long end = data.Length - 1;
            var partial = false;
            if (SupportRanges && TryParseRange(rangeHeader, data.Length, out var rangeStart, out var rangeEnd))
            {
                if (rangeStart >= data.Length)
                {
                    response.StatusCode = 416;
                    response.Headers["Content-Range"] = $"bytes */{data.Length}";
                    response.Close();
                    return;
                }
                start = rangeStart;
                end = rangeEnd;
                partial = true;
            }
            response.Headers["Accept-Ranges"] = SupportRanges ? "bytes" : "none";
            if (ETag is not null) response.Headers["ETag"] = ETag;
            if (LastModified is not null) response.Headers["Last-Modified"] = LastModified;
            if (file.ContentDisposition is not null) response.Headers["Content-Disposition"] = file.ContentDisposition;
            response.ContentType = file.ContentType;
            var count = end - start + 1;
            if (partial)
            {
                response.StatusCode = 206;
                response.Headers["Content-Range"] = $"bytes {start}-{end}/{data.Length}";
            }
            else
            {
                response.StatusCode = 200;
            }
            if (Chunked) response.SendChunked = true;
            else response.ContentLength64 = count;
            if (request.HttpMethod == "HEAD")
            {
                response.Close();
                return;
            }
            var bodyIndex = Interlocked.Increment(ref _bodyResponses);
            var drop = DropAfterBytes >= 0 && bodyIndex <= DropResponses;
            var delay = SlowRangeStart is { } slow && slow == start ? SlowChunkDelayMs : DelayPerChunkMs;
            var offset = start;
            long written = 0;
            var output = response.OutputStream;
            while (offset <= end)
            {
                var chunk = (int)Math.Min(ChunkSize, end - offset + 1);
                if (drop && written + chunk > DropAfterBytes)
                {
                    response.Abort();
                    return;
                }
                await output.WriteAsync(data.AsMemory((int)offset, chunk)).ConfigureAwait(false);
                await output.FlushAsync().ConfigureAwait(false);
                offset += chunk;
                written += chunk;
                Interlocked.Add(ref _bytesServed, chunk);
                if (delay > 0) await Task.Delay(delay).ConfigureAwait(false);
            }
            response.Close();
        }
        catch
        {
            try { response.Abort(); } catch { }
        }
    }

    private static bool TryParseRange(string? header, long length, out long start, out long end)
    {
        start = 0;
        end = length - 1;
        if (string.IsNullOrEmpty(header) || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
        var spec = header[6..].Split(',')[0].Trim();
        var dash = spec.IndexOf('-');
        if (dash < 0) return false;
        var left = spec[..dash];
        var right = spec[(dash + 1)..];
        if (left.Length == 0)
        {
            if (!long.TryParse(right, out var suffix)) return false;
            start = Math.Max(0, length - suffix);
            end = length - 1;
            return true;
        }
        if (!long.TryParse(left, out start)) return false;
        if (right.Length > 0 && long.TryParse(right, out var parsedEnd)) end = Math.Min(parsedEnd, length - 1);
        else end = length - 1;
        return true;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        try { _acceptLoop.Wait(2000); } catch { }
    }
}
