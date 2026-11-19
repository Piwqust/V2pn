namespace ServiceLib;

/// <summary>
/// What a partner's reviewed files add to the connection to its own servers: its transport (transport.json) and its
/// piece of core config (core.json). Only for the servers of the partner's subscription, and only while the person
/// keeps "Connection settings" of that subscription on.
/// </summary>
public sealed record V2pnPartnerTraffic(string PartnerId, string Name, V2pnTransport? Transport, V2pnCoreFragment? Core)
{
    /// <summary>The partner's core.json when it is written for <paramref name="core"/>.</summary>
    public V2pnCoreFragment? CoreFor(ECoreType core) => Core?.CoreType == core ? Core : null;
}

/// <summary>Builds the per-subscription map the core config generators read (<see cref="CoreConfigContext.V2pnPartnerSubs"/>).</summary>
public static class V2pnPartnerTraffics
{
    /// <summary>Raised after the person turned a subscription's connection settings on or off, with its id.</summary>
    public static event Action<string>? Changed;

    /// <summary>The traffic of a partner for one subscription, or null when it adds nothing or the person turned it off.</summary>
    public static V2pnPartnerTraffic? For(V2pnPartner? partner, SubInfoItem? info)
    {
        if (partner == null || info?.PartnerConnectionOff == true)
        {
            return null;
        }
        var features = partner.Features;
        return features.Transport == null && features.Core == null
            ? null
            : new V2pnPartnerTraffic(partner.Id, partner.Name, features.Transport, features.Core);
    }

    /// <summary>Every subscription of a partner with a transport or core config in use, by subscription id.</summary>
    public static async Task<IReadOnlyDictionary<string, V2pnPartnerTraffic>> SnapshotAsync()
    {
        var map = new Dictionary<string, V2pnPartnerTraffic>(StringComparer.Ordinal);
        try
        {
            await V2pnPartners.Instance.LoadAsync();
            if (V2pnPartners.Instance.All.Count == 0)
            {
                return map;
            }
            await SubInfoManager.Instance.LoadAsync();
            foreach (var sub in await ConfigDbQuery.SubItems() ?? [])
            {
                if (For(V2pnPartners.Instance.ForSubscription(sub), SubInfoManager.Instance.Get(sub.Id)) is { } traffic)
                {
                    map[sub.Id] = traffic;
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnPartnerTraffics), ex);
        }
        return map;
    }

    /// <summary>Turns a subscription's connection settings from its partner off (or on again) and says so.</summary>
    public static async Task SetOffAsync(string subId, bool off)
    {
        if (await SubInfoManager.Instance.SetPartnerConnectionOffAsync(subId, off))
        {
            Changed?.Invoke(subId);
        }
    }
}
