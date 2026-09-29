using System.Net;

namespace FileRush.Core.Engine;

public sealed class DownloadEngineOptions
{
    public int MaxConnections { get; set; } = 8;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxRetries { get; set; } = 10;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public long MinSegmentSize { get; set; } = 512 * 1024;
    public int BufferSize { get; set; } = 128 * 1024;
    public string UserAgent { get; set; } = "FileRush/1.0";
    public IWebProxy? Proxy { get; set; }
    public bool UseSystemProxy { get; set; } = true;
    public bool FtpPassive { get; set; } = true;
    public bool IgnoreModificationTime { get; set; }
    public string? TempDirectory { get; set; }
    public TimeSpan ProgressInterval { get; set; } = TimeSpan.FromMilliseconds(400);
    public TimeSpan StateSaveInterval { get; set; } = TimeSpan.FromSeconds(3);
}
