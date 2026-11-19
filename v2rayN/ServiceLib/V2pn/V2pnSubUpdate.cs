namespace ServiceLib;

/// <summary>
/// V2pn: a subscription update must not cut the connection in use. v2rayN restarts the core after
/// every update, which drops every open connection even when the server in use did not change.
/// </summary>
public static class V2pnSubUpdate
{
    /// <summary>
    /// What the core is given for <paramref name="item"/>, without the id and name a subscription update
    /// renews. Null when that cannot tell: groups, chains and custom configs depend on other profiles or
    /// files, and a subscription with a front or landing proxy on other servers.
    /// </summary>
    public static async Task<string?> ConnectionKey(ProfileItem? item)
    {
        if (item == null || item.ConfigType is EConfigType.PolicyGroup or EConfigType.ProxyChain or EConfigType.Custom)
        {
            return null;
        }
        if (item.Subid.IsNotEmpty()
            && await ConfigDbQuery.GetSubItem(item.Subid) is { } sub
            && (sub.PrevProfile.IsNotEmpty() || sub.NextProfile.IsNotEmpty()))
        {
            return null;
        }
        var copy = JsonUtils.DeepCopy(item);
        if (copy == null)
        {
            return null;
        }
        copy.IndexId = string.Empty;
        copy.Remarks = string.Empty;
        return JsonUtils.Serialize(copy, false);
    }
}
