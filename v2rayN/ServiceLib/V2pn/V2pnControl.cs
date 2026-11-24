namespace ServiceLib;

/// <summary>V2pn: what the command line and AI assistants may do; Settings → General. Off until the person turns it on.</summary>
public enum EV2pnControlLevel
{
    Off,
    ReadOnly,
    Control,
}

/// <summary>What a revert puts back: the connection on or off, the mode, the server and the route.</summary>
public sealed record V2pnControlState(bool Connected, bool Tun, string? Server, string? Route);

/// <summary>What a change did. <see cref="Changed"/> false: it was already so, and nothing is said on the screen.</summary>
public sealed record V2pnControlResult(bool Ok, bool Changed, string Message, string? Code = null, string? Detail = null)
{
    public static V2pnControlResult Done(string message, string? detail = null) => new(true, true, message, null, detail);

    public static V2pnControlResult Already(string message) => new(true, false, message);

    public static V2pnControlResult Fail(string code, string message) => new(false, false, message, code);
}

/// <summary>What the window tells the person after a command (a toast), and in whose name.</summary>
public enum EV2pnControlNotice
{
    Connected,
    Disconnected,
    ServerChanged,
    ModeChanged,
    RouteChanged,
    Reverted,
}

/// <summary>
/// The running V2pn as the control channel sees it: what the commands read and change. <see cref="V2pnControlApp"/> is
/// the real one; the tests have their own.
/// </summary>
public interface IV2pnControlApp
{
    EV2pnControlLevel Level { get; }

    /// <summary>The status as <see cref="V2pnControlData.Status"/> shapes it.</summary>
    Task<JsonObject> StatusAsync();

    Task<JsonArray> ServersAsync(string? country);

    Task<JsonArray> SubsAsync();

    Task<string> DiagnosticsAsync();

    /// <summary>Runs the check of the connection again; false when there is no connection to check.</summary>
    Task<bool> CheckAsync();

    Task ShowWindowAsync();

    // A change answers in the asker's words: Russian for a v2pnctl that asked for it, English for the rest.

    Task<V2pnControlResult> ConnectAsync(V2pnControlText t);

    Task<V2pnControlResult> DisconnectAsync(V2pnControlText t);

    Task<V2pnControlResult> UseAsync(string? server, bool fastest, string? country, V2pnControlText t);

    Task<V2pnControlResult> SetModeAsync(bool tun, V2pnControlText t);

    Task<V2pnControlResult> SetRouteAsync(EV2pnPreset preset, V2pnControlText t);

    Task<V2pnControlState> CaptureAsync();

    Task RestoreAsync(V2pnControlState state);

    /// <summary>A short note on the screen: what a command changed, so the person always sees that something did.</summary>
    void Announce(EV2pnControlNotice notice, string source, string? detail);
}

/// <summary>
/// V2pn: what the control channel does with a request. It checks the setting, runs one change at a time, writes every
/// command to the log with who sent it, tells the person on the screen, and puts back a change made with
/// <c>confirmWithin</c> that was not confirmed in time.
/// <para>
/// The revert is what makes it safe to let an AI assistant switch V2pn: from Russia the assistant's own API is often
/// reachable only through the VPN, so one that disconnects or picks a dead server cuts itself off and cannot undo it.
/// It cannot confirm either, and the connection comes back by itself, as a screen resolution does when nobody presses
/// "Keep". A change the person (or another command) made since is left alone.
/// </para>
/// </summary>
public sealed class V2pnControlHandler(IV2pnControlApp app, Func<DateTime> utcNow, Action<string> log)
{
    // The Russian names are the window's: the settings page, the setting and its choices.
    public static string DisabledMessage(V2pnControlText t) => t.T(
        "Control from the command line is off in V2pn. To turn it on, open V2pn: Settings > General > Control from the command line and AI, and choose Read only or Control.",
        "Управление из командной строки в V2pn выключено. Чтобы включить его, откройте V2pn: Настройки → Общие → «Управление из командной строки и ИИ» и выберите «Только чтение» или «Управление».");

