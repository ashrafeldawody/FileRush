using FileRush.Core.Models;
using FileRush.Core.Utils;

namespace FileRush.Core.Engine;

public static class UrlProber
{
    public static async Task<RemoteFileInfo> ProbeAsync(DownloadItem item, DownloadEngineOptions options, IRangeSourceFactory factory, CancellationToken ct)
    {
        using var source = factory.Create(item, options);
        using var response = await source.OpenAsync(0, -1, ct).ConfigureAwait(false);
        return FromResponse(item.Url, response);
    }

    public static RemoteFileInfo FromResponse(string url, RangeResponse response)
    {
        var finalUrl = response.FinalUrl ?? url;
        return new RemoteFileInfo
        {
            Url = url,
            FinalUrl = finalUrl,
            FileName = FileNameResolver.Resolve(finalUrl, response.ContentDisposition, response.ContentType),
            Size = response.TotalLength,
            SupportsRange = response.AcceptRanges && response.TotalLength > 0,
            ETag = response.ETag,
            LastModified = response.LastModified,
            ContentType = response.ContentType
        };
    }
}
