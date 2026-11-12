namespace v2rayN.Desktop.Common;

/// <summary>Human-readable formatting shared by the V2pn views.</summary>
public static class Fmt
{
    private static bool Ru => CultureInfo.CurrentUICulture.Name.StartsWith("ru", StringComparison.OrdinalIgnoreCase);

    public static string Ago(DateTime time)
    {
        var span = DateTime.Now - time;
        if (span.TotalMinutes < 1)
        {
            return L.JustNow;
        }
        if (span.TotalHours < 1)
        {
            return string.Format(L.MinutesAgo, (int)span.TotalMinutes);
        }
        if (span.TotalDays < 1)
        {
            return string.Format(L.HoursAgo, (int)span.TotalHours);
        }
        return time.ToString("d MMM", CultureInfo.CurrentUICulture);
    }

    public static string SubUpdated(SubItem sub) => sub.UpdateTime > 0
        ? string.Format(L.SubUpdatedAgo, Ago(DateTimeOffset.FromUnixTimeSeconds(sub.UpdateTime).LocalDateTime))
        : L.NeverUpdated;

    // The sidebar row is narrow: the freshness at the end of "40 servers · updated 5 min ago" was the part cut off.
    public static string SubUpdatedShort(SubItem sub) => sub.UpdateTime > 0
        ? string.Format(L.SubAgoShort, Ago(DateTimeOffset.FromUnixTimeSeconds(sub.UpdateTime).LocalDateTime))
        : L.NeverUpdated;

    public static string Duration(TimeSpan t)
    {
        if (t.TotalSeconds < 60)
        {
            return string.Format(L.DurationSec, (int)t.TotalSeconds);
        }
        if (t.TotalMinutes < 60)
        {
            return string.Format(L.DurationMinSec, (int)t.TotalMinutes, t.Seconds);
        }
        return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
    }

    public static string Bytes(double bytes)
    {
        string[] units = Ru ? ["Б", "КБ", "МБ", "ГБ", "ТБ"] : ["B", "KB", "MB", "GB", "TB"];
        var i = 0;
        while (bytes >= 1024 && i < units.Length - 1)
        {
            bytes /= 1024;
            i++;
        }
        return i == 0 ? $"{bytes:0} {units[i]}" : $"{bytes:0.#} {units[i]}";
    }

    // Live speed keeps one decimal below 100 instead of Bytes' optional one, so the readout does not
    // change length on every sample ("1 MB/s" → "1.3 MB/s").
    public static string Speed(double bytesPerSecond)
    {
        string[] units = Ru ? ["Б", "КБ", "МБ", "ГБ", "ТБ"] : ["B", "KB", "MB", "GB", "TB"];
        var v = bytesPerSecond;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1)
        {
            v /= 1024;
            i++;
        }
        var n = i == 0 || v >= 100 ? $"{v:0}" : $"{v:0.0}";
        return $"{n} {units[i]}" + (Ru ? "/с" : "/s");
    }
}
