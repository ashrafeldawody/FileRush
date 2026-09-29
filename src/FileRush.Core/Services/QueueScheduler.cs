using FileRush.Core.Models;

namespace FileRush.Core.Services;

public static class QueueScheduler
{
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public static bool ShouldStart(QueueSchedule schedule, DateTime now)
    {
        if (!schedule.StartEnabled) return false;
        return IsDue(schedule, schedule.StartTime, schedule.LastStarted, now);
    }

    public static bool ShouldStop(QueueSchedule schedule, DateTime now)
    {
        if (!schedule.StopEnabled) return false;
        return IsDue(schedule, schedule.StopTime, schedule.LastStopped, now);
    }

    public static DateTime? NextStart(QueueSchedule schedule, DateTime now)
    {
        if (!schedule.StartEnabled) return null;
        if (schedule.Mode == ScheduleMode.Once)
        {
            var at = schedule.OnceDate.Date + schedule.StartTime;
            return at >= now ? at : null;
        }
        for (int i = 0; i < 8; i++)
        {
            var day = now.Date.AddDays(i);
            if (!DayEnabled(schedule.Days, day.DayOfWeek)) continue;
            var at = day + schedule.StartTime;
            if (at >= now) return at;
        }
        return null;
    }

    private static bool IsDue(QueueSchedule schedule, TimeSpan time, DateTime? last, DateTime now)
    {
        DateTime scheduled;
        if (schedule.Mode == ScheduleMode.Once)
        {
            scheduled = schedule.OnceDate.Date + time;
        }
        else
        {
            if (!DayEnabled(schedule.Days, now.DayOfWeek)) return false;
            scheduled = now.Date + time;
        }
        if (now < scheduled || now >= scheduled + Tolerance) return false;
        return last is null || last.Value < scheduled;
    }

    public static bool DayEnabled(ScheduleDays days, DayOfWeek day)
    {
        var flag = day switch
        {
            DayOfWeek.Monday => ScheduleDays.Monday,
            DayOfWeek.Tuesday => ScheduleDays.Tuesday,
            DayOfWeek.Wednesday => ScheduleDays.Wednesday,
            DayOfWeek.Thursday => ScheduleDays.Thursday,
            DayOfWeek.Friday => ScheduleDays.Friday,
            DayOfWeek.Saturday => ScheduleDays.Saturday,
            _ => ScheduleDays.Sunday
        };
        return (days & flag) != 0;
    }
}
