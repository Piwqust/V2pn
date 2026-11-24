using ServiceLib.ViewModels;

namespace ServiceLib;

/// <summary>
/// What the control channel needs from the window: its thread, the bits of state only it holds, the server and route
/// switches that live in its view models, and the person's screen (the window itself, a toast, the question about
/// VPN rights).
/// </summary>
public interface IV2pnControlUi
{
    /// <summary>Runs <paramref name="work"/> on the window's thread, where the switches and the view models live.</summary>
    Task<T> InvokeAsync<T>(Func<Task<T>> work);

    V2pnConnectionSnapshot Snapshot();

    /// <summary>The server's country as the window shows it: the partner's word, or a guess from the name.</summary>
    string? CountryOf(ProfileItem server);

    Task SelectServerAsync(string indexId);

    /// <summary>Tests the servers and returns the quickest that answered, or null.</summary>
    Task<string?> FastestAsync(IReadOnlyList<string> indexIds);

    /// <summary>One of the three routes, downloading the Russian lists first when it needs them. False when it did not change.</summary>
    Task<bool> SelectPresetAsync(EV2pnPreset preset);

    /// <summary>Any rule set by its id, as a revert puts it back.</summary>
    Task ActivateRouteAsync(string routingId);

    /// <summary>Shows the window and asks there for the rights VPN mode needs (then connects, or switches the mode).</summary>
    void AskForTunRights(bool connect);

    void ShowWindow();

    void Announce(EV2pnControlNotice notice, string source, string? detail);
}

/// <summary>
/// V2pn: the running app as the control channel's commands see it. Connecting goes through V2pnConnectionService, as the
/// window's own button does; the rest reads the same database and view models the window reads.
/// <para>
/// Nothing that needs the person happens quietly: VPN mode without its rights answers "confirm on the screen" and asks
/// there, where v2rayN would restart itself as administrator.
/// </para>
/// </summary>
public sealed class V2pnControlApp(Config config, StatusBarViewModel status, V2pnConnectionService connection, IV2pnControlUi ui) : IV2pnControlApp
{
    public EV2pnControlLevel Level => config.UiItem.V2pnControl;

    public Task<JsonObject> StatusAsync() => ui.InvokeAsync(async () =>
    {
        var server = await ConfigDbQuery.GetProfileItem(config.IndexId);
        return V2pnControlData.Status(ui.Snapshot(), status.CurrentAvailability, status.AvailabilityCheckedAt, config.UiItem.V2pnPreferTun,
            server, server == null ? null : ui.CountryOf(server), status.SelectedRouting?.Id);
    });

    // The servers people see: the ones a partner hides are not listed, as in the window.
    private static async Task<List<ProfileItem>> VisibleServersAsync() =>
        (await ConfigDbQuery.ProfileItems(string.Empty) ?? [])
        .Where(p => !V2pnPartnerLive.Instance.Hidden(p.Subid, p.Remarks, p.Address))
        .ToList();

