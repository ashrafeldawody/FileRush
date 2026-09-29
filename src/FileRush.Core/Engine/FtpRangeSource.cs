using System.Net;
using FileRush.Core.Models;

namespace FileRush.Core.Engine;

public sealed class FtpRangeSource : IRangeSource
{
    private readonly DownloadItem _item;
    private readonly DownloadEngineOptions _options;
    private readonly NetworkCredential? _credential;
    private long _cachedSize = -2;

    public FtpRangeSource(DownloadItem item, DownloadEngineOptions options, NetworkCredential? credential)
    {
        _item = item;
        _options = options;
        _credential = credential;
    }

    public async Task<RangeResponse> OpenAsync(long start, long end, CancellationToken ct)
    {
        var size = await GetSizeAsync(ct).ConfigureAwait(false);
        var request = CreateRequest(WebRequestMethods.Ftp.DownloadFile);
        request.ContentOffset = start;
        using var registration = ct.Register(request.Abort);
        FtpWebResponse response;
        try
        {
            response = (FtpWebResponse)await request.GetResponseAsync().WaitAsync(_options.Timeout, ct).ConfigureAwait(false);
        }
        catch (WebException ex) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ex.Message, ex, ct);
        }
        catch (WebException ex) when (start > 0 && ex.Response is FtpWebResponse { StatusCode: FtpStatusCode.CommandSyntaxError or FtpStatusCode.CommandNotImplemented })
        {
            throw new RangeNotSupportedException();
        }
        var stream = response.GetResponseStream();
        return new RangeResponse(stream, start, size, start > 0, response)
        {
            AcceptRanges = true,
            FinalUrl = _item.Url,
            LastModified = response.LastModified == default ? null : response.LastModified.ToUniversalTime().ToString("R")
        };
    }

    private async Task<long> GetSizeAsync(CancellationToken ct)
    {
        if (_cachedSize != -2) return _cachedSize;
        try
        {
            var request = CreateRequest(WebRequestMethods.Ftp.GetFileSize);
            using var registration = ct.Register(request.Abort);
            using var response = (FtpWebResponse)await request.GetResponseAsync().WaitAsync(_options.Timeout, ct).ConfigureAwait(false);
            _cachedSize = response.ContentLength;
        }
        catch (WebException) when (!ct.IsCancellationRequested)
        {
            _cachedSize = -1;
        }
        return _cachedSize;
    }

    private FtpWebRequest CreateRequest(string method)
    {
        var request = (FtpWebRequest)WebRequest.Create(_item.Url);
        request.Method = method;
        request.UseBinary = true;
        request.UsePassive = _options.FtpPassive;
        request.KeepAlive = false;
        request.Timeout = (int)_options.Timeout.TotalMilliseconds;
        request.ReadWriteTimeout = (int)_options.Timeout.TotalMilliseconds;
        request.Credentials = _credential ?? new NetworkCredential("anonymous", "filerush@example.com");
        if (_options.Proxy is not null) request.Proxy = _options.Proxy;
        return request;
    }

    public void Dispose()
    {
    }
}
