using System.Globalization;

namespace ServiceLib;

/// <summary>Splitting the TLS handshake into pieces, so a filter that reads it whole misses the site's name.</summary>
public sealed record V2pnFragment(string Packets, string Length, string Interval);

/// <summary>Random bytes sent before a UDP connection's first packet (Xray finalmask noise).</summary>
public sealed record V2pnNoise(string Rand, string Delay);

/// <summary>
/// transport.json: how V2pn connects to a provider's own servers, reviewed by hand. Used only for the servers of
/// that provider's subscription, in place of the person's own fragment setting for them; the rest of the person's
/// connections never see it. Xray takes fragment and noise, sing-box only turns its TLS fragment on (its pieces
/// cannot be sized); mux is left out on purpose: Xray's and sing-box's are different protocols and need the server's
/// support, which V2pn cannot check.
/// </summary>
public sealed record V2pnTransport(V2pnFragment? Fragment, IReadOnlyList<V2pnNoise> Noise)
{
    public const int MaxNoise = 8;

    /// <summary>The fragment as Xray's finalmask writes it; tlshello breaks REALITY, so its servers get packets 1-3.</summary>
    public JsonObject? XrayFragmentMask(bool reality)
    {
        if (Fragment == null)
        {
            return null;
        }
        var packets = reality && Fragment.Packets == "tlshello" ? "1-3" : Fragment.Packets;
        return new JsonObject
        {
            ["type"] = "fragment",
            ["settings"] = new JsonObject
            {
                ["packets"] = packets,
                ["lengths"] = new JsonArray(JsonValue.Create(Fragment.Length)),
                ["delays"] = new JsonArray(JsonValue.Create(Fragment.Interval)),
                ["maxSplit"] = 0,
            },
        };
    }

    public JsonObject? XrayNoiseMask()
    {
        if (Noise.Count == 0)
        {
            return null;
        }
        var list = new JsonArray();
        foreach (var noise in Noise)
        {
            list.Add(new JsonObject { ["rand"] = noise.Rand, ["delay"] = noise.Delay });
        }
        return new JsonObject { ["type"] = "noise", ["settings"] = new JsonObject { ["noise"] = list } };
    }

    /// <summary>
    /// Puts the provider's fragment on an Xray outbound to one of its servers that speaks TLS or REALITY and dials
    /// directly (a server reached through another one is fragmented by that one), and its noise on a UDP transport
    /// (mKCP, Hysteria). A mask the server's own link set stays.
    /// </summary>
    public void ApplyXray(JsonObject outbound)
    {
        if (outbound["streamSettings"] is not JsonObject stream)
        {
            return;
        }
        var finalmask = stream["finalmask"] as JsonObject;
        var security = (stream["security"] as JsonValue)?.ToString();
        var dialer = ((stream["sockopt"] as JsonObject)?["dialerProxy"] as JsonValue)?.ToString();
        if (XrayFragmentMask(security == Global.StreamSecurityReality) is { } fragment
            && security is { Length: > 0 } and not "none" && dialer.IsNullOrEmpty()
            && finalmask?["tcp"] is not JsonArray { Count: > 0 })
        {
            finalmask ??= new JsonObject();
            finalmask["tcp"] = new JsonArray(fragment);
        }
        if (XrayNoiseMask() is { } noise && finalmask?["udp"] is JsonArray udp)
        {
            udp.Add(noise);
        }
        if (finalmask is { Count: > 0 })
        {
            stream["finalmask"] = finalmask;
        }
    }

    /// <summary>The same for an outbound V2pn built itself.</summary>
    public void ApplyXray(Outbounds4Ray outbound)
    {
        if (outbound.streamSettings == null)
        {
            return;
        }
        var node = JsonUtils.SerializeToNode(outbound) as JsonObject;
        if (node == null)
        {
            return;
        }
        ApplyXray(node);
        if ((node["streamSettings"] as JsonObject)?["finalmask"] is JsonObject finalmask)
        {
            outbound.streamSettings.finalmask = JsonUtils.ParseJson(finalmask.ToJsonString()) as JsonObject;
        }
    }

    /// <summary>The transport when <paramref name="json"/> is a valid transport.json; otherwise null and the first problem found.</summary>
    public static V2pnTransport? Parse(byte[] json, out string problem)
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
            V2pnFragment? fragment = null;
            if (root["fragment"] is { } fragmentNode)
            {
                if (fragmentNode is not JsonObject f)
                {
                    problem = "fragment: an object";
                    return null;
                }
                var packets = V2pnRoutingPreset.Text(f["packets"]) ?? "tlshello";
                if (packets != "tlshello" && Range(packets, 1, 10) == null)
                {
                    problem = "fragment.packets: tlshello or a range of packets from 1 to 10, like 1-3";
                    return null;
                }
                var length = Range(V2pnRoutingPreset.Text(f["length"]) ?? "100-200", 1, 1500);
                if (length == null)
                {
                    problem = "fragment.length: bytes from 1 to 1500, like 100-200";
                    return null;
                }
                var interval = Range(V2pnRoutingPreset.Text(f["interval"]) ?? "10-20", 0, 200);
                if (interval == null)
                {
                    problem = "fragment.interval: milliseconds from 0 to 200, like 10-20";
                    return null;
                }
                fragment = new V2pnFragment(packets, length, interval);
            }
            var noise = new List<V2pnNoise>();
            if (root["noise"] is { } noiseNode)
            {
                if (noiseNode is not JsonArray list || list.Count > MaxNoise)
                {
                    problem = $"noise: a list of at most {MaxNoise}";
                    return null;
                }
                for (var i = 0; i < list.Count; i++)
                {
                    var rand = Range(V2pnRoutingPreset.Text(list[i]?["rand"]), 1, 1024);
                    var delay = Range(V2pnRoutingPreset.Text(list[i]?["delay"]) ?? "10-16", 0, 200);
                    if (rand == null || delay == null)
                    {
                        problem = $"noise[{i}]: rand is bytes from 1 to 1024 (like 10-20), delay milliseconds from 0 to 200";
                        return null;
                    }
                    noise.Add(new V2pnNoise(rand, delay));
                }
            }
            if (fragment == null && noise.Count == 0)
            {
                problem = "neither fragment nor noise";
                return null;
            }
            problem = string.Empty;
            return new V2pnTransport(fragment, noise);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            problem = "not valid JSON: " + ex.Message;
            return null;
        }
    }

    /// <summary>"a-b" or "a" with min &lt;= a &lt;= b &lt;= max, written back as "a-b" or "a".</summary>
    private static string? Range(string? text, int min, int max)
    {
        var parts = text?.Split('-') ?? [];
        if (parts.Length is 0 or > 2)
        {
            return null;
        }
        var numbers = new List<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < min || n > max)
            {
                return null;
            }
            numbers.Add(n);
        }
        return numbers.Count == 2 && numbers[0] > numbers[1] ? null : string.Join('-', numbers);
    }
}
