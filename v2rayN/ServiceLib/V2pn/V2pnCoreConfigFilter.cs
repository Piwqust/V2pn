using YamlDotNet.RepresentationModel;

namespace ServiceLib;

/// <summary>
/// V2pn: a whole core config that came with a subscription is the provider's text, and the cores treat their
/// config as trusted. Unfiltered, it can open a proxy or the core's API to the whole network, write files
/// (logs, TLS key logs, downloaded web panels), read local files, run a program (sing-box tor) or let the
/// server open connections back into this computer (Xray reverse, sing-box endpoints).
/// Before such a config runs, V2pn keeps only how to reach the provider's servers and how to route to them;
/// listeners stay on this computer. Configs the person imported from their own files are not touched.
/// </summary>
public static class V2pnCoreConfigFilter
{
    /// <summary>A node whose config came with a subscription update, not from the person's own file.</summary>
    public static bool FromSubscription(ProfileItem node) => node.IsSub && node.Subid.IsNotEmpty();

    /// <summary>
    /// A whole Xray or sing-box config from a subscription, ready to run, or null when V2pn does not run it:
    /// another core (V2pn cannot check its configs) or text that is not a config.
    /// </summary>
    public static string? FilterWhole(ECoreType? core, string text, List<string> removed)
    {
        if (core is not (ECoreType.Xray or ECoreType.sing_box) || JsonUtils.ParseJson(text) is not JsonObject config)
        {
            return null;
        }
        var found = core == ECoreType.Xray ? FilterXray(config) : FilterSingbox(config);
        removed.AddRange(found);
        return found.Count == 0 ? text : JsonUtils.Serialize(config);
    }

    /// <summary>
    /// A single outbound (a sing-box endpoint too) from a subscription, or null when it may not run at all.
    /// </summary>
    public static string? FilterOutbound(ECoreType? core, string text, List<string> removed)
    {
        if (JsonUtils.ParseJson(text) is not JsonObject outbound)
        {
            return null;
        }
        var found = new List<string>();
        if (core == ECoreType.sing_box)
        {
            var type = Str(outbound["type"]);
            if (type is "tor" or "tailscale")
            {
                removed.Add($"{type} {Str(outbound["tag"])}".TrimEnd());
                return null;
            }
            RemoveKeys(outbound, SingboxFileKey, found, Name(outbound, "type"));
        }
        else
        {
            RemoveKeys(outbound, XrayOutboundKey, found, Name(outbound, "protocol"));
        }
        removed.AddRange(found);
        return found.Count == 0 ? text : JsonUtils.Serialize(outbound);
    }

    #region Xray

    private static readonly HashSet<string> XrayTop =
        ["remarks", "log", "dns", "routing", "policy", "inbounds", "outbounds", "observatory", "burstObservatory", "fakedns"];

    private static readonly HashSet<string> XrayInbounds = ["socks", "http", "mixed", "dokodemo-door", "tunnel"];

    // Files read or written by an outbound, and VLESS reverse, which makes this computer a bridge the server
    // opens connections through.
    private static bool XrayOutboundKey(string key) => key is "certificateFile" or "keyFile" or "masterKeyLog" or "reverse";

    /// <summary>Filters an Xray config in place; returns what was taken out.</summary>
    public static List<string> FilterXray(JsonObject config)
    {
        var removed = new List<string>();
        var apiTag = Str((config["api"] as JsonObject)?["tag"]);

        foreach (var key in config.Select(p => p.Key).ToList())
        {
            if (!XrayTop.Contains(key))
            {
                config.Remove(key);
                removed.Add(key);
            }
        }

        if (config["log"] is JsonObject log)
        {
            foreach (var key in log.Select(p => p.Key).Where(k => k is not ("loglevel" or "dnsLog" or "maskAddress")).ToList())
            {
                log.Remove(key);
                removed.Add($"log.{key}");
            }
        }
        else if (config.Remove("log"))
        {
            removed.Add("log");
        }

        if (config["inbounds"] is JsonArray inbounds)
        {
            for (var i = inbounds.Count - 1; i >= 0; i--)
            {
                if (inbounds[i] is not JsonObject inbound)
                {
                    inbounds.RemoveAt(i);
                    continue;
                }
                var name = Name(inbound, "protocol", "inbound");
                if (!XrayInbounds.Contains(Str(inbound["protocol"]) ?? string.Empty)
                    || (apiTag != null && Str(inbound["tag"]) == apiTag)
                    || !KeepLocal(inbound, removed, name))
                {
                    inbounds.RemoveAt(i);
                    removed.Add(name);
                    continue;
                }
                if (inbound.Remove("streamSettings"))
                {
                    removed.Add($"{name}: streamSettings");
                }
            }
        }
        else if (config.Remove("inbounds"))
        {
            removed.Add("inbounds");
        }

        if (config["outbounds"] is JsonArray outbounds)
        {
            foreach (var outbound in outbounds.OfType<JsonObject>())
            {
                RemoveKeys(outbound, XrayOutboundKey, removed, Name(outbound, "protocol"));
            }
        }

        // Rules that led to the API go with it.
        if (apiTag != null && config["routing"] is JsonObject routing && routing["rules"] is JsonArray rules)
        {
            for (var i = rules.Count - 1; i >= 0; i--)
            {
                if (Str((rules[i] as JsonObject)?["outboundTag"]) == apiTag)
                {
                    rules.RemoveAt(i);
                }
            }
        }
        return removed;
    }

