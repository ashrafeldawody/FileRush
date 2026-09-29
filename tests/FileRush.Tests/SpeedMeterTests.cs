using System.Diagnostics;
using FileRush.Core.Engine;
using Xunit;

namespace FileRush.Tests;

public sealed class SpeedMeterTests
{
    private static long Seconds(double s) => (long)(s * Stopwatch.Frequency);

    [Fact]
    public void SingleSample_HasZeroRate()
    {
        var meter = new SpeedMeter(TimeSpan.FromSeconds(4));
        meter.Update(1000, Seconds(1));
        Assert.Equal(0, meter.Rate);
    }

    [Fact]
    public void TwoSamples_ComputeBytesPerSecond()
    {
        var meter = new SpeedMeter(TimeSpan.FromSeconds(4));
        meter.Update(0, Seconds(1));
        meter.Update(1000, Seconds(2));
        Assert.InRange(meter.Rate, 990, 1010);
    }

    [Fact]
    public void OldSamples_FallOutOfWindow()
    {
        var meter = new SpeedMeter(TimeSpan.FromSeconds(4));
        meter.Update(0, Seconds(0));
        meter.Update(1000, Seconds(1));
        meter.Update(5000, Seconds(10));
        Assert.InRange(meter.Rate, 440, 450);
    }

    [Fact]
    public void Reset_ClearsRate()
    {
        var meter = new SpeedMeter(TimeSpan.FromSeconds(4));
        meter.Update(0, Seconds(0));
        meter.Update(4000, Seconds(2));
        Assert.True(meter.Rate > 0);
        meter.Reset();
        Assert.Equal(0, meter.Rate);
        meter.Update(100, Seconds(3));
        Assert.Equal(0, meter.Rate);
    }

    [Fact]
    public void DecreasingTotal_NeverNegative()
    {
        var meter = new SpeedMeter(TimeSpan.FromSeconds(4));
        meter.Update(5000, Seconds(0));
        meter.Update(1000, Seconds(1));
        Assert.Equal(0, meter.Rate);
    }
}
