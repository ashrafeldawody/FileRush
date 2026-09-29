using FileRush.Core.Models;
using FileRush.Core.Services;
using Xunit;

namespace FileRush.Tests;

public sealed class QueueSchedulerTests
{
    private static readonly DateTime Monday = new(2026, 1, 5, 0, 0, 0);

    private static QueueSchedule Daily(int hour, int minute) => new()
    {
        StartEnabled = true,
        StartTime = new TimeSpan(hour, minute, 0),
        Mode = ScheduleMode.Daily,
        Days = ScheduleDays.All
    };

    [Fact]
    public void Daily_DueWithinTolerance()
    {
        var schedule = Daily(10, 0);
        Assert.True(QueueScheduler.ShouldStart(schedule, Monday.AddHours(10).AddMinutes(2)));
    }

    [Fact]
    public void Daily_NotDueBeforeTime()
    {
        var schedule = Daily(10, 0);
        Assert.False(QueueScheduler.ShouldStart(schedule, Monday.AddHours(9).AddMinutes(59)));
    }

    [Fact]
    public void Daily_NotDueAfterTolerance()
    {
        var schedule = Daily(10, 0);
        Assert.False(QueueScheduler.ShouldStart(schedule, Monday.AddHours(10).Add(QueueScheduler.Tolerance)));
    }

    [Fact]
    public void Daily_NotDueTwiceSameDay()
    {
        var schedule = Daily(10, 0);
        schedule.LastStarted = Monday.AddHours(10).AddMinutes(1);
        Assert.False(QueueScheduler.ShouldStart(schedule, Monday.AddHours(10).AddMinutes(3)));
        Assert.True(QueueScheduler.ShouldStart(schedule, Monday.AddDays(1).AddHours(10).AddMinutes(1)));
    }

    [Fact]
    public void Disabled_NeverDue()
    {
        var schedule = Daily(10, 0);
        schedule.StartEnabled = false;
        Assert.False(QueueScheduler.ShouldStart(schedule, Monday.AddHours(10)));
    }

    [Fact]
    public void DaysMask_IsRespected()
    {
        var schedule = Daily(10, 0);
        schedule.Days = ScheduleDays.Weekdays;
        var saturday = new DateTime(2026, 1, 10, 10, 1, 0);
        Assert.False(QueueScheduler.ShouldStart(schedule, saturday));
        Assert.True(QueueScheduler.ShouldStart(schedule, Monday.AddHours(10).AddMinutes(1)));
    }

    [Fact]
    public void Once_OnlyOnThatDate()
    {
        var schedule = Daily(10, 0);
        schedule.Mode = ScheduleMode.Once;
        schedule.OnceDate = Monday;
        Assert.True(QueueScheduler.ShouldStart(schedule, Monday.AddHours(10).AddMinutes(1)));
        Assert.False(QueueScheduler.ShouldStart(schedule, Monday.AddDays(1).AddHours(10).AddMinutes(1)));
    }

    [Fact]
    public void Stop_UsesStopTime()
    {
        var schedule = Daily(10, 0);
        schedule.StopEnabled = true;
        schedule.StopTime = new TimeSpan(12, 0, 0);
        Assert.False(QueueScheduler.ShouldStop(schedule, Monday.AddHours(10).AddMinutes(1)));
        Assert.True(QueueScheduler.ShouldStop(schedule, Monday.AddHours(12).AddMinutes(1)));
        schedule.StopEnabled = false;
        Assert.False(QueueScheduler.ShouldStop(schedule, Monday.AddHours(12).AddMinutes(1)));
    }

    [Fact]
    public void NextStart_Daily()
    {
        var schedule = Daily(10, 0);
        Assert.Equal(Monday.AddDays(1).AddHours(10), QueueScheduler.NextStart(schedule, Monday.AddHours(11)));
        Assert.Equal(Monday.AddHours(10), QueueScheduler.NextStart(schedule, Monday.AddHours(9)));
    }

    [Fact]
    public void NextStart_SkipsDisabledDays()
    {
        var schedule = Daily(10, 0);
        schedule.Days = ScheduleDays.Weekdays;
        var friday = new DateTime(2026, 1, 9, 11, 0, 0);
        Assert.Equal(new DateTime(2026, 1, 12, 10, 0, 0), QueueScheduler.NextStart(schedule, friday));
    }

    [Fact]
    public void NextStart_Once()
    {
        var schedule = Daily(10, 0);
        schedule.Mode = ScheduleMode.Once;
        schedule.OnceDate = Monday;
        Assert.Equal(Monday.AddHours(10), QueueScheduler.NextStart(schedule, Monday.AddHours(9)));
        Assert.Null(QueueScheduler.NextStart(schedule, Monday.AddHours(11)));
        schedule.StartEnabled = false;
        Assert.Null(QueueScheduler.NextStart(schedule, Monday));
    }

    [Fact]
    public void DayEnabled_MapsFlags()
    {
        Assert.True(QueueScheduler.DayEnabled(ScheduleDays.Sunday, DayOfWeek.Sunday));
        Assert.False(QueueScheduler.DayEnabled(ScheduleDays.Weekdays, DayOfWeek.Sunday));
        Assert.True(QueueScheduler.DayEnabled(ScheduleDays.Weekdays, DayOfWeek.Wednesday));
    }
}
