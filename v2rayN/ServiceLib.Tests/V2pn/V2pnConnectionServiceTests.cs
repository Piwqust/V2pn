using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

/// <summary>Connect, Disconnect and the mode, as every surface of V2pn now asks for them.</summary>
[NotInParallel("V2pnConnectionService")]
public class V2pnConnectionServiceTests
{
    private sealed class State : IV2pnConnectionState
    {
        public bool EnableTun { get; set; }
        public int SystemProxySelected { get; set; } = (int)ESysProxyType.ForcedClear;
    }

    private sealed class Ui(bool tunAllowed) : IV2pnConnectionUi
    {
        public int TunAsked;
        public int NoServer;
        public int Disconnects;

        public Task<bool> EnsureTunAllowedAsync(bool connectAfterRestart)
        {
            TunAsked++;
            return Task.FromResult(tunAllowed);
        }

        public void NoServerChosen() => NoServer++;

        public void Disconnecting() => Disconnects++;
    }

    private static (V2pnConnectionService Service, Config Config, State State, Ui Ui) Make(bool tunAllowed = true, bool preferTun = false, string? server = "s1")
    {
        var config = CoreConfigTestFactory.CreateConfig();
        CoreConfigTestFactory.BindAppManagerConfig(config);
        config.IndexId = server;
        config.UiItem.V2pnPreferTun = preferTun;
        config.UiItem.V2pnProxyMethod = ESysProxyType.ForcedChange;
        var state = new State();
        var ui = new Ui(tunAllowed);
        var service = new V2pnConnectionService();
        service.Attach(config, state, ui);
        return (service, config, state, ui);
    }

    [Test]
    public async Task Connect_InProxyMode_SetsTheChosenWayOfTheProxy()
    {
        var (service, config, state, _) = Make();
        config.UiItem.V2pnProxyMethod = ESysProxyType.Pac;

        await (await service.ConnectAsync()).Should().BeTrue();

        await state.EnableTun.Should().BeFalse();
        var expected = OperatingSystem.IsWindows() ? ESysProxyType.Pac : ESysProxyType.ForcedChange;
        await ((ESysProxyType)state.SystemProxySelected).Should().BeEqualTo(expected);
        await service.IsConnected.Should().BeTrue();
    }

    [Test]
    public async Task Connect_InVpnMode_AsksForTheRights_AndLeavesNoSystemProxy()
    {
        var (service, _, state, ui) = Make(preferTun: true);

        await (await service.ConnectAsync()).Should().BeTrue();

        await ui.TunAsked.Should().BeEqualTo(1);
        await state.EnableTun.Should().BeTrue();
        await ((ESysProxyType)state.SystemProxySelected).Should().BeEqualTo(ESysProxyType.ForcedClear);
    }

    [Test]
    public async Task Connect_InVpnMode_WithoutTheRights_ChangesNothing()
    {
        var (service, _, state, _) = Make(tunAllowed: false, preferTun: true);

        await (await service.ConnectAsync()).Should().BeFalse();

        await service.IsConnected.Should().BeFalse();
        await state.EnableTun.Should().BeFalse();
    }

    [Test]
    public async Task Connect_WithoutAServer_SaysSo()
    {
        var (service, _, _, ui) = Make(server: null);

        await (await service.ConnectAsync()).Should().BeFalse();

        await ui.NoServer.Should().BeEqualTo(1);
        await service.IsConnected.Should().BeFalse();
    }

    [Test]
    public async Task Disconnect_TurnsBothSwitchesOff()
    {
        var (service, _, state, ui) = Make(preferTun: true);
        await service.ConnectAsync();

        service.Disconnect();

        await service.IsConnected.Should().BeFalse();
        await state.EnableTun.Should().BeFalse();
        await ui.Disconnects.Should().BeEqualTo(1);
    }

    [Test]
    public async Task SetMode_WhileOff_OnlyRemembers()
    {
        var (service, config, state, ui) = Make();

        await (await service.SetModeAsync(true)).Should().BeTrue();

        await config.UiItem.V2pnPreferTun.Should().BeTrue();
        await ui.TunAsked.Should().BeEqualTo(0);
        await service.IsConnected.Should().BeFalse();
        await state.EnableTun.Should().BeFalse();
    }

    [Test]
    public async Task SetMode_WhileConnected_SwitchesAtOnce_OrNotAtAllWithoutTheRights()
    {
        var (service, _, state, _) = Make();
        await service.ConnectAsync();

        await (await service.SetModeAsync(true)).Should().BeTrue();
        await state.EnableTun.Should().BeTrue();
        await ((ESysProxyType)state.SystemProxySelected).Should().BeEqualTo(ESysProxyType.ForcedClear);

        var (refused, refusedConfig, refusedState, _) = Make(tunAllowed: false);
        await refused.ConnectAsync();
        await (await refused.SetModeAsync(true)).Should().BeFalse();
        await refusedState.EnableTun.Should().BeFalse();
        await refusedConfig.UiItem.V2pnPreferTun.Should().BeFalse();
    }

    [Test]
    public async Task SetProxyMethod_MovesAProxyConnection_NotAVpnOne()
    {
        var (service, _, state, _) = Make();
        await service.ConnectAsync();
        service.SetProxyMethod(ESysProxyType.Unchanged);
        await ((ESysProxyType)state.SystemProxySelected).Should().BeEqualTo(ESysProxyType.Unchanged);

        var (vpn, _, vpnState, _) = Make(preferTun: true);
        await vpn.ConnectAsync();
        vpn.SetProxyMethod(ESysProxyType.ForcedChange);
        await vpnState.EnableTun.Should().BeTrue();
        await ((ESysProxyType)vpnState.SystemProxySelected).Should().BeEqualTo(ESysProxyType.ForcedClear);
    }
}
