namespace ServiceLib;

/// <summary>
/// Programs and sites that always go through one server or group, whatever server is selected and
/// whatever rule set is active: for example, every Claude on the computer through one pool.
/// </summary>
public class V2pnPin
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public bool Enabled { get; set; } = true;
    public List<string>? Programs { get; set; }
    public List<string>? Sites { get; set; }

    /// <summary>The server or group by IndexId, so renaming it does not break the pin.</summary>
    public string? ServerId { get; set; }

    /// <summary>Its name when it was chosen: found by name if a subscription update gave it a new id.</summary>
    public string? ServerName { get; set; }
}

public static class V2pnPins
{
    /// <summary>
    /// Outbound tag of a pin's rule. Ordinary rules name their server by its remarks, and two servers
    /// from different subscriptions often share one; the tag carries the id instead.
    /// </summary>
    public const string TagPrefix = "v2pn-pin:";

    // The Claude app is Claude.exe and Claude Code is claude.exe; ProgramNames adds the other case.
    public static readonly string[] ClaudePrograms = ["claude.exe"];

    public static readonly string[] ClaudeSites = ["anthropic.com", "claude.ai", "claude.com", "claudeusercontent.com"];

    public static List<V2pnPin> All(Config config) => config.RoutingBasicItem?.V2pnPins ?? [];

    public static bool HasRules(V2pnPin pin) => ProgramNames(pin.Programs).Count > 0 || SiteNames(pin.Sites).Count > 0;

    public static bool IsUsable(V2pnPin pin) =>
        pin.Enabled && (pin.ServerId.IsNotEmpty() || pin.ServerName.IsNotEmpty()) && HasRules(pin);

    public static bool IsClaude(V2pnPin pin) =>
        pin.Programs?.Any(p => ClaudePrograms.Contains(p.Trim(), StringComparer.OrdinalIgnoreCase)) == true;

    public static V2pnPin NewClaude() => new()
    {
        Id = Utils.GetGuid(false),
        Name = "Claude",
        Programs = [.. ClaudePrograms],
        Sites = [.. ClaudeSites],
    };

    public static async Task<ProfileItem?> FindServerAsync(V2pnPin pin)
    {
        if (pin.ServerId.IsNotEmpty() && await ConfigDbQuery.GetProfileItem(pin.ServerId) is { } byId)
        {
            return byId;
        }
        return await ConfigDbQuery.GetProfileItemViaRemarks(pin.ServerName);
    }

    /// <summary>
    /// Puts the enabled pins in front of the active rule set's rules and maps each pin's server, so the
    /// Xray and sing-box builders route them first. A pin whose server is gone is left out with a
    /// warning; its programs then follow the ordinary rules.
    /// </summary>
    public static async Task<CoreConfigContext> ApplyAsync(CoreConfigContext context, NodeValidatorResult result)
    {
        var pins = All(context.AppConfig).Where(IsUsable).ToList();
        if (pins.Count == 0 || context.RoutingItem is null)
        {
            return context;
        }

        var rules = new List<RulesItem>();
        foreach (var pin in pins)
        {
            var server = await FindServerAsync(pin);
            if (server is null)
            {
                result.Warnings.Add(MissingServerText(pin));
                continue;
            }

            var (actServer, serverResult) = await CoreConfigContextBuilder.ResolveNodeAsync(context, server, false);
            result.Warnings.AddRange(serverResult.Warnings.Select(w => $"{Label(pin)}: {w}"));
            if (!serverResult.Success)
            {
                result.Warnings.AddRange(serverResult.Errors.Select(e => $"{Label(pin)}: {e}"));
                result.Warnings.Add(MissingServerText(pin));
                continue;
            }

            var rule = ToRule(pin, server.IndexId);
            context.AllProxiesMap[$"remark:{rule.OutboundTag}"] = actServer;
            rules.Add(rule);
        }

        return rules.Count == 0 ? context : context with { RoutingItem = WithRulesFirst(context.RoutingItem, rules) };
    }

    public static RulesItem ToRule(V2pnPin pin, string serverId)
    {
        var programs = ProgramNames(pin.Programs);
        var sites = SiteNames(pin.Sites);
        return new RulesItem
        {
            Id = $"v2pn-pin-{pin.Id}",
            Remarks = pin.Name,
            Enabled = true,
            Process = programs.Count > 0 ? programs : null,
            Domain = sites.Count > 0 ? sites : null,
            OutboundTag = TagPrefix + serverId,
        };
    }

    public static RoutingItem WithRulesFirst(RoutingItem routing, List<RulesItem> first)
    {
        var copy = JsonUtils.DeepCopy(routing) ?? throw new InvalidOperationException("RoutingItem copy failed");
        List<RulesItem> rules = [.. first, .. JsonUtils.Deserialize<List<RulesItem>>(routing.RuleSet) ?? []];
        copy.RuleSet = JsonUtils.Serialize(rules, false);
        copy.RuleNum = rules.Count;
        return copy;
    }

    /// <summary>
    /// Xray and sing-box compare process names case by case, Windows file names do not: each bare name
    /// also goes in lower case and capitalized. Outside Windows the ".exe" is dropped as well, so a list
    /// written on Windows still matches "claude" on a Mac. A path (with a slash) is passed on as written.
    /// </summary>
    public static List<string> ProgramNames(IEnumerable<string>? programs)
    {
        var names = new List<string>();
        foreach (var raw in programs ?? [])
        {
            var program = raw.Trim().Trim('"').Trim();
            if (program.IsNullOrEmpty() || program.StartsWith('#'))
            {
                continue;
            }
            if (program.Contains('/') || program.Contains('\\'))
            {
                Add(program.Replace('\\', '/'));
                continue;
            }

            var lower = program.ToLowerInvariant();
            string[] variants = [program, lower, char.ToUpperInvariant(lower[0]) + lower[1..]];
            foreach (var name in variants)
            {
                Add(name);
                if (!Utils.IsWindows() && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && name.Length > 4)
                {
                    Add(name[..^4]);
                }
            }
        }
        return names;

        void Add(string name)
        {
            if (!names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }
        }
    }

    /// <summary>
    /// A bare name covers the site and its subdomains ("claude.ai" becomes "domain:claude.ai"); a name
    /// with a prefix (geosite:, full:, regexp:, keyword:) is passed on as written.
    /// </summary>
    public static List<string> SiteNames(IEnumerable<string>? sites)
    {
        var names = new List<string>();
        foreach (var raw in sites ?? [])
        {
            var site = raw.Trim();
            if (site.IsNullOrEmpty() || site.StartsWith('#'))
            {
                continue;
            }
            if (!site.Contains(':'))
            {
                site = "domain:" + site.TrimStart('.').TrimEnd('/');
            }
            if (!names.Contains(site, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(site);
            }
        }
        return names;
    }

    public static string Label(V2pnPin pin) => pin.Name.IsNotEmpty() ? pin.Name! : string.Join(", ", pin.Programs ?? []);

    public static string MissingServerText(V2pnPin pin) => T(
        $"“{Label(pin)}”: the server or group “{pin.ServerName}” is gone. These programs follow the ordinary rules for now.",
        $"«{Label(pin)}»: сервер или группа «{pin.ServerName}» не найдены. Пока эти программы идут по обычным правилам.");

    // The interface language from the config: core configs are built on pool threads, which do not
    // carry the UI culture.
    private static string T(string en, string ru) =>
        ConfigTextHandler.Config?.UiItem?.CurrentLanguage?.StartsWith("ru", StringComparison.OrdinalIgnoreCase) == true ? ru : en;
}
