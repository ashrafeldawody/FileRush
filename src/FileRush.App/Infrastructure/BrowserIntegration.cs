using System.IO;
using FileRush.Core.Services;
using Microsoft.Win32;

namespace FileRush.App.Infrastructure;

public sealed record BrowserRegistration(string Key, string DisplayName, bool Registered);

public static class BrowserIntegration
{
    private static readonly IReadOnlyDictionary<string, string> DisplayNames = new Dictionary<string, string>
    {
        ["chrome"] = "Chrome / Opera",
        ["edge"] = "Edge",
        ["brave"] = "Brave",
        ["chromium"] = "Chromium",
        ["vivaldi"] = "Vivaldi",
        ["firefox"] = "Firefox"
    };

    public static string HostDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileRush", "NativeHost");

    public static string ChromeManifestPath => Path.Combine(HostDirectory, BrowserIntegrationInfo.HostName + ".json");

    public static string FirefoxManifestPath => Path.Combine(HostDirectory, BrowserIntegrationInfo.HostName + ".firefox.json");

    public static string HostPath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "FileRush.exe");

    public static string ExtensionDirectory
    {
        get
        {
            var local = Path.Combine(AppContext.BaseDirectory, "browser-extension");
            if (File.Exists(Path.Combine(local, "manifest.json"))) return local;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && dir is not null; i++)
            {
                var candidate = Path.Combine(dir.FullName, "browser-extension");
                if (File.Exists(Path.Combine(candidate, "manifest.json"))) return candidate;
                dir = dir.Parent;
            }
            return local;
        }
    }

    public static void Register()
    {
        Directory.CreateDirectory(HostDirectory);
        File.WriteAllText(ChromeManifestPath, BrowserIntegrationInfo.BuildChromeManifest(HostPath));
        File.WriteAllText(FirefoxManifestPath, BrowserIntegrationInfo.BuildFirefoxManifest(HostPath));
        foreach (var key in BrowserIntegrationInfo.ChromiumRegistryKeys.Values)
        {
            WriteKey(key, ChromeManifestPath);
        }
        WriteKey(BrowserIntegrationInfo.FirefoxRegistryKey, FirefoxManifestPath);
    }

    public static void Unregister()
    {
        foreach (var key in BrowserIntegrationInfo.ChromiumRegistryKeys.Values) DeleteKey(key);
        DeleteKey(BrowserIntegrationInfo.FirefoxRegistryKey);
        try
        {
            if (File.Exists(ChromeManifestPath)) File.Delete(ChromeManifestPath);
            if (File.Exists(FirefoxManifestPath)) File.Delete(FirefoxManifestPath);
        }
        catch
        {
        }
    }

    public static IReadOnlyList<BrowserRegistration> Status()
    {
        var list = new List<BrowserRegistration>();
        foreach (var (name, key) in BrowserIntegrationInfo.ChromiumRegistryKeys)
        {
            list.Add(new BrowserRegistration(name, DisplayNames[name], IsRegistered(key, ChromeManifestPath)));
        }
        list.Add(new BrowserRegistration("firefox", DisplayNames["firefox"], IsRegistered(BrowserIntegrationInfo.FirefoxRegistryKey, FirefoxManifestPath)));
        return list;
    }

    public static bool IsAnyRegistered() => Status().Any(s => s.Registered);

    private static bool IsRegistered(string registryKey, string manifestPath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(registryKey + "\\" + BrowserIntegrationInfo.HostName);
            var value = key?.GetValue(string.Empty) as string;
            if (string.IsNullOrEmpty(value) || !File.Exists(value)) return false;
            if (!string.Equals(value, manifestPath, StringComparison.OrdinalIgnoreCase)) return false;
            var text = File.ReadAllText(value);
            return text.Contains(HostPath.Replace("\\", "\\\\"), StringComparison.OrdinalIgnoreCase) || text.Contains(HostPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void WriteKey(string registryKey, string manifestPath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(registryKey + "\\" + BrowserIntegrationInfo.HostName);
            key?.SetValue(string.Empty, manifestPath);
        }
        catch
        {
        }
    }

    private static void DeleteKey(string registryKey)
    {
        try
        {
            using var parent = Registry.CurrentUser.OpenSubKey(registryKey, true);
            parent?.DeleteSubKeyTree(BrowserIntegrationInfo.HostName, false);
        }
        catch
        {
        }
    }
}
