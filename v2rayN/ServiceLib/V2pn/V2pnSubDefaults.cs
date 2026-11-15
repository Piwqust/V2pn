namespace ServiceLib;

/// <summary>A banner of a subscription: the provider's info, or the reminder that it runs out.</summary>
/// <param name="Tone">SubPanelHeaders.ToneInfo, ToneAttention or ToneOk: V2pn's own colours, never the provider's.</param>
public sealed record SubBanner(string Text, string Tone, string? ButtonText, string? ButtonUrl, bool IsExpiry);

/// <summary>What the app does at start for a subscription whose provider asked to connect.</summary>
public sealed record SubAutoconnectPlan(string SubId, string Type);

/// <summary>
/// V2pn: what the Happ, INCY and v2RayTun values of a subscription (<see cref="SubInfoItem"/>) mean for the app. The
/// provider sets defaults; the person's own choices on the subscription win. Kept apart from the UI so it is testable.
/// </summary>
public static class V2pnSubDefaults
{
    /// <summary>The expiry reminder shows from this many days before the end, and after it.</summary>
    public const int ExpiryDays = 3;

    /// <summary>The one banner to show: the expiry reminder when it is due, else the provider's info banner.</summary>
    public static SubBanner? Banner(SubInfoItem? info, DateTimeOffset now) => ExpiryBanner(info, now) ?? InfoBanner(info);

    /// <summary>
    /// Due when the subscription ends within <see cref="ExpiryDays"/> days or has ended, unless the provider turned it
    /// off (Happ sub-expire: 0). The provider's words when it sent them (INCY expiry-banner-text), V2pn's otherwise.
    /// "Renew" leads to the provider's renew link, else its site, else there is no button.
    /// </summary>
    public static SubBanner? ExpiryBanner(SubInfoItem? info, DateTimeOffset now)
    {
        if (info == null || info.Expire <= 0 || info.ExpiryNotice < 0 || info.Expire > 253_402_300_799)
        {
            return null;
        }
        var left = DateTimeOffset.FromUnixTimeSeconds(info.Expire) - now;
        if (left > TimeSpan.FromDays(ExpiryDays))
        {
            return null;
        }
        // Whole days, as the subscription's row counts them ("1 day left" until the last day, then hours).
        var days = (int)left.TotalDays;
        var text = info.ExpiryText.IsNotEmpty() ? info.ExpiryText
            : left <= TimeSpan.Zero ? ResUI.V2pnSubEnded
            : days < 1 ? ResUI.V2pnSubEndsWithinDay
            : days == 1 ? ResUI.V2pnSubEndsInOneDay
            : string.Format(ResUI.V2pnSubEndsInDays, days);
        var url = info.ExpiryButtonUrl.IsNotEmpty() ? info.ExpiryButtonUrl : info.WebPageUrl.IsNotEmpty() ? info.WebPageUrl : null;
        var button = url == null ? null : info.ExpiryButtonText.IsNotEmpty() ? info.ExpiryButtonText : ResUI.V2pnSubRenew;
        return new SubBanner(text, SubPanelHeaders.ToneAttention, button, url, true);
    }

    /// <summary>The provider's info banner (Happ sub-info, INCY banner), with "More" when it gave a link but no label.</summary>
    public static SubBanner? InfoBanner(SubInfoItem? info)
    {
        if (info?.InfoText.IsNotEmpty() != true)
        {
            return null;
        }
        var url = info.InfoButtonUrl.IsNotEmpty() ? info.InfoButtonUrl : null;
        var button = url == null ? null : info.InfoButtonText.IsNotEmpty() ? info.InfoButtonText : ResUI.V2pnSubMore;
        var tone = info.InfoTone is SubPanelHeaders.ToneAttention or SubPanelHeaders.ToneOk ? info.InfoTone : SubPanelHeaders.ToneInfo;
        return new SubBanner(info.InfoText!, tone, button, url, false);
    }

