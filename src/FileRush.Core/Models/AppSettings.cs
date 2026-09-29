namespace FileRush.Core.Models;

public sealed class VirusScanSettings
{
    public bool Enabled { get; set; }
    public string Command { get; set; } = string.Empty;
    public string Arguments { get; set; } = "\"%f\"";
    public bool ScanOnlyExecutables { get; set; } = true;
}

public sealed class SpeedLimitSettings
{
    public bool Enabled { get; set; }
    public int MaxKilobytesPerSecond { get; set; } = 128;
}

public sealed class AppSettings
{
    public string DownloadsRoot { get; set; } = string.Empty;

    public string TempDirectory { get; set; } = string.Empty;

    public bool MonitorClipboard { get; set; } = true;

    public bool BrowserIntegration { get; set; } = true;

    public bool StartWithWindows { get; set; }

    public bool MinimizeToTray { get; set; } = true;

    public bool CloseToTray { get; set; } = true;

    public string MonitoredExtensions { get; set; } = "3GP 7Z AAC ACE APK ARJ ASF AVI BIN BZ2 CAB CHM CSV DEB DJVU DMG DOC DOCX EPUB EXE FLAC FLV GZ ISO JAR LZH M4A M4V MKV MOV MP3 MP4 MPEG MPG MSI MSU OGG PDF PPT PPTX RAR RPM SEA SIT SITX TAR TGZ TIF TIFF TXT WAV WEBM WMA WMV XLS XLSX XZ Z ZIP ZST";

    public List<string> UrlExceptions { get; set; } = new();

    public bool RememberLastPathPerCategory { get; set; } = true;

    public int MaxConnectionsPerFile { get; set; } = 8;

    public int MaxSimultaneousDownloads { get; set; } = 8;

    public int TimeoutSeconds { get; set; } = 30;

    public int MaxRetries { get; set; } = 20;

    public int RetryDelaySeconds { get; set; } = 5;

    public int MinSegmentSizeKb { get; set; } = 512;

    public bool ShowStartDownloadDialog { get; set; } = true;

    public bool StartDownloadWhileShowingInfoDialog { get; set; } = true;

    public bool ShowProgressDialog { get; set; } = true;

    public bool ShowCompleteDialog { get; set; } = true;

    public bool IgnoreModificationTimeOnResume { get; set; }

    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) FileRush/1.0";

    public VirusScanSettings VirusScan { get; set; } = new();

    public ProxySettings Proxy { get; set; } = new();

    public List<SiteLogin> SiteLogins { get; set; } = new();

    public SpeedLimitSettings SpeedLimit { get; set; } = new();

    public List<Category> Categories { get; set; } = new();

    public bool PlaySounds { get; set; }

    public bool ConfirmDelete { get; set; } = true;

    public bool ShowCategoriesPane { get; set; } = true;

    public bool LargeToolbarIcons { get; set; } = true;

    public double WindowWidth { get; set; } = 1100;

    public double WindowHeight { get; set; } = 650;

    public string SortColumn { get; set; } = string.Empty;

    public bool SortDescending { get; set; }

    public HashSet<string> MonitoredExtensionSet()
    {
        return MonitoredExtensions
            .Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().TrimStart('.').ToUpperInvariant())
            .ToHashSet();
    }

    public static AppSettings CreateDefault()
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var settings = new AppSettings
        {
            DownloadsRoot = downloads,
            TempDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileRush", "Temp")
        };
        settings.Categories = Category.CreateDefaults(downloads);
        return settings;
    }

    public void EnsureCategories()
    {
        if (Categories.Count == 0)
        {
            Categories = Category.CreateDefaults(string.IsNullOrEmpty(DownloadsRoot)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                : DownloadsRoot);
        }
    }
}
