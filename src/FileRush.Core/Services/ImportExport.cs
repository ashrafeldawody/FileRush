using System.Text.Json;
using FileRush.Core.Models;
using FileRush.Core.Utils;

namespace FileRush.Core.Services;

public sealed class ExportedDownload
{
    public string Url { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string SaveDirectory { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Referrer { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public long TotalSize { get; set; } = -1;
    public bool Completed { get; set; }
}

public static class ImportExport
{
    public static string ToUrlList(IEnumerable<DownloadItem> items)
    {
        return string.Join(Environment.NewLine, items.Select(i => i.Url));
    }

    public static string ToJson(IEnumerable<DownloadItem> items)
    {
        var list = items.Select(i => new ExportedDownload
        {
            Url = i.Url,
            FileName = i.FileName,
            SaveDirectory = i.SaveDirectory,
            Category = i.Category,
            Description = i.Description,
            Referrer = i.Referrer,
            Username = i.Username,
            Password = i.Password,
            TotalSize = i.TotalSize,
            Completed = i.Status == DownloadStatus.Completed
        }).ToList();
        return JsonSerializer.Serialize(list, JsonDefaults.Options);
    }

    public static IReadOnlyList<ExportedDownload> FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<ExportedDownload>>(json, JsonDefaults.Options) ?? new List<ExportedDownload>();
        }
        catch
        {
            return Array.Empty<ExportedDownload>();
        }
    }

    public static IReadOnlyList<string> FromUrlList(string text)
    {
        return UrlUtils.ExtractUrls(text).ToList();
    }

    public static IReadOnlyList<ExportedDownload> Import(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('['))
        {
            var parsed = FromJson(text);
            if (parsed.Count > 0) return parsed;
        }
        return FromUrlList(text).Select(u => new ExportedDownload { Url = u }).ToList();
    }
}
