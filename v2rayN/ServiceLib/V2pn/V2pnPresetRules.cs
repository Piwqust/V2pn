using System.Globalization;

namespace ServiceLib;

/// <summary>V2pn's three ways of routing, the menu under Connect.</summary>
public enum EV2pnPreset
{
    RussiaDirect,
    BlockedOnly,
    All,
}

/// <summary>One site the person sends one way whatever the preset says.</summary>
public sealed class V2pnSiteRule
{
    /// <summary>The site's domain, lower case, in ASCII (punycode for Cyrillic names).</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>True: always direct, past the server. False: always through the server.</summary>
    public bool Direct { get; set; }

    /// <summary>True: the domain and every name under it (www., m., cdn.…). False: exactly this name.</summary>
    public bool Subdomains { get; set; } = true;
}

/// <summary>One program that always goes directly, past the server, while VPN mode is on.</summary>
public sealed class V2pnAppRule
{
    /// <summary>
    /// The program's file name as the system names its process, in the case the file has: "SberBank.exe" on Windows,
    /// the app's executable ("Telegram", from the bundle's Info.plist) on a Mac.
    /// </summary>
    public string Process { get; set; } = string.Empty;
}

/// <summary>
/// V2pn: the rules of the three routing presets, and the sites the person sends one way on top of them. They were built
/// inside the window, where nothing tested them, though they decide where every connection goes.
/// </summary>
public static class V2pnPresetRules
{
    /// <summary>At most this many sites: the list is read by hand, not imported.</summary>
    public const int MaxSites = 200;

    /// <summary>At most this many programs.</summary>
    public const int MaxApps = 100;

    /// <summary>
    /// Marks the rule of the programs that always go directly. Rule ids are regenerated each time the presets are
    /// written, so the core config builders find the rule by this name to keep it out of every core that does not
    /// run the tunnel (<see cref="IsAppRule"/>).
    /// </summary>
    public const string AppsRemarks = "V2pn: programs that always go directly";

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