    public static string ReadOnlyMessage(V2pnControlText t) => t.T(
        "V2pn lets the command line only read. To let it change the connection, open V2pn: Settings > General > Control from the command line and AI, and choose Control.",
        "V2pn разрешает командной строке только чтение. Чтобы она могла менять подключение, откройте V2pn: Настройки → Общие → «Управление из командной строки и ИИ» и выберите «Управление».");

    /// <summary>What a change made with <c>confirmWithin</c> adds to its message: how to keep it, and what happens otherwise.</summary>
    public static string ConfirmNote(string message, int seconds, string source, V2pnControlText t)
    {
        var mcp = source == V2pnControlProtocol.SourceMcp;
        return t.T(
            $"{message} {(mcp ? "Call v2pn_confirm" : "Run \"v2pnctl confirm\"")} within {seconds} s to keep it; otherwise V2pn puts back what was there before.",
            $"{message} Чтобы оставить изменение, {(mcp ? "вызовите v2pn_confirm" : "выполните v2pnctl confirm")} в течение {seconds} сек, иначе V2pn вернёт как было.");
    }

    private sealed record Pending(V2pnControlState Before, V2pnControlState After, DateTime Deadline, string Source);

    // One change at a time: from the terminal, an assistant, and the revert itself.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Pending? _pending;

    /// <summary>How long a change waits for the connection to settle (the core up, the check done) before answering.</summary>
    public TimeSpan SettleTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan SettlePoll { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Whether a change is waiting for its confirmation.</summary>
    public bool Waiting => _pending != null;

    /// <summary>One line in, one line out; never throws.</summary>
    public async Task<string> HandleLineAsync(string line)
    {
        if (V2pnControlRequest.Parse(line, out var refusal) is not { } request)
        {
            log($"V2pn control: refused a request ({refusal!.ErrorCode})");
            return refusal.ToLine();
        }
        return (await HandleAsync(request)).ToLine();
    }

    public async Task<V2pnControlAnswer> HandleAsync(V2pnControlRequest request)
    {
        V2pnControlAnswer answer;
        try
        {
            answer = await RunAsync(request);
        }
        catch (Exception ex)
        {
            log($"V2pn control: {request.Command} from {request.Source} failed: {ex.Message}");
            answer = V2pnControlAnswer.Failure(request.Id, V2pnControlProtocol.ErrFailed,
                request.Text.T($"V2pn could not do it: {ex.Message}", $"V2pn не смог это сделать: {ex.Message}"));
        }
        // Every command reaches the log with its source, so a change nobody remembers making can be traced.
        log($"V2pn control: {request.Source} {request.Command}{Describe(request.Args)}: {(answer.Ok ? "done" : answer.ErrorCode)}");
        return answer;
    }

    // The arguments a command takes are a server's name or id, a mode, a route, a country and a number: none secret.
    private static string Describe(JsonObject args) => args.Count == 0 ? string.Empty
        : " " + string.Join(", ", args.Select(a => $"{a.Key}={a.Value?.ToJsonString(V2pnControlProtocol.Compact)}"));

    private static string Unknown(V2pnControlRequest r) =>
        r.Text.T($"V2pn does not know the command {r.Command}.", $"V2pn не знает команду {r.Command}.");

    private async Task<V2pnControlAnswer> RunAsync(V2pnControlRequest r)
    {
        // The words of the answer: Russian when v2pnctl asked for them, English for MCP and older v2pnctl.
        var t = r.Text;
        var level = app.Level;
        if (level == EV2pnControlLevel.Off)
        {
            return V2pnControlAnswer.Failure(r.Id, V2pnControlProtocol.ErrDisabled, DisabledMessage(t));
        }
        if (!V2pnControlProtocol.IsKnown(r.Command))
        {
            return V2pnControlAnswer.Failure(r.Id, V2pnControlProtocol.ErrUnknownCommand, Unknown(r));
        }
        if (V2pnControlProtocol.ControlCommands.Contains(r.Command) && level != EV2pnControlLevel.Control)
        {
            return V2pnControlAnswer.Failure(r.Id, V2pnControlProtocol.ErrForbidden, ReadOnlyMessage(t));
        }
        int? within = null;
        if (r.Args["confirmWithin"] is { } node)
        {
            if (!V2pnControlProtocol.RevertibleCommands.Contains(r.Command) || V2pnControlJson.Long(node) is not { } seconds
                || seconds < V2pnControlProtocol.MinConfirmWithin || seconds > V2pnControlProtocol.MaxConfirmWithin)
            {
                return BadRequest(r, t.T(
                    $"confirmWithin is a number of seconds from {V2pnControlProtocol.MinConfirmWithin} to {V2pnControlProtocol.MaxConfirmWithin}, for connect, disconnect, use, mode and route.",
                    $"confirmWithin — число секунд от {V2pnControlProtocol.MinConfirmWithin} до {V2pnControlProtocol.MaxConfirmWithin}, только для connect, disconnect, use, mode и route."));
            }
            within = (int)seconds;
        }

        switch (r.Command)
        {
            case V2pnControlProtocol.Status:
                return V2pnControlAnswer.Success(r.Id, await StatusAsync());

            case V2pnControlProtocol.Servers:
                var country = V2pnControlJson.String(r.Args["country"])?.Trim().ToUpperInvariant();
                return V2pnControlAnswer.Success(r.Id, await app.ServersAsync(country is { Length: > 0 } ? country : null));

            case V2pnControlProtocol.Subs:
                return V2pnControlAnswer.Success(r.Id, await app.SubsAsync());

            case V2pnControlProtocol.Diag:
                return V2pnControlAnswer.Success(r.Id, new JsonObject { ["text"] = await app.DiagnosticsAsync() });

            case V2pnControlProtocol.Open:
                await app.ShowWindowAsync();
                return V2pnControlAnswer.Success(r.Id, new JsonObject { ["message"] = t.T("V2pn's window is shown.", "Окно V2pn открыто.") });

            case V2pnControlProtocol.Check:
                if (!await app.CheckAsync())
                {
                    return V2pnControlAnswer.Failure(r.Id, V2pnControlProtocol.ErrNotConnected,
                        t.T("V2pn is not connected, so there is nothing to check.", "V2pn не подключён, проверять нечего."));
                }
                return V2pnControlAnswer.Success(r.Id, new JsonObject { ["message"] = t.T("Checked.", "Проверено."), ["status"] = await StatusAsync() });

            case V2pnControlProtocol.Confirm:
                return await ConfirmAsync(r);

            case V2pnControlProtocol.Connect:
                return await ChangeAsync(r, within, () => app.ConnectAsync(t), EV2pnControlNotice.Connected, settle: true);

            case V2pnControlProtocol.Disconnect:
                return await ChangeAsync(r, within, () => app.DisconnectAsync(t), EV2pnControlNotice.Disconnected, settle: false);

            case V2pnControlProtocol.Use:
                var fastest = V2pnControlJson.Bool(r.Args["fastest"]) == true;
                var server = V2pnControlJson.String(r.Args["server"])?.Trim();
                if (fastest == server is { Length: > 0 })
                {
                    return BadRequest(r, t.T("use needs either a server (an id or a name) or fastest.", "Для use нужен либо сервер (id или название), либо fastest."));
                }
                var near = V2pnControlJson.String(r.Args["country"])?.Trim().ToUpperInvariant();
                return await ChangeAsync(r, within, () => app.UseAsync(server, fastest, near is { Length: > 0 } ? near : null, t),
                    EV2pnControlNotice.ServerChanged, settle: true);

            case V2pnControlProtocol.Mode:
                var mode = V2pnControlJson.String(r.Args["mode"]);
                if (mode is not ("proxy" or "vpn"))
                {
                    return BadRequest(r, t.T("mode is proxy or vpn.", "mode — это proxy или vpn."));
                }
                return await ChangeAsync(r, within, () => app.SetModeAsync(mode == "vpn", t), EV2pnControlNotice.ModeChanged, settle: true);

            case V2pnControlProtocol.Route:
                var route = V2pnControlJson.String(r.Args["route"]);
                EV2pnPreset? preset = route switch
                {
                    "ru-direct" => EV2pnPreset.RussiaDirect,
                    "blocked-only" => EV2pnPreset.BlockedOnly,
                    "all" => EV2pnPreset.All,
                    _ => null,
                };
                if (preset is not { } chosen)
                {
                    return BadRequest(r, t.T("route is ru-direct, blocked-only or all.", "route — это ru-direct, blocked-only или all."));
                }
                return await ChangeAsync(r, within, () => app.SetRouteAsync(chosen, t), EV2pnControlNotice.RouteChanged, settle: true);
        }
        return V2pnControlAnswer.Failure(r.Id, V2pnControlProtocol.ErrUnknownCommand, Unknown(r));
    }

    private static V2pnControlAnswer BadRequest(V2pnControlRequest r, string message) =>
        V2pnControlAnswer.Failure(r.Id, V2pnControlProtocol.ErrBadRequest, message);

    private async Task<V2pnControlAnswer> ChangeAsync(V2pnControlRequest r, int? within, Func<Task<V2pnControlResult>> change,
        EV2pnControlNotice notice, bool settle)
    {
        V2pnControlResult result;
        var waiting = false;
        await _gate.WaitAsync();
        try
        {
            // Changes waiting together are put back together, to what was there before the first of them.
            var before = _pending?.Before ?? await app.CaptureAsync();
            result = await change();
            if (!result.Ok)
            {
                return V2pnControlAnswer.Failure(r.Id, result.Code ?? V2pnControlProtocol.ErrFailed, result.Message);
            }
            // A change that changed nothing has nothing to put back, unless an earlier one waits.
            if (within is { } seconds && (result.Changed || _pending != null))
            {
                _pending = new Pending(before, await app.CaptureAsync(), utcNow().AddSeconds(seconds), r.Source);
                waiting = true;
            }
            if (result.Changed)
            {
                app.Announce(notice, r.Source, result.Detail);
            }
        }
        finally
        {
            _gate.Release();
        }

        var data = new JsonObject { ["message"] = result.Message };
        if (waiting && within is { } wait)
        {
            data["confirmWithin"] = wait;
            data["message"] = ConfirmNote(result.Message, wait, r.Source, r.Text);
        }
        data["status"] = settle && result.Changed ? await SettledStatusAsync() : await StatusAsync();
        return V2pnControlAnswer.Success(r.Id, data);
    }

    private async Task<V2pnControlAnswer> ConfirmAsync(V2pnControlRequest r)
    {
        bool had;
        await _gate.WaitAsync();
        try
        {
            had = _pending != null;
            _pending = null;
        }
        finally
        {
            _gate.Release();
        }
        return V2pnControlAnswer.Success(r.Id, new JsonObject
        {
            ["confirmed"] = had,
            ["message"] = had
                ? r.Text.T("Kept: V2pn will not put the change back.", "Изменение оставлено: V2pn не будет его возвращать.")
                : r.Text.T("Nothing was waiting for a confirmation.", "Подтверждать нечего."),
        });
    }

    /// <summary>Puts back a change whose time ran out. Called every second or so.</summary>
    public async Task TickAsync()
    {
        if (_pending is not { } waiting || utcNow() < waiting.Deadline)
        {
            return;
        }
        await _gate.WaitAsync();
        try
        {
            if (_pending is not { } due || utcNow() < due.Deadline)
            {
                return;
            }
            _pending = null;
            var now = await app.CaptureAsync();
            if (now != due.After)
            {
                log("V2pn control: a change was not confirmed, but the connection was changed again since; it is left as it is");
                return;
            }
            log($"V2pn control: a change from {due.Source} was not confirmed in time; putting back the connection as it was");
            await app.RestoreAsync(due.Before);
            app.Announce(EV2pnControlNotice.Reverted, due.Source, null);
        }
        catch (Exception ex)
        {
            log($"V2pn control: putting the connection back failed: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<JsonObject> StatusAsync()
    {
        var status = await app.StatusAsync();
        if (_pending is { } waiting)
        {
            var left = Math.Max(0, (int)Math.Ceiling((waiting.Deadline - utcNow()).TotalSeconds));
            status["pendingConfirm"] = new JsonObject { ["secondsLeft"] = left };
        }
        return status;
    }

    // A change answers once the connection shows its result: the core up and the check done, or a failure.
    private async Task<JsonObject> SettledStatusAsync()
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            var status = await StatusAsync();
            if (V2pnControlJson.Bool(status["settled"]) != false || waited.Elapsed >= SettleTimeout)
            {
                return status;
            }
            await Task.Delay(SettlePoll);
        }
    }
}
