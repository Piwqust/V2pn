namespace ServiceLib;

/// <summary>The two switches that say whether and how V2pn is connected; StatusBarViewModel holds them.</summary>
public interface IV2pnConnectionState
{
    bool EnableTun { get; set; }

    /// <summary>An <see cref="ESysProxyType"/>: ForcedClear is off.</summary>
    int SystemProxySelected { get; set; }
}

/// <summary>What connecting may need to ask the person: the window answers, a command line would refuse.</summary>
public interface IV2pnConnectionUi
{
    /// <summary>
    /// Whether VPN mode may start now. The window asks for the rights (on Windows it offers to restart as
    /// administrator with VPN mode already chosen, on a Mac it asks for the password) and answers false while asking.
    /// </summary>
    Task<bool> EnsureTunAllowedAsync(bool connectAfterRestart);

    /// <summary>Connect was asked for with no server chosen.</summary>
    void NoServerChosen();

    /// <summary>Disconnect was asked for: forget that VPN rights were refused, it was the person's last word.</summary>
    void Disconnecting();
}

/// <summary>
/// V2pn: the one place that connects, disconnects and switches the mode. The main window, its mode bar, the global
/// hotkeys, the Mac menus, the settings and the fixes on the status card each set the switches on their own, with
/// their own rules (the hotkeys put a system proxy over the tunnel; the settings could switch a VPN connection). They
/// all come here now, and so can a command line later. The verdict on what the switches mean is
/// <see cref="V2pnConnectionVerdict"/>.
/// </summary>
public sealed class V2pnConnectionService
{
    public static V2pnConnectionService Instance { get; } = new();

    private Config? _config;
    private IV2pnConnectionState? _state;
    private IV2pnConnectionUi? _ui;

    /// <summary>Called once the window has its view model.</summary>
    public void Attach(Config config, IV2pnConnectionState state, IV2pnConnectionUi ui)
    {
        _config = config;
        _state = state;
        _ui = ui;
    }

    /// <summary>Whether V2pn is on: the tunnel, or a system proxy (or PAC, or the local port only) in Proxy mode.</summary>
    public bool IsConnected => _state is { } s && IsOn(s);

    public static bool IsOn(IV2pnConnectionState s) => s.EnableTun || (ESysProxyType)s.SystemProxySelected != ESysProxyType.ForcedClear;

    /// <summary>
    /// What Connect does in Proxy mode. The saved choice; on first use it is taken from the mode v2rayN had (On, PAC
    /// or Keep), so an upgrade keeps behaving the same. PAC is Windows only.
    /// </summary>
    public static ESysProxyType ProxyMethod(Config config)
    {
        var method = config.UiItem.V2pnProxyMethod ??= config.SystemProxyItem.SysProxyType switch
        {
            ESysProxyType.Pac or ESysProxyType.Unchanged => config.SystemProxyItem.SysProxyType,
            _ => ESysProxyType.ForcedChange,
        };
        if (method == ESysProxyType.ForcedClear || (method == ESysProxyType.Pac && !OperatingSystem.IsWindows()))
        {
            method = ESysProxyType.ForcedChange;
        }
        return method;
    }

    /// <summary>Connects with the chosen server, mode and way of the proxy. False when it did not (no server, no rights).</summary>
    public async Task<bool> ConnectAsync()
    {
        if (_config is not { } config || _state is not { } state || _ui is not { } ui)
        {
            return false;
        }
        if (config.IndexId.IsNullOrEmpty())
        {
            ui.NoServerChosen();
            return false;
        }
        var tun = config.UiItem.V2pnPreferTun;
        if (tun && !await ui.EnsureTunAllowedAsync(connectAfterRestart: true))
        {
            return false;
        }
        Apply(state, config, tun);
        return true;
    }

    /// <summary>Stops the core; the system proxy is cleared only if V2pn set it (V2pnConnection).</summary>
    public void Disconnect()
    {
        if (_state is not { } state)
        {
            return;
        }
        _ui?.Disconnecting();
        if (state.EnableTun)
        {
            state.EnableTun = false;
        }
        state.SystemProxySelected = (int)ESysProxyType.ForcedClear;
    }

    /// <summary>
    /// Chooses Proxy or VPN mode: remembered for the next Connect, and applied at once while connected. False when VPN
    /// mode was chosen while connected and its rights were not given; nothing changes then.
    /// </summary>
    public async Task<bool> SetModeAsync(bool tun)
    {
        if (_config is not { } config || _state is not { } state || _ui is not { } ui)
        {
            return false;
        }
        var connected = IsOn(state);
        if (tun && connected && !await ui.EnsureTunAllowedAsync(connectAfterRestart: true))
        {
            return false;
        }
        config.UiItem.V2pnPreferTun = tun;
        await ConfigTextHandler.SaveConfig();
        if (connected && tun != state.EnableTun)
        {
            Apply(state, config, tun);
        }
        return true;
    }

    /// <summary>Chooses how Proxy mode sets the system proxy; a Proxy-mode connection follows at once.</summary>
    public void SetProxyMethod(ESysProxyType method)
    {
        if (_config is not { } config)
        {
            return;
        }
        config.UiItem.V2pnProxyMethod = method;
        if (_state is { EnableTun: false } state && (ESysProxyType)state.SystemProxySelected is var live
            && live != ESysProxyType.ForcedClear && live != method)
        {
            state.SystemProxySelected = (int)method;
        }
    }

    /// <summary>Turns V2pn off when the last server is gone: "connected" to nothing would only mislead.</summary>
    public async Task DisconnectWhenNoServersAsync()
    {
        if (IsConnected && !await V2pnConnection.HasAnyServerAsync())
        {
            Disconnect();
        }
    }

    /// <summary>Sets the switches for a mode. TUN already carries everything; a system proxy left behind would outlive a crash.</summary>
    public static void Apply(IV2pnConnectionState state, Config config, bool tun)
    {
        if (tun)
        {
            state.EnableTun = true;
            state.SystemProxySelected = (int)ESysProxyType.ForcedClear;
        }
        else
        {
            state.SystemProxySelected = (int)ProxyMethod(config);
            state.EnableTun = false;
        }
    }
}