    #endregion Xray

    #region sing-box

    private static readonly HashSet<string> SingboxTop = ["log", "dns", "inbounds", "outbounds", "endpoints", "route"];

    private static readonly HashSet<string> SingboxInbounds = ["mixed", "socks", "http", "direct"];

    // Files an outbound, endpoint or DNS server reads (certificates, keys, ECH configs) and the tor outbound's
    // program and data folder. Rules are not swept: their process_path matches programs, it reads nothing.
    private static bool SingboxFileKey(string key) => key.EndsWith("_path", StringComparison.Ordinal) || key is "data_directory" or "state_directory";

    /// <summary>Filters a sing-box config in place; returns what was taken out.</summary>
    public static List<string> FilterSingbox(JsonObject config)
    {
        var removed = new List<string>();

        foreach (var key in config.Select(p => p.Key).ToList())
        {
            if (!SingboxTop.Contains(key))
            {
                config.Remove(key);
                removed.Add(key);
            }
        }

        if (config["log"] is JsonObject log)
        {
            foreach (var key in log.Select(p => p.Key).Where(k => k is not ("level" or "timestamp" or "disabled")).ToList())
            {
                log.Remove(key);
                removed.Add($"log.{key}");
            }
        }
        else if (config.Remove("log"))
        {
            removed.Add("log");
        }

        if (config["inbounds"] is JsonArray inbounds)
        {
            for (var i = inbounds.Count - 1; i >= 0; i--)
            {
                if (inbounds[i] is not JsonObject inbound)
                {
                    inbounds.RemoveAt(i);
                    continue;
                }
                var name = Name(inbound, "type", "inbound");
                if (!SingboxInbounds.Contains(Str(inbound["type"]) ?? string.Empty) || !KeepLocal(inbound, removed, name))
                {
                    inbounds.RemoveAt(i);
                    removed.Add(name);
                    continue;
                }
                // V2pn sets the system proxy itself; an inbound's own TLS reads certificate files.
                foreach (var key in new[] { "set_system_proxy", "tls" })
                {
                    if (inbound.Remove(key))
                    {
                        removed.Add($"{name}: {key}");
                    }
                }
            }
        }
        else if (config.Remove("inbounds"))
        {
            removed.Add("inbounds");
        }

        if (config["outbounds"] is JsonArray outbounds)
        {
            for (var i = outbounds.Count - 1; i >= 0; i--)
            {
                if (outbounds[i] is not JsonObject outbound)
                {
                    continue;
                }
                // tor runs a program from a path in the config.
                if (Str(outbound["type"]) == "tor")
                {
                    outbounds.RemoveAt(i);
                    removed.Add(Name(outbound, "type"));
                    continue;
                }
                RemoveKeys(outbound, SingboxFileKey, removed, Name(outbound, "type"));
            }
        }

        // An endpoint also takes connections from its peers; WireGuard stays, but those connections are refused.
        // Tailscale would join this computer to the provider's network.
        var endpointTags = new JsonArray();
        if (config["endpoints"] is JsonArray endpoints)
        {
            for (var i = endpoints.Count - 1; i >= 0; i--)
            {
                if (endpoints[i] is not JsonObject endpoint || Str(endpoint["type"]) != "wireguard")
                {
                    removed.Add(endpoints[i] is JsonObject other ? Name(other, "type") : "endpoint");
                    endpoints.RemoveAt(i);
                    continue;
                }
                RemoveKeys(endpoint, SingboxFileKey, removed, Name(endpoint, "type"));
                if (Str(endpoint["tag"]) is { } tag)
                {
                    endpointTags.Add(JsonValue.Create(tag));
                }
            }
        }

        // Local rule sets read files; rules that used them go too, or they would match everything.
        var droppedSets = new HashSet<string>();
        var route = config["route"] as JsonObject;
        if (route?["rule_set"] is JsonArray ruleSets)
        {
            for (var i = ruleSets.Count - 1; i >= 0; i--)
            {
                if (ruleSets[i] is JsonObject set && Str(set["type"]) is "inline" or "remote")
                {
                    RemoveKeys(set, SingboxFileKey, removed, $"rule_set {Str(set["tag"])}");
                    continue;
                }
                var tag = Str((ruleSets[i] as JsonObject)?["tag"]);
                if (tag != null)
                {
                    droppedSets.Add(tag);
                }
                ruleSets.RemoveAt(i);
                removed.Add($"rule_set {tag}".TrimEnd());
            }
        }
        foreach (var legacy in new[] { "geoip", "geosite" })
        {
            if (route?[legacy] is JsonObject db && db.Remove("path"))
            {
                removed.Add($"route.{legacy}.path");
            }
        }
        if (route?["rules"] is JsonArray routeRules)
        {
            DropRulesUsing(routeRules, droppedSets);
        }
        if (endpointTags.Count > 0)
        {
            route ??= (JsonObject)(config["route"] = new JsonObject());
            if (route["rules"] is not JsonArray rules)
            {
                route["rules"] = rules = new JsonArray();
            }
            rules.Insert(0, new JsonObject { ["inbound"] = endpointTags, ["action"] = "reject" });
        }

        if (config["dns"] is JsonObject dns)
        {
            if (dns["servers"] is JsonArray servers)
            {
                foreach (var server in servers.OfType<JsonObject>())
                {
                    RemoveKeys(server, SingboxFileKey, removed, Name(server, "type", "dns"));
                    // A hosts server reads the files it names; without them it reads the system's hosts file.
                    if (Str(server["type"]) == "hosts" && server.Remove("path"))
                    {
                        removed.Add($"{Name(server, "type", "dns")}: path");
                    }
                }
            }
            if (dns["rules"] is JsonArray dnsRules)
            {
                DropRulesUsing(dnsRules, droppedSets);
            }
        }
        return removed;
    }

