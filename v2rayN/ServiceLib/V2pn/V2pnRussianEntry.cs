namespace ServiceLib;

/// <summary>
/// Getting past a whitelist: on mobile networks that let only approved Russian addresses through, the only way out is a
/// server inside Russia that forwards to one abroad. V2pn cannot make such a server, but it can find one the provider
/// already gives, or join the person's own Russian server and a foreign one into a chain.
/// </summary>
public static class V2pnRussianEntry
{
    // How providers name servers entered in Russia: "🇷🇺 → 🇩🇪 Германия", "RU-DE", "Обход белых списков", "БС", "LTE".
    private static readonly Regex EntryWords = new(
        @"белы[ехй]\s*спис|\bбс\b|обход\s*бс|whitelist|white\s*list|\bwl\b|🇷🇺\s*(?:→|->|-|>|»|⇒)|\bru\s*(?:→|->|>|»|⇒)|росси[яи]\s*(?:→|->|>|»)|\bмоб(?:ильн)?|\blte\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The server is in Russia, or its name says it is entered from Russia.</summary>
    public static bool LooksLikeEntry(ProfileItem server, string? country) =>
        string.Equals(country, "RU", StringComparison.OrdinalIgnoreCase)
        || EntryWords.IsMatch(server.Remarks ?? string.Empty);

    /// <summary>
    /// A chain "Russian server, then the foreign one", saved as a server of its own (not in any subscription, so an
    /// update does not remove it). Returns its id, or null when it could not be saved.
    /// </summary>
    public static async Task<string?> CreateChainAsync(Config config, ProfileItem entry, ProfileItem exit, string remarks)
    {
        if (entry.IndexId.IsNullOrEmpty() || exit.IndexId.IsNullOrEmpty() || entry.IndexId == exit.IndexId)
        {
            return null;
        }
        var chain = new ProfileItem
        {
            ConfigType = EConfigType.ProxyChain,
            CoreType = ECoreType.Xray,
            Remarks = remarks,
            Address = string.Empty,
        };
        chain.SetProtocolExtra(new ProtocolExtraItem { ChildItems = Utils.List2String([entry.IndexId, exit.IndexId]) });
        return await ConfigDbHandler.AddServerCommon(chain) == 0 ? chain.IndexId : null;
    }
}
