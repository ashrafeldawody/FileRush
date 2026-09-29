using System.Diagnostics;

namespace FileRush.Core.Engine;

public sealed class SpeedMeter
{
    private readonly Queue<(long Ticks, long Bytes)> _samples = new();
    private readonly TimeSpan _window;
    private readonly object _gate = new();

    public SpeedMeter(TimeSpan? window = null)
    {
        _window = window ?? TimeSpan.FromSeconds(4);
    }

    public double Rate { get; private set; }

    public void Update(long totalBytes)
    {
        Update(totalBytes, Stopwatch.GetTimestamp());
    }

    public void Update(long totalBytes, long ticks)
    {
        lock (_gate)
        {
            _samples.Enqueue((ticks, totalBytes));
            var windowTicks = (long)(_window.TotalSeconds * Stopwatch.Frequency);
            while (_samples.Count > 2 && ticks - _samples.Peek().Ticks > windowTicks)
            {
                _samples.Dequeue();
            }
            var oldest = _samples.Peek();
            var dt = (ticks - oldest.Ticks) / (double)Stopwatch.Frequency;
            Rate = dt <= 0.05 ? 0 : Math.Max(0, (totalBytes - oldest.Bytes) / dt);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _samples.Clear();
            Rate = 0;
        }
    }
}
