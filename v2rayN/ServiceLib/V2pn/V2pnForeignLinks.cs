using YamlDotNet.RepresentationModel;

namespace ServiceLib;

/// <summary>
/// V2pn: servers kept by other clients (Clash proxies, sing-box outbounds, NekoRay beans) turned into
/// share links, the one format v2rayN's importer reads for every protocol. Going through links keeps a
/// single path into the database: the same parsing, checks and duplicates handling as a pasted link.
/// What a link cannot carry (h2, shadow-tls, http proxies, TUIC v4) is counted as skipped.
/// </summary>
public static class V2pnForeignLinks
{
    /// <summary>A server in neutral terms; every source fills it, <see cref="ToLink"/> writes it.</summary>
    public sealed class Node
    {
        public string Type = string.Empty;
        public string Name = string.Empty;
        public string Server = string.Empty;
        public int Port;
        public string? Uuid, Password, Username, Method, Flow, Encryption, AlterId, VmessCipher;
        public string Network = "tcp";
        public string? Path, Host, ServiceName, Mode, HeaderType;
        public bool Tls, Insecure;
        public string? Sni, Alpn, Fingerprint, PublicKey, ShortId;
        public string? Obfs, ObfsPassword, Ports, PinSha256, Congestion;
        public string? Plugin;
        public string? PrivateKey, PeerPublicKey, PreSharedKey, Reserved, LocalAddress;
        public int? Mtu;
    }

    #region Clash

