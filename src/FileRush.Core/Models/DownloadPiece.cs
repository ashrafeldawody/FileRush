namespace FileRush.Core.Models;

public readonly record struct DownloadPiece(long Start, long End, int Connection, string Route)
{
    public long Length => End - Start + 1;
}
