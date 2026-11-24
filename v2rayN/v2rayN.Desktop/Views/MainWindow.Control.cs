using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>
/// V2pn: the window's part in the control channel (v2pnctl and AI assistants, ServiceLib/V2pn/V2pnControl*.cs). The
/// commands run on this window's thread and switch servers and routes the way its own buttons do; whatever a command
/// changes is said in a toast, so the person always sees that something did.
/// </summary>
public partial class MainWindow : IV2pnControlUi
{
    private V2pnControlService? _control;
    private bool _controlStarted;
    private bool _controlStopped;

    /// <summary>Once the window has its view model: the channel answers "off" until the setting allows more.</summary>
    private void InitControl()
    {
        if (_controlStarted || ViewModel is not { } vm)
        {
            return;
        }
        _controlStarted = true;
        _ = StartControlAsync(new V2pnControlApp(_config, vm.StatusBarViewModel, V2pnConnectionService.Instance, this));
    }

    // Right after a restart as administrator the V2pn before it may still hold the pipe for a moment.
    private async Task StartControlAsync(V2pnControlApp app)
    {
        for (var attempt = 0; attempt < 10 && !_controlStopped; attempt++)
        {
            if ((_control = V2pnControlService.Start(app, Utils.GetBaseDirectory())) != null)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    private void StopControl()
    {
        _controlStopped = true;
        _control?.Dispose();
        _control = null;
    }

    Task<T> IV2pnControlUi.InvokeAsync<T>(Func<Task<T>> work) => Dispatcher.UIThread.InvokeAsync(work);

    V2pnConnectionSnapshot IV2pnControlUi.Snapshot() => Snapshot();

    string? IV2pnControlUi.CountryOf(ProfileItem server) =>
        V2pnPartnerLive.Instance.Describe(server.Subid, server.Remarks, server.Address)?.Country ?? CountryResolver.Resolve(server.Remarks);

    async Task IV2pnControlUi.SelectServerAsync(string indexId)
    {
        if (ViewModel?.ProfilesViewModel is { } profiles)
        {
            await profiles.SetDefaultServer(indexId);
        }
    }

    async Task<string?> IV2pnControlUi.FastestAsync(IReadOnlyList<string> indexIds)
    {
        if (ViewModel?.ProfilesViewModel is not { } profiles)
        {
            return null;
        }
        await WaitForPingEndAsync(profiles);
        // The test takes ids; the list on screen may be one subscription's, so the models are made for it.
        var models = indexIds.Select(id => new ProfileItemModel { IndexId = id }).ToList();
        if (!await PingAndWaitAsync(() => profiles.PingProfilesAsync(models)))
        {
            return null;
        }
        return await FastestOfAsync(indexIds);
    }

    async Task<bool> IV2pnControlUi.SelectPresetAsync(EV2pnPreset preset)
    {
        var kind = preset switch
        {
            EV2pnPreset.RussiaDirect => RoutingPresetKind.RussiaDirect,
            EV2pnPreset.BlockedOnly => RoutingPresetKind.BlockedOnly,
            _ => RoutingPresetKind.All,
        };
        var done = await SelectPresetAsync(kind);
        RefreshRouteUi();
        return done;
    }

    Task IV2pnControlUi.ActivateRouteAsync(string routingId) => ActivateRoutingAsync(routingId, sourcesChanged: false);

    void IV2pnControlUi.AskForTunRights(bool connect)
    {
        ShowHideWindow(true);
        // The window's own question (administrator rights on Windows, the password on a Mac); not awaited, the person
        // answers when they are there.
        _ = connect ? V2pnConnectionService.Instance.ConnectAsync() : V2pnConnectionService.Instance.SetModeAsync(true);
    }

    void IV2pnControlUi.ShowWindow() => ShowHideWindow(true);

    void IV2pnControlUi.Announce(EV2pnControlNotice notice, string source, string? detail) => Dispatcher.UIThread.Post(() =>
    {
        var by = source == V2pnControlProtocol.SourceMcp ? L.CtlByAgent : L.CtlByTerminal;
        var text = notice switch
        {
            EV2pnControlNotice.Connected => string.Format(L.CtlConnected, by),
            EV2pnControlNotice.Disconnected => string.Format(L.CtlDisconnected, by),
            EV2pnControlNotice.ServerChanged => string.Format(L.CtlServerChanged, detail, by),
            EV2pnControlNotice.ModeChanged => string.Format(detail == "vpn" ? L.CtlModeVpn : L.CtlModeProxy, by),
            EV2pnControlNotice.RouteChanged => string.Format(L.CtlRouteChanged, RouteTitle(detail), by),
            _ => L.CtlReverted,
        };
        ShowToast(notice == EV2pnControlNotice.Reverted ? ToastKind.Warning : ToastKind.Info, text);
    });

    private static string RouteTitle(string? name) => name switch
    {
        "ru-direct" => L.RouteRussiaDirect,
        "blocked-only" => L.RouteBlockedOnly,
        _ => L.RouteAll,
    };
}
