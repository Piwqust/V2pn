namespace ServiceLib;

/// <summary>
/// V2pn: switching to the Russian rule sources only counts when the geo files really arrived.
/// UpdateGeoFileAll never throws and always reports success, so the files' write times are compared
/// instead; on failure everything ApplyRegionalPreset touched is put back, so the next attempt downloads again.
/// </summary>
public static class V2pnRussianSources
{
    /// <summary>What ApplyRegionalPreset(Russia) and InitRouting change, taken before they run.</summary>
    public sealed record Snapshot(
        string? GeoSourceUrl,
        string? SrsSourceUrl,
        string? RouteRulesTemplateSourceUrl,
        SimpleDNSItem? SimpleDns,
        DNSItem? XrayDns,
        DNSItem? SingboxDns,
        string? ActiveRoutingId,
        IReadOnlyDictionary<string, DateTime?> Files);

    public static IEnumerable<string> GeoFiles() => [Utils.GetBinPath("geosite.dat"), Utils.GetBinPath("geoip.dat")];

    /// <summary>Last write time of each file, null for a file that does not exist.</summary>
    public static Dictionary<string, DateTime?> StampFiles(IEnumerable<string> paths) =>
        paths.Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(p => p, p => File.Exists(p) ? File.GetLastWriteTimeUtc(p) : (DateTime?)null, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when every file exists now and was written after <paramref name="before"/> was taken.</summary>
    public static bool FilesRefreshed(IReadOnlyDictionary<string, DateTime?> before)
    {
        if (before.Count == 0)
        {
            return false;
        }
        foreach (var (path, stamp) in before)
        {
            if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) == stamp)
            {
                return false;
            }
        }
        return true;
    }

    public static async Task<Snapshot> TakeAsync(Config config, IEnumerable<string>? files = null)
    {
        var active = (await AppManager.Instance.RoutingItems())?.FirstOrDefault(t => t.IsActive);
        return new Snapshot(
            config.ConstItem.GeoSourceUrl,
            config.ConstItem.SrsSourceUrl,
            config.ConstItem.RouteRulesTemplateSourceUrl,
            JsonUtils.DeepCopy(config.SimpleDNSItem),
            JsonUtils.DeepCopy(await AppManager.Instance.GetDNSItem(ECoreType.Xray)),
            JsonUtils.DeepCopy(await AppManager.Instance.GetDNSItem(ECoreType.sing_box)),
            active?.Id,
            StampFiles(files ?? GeoFiles()));
    }

    /// <summary>
    /// Puts back the sources, DNS and the active routing. Routing items the template added stay: a later
    /// successful attempt adds the same ones, and the rule set only matters once it is active.
    /// The caller saves the config.
    /// </summary>
    public static async Task RestoreAsync(Config config, Snapshot snapshot)
    {
        config.ConstItem.GeoSourceUrl = snapshot.GeoSourceUrl;
        config.ConstItem.SrsSourceUrl = snapshot.SrsSourceUrl;
        config.ConstItem.RouteRulesTemplateSourceUrl = snapshot.RouteRulesTemplateSourceUrl;
        if (snapshot.SimpleDns != null)
        {
            config.SimpleDNSItem = snapshot.SimpleDns;
        }
        if (snapshot.XrayDns != null)
        {
            await ConfigHandler.SaveDNSItems(config, snapshot.XrayDns);
        }
        if (snapshot.SingboxDns != null)
        {
            await ConfigHandler.SaveDNSItems(config, snapshot.SingboxDns);
        }
        // An external template makes its first rule set the default, and that set needs the new files.
        if (snapshot.ActiveRoutingId.IsNotEmpty() && await AppManager.Instance.GetRoutingItem(snapshot.ActiveRoutingId) is { } routing)
        {
            await ConfigHandler.SetDefaultRouting(config, routing);
        }
    }
}
