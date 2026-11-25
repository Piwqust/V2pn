namespace ServiceLib.Handler.Data;

// V2pn: a panel's whole Xray configs, taken apart by V2pnProviderConfigs, go into the database as V2pn's own servers.
public static partial class ConfigDbHandler
{
    /// <summary>
    /// Adds the servers of <paramref name="unpacked"/> as servers that carry the provider's outbound and rules, each
    /// balancing config as a group of them, and the configs left whole as before.
    /// </summary>
    /// <returns>How many servers and groups were added.</returns>
    private static async Task<int> AddV2pnProviderConfigs(V2pnProviderConfigs.Unpacked unpacked, string subid, bool isSub, SubItem? subItem)
    {
        var count = 0;
        // In a subscription a group finds its servers by name among the subscription's own, so it stays right when an
        // update gives the servers new rows; it goes first, where panels put "AUTO".
        var inSub = subid.IsNotEmpty();
        if (inSub)
        {
            foreach (var group in unpacked.Groups)
            {
                if (await AddV2pnProviderGroup(group, subid, isSub, null) == 0)
                {
                    count++;
                }
            }
        }

        var byLink = new Dictionary<string, V2pnProviderConfigs.Server>(StringComparer.Ordinal);
        foreach (var server in unpacked.Servers)
        {
            byLink.TryAdd(server.Link, server);
        }
        var added = new ConcurrentBag<ProfileItem>();
        var servers = await AddBatchServersCommon(string.Join(Environment.NewLine, byLink.Keys), subid, isSub, (line, item) =>
        {
            if (byLink.TryGetValue(line, out var server))
            {
                item.V2pnOutbound = server.Outbound;
                item.V2pnRules = server.Rules;
                added.Add(item);
            }
            return true;
        });
        count += Math.Max(servers, 0);

        // Outside a subscription the group names its servers by their rows.
        if (!inSub)
        {
            foreach (var group in unpacked.Groups)
            {
                var members = added.Where(i => i.IndexId.IsNotEmpty() && group.Members.Contains(i.Remarks.Trim())).Select(i => i.IndexId).ToList();
                if (members.Count > 0 && await AddV2pnProviderGroup(group, subid, isSub, members) == 0)
                {
                    count++;
                }
            }
        }

        foreach (var config in unpacked.Rest)
        {
            var profiles = V2rayFmt.ResolveToCustom(config.ToJsonString(), subItem?.Remarks);
            if (profiles.Count > 0)
            {
                count += Math.Max(await AddBatchCustomServers(profiles, subid, isSub, subItem), 0);
            }
        }
        return count;
    }

    /// <param name="members">The rows of its servers, or null to find them by name in the subscription.</param>
    private static async Task<int> AddV2pnProviderGroup(V2pnProviderConfigs.Group group, string subid, bool isSub, List<string>? members)
    {
        var profile = new ProfileItem
        {
            CoreType = ECoreType.Xray,
            ConfigType = EConfigType.PolicyGroup,
            Remarks = group.Name,
            Subid = subid,
            IsSub = isSub,
            V2pnRules = group.Rules,
        };
        profile.SetProtocolExtra(new ProtocolExtraItem
        {
            GroupType = nameof(EConfigType.PolicyGroup),
            MultipleLoad = group.Load,
            SubChildItems = members == null ? subid : null,
            Filter = members == null ? V2pnProviderConfigs.MembersFilter(group.Members) : null,
            ChildItems = members == null ? null : Utils.List2String(members),
        });
        return await AddServerCommon(profile, true);
    }
}