    public Task<JsonArray> ServersAsync(string? country) => ui.InvokeAsync(async () =>
    {
        var servers = await VisibleServersAsync();
        var tests = (await ProfileExManager.Instance.GetProfileExs()).GroupBy(t => t.IndexId).ToDictionary(g => g.Key, g => g.First());
        var subs = await ConfigDbQuery.SubItems() ?? [];
        var names = subs.Select((s, i) => (s.Id, Name: V2pnControlData.SubName(s, SubInfoManager.Instance.Get(s.Id), i)))
            .GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().Name);
        return V2pnControlData.Servers(servers, tests, names, ui.CountryOf, config.IndexId, country);
    });

    public Task<JsonArray> SubsAsync() => ui.InvokeAsync(async () =>
    {
        var subs = await ConfigDbQuery.SubItems() ?? [];
        var counts = (await VisibleServersAsync()).GroupBy(p => p.Subid ?? string.Empty).ToDictionary(g => g.Key, g => g.Count());
        return V2pnControlData.Subs(subs, SubInfoManager.Instance.Get, counts);
    });

    public Task<string> DiagnosticsAsync() => ui.InvokeAsync(async () =>
    {
        var routeId = status.SelectedRouting?.Id;
        var route = V2pnControlProtocol.Routes.Any(r => r.Id == routeId) ? routeId! : routeId.IsNullOrEmpty() ? "none" : "custom";
        var snapshot = await V2pnDiagnostics.CollectAsync(config, status.CurrentAvailability, status.AvailabilityCheckedAt, route);
        return V2pnDiagnostics.Format(snapshot);
    });

    public Task<bool> CheckAsync() => ui.InvokeAsync(async () =>
    {
        if (!connection.IsConnected)
        {
            return false;
        }
        await status.TestServerAvailability();
        return true;
    });

    public Task ShowWindowAsync() => ui.InvokeAsync(() =>
    {
        ui.ShowWindow();
        return Task.FromResult(true);
    });

    public Task<V2pnControlResult> ConnectAsync(V2pnControlText t) => ui.InvokeAsync(async () =>
    {
        if (connection.IsConnected)
        {
            return V2pnControlResult.Already(V2pnControlMessages.AlreadyConnected(t));
        }
        if (config.IndexId.IsNullOrEmpty() || await ConfigDbQuery.GetProfileItem(config.IndexId) == null)
        {
            return V2pnControlResult.Fail(V2pnControlProtocol.ErrNoServer, V2pnControlMessages.NoServer(t));
        }
        if (config.UiItem.V2pnPreferTun && !status.AllowEnableTun())
        {
            ui.AskForTunRights(connect: true);
            return V2pnControlResult.Fail(V2pnControlProtocol.ErrNeedsConfirmation, V2pnControlMessages.VpnRights(t));
        }
        return await connection.ConnectAsync()
            ? V2pnControlResult.Done(V2pnControlMessages.Connecting(t))
            : V2pnControlResult.Fail(V2pnControlProtocol.ErrFailed, V2pnControlMessages.DidNotConnect(t));
    });

    public Task<V2pnControlResult> DisconnectAsync(V2pnControlText t) => ui.InvokeAsync(() =>
    {
        if (!connection.IsConnected)
        {
            return Task.FromResult(V2pnControlResult.Already(V2pnControlMessages.AlreadyOff(t)));
        }
        connection.Disconnect();
        return Task.FromResult(V2pnControlResult.Done(V2pnControlMessages.Disconnected(t)));
    });

    public Task<V2pnControlResult> UseAsync(string? server, bool fastest, string? country, V2pnControlText t) => ui.InvokeAsync(async () =>
    {
        var servers = await VisibleServersAsync();
        string? id;
        if (fastest)
        {
            var candidates = servers.Where(s => country == null || string.Equals(ui.CountryOf(s), country, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.IndexId).ToList();
            if (candidates.Count == 0)
            {
                return V2pnControlResult.Fail(V2pnControlProtocol.ErrNotFound, V2pnControlMessages.NoServers(country, t));
            }
            id = await ui.FastestAsync(candidates);
            if (id == null)
            {
                return V2pnControlResult.Fail(V2pnControlProtocol.ErrNoAnswer, V2pnControlMessages.NoneAnswered(t));
            }
        }
        else
        {
            var (found, code, message) = V2pnControlData.FindServer(servers.Select(s => (s.IndexId, V2pnControlData.ServerName(s))).ToList(), server ?? string.Empty, t);
            if (found == null)
            {
                return V2pnControlResult.Fail(code ?? V2pnControlProtocol.ErrNotFound, message);
            }
            id = found;
        }
        var name = V2pnControlData.ServerName(servers.First(s => s.IndexId == id));
        if (id == config.IndexId)
        {
            return V2pnControlResult.Already(V2pnControlMessages.AlreadyInUse(name, t));
        }
        await ui.SelectServerAsync(id);
        if (config.IndexId != id)
        {
            return V2pnControlResult.Fail(V2pnControlProtocol.ErrFailed, V2pnControlMessages.DidNotSwitch(name, t));
        }
        return V2pnControlResult.Done(V2pnControlMessages.Switched(name, connection.IsConnected, t), name);
    });

    public Task<V2pnControlResult> SetModeAsync(bool tun, V2pnControlText t) => ui.InvokeAsync(async () =>
    {
        var connected = connection.IsConnected;
        var now = connected ? status.EnableTun : config.UiItem.V2pnPreferTun;
        if (now == tun && config.UiItem.V2pnPreferTun == tun)
        {
            return V2pnControlResult.Already(V2pnControlMessages.ModeAlready(tun, t));
        }
        if (tun && connected && !status.AllowEnableTun())
        {
            ui.AskForTunRights(connect: false);
            return V2pnControlResult.Fail(V2pnControlProtocol.ErrNeedsConfirmation, V2pnControlMessages.VpnRights(t));
        }
        if (!await connection.SetModeAsync(tun))
        {
            return V2pnControlResult.Fail(V2pnControlProtocol.ErrNeedsConfirmation, V2pnControlMessages.VpnRights(t));
        }
        return V2pnControlResult.Done(V2pnControlMessages.ModeSet(tun, connected, t), tun ? "vpn" : "proxy");
    });

    public Task<V2pnControlResult> SetRouteAsync(EV2pnPreset preset, V2pnControlText t) => ui.InvokeAsync(async () =>
    {
        var id = V2pnControlData.RouteId(preset);
        var name = V2pnControlData.RouteName(id);
        if (status.SelectedRouting?.Id == id)
        {
            return V2pnControlResult.Already(V2pnControlMessages.RouteAlready(name, t));
        }
        if (!await ui.SelectPresetAsync(preset) || status.SelectedRouting?.Id != id)
        {
            return V2pnControlResult.Fail(V2pnControlProtocol.ErrFailed, V2pnControlMessages.RouteFailed(t));
        }
        return V2pnControlResult.Done(V2pnControlMessages.RouteSet(name, t), name);
    });

    private V2pnControlState Capture() => new(connection.IsConnected,
        connection.IsConnected ? status.EnableTun : config.UiItem.V2pnPreferTun, config.IndexId, status.SelectedRouting?.Id);

    public Task<V2pnControlState> CaptureAsync() => ui.InvokeAsync(() => Task.FromResult(Capture()));

    public Task RestoreAsync(V2pnControlState state) => ui.InvokeAsync(async () =>
    {
        var now = Capture();
        if (state.Route is { Length: > 0 } route && route != now.Route)
        {
            await ui.ActivateRouteAsync(route);
        }
        if (state.Server is { Length: > 0 } server && server != now.Server && await ConfigDbQuery.GetProfileItem(server) != null)
        {
            await ui.SelectServerAsync(server);
        }
        // VPN mode came back only with its rights; without them it would ask, and nobody may be there to answer.
        var tunAllowed = !state.Tun || status.AllowEnableTun();
        if (state.Tun != now.Tun && (tunAllowed || !connection.IsConnected))
        {
            await connection.SetModeAsync(state.Tun);
        }
        if (state.Connected && !connection.IsConnected && (!config.UiItem.V2pnPreferTun || status.AllowEnableTun()))
        {
            await connection.ConnectAsync();
        }
        else if (!state.Connected && connection.IsConnected)
        {
            connection.Disconnect();
        }
        return true;
    });

    public void Announce(EV2pnControlNotice notice, string source, string? detail) => ui.Announce(notice, source, detail);
}

