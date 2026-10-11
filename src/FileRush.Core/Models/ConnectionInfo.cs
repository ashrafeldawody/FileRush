namespace FileRush.Core.Models;

public sealed class ConnectionInfo
{
    public int Number { get; init; }
    public string Route { get; init; } = string.Empty;
    public long Downloaded { get; set; }
    public string Info { get; set; } = string.Empty;
    public bool Active { get; set; }
    public bool RejectedByServer { get; set; }
}
