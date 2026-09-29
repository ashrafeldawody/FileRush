using System.Text.Json.Serialization;

namespace FileRush.Core.Models;

public sealed class Segment
{
    public Segment() { }

    public Segment(long start, long end)
    {
        Start = start;
        End = end;
        Position = start;
    }

    public long Start { get; set; }

    public long End { get; set; }

    public long Position { get; set; }

    [JsonIgnore]
    public bool IsOpenEnded => End < 0;

    [JsonIgnore]
    public bool IsComplete => !IsOpenEnded && Position > End;

    [JsonIgnore]
    public long Remaining => IsOpenEnded ? long.MaxValue : Math.Max(0, End - Position + 1);

    [JsonIgnore]
    public long Downloaded => Position - Start;

    [JsonIgnore]
    public long Length => IsOpenEnded ? -1 : End - Start + 1;

    public Segment Clone() => new() { Start = Start, End = End, Position = Position };
}
