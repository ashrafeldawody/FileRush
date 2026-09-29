using System.Text.RegularExpressions;
using FileRush.Core.Utils;

namespace FileRush.Core.Services;

public sealed class SiteGrabberOptions
{
    public string StartUrl { get; set; } = string.Empty;
    public int MaxDepth { get; set; } = 1;
    public int MaxPages { get; set; } = 200;
    public bool SameHostOnly { get; set; } = true;
    public HashSet<string> Extensions { get; set; } = new(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "png", "gif", "webp", "zip", "rar", "pdf", "mp3", "mp4" };
    public string? UserAgent { get; set; }
}

public sealed class SiteGrabberProgress
{
    public int PagesScanned { get; init; }
    public int FilesFound { get; init; }
    public string CurrentUrl { get; init; } = string.Empty;
}

public sealed partial class SiteGrabber
{
    private readonly HttpClient _client;

    public SiteGrabber(HttpClient? client = null)
    {
        _client = client ?? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, ConnectTimeout = TimeSpan.FromSeconds(20) })
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
    }

    public async Task<IReadOnlyList<string>> GrabAsync(SiteGrabberOptions options, IProgress<SiteGrabberProgress>? progress, CancellationToken ct)
    {
        if (!Uri.TryCreate(options.StartUrl, UriKind.Absolute, out var start)) return Array.Empty<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<string>();
        var fileSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(Uri Url, int Depth)>();
        queue.Enqueue((start, 0));
        visited.Add(start.AbsoluteUri);
        var pages = 0;
        while (queue.Count > 0 && pages < options.MaxPages)
        {
            ct.ThrowIfCancellationRequested();
            var (url, depth) = queue.Dequeue();
            progress?.Report(new SiteGrabberProgress { PagesScanned = pages, FilesFound = files.Count, CurrentUrl = url.AbsoluteUri });
            string html;
            try
            {
                html = await FetchHtmlAsync(url, options, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                continue;
            }
            pages++;
            foreach (var link in ExtractLinks(html, url))
            {
                if (options.SameHostOnly && !link.Host.Equals(start.Host, StringComparison.OrdinalIgnoreCase)) continue;
                var ext = UrlUtils.ExtensionOf(link);
                if (ext.Length > 0 && options.Extensions.Contains(ext))
                {
                    if (fileSet.Add(link.AbsoluteUri)) files.Add(link.AbsoluteUri);
                    continue;
                }
                if (depth < options.MaxDepth && visited.Add(link.AbsoluteUri))
                {
                    queue.Enqueue((link, depth + 1));
                }
            }
        }
        progress?.Report(new SiteGrabberProgress { PagesScanned = pages, FilesFound = files.Count, CurrentUrl = string.Empty });
        return files;
    }

    private async Task<string> FetchHtmlAsync(Uri url, SiteGrabberOptions options, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", options.UserAgent ?? "Mozilla/5.0 FileRush Grabber");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return string.Empty;
        var type = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!type.Contains("html", StringComparison.OrdinalIgnoreCase) && !type.Contains("xml", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        if (response.Content.Headers.ContentLength > 5 * 1024 * 1024) return string.Empty;
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    public static IEnumerable<Uri> ExtractLinks(string html, Uri baseUrl)
    {
        if (string.IsNullOrEmpty(html)) yield break;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in LinkRegex().Matches(html))
        {
            var raw = match.Groups["u"].Value.Trim();
            if (raw.Length == 0 || raw.StartsWith('#') || raw.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            raw = System.Net.WebUtility.HtmlDecode(raw);
            if (!Uri.TryCreate(baseUrl, raw, out var absolute)) continue;
            if (absolute.Scheme is not ("http" or "https")) continue;
            var normalized = absolute.GetLeftPart(UriPartial.Query);
            if (seen.Add(normalized)) yield return new Uri(normalized);
        }
    }

    [GeneratedRegex("(?:href|src|data-src)\\s*=\\s*(?:\"(?<u>[^\"]*)\"|'(?<u>[^']*)'|(?<u>[^\\s>]+))", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRegex();
}
