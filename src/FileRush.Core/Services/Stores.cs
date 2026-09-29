using System.Text.Json;
using System.Text.Json.Serialization;
using FileRush.Core.Models;

namespace FileRush.Core.Services;

public static class DataPaths
{
    public static string Root { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileRush");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string DownloadsFile => Path.Combine(Root, "downloads.json");

    public static string GrabberFile => Path.Combine(Root, "grabber.json");
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };
}

public static class AtomicFile
{
    public static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true);
        else File.Move(temp, path);
    }

    public static string? Read(string path)
    {
        if (File.Exists(path))
        {
            try { return File.ReadAllText(path); } catch { }
        }
        var backup = path + ".bak";
        if (File.Exists(backup))
        {
            try { return File.ReadAllText(backup); } catch { }
        }
        return null;
    }
}

public sealed class DownloadStoreData
{
    public List<DownloadItem> Items { get; set; } = new();
    public List<DownloadQueue> Queues { get; set; } = new();
}

public sealed class DownloadStore
{
    private readonly string _path;

    public DownloadStore(string path)
    {
        _path = path;
    }

    public DownloadStoreData Load()
    {
        var text = AtomicFile.Read(_path);
        if (string.IsNullOrWhiteSpace(text)) return new DownloadStoreData();
        try
        {
            return JsonSerializer.Deserialize<DownloadStoreData>(text, JsonDefaults.Options) ?? new DownloadStoreData();
        }
        catch
        {
            return new DownloadStoreData();
        }
    }

    public void Save(DownloadStoreData data)
    {
        AtomicFile.Write(_path, JsonSerializer.Serialize(data, JsonDefaults.Options));
    }
}

public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore(string path)
    {
        _path = path;
    }

    public AppSettings Load()
    {
        var text = AtomicFile.Read(_path);
        AppSettings? settings = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try { settings = JsonSerializer.Deserialize<AppSettings>(text, JsonDefaults.Options); } catch { }
        }
        settings ??= AppSettings.CreateDefault();
        if (string.IsNullOrWhiteSpace(settings.DownloadsRoot)) settings.DownloadsRoot = AppSettings.CreateDefault().DownloadsRoot;
        settings.EnsureCategories();
        return settings;
    }

    public void Save(AppSettings settings)
    {
        AtomicFile.Write(_path, JsonSerializer.Serialize(settings, JsonDefaults.Options));
    }
}
