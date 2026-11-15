namespace ServiceLib.Models.Entities;

/// <summary>
/// V2pn: what a subscription's panel reports about itself in the response headers (name, traffic,
/// expiry, links). Kept apart from <see cref="SubItem"/> so v2rayN's table stays as upstream has it.
/// </summary>
public class SubInfoItem
{
    /// <summary>The <see cref="SubItem.Id"/> this belongs to.</summary>
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The name the panel gave on the last successful update. While the subscription's own name equals
    /// it, the name belongs to the panel and follows it; once the user renames, it stays theirs.
    /// </summary>
    public string? PanelName { get; set; }

    /// <summary>Minutes V2pn last put into <see cref="SubItem.AutoUpdateInterval"/> from the panel; 0 when it never did.</summary>
    public int PanelInterval { get; set; }

    public bool HasUserInfo { get; set; }

    public long Upload { get; set; }

    public long Download { get; set; }

    /// <summary>Bytes; 0 means unlimited.</summary>
    public long Total { get; set; }

    /// <summary>Unix seconds; 0 means it never expires.</summary>
    public long Expire { get; set; }

    /// <summary>Hours between updates the panel asks for; 0 when it did not say.</summary>
    public int UpdateIntervalHours { get; set; }

    public string? SupportUrl { get; set; }

    public string? WebPageUrl { get; set; }

    public string? Announce { get; set; }

    /// <summary>Unix seconds of the response these values came from.</summary>
    public long ReceivedTime { get; set; }

    /// <summary>The v2pn-partner folder the panel named; null when it named none.</summary>
    public string? PartnerUrl { get; set; }

    /// <summary>The v2pn-account-token for the partner's live-data API; never shown or logged.</summary>
    public string? AccountToken { get; set; }
}
