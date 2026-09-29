namespace FileRush.Core.Models;

public enum DownloadStatus
{
    Pending,
    Queued,
    Connecting,
    Downloading,
    Paused,
    Completed,
    Error
}
