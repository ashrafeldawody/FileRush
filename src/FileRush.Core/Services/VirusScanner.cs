using System.Diagnostics;
using FileRush.Core.Models;

namespace FileRush.Core.Services;

public static class VirusScanner
{
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".bat", ".cmd", ".com", ".scr", ".ps1", ".vbs", ".js", ".jar", ".dll", ".apk", ".msix", ".appx"
    };

    public static bool IsExecutable(string fileName) => ExecutableExtensions.Contains(Path.GetExtension(fileName));

    public static bool ShouldScan(DownloadItem item, VirusScanSettings settings)
    {
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Command)) return false;
        return !settings.ScanOnlyExecutables || IsExecutable(item.FileName);
    }

    public static string BuildArguments(VirusScanSettings settings, string filePath)
    {
        var args = string.IsNullOrWhiteSpace(settings.Arguments) ? "\"%f\"" : settings.Arguments;
        return args.Replace("%f", filePath, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryRun(DownloadItem item, VirusScanSettings settings)
    {
        if (!ShouldScan(item, settings)) return false;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = settings.Command,
                Arguments = BuildArguments(settings, item.FullPath),
                UseShellExecute = true
            });
            item.VirusChecked = true;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
