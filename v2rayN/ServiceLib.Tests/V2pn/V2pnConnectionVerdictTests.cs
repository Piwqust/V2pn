namespace ServiceLib.Tests.V2pn;

/// <summary>The one verdict on the connection that the window, the tray and the Mac menus all draw.</summary>
public class V2pnConnectionVerdictTests
{
    private static V2pnConnectionSnapshot S(
        bool server = true,
        bool tun = false,
        ESysProxyType proxy = ESysProxyType.ForcedChange,
        EV2pnCoreState core = EV2pnCoreState.Running,
        EV2pnFailure failure = EV2pnFailure.None,
        bool holding = false,
        AvailabilityState check = AvailabilityState.Available) =>
        new(server, tun, proxy, core, failure, holding, check);

    private static async Task Is(V2pnConnectionSnapshot s, EV2pnVerdictKind kind, EV2pnVerdict verdict)
    {
        var (k, v) = V2pnConnectionVerdict.Of(s);
        await v.Should().BeEqualTo(verdict);
        await k.Should().BeEqualTo(kind);
    }

    [Test]
    public async Task Connected_IsSafe_ByMode()
    {
        await Is(S(), EV2pnVerdictKind.Safe, EV2pnVerdict.ProxyConnected);
        await Is(S(proxy: ESysProxyType.Pac), EV2pnVerdictKind.Safe, EV2pnVerdict.ProxyConnected);
        await Is(S(tun: true, proxy: ESysProxyType.ForcedClear), EV2pnVerdictKind.Safe, EV2pnVerdict.VpnConnected);
        await Is(S(proxy: ESysProxyType.Unchanged), EV2pnVerdictKind.Safe, EV2pnVerdict.LocalOnly);
    }

    [Test]
    public async Task Off_OrNoServer_IsUnsafe()
    {
        await Is(S(proxy: ESysProxyType.ForcedClear), EV2pnVerdictKind.Unsafe, EV2pnVerdict.Off);
        await Is(S(server: false, tun: true), EV2pnVerdictKind.Unsafe, EV2pnVerdict.Off);
        await V2pnConnectionVerdict.Protection(S(server: false, tun: true)).Should().BeEqualTo(EV2pnProtection.Off);
    }

    [Test]
    public async Task ConnectedNeverShows_BeforeTheCheckPasses()
    {
        await Is(S(check: AvailabilityState.Unchecked), EV2pnVerdictKind.Warn, EV2pnVerdict.CheckProblem);
        await Is(S(check: AvailabilityState.Checking), EV2pnVerdictKind.Checking, EV2pnVerdict.Checking);
        await Is(S(check: AvailabilityState.NoResponse), EV2pnVerdictKind.Warn, EV2pnVerdict.CheckProblem);
        await Is(S(check: AvailabilityState.TimedOut, tun: true), EV2pnVerdictKind.Warn, EV2pnVerdict.CheckProblem);
    }

    [Test]
    public async Task TheCoreComingUp_IsConnectingOrReconnecting_WhateverTheLastCheckSaid()
    {
        await Is(S(core: EV2pnCoreState.Starting), EV2pnVerdictKind.Checking, EV2pnVerdict.Connecting);
        await Is(S(core: EV2pnCoreState.Stopped), EV2pnVerdictKind.Checking, EV2pnVerdict.Connecting);
        await Is(S(core: EV2pnCoreState.Restarting), EV2pnVerdictKind.Checking, EV2pnVerdict.Reconnecting);
        // A failure noted while it restarts is not shown until V2pn gives up.
        await Is(S(core: EV2pnCoreState.Restarting, failure: EV2pnFailure.KeepsStopping), EV2pnVerdictKind.Checking, EV2pnVerdict.Reconnecting);
    }

    [Test]
    public async Task AFailure_IsShown_OnceV2pnGaveUp_OrWhenOff()
    {
        await Is(S(core: EV2pnCoreState.Failed, failure: EV2pnFailure.KeepsStopping), EV2pnVerdictKind.Warn, EV2pnVerdict.CoreStopped);
        await Is(S(core: EV2pnCoreState.Failed, failure: EV2pnFailure.StartFailed), EV2pnVerdictKind.Warn, EV2pnVerdict.CoreFailed);
        await Is(S(proxy: ESysProxyType.ForcedClear, core: EV2pnCoreState.Stopped, failure: EV2pnFailure.StartFailed), EV2pnVerdictKind.Warn, EV2pnVerdict.CoreFailed);
        await V2pnConnectionVerdict.Faulted(S(core: EV2pnCoreState.Failed, failure: EV2pnFailure.StartFailed)).Should().BeTrue();
        await V2pnConnectionVerdict.Faulted(S()).Should().BeFalse();
    }

    [Test]
    public async Task TheKillSwitchHolding_WinsOverEverything()
    {
        await Is(S(core: EV2pnCoreState.Failed, failure: EV2pnFailure.KeepsStopping, holding: true), EV2pnVerdictKind.Unsafe, EV2pnVerdict.KillSwitch);
        // Holding without a failure is not shown as holding.
        await Is(S(holding: true), EV2pnVerdictKind.Safe, EV2pnVerdict.ProxyConnected);
    }
}
