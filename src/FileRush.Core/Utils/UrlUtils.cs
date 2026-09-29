namespace FileRush.Core.Utils;

public static class UrlUtils
{
    public static bool IsDownloadableUrl(string? text, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (t.Contains('\n') || t.Contains('\r')) return false;
        if (!Uri.TryCreate(t, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme is not ("http" or "https" or "ftp" or "ftps")) return false;
        uri = parsed;
        return true;
    }

    public static string NormalizeUrl(string url)
    {
        var t = url.Trim();
        if (!t.Contains("://", StringComparison.Ordinal)) t = "http://" + t;
        return t;
    }

    public static string ExtensionOf(Uri uri)
    {
        var path = uri.AbsolutePath;
        var slash = path.LastIndexOf('/');
        var name = slash >= 0 ? path[(slash + 1)..] : path;
        var dot = name.LastIndexOf('.');
        if (dot < 0 || dot == name.Length - 1) return string.Empty;
        return name[(dot + 1)..].ToUpperInvariant();
    }

    public static IEnumerable<string> ExtractUrls(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split(new[] { '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = raw.Trim().Trim('"', '\'', '<', '>', ',');
            if (IsDownloadableUrl(candidate, out var uri) && seen.Add(uri.AbsoluteUri))
            {
                yield return uri.AbsoluteUri;
            }
        }
    }
}
