using FileRush.Core.Models;
using FileRush.Core.Utils;

namespace FileRush.Core.Services;

public static class ClipboardUrlDetector
{
    public static bool ShouldCapture(string? text, AppSettings settings, out Uri uri)
    {
        uri = null!;
        if (!settings.MonitorClipboard) return false;
        if (!UrlUtils.IsDownloadableUrl(text, out var parsed)) return false;
        if (IsException(parsed, settings.UrlExceptions)) return false;
        var ext = UrlUtils.ExtensionOf(parsed);
        if (ext.Length == 0 || !settings.MonitoredExtensionSet().Contains(ext)) return false;
        uri = parsed;
        return true;
    }

    public static bool IsException(Uri uri, IEnumerable<string> exceptions)
    {
        foreach (var raw in exceptions)
        {
            var pattern = raw.Trim();
            if (pattern.Length == 0) continue;
            if (pattern.Contains('*'))
            {
                if (WildcardMatch(uri.AbsoluteUri, pattern) || WildcardMatch(uri.Host, pattern)) return true;
                continue;
            }
            if (uri.Host.Equals(pattern, StringComparison.OrdinalIgnoreCase)) return true;
            if (uri.Host.EndsWith("." + pattern, StringComparison.OrdinalIgnoreCase)) return true;
            if (uri.AbsoluteUri.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static bool WildcardMatch(string input, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(input, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
