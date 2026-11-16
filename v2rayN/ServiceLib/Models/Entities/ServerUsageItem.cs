namespace ServiceLib.Models.Entities;

/// <summary>
/// V2pn: how one server served this user: time connected through it and how often it answered tests.
/// Collected only while server statistics are on, and never leaves the computer.
/// </summary>
public class ServerUsageItem
{
    [PrimaryKey]
    public string IndexId { get; set; } = string.Empty;

    /// <summary>Time the connection through this server was up and passing checks.</summary>
    public long ConnectedSeconds { get; set; }

    /// <summary>Separate connections through it: each switch to it, or reconnect after a break.</summary>
    public int Sessions { get; set; }

    /// <summary>Unix seconds of the last connection through it; 0 when never.</summary>
    public long LastConnected { get; set; }

    /// <summary>Ping tests and connection checks of this server, answered or not.</summary>
    public int Tests { get; set; }

    public int Answers { get; set; }

    /// <summary>Sum of the answered tests' delays, for the average.</summary>
    public long DelaySum { get; set; }

    public int BestDelay { get; set; }

    /// <summary>Share of tests answered, 0..1; null before the first test.</summary>
    [Ignore]
    public double? AnswerRate => Tests > 0 ? (double)Answers / Tests : null;

    [Ignore]
    public int AverageDelay => Answers > 0 ? (int)(DelaySum / Answers) : 0;

    public ServerUsageItem Clone() => (ServerUsageItem)MemberwiseClone();
}

/// <summary>V2pn: connected time per local day, for the statistics chart.</summary>
public class UsageDayItem
{
    /// <summary>The local date as yyyy-MM-dd.</summary>
    [PrimaryKey]
    public string Day { get; set; } = string.Empty;

    public long Seconds { get; set; }
}
