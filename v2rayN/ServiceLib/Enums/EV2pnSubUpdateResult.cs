namespace ServiceLib.Enums;

/// <summary>V2pn: how the last update of a subscription ended. Stored as a number; append only.</summary>
public enum EV2pnSubUpdateResult
{
    /// <summary>Not updated since V2pn started keeping this.</summary>
    None = 0,

    /// <summary>Servers came and were imported.</summary>
    Ok = 1,

    /// <summary>No answer from the provider's site, or an HTTP error, by every route tried.</summary>
    Unreachable = 2,

    /// <summary>The site answered with nothing.</summary>
    Empty = 3,

    /// <summary>The site answered, but not with servers V2pn can read.</summary>
    Invalid = 4,

    /// <summary>Something else went wrong (for example the subscription's request headers).</summary>
    Failed = 5,
}
