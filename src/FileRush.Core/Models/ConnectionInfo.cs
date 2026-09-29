namespace FileRush.Core.Models;

public sealed class ConnectionInfo
{
    public int Number { get; init; }
    public long Downloaded { get; set; }
    public string Info { get; set; } = string.Empty;
    public bool Active { get; set; }
}
