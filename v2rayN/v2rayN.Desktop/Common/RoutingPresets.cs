namespace v2rayN.Desktop.Common;

public enum RoutingPresetKind
{
    All,
    RussiaDirect,
    BlockedOnly,
}

/// <summary>
/// The three routing modes V2pn offers in plain words. They are ordinary v2rayN routing items with
/// fixed ids, so the routing editor, the tray and the core see nothing special; V2pn only rewrites
/// their rules (for the ads toggle) and keeps their names in the interface language.
/// <para>
/// The Russian modes rely on tags that exist only in the runetfreedom geo files (ru-blocked,
/// ru-available-only-inside, category-gov-ru…). An unknown tag stops the core, so selecting one of
/// them first switches the geo sources to runetfreedom (see <see cref="NeedsRussianSources"/>).
/// </para>
/// </summary>
public static class RoutingPresets
{
    public const string IdAll = "v2pn-all";
    public const string IdRussiaDirect = "v2pn-ru-direct";
    public const string IdBlockedOnly = "v2pn-blocked-only";

    public static readonly RoutingPresetKind[] Kinds = [RoutingPresetKind.RussiaDirect, RoutingPresetKind.BlockedOnly, RoutingPresetKind.All];

    public static string Id(RoutingPresetKind kind) => kind switch
    {
        RoutingPresetKind.All => IdAll,
        RoutingPresetKind.RussiaDirect => IdRussiaDirect,
        _ => IdBlockedOnly,
    };

    public static RoutingPresetKind? KindOf(string? id) => id switch
    {
        IdAll => RoutingPresetKind.All,
        IdRussiaDirect => RoutingPresetKind.RussiaDirect,
        IdBlockedOnly => RoutingPresetKind.BlockedOnly,
        _ => null,
    };

    public static bool IsPreset(string? id) => KindOf(id) != null;

    /// <summary>
    /// The name to show for any rule set. v2rayN creates its three built-in sets with Chinese names
    /// ("V4-绕过大陆(Whitelist)"…); they are shown by what they do, in the interface language.
    /// </summary>
    public static string DisplayName(string? remarks)
    {
        if (remarks.IsNullOrEmpty())
        {
            return string.Empty;
        }
        if (remarks.EndsWith("绕过大陆(Whitelist)", StringComparison.Ordinal))
        {
            return L.RouteBuiltinWhitelist;
        }
        if (remarks.EndsWith("黑名单(Blacklist)", StringComparison.Ordinal))
        {
            return L.RouteBuiltinBlacklist;
        }
        return remarks.EndsWith("全局(Global)", StringComparison.Ordinal) ? L.RouteBuiltinGlobal : remarks;
    }

    public static readonly Avalonia.Data.Converters.FuncValueConverter<string?, string> NameConverter = new(DisplayName);

    /// <summary>One of the three sets v2rayN creates on its own.</summary>
    public static bool IsV2rayNBuiltin(string? remarks) =>
        remarks != null && (remarks.EndsWith("绕过大陆(Whitelist)", StringComparison.Ordinal)
                            || remarks.EndsWith("黑名单(Blacklist)", StringComparison.Ordinal)
                            || remarks.EndsWith("全局(Global)", StringComparison.Ordinal));

    public static string Title(RoutingPresetKind kind) => kind switch
    {
        RoutingPresetKind.All => L.RouteAll,
        RoutingPresetKind.RussiaDirect => L.RouteRussiaDirect,
        _ => L.RouteBlockedOnly,
    };

    public static string Description(RoutingPresetKind kind) => kind switch
    {
        RoutingPresetKind.All => L.RouteAllHint,
        RoutingPresetKind.RussiaDirect => L.RouteRussiaDirectHint,
        _ => L.RouteBlockedOnlyHint,
    };

    /// <summary>The Russian modes need the runetfreedom geo files.</summary>
    public static bool NeedsRussianSources(Config config, RoutingPresetKind kind) =>
        kind != RoutingPresetKind.All && config.ConstItem.GeoSourceUrl != Global.GeoFilesSources[1];

