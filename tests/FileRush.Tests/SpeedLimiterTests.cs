using System.Diagnostics;
using FileRush.Core.Engine;
using Xunit;

namespace FileRush.Tests;

public sealed class SpeedLimiterTests
{
    [Fact]
    public async Task Unlimited_CompletesSynchronously()
    {
        var limiter = new SpeedLimiter();
        Assert.False(limiter.IsEnabled);
        var task = limiter.WaitAsync(10 * 1024 * 1024, CancellationToken.None);
        Assert.True(task.IsCompleted);
        await task;
    }

    [Fact]
    public async Task Limited_DelaysToMatchRate()
    {
        var limiter = new SpeedLimiter(500 * 1024);
        Assert.True(limiter.IsEnabled);
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 3; i++)
        {
            await limiter.WaitAsync(250 * 1024, CancellationToken.None);
        }
        watch.Stop();
        Assert.True(watch.Elapsed >= TimeSpan.FromSeconds(1.2), $"elapsed {watch.Elapsed.TotalSeconds:F2}s");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ChangingRate_ResetsBucket()
    {
        var limiter = new SpeedLimiter(1000);
        limiter.BytesPerSecond = 0;
        Assert.Equal(0, limiter.BytesPerSecond);
        Assert.False(limiter.IsEnabled);
        limiter.BytesPerSecond = -5;
        Assert.Equal(0, limiter.BytesPerSecond);
        limiter.BytesPerSecond = 2048;
        Assert.Equal(2048, limiter.BytesPerSecond);
        Assert.True(limiter.IsEnabled);
    }

    [Fact]
    public async Task ZeroBytes_NeverWaits()
    {
        var limiter = new SpeedLimiter(1);
        var task = limiter.WaitAsync(0, CancellationToken.None);
        Assert.True(task.IsCompleted);
        await task;
    }

    [Fact]
    public async Task Cancellation_AbortsWait()
    {
        var limiter = new SpeedLimiter(1);
        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await limiter.WaitAsync(1000, cts.Token));
    }
}
