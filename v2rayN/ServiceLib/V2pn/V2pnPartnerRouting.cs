using System.Globalization;

namespace ServiceLib;

/// <summary>
/// The sites and addresses of a provider's rule (routing.json, core.json), in the words V2pn's own routing uses:
/// "example.com" and "domain:example.com" (the site and everything under it), "full:", "keyword:", "geosite:" lists of
/// the geo files, and IPs, networks and "geoip:" lists. No regular expressions: a reviewer has to read every entry.
/// </summary>
public static class V2pnRouteEntries
{
    public const int MaxEntryLength = 200;

    private static readonly Regex Label = new("^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.CultureInvariant);
    private static readonly Regex GeoTag = new("^!?[a-z0-9][a-z0-9!@._-]{0,63}$", RegexOptions.CultureInvariant);

    // Public resolvers: a rule that moves them moves the person's DNS too.
    private static readonly HashSet<string> DnsResolvers =
    [
        "1.1.1.1", "1.0.0.1", "8.8.8.8", "8.8.4.4", "9.9.9.9", "149.112.112.112", "208.67.222.222", "208.67.220.220",
        "77.88.8.8", "77.88.8.1", "94.140.14.14", "94.140.15.15", "2606:4700:4700::1111", "2606:4700:4700::1001",
        "2001:4860:4860::8888", "2001:4860:4860::8844",
    ];

    /// <summary>The entry as V2pn's routing writes it ("example.com" becomes "domain:example.com"), or null with why.</summary>
    public static string? Domain(string? entry, out string problem)
    {
        problem = string.Empty;
        var text = entry?.Trim().ToLowerInvariant() ?? string.Empty;
        if (text.Length is 0 or > MaxEntryLength)
        {
            problem = $"\"{entry}\": empty or longer than {MaxEntryLength} characters";
            return null;
        }
        var colon = text.IndexOf(':');
        var kind = colon < 0 ? "domain" : text[..colon];
        var value = colon < 0 ? text : text[(colon + 1)..];
        switch (kind)
        {
            case "geosite":
                if (GeoTag.IsMatch(value))
                {
                    return text;
                }
                break;

            case "domain" or "full":
                if (IsDomainName(value))
                {
                    return $"{kind}:{value.TrimEnd('.')}";
                }
                break;

            case "keyword":
                // Three letters at least: "keyword:a" would catch nearly every site.
                if (value.Length >= 3 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.'))
                {
                    return text;
                }
                break;

            default:
                problem = $"\"{entry}\": {kind}: is not a kind V2pn takes (domain:, full:, keyword:, geosite:)";
                return null;
        }
        problem = $"\"{entry}\": not a valid {kind} entry";
        return null;
    }

    /// <summary>The entry as an address, a network ("10.0.0.0/8") or "geoip:tag", or null with why.</summary>
    public static string? Ip(string? entry, out string problem)
    {
        problem = string.Empty;
        var text = entry?.Trim().ToLowerInvariant() ?? string.Empty;
        if (text.StartsWith("geoip:", StringComparison.Ordinal))
        {
            if (GeoTag.IsMatch(text[6..]))
            {
                return text;
            }
        }
        else if (Network(text) is { } network)
        {
            return $"{network.Address}{(network.Prefix is { } prefix ? "/" + prefix : string.Empty)}";
        }
        problem = $"\"{entry}\": not an IP address, a network or geoip:";
        return null;
    }

    public static bool IsGeo(string entry) => entry.StartsWith("geosite:", StringComparison.Ordinal) || entry.StartsWith("geoip:", StringComparison.Ordinal);

    /// <summary>The local network's own lists and addresses; V2pn always sends them direct itself.</summary>
    public static bool IsPrivate(string entry)
    {
        if (entry is "geosite:private" or "geoip:private")
        {
            return true;
        }
        if (Network(entry) is not { } network)
        {
            return false;
        }
        var bytes = network.Address.GetAddressBytes();
        return IPAddress.IsLoopback(network.Address) || network.Address.IsIPv6LinkLocal || network.Address.IsIPv6UniqueLocal
               || bytes is [10, ..] or [192, 168, ..] or [169, 254, ..] || (bytes is [172, var b, ..] && b is >= 16 and <= 31)
               || (network.Prefix is { } prefix && prefix < 8);
    }

    /// <summary>An entry that touches DNS: a public resolver, a DNS service's name, DNS ports' lists.</summary>
    public static bool AffectsDns(string entry)
    {
        var value = entry.Contains(':') && !entry.Contains("::") && !IPAddress.TryParse(entry, out _) ? entry[(entry.IndexOf(':') + 1)..] : entry;
        var address = value.Split('/')[0];
        return DnsResolvers.Contains(address) || value.Contains("dns", StringComparison.Ordinal) || value is "dns.google" or "one.one.one.one";
    }

    /// <summary>
    /// Why blocking this entry is too broad for a provider: whole countries (geoip:), the local network, a whole
    /// zone ("domain:ru"), a network wider than /16 (/32 for IPv6). Null when it is narrow enough to be reviewed.
    /// </summary>
    public static string? TooBroadToBlock(string entry)
    {
        if (entry.StartsWith("geoip:", StringComparison.Ordinal))
        {
            return "a whole geoip: list";
        }
        if (entry.StartsWith("domain:", StringComparison.Ordinal) && !entry[7..].Contains('.'))
        {
            return "a whole domain zone";
        }
        if (entry.StartsWith("keyword:", StringComparison.Ordinal) && entry.Length - 8 < 5)
        {
            return "a keyword shorter than 5 characters";
        }
        if (!entry.Contains(':') || entry.Contains("::") || IPAddress.TryParse(entry.Split('/')[0], out _))
        {
            if (Network(entry) is { } network && network.Prefix is { } prefix
                && prefix < (network.Address.AddressFamily == AddressFamily.InterNetworkV6 ? 32 : 16))
            {
                return $"a network wider than /{(network.Address.AddressFamily == AddressFamily.InterNetworkV6 ? 32 : 16)}";
            }
        }
        return IsPrivate(entry) ? "the local network" : null;
    }

    private static (IPAddress Address, int? Prefix)? Network(string text)
    {
        var parts = text.Split('/');
        if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out var address)
            || address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6) || parts[0].Contains('%'))
        {
            return null;
        }
        // IPAddress.TryParse takes "1" as 0.0.0.1; only dotted quads and real IPv6 count.
        if (address.AddressFamily == AddressFamily.InterNetwork && parts[0].Count(c => c == '.') != 3)
        {
            return null;
        }
        if (parts.Length == 1)
        {
            return (address, null);
        }
        var max = address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        return int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var prefix) && prefix <= max ? (address, prefix) : null;
    }

    private static bool IsDomainName(string value)
    {
        value = value.TrimEnd('.');
        if (value.Length is 0 or > 253)
        {
            return false;
        }
        return value.Split('.').All(Label.IsMatch);
    }
}