    /// <summary>
    /// The rules of a preset. The person's programs come first: a program they send directly is not pulled back
    /// through the server by a site rule, ad blocking or the preset (pinned programs, V2pnPins, still go in front of
    /// everything, since a pin names its own server). Then the person's sites: their choice wins over the preset and
    /// over ad blocking (a site they send through the server on purpose is not blocked). Then ads, the local network,
    /// the preset. The programs' rule works only in VPN mode: the core config builders leave it out of every core that
    /// does not run the tunnel (<see cref="IsAppRule"/>).
    /// </summary>
    public static List<RulesItem> Rules(EV2pnPreset kind, bool blockAds, IReadOnlyList<V2pnSiteRule>? sites = null,
        IReadOnlyList<V2pnAppRule>? apps = null)
    {
        var rules = AppRules(apps);
        rules.AddRange(SiteRules(sites));
        if (blockAds)
        {
            rules.Add(Rule(Global.BlockTag, domain: ["geosite:category-ads-all"]));
        }
        rules.Add(Rule(Global.DirectTag, ip: ["geoip:private"]));
        rules.Add(Rule(Global.DirectTag, domain: ["geosite:private"]));

        switch (kind)
        {
            case EV2pnPreset.All:
                rules.Add(Rule(Global.ProxyTag, port: "0-65535"));
                break;

            case EV2pnPreset.RussiaDirect:
                rules.Add(Rule(Global.DirectTag, protocol: ["bittorrent"]));
                // Blocked .ru sites still go through the server, so this comes before the Russian rules.
                rules.Add(Rule(Global.ProxyTag, domain: ["geosite:ru-blocked"]));
                rules.Add(Rule(Global.DirectTag, domain: RussianDomains));
                rules.Add(Rule(Global.DirectTag, ip: ["geoip:ru"]));
                rules.Add(Rule(Global.ProxyTag, port: "0-65535"));
                break;

            case EV2pnPreset.BlockedOnly:
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

    /// <summary>One rule for the sites sent direct and one for those sent through the server, in that order.</summary>
    private static List<RulesItem> SiteRules(IReadOnlyList<V2pnSiteRule>? sites)
    {
        var rules = new List<RulesItem>();
        foreach (var direct in new[] { true, false })
        {
            var domains = (sites ?? [])
                .Where(s => s.Direct == direct && NormalizeDomain(s.Domain) == s.Domain)
                .Select(s => (s.Subdomains ? "domain:" : "full:") + s.Domain)
                .Distinct()
                .ToArray();
            if (domains.Length > 0)
            {
                rules.Add(Rule(direct ? Global.DirectTag : Global.ProxyTag, domain: domains));
            }
        }
        return rules;
    }

    /// <summary>
    /// One rule that sends the person's programs directly. Xray and sing-box compare process names case by case while
    /// Windows and macOS file names are not, so each name also goes in lower case and capitalized (V2pnPins).
    /// </summary>
    private static List<RulesItem> AppRules(IReadOnlyList<V2pnAppRule>? apps)
    {
        var names = V2pnPins.ProgramNames((apps ?? [])
            .Select(a => NormalizeProgram(a.Process))
            .OfType<string>()
            .DistinctBy(ProgramKey));
        return names.Count == 0
            ? []
            : [new RulesItem { OutboundTag = Global.DirectTag, Process = names, Remarks = AppsRemarks }];
    }

    /// <summary>The rule of the programs that always go directly, which only the core running the tunnel takes.</summary>
    public static bool IsAppRule(RulesItem rule) =>
        rule.Remarks == AppsRemarks && rule.Process is { Count: > 0 };

    private static readonly char[] NotInFileNames = ['<', '>', ':', '"', '|', '?', '*'];

    /// <summary>
    /// The program in <paramref name="input"/> as a rule takes it, or null: a file name, or a path to the program
    /// (its file name is taken; a Mac's "Telegram.app" gives "Telegram", which is the executable of most apps). The
    /// case is kept as written, since the file's own case is the one most likely to match.
    /// </summary>
    public static string? NormalizeProgram(string? input)
    {
        var text = (input ?? string.Empty).Trim().Trim('"').Trim().TrimEnd('/', '\\');
        var name = text[(text.LastIndexOfAny(['/', '\\']) + 1)..].Trim();
        if (name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4].Trim();
        }
        return name.Length is 0 or > 255 || name is "." or ".." || name.StartsWith('#')
               || name.IndexOfAny(NotInFileNames) >= 0 || name.Any(char.IsControl)
            ? null
            : name;
    }

    /// <summary>One key for the names of one program: "SberBank.exe", "sberbank.EXE" and "sberbank" are the same.</summary>
    public static string ProgramKey(string name)
    {
        var key = name.ToLowerInvariant();
        return key.EndsWith(".exe", StringComparison.Ordinal) && key.Length > 4 ? key[..^4] : key;
    }

    /// <summary>The list with <paramref name="app"/> in it: an earlier entry for the same program is replaced.</summary>
    public static List<V2pnAppRule> With(IReadOnlyList<V2pnAppRule>? apps, V2pnAppRule app) =>
        (apps ?? []).Where(a => ProgramKey(a.Process) != ProgramKey(app.Process)).Append(app).TakeLast(MaxApps).ToList();

    private static readonly IdnMapping Idn = new();
    private static readonly Regex Label = new("^(?!-)[a-z0-9-]{1,63}(?<!-)$");

    /// <summary>
    /// The domain in <paramref name="input"/> as a rule takes it, or null: an address, a link or a name, with or without
    /// "www."-like parts kept as typed; Cyrillic names become punycode. A bare top-level name ("ru") or an IP address is
    /// not a site.
    /// </summary>
    public static string? NormalizeDomain(string? input)
    {
        var text = (input ?? string.Empty).Trim();
        if (text.Length == 0 || text.Length > 253)
        {
            return null;
        }
        if (text.Contains("://", StringComparison.Ordinal) && Utils.TryUri(text) is { } uri)
        {
            text = uri.Host;
        }
        text = text.Split('/', '?', '#')[0];
        if (text.LastIndexOf(':') is var colon and > 0 && !text.Contains('['))
        {
            text = text[..colon];
        }
        text = text.Trim('.').ToLowerInvariant();
        if (IPAddress.TryParse(text, out _))
        {
            return null;
        }
        try
        {
            text = Idn.GetAscii(text);
        }
        catch (ArgumentException)
        {
            return null;
        }
        var labels = text.Split('.');
        return labels.Length >= 2 && labels.All(Label.IsMatch) && !labels[^1].All(char.IsDigit) ? text : null;
    }

    /// <summary>The list with <paramref name="site"/> in it: an earlier entry for the same domain is replaced.</summary>
    public static List<V2pnSiteRule> With(IReadOnlyList<V2pnSiteRule>? sites, V2pnSiteRule site) =>
        (sites ?? []).Where(s => s.Domain != site.Domain).Append(site).TakeLast(MaxSites).ToList();
}
