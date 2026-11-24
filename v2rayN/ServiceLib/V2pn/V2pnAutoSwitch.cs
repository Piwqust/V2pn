namespace ServiceLib;

/// <summary>What to do after a check of the connection.</summary>
public enum EV2pnAutoAction
{
    None,

    /// <summary>One failed check: check once more in a moment before blaming the server.</summary>
    RecheckSoon,

    /// <summary>The server failed twice: find a working one in the same country and switch to it.</summary>
    Search,
}

/// <summary>
/// V2pn: when to switch to another server by itself (Settings → "Switch to a working server by itself", on by default).
/// A server that stops answering used to stay chosen until the person pressed "Find a working server". Now one failed
/// check is checked again; a second failure looks for a working server in the same country and switches to it with a
/// note. Another country is never chosen without asking, and switches are at least <see cref="Cooldown"/> apart, so a
/// network that is down for everyone does not make V2pn hop from server to server.
/// </summary>
public sealed class V2pnAutoSwitch
{
    /// <summary>How often a connection that works is checked again, so a server that dies mid-session is noticed.</summary>
    public static readonly TimeSpan Recheck = TimeSpan.FromMinutes(5);

    /// <summary>The wait before the second check that confirms a failure.</summary>
    public static readonly TimeSpan Confirm = TimeSpan.FromSeconds(15);

    /// <summary>The least time between two switches made by V2pn itself.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    private string? _server;
    private int _strikes;
    private DateTime? _lastSwitch;

    /// <summary>Whether the person left it on (it is on until turned off).</summary>
    public static bool Enabled(Config config) => config.UiItem.V2pnAutoSwitch ?? true;

    /// <summary>The next step after a check of <paramref name="serverId"/> that gave <paramref name="result"/>.</summary>
    public EV2pnAutoAction OnResult(bool enabled, bool connected, string? serverId, AvailabilityCheckResult result, DateTime now)
    {
        if (serverId != _server)
        {
            _server = serverId;
            _strikes = 0;
        }
        if (!enabled || !connected || serverId.IsNullOrEmpty())
        {
            return EV2pnAutoAction.None;
        }
        if (result.IsAvailable)
        {
            _strikes = 0;
            return EV2pnAutoAction.None;
        }
        if (!result.CanSearchForReplacement)
        {
            return EV2pnAutoAction.None;
        }
        _strikes++;
        if (_strikes == 1)
        {
            return EV2pnAutoAction.RecheckSoon;
        }
        return _lastSwitch is { } last && now - last < Cooldown ? EV2pnAutoAction.None : EV2pnAutoAction.Search;
    }

    /// <summary>V2pn switched (or found nothing): the next search waits for the cooldown.</summary>
    public void Searched(DateTime now)
    {
        _lastSwitch = now;
        _strikes = 0;
    }
}
