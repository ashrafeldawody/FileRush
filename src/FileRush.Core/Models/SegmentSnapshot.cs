namespace FileRush.Core.Models;

public readonly record struct SegmentSnapshot(long Start, long End, long Position, bool Active)
{
    public long Downloaded => Position - Start;
    public bool IsComplete => End >= 0 && Position > End;
}
