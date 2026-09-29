using System.Globalization;

namespace FileRush.Core.Utils;

public static class ByteFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string Format(long bytes)
    {
        if (bytes < 0) return "Unknown";
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var digits = unit == 0 ? 0 : (value < 10 ? 2 : value < 100 ? 1 : 0);
        return value.ToString("F" + digits, CultureInfo.InvariantCulture) + " " + Units[unit];
    }

    public static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return "";
        return Format((long)bytesPerSecond) + "/sec";
    }

    public static string FormatTime(TimeSpan? span)
    {
        if (span is null) return "";
        var t = span.Value;
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays} d {t.Hours} h";
        if (t.TotalHours >= 1) return $"{t.Hours} h {t.Minutes} min";
        if (t.TotalMinutes >= 1) return $"{t.Minutes} min {t.Seconds} sec";
        return $"{t.Seconds} sec";
    }
}
