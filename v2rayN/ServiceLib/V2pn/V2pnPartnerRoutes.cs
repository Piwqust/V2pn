namespace ServiceLib;

/// <summary>
/// The routing presets of partners (reviewed routing.json) as ordinary routing items with fixed ids, so the routing
/// editor, the core and the menu "What goes through the VPN" treat them like V2pn's own modes. V2pn writes them;
/// a preset whose partner went away (unlinked, expired, revoked) or whose file is no longer usable is removed, and if
/// it was the active mode, V2pn's "Everything through VPN" takes its place.
/// </summary>
public static class V2pnPartnerRoutes
{
    public const string IdPrefix = "v2pn-partner-";

    /// <summary>The ids of the preset's own rules in its routing item; any other rule there is the person's.</summary>
    public const string RuleIdPrefix = "v2pn-partner-rule-";

    /// <summary>The mode that replaces a removed partner preset when it was active (V2pn's "Everything through VPN").</summary>
    public const string FallbackId = "v2pn-all";

    public static string Id(string partnerId) => IdPrefix + partnerId;

    public static bool IsPartnerRoute(string? routingId) => routingId?.StartsWith(IdPrefix, StringComparison.Ordinal) == true;

    public static string? PartnerOf(string? routingId) => IsPartnerRoute(routingId) ? routingId![IdPrefix.Length..] : null;

    /// <summary>
    /// A rule that matches everything (all ports, any network, nothing else): the default at the end of a routing mode.
    /// A partner's core.json rules go right before it, after every rule the person has.
    /// </summary>
    public static bool IsCatchAll(RulesItem rule) =>
        rule.Enabled && rule.RuleType != ERuleType.DNS
        && rule.Domain is not { Count: > 0 } && rule.Ip is not { Count: > 0 } && rule.Process is not { Count: > 0 }
        && rule.Protocol is not { Count: > 0 } && rule.InboundTag is not { Count: > 0 }
        && (rule.Network.IsNullOrEmpty() || rule.Network.Replace(" ", string.Empty) is "tcp,udp" or "udp,tcp")
        && (rule.Port.IsNullOrEmpty() || rule.Port.Trim() is "0-65535" or "1-65535");

    /// <summary>The name of a partner's mode: "Kestrel routes" / «Маршруты Kestrel».</summary>
    public static string Title(string partnerName) => string.Format(ResUI.V2pnPartnerRoutesName, partnerName);

    /// <summary>What <see cref="Plan"/> decided.</summary>
    /// <param name="Upserts">Items to write (new, or with new rules or names).</param>
    /// <param name="Deletes">Items of partners that are gone.</param>
    /// <param name="Activate">The item to make active instead of a deleted active one, or null.</param>
    /// <param name="ActiveChanged">The active mode's rules change or the active mode goes: the core needs a reload.</param>
    public sealed record Changes(IReadOnlyList<RoutingItem> Upserts, IReadOnlyList<RoutingItem> Deletes, RoutingItem? Activate, bool ActiveChanged)
    {
        public bool Any => Upserts.Count > 0 || Deletes.Count > 0;
    }

    /// <summary>
    /// Pure: compares the routing items on this computer with the partners whose routing.json is usable.
    /// </summary>
    public static Changes Plan(IReadOnlyList<RoutingItem> existing, IEnumerable<V2pnPartner> partners)
    {
        var upserts = new List<RoutingItem>();
        var deletes = new List<RoutingItem>();
        var activeChanged = false;
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        var sort = (existing.Count > 0 ? existing.Max(t => t.Sort) : 0) + 1;
        foreach (var partner in partners)
        {
            if (partner.Features.Routing is not { } preset)
            {
                continue;
            }
            var id = Id(partner.Id);
            wanted.Add(id);
            var presetRules = preset.ToRules();
            for (var i = 0; i < presetRules.Count; i++)
            {
                presetRules[i].Id = RuleIdPrefix + i;
            }
            var item = existing.FirstOrDefault(t => t.Id == id);
            // Rules the person added to the preset in the routing editor stay, and stay first: the person's own rules win.
            var own = (JsonUtils.Deserialize<List<RulesItem>>(item?.RuleSet ?? string.Empty) ?? [])
                .Where(r => r.Id?.StartsWith(RuleIdPrefix, StringComparison.Ordinal) != true && !presetRules.Any(p => SameRule(p, r)))
                .ToList();
            var rules = own.Concat(presetRules).ToList();
            var ruleSet = JsonUtils.Serialize(rules, false);
            if (item != null && item.Remarks == Title(partner.Name) && item.DomainStrategy == preset.DomainStrategy && item.RuleSet == ruleSet)
            {
                continue;
            }
            var fresh = item == null ? new RoutingItem { Id = id, Sort = sort++, Enabled = true } : JsonUtils.DeepCopy(item)!;
            fresh.Remarks = Title(partner.Name);
            fresh.DomainStrategy = preset.DomainStrategy;
            fresh.DomainStrategy4Singbox = string.Empty;
            fresh.Enabled = true;
            fresh.RuleSet = ruleSet;
            fresh.RuleNum = rules.Count;
            upserts.Add(fresh);
            activeChanged |= item?.IsActive == true;
        }
        foreach (var item in existing.Where(t => IsPartnerRoute(t.Id) && !wanted.Contains(t.Id)))
        {
            deletes.Add(item);
            activeChanged |= item.IsActive;
        }
        RoutingItem? activate = null;
        if (deletes.Any(d => d.IsActive))
        {
            activate = existing.FirstOrDefault(t => t.Id == FallbackId)
                       ?? existing.Where(t => !IsPartnerRoute(t.Id)).OrderBy(t => t.Sort).FirstOrDefault();
        }
        return new Changes(upserts, deletes, activate, activeChanged);
    }

    /// <summary>Brings the partner presets in the database in line with <paramref name="partners"/>; returns what changed.</summary>
    public static async Task<Changes> SyncAsync(Config config, IEnumerable<V2pnPartner> partners)
    {
        var existing = await ConfigDbQuery.RoutingItems() ?? [];
        var changes = Plan(existing, partners);
        foreach (var item in changes.Upserts)
        {
            // Saved as planned: the preset's rule ids tell its rules from the person's.
            await ConfigDbHandler.SaveRoutingItem(item);
        }
        foreach (var item in changes.Deletes)
        {
            await ConfigDbHandler.RemoveRoutingItem(item);
            Logging.SaveLog($"{nameof(V2pnPartnerRoutes)}: removed {item.Id}");
        }
        if (changes.Activate != null)
        {
            await ConfigDbHandler.SetDefaultRouting(changes.Activate);
        }
        return changes;
    }

    // What a rule does, whatever its id (a preset saved by an earlier build carries generated ids).
    private static bool SameRule(RulesItem a, RulesItem b)
    {
        var x = JsonUtils.DeepCopy(a)!;
        var y = JsonUtils.DeepCopy(b)!;
        x.Id = y.Id = string.Empty;
        return JsonUtils.Serialize(x, false) == JsonUtils.Serialize(y, false);
    }
}
