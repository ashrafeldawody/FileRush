using FileRush.Core.Models;

namespace FileRush.Core.Engine;

public sealed class RangeResponse : IDisposable
{
    private readonly IDisposable? _owner;

    public RangeResponse(Stream stream, long start, long totalLength, bool isPartial, IDisposable? owner)
    {
        Stream = stream;
        Start = start;
        TotalLength = totalLength;
        IsPartial = isPartial;
        _owner = owner;
    }

    public Stream Stream { get; }
    public long Start { get; }
    public long TotalLength { get; }
    public bool IsPartial { get; }
    public string? ETag { get; init; }
    public string? LastModified { get; init; }
    public string? ContentDisposition { get; init; }
    public string? ContentType { get; init; }
    public string? FinalUrl { get; init; }
    public bool AcceptRanges { get; init; }

    public void Dispose()
    {
        try { Stream.Dispose(); } catch { }
        try { _owner?.Dispose(); } catch { }
    }
}

public interface IRangeSource : IDisposable
{
    Task<RangeResponse> OpenAsync(long start, long end, CancellationToken ct);
}

public interface IRangeSourceFactory
{
    IRangeSource Create(DownloadItem item, DownloadEngineOptions options);
}