/// <summary>
/// V2pn: what <see cref="V2pnControlApp"/> says about a change, in the asker's words (<see cref="V2pnControlText"/>):
/// English for MCP and older v2pnctl, Russian for a v2pnctl that asked for it. Commands and route names stay as they
/// are typed. Kept apart from the app, which needs the window, so the tests read them.
/// </summary>
public static class V2pnControlMessages
{
    public static string VpnRights(V2pnControlText t) => t.T(
        "VPN mode needs administrator rights, and V2pn does not take them without the person: it asks on the screen now. Answer there, or use proxy mode (\"v2pnctl mode proxy\").",
        "Для режима VPN нужны права администратора, а сам V2pn их не берёт: он спрашивает на экране. Ответьте там или включите режим прокси: v2pnctl mode proxy.");

    public static string AlreadyConnected(V2pnControlText t) => t.T("V2pn is already connected.", "V2pn уже подключён.");

    public static string NoServer(V2pnControlText t) => t.T(
        "No server is chosen. Choose one with \"v2pnctl use <name>\".",
        "Сервер не выбран. Выберите его: v2pnctl use <название>.");

    public static string Connecting(V2pnControlText t) => t.T("Connecting.", "V2pn подключается.");

    public static string DidNotConnect(V2pnControlText t) => t.T("V2pn did not connect.", "V2pn не подключился.");

