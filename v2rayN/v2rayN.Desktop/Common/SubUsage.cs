namespace v2rayN.Desktop.Common;

/// <summary>
/// How a subscription stands with its provider (traffic and expiry from <see cref="SubInfoItem"/>),
/// worded for the sidebar and the subscription editor. Null when the panel reported nothing to show.
/// </summary>
public sealed class SubUsage
{
    private const double WarnFraction = 0.9;
    private const int WarnDays = 3;

    public static SubUsage? From(SubInfoItem? info) => From(info, DateTime.Now);

    public static SubUsage? From(SubInfoItem? info, DateTime now)
    {
        if (info is not { HasUserInfo: true })
        {
            return null;
        }
        var used = info.Upload > long.MaxValue - info.Download ? long.MaxValue : info.Upload + info.Download;
        var expires = ToLocal(info.Expire);
        if (info.Total <= 0 && used <= 0 && expires == null)
        {
            return null;
        }
        return new SubUsage(info, used, expires, now);
    }

    private SubUsage(SubInfoItem info, long used, DateTime? expires, DateTime now)
    {
        var split = string.Format(L.TrafficSplit, Fmt.Bytes(info.Upload), Fmt.Bytes(info.Download));
        if (info.Total > 0)
        {
            HasTraffic = true;
            ShowBar = true;
            Fraction = Math.Clamp((double)used / info.Total, 0, 1);
            TrafficText = string.Format(L.TrafficOf, Fmt.Bytes(used), Fmt.Bytes(info.Total));
            TrafficBad = used >= info.Total;
            TrafficWarn = !TrafficBad && Fraction >= WarnFraction;
            TrafficHint = TrafficBad ? $"{L.TrafficOut} · {split}" : split;
        }
        else if (used > 0)
        {
            HasTraffic = true;
            TrafficText = string.Format(L.TrafficUsed, Fmt.Bytes(used));
            TrafficHint = $"{L.TrafficUnlimited} · {split}";
        }

        if (expires is { } at)
        {
            HasExpiry = true;
            var left = at - now;
            var days = (int)left.TotalDays;
            DaysLeft = Math.Max(0, days);
            string relative;
            if (left <= TimeSpan.Zero)
            {
                ExpiryBad = true;
                ExpiryText = L.SubExpired;
                relative = string.Format(L.SubExpiredOn, ShortDate(at, now));
            }
            else if (days < 1)
            {
                ExpiryWarn = true;
                relative = ExpiryText = string.Format(L.HoursLeft, Math.Max(1, (int)left.TotalHours));
            }
            else if (days <= 7)
            {
                ExpiryWarn = days <= WarnDays;
                relative = ExpiryText = L.DaysLeft(days);
            }
            else
            {
                ExpiryText = string.Format(L.ValidUntil, ShortDate(at, now));
                relative = L.DaysLeft(days);
            }
            ExpiryLong = ExpiryBad
                ? relative
                : $"{at.ToString("d MMMM yyyy", CultureInfo.CurrentUICulture)} · {relative}";
        }
        else if (HasTraffic)
        {
            ExpiryLong = L.SubNoEnd;
        }

        Tooltip = string.Join("\n", new[] { TrafficText, TrafficHint, HasExpiry ? ExpiryLong : null }.Where(t => t.IsNotEmpty()));

        // The sidebar line starts with the traffic; without it the expiry takes its place.
        if (HasTraffic)
        {
            (RowStart, RowStartWarn, RowStartBad) = (TrafficText, TrafficWarn, TrafficBad);
            (RowEnd, RowEndWarn, RowEndBad) = (ExpiryText, ExpiryWarn, ExpiryBad);
        }
        else
        {
            (RowStart, RowStartWarn, RowStartBad) = (ExpiryText, ExpiryWarn, ExpiryBad);
        }
    }

    public string RowStart { get; } = string.Empty;
    public bool RowStartWarn { get; }
    public bool RowStartBad { get; }
    public string RowEnd { get; } = string.Empty;
    public bool RowEndWarn { get; }
    public bool RowEndBad { get; }

    public bool HasTraffic { get; }
    public bool ShowBar { get; }
    public double Fraction { get; }

    /// <summary>"12.4 GB of 100 GB", or "12.4 GB used" when the traffic is unlimited.</summary>
    public string TrafficText { get; } = string.Empty;

    /// <summary>Upload and download apart, and whether the traffic is unlimited or used up.</summary>
    public string TrafficHint { get; } = string.Empty;

    public bool TrafficWarn { get; }
    public bool TrafficBad { get; }

    public bool HasExpiry { get; }

    /// <summary>Whole days left until the end (0 on the last day and after it); null without an end.</summary>
    public int? DaysLeft { get; }

    /// <summary>Short: "until 12 Dec", "3 days left", "Expired".</summary>
    public string ExpiryText { get; } = string.Empty;

    /// <summary>Long, for the editor: "12 December 2026 · 35 days left".</summary>
    public string ExpiryLong { get; } = string.Empty;

    public bool ExpiryWarn { get; }
    public bool ExpiryBad { get; }

    public string Tooltip { get; }

    private static DateTime? ToLocal(long unixSeconds)
    {
        // DateTimeOffset tops out in the year 9999; a panel may send anything.
        if (unixSeconds <= 0 || unixSeconds > 253_402_300_799)
        {
            return null;
        }
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime;
    }

    private static string ShortDate(DateTime at, DateTime now) =>
        at.ToString(at.Year == now.Year ? "d MMM" : "d MMM yyyy", CultureInfo.CurrentUICulture);
}