    /// <summary>Links for the <c>proxies:</c> of a Clash / mihomo config; empty when it has none.</summary>
    public static List<string> FromClashYaml(string? yaml, out int skipped)
    {
        skipped = 0;
        var links = new List<string>();
        if (yaml.IsNullOrEmpty() || !yaml.Contains("proxies", StringComparison.Ordinal))
        {
            return links;
        }
        YamlSequenceNode? proxies;
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                return links;
            }
            proxies = Child(root, "proxies") as YamlSequenceNode;
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"V2pn: not a Clash config: {ex.Message}");
            return links;
        }
        foreach (var entry in proxies?.Children ?? [])
        {
            var link = entry is YamlMappingNode map ? ToLink(FromClashProxy(map)) : null;
            if (link == null)
            {
                skipped++;
                continue;
            }
            links.Add(link);
        }
        return links;
    }

    /// <summary>One Clash proxy (mihomo field names) in neutral terms; null for a kind V2pn cannot hold.</summary>
    public static Node? FromClashProxy(YamlMappingNode p)
    {
        var type = Str(p, "type")?.ToLowerInvariant();
        var node = new Node
        {
            Type = type switch
            {
                "ss" => "shadowsocks",
                "hy2" => "hysteria2",
                "socks5" => "socks",
                _ => type ?? string.Empty,
            },
            Name = Str(p, "name") ?? string.Empty,
            Server = Str(p, "server") ?? string.Empty,
            Port = Int(p, "port") ?? 0,
            Uuid = Str(p, "uuid"),
            Password = Str(p, "password"),
            Username = Str(p, "username"),
            Flow = Str(p, "flow"),
            Encryption = Str(p, "encryption"),
            AlterId = Str(p, "alterId"),
            VmessCipher = Str(p, "cipher"),
            Method = Str(p, "cipher"),
            Tls = Bool(p, "tls"),
            Insecure = Bool(p, "skip-cert-verify"),
            Sni = Str(p, "servername") ?? Str(p, "sni"),
            Alpn = Join(p, "alpn"),
            Fingerprint = Str(p, "client-fingerprint"),
            Network = Str(p, "network") ?? "tcp",
            Obfs = Str(p, "obfs"),
            ObfsPassword = Str(p, "obfs-password"),
            Ports = Str(p, "ports"),
            PinSha256 = Str(p, "fingerprint"),
            Congestion = Str(p, "congestion-controller"),
            PrivateKey = Str(p, "private-key"),
            PeerPublicKey = Str(p, "public-key"),
            PreSharedKey = Str(p, "pre-shared-key"),
            Reserved = Join(p, "reserved"),
            Mtu = Int(p, "mtu"),
        };

        if (Child(p, "reality-opts") is YamlMappingNode reality)
        {
            node.PublicKey = Str(reality, "public-key");
            node.ShortId = Str(reality, "short-id");
        }

        switch (node.Network)
        {
            case "ws":
                var ws = Child(p, "ws-opts") as YamlMappingNode;
                node.Path = ws == null ? null : Str(ws, "path");
                node.Host = ws != null && Child(ws, "headers") is YamlMappingNode headers ? Str(headers, "Host") ?? Str(headers, "host") : null;
                if (ws != null && Bool(ws, "v2ray-http-upgrade"))
                {
                    node.Network = "httpupgrade";
                }
                break;

            case "grpc":
                node.ServiceName = Child(p, "grpc-opts") is YamlMappingNode grpc ? Str(grpc, "grpc-service-name") : null;
                break;

            case "xhttp":
                if (Child(p, "xhttp-opts") is YamlMappingNode xhttp)
                {
                    node.Path = Str(xhttp, "path");
                    node.Host = Str(xhttp, "host");
                    node.Mode = Str(xhttp, "mode");
                }
                break;

            case "http":
                // Clash's "http" network is plain TCP dressed as HTTP, Xray's raw with an http header.
                node.Network = "tcp";
                node.HeaderType = "http";
                if (Child(p, "http-opts") is YamlMappingNode http)
                {
                    node.Path = First(http, "path");
                    node.Host = Child(http, "headers") is YamlMappingNode h ? First(h, "Host") ?? First(h, "host") : null;
                }
                break;
        }

        if (node.Type == "shadowsocks" && Str(p, "plugin") is { } plugin)
        {
            node.Plugin = ClashPlugin(plugin, Child(p, "plugin-opts") as YamlMappingNode);
            if (node.Plugin == null)
            {
                return null;
            }
        }

        if (node.Type == "wireguard")
        {
            // Newer mihomo configs keep the peer in a list; one peer is all a link can hold.
            if (Child(p, "peers") is YamlSequenceNode { Children.Count: > 0 } peers && peers.Children[0] is YamlMappingNode peer)
            {
                node.Server = Str(peer, "server") ?? node.Server;
                node.Port = Int(peer, "port") ?? node.Port;
                node.PeerPublicKey = Str(peer, "public-key") ?? node.PeerPublicKey;
                node.PreSharedKey = Str(peer, "pre-shared-key") ?? node.PreSharedKey;
                node.Reserved = Join(peer, "reserved") ?? node.Reserved;
            }
            var addresses = new[] { Cidr(Str(p, "ip"), 32), Cidr(Str(p, "ipv6"), 128) }.Where(a => a != null);
            node.LocalAddress = string.Join(",", addresses);
        }
        return node;
    }

    /// <summary>A Clash shadowsocks plugin as a SIP002 plugin string; null for plugins v2rayN cannot run.</summary>
    private static string? ClashPlugin(string plugin, YamlMappingNode? opts)
    {
        var mode = opts == null ? null : Str(opts, "mode");
        var host = opts == null ? null : Str(opts, "host");
        switch (plugin)
        {
            case "obfs" when mode == "http":
                return $"obfs-local;obfs=http;obfs-host={host}";

            case "v2ray-plugin" when mode is null or "websocket":
                var args = new List<string> { "v2ray-plugin", "mode=websocket" };
                if (host.IsNotEmpty())
                {
                    args.Add($"host={host}");
                }
                if (opts != null && Str(opts, "path") is { } path)
                {
                    args.Add($"path={path}");
                }
                if (opts != null && Bool(opts, "tls"))
                {
                    args.Add("tls");
                }
                return string.Join(";", args);

            default:
                return null;
        }
    }

    private static YamlNode? Child(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static string? Str(YamlMappingNode map, string key) =>
        Child(map, key) is YamlScalarNode { Value: { Length: > 0 } value } ? value : null;

    private static int? Int(YamlMappingNode map, string key) =>
        int.TryParse(Str(map, key), out var value) ? value : null;

    private static bool Bool(YamlMappingNode map, string key) =>
        Str(map, key) is { } value && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");

    /// <summary>A list or a single value as one comma-joined string.</summary>
    private static string? Join(YamlMappingNode map, string key) => Child(map, key) switch
    {
        YamlSequenceNode seq => seq.Children.OfType<YamlScalarNode>().Select(s => s.Value).Where(v => v.IsNotEmpty()) is var values && values.Any()
            ? string.Join(",", values)
            : null,
        YamlScalarNode { Value: { Length: > 0 } value } => value,
        _ => null,
    };

    private static string? First(YamlMappingNode map, string key) => Child(map, key) switch
    {
        YamlSequenceNode seq => seq.Children.OfType<YamlScalarNode>().Select(s => s.Value).FirstOrDefault(v => v.IsNotEmpty()),
        YamlScalarNode { Value: { Length: > 0 } value } => value,
        _ => null,
    };

    private static string? Cidr(string? address, int bits) =>
        address.IsNullOrEmpty() ? null : address.Contains('/') ? address : $"{address}/{bits}";

    #endregion Clash

    #region sing-box

    /// <summary>Links for the outbounds (and WireGuard endpoints) of a sing-box config or a bare outbound list.</summary>
    public static List<string> FromSingboxJson(string? json, out int skipped)
    {
        skipped = 0;
        var links = new List<string>();
        var root = JsonUtils.ParseJson(json ?? string.Empty);
        IEnumerable<JsonNode?> outbounds = root switch
        {
            JsonArray array => array,
            JsonObject obj when obj["outbounds"] is JsonArray list => list.Concat(obj["endpoints"] as JsonArray ?? []),
            JsonObject obj when obj["type"] != null => [obj],
            _ => [],
        };
        foreach (var outbound in outbounds.OfType<JsonObject>())
        {
            var type = outbound["type"]?.GetValue<string>();
            // Groups and the built-in outbounds are not servers.
            if (type is null or "direct" or "block" or "dns" or "selector" or "urltest")
            {
                continue;
            }
            var link = ToLink(FromSingboxOutbound(outbound));
            if (link == null)
            {
                skipped++;
                continue;
            }
            links.Add(link);
        }
        return links;
    }

    /// <summary>One sing-box outbound in neutral terms.</summary>
    public static Node? FromSingboxOutbound(JsonObject o)
    {
        var node = new Node
        {
            Type = JStr(o, "type") ?? string.Empty,
            Name = JStr(o, "tag") ?? string.Empty,
            Server = JStr(o, "server") ?? string.Empty,
            Port = JInt(o, "server_port") ?? 0,
            Uuid = JStr(o, "uuid"),
            Password = JStr(o, "password"),
            Username = JStr(o, "username"),
            Method = JStr(o, "method"),
            Flow = JStr(o, "flow"),
            AlterId = JStr(o, "alter_id"),
            VmessCipher = JStr(o, "security"),
            Congestion = JStr(o, "congestion_control"),
            Ports = JJoin(o, "server_ports")?.Replace(':', '-'),
            Mtu = JInt(o, "mtu"),
            PrivateKey = JStr(o, "private_key"),
            PeerPublicKey = JStr(o, "peer_public_key"),
            PreSharedKey = JStr(o, "pre_shared_key"),
            Reserved = JJoin(o, "reserved"),
            LocalAddress = JJoin(o, "local_address") ?? JJoin(o, "address"),
        };

        if (o["tls"] is JsonObject tls && JBool(tls, "enabled"))
        {
            node.Tls = true;
            node.Sni = JStr(tls, "server_name");
            node.Insecure = JBool(tls, "insecure");
            node.Alpn = JJoin(tls, "alpn");
            if (tls["utls"] is JsonObject utls && JBool(utls, "enabled"))
            {
                node.Fingerprint = JStr(utls, "fingerprint");
            }
            if (tls["reality"] is JsonObject reality && JBool(reality, "enabled"))
            {
                node.PublicKey = JStr(reality, "public_key");
                node.ShortId = JStr(reality, "short_id");
            }
        }

        if (o["transport"] is JsonObject transport)
        {
            node.Network = JStr(transport, "type") switch
            {
                "ws" => "ws",
                "grpc" => "grpc",
                "httpupgrade" => "httpupgrade",
                "http" => "h2",
                var other => other ?? "tcp",
            };
            node.Path = JStr(transport, "path");
            node.ServiceName = JStr(transport, "service_name");
            node.Host = JStr(transport, "host") ?? JJoin(transport, "host")
                        ?? (transport["headers"] is JsonObject headers ? JStr(headers, "Host") ?? JStr(headers, "host") : null);
        }

        if (o["obfs"] is JsonObject obfs)
        {
            node.Obfs = JStr(obfs, "type");
            node.ObfsPassword = JStr(obfs, "password");
        }

        if (node.Type == "shadowsocks" && JStr(o, "plugin") is { } plugin)
        {
            node.Plugin = plugin switch
            {
                "obfs-local" or "v2ray-plugin" => $"{plugin};{JStr(o, "plugin_opts")}".TrimEnd(';'),
                _ => null,
            };
            if (node.Plugin == null)
            {
                return null;
            }
        }

        // sing-box 1.11+ keeps WireGuard as an endpoint with its peers.
        if (node.Type == "wireguard" && o["peers"] is JsonArray { Count: > 0 } peers && peers[0] is JsonObject peer)
        {
            node.Server = JStr(peer, "address") ?? node.Server;
            node.Port = JInt(peer, "port") ?? node.Port;
            node.PeerPublicKey = JStr(peer, "public_key") ?? node.PeerPublicKey;
            node.PreSharedKey = JStr(peer, "pre_shared_key") ?? node.PreSharedKey;
            node.Reserved = JJoin(peer, "reserved") ?? node.Reserved;
        }
        return node;
    }

    private static string? JStr(JsonObject o, string key)
    {
        try
        {
            return o[key] is JsonValue value && value.ToString() is { Length: > 0 } text ? text : null;
        }
        catch
        {
            return null;
        }
    }

    private static int? JInt(JsonObject o, string key) => int.TryParse(JStr(o, key), out var value) ? value : null;

    private static bool JBool(JsonObject o, string key) => JStr(o, key)?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

    private static string? JJoin(JsonObject o, string key) => o[key] switch
    {
        JsonArray array => array.Select(a => a?.ToString()).Where(v => v.IsNotEmpty()) is var values && values.Any() ? string.Join(",", values) : null,
        JsonValue => JStr(o, key),
        _ => null,
    };

    #endregion sing-box

    #region Links

    /// <summary>The share link for <paramref name="node"/>, in the form v2rayN's importer reads; null when no link fits.</summary>
    public static string? ToLink(Node? node)
    {
        if (node == null || node.Server.IsNullOrEmpty() || node.Port is <= 0 or > 65535)
        {
            return null;
        }
        try
        {
            return node.Type switch
            {
                "vless" when node.Uuid.IsNotEmpty() => Stream("vless", node.Uuid!, node, false,
                    ("encryption", node.Encryption ?? Global.None), ("flow", node.Flow)),
                "trojan" when node.Password.IsNotEmpty() => Stream("trojan", node.Password!, node, true,
                    ("allowInsecure", node.Insecure ? "1" : null)),
                "vmess" when node.Uuid.IsNotEmpty() => Vmess(node),
                "shadowsocks" when node.Method.IsNotEmpty() && node.Password != null => Build("ss",
                    $"{Esc(node.Method!)}:{Esc(node.Password)}", node, ("plugin", node.Plugin)),
                "hysteria2" when node.Password.IsNotEmpty() => Build("hysteria2", Esc(node.Password!), node,
                    ("sni", node.Sni), ("alpn", node.Alpn), ("insecure", node.Insecure ? "1" : null),
                    ("obfs", node.Obfs), ("obfs-password", node.ObfsPassword), ("mport", node.Ports),
                    ("pinSHA256", node.PinSha256)),
                "tuic" when node.Uuid.IsNotEmpty() && node.Password.IsNotEmpty() => Build("tuic",
                    $"{Esc(node.Uuid!)}:{Esc(node.Password!)}", node,
                    ("sni", node.Sni), ("alpn", node.Alpn), ("congestion_control", node.Congestion),
                    ("allow_insecure", node.Insecure ? "1" : null)),
                "anytls" when node.Password.IsNotEmpty() => Build("anytls", Esc(node.Password!), node,
                    ("security", "tls"), ("sni", node.Sni), ("fp", node.Fingerprint), ("alpn", node.Alpn),
                    ("insecure", node.Insecure ? "1" : null)),
                "socks" => Build("socks", node.Username.IsNotEmpty()
                    ? Utils.Base64Encode($"{node.Username}:{node.Password}", removePadding: true) : null, node),
                "wireguard" when node.PrivateKey.IsNotEmpty() && node.PeerPublicKey.IsNotEmpty() => Build("wireguard",
                    Esc(node.PrivateKey!), node,
                    ("publickey", node.PeerPublicKey), ("presharedkey", node.PreSharedKey), ("reserved", node.Reserved),
                    ("address", node.LocalAddress), ("mtu", node.Mtu?.ToString())),
                _ => null,
            };
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"V2pn: no link for {node.Type} {node.Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>vless:// and trojan://: the Xray share format with its transport and security.</summary>
    private static string? Stream(string scheme, string user, Node node, bool alwaysTls, params (string Key, string? Value)[] extra)
    {
        if (!TransportQuery(node, out var transport))
        {
            return null;
        }
        var security = node.PublicKey.IsNotEmpty() ? "reality" : node.Tls || alwaysTls ? "tls" : Global.None;
        var query = new List<(string, string?)>(extra)
        {
            ("security", security),
            ("sni", node.Sni),
            ("fp", node.Fingerprint),
            ("pbk", node.PublicKey),
            ("sid", node.ShortId),
            ("alpn", security == "tls" ? node.Alpn : null),
        };
        query.AddRange(transport);
        return Build(scheme, Esc(user), node, query.ToArray());
    }

    /// <summary>The type/host/path/serviceName part; false for a transport v2rayN has no place for (h2, quic).</summary>
    private static bool TransportQuery(Node node, out List<(string, string?)> query)
    {
        query = [];
        switch (node.Network)
        {
            case "tcp" or "raw" or "":
                query.Add(("type", "tcp"));
                query.Add(("headerType", node.HeaderType ?? Global.None));
                if (node.HeaderType == "http")
                {
                    query.Add(("host", node.Host));
                    query.Add(("path", node.Path));
                }
                return true;

            case "ws" or "httpupgrade":
                query.Add(("type", node.Network));
                query.Add(("host", node.Host));
                query.Add(("path", node.Path ?? "/"));
                return true;

            case "grpc":
                query.Add(("type", "grpc"));
                query.Add(("serviceName", node.ServiceName ?? string.Empty));
                query.Add(("mode", Global.GrpcGunMode));
                return true;

            case "xhttp":
                query.Add(("type", "xhttp"));
                query.Add(("host", node.Host));
                query.Add(("path", node.Path ?? "/"));
                query.Add(("mode", node.Mode));
                return true;

            default:
                return false;
        }
    }

    private static string? Vmess(Node node)
    {
        if (!TransportQuery(node, out _))
        {
            return null;
        }
        var net = node.Network is "raw" or "" ? "tcp" : node.Network;
        // A JsonObject, not a dictionary: reflection-based serialization is off in the trimmed build.
        var json = new JsonObject
        {
            ["v"] = "2",
            ["ps"] = node.Name,
            ["add"] = node.Server,
            ["port"] = node.Port,
            ["id"] = node.Uuid,
            ["aid"] = int.TryParse(node.AlterId, out var aid) ? aid : 0,
            ["scy"] = node.VmessCipher ?? "auto",
            ["net"] = net,
            ["type"] = net switch
            {
                "tcp" => node.HeaderType ?? Global.None,
                "grpc" => Global.GrpcGunMode,
                "xhttp" => node.Mode ?? string.Empty,
                _ => Global.None,
            },
            ["host"] = node.Host ?? string.Empty,
            ["path"] = net == "grpc" ? node.ServiceName ?? string.Empty : node.Path ?? (net is "ws" or "httpupgrade" or "xhttp" ? "/" : string.Empty),
            ["tls"] = node.Tls ? "tls" : string.Empty,
            ["sni"] = node.Sni ?? string.Empty,
            ["alpn"] = node.Alpn ?? string.Empty,
            ["fp"] = node.Fingerprint ?? string.Empty,
            ["insecure"] = node.Insecure ? "1" : "0",
        };
        return "vmess://" + Utils.Base64Encode(json.ToJsonString());
    }

    private static string Build(string scheme, string? userInfo, Node node, params (string Key, string? Value)[] query)
    {
        var host = node.Server.Contains(':') && !node.Server.StartsWith('[') ? $"[{node.Server}]" : node.Server;
        var sb = new StringBuilder($"{scheme}://");
        if (userInfo.IsNotEmpty())
        {
            sb.Append(userInfo).Append('@');
        }
        sb.Append(host).Append(':').Append(node.Port);
        var pairs = query.Where(q => q.Value.IsNotEmpty()).Select(q => $"{q.Key}={Esc(q.Value!)}").ToList();
        if (pairs.Count > 0)
        {
            sb.Append('?').Append(string.Join("&", pairs));
        }
        if (node.Name.IsNotEmpty())
        {
            sb.Append('#').Append(Esc(node.Name));
        }
        return sb.ToString();
    }

    private static string Esc(string value) => Uri.EscapeDataString(value);

    #endregion Links
}