/// <summary>One rule of a provider: these sites and addresses go to <see cref="To"/>.</summary>
public sealed record V2pnRouteRule(IReadOnlyList<string> Domains, IReadOnlyList<string> Ips, string To);

/// <summary>
/// routing.json: a provider's routing preset, reviewed by hand and offered as one more mode of "What goes through the
/// VPN". V2pn turns it into an ordinary routing item of its own (<see cref="V2pnPartnerRoutes"/>), with the local
/// network always direct first.
/// </summary>
public sealed record V2pnRoutingPreset(string? Name, IReadOnlyList<V2pnRouteRule> Rules, string Default)
{
    public const string Proxy = "proxy";
    public const string Direct = "direct";
    public const string Block = "block";

    public const int MaxRules = 50;
    public const int MaxEntriesPerRule = 500;
    public const int MaxEntries = 2000;
    public const int MaxName = 40;

    /// <summary>Uses lists of the geo files besides the local network's; they come from the same sources as V2pn's Russian modes.</summary>
    public bool UsesGeoFiles => Rules.Any(r => r.Domains.Concat(r.Ips).Any(e => V2pnRouteEntries.IsGeo(e) && !V2pnRouteEntries.IsPrivate(e)));

    /// <summary>IP rules need the site's address, so the core looks it up when no domain rule matched first.</summary>
    public string DomainStrategy => Rules.Any(r => r.Ips.Count > 0) ? Global.IPOnDemand : Global.AsIs;

