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
}