    private static void DropRulesUsing(JsonArray rules, HashSet<string> droppedSets)
    {
        if (droppedSets.Count == 0)
        {
            return;
        }
        for (var i = rules.Count - 1; i >= 0; i--)
        {
            if (UsesRuleSet(rules[i], droppedSets))
            {
                rules.RemoveAt(i);
            }
        }
    }

    private static bool UsesRuleSet(JsonNode? node, HashSet<string> sets) => node switch
    {
        JsonObject obj => obj.Any(p => (p.Key == "rule_set" && Mentions(p.Value, sets)) || (p.Key == "rules" && UsesRuleSet(p.Value, sets))),
        JsonArray arr => arr.Any(item => UsesRuleSet(item, sets)),
        _ => false,
    };

    private static bool Mentions(JsonNode? value, HashSet<string> sets) => value switch
    {
        JsonArray arr => arr.Any(item => Str(item) is { } s && sets.Contains(s)),
        _ => Str(value) is { } s && sets.Contains(s),
    };

    #endregion sing-box

    #region mihomo

    // Listeners beyond the one V2pn sets, the web panel (downloaded and served), the controller's other
    // doors, port forwards, the system clock, TUN and iptables (V2pn sets its own TUN when it is on).
    private static readonly string[] ClashRemoved =
    [
        "external-ui", "external-ui-url", "external-ui-name", "external-controller-tls", "external-controller-unix",
        "external-controller-pipe", "external-controller-cors", "external-doh-server", "tls", "listeners", "tunnels",
        "ntp", "port", "socks-port", "redir-port", "tproxy-port", "authentication", "skip-auth-prefixes", "iptables",
        "tun", "experimental", "script", "ebpf",
    ];

    /// <summary>
    /// Filters a Clash / mihomo config in place, before V2pn sets its own port, controller and TUN; returns
    /// what was taken out.
    /// </summary>
    public static List<string> FilterClash(YamlMappingNode config)
    {
        var removed = new List<string>();
        foreach (var key in ClashRemoved)
        {
            if (config.Children.Remove(new YamlScalarNode(key)))
            {
                removed.Add(key);
            }
        }

        if (Child(config, "dns") is YamlMappingNode dns && dns.Children.Remove(new YamlScalarNode("listen")))
        {
            removed.Add("dns.listen");
        }

        var droppedRules = FilterProviders(config, "rule-providers", removed);
        var droppedProxies = FilterProviders(config, "proxy-providers", removed);

        if (droppedRules.Count > 0)
        {
            if (Child(config, "rules") is YamlSequenceNode rules)
            {
                DropClashRules(rules, droppedRules);
            }
            if (Child(config, "sub-rules") is YamlMappingNode subRules)
            {
                foreach (var list in subRules.Children.Values.OfType<YamlSequenceNode>())
                {
                    DropClashRules(list, droppedRules);
                }
            }
        }
        if (droppedProxies.Count > 0 && Child(config, "proxy-groups") is YamlSequenceNode groups)
        {
            foreach (var group in groups.Children.OfType<YamlMappingNode>())
            {
                if (Child(group, "use") is YamlSequenceNode use)
                {
                    for (var i = use.Children.Count - 1; i >= 0; i--)
                    {
                        if (use.Children[i] is YamlScalarNode { Value: { } name } && droppedProxies.Contains(name))
                        {
                            use.Children.RemoveAt(i);
                        }
                    }
                }
            }
        }
        return removed;
    }

