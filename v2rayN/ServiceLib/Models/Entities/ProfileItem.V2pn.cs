namespace ServiceLib.Models.Entities;

public partial class ProfileItem
{
    /// <summary>
    /// V2pn: the server came with a subscription update, wherever it sits now. A whole core config from a subscription
    /// runs only through V2pnCoreConfigFilter, and that used to be told by the subscription id alone: moving such a
    /// server to "no group" emptied the id and let the provider's config run unfiltered. Set after every subscription
    /// import (V2pnSubProvenance) and never cleared by moving the server.
    /// </summary>
    public bool V2pnFromSub { get; set; }

    /// <summary>
    /// V2pn: for a server taken out of a provider's whole Xray config (V2pnProviderConfigs), the provider's own
    /// outbound for it, as sent. Xray runs it in place of the outbound V2pn builds from the server's fields, so what
    /// a link cannot hold still reaches the server; it passes V2pnCoreConfigFilter first. Null for every other server.
    /// </summary>
    public string? V2pnOutbound { get; set; }

    /// <summary>
    /// V2pn: the routing of the provider's config this server (or group) came in, in V2pn's terms
    /// (V2pnProviderConfigs.ProviderRules). Used only when the person chose the provider's rules for the subscription.
    /// </summary>
    public string? V2pnRules { get; set; }
}
