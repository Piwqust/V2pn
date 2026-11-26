using v2rayN.Desktop.Common;
using v2rayN.Desktop.Converters;
using v2rayN.Desktop.ViewModels;

namespace v2rayN.Desktop.Views;

/// <summary>
/// V2pn: what the tray panel (TrayPanel, Windows) reads and does. It shows what the window shows, from the
/// same fields, and acts through the window's own paths: Connect, the mode switch, the route presets.
/// </summary>
public partial class MainWindow
{
    /// <summary>Raised when something the tray panel shows may have changed; an open panel reads TrayViewNow again.</summary>
    internal event Action? TrayChanged;

    private void NotifyTray() => TrayChanged?.Invoke();

    internal TrayView? TrayViewNow()
    {
        if (ViewModel?.StatusBarViewModel is not { } status)
        {
            return null;
        }
        var state = _trayState?.State ?? CurrentState();
        var label = _trayState?.Label ?? L.Unprotected;
        var connected = state != ProtectionState.Unprotected;
        var hasServers = (ViewModel.ProfilesViewModel.AllServersCount) > 0 || _hasServer;
        var active = status.SelectedRouting;
        var route = RoutingPresets.KindOf(active?.Id);
        return new TrayView(
            Kind: _pillKind ?? "unsafe",
            StateLabel: label,
            Connected: connected,
            Busy: label == L.StateConnecting || label == L.StateReconnecting,
            HasServers: hasServers,
            Flag: borderHeaderFlag.IsVisible ? imgHeaderFlag.Source as Bitmap : null,
            Title: _hasServer ? txtHeaderTitle.Text ?? L.NoServer : L.NoServer,
            Subtitle: _hasServer ? txtHeaderSubtitle.Text : L.PickServer,
            Delay: _mapDelay,
            Tun: lstMode.SelectedIndex == 1,
            NeedsAdmin: Utils.IsWindows() && !Utils.IsAdministrator(),
            Method: ConnectionPrefs.ProxyMethod(_config),
            PacAvailable: status.BlSystemProxyPacVisible,
            Route: route,
            CustomRoute: route == null && active != null ? RoutingPresets.DisplayName(active.Remarks) : null,
            Servers: TrayServers());
    }

    // The favorites, then the servers used last: the ones a person goes back to.
    private List<TrayServer> TrayServers()
    {
        if (_sidebar is not { } sidebar)
        {
            return [];
        }
        var picked = sidebar.MenuFavorites().Concat(sidebar.MenuRecent(6))
            .GroupBy(m => m.IndexId).Select(g => g.First())
            .Take(TrayPanel.QuickServers)
            .ToList();
        return picked.Select(m =>
        {
            var code = V2pnPartnerLive.Instance.Describe(m.Subid, m.Remarks, m.Address)?.Country ?? CountryResolver.Resolve(m.Remarks);
            var name = Shorten(ServerNames.TrimLeadingSymbols(CountryResolver.ServerName(m)), 40);
            return new TrayServer(m.IndexId, name, V2pnConverters.Flag(code), m.Delay, m.IndexId == _config.IndexId);
        }).ToList();
    }

    internal Task TrayConnectToAsync(string indexId) => ConnectToAsync(indexId);

    internal Task TrayFastestAsync() => RecheckFastestAsync();

    /// <summary>The window's own mode switch: it asks for the rights, saves the choice and reconnects when connected.</summary>
    internal void TraySetMode(bool tun)
    {
        if (tun && ViewModel?.StatusBarViewModel is { } status && !status.AllowEnableTun())
        {
            // VPN mode asks for administrator rights in the window.
            ShowHideWindow(true);
        }
        lstMode.SelectedIndex = tun ? 1 : 0;
    }

    /// <summary>How Proxy mode sets the system proxy, as Settings → General has it; a Proxy-mode connection follows at once.</summary>
    internal void TraySetMethod(ESysProxyType method)
    {
        V2pnConnectionService.Instance.SetProxyMethod(method);
        _ = ConfigTextHandler.SaveConfig();
        NotifyTray();
    }

    internal void TrayShowServers()
    {
        ShowHideWindow(true);
        SelectTab(SidebarTab.Countries);
    }

    internal void TrayShowRoutes() => ShowHideWindow(true);

    internal async Task TrayPasteAsync()
    {
        // The window comes up first, so its toasts and the new subscription's spinner are seen.
        ShowHideWindow(true);
        await AddServerViaClipboardAsync();
    }

    internal void TrayUpdateSubscriptions(bool viaVpn)
    {
        if (ViewModel?.StatusBarViewModel is { } status)
        {
            (viaVpn ? status.SubUpdateViaProxyCmd : status.SubUpdateCmd).Execute().Subscribe();
        }
    }

    internal void TrayCopyProxyCommand() => ViewModel?.StatusBarViewModel?.CopyProxyCmdToClipboardCmd.Execute().Subscribe();

    internal void TrayOpenSettings()
    {
        ShowHideWindow(true);
        ViewModel?.OptionSettingCmd.Execute().Subscribe();
    }

    internal static async Task TrayQuitAsync()
    {
        await AppManager.Instance.AppExitAsync(false);
        AppManager.Instance.Shutdown(true);
    }
}