    private static RulesItem Rule(string outbound, string[]? domain = null, string[]? ip = null, string? port = null,
        string? network = null, string[]? protocol = null) => new()
        {
            OutboundTag = outbound,
            Domain = domain?.ToList(),
            Ip = ip?.ToList(),
            Port = port,
            Network = network,
            Protocol = protocol?.ToList(),
        };

    private static readonly string[] RussianDomains =
    [
        "geosite:ru-available-only-inside", "geosite:category-gov-ru", "geosite:category-bank-ru",
        "geosite:category-ru", "domain:ru", "domain:su", "domain:xn--p1ai",
    ];

    public static List<RulesItem> Rules(RoutingPresetKind kind, bool blockAds)
    {
        var rules = new List<RulesItem>();
        if (blockAds)
        {
            rules.Add(Rule(Global.BlockTag, domain: ["geosite:category-ads-all"]));
        }
        rules.Add(Rule(Global.DirectTag, ip: ["geoip:private"]));
        rules.Add(Rule(Global.DirectTag, domain: ["geosite:private"]));

        switch (kind)
        {
            case RoutingPresetKind.All:
                rules.Add(Rule(Global.ProxyTag, port: "0-65535"));
                break;

            case RoutingPresetKind.RussiaDirect:
                rules.Add(Rule(Global.DirectTag, protocol: ["bittorrent"]));
                // Blocked .ru sites still go through the server, so this comes before the Russian rules.
                rules.Add(Rule(Global.ProxyTag, domain: ["geosite:ru-blocked"]));
                rules.Add(Rule(Global.DirectTag, domain: RussianDomains));
                rules.Add(Rule(Global.DirectTag, ip: ["geoip:ru"]));
                rules.Add(Rule(Global.ProxyTag, port: "0-65535"));
                break;

            case RoutingPresetKind.BlockedOnly:
                rules.Add(Rule(Global.DirectTag, protocol: ["bittorrent"]));
                // Remote DNS servers have to be reachable through the server too.
                rules.Add(Rule(Global.ProxyTag, ip: ["1.1.1.1", "1.0.0.1", "8.8.8.8", "8.8.4.4"]));
                rules.Add(Rule(Global.ProxyTag, domain: ["geosite:ru-blocked", "geosite:youtube", "geosite:discord", "geosite:telegram", "geosite:category-ai-!cn"]));
                rules.Add(Rule(Global.ProxyTag, ip: ["geoip:ru-blocked", "geoip:telegram"]));
                // Discord voice.
                rules.Add(Rule(Global.ProxyTag, port: "50000-65535", network: "udp"));
                rules.Add(Rule(Global.DirectTag, port: "0-65535"));
                break;
        }
        return rules;
    }

    /// <summary>
    /// Creates the preset items or refreshes their names and rules; keeps which one is active.
    /// Returns true when the rules changed (the core needs a reload if a preset is active).
    /// </summary>
    public static async Task<bool> EnsureAsync(Config config)
    {
        var existing = await ConfigDbQuery.RoutingItems() ?? [];
        var minSort = existing.Count > 0 ? existing.Min(t => t.Sort) : 0;
        var changed = false;
        var i = 0;
        foreach (var kind in Kinds)
        {
            var id = Id(kind);
            var item = existing.FirstOrDefault(t => t.Id == id);
            var rules = JsonUtils.Serialize(Rules(kind, config.UiItem.V2pnBlockAds), false);
            var strategy = kind == RoutingPresetKind.All ? Global.AsIs : Global.IPOnDemand;
            if (item != null && item.Remarks == Title(kind) && item.DomainStrategy == strategy && SameRules(item.RuleSet, rules))
            {
                i++;
                continue;
            }
            item ??= new RoutingItem { Id = id, Sort = minSort - Kinds.Length + i };
            item.Remarks = Title(kind);
            item.Enabled = true;
            item.DomainStrategy = strategy;
            await ConfigDbHandler.AddBatchRoutingRules(item, rules);
            changed = true;
            i++;
        }
        return changed;
    }

    // Stored rules carry generated ids; compare what they do.
    private static bool SameRules(string? stored, string fresh)
    {
        var a = JsonUtils.Deserialize<List<RulesItem>>(stored ?? string.Empty);
        if (a == null)
        {
            return false;
        }
        a.ForEach(r => r.Id = null!);
        return JsonUtils.Serialize(a, false) == fresh;
    }
}