    /// <summary>
    /// The provider advises connecting at start and the person left that to it, or the provider closed the
    /// subscription's settings (<paramref name="locked"/>, V2pnLockState.Settings): then its advice holds.
    /// </summary>
    public static bool AutoconnectOn(SubInfoItem? info, bool locked = false) =>
        info is { Autoconnect: true } && (locked || !info.OwnAutoconnect);

    /// <summary>How the server is picked when the provider's autoconnect runs: its type, else the last used one.</summary>
    public static string AutoconnectType(SubInfoItem? info) => info?.AutoconnectType ?? SubPanelHeaders.ConnectLastUsed;

    /// <summary>
    /// The order of a subscription's servers: the person's, else the provider's, else fastest first. With the settings
    /// closed (<paramref name="locked"/>) the provider's order holds.
    /// </summary>
    public static string SortOf(SubInfoItem? info, bool locked = false) =>
        (locked ? null : SubPanelHeaders.ParseSortType(info?.OwnSort)) ?? SubPanelHeaders.ParseSortType(info?.SortType) ?? SubPanelHeaders.SortPing;

    /// <summary>Puts servers in the order <see cref="SortOf"/> names. "without" keeps the order they came in.</summary>
    public static IEnumerable<T> Order<T>(IEnumerable<T> items, string sort, Func<T, string?> name, Func<T, int> delay) => sort switch
    {
        SubPanelHeaders.SortAlphabet => items.OrderBy(i => name(i) ?? string.Empty, NaturalComparer.Instance),
        SubPanelHeaders.SortWithout => items,
        // Tested servers first, quickest on top; the rest keep their own order.
        _ => items.OrderBy(i => delay(i) > 0 ? delay(i) : int.MaxValue),
    };

    public static bool UpdatesOnStart(SubInfoItem? info) => info?.UpdateOnOpen == true;

    public static bool PingsOnStart(SubInfoItem? info) => info?.PingOnOpen == true;

    /// <summary>
    /// Whether the app connects by itself at start, and to which subscription. Only when V2pn would otherwise start
    /// switched off: a person who left it connected gets that connection back as always. The subscription is the one
    /// of the chosen server; with no server chosen, the first subscription that has servers. A server of the person's
    /// own (in no subscription) is their choice, and no provider overrides it.
    /// </summary>
    /// <param name="subs">Enabled subscriptions in the order the app lists them.</param>
    /// <param name="serverSubId">The subscription of the chosen server: null when none is chosen, "" for a server in no subscription.</param>
    /// <param name="hasServers">Whether a subscription has servers.</param>
    /// <param name="locked">Whether a provider closed a subscription's settings: its advice then holds.</param>
    public static SubAutoconnectPlan? StartPlan(bool startsConnected, IEnumerable<SubItem> subs, string? serverSubId,
        Func<string, SubInfoItem?> info, Func<string, bool> hasServers, Func<string, bool>? locked = null)
    {
        if (startsConnected)
        {
            return null;
        }
        var list = subs.Where(s => s.Id.IsNotEmpty() && s.Enabled).ToList();
        SubItem? sub = serverSubId switch
        {
            null => list.FirstOrDefault(s => hasServers(s.Id)),
            "" => null,
            _ => list.FirstOrDefault(s => s.Id == serverSubId),
        };
        if (sub == null || !hasServers(sub.Id))
        {
            return null;
        }
        var subInfo = info(sub.Id);
        return AutoconnectOn(subInfo, locked?.Invoke(sub.Id) == true) ? new SubAutoconnectPlan(sub.Id, AutoconnectType(subInfo)) : null;
    }

