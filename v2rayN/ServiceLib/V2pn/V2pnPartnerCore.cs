using System.Globalization;

namespace ServiceLib;

/// <summary>An Xray balancer of core.json: one of <see cref="Selector"/> carries the traffic, picked by <see cref="Strategy"/>.</summary>
public sealed record V2pnBalancer(string Tag, IReadOnlyList<string> Selector, string Strategy);

/// <summary>
/// core.json: a piece of Xray or sing-box config a provider adds to the configs of its own servers, reviewed by hand.
/// It may bring extra outbounds (a relay its servers are dialed through, servers for one purpose), balancers or
/// urltest groups over them, and rules that lead sites to them. Nothing else: no listeners, files, API, DNS servers
/// or direct and block rules (those are routing.json's), and every outbound passes <see cref="V2pnCoreConfigFilter"/>
/// untouched. Its tags are prefixed with "partner-&lt;id&gt;-" in the config, so they never meet V2pn's own;
/// "proxy" means the provider's server the person connected to.
/// </summary>
public sealed class V2pnCoreFragment
{
    public const string Xray = "xray";
    public const string Singbox = "sing-box";

    public const int MaxOutbounds = 8;
    public const int MaxBalancers = 4;
    public const int MaxRules = 32;
    public const int MaxSelector = 8;
    public const string DefaultProbeUrl = "https://www.gstatic.com/generate_204";
    public const string DefaultProbeInterval = "1m";