    /// <summary>The preset as V2pn's own rules: the local network direct, then the provider's rules in order, then its default.</summary>
    public List<RulesItem> ToRules()
    {
        var rules = new List<RulesItem>
        {
            new() { OutboundTag = Global.DirectTag, Ip = ["geoip:private"] },
            new() { OutboundTag = Global.DirectTag, Domain = ["geosite:private"] },
        };
        foreach (var rule in Rules)
        {
            if (rule.Domains.Count > 0)
            {
                rules.Add(new RulesItem { OutboundTag = rule.To, Domain = rule.Domains.ToList() });
            }
            if (rule.Ips.Count > 0)
            {
                rules.Add(new RulesItem { OutboundTag = rule.To, Ip = rule.Ips.ToList() });
            }
        }
        rules.Add(new RulesItem { OutboundTag = Default, Port = "0-65535" });
        return rules;
    }

    /// <summary>The preset when <paramref name="json"/> is a valid routing.json; otherwise null and the first problem found.</summary>
    public static V2pnRoutingPreset? Parse(byte[] json, out string problem)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
            {
                problem = "not a JSON object";
                return null;
            }
            if (root["schema"] is { } schema && (schema is not JsonValue sv || !sv.TryGetValue<int>(out var s) || s != 1))
            {
                problem = "schema must be 1";
                return null;
            }
            var name = Text(root["name"]);
            if (name != null && (name.Length > MaxName || name.Any(char.IsControl)))
            {
                problem = $"name: at most {MaxName} characters";
                return null;
            }
            var fallback = Text(root["default"]) ?? Proxy;
            if (fallback is not (Proxy or Direct))
            {
                problem = "default: proxy or direct (never block: that would cut off everything else)";
                return null;
            }
            if (root["rules"] is not JsonArray array || array.Count == 0 || array.Count > MaxRules)
            {
                problem = $"rules: 1 to {MaxRules} rules";
                return null;
            }
            var rules = new List<V2pnRouteRule>();
            var total = 0;
            for (var i = 0; i < array.Count; i++)
            {
                var rule = ParseRule(array[i], i, [Proxy, Direct, Block], allowGeo: true, out problem);
                if (rule == null)
                {
                    return null;
                }
                total += rule.Domains.Count + rule.Ips.Count;
                if (total > MaxEntries)
                {
                    problem = $"rules: at most {MaxEntries} sites and addresses in all";
                    return null;
                }
                rules.Add(rule);
            }
            problem = string.Empty;
            return new V2pnRoutingPreset(name, rules, fallback);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            problem = "not valid JSON: " + ex.Message;
            return null;
        }
    }

    /// <summary>
    /// One rule: "domains", "ips" and "to". Shared with core.json, whose rules lead to its own outbounds; a block rule
    /// may not block something broad (<see cref="V2pnRouteEntries.TooBroadToBlock"/>).
    /// </summary>
    internal static V2pnRouteRule? ParseRule(JsonNode? node, int index, IReadOnlyCollection<string> targets, bool allowGeo, out string problem)
    {
        var at = $"rules[{index}]";
        if (node is not JsonObject obj)
        {
            problem = $"{at}: not an object";
            return null;
        }
        var to = Text(obj["to"]);
        if (to == null || !targets.Contains(to))
        {
            problem = $"{at}.to: one of {string.Join(", ", targets)}";
            return null;
        }
        var domains = new List<string>();
        var ips = new List<string>();
        foreach (var (key, list) in new[] { ("domains", domains), ("ips", ips) })
        {
            if (obj[key] is null)
            {
                continue;
            }
            if (obj[key] is not JsonArray entries || entries.Count > MaxEntriesPerRule)
            {
                problem = $"{at}.{key}: a list of at most {MaxEntriesPerRule} entries";
                return null;
            }
            foreach (var entry in entries)
            {
                var raw = entry is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;
                string why;
                var value = key == "domains" ? V2pnRouteEntries.Domain(raw, out why) : V2pnRouteEntries.Ip(raw, out why);
                if (value == null)
                {
                    problem = $"{at}.{key}: {why}";
                    return null;
                }
                if (!allowGeo && V2pnRouteEntries.IsGeo(value))
                {
                    problem = $"{at}.{key}: \"{value}\": geo lists are not taken here";
                    return null;
                }
                if (to == Block && V2pnRouteEntries.TooBroadToBlock(value) is { } broad)
                {
                    problem = $"{at}.{key}: \"{value}\" blocks {broad}; a provider may block only narrow lists";
                    return null;
                }
                if (!list.Contains(value))
                {
                    list.Add(value);
                }
            }
        }
        if (domains.Count + ips.Count == 0)
        {
            problem = $"{at}: no domains or ips";
            return null;
        }
        problem = string.Empty;
        return new V2pnRouteRule(domains, ips, to);
    }

    internal static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text.Trim() : null;
}
