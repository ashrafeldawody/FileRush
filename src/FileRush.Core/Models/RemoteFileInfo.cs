namespace FileRush.Core.Models;

public sealed class RemoteFileInfo
{
    public string Url { get; init; } = string.Empty;
    public string FinalUrl { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long Size { get; init; } = -1;
    public bool SupportsRange { get; init; }
    public string? ETag { get; init; }
    public string? LastModified { get; init; }
    public string? ContentType { get; init; }
}
