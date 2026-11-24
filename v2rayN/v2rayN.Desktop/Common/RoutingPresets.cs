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

    /// <summary>
    /// The rules of a preset, with the person's programs and then their sites first (V2pnPresetRules, in ServiceLib
    /// with its tests).
    /// </summary>
    public static List<RulesItem> Rules(RoutingPresetKind kind, bool blockAds, IReadOnlyList<V2pnSiteRule>? sites = null,
        IReadOnlyList<V2pnAppRule>? apps = null) =>
        V2pnPresetRules.Rules(kind switch
        {
            RoutingPresetKind.RussiaDirect => EV2pnPreset.RussiaDirect,
            RoutingPresetKind.BlockedOnly => EV2pnPreset.BlockedOnly,
            _ => EV2pnPreset.All,
        }, blockAds, sites, apps);

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
            var rules = JsonUtils.Serialize(Rules(kind, config.UiItem.V2pnBlockAds, config.UiItem.V2pnSiteRules,
                config.UiItem.V2pnDirectApps), false);
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