    /// <summary>
    /// A provider of type file reads a local file: it goes. Other providers keep their downloads under
    /// mihomo's home folder: a path outside it is replaced. Returns the names taken out.
    /// </summary>
    private static HashSet<string> FilterProviders(YamlMappingNode config, string section, List<string> removed)
    {
        var dropped = new HashSet<string>();
        if (Child(config, section) is not YamlMappingNode providers)
        {
            return dropped;
        }
        foreach (var (keyNode, valueNode) in providers.Children.ToList())
        {
            var name = (keyNode as YamlScalarNode)?.Value ?? string.Empty;
            if (valueNode is not YamlMappingNode provider || (Child(provider, "type") as YamlScalarNode)?.Value == "file")
            {
                providers.Children.Remove(keyNode);
                dropped.Add(name);
                removed.Add($"{section}.{name}");
                continue;
            }
            if (Child(provider, "path") is YamlScalarNode { Value: { } path } && !SafeRelative(path))
            {
                var ext = Path.GetExtension(path);
                var safeExt = Regex.IsMatch(ext, "^\\.[A-Za-z0-9]{1,5}$") ? ext : string.Empty;
                provider.Children[new YamlScalarNode("path")] = new YamlScalarNode($"./providers/v2pn-{section}-{Hash(name)}{safeExt}");
                removed.Add($"{section}.{name}.path");
            }
        }
        return dropped;
    }

    private static void DropClashRules(YamlSequenceNode rules, HashSet<string> dropped)
    {
        for (var i = rules.Children.Count - 1; i >= 0; i--)
        {
            if (rules.Children[i] is YamlScalarNode { Value: { } rule }
                && dropped.Any(name => Regex.IsMatch(rule, $@"RULE-SET\s*,\s*{Regex.Escape(name)}\s*(,|\)|$)", RegexOptions.IgnoreCase)))
            {
                rules.Children.RemoveAt(i);
            }
        }
    }

    private static bool SafeRelative(string path)
    {
        var p = path.Replace('\\', '/');
        return p.Length > 0 && !Path.IsPathRooted(path) && !p.StartsWith('/') && !p.StartsWith('~') && !p.Contains(':')
            && !p.Split('/').Any(segment => segment == "..");
    }

    private static YamlNode? Child(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12].ToLowerInvariant();

    #endregion mihomo

    #region Shared

    /// <summary>
    /// Keeps a listener on this computer: a loopback address stays, a missing or other address becomes
    /// 127.0.0.1 (the cores listen on every interface without one). A socket path or a host name: false.
    /// </summary>
    private static bool KeepLocal(JsonObject listener, List<string> removed, string name)
    {
        var listen = listener["listen"];
        if (listen is null)
        {
            listener["listen"] = Global.Loopback;
            removed.Add($"{name}: listen on every interface");
            return true;
        }
        var text = Str(listen);
        if (text is null)
        {
            return false;
        }
        if (text.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (IPAddress.TryParse(text.Trim('[', ']'), out var ip))
        {
            if (!IPAddress.IsLoopback(ip))
            {
                listener["listen"] = Global.Loopback;
                removed.Add($"{name}: listen {text}");
            }
            return true;
        }
        return false;
    }

    private static void RemoveKeys(JsonNode? node, Func<string, bool> match, List<string> removed, string name)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (match(key))
                    {
                        obj.Remove(key);
                        removed.Add($"{name}: {key}");
                    }
                    else
                    {
                        RemoveKeys(obj[key], match, removed, name);
                    }
                }
                break;

            case JsonArray arr:
                foreach (var item in arr)
                {
                    RemoveKeys(item, match, removed, name);
                }
                break;
        }
    }

    private static string Name(JsonObject item, string kindKey, string? fallback = null)
    {
        var tag = Str(item["tag"]);
        var kind = Str(item[kindKey]) ?? fallback ?? "?";
        return tag != null ? $"{kind} {tag}" : kind;
    }

    private static string? Str(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    #endregion Shared
}
