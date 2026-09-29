using System.Diagnostics;

namespace FileRush.Core.Engine;

public sealed class SpeedLimiter
{
    private readonly object _gate = new();
    private long _bytesPerSecond;
    private double _tokens;
    private long _lastTick;

    public SpeedLimiter(long bytesPerSecond = 0)
    {
        _bytesPerSecond = bytesPerSecond;
        _lastTick = Stopwatch.GetTimestamp();
    }

    public long BytesPerSecond
    {
        get => Interlocked.Read(ref _bytesPerSecond);
        set
        {
            lock (_gate)
            {
                Interlocked.Exchange(ref _bytesPerSecond, Math.Max(0, value));
                _tokens = 0;
                _lastTick = Stopwatch.GetTimestamp();
            }
        }
    }

    public bool IsEnabled => BytesPerSecond > 0;

    public ValueTask WaitAsync(int bytes, CancellationToken ct)
    {
        var limit = BytesPerSecond;
        if (limit <= 0 || bytes <= 0) return ValueTask.CompletedTask;
        TimeSpan delay;
        lock (_gate)
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = (now - _lastTick) / (double)Stopwatch.Frequency;
            _lastTick = now;
            _tokens = Math.Min(limit, _tokens + elapsed * limit);
            _tokens -= bytes;
            if (_tokens >= 0) return ValueTask.CompletedTask;
            delay = TimeSpan.FromSeconds(-_tokens / limit);
        }
        return new ValueTask(Task.Delay(delay, ct));
    }
}
