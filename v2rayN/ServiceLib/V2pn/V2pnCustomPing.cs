namespace ServiceLib;

/// <summary>
/// V2pn: a ping for a whole config ("Custom"), which v2rayN never tested. The real ping runs the config itself on its
/// own core, with its local ports replaced by one SOCKS port of the test's, so the request goes the way the person's
/// traffic would (the provider's rules included). The TCP ping reaches the servers the config names and takes the
/// fastest. Xray and sing-box configs only; other cores' configs are left untested, as before.
/// </summary>
public static class V2pnCustomPing
{
    public static bool CanTest(ProfileItem node) =>
        node.ConfigType == EConfigType.Custom && node.CoreType is ECoreType.Xray or ECoreType.sing_box;

    /// <summary>The config's text, filtered when it came with a subscription; null when it cannot be read or may not run.</summary>
    public static async Task<string?> ReadAsync(ProfileItem node)
    {
        var path = File.Exists(node.Address) ? node.Address : Utils.GetConfigPath(node.Address);
        if (!CanTest(node) || !File.Exists(path))
        {
            return null;
        }
        var text = await File.ReadAllTextAsync(path);
        return V2pnCoreConfigFilter.FromSubscription(node)
            ? V2pnCoreConfigFilter.FilterWhole(node.CoreType, text, [])
            : text;
    }

    /// <summary>
    /// <paramref name="text"/> with its inbounds replaced by one SOCKS inbound on 127.0.0.1:<paramref name="port"/>
    /// under the first inbound's tag (rules that name it still apply), and without anything that listens or writes
    /// elsewhere (the API, logs to files); null when it is not a config of <paramref name="core"/>.
    /// </summary>
    public static string? TestConfig(string text, ECoreType core, int port)
    {
        if (JsonUtils.ParseJson(text) is not JsonObject config || config["outbounds"] is not JsonArray)
        {
            return null;
        }
        var first = (config["inbounds"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
        var tag = first?["tag"]?.ToString();
        if (core == ECoreType.Xray)
        {
            config["inbounds"] = new JsonArray(new JsonObject
            {
                ["tag"] = tag.IsNullOrEmpty() ? "socks" : tag,
                ["listen"] = Global.Loopback,
                ["port"] = port,
                ["protocol"] = "socks",
                ["settings"] = new JsonObject { ["auth"] = "noauth", ["udp"] = true },
                ["sniffing"] = first?["sniffing"]?.DeepClone() ?? new JsonObject { ["enabled"] = true, ["destOverride"] = new JsonArray("http", "tls") },
            });
            config.Remove("api");
            config["log"] = new JsonObject { ["loglevel"] = "warning" };
        }
        else
        {
            config["inbounds"] = new JsonArray(new JsonObject
            {
                ["type"] = "mixed",
                ["tag"] = tag.IsNullOrEmpty() ? "mixed-in" : tag,
                ["listen"] = Global.Loopback,
                ["listen_port"] = port,
            });
            if (config["experimental"] is JsonObject experimental)
            {
                experimental.Remove("clash_api");
                experimental.Remove("cache_file");
            }
            config["log"] = new JsonObject { ["level"] = "warn" };
            // The config's own TUN goes with its inbound; its auto-route settings would mean nothing without it.
            if (config["route"] is JsonObject route)
            {
                route.Remove("auto_detect_interface");
            }
        }
        return config.ToJsonString();
    }

    /// <summary>The servers the config reaches: host and port of every outbound that is not built in.</summary>
    public static List<(string Host, int Port)> Endpoints(string text, ECoreType core)
    {
        var result = new List<(string, int)>();
        if (JsonUtils.ParseJson(text) is not JsonObject config)
        {
            return result;
        }
        IEnumerable<JsonObject> outbounds = (config["outbounds"] as JsonArray ?? []).OfType<JsonObject>();
        if (core == ECoreType.sing_box)
        {
            outbounds = outbounds.Concat((config["endpoints"] as JsonArray ?? []).OfType<JsonObject>());
        }
        foreach (var outbound in outbounds)
        {
            var (host, port) = core == ECoreType.Xray ? XrayEndpoint(outbound) : SingboxEndpoint(outbound);
            if (host.IsNotEmpty() && port is > 0 and < 65536 && !V2pnProviderConfigs.IsMadeUp(host, port))
            {
                result.Add((host!, port));
            }
        }
        return result.Distinct().ToList();
    }

    private static (string? Host, int Port) XrayEndpoint(JsonObject outbound)
    {
        if (outbound["protocol"]?.ToString() is null or "freedom" or "blackhole" or "dns" or "loopback")
        {
            return (null, 0);
        }
        var settings = outbound["settings"] as JsonObject ?? [];
        var server = (settings["vnext"] as JsonArray)?.FirstOrDefault() as JsonObject
                     ?? (settings["servers"] as JsonArray)?.FirstOrDefault() as JsonObject
                     ?? (settings["peers"] as JsonArray)?.FirstOrDefault() as JsonObject
                     ?? settings;
        var host = server["address"]?.ToString();
        var port = int.TryParse(server["port"]?.ToString(), out var p) ? p : 0;
        // WireGuard names its peer as host:port.
        if (host == null && server["endpoint"]?.ToString() is { } endpoint && endpoint.LastIndexOf(':') is var colon and > 0)
        {
            host = endpoint[..colon].Trim('[', ']');
            port = int.TryParse(endpoint[(colon + 1)..], out p) ? p : 0;
        }
        return (host, port);
    }

    private static (string? Host, int Port) SingboxEndpoint(JsonObject outbound)
    {
        if (outbound["type"]?.ToString() is null or "direct" or "block" or "dns" or "selector" or "urltest")
        {
            return (null, 0);
        }
        var server = outbound;
        if (outbound["peers"] is JsonArray { Count: > 0 } peers && peers[0] is JsonObject peer)
        {
            return (peer["address"]?.ToString(), int.TryParse(peer["port"]?.ToString(), out var pp) ? pp : 0);
        }
        return (server["server"]?.ToString(), int.TryParse(server["server_port"]?.ToString(), out var p) ? p : 0);
    }
}