    /// <summary>
    /// The server to connect to by <paramref name="type"/>, from <paramref name="servers"/> (one subscription, in its
    /// order, with their last test results; 0 or less is untested or failed). lowestdelay takes the quickest that
    /// answered and nothing when none did; random any; lastused the newest of <paramref name="recent"/> that is in the
    /// subscription, else the chosen server, else the first.
    /// </summary>
    public static string? PickServer(string type, IReadOnlyList<(string Id, int Delay)> servers, IEnumerable<string> recent,
        string? current, Random random)
    {
        if (servers.Count == 0)
        {
            return null;
        }
        switch (type)
        {
            case SubPanelHeaders.ConnectLowestDelay:
                return servers.Where(s => s.Delay > 0).OrderBy(s => s.Delay).Select(s => s.Id).FirstOrDefault();

            case SubPanelHeaders.ConnectRandom:
                return servers[random.Next(servers.Count)].Id;

            default:
                var ids = servers.Select(s => s.Id).ToHashSet();
                return recent.FirstOrDefault(ids.Contains)
                       ?? (current != null && ids.Contains(current) ? current : null)
                       ?? servers[0].Id;
        }
    }

    /// <summary>
    /// Where a subscription moves when its panel sends new-url or new-domain, or null to stay. Honoured only for a
    /// verified partner (<paramref name="partner"/> is the certificate the subscription has now) and only to a host the
    /// certificate names; otherwise anyone answering on the old address could take the subscription away. new-url wins
    /// over new-domain, and a new-url that is not usable does not fall back to it. A move to the same host is refused.
    /// </summary>
    public static string? MovedUrl(string? currentUrl, string? newUrl, string? newDomain, V2pnPartnerCert? partner)
    {
        var current = Utils.TryUri(currentUrl?.Trim() ?? string.Empty);
        if (partner == null || current == null || current.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }
        string? target;
        if (newUrl.IsNotEmpty())
        {
            target = SubPanelHeaders.ParseNewUrl(newUrl);
        }
        else if (SubPanelHeaders.ParseNewDomain(newDomain) is { } domain)
        {
            target = WithHost(currentUrl!.Trim(), domain);
        }
        else
        {
            return null;
        }
        var targetUri = Utils.TryUri(target ?? string.Empty);
        if (target == null || targetUri == null
            || string.Equals(targetUri.IdnHost, current.IdnHost, StringComparison.OrdinalIgnoreCase)
            || !V2pnPartnerTrust.DomainAllowed(partner, target))
        {
            return null;
        }
        return target;
    }

    // Swaps only the host in the address as written: port, path, query and fragment stay byte for byte, so an opaque
    // token in the path is never decoded and encoded again.
    private static string? WithHost(string url, string host)
    {
        var start = url.IndexOf("://", StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }
        start += 3;
        var end = url.IndexOfAny(['/', '?', '#'], start);
        var authority = end < 0 ? url[start..] : url[start..end];
        if (authority.Contains('@') || authority.Contains('['))
        {
            return null;
        }
        var colon = authority.IndexOf(':');
        var port = colon < 0 ? string.Empty : authority[colon..];
        return url[..start] + host + port + (end < 0 ? string.Empty : url[end..]);
    }

    /// <summary>Names with numbers in them sort as people count: "Germany 2" before "Germany 10".</summary>
    private sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            x ??= string.Empty;
            y ??= string.Empty;
            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
                {
                    var si = i;
                    var sj = j;
                    while (i < x.Length && char.IsAsciiDigit(x[i]))
                    {
                        i++;
                    }
                    while (j < y.Length && char.IsAsciiDigit(y[j]))
                    {
                        j++;
                    }
                    var a = x[si..i].TrimStart('0');
                    var b = y[sj..j].TrimStart('0');
                    var byNumber = a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
                    if (byNumber != 0)
                    {
                        return byNumber;
                    }
                    continue;
                }
                var byText = string.Compare(x, i, y, j, 1, StringComparison.CurrentCultureIgnoreCase);
                if (byText != 0)
                {
                    return byText;
                }
                i++;
                j++;
            }
            return (x.Length - i).CompareTo(y.Length - j);
        }
    }
}
