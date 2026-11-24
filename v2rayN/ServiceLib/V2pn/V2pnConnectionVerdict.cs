namespace ServiceLib;

/// <summary>What the person chose: off, the traffic protected (system proxy, PAC or the tunnel), or only a local proxy.</summary>
public enum EV2pnProtection
{
    Off,
    Protected,
    LocalOnly,
}

/// <summary>How a verdict looks: its colour on the pill, the tray and the Mac menu bar icon.</summary>
public enum EV2pnVerdictKind
{
    Safe,
    Unsafe,
    Warn,
    Checking,
}

/// <summary>The one answer to "is V2pn working right now", for every place that shows it.</summary>
public enum EV2pnVerdict
{
    /// <summary>The connection fell and the kill switch holds the internet closed.</summary>
    KillSwitch,

    /// <summary>The core kept stopping and V2pn gave up restarting it.</summary>
    CoreStopped,

    /// <summary>The core did not start.</summary>
    CoreFailed,

    Off,
    Reconnecting,
    Connecting,
    Checking,

    /// <summary>The check passed; only the local proxy port is on.</summary>
    LocalOnly,

    VpnConnected,
    ProxyConnected,

    /// <summary>On, and the last check of the connection did not pass (or has not run): the check's own words say why.</summary>
    CheckProblem,
}

/// <summary>Everything the verdict depends on, read at one moment.</summary>
public sealed record V2pnConnectionSnapshot(
    bool HasServer,
    bool EnableTun,
    ESysProxyType SystemProxy,
    EV2pnCoreState Core,
    EV2pnFailure Failure,
    bool KillSwitchHolding,
    AvailabilityState Check);

/// <summary>
/// V2pn: the verdict on the connection as one function of one snapshot. It was worked out inside the main window, so the
/// window, the tray, the mini window and the Mac menus could only agree by all asking the window, and none of it could
/// be tested. The window, the tray and the menus now draw what this returns.
/// </summary>
public static class V2pnConnectionVerdict
{
    /// <summary>What the person chose, from the mode and the way of the proxy. Without a server nothing runs.</summary>
    public static EV2pnProtection Protection(V2pnConnectionSnapshot s)
    {
        if (!s.HasServer)
        {
            return EV2pnProtection.Off;
        }
        if (s.EnableTun)
        {
            return EV2pnProtection.Protected;
        }
        return s.SystemProxy switch
        {
            ESysProxyType.ForcedChange or ESysProxyType.Pac => EV2pnProtection.Protected,
            ESysProxyType.Unchanged => EV2pnProtection.LocalOnly,
            _ => EV2pnProtection.Off,
        };
    }

    /// <summary>The verdict, first match wins: a fault, off, the core coming up, the check, then connected.</summary>
    public static (EV2pnVerdictKind Kind, EV2pnVerdict Verdict) Of(V2pnConnectionSnapshot s)
    {
        var protection = Protection(s);
        var enabled = protection != EV2pnProtection.Off;
        // A failure counts once V2pn gave up (or the person turned it off after one); while it restarts, it does not.
        var fault = !enabled || s.Core == EV2pnCoreState.Failed ? s.Failure : EV2pnFailure.None;
        if (fault != EV2pnFailure.None && s.KillSwitchHolding)
        {
            return (EV2pnVerdictKind.Unsafe, EV2pnVerdict.KillSwitch);
        }
        if (fault == EV2pnFailure.KeepsStopping)
        {
            return (EV2pnVerdictKind.Warn, EV2pnVerdict.CoreStopped);
        }
        if (fault != EV2pnFailure.None)
        {
            return (EV2pnVerdictKind.Warn, EV2pnVerdict.CoreFailed);
        }
        if (!enabled)
        {
            return (EV2pnVerdictKind.Unsafe, EV2pnVerdict.Off);
        }
        if (s.Core == EV2pnCoreState.Restarting)
        {
            return (EV2pnVerdictKind.Checking, EV2pnVerdict.Reconnecting);
        }
        if (s.Core is EV2pnCoreState.Starting or EV2pnCoreState.Stopped)
        {
            return (EV2pnVerdictKind.Checking, EV2pnVerdict.Connecting);
        }
        return s.Check switch
        {
            AvailabilityState.Checking => (EV2pnVerdictKind.Checking, EV2pnVerdict.Checking),
            AvailabilityState.Available when protection == EV2pnProtection.LocalOnly => (EV2pnVerdictKind.Safe, EV2pnVerdict.LocalOnly),
            AvailabilityState.Available => (EV2pnVerdictKind.Safe, s.EnableTun ? EV2pnVerdict.VpnConnected : EV2pnVerdict.ProxyConnected),
            _ => (EV2pnVerdictKind.Warn, EV2pnVerdict.CheckProblem),
        };
    }

    /// <summary>Whether a fault is what the verdict shows: the pill then opens the log and offers the fixes.</summary>
    public static bool Faulted(V2pnConnectionSnapshot s) =>
        Of(s).Verdict is EV2pnVerdict.KillSwitch or EV2pnVerdict.CoreStopped or EV2pnVerdict.CoreFailed;
}
