namespace ServiceLib;

/// <summary>
/// V2pn: panels (Remnawave's xray-json template, used by Happ and INCY providers) answer V2pn with whole Xray
/// configs instead of links: one config per server with its own DNS, rules and local ports, one more for "AUTO"
/// that balances all of them, and configs whose server is not real but a line of text for the person
/// ("Проверяйте отклик…"). Imported as they are, every one was a "Custom" profile V2pn could not ping, put on the
/// map, tune or route by the person's own rules.
/// <para>
/// Here a whole config is taken apart. A config with one server becomes that server (a link the usual importer
/// reads), and the provider's own outbound stays with it, so what a link cannot hold (ws headers, xhttp extras,
/// finalmask) still reaches the core. A config that balances servers which also came one by one becomes a V2pn
/// group of them, without the provider's way out past the VPN (<c>fallbackTag: direct</c>). A config whose servers
/// are all made up is a notice. Anything else stays a whole config, as before. The provider's rules are kept, in
/// V2pn's terms, for the person who chooses them over V2pn's.
/// </para>
/// </summary>
public static class V2pnProviderConfigs
{
    /// <summary>A server taken out of a whole config.</summary>
    /// <param name="Link">The share link the importer reads.</param>
    /// <param name="Name">The config's own name.</param>
    /// <param name="Outbound">The provider's outbound, as sent.</param>
    /// <param name="Rules">The config's rules in V2pn's terms (<see cref="ProviderRules"/>), or null.</param>
    public sealed record Server(string Link, string Name, string Outbound, string? Rules);

    /// <summary>A balancing config as the names of the servers it balances.</summary>
    public sealed record Group(string Name, EMultipleLoad Load, List<string> Members, string? Rules);

    /// <summary>What a set of whole configs holds.</summary>
    /// <param name="Rest">Configs left whole: chains, transports no link holds, balancers over servers that did not come alone.</param>
    public sealed record Unpacked(List<Server> Servers, List<Group> Groups, List<string> Notices, List<JsonObject> Rest);

    private static readonly HashSet<string> BuiltIn = ["freedom", "blackhole", "dns", "loopback"];

    /// <summary>
    /// The servers, groups and notices of <paramref name="text"/>, or null when it is not whole Xray configs
    /// (a JSON array of them or one config, each with inbounds and outbounds).
    /// </summary>
    public static Unpacked? Unpack(string? text)
    {
        var configs = WholeConfigs(text);
        if (configs == null)
        {
            return null;
        }

        var result = new Unpacked([], [], [], []);
        var balancing = new List<JsonObject>();
        // Each server's outbound in a canonical form, to find it again inside a balancing config.
        var known = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var config in configs)
        {
            var name = Str(config["remarks"])?.Trim() ?? string.Empty;
            var proxies = Proxies(config);
            if (proxies.Count == 0)
            {
                continue;
            }
            if (proxies.All(IsMadeUp))
            {
                if (name.Length > 0)
                {
                    result.Notices.Add(name);
                }
                continue;
            }
            if (proxies.Count > 1)
            {
                balancing.Add(config);
                continue;
            }
            var outbound = proxies[0];
            if (Chained(outbound) || ToLink(outbound, name) is not { } link)
            {
                result.Rest.Add(config);
                continue;
            }
            result.Servers.Add(new Server(link, name, outbound.ToJsonString(), ProviderRules(config)));
            known.TryAdd(Canon(outbound), name);
        }

