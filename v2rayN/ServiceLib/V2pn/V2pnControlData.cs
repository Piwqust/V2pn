using System.Globalization;

namespace ServiceLib;

/// <summary>
/// V2pn: what the control channel tells about the connection, the servers and the subscriptions, built from a list of
/// allowed fields like the diagnostics report (V2pnDiagnostics). Never an address, a port, a link, a UUID, a password or
/// a key: the output goes to terminals, scripts and AI assistants, and from there anywhere. A server's name is its
/// remark, with its address cut out when the remark repeats it.
/// </summary>
public static class V2pnControlData
{
    public static string Verdict(EV2pnVerdict verdict) => verdict switch
    {
        EV2pnVerdict.KillSwitch => "kill-switch",
        EV2pnVerdict.CoreStopped => "core-stopped",
        EV2pnVerdict.CoreFailed => "core-failed",
        EV2pnVerdict.Off => "off",
        EV2pnVerdict.Reconnecting => "reconnecting",
        EV2pnVerdict.Connecting => "connecting",
        EV2pnVerdict.Checking => "checking",
        EV2pnVerdict.LocalOnly => "local-only",
        EV2pnVerdict.VpnConnected => "vpn-connected",
        EV2pnVerdict.ProxyConnected => "proxy-connected",
        _ => "check-problem",
    };

    /// <summary>The route by its command-line name: one of the three presets, "custom" for any other rule set, or "none".</summary>
    public static string RouteName(string? routingId) =>
        routingId.IsNullOrEmpty() ? "none" : V2pnControlProtocol.Routes.FirstOrDefault(r => r.Id == routingId).Name ?? "custom";

    public static string RouteId(EV2pnPreset preset) => preset switch
    {
        EV2pnPreset.RussiaDirect => "v2pn-ru-direct",
        EV2pnPreset.BlockedOnly => "v2pn-blocked-only",
        _ => "v2pn-all",
    };

    /// <summary>
    /// The status: the verdict the window shows (V2pnConnectionVerdict), the mode, the server's name and country, the
    /// route and the last check. "settled" is false while the connection is still on its way: the core starting, or the
    /// check not done yet.
    /// </summary>
    public static JsonObject Status(V2pnConnectionSnapshot snapshot, AvailabilityCheckResult check, DateTime? checkedAt, bool preferTun,
        ProfileItem? server, string? country, string? routingId)
    {
        var (kind, verdict) = V2pnConnectionVerdict.Of(snapshot);
        var on = V2pnConnectionVerdict.Protection(snapshot) != EV2pnProtection.Off;
        var settled = verdict switch
        {
            EV2pnVerdict.Connecting or EV2pnVerdict.Reconnecting or EV2pnVerdict.Checking => false,
            EV2pnVerdict.CheckProblem => snapshot.Check != AvailabilityState.Unchecked,
            _ => true,
        };
        var status = new JsonObject
        {
            ["connected"] = on,
            ["verdict"] = Verdict(verdict),
            ["kind"] = kind.ToString().ToLowerInvariant(),
            ["mode"] = (on ? snapshot.EnableTun : preferTun) ? "vpn" : "proxy",
            ["server"] = server == null ? null : new JsonObject
            {
                ["id"] = server.IndexId,
                ["name"] = ServerName(server),
                ["country"] = Country(country),
                ["protocol"] = server.ConfigType.ToString(),
            },
            ["route"] = RouteName(routingId),
            ["killSwitchHolding"] = snapshot.KillSwitchHolding,
            ["core"] = snapshot.Core.ToString(),
            ["settled"] = settled,
        };
        if (snapshot.Failure != EV2pnFailure.None)
        {
            status["failure"] = snapshot.Failure.ToString();
        }
        if (check.State != AvailabilityState.Unchecked)
        {
            // The exit address the check saw is left out: it is the server's.
            var shown = new JsonObject { ["state"] = check.State.ToString() };
            if (check.IsAvailable)
            {
                shown["ms"] = check.Time;
            }
            if (check.HttpStatus is { } http)
            {
                shown["httpStatus"] = http;
            }
            if (checkedAt is { } at)
            {
                shown["at"] = at.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            }
            status["check"] = shown;
        }
        return status;
    }

