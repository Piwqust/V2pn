namespace ServiceLib;

/// <summary>
/// V2pn: the rules a subscription's provider put in its whole configs (V2pnProviderConfigs.ProviderRules), for the
/// person who chose them for that subscription over V2pn's routing (SubInfoItem.ProviderRules). They take the place
/// of the person's rule set; pinned programs, a partner's reviewed rules, the kill switch and the rest of V2pn stay.
/// Entries naming a geosite or geoip list V2pn's files do not have are left out: Xray refuses to start on them.
/// </summary>
public sealed record V2pnProviderRouting(string? DomainStrategy, List<RulesItem4Ray> Rules)
{
    /// <summary>The provider's routing for <paramref name="node"/>, or null when V2pn's own routing applies.</summary>
    public static V2pnProviderRouting? For(ProfileItem? node)
    {
        if (node?.V2pnRules.IsNullOrEmpty() != false || node.Subid.IsNullOrEmpty()
            || SubInfoManager.Instance.Get(node.Subid)?.ProviderRules != true)
        {
            return null;
        }
        return Parse(node.V2pnRules, V2pnGeoTags.Has);
    }

    /// <param name="hasList">Whether the core has a list: ("geosite" or "geoip", its name).</param>
    public static V2pnProviderRouting? Parse(string? json, Func<string, string, bool> hasList)
    {
        if (JsonUtils.ParseJson(json ?? string.Empty) is not JsonObject routing || routing["rules"] is not JsonArray array)
        {
            return null;
        }
        var rules = new List<RulesItem4Ray>();
        foreach (var node in array.OfType<JsonObject>())
        {
            var rule = JsonUtils.Deserialize<RulesItem4Ray>(node.ToJsonString());
            if (rule?.outboundTag is not (Global.ProxyTag or Global.DirectTag or Global.BlockTag))
            {
                continue;
            }
            rule.type = "field";
            rule.balancerTag = null;
            rule.inboundTag = null;
            rule.process = null;
            // An entry the core cannot load goes; a rule left with no entries of a list it had goes whole, or it
            // would match everything.
            if (rule.domain is { Count: > 0 } domains && (rule.domain = Known(domains, "geosite", hasList)) == null
                || rule.ip is { Count: > 0 } ips && (rule.ip = Known(ips, "geoip", hasList)) == null)
            {
                continue;
            }
            rules.Add(rule);
        }
        var strategy = routing["domainStrategy"]?.ToString();
        return rules.Count == 0 ? null : new V2pnProviderRouting(strategy is Global.AsIs or Global.IPIfNonMatch or Global.IPOnDemand ? strategy : null, rules);
    }

    private static List<string>? Known(List<string> entries, string kind, Func<string, string, bool> hasList)
    {
        var kept = entries.Where(e =>
        {
            if (e.StartsWith(kind + ":", StringComparison.OrdinalIgnoreCase))
            {
                // geosite:google@cn: the list is "google", "@cn" picks its entries.
                var name = e[(kind.Length + 1)..].Split('@')[0].TrimStart('!');
                return hasList(kind, name);
            }
            if (e.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = e.Split(':');
                return parts.Length >= 3 && File.Exists(Utils.GetBinPath(parts[1]));
            }
            return true;
        }).ToList();
        return kept.Count == 0 ? null : kept;
    }
}

/// <summary>V2pn: the names of the lists in the core's geosite.dat and geoip.dat, read once per file version.</summary>
public static class V2pnGeoTags
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, (DateTime Written, HashSet<string> Names)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the core's <paramref name="kind"/>.dat ("geosite" or "geoip") has the list <paramref name="name"/>.
    /// True when the file cannot be read: then the core decides, as it did before.</summary>
    public static bool Has(string kind, string name) =>
        Names(Utils.GetBinPath($"{kind}.dat")) is not { } names || names.Contains(name);

    public static HashSet<string>? Names(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            var written = File.GetLastWriteTimeUtc(path);
            lock (Gate)
            {
                if (Cache.TryGetValue(path, out var cached) && cached.Written == written)
                {
                    return cached.Names;
                }
            }
            var names = Read(File.ReadAllBytes(path));
            lock (Gate)
            {
                Cache[path] = (written, names);
            }
            return names;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnGeoTags), ex);
            return null;
        }
    }

    /// <summary>
    /// The country_code of every entry of a GeoSiteList / GeoIPList (protobuf: field 1 repeated entries, each with
    /// its name as its own field 1).
    /// </summary>
    public static HashSet<string> Read(ReadOnlySpan<byte> data)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pos = 0;
        while (pos < data.Length)
        {
            var key = Varint(data, ref pos);
            var wire = (int)(key & 7);
            if (wire != 2)
            {
                Skip(data, ref pos, wire);
                continue;
            }
            var length = (int)Varint(data, ref pos);
            var end = pos + length;
            if (key >> 3 == 1 && end <= data.Length)
            {
                var entry = data[pos..end];
                var p = 0;
                if (p < entry.Length && Varint(entry, ref p) == ((1 << 3) | 2))
                {
                    var nameLength = (int)Varint(entry, ref p);
                    if (p + nameLength <= entry.Length)
                    {
                        names.Add(Encoding.UTF8.GetString(entry.Slice(p, nameLength)));
                    }
                }
            }
            pos = end;
        }
        return names;
    }

    private static ulong Varint(ReadOnlySpan<byte> data, ref int pos)
    {
        ulong value = 0;
        for (var shift = 0; pos < data.Length && shift < 64; shift += 7)
        {
            var b = data[pos++];
            value |= (ulong)(b & 0x7f) << shift;
            if ((b & 0x80) == 0)
            {
                break;
            }
        }
        return value;
    }

    private static void Skip(ReadOnlySpan<byte> data, ref int pos, int wire)
    {
        switch (wire)
        {
            case 0:
                Varint(data, ref pos);
                break;
            case 1:
                pos += 8;
                break;
            case 5:
                pos += 4;
                break;
            default:
                pos = data.Length;
                break;
        }
    }
}
