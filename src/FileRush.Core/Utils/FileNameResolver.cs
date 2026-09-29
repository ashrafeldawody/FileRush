using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace FileRush.Core.Utils;

public static partial class FileNameResolver
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    public static string Resolve(string url, string? contentDispositionHeader, string? contentType = null)
    {
        var fromHeader = FromContentDisposition(contentDispositionHeader);
        if (!string.IsNullOrWhiteSpace(fromHeader)) return Sanitize(fromHeader);
        var fromUrl = FromUrl(url);
        if (string.IsNullOrWhiteSpace(fromUrl)) fromUrl = "index.html";
        if (!Path.HasExtension(fromUrl) && contentType is not null)
        {
            var ext = ExtensionForContentType(contentType);
            if (ext is not null) fromUrl += ext;
        }
        return Sanitize(fromUrl);
    }

    public static string FromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return string.Empty;
        var path = uri.AbsolutePath;
        var lastSlash = path.LastIndexOf('/');
        var name = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
        name = Uri.UnescapeDataString(name);
        if (name.Length == 0 && uri.Host.Length > 0) return string.Empty;
        return name;
    }

    public static string? FromContentDisposition(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        var star = FileNameStarRegex().Match(header);
        if (star.Success)
        {
            var value = star.Groups["v"].Value.Trim().Trim('"');
            try
            {
                return Uri.UnescapeDataString(value);
            }
            catch
            {
            }
        }
        if (ContentDispositionHeaderValue.TryParse(header, out var parsed))
        {
            var name = parsed.FileNameStar ?? parsed.FileName;
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim().Trim('"');
        }
        var plain = FileNameRegex().Match(header);
        if (plain.Success) return plain.Groups["v"].Value.Trim().Trim('"');
        return null;
    }

    public static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(Array.IndexOf(InvalidChars, c) >= 0 ? '_' : c);
        }
        var result = sb.ToString().Trim().TrimEnd('.');
        if (result.Length == 0) result = "download";
        if (result.Length > 200)
        {
            var ext = Path.GetExtension(result);
            result = result[..(200 - ext.Length)] + ext;
        }
        return result;
    }

    public static string MakeUnique(string directory, string fileName, Func<string, bool>? exists = null)
    {
        exists ??= p => File.Exists(p);
        var candidate = Path.Combine(directory, fileName);
        if (!exists(candidate)) return fileName;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (int i = 2; i < 10000; i++)
        {
            var name = $"{stem}_{i}{ext}";
            if (!exists(Path.Combine(directory, name))) return name;
        }
        return $"{stem}_{Guid.NewGuid():N}{ext}";
    }

    public static string? ExtensionForContentType(string contentType)
    {
        var mime = contentType.Split(';')[0].Trim().ToLowerInvariant();
        return mime switch
        {
            "application/zip" => ".zip",
            "application/x-rar-compressed" => ".rar",
            "application/x-7z-compressed" => ".7z",
            "application/pdf" => ".pdf",
            "application/x-msdownload" => ".exe",
            "application/octet-stream" => null,
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "video/x-matroska" => ".mkv",
            "audio/mpeg" => ".mp3",
            "audio/mp4" => ".m4a",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "text/html" => ".html",
            "text/plain" => ".txt",
            "application/json" => ".json",
            _ => null
        };
    }

    [GeneratedRegex(@"filename\*\s*=\s*(?:[\w-]+)?'[^']*'(?<v>[^;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex FileNameStarRegex();

    [GeneratedRegex(@"filename\s*=\s*(?<v>""[^""]*""|[^;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex FileNameRegex();
}