    /// <summary>
    /// The servers, in the order V2pn keeps them, the ones a partner hides already left out. <paramref name="country"/>
    /// names each one's country; <paramref name="onlyCountry"/> keeps one country.
    /// </summary>
    public static JsonArray Servers(IEnumerable<ProfileItem> servers, IReadOnlyDictionary<string, ProfileExItem> tests,
        IReadOnlyDictionary<string, string> subNames, Func<ProfileItem, string?> country, string? activeId, string? onlyCountry)
    {
        var list = new JsonArray();
        foreach (var server in servers)
        {
            var code = Country(country(server));
            if (onlyCountry != null && !string.Equals(code, onlyCountry, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var test = tests.GetValueOrDefault(server.IndexId);
            var delay = test?.Delay ?? 0;
            list.Add(new JsonObject
            {
                ["id"] = server.IndexId,
                ["name"] = ServerName(server),
                ["country"] = code,
                ["protocol"] = server.ConfigType.ToString(),
                ["delayMs"] = delay > 0 ? delay : null,
                ["test"] = delay > 0 ? "ok" : delay < 0 ? "failed" : "untested",
                ["favorite"] = test?.IsFavorite == true,
                ["subscription"] = server.Subid.IsNotEmpty() ? subNames.GetValueOrDefault(server.Subid) : null,
                ["active"] = server.IndexId == activeId,
            });
        }
        return list;
    }

    /// <summary>The subscriptions: names, counts, the last update and what the provider reports. Never their links.</summary>
    public static JsonArray Subs(IReadOnlyList<SubItem> subs, Func<string, SubInfoItem?> info, IReadOnlyDictionary<string, int> counts)
    {
        var list = new JsonArray();
        for (var i = 0; i < subs.Count; i++)
        {
            var sub = subs[i];
            var about = info(sub.Id);
            var updated = about?.LastAttemptTime > 0 ? about.LastAttemptTime : sub.UpdateTime;
            var entry = new JsonObject
            {
                ["id"] = sub.Id,
                ["name"] = SubName(sub, about, i),
                ["servers"] = counts.GetValueOrDefault(sub.Id),
                ["enabled"] = sub.Enabled,
                ["updated"] = Time(updated),
                ["lastResult"] = (about?.LastResult ?? EV2pnSubUpdateResult.None).ToString(),
            };
            if (about?.HasUserInfo == true)
            {
                entry["expires"] = Time(about.Expire);
                entry["trafficUsedBytes"] = about.Upload + about.Download;
                entry["trafficTotalBytes"] = about.Total > 0 ? about.Total : null;
            }
            list.Add(entry);
        }
        return list;
    }

    private static string? Time(long unixSeconds) => unixSeconds > 0
        ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        : null;

    private static string? Country(string? code) =>
        code is { Length: 2 } && code.All(char.IsAsciiLetter) ? code.ToUpperInvariant() : null;

    /// <summary>
    /// The name to show for a server: its remark without flag emoji. A remark that repeats the address, the user id or
    /// the password gets them cut out; a server with no remark is named by its protocol, never by its address.
    /// </summary>
    public static string ServerName(ProfileItem server)
    {
        var name = StripFlags(server.Remarks);
        foreach (var secret in new[] { server.Address, server.Password, server.Username })
        {
            if (secret is { Length: >= 4 } && name.Contains(secret, StringComparison.OrdinalIgnoreCase))
            {
                name = name.Replace(secret, "…", StringComparison.OrdinalIgnoreCase);
            }
        }
        name = name.Trim();
        return name.Length == 0 || name == "…" ? $"{server.ConfigType} server" : name;
    }

    /// <summary>A subscription's name: the person's or the provider's, never its link; "Subscription 2" when there is neither.</summary>
    public static string SubName(SubItem sub, SubInfoItem? info, int index)
    {
        foreach (var candidate in new[] { sub.Remarks, info?.PanelName })
        {
            var name = candidate?.Trim();
            if (name.IsNullOrEmpty() || name.Contains("://", StringComparison.Ordinal)
                || (sub.Url is { Length: >= 8 } url && name.Contains(url, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            return name;
        }
        return $"Subscription {index + 1}";
    }

    private static string StripFlags(string? remarks)
    {
        if (remarks.IsNullOrEmpty())
        {
            return string.Empty;
        }
        var sb = new StringBuilder(remarks.Length);
        foreach (var rune in remarks.EnumerateRunes())
        {
            if (rune.Value is >= 0x1F1E6 and <= 0x1F1FF)
            {
                continue;
            }
            sb.Append(rune.ToString());
        }
        return sb.ToString().Trim().TrimStart('|', '-', '·', ' ').Trim();
    }

    /// <summary>
    /// The server a person means by <paramref name="query"/>: its id, its whole name, or a part of the name that only one
    /// server has. Two or more that fit are named in the answer, so the next try can be exact.
    /// </summary>
    public static (string? Id, string? Code, string Message) FindServer(IReadOnlyList<(string Id, string Name)> servers, string query)
    {
        var q = query.Trim();
        if (servers.FirstOrDefault(s => s.Id == q) is { Id: not null } byId)
        {
            return (byId.Id, null, byId.Name);
        }
        var exact = servers.Where(s => string.Equals(s.Name, q, StringComparison.OrdinalIgnoreCase)).ToList();
        var found = exact.Count > 0 ? exact : servers.Where(s => s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        return found.Count switch
        {
            0 => (null, V2pnControlProtocol.ErrNotFound, $"No server is called \"{q}\". \"v2pnctl servers\" lists them."),
            1 => (found[0].Id, null, found[0].Name),
            _ => (null, V2pnControlProtocol.ErrAmbiguous,
                $"{found.Count} servers fit \"{q}\": {string.Join(", ", found.Take(5).Select(s => $"{s.Name} ({s.Id})"))}{(found.Count > 5 ? ", …" : string.Empty)}. Give the id or the whole name."),
        };
    }
}
