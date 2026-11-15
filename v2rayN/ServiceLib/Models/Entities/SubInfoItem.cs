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

    // V2pn: what Happ, INCY and v2RayTun providers send (SubPanelInfo has the details). Replaced on every update.

    public string? AnnounceUrl { get; set; }

    public string? InfoText { get; set; }

    /// <summary>info, attention or ok (SubPanelHeaders.Tone*).</summary>
    public string? InfoTone { get; set; }

    public string? InfoButtonText { get; set; }

    public string? InfoButtonUrl { get; set; }

    /// <summary>Happ sub-expire: 1 on, -1 off, 0 not sent.</summary>
    public int ExpiryNotice { get; set; }

    public string? ExpiryText { get; set; }

    public string? ExpiryButtonText { get; set; }

    public string? ExpiryButtonUrl { get; set; }

    public bool Autoconnect { get; set; }

    /// <summary>lastused, lowestdelay or random (SubPanelHeaders.Connect*); null when not sent.</summary>
    public string? AutoconnectType { get; set; }

    /// <summary>ping, alphabet or without (SubPanelHeaders.Sort*); null when not sent.</summary>
    public string? SortType { get; set; }

    public bool PingOnOpen { get; set; }

    public string? PingType { get; set; }

    public string? CheckUrl { get; set; }

    public bool UpdateOnOpen { get; set; }

    /// <summary>Stored only: honoured in a later stage, for partners with the lock grant.</summary>
    public bool HideSettings { get; set; }

    /// <summary>Stored only, as <see cref="HideSettings"/>.</summary>
    public bool HideUrl { get; set; }

    // The person's own choices, which the provider's defaults never overwrite.

    /// <summary>true: the person decides about connecting at start, whatever the provider advises.</summary>
    public bool OwnAutoconnect { get; set; }

    /// <summary>The order the person chose for this subscription's servers; null follows the provider.</summary>
    public string? OwnSort { get; set; }

    /// <summary>true: the person turned off the reviewed transport and core config of this subscription's partner.</summary>
    public bool PartnerConnectionOff { get; set; }
}
