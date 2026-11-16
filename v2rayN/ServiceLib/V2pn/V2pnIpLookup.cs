using System.Globalization;

namespace ServiceLib;

/// <summary>
/// The full picture of an IP address, as 2ip-style sites show it: where it is, whose network it is in,
/// and whether sites take it for a VPN. Asked only when the person opens the IP card, never in the
/// background: the services see the address, so nothing goes to them unless the person wants it.
/// </summary>
public static class V2pnIpLookup
{
    /// <summary>Where an address is and whose network it belongs to.</summary>
    public sealed record Details(
        string Ip,
        string? Version,
        string? Country,
        string? CountryCode,
        string? Region,
        string? City,
        string? Postal,
        double? Latitude,
        double? Longitude,
        string? TimeZone,
        string? UtcOffset,
        long? Asn,
        string? AsnOrg,
        string? Isp,
        string? Domain,
        string Source);

    /// <summary>How anti-fraud databases see the address: "VPN", "Hosting", "Residential"... and a 0-100 risk.</summary>
    public sealed record Reputation(bool? Proxy, string? Kind, int? Risk);

    /// <summary>
    /// <paramref name="Exit"/> is the address sites see through V2pn (null when the core is not listening);
    /// <paramref name="Direct"/> is the person's own address, asked past V2pn (null in VPN mode, where every
    /// request goes through the tunnel and the "own" address would be the VPN's again).
    /// </summary>
    public sealed record Report(Details? Exit, Reputation? ExitReputation, Details? Direct, bool ViaProxy, bool DirectAsked)
    {
        public Details? Main => Exit ?? Direct;

        /// <summary>Through V2pn, and the site still saw the person's own address: routing sent it direct.</summary>
        public bool Leaks => Exit != null && Direct != null && string.Equals(Exit.Ip, Direct.Ip, StringComparison.OrdinalIgnoreCase);
    }

    public const string IpWhoUrl = "https://ipwho.is/";
    public const string IpSbUrl = "https://api.ip.sb/geoip";

    // An IP in the path only: the address is checked, the request itself carries no other data.
    public static string ReputationUrl(string ip) => $"https://proxycheck.io/v2/{Uri.EscapeDataString(ip)}?vpn=1&asn=1&risk=1";

    // A page for the browser with the same facts and more (DNS, WebRTC, speed).
    public const string BrowserUrl = "https://2ip.io/";

    private const int MaxBytes = 64 * 1024;

    /// <summary>
    /// Asks through the local SOCKS inbound when a core listens on it, and past it as well when
    /// <paramref name="askDirect"/> is set, so the two addresses can be compared.
    /// </summary>
    public static async Task<Report> LookUpAsync(bool askDirect, CancellationToken cancellationToken = default)
    {
        var proxy = await V2pnHttp.LocalProxyAsync(cancellationToken);
        // Without a running core the only address there is is the person's own.
        var direct = askDirect || proxy == null;

        var exitTask = proxy != null ? DetailsAsync(proxy, cancellationToken) : Task.FromResult<Details?>(null);
        var directTask = direct ? DetailsAsync(null, cancellationToken) : Task.FromResult<Details?>(null);
        var exit = await exitTask;
        var reputationTask = exit != null ? ReputationAsync(exit.Ip, proxy, cancellationToken) : Task.FromResult<Reputation?>(null);
        await Task.WhenAll(directTask, reputationTask);
        return new Report(exit, reputationTask.Result, directTask.Result, proxy != null, direct);
    }

    private static async Task<Details?> DetailsAsync(IWebProxy? proxy, CancellationToken cancellationToken)
    {
        var who = await V2pnHttp.GetAsync(IpWhoUrl, proxy, MaxBytes, cancellationToken);
        if (who.Ok && ParseIpWho(Encoding.UTF8.GetString(who.Body!)) is { } details)
        {
            return details;
        }
        // ipwho.is limits free use per address; ip.sb has fewer fields but answers the same questions.
        var sb = await V2pnHttp.GetAsync(IpSbUrl, proxy, MaxBytes, cancellationToken);
        return sb.Ok ? ParseIpSb(Encoding.UTF8.GetString(sb.Body!)) : null;
    }

    private static async Task<Reputation?> ReputationAsync(string ip, IWebProxy? proxy, CancellationToken cancellationToken)
    {
        var result = await V2pnHttp.GetAsync(ReputationUrl(ip), proxy, MaxBytes, cancellationToken);
        return result.Ok ? ParseReputation(Encoding.UTF8.GetString(result.Body!), ip) : null;
    }

    /// <summary>An ipwho.is answer; null for a refusal ("success": false) or anything unreadable.</summary>
    public static Details? ParseIpWho(string json)
    {
        if (Parse(json) is not JsonObject o || Bool(o["success"]) == false || Str(o["ip"]) is not { } ip)
        {
            return null;
        }
        var connection = o["connection"] as JsonObject;
        var zone = o["timezone"] as JsonObject;
        return new Details(
            ip,
            Str(o["type"]) ?? VersionOf(ip),
            Str(o["country"]),
            Str(o["country_code"]),
            Str(o["region"]),
            Str(o["city"]),
            Str(o["postal"]),
            Num(o["latitude"]),
            Num(o["longitude"]),
            Str(zone?["id"]),
            Str(zone?["utc"]),
            Long(connection?["asn"]),
            Str(connection?["org"]),
            Str(connection?["isp"]),
            Str(connection?["domain"]),
            "ipwho.is");
    }

    /// <summary>An api.ip.sb/geoip answer.</summary>
    public static Details? ParseIpSb(string json)
    {
        if (Parse(json) is not JsonObject o || Str(o["ip"]) is not { } ip)
        {
            return null;
        }
        return new Details(
            ip,
            VersionOf(ip),
            Str(o["country"]),
            Str(o["country_code"]),
            Str(o["region"]),
            Str(o["city"]),
            Str(o["postal_code"]),
            Num(o["latitude"]),
            Num(o["longitude"]),
            Str(o["timezone"]),
            Long(o["offset"]) is { } offset ? Offset(offset) : null,
            Long(o["asn"]),
            Str(o["asn_organization"]),
            Str(o["isp"]) ?? Str(o["organization"]),
            null,
            "ip.sb");
    }

    /// <summary>A proxycheck.io v2 answer: the verdict sits under the address itself.</summary>
    public static Reputation? ParseReputation(string json, string ip)
    {
        if (Parse(json) is not JsonObject o || Str(o["status"]) is "error" or "denied" || o[ip] is not JsonObject entry)
        {
            return null;
        }
        bool? proxy = Str(entry["proxy"]) switch
        {
            "yes" => true,
            "no" => false,
            _ => null,
        };
        var risk = Long(entry["risk"]);
        return new Reputation(proxy, Str(entry["type"]), risk is >= 0 and <= 100 ? (int)risk : null);
    }

    /// <summary>"+03:00" for 10800 seconds east of UTC.</summary>
    public static string Offset(long seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Abs(seconds));
        return $"{(seconds < 0 ? '-' : '+')}{span.Hours:00}:{span.Minutes:00}";
    }

    private static string VersionOf(string ip) =>
        IPAddress.TryParse(ip, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4";

    private static JsonNode? Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }
        var text = value.TryGetValue<string>(out var s) ? s : value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static double? Num(JsonNode? node) =>
        node is JsonValue value && (value.TryGetValue<double>(out var d)
            || (value.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)))
            ? d
            : null;

    private static long? Long(JsonNode? node) => Num(node) is { } d ? (long)d : null;

    private static bool? Bool(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var b) ? b : null;
}