    private static readonly Regex TagPattern = new("^[a-z0-9][a-z0-9-]{0,23}$", RegexOptions.CultureInvariant);
    private static readonly Regex Duration = new("^([1-9][0-9]{0,3})(s|m|h)$", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> XrayProtocols = ["vless", "vmess", "trojan", "shadowsocks", "socks", "http", "hysteria", "wireguard"];
    private static readonly HashSet<string> SingboxTypes = ["vless", "vmess", "trojan", "shadowsocks", "socks", "http", "hysteria2", "tuic", "anytls", "urltest"];
    private static readonly HashSet<string> BalancerStrategies = ["random", "roundRobin", "leastPing", "leastLoad"];

    /// <summary>
    /// Top-level keys a whole-config subscription of the provider may keep besides what the filter keeps, when core.json
    /// lists them in allowTop: sing-box's NTP client and its certificate store (files in it are still taken out).
    /// Nothing for Xray: every other key it has opens a listener or the API.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> AllowableTop = new Dictionary<string, IReadOnlyList<string>>
    {
        [Xray] = [],
        [Singbox] = ["ntp", "certificate"],
    };

    private V2pnCoreFragment(string core, IReadOnlyList<JsonObject> outbounds, string? dialer, IReadOnlyList<V2pnBalancer> balancers,
        string probeUrl, string probeInterval, IReadOnlyList<V2pnRouteRule> rules, IReadOnlyList<string> allowTop)
    {
        Core = core;
        Outbounds = outbounds;
        Dialer = dialer;
        Balancers = balancers;
        ProbeUrl = probeUrl;
        ProbeInterval = probeInterval;
        Rules = rules;
        AllowTop = allowTop;
    }

    /// <summary>"xray" or "sing-box".</summary>
    public string Core { get; }

    public ECoreType CoreType => Core == Xray ? ECoreType.Xray : ECoreType.sing_box;

    /// <summary>The outbounds as written, with the provider's own tags.</summary>
    public IReadOnlyList<JsonObject> Outbounds { get; }

    /// <summary>The outbound the provider's servers are dialed through, if any.</summary>
    public string? Dialer { get; }

    public IReadOnlyList<V2pnBalancer> Balancers { get; }
    public string ProbeUrl { get; }
    public string ProbeInterval { get; }
    public IReadOnlyList<V2pnRouteRule> Rules { get; }
    public IReadOnlyList<string> AllowTop { get; }

    public static string Prefix(string partnerId) => $"partner-{partnerId}-";

    /// <summary>The tag a name of core.json gets in the config: "proxy" stays V2pn's, the rest is prefixed.</summary>
    public static string TagFor(string partnerId, string tag) => tag == Global.ProxyTag ? Global.ProxyTag : Prefix(partnerId) + tag;

    /// <summary>True for a tag this fragment put into a config of <paramref name="partnerId"/>.</summary>
    public bool Owns(string partnerId, string? tag) =>
        tag != null && tag.StartsWith(Prefix(partnerId), StringComparison.Ordinal)
        && OwnTags().Contains(tag[Prefix(partnerId).Length..]);

    private IEnumerable<string> OwnTags() => Outbounds.Select(o => Str(o["tag"])!).Concat(Balancers.Select(b => b.Tag));

    public string? DialerFor(string partnerId) => Dialer == null ? null : TagFor(partnerId, Dialer);

    /// <summary>
    /// The outbounds for a config, prefixed, with their references to each other rewritten and the provider's
    /// transport (when it has one and the person left it on) on those that reach a server directly.
    /// </summary>
    public List<JsonObject> OutboundsFor(string partnerId, V2pnTransport? transport)
    {
        var result = new List<JsonObject>();
        foreach (var source in Outbounds)
        {
            var outbound = (JsonObject)source.DeepClone();
            outbound["tag"] = TagFor(partnerId, Str(outbound["tag"])!);
            if (Core == Xray)
            {
                if (outbound["streamSettings"]?["sockopt"] is JsonObject sockopt && Str(sockopt["dialerProxy"]) is { } via)
                {
                    sockopt["dialerProxy"] = TagFor(partnerId, via);
                }
                transport?.ApplyXray(outbound);
            }
            else
            {
                if (Str(outbound["detour"]) is { } detour)
                {
                    outbound["detour"] = TagFor(partnerId, detour);
                }
                if (outbound["outbounds"] is JsonArray members)
                {
                    outbound["outbounds"] = new JsonArray(members.Select(m => (JsonNode)JsonValue.Create(TagFor(partnerId, Str(m)!))).ToArray());
                }
                if (transport?.Fragment != null && outbound["tls"] is JsonObject tls && tls["enabled"] is JsonValue enabled && enabled.TryGetValue<bool>(out var on) && on
                    && Str(outbound["detour"]) == null)
                {
                    tls["fragment"] = true;
                    tls["record_fragment"] = true;
                }
            }
            result.Add(outbound);
        }
        return result;
    }

    /// <summary>The balancers for an Xray config. Their tags end in "-balancer", so V2pn's rules to "&lt;tag&gt;" use them.</summary>
    public List<BalancersItem4Ray> XrayBalancers(string partnerId) =>
        Balancers.Select(b => new BalancersItem4Ray
        {
            tag = TagFor(partnerId, b.Tag) + Global.BalancerTagSuffix,
            selector = b.Selector.Select(s => TagFor(partnerId, s)).ToList(),
            strategy = new BalancersStrategy4Ray { type = b.Strategy },
        }).ToList();

    /// <summary>The rules for V2pn's routing: sites lead to the prefixed tags (balancers by their base tag).</summary>
    public List<RulesItem> RulesFor(string partnerId)
    {
        var rules = new List<RulesItem>();
        foreach (var rule in Rules)
        {
            var to = TagFor(partnerId, rule.To);
            if (rule.Domains.Count > 0)
            {
                rules.Add(new RulesItem { Id = $"v2pn-partner-core-{rules.Count}", Enabled = true, OutboundTag = to, Domain = rule.Domains.ToList() });
            }
            if (rule.Ips.Count > 0)
            {
                rules.Add(new RulesItem { Id = $"v2pn-partner-core-{rules.Count}", Enabled = true, OutboundTag = to, Ip = rule.Ips.ToList() });
            }
        }
        return rules;
    }

    /// <summary>Adds the balancers, the observatories they need and the outbounds of a partner to an Xray config (typed parts).</summary>
    public void MergeXrayRouting(V2rayConfig config, string partnerId)
    {
        if (Balancers.Count == 0)
        {
            return;
        }
        config.routing.balancers ??= [];
        config.routing.balancers.AddRange(XrayBalancers(partnerId));
        foreach (var balancer in Balancers)
        {
            var selectors = balancer.Selector.Select(s => TagFor(partnerId, s)).ToList();
            if (balancer.Strategy == "leastPing")
            {
                config.observatory ??= new Observatory4Ray { probeUrl = ProbeUrl, probeInterval = ProbeInterval, enableConcurrency = true, subjectSelector = [] };
                config.observatory.subjectSelector ??= [];
                config.observatory.subjectSelector.AddRange(selectors.Where(s => !config.observatory.subjectSelector.Contains(s)));
            }
            else if (balancer.Strategy == "leastLoad")
            {
                config.burstObservatory ??= new BurstObservatory4Ray
                {
                    subjectSelector = [],
                    pingConfig = new() { destination = ProbeUrl, interval = ProbeInterval, timeout = "30s", sampling = 2 },
                };
                config.burstObservatory.subjectSelector ??= [];
                config.burstObservatory.subjectSelector.AddRange(selectors.Where(s => !config.burstObservatory.subjectSelector.Contains(s)));
            }
        }
    }

    /// <summary>Appends the partner's outbounds to a generated config (Xray or sing-box JSON).</summary>
    public void MergeOutbounds(JsonObject config, string partnerId, V2pnTransport? transport)
    {
        if (config["outbounds"] is not JsonArray outbounds)
        {
            config["outbounds"] = outbounds = new JsonArray();
        }
        var taken = outbounds.OfType<JsonObject>().Select(o => Str(o["tag"])).ToHashSet();
        foreach (var outbound in OutboundsFor(partnerId, transport))
        {
            if (!taken.Contains(Str(outbound["tag"])))
            {
                outbounds.Add(outbound);
            }
        }
    }

    /// <summary>The fragment when <paramref name="json"/> is a valid core.json; otherwise null and the first problem found.</summary>
    public static V2pnCoreFragment? Parse(byte[] json, out string problem)
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
            var core = V2pnRoutingPreset.Text(root["core"]);
            if (core is not (Xray or Singbox))
            {
                problem = "core: xray or sing-box";
                return null;
            }
            foreach (var key in root.Select(p => p.Key))
            {
                if (key is not ("schema" or "core" or "outbounds" or "dialer" or "balancers" or "observatory" or "rules" or "allowTop" or "note"))
                {
                    problem = $"{key}: not a key of core.json (outbounds, dialer, balancers, observatory, rules, allowTop)";
                    return null;
                }
            }

            var outbounds = new List<JsonObject>();
            var tags = new HashSet<string>(StringComparer.Ordinal);
            if (root["outbounds"] is { } outboundNode)
            {
                if (outboundNode is not JsonArray list || list.Count > MaxOutbounds)
                {
                    problem = $"outbounds: a list of at most {MaxOutbounds}";
                    return null;
                }
                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i] is not JsonObject outbound)
                    {
                        problem = $"outbounds[{i}]: not an object";
                        return null;
                    }
                    if (CheckTag(Str(outbound["tag"]), tags, $"outbounds[{i}].tag") is { } tagProblem)
                    {
                        problem = tagProblem;
                        return null;
                    }
                    tags.Add(Str(outbound["tag"])!);
                    outbounds.Add(outbound);
                }
            }

            var balancers = new List<V2pnBalancer>();
            if (root["balancers"] is { } balancerNode)
            {
                if (core != Xray)
                {
                    problem = "balancers: Xray only; sing-box takes an urltest outbound instead";
                    return null;
                }
                if (balancerNode is not JsonArray list || list.Count > MaxBalancers)
                {
                    problem = $"balancers: a list of at most {MaxBalancers}";
                    return null;
                }
                for (var i = 0; i < list.Count; i++)
                {
                    var at = $"balancers[{i}]";
                    if (list[i] is not JsonObject b)
                    {
                        problem = $"{at}: not an object";
                        return null;
                    }
                    var tag = Str(b["tag"]);
                    if (CheckTag(tag, tags, $"{at}.tag") is { } tagProblem)
                    {
                        problem = tagProblem;
                        return null;
                    }
                    tags.Add(tag!);
                    var strategy = b["strategy"] is JsonObject so ? Str(so["type"]) : Str(b["strategy"]) ?? "random";
                    if (strategy == null || !BalancerStrategies.Contains(strategy))
                    {
                        problem = $"{at}.strategy: {string.Join(", ", BalancerStrategies)}";
                        return null;
                    }
                    var selector = (b["selector"] as JsonArray)?.Select(Str).ToList();
                    if (selector is not { Count: > 0 and <= MaxSelector } || selector.Any(x => x == null))
                    {
                        problem = $"{at}.selector: 1 to {MaxSelector} outbound tags";
                        return null;
                    }
                    balancers.Add(new V2pnBalancer(tag!, selector!, strategy));
                }
            }

            // References are checked once every tag is known: a selector, a dialer or a detour may name a later outbound.
            var outboundTags = outbounds.Select(o => Str(o["tag"])!).ToHashSet(StringComparer.Ordinal);
            foreach (var balancer in balancers)
            {
                if (balancer.Selector.FirstOrDefault(x => x != Global.ProxyTag && !outboundTags.Contains(x)) is { } unknown)
                {
                    problem = $"balancer {balancer.Tag}: selector {unknown} is not an outbound of core.json or proxy";
                    return null;
                }
            }
            for (var i = 0; i < outbounds.Count; i++)
            {
                if (CheckOutbound(core, outbounds[i], outboundTags, $"outbounds[{i}]") is { } outboundProblem)
                {
                    problem = outboundProblem;
                    return null;
                }
            }

            var dialer = V2pnRoutingPreset.Text(root["dialer"]);
            if (dialer != null && !outboundTags.Contains(dialer))
            {
                problem = "dialer: the tag of one of core.json's outbounds";
                return null;
            }

            var probeUrl = DefaultProbeUrl;
            var probeInterval = DefaultProbeInterval;
            if (root["observatory"] is { } observatoryNode)
            {
                if (core != Xray || observatoryNode is not JsonObject observatory)
                {
                    problem = "observatory: an object, Xray only";
                    return null;
                }
                probeUrl = V2pnRoutingPreset.Text(observatory["probeUrl"]) ?? probeUrl;
                probeInterval = V2pnRoutingPreset.Text(observatory["probeInterval"]) ?? probeInterval;
                if (Utils.TryUri(probeUrl)?.Scheme != Uri.UriSchemeHttps || !IsDuration(probeInterval, 10, 3600))
                {
                    problem = "observatory: probeUrl is https, probeInterval from 10s to 1h";
                    return null;
                }
            }

            var rules = new List<V2pnRouteRule>();
            if (root["rules"] is { } rulesNode)
            {
                if (rulesNode is not JsonArray list || list.Count > MaxRules)
                {
                    problem = $"rules: a list of at most {MaxRules}";
                    return null;
                }
                // Rules lead to the provider's own outbounds only: what goes direct or is blocked is routing.json's.
                var targets = tags.Append(Global.ProxyTag).ToList();
                for (var i = 0; i < list.Count; i++)
                {
                    var rule = V2pnRoutingPreset.ParseRule(list[i], i, targets, allowGeo: true, out problem);
                    if (rule == null)
                    {
                        return null;
                    }
                    if (rule.Domains.Concat(rule.Ips).FirstOrDefault(V2pnRouteEntries.IsPrivate) is { } local)
                    {
                        problem = $"rules[{i}]: {local} stays on this computer; it never goes to a provider";
                        return null;
                    }
                    rules.Add(rule);
                }
            }

            var allowTop = new List<string>();
            if (root["allowTop"] is { } allowNode)
            {
                var allowed = AllowableTop[core];
                if (allowNode is not JsonArray list || list.Select(Str).Any(k => k == null || !allowed.Contains(k)))
                {
                    problem = allowed.Count == 0
                        ? "allowTop: Xray has no other keys a subscription may keep"
                        : $"allowTop: only {string.Join(", ", allowed)}";
                    return null;
                }
                allowTop.AddRange(list.Select(Str).Distinct()!);
            }

            if (outbounds.Count == 0 && rules.Count == 0 && allowTop.Count == 0)
            {
                problem = "nothing to add: no outbounds, rules or allowTop";
                return null;
            }
            problem = string.Empty;
            return new V2pnCoreFragment(core, outbounds, dialer, balancers, probeUrl, probeInterval, rules, allowTop);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            problem = "not valid JSON: " + ex.Message;
            return null;
        }
    }

    private static string? CheckTag(string? tag, HashSet<string> taken, string at)
    {
        if (tag == null || !TagPattern.IsMatch(tag))
        {
            return $"{at}: lowercase letters, digits and dashes, up to 24";
        }
        if (tag is Global.ProxyTag or Global.DirectTag or Global.BlockTag || taken.Contains(tag))
        {
            return $"{at}: {tag} is V2pn's own or used twice";
        }
        return null;
    }

    /// <summary>Why an outbound of core.json may not run, or null when it may.</summary>
    private static string? CheckOutbound(string core, JsonObject outbound, HashSet<string> tags, string at)
    {
        var tag = Str(outbound["tag"]);
        var removed = new List<string>();
        if (V2pnCoreConfigFilter.FilterOutbound(core == Xray ? ECoreType.Xray : ECoreType.sing_box, outbound.ToJsonString(), removed) == null
            || removed.Count > 0)
        {
            return $"{at}: the subscription filter takes out {string.Join(", ", removed.DefaultIfEmpty("all of it"))}";
        }
        string? Ref(string? target, string what) =>
            target == null ? null
            : target == tag ? $"{at}.{what}: an outbound cannot lead to itself"
            : tags.Contains(target) ? null
            : $"{at}.{what}: {target} is not an outbound of core.json";

        if (core == Xray)
        {
            var protocol = Str(outbound["protocol"]);
            if (protocol == null || !XrayProtocols.Contains(protocol))
            {
                return $"{at}.protocol: one of {string.Join(", ", XrayProtocols)}";
            }
            foreach (var key in new[] { "proxySettings", "sendThrough" })
            {
                if (outbound.ContainsKey(key))
                {
                    return $"{at}.{key}: not taken; chain outbounds with streamSettings.sockopt.dialerProxy";
                }
            }
            return Ref(Str(outbound["streamSettings"]?["sockopt"]?["dialerProxy"]), "streamSettings.sockopt.dialerProxy");
        }

        var type = Str(outbound["type"]);
        if (type == null || !SingboxTypes.Contains(type))
        {
            return $"{at}.type: one of {string.Join(", ", SingboxTypes)}";
        }
        foreach (var key in new[] { "domain_resolver", "netns" })
        {
            if (outbound.ContainsKey(key))
            {
                return $"{at}.{key}: not taken; V2pn resolves the server's name itself";
            }
        }
        if (Ref(Str(outbound["detour"]), "detour") is { } detourProblem)
        {
            return detourProblem;
        }
        if (type == "urltest")
        {
            var members = (outbound["outbounds"] as JsonArray)?.Select(Str).ToList();
            if (members is not { Count: > 0 and <= MaxSelector } || members.Any(m => m == null || m == tag || (m != Global.ProxyTag && !tags.Contains(m))))
            {
                return $"{at}.outbounds: 1 to {MaxSelector} outbounds of core.json or proxy";
            }
            var url = Str(outbound["url"]);
            if (url != null && Utils.TryUri(url)?.Scheme != Uri.UriSchemeHttps)
            {
                return $"{at}.url: https";
            }
            if (Str(outbound["interval"]) is { } interval && !IsDuration(interval, 10, 3600))
            {
                return $"{at}.interval: from 10s to 1h";
            }
        }
        return null;
    }

    private static bool IsDuration(string text, int minSeconds, int maxSeconds)
    {
        var match = Duration.Match(text);
        if (!match.Success)
        {
            return false;
        }
        var n = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var seconds = match.Groups[2].Value switch { "s" => n, "m" => n * 60, _ => n * 3600 };
        return seconds >= minSeconds && seconds <= maxSeconds;
    }

    private static string? Str(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
