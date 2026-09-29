namespace FileRush.Core.Models;

public sealed class NewDownloadRequest
{
    public string Url { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? SaveDirectory { get; set; }
    public string? Category { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Referrer { get; set; }
    public string? UserAgent { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public Guid? QueueId { get; set; }
    public RemoteFileInfo? KnownInfo { get; set; }
    public int MaxConnections { get; set; }
}