        foreach (var config in balancing)
        {
            var name = Str(config["remarks"])?.Trim() ?? string.Empty;
            var proxies = Proxies(config).Where(p => !IsMadeUp(p)).ToList();
            var members = proxies.Select(p => Chained(p) ? null : known.GetValueOrDefault(Canon(p))).ToList();
            if (name.Length == 0 || members.Count == 0 || members.Any(m => m == null))
            {
                result.Rest.Add(config);
                continue;
            }
            result.Groups.Add(new Group(name, Load(config), members.Distinct().ToList()!, ProviderRules(config)));
        }
        return result;
    }

    /// <summary>The names of the made-up servers in a subscription's answer, whole configs or links; empty when none.</summary>
    public static List<string> Notices(string? text)
    {
        if (Unpack(text) is { } unpacked)
        {
            return unpacked.Notices;
        }
        var notices = new List<string>();
        foreach (var line in LinkLines(text))
        {
            if (FmtHandler.ResolveConfig(line, out _) is { } item && IsMadeUp(item))
            {
                if (item.Remarks.Trim() is { Length: > 0 } remarks)
                {
                    notices.Add(remarks);
                }
            }
        }
        return notices;
    }

    /// <summary>
    /// A server no one can reach, which panels send to show a line of text: no host (0.0.0.0, "::", a word with no
    /// dot that is not an address), port 0 or 1, or the all-zero id Remnawave puts in when it refuses a device.
    /// </summary>
    public static bool IsMadeUp(string? host, int port, string? id = null)
    {
        host = host?.Trim().Trim('[', ']') ?? string.Empty;
        if (port is <= 1 or > 65535 || id == "00000000-0000-0000-0000-000000000000")
        {
            return true;
        }
        if (host.Length == 0 || host is "0.0.0.0" or "::")
        {
            return true;
        }
        return !IPAddress.TryParse(host, out _) && !host.Contains('.')
            && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMadeUp(ProfileItem item) =>
        !item.ConfigType.IsComplexType() && item.ConfigType != EConfigType.Outbound
        && IsMadeUp(item.Address, item.Port, item.ConfigType is EConfigType.VLESS or EConfigType.VMess ? item.Password : null);

    /// <summary>A group filter (a regex on the servers' names) that takes exactly the servers named.</summary>
    public static string MembersFilter(IEnumerable<string> names) =>
        $@"^\s*(?:{string.Join("|", names.Select(n => Regex.Escape(n.Trim())))})\s*$";

    #region Configs

    private static List<JsonObject>? WholeConfigs(string? text)
    {
        var trimmed = text?.TrimStart();
        if (trimmed.IsNullOrEmpty() || trimmed[0] is not ('[' or '{'))
        {
            return null;
        }
        List<JsonObject> configs = JsonUtils.ParseJson(trimmed) switch
        {
            JsonArray array => array.OfType<JsonObject>().ToList(),
            JsonObject one => [one],
            _ => [],
        };
        configs = configs.Where(c => c["outbounds"] is JsonArray && c["inbounds"] is JsonArray).ToList();
        return configs.Count == 0 ? null : configs;
    }

    private static List<JsonObject> Proxies(JsonObject config) =>
        (config["outbounds"] as JsonArray ?? [])
        .OfType<JsonObject>()
        .Where(o => Str(o["protocol"]) is { } protocol && !BuiltIn.Contains(protocol))
        .ToList();

    /// <summary>The outbound goes out through another one, which a single server cannot hold.</summary>
    private static bool Chained(JsonObject outbound) =>
        Str((outbound["proxySettings"] as JsonObject)?["tag"]) != null
        || Str(outbound["streamSettings"]?["sockopt"]?["dialerProxy"]) != null;

    private static bool IsMadeUp(JsonObject outbound)
    {
        var (host, port, id) = Endpoint(outbound);
        return IsMadeUp(host, port, id);
    }

    /// <summary>Host, port and user id of an outbound, in the old (vnext/servers) and the flat settings form.</summary>
    private static (string? Host, int Port, string? Id) Endpoint(JsonObject outbound)
    {
        var settings = outbound["settings"] as JsonObject ?? [];
        var server = (settings["vnext"] as JsonArray)?.FirstOrDefault() as JsonObject
                     ?? (settings["servers"] as JsonArray)?.FirstOrDefault() as JsonObject
                     ?? settings;
        var user = (server["users"] as JsonArray)?.FirstOrDefault() as JsonObject;
        return (Str(server["address"]), Int(server["port"]) ?? 0, Str(user?["id"]) ?? Str(server["id"]));
    }

    private static EMultipleLoad Load(JsonObject config)
    {
        var balancer = (config["routing"]?["balancers"] as JsonArray)?.FirstOrDefault() as JsonObject;
        return Str(balancer?["strategy"]?["type"]) switch
        {
            "leastLoad" => EMultipleLoad.LeastLoad,
            "leastPing" => EMultipleLoad.LeastPing,
            "roundRobin" => EMultipleLoad.RoundRobin,
            "random" => EMultipleLoad.Random,
            // No balancer or one V2pn does not know: the fastest server, as V2pn's own groups do.
            _ => EMultipleLoad.LeastPing,
        };
    }

    /// <summary>The JSON with its object keys sorted and the tag left out: equal for the same server in two configs.</summary>
    internal static string Canon(JsonObject outbound)
    {
        var copy = (JsonObject)outbound.DeepClone();
        copy.Remove("tag");
        return Sorted(copy)?.ToJsonString() ?? string.Empty;

        static JsonNode? Sorted(JsonNode? node) => node switch
        {
            JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value)))),
            JsonArray array => new JsonArray(array.Select(Sorted).ToArray()),
            _ => node?.DeepClone(),
        };
    }

    #endregion Configs

    #region Rules

    /// <summary>
    /// The config's routing in V2pn's terms, as {"domainStrategy", "rules"}: each rule sends to proxy, direct or block
    /// (a balancer and every proxy outbound of the config are "proxy"); rules about the config's own inbounds, users
    /// or other outbounds are left out. Null when nothing is left.
    /// </summary>
    public static string? ProviderRules(JsonObject config)
    {
        if (config["routing"] is not JsonObject routing || routing["rules"] is not JsonArray rules)
        {
            return null;
        }
        var outbounds = (config["outbounds"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        string? Target(string? tag)
        {
            var outbound = outbounds.FirstOrDefault(o => Str(o["tag"]) == tag);
            return Str(outbound?["protocol"]) switch
            {
                null => null,
                "freedom" => Global.DirectTag,
                "blackhole" => Global.BlockTag,
                "dns" or "loopback" => null,
                _ => Global.ProxyTag,
            };
        }

        var kept = new JsonArray();
        foreach (var rule in rules.OfType<JsonObject>())
        {
            if (rule["inboundTag"] != null || rule["user"] != null || rule["attrs"] != null)
            {
                continue;
            }
            var target = Str(rule["balancerTag"]) != null ? Global.ProxyTag : Target(Str(rule["outboundTag"]));
            if (target == null)
            {
                continue;
            }
            var copy = new JsonObject { ["type"] = "field" };
            var matches = false;
            foreach (var key in new[] { "domain", "ip", "port", "network", "protocol", "source", "sourcePort" })
            {
                if (rule[key] is { } value && (value is JsonArray { Count: > 0 } || Str(value) != null))
                {
                    copy[key] = value.DeepClone();
                    matches = true;
                }
            }
            if (!matches)
            {
                continue;
            }
            copy["outboundTag"] = target;
            kept.Add(copy);
        }
        if (kept.Count == 0)
        {
            return null;
        }
        var result = new JsonObject { ["rules"] = kept };
        if (Str(routing["domainStrategy"]) is { } strategy)
        {
            result["domainStrategy"] = strategy;
        }
        return result.ToJsonString();
    }

    #endregion Rules

    #region Links

    /// <summary>The share link for an Xray outbound, named <paramref name="name"/>; null when no link holds it.</summary>
    public static string? ToLink(JsonObject outbound, string name) => V2pnForeignLinks.ToLink(FromXrayOutbound(outbound, name));

    /// <summary>An Xray outbound in neutral terms; null for a protocol V2pn has no link for.</summary>
    public static V2pnForeignLinks.Node? FromXrayOutbound(JsonObject o, string name)
    {
        var protocol = Str(o["protocol"]);
        var settings = o["settings"] as JsonObject ?? [];
        var server = (settings["vnext"] as JsonArray)?.FirstOrDefault() as JsonObject
                     ?? (settings["servers"] as JsonArray)?.FirstOrDefault() as JsonObject
                     ?? settings;
        var user = (server["users"] as JsonArray)?.FirstOrDefault() as JsonObject ?? server;
        var stream = o["streamSettings"] as JsonObject ?? [];

        var node = new V2pnForeignLinks.Node
        {
            Name = name,
            Server = Str(server["address"]) ?? string.Empty,
            Port = Int(server["port"]) ?? 0,
        };
        switch (protocol)
        {
            case "vless":
                node.Type = "vless";
                node.Uuid = Str(user["id"]);
                node.Flow = Str(user["flow"]);
                node.Encryption = Str(user["encryption"]);
                break;

            case "vmess":
                node.Type = "vmess";
                node.Uuid = Str(user["id"]);
                node.AlterId = Str(user["alterId"]);
                node.VmessCipher = Str(user["security"]);
                break;

            case "trojan":
                node.Type = "trojan";
                node.Password = Str(server["password"]);
                break;

            case "shadowsocks":
                node.Type = "shadowsocks";
                node.Method = Str(server["method"]);
                node.Password = Str(server["password"]);
                break;

            case "hysteria" when (Int(settings["version"]) ?? Int(stream["hysteriaSettings"]?["version"])) == 2:
                node.Type = "hysteria2";
                node.Password = Str(stream["hysteriaSettings"]?["auth"]);
                break;

            case "socks":
                node.Type = "socks";
                node.Username = Str(user["user"]);
                node.Password = Str(user["pass"]);
                break;

            default:
                return null;
        }

        var security = Str(stream["security"]) ?? Global.None;
        if (security == "tls" && stream["tlsSettings"] is JsonObject tls)
        {
            node.Tls = true;
            node.Sni = Str(tls["serverName"]);
            node.Alpn = Join(tls["alpn"]);
            node.Fingerprint = Str(tls["fingerprint"]);
            node.Insecure = Str(tls["allowInsecure"]) == "true";
        }
        else if (security == "reality" && stream["realitySettings"] is JsonObject reality)
        {
            node.Tls = true;
            node.Sni = Str(reality["serverName"]);
            node.Fingerprint = Str(reality["fingerprint"]);
            node.PublicKey = Str(reality["publicKey"]) ?? Str(reality["password"]);
            node.ShortId = Str(reality["shortId"]);
            if (node.PublicKey == null)
            {
                return null;
            }
        }
        else if (security is not ("none" or ""))
        {
            return null;
        }

        if (node.Type == "hysteria2")
        {
            // Hysteria rides its own transport; the link holds only its TLS.
            return node;
        }

        node.Network = Str(stream["network"]) ?? "tcp";
        switch (node.Network)
        {
            case "ws" when stream["wsSettings"] is JsonObject ws:
                node.Path = Str(ws["path"]);
                node.Host = Str(ws["host"]) ?? Str(ws["headers"]?["Host"]) ?? Str(ws["headers"]?["host"]);
                break;

            case "httpupgrade" when stream["httpupgradeSettings"] is JsonObject upgrade:
                node.Path = Str(upgrade["path"]);
                node.Host = Str(upgrade["host"]);
                break;

            case "grpc" when stream["grpcSettings"] is JsonObject grpc:
                node.ServiceName = Str(grpc["serviceName"]);
                break;

            case "xhttp" or "splithttp" when (stream["xhttpSettings"] ?? stream["splithttpSettings"]) is JsonObject xhttp:
                node.Network = "xhttp";
                node.Path = Str(xhttp["path"]);
                node.Host = Str(xhttp["host"]);
                node.Mode = Str(xhttp["mode"]);
                break;

            case "tcp" or "raw":
                var header = (stream["rawSettings"] ?? stream["tcpSettings"])?["header"] as JsonObject;
                node.HeaderType = Str(header?["type"]);
                if (node.HeaderType == "http" && header?["request"] is JsonObject request)
                {
                    node.Path = Join(request["path"]);
                    node.Host = Join(request["headers"]?["Host"]);
                }
                break;
        }
        return node;
    }

    #endregion Links

    private static IEnumerable<string> LinkLines(string? text)
    {
        if (text.IsNullOrEmpty())
        {
            return [];
        }
        var plain = Utils.IsBase64String(text) ? Utils.Base64Decode(text) : text;
        return plain.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Contains("://"));
    }

    private static string? Str(JsonNode? node)
    {
        try
        {
            return node is JsonValue value && value.ToString() is { Length: > 0 } text ? text : null;
        }
        catch
        {
            return null;
        }
    }

    private static int? Int(JsonNode? node) => int.TryParse(Str(node), out var value) ? value : null;

    private static string? Join(JsonNode? node) => node switch
    {
        JsonArray array => array.Select(Str).Where(v => v != null).ToList() is { Count: > 0 } values ? string.Join(",", values) : null,
        _ => Str(node),
    };
}