    public static string AlreadyOff(V2pnControlText t) => t.T("V2pn is already off.", "V2pn уже отключён.");

    public static string Disconnected(V2pnControlText t) => t.T("Disconnected.", "Отключено.");

    public static string NoServers(string? country, V2pnControlText t) => country == null
        ? t.T("There are no servers.", "Серверов нет.")
        : t.T($"There are no servers in {country}.", $"Серверов в стране {country} нет.");

    public static string NoneAnswered(V2pnControlText t) => t.T("None of the servers answered the test.", "Ни один сервер не ответил на проверку.");

    public static string AlreadyInUse(string name, V2pnControlText t) => t.T($"{name} is already in use.", $"Сервер {name} уже выбран.");

    public static string DidNotSwitch(string name, V2pnControlText t) => t.T($"V2pn did not switch to {name}.", $"V2pn не переключился на {name}.");

    public static string Switched(string name, bool connected, V2pnControlText t) => connected
        ? t.T($"Switched to {name}.", $"Переключено на {name}.")
        : t.T($"{name} is chosen; V2pn uses it on the next connect.", $"Выбран сервер {name}; V2pn подключится через него в следующий раз.");

    public static string ModeAlready(bool tun, V2pnControlText t) => tun
        ? t.T("VPN mode is already on.", "Режим VPN уже включён.")
        : t.T("Proxy mode is already on.", "Режим прокси уже включён.");

    public static string ModeSet(bool tun, bool connected, V2pnControlText t)
    {
        var (en, ru) = tun ? ("VPN mode", "режим VPN") : ("Proxy mode", "режим прокси");
        return connected
            ? t.T($"{en}.", $"Включён {ru}.")
            : t.T($"{en}: V2pn connects so next time.", $"Выбран {ru}: так V2pn подключится в следующий раз.");
    }

    // The route by its command-line name (ru-direct, blocked-only, all), the one people type.
    public static string RouteAlready(string name, V2pnControlText t) => t.T($"The route is already {name}.", $"Маршрут {name} уже выбран.");

    public static string RouteSet(string name, V2pnControlText t) => t.T($"Route: {name}.", $"Маршрут: {name}.");

    public static string RouteFailed(V2pnControlText t) => t.T(
        "The route did not change. The Russian routes need their lists downloaded first; V2pn's window says why.",
        "Маршрут не изменился. Для российских маршрутов сначала нужно скачать списки сайтов; причина — в окне V2pn.");
}

/// <summary>
/// V2pn: the control channel of this running V2pn: its listening end, the handler and the clock that puts back
/// unconfirmed changes. Started once the window is up; the channel answers "off" until the setting allows more.
/// </summary>
public sealed class V2pnControlService : IDisposable
{
    private readonly V2pnControlServer _server;
    private readonly CancellationTokenSource _stop = new();

    private V2pnControlService(V2pnControlServer server, V2pnControlHandler handler)
    {
        _server = server;
        Handler = handler;
        _ = Task.Run(TickAsync);
    }

    public V2pnControlHandler Handler { get; }

    /// <summary>Listens where v2pnctl looks for this copy of V2pn (<see cref="V2pnControlAddress"/>). Null when it cannot.</summary>
    public static V2pnControlService? Start(IV2pnControlApp app, string programFolder)
    {
        var handler = new V2pnControlHandler(app, () => DateTime.UtcNow, Logging.SaveLog);
        var endpoint = V2pnControlEndpoint.For(programFolder);
        var server = V2pnControlServer.Start(endpoint, handler.HandleLineAsync, Logging.SaveLog);
        if (server == null)
        {
            return null;
        }
        Logging.SaveLog($"V2pn control: listening on {endpoint}");
        return new V2pnControlService(server, handler);
    }

    private async Task TickAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                await Handler.TickAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _server.Dispose();
    }
}
