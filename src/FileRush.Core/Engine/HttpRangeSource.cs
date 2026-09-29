using System.Net;
using System.Net.Http.Headers;
using FileRush.Core.Models;

namespace FileRush.Core.Engine;

public sealed class HttpRangeSource : IRangeSource
{
    private readonly HttpClient _client;
    private readonly DownloadItem _item;
    private readonly DownloadEngineOptions _options;
    private readonly bool _ownsClient;

    public HttpRangeSource(HttpClient client, DownloadItem item, DownloadEngineOptions options, bool ownsClient = true)
    {
        _client = client;
        _item = item;
        _options = options;
        _ownsClient = ownsClient;
    }

    public async Task<RangeResponse> OpenAsync(long start, long end, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _item.Url);
        request.Headers.Range = new RangeHeaderValue(start, end >= 0 ? end : null);
        ApplyHeaders(request);

        using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        headerCts.CancelAfter(_options.Timeout);
        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"No response from server within {_options.Timeout.TotalSeconds:F0} seconds.");
        }

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            response.Dispose();
            throw new DownloadFailedException("Requested range not satisfiable (416).");
        }
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            var reason = response.ReasonPhrase;
            response.Dispose();
            throw new HttpRequestException($"HTTP {(int)status} {reason}", null, status);
        }

        var partial = response.StatusCode == HttpStatusCode.PartialContent;
        if (!partial && start > 0)
        {
            response.Dispose();
            throw new RangeNotSupportedException();
        }

        long total = -1;
        long responseStart = 0;
        if (partial && response.Content.Headers.ContentRange is { } contentRange)
        {
            responseStart = contentRange.From ?? 0;
            total = contentRange.Length ?? -1;
            if (total < 0 && contentRange.To is { } to && end < 0) total = to + 1;
        }
        else if (response.Content.Headers.ContentLength is { } length)
        {
            total = length;
        }
        if (partial && responseStart != start)
        {
            response.Dispose();
            throw new DownloadFailedException($"Server returned wrong range start {responseStart}, expected {start}.");
        }

        Stream stream;
        try
        {
            stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            response.Dispose();
            throw;
        }

        string? disposition = null;
        if (response.Content.Headers.TryGetValues("Content-Disposition", out var dispositions))
        {
            disposition = dispositions.FirstOrDefault();
        }
        var acceptRanges = partial || response.Headers.AcceptRanges.Any(v => v.Equals("bytes", StringComparison.OrdinalIgnoreCase));
        return new RangeResponse(stream, start, total, partial, response)
        {
            ETag = response.Headers.ETag?.Tag,
            LastModified = response.Content.Headers.LastModified?.UtcDateTime.ToString("R"),
            ContentDisposition = disposition,
            ContentType = response.Content.Headers.ContentType?.ToString(),
            FinalUrl = response.RequestMessage?.RequestUri?.ToString() ?? _item.Url,
            AcceptRanges = acceptRanges
        };
    }

    private void ApplyHeaders(HttpRequestMessage request)
    {
        var agent = string.IsNullOrWhiteSpace(_item.UserAgent) ? _options.UserAgent : _item.UserAgent;
        request.Headers.TryAddWithoutValidation("User-Agent", agent);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        if (!string.IsNullOrWhiteSpace(_item.Referrer))
        {
            request.Headers.TryAddWithoutValidation("Referer", _item.Referrer);
        }
        foreach (var (key, value) in _item.Headers)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            request.Headers.Remove(key);
            request.Headers.TryAddWithoutValidation(key, value);
        }
    }

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}
