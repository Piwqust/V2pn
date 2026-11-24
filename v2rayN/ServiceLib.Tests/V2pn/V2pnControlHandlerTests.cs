namespace ServiceLib.Tests.V2pn;

/// <summary>The setting's levels, the log, the toasts, and a change put back when it is not confirmed in time.</summary>
public class V2pnControlHandlerTests
{
    internal sealed class FakeApp : IV2pnControlApp
    {
        public EV2pnControlLevel Level { get; set; } = EV2pnControlLevel.Control;
        public V2pnControlState State { get; set; } = new(Connected: true, Tun: false, Server: "s1", Route: "v2pn-all");
        public List<V2pnControlState> Restored { get; } = [];
        public List<(EV2pnControlNotice Notice, string Source, string? Detail)> Notices { get; } = [];
        public int Checks;
        public int Shown;
        public V2pnControlResult? Next;

        public Task<JsonObject> StatusAsync() => Task.FromResult(new JsonObject
        {
            ["connected"] = State.Connected,
            ["verdict"] = State.Connected ? "proxy-connected" : "off",
            ["mode"] = State.Tun ? "vpn" : "proxy",
            ["route"] = V2pnControlData.RouteName(State.Route),
            ["settled"] = true,
        });

        public Task<JsonArray> ServersAsync(string? country) => Task.FromResult(new JsonArray(new JsonObject { ["id"] = "s1", ["country"] = country }));

        public Task<JsonArray> SubsAsync() => Task.FromResult(new JsonArray());

        public Task<string> DiagnosticsAsync() => Task.FromResult("V2pn diagnostics");

        public Task<bool> CheckAsync()
        {
            Checks++;
            return Task.FromResult(State.Connected);
        }

        public Task ShowWindowAsync()
        {
            Shown++;
            return Task.CompletedTask;
        }

        private Task<V2pnControlResult> Change(V2pnControlState after, string message, string? detail = null)
        {
            if (Next is { } next)
            {
                Next = null;
                return Task.FromResult(next);
            }
            if (after == State)
            {
                return Task.FromResult(V2pnControlResult.Already("Already."));
            }
            State = after;
            return Task.FromResult(V2pnControlResult.Done(message, detail));
        }

        public Task<V2pnControlResult> ConnectAsync() => Change(State with { Connected = true }, "Connecting.");

        public Task<V2pnControlResult> DisconnectAsync() => Change(State with { Connected = false }, "Disconnected.");

        public Task<V2pnControlResult> UseAsync(string? server, bool fastest, string? country) =>
            Change(State with { Server = fastest ? "fastest" : server }, "Switched.", server);

        public Task<V2pnControlResult> SetModeAsync(bool tun) => Change(State with { Tun = tun }, "Mode.", tun ? "vpn" : "proxy");

        public Task<V2pnControlResult> SetRouteAsync(EV2pnPreset preset) =>
            Change(State with { Route = V2pnControlData.RouteId(preset) }, "Route.", V2pnControlData.RouteName(V2pnControlData.RouteId(preset)));

        public Task<V2pnControlState> CaptureAsync() => Task.FromResult(State);

        public Task RestoreAsync(V2pnControlState state)
        {
            Restored.Add(state);
            State = state;
            return Task.CompletedTask;
        }

        public void Announce(EV2pnControlNotice notice, string source, string? detail) => Notices.Add((notice, source, detail));
    }

    internal sealed class Clock
    {
        public DateTime Now { get; set; } = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

        public void Advance(int seconds) => Now = Now.AddSeconds(seconds);
    }

    private static (V2pnControlHandler Handler, FakeApp App, Clock Clock, List<string> Log) Make(EV2pnControlLevel level = EV2pnControlLevel.Control)
    {
        var app = new FakeApp { Level = level };
        var clock = new Clock();
        var log = new List<string>();
        return (new V2pnControlHandler(app, () => clock.Now, log.Add), app, clock, log);
    }

    private static Task<V2pnControlAnswer> Send(V2pnControlHandler handler, string command, JsonObject? args = null, string source = "cli") =>
        handler.HandleAsync(new V2pnControlRequest(1, command, args ?? new JsonObject(), source));

    [Test]
    public async Task Off_AnswersEveryCommandWithHowToTurnItOn()
    {
        var (handler, app, _, _) = Make(EV2pnControlLevel.Off);

        foreach (var command in new[] { "status", "servers", "connect", "disconnect", "confirm" })
        {
            var answer = await Send(handler, command);
            await answer.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrDisabled);
            await answer.ErrorMessage!.Should().Contain("Settings > General");
            await V2pnControlProtocol.ExitCode(answer).Should().BeEqualTo(4);
        }
        await app.State.Connected.Should().BeTrue();
    }

    [Test]
    public async Task ReadOnly_ReadsButChangesNothing()
    {
        var (handler, app, _, _) = Make(EV2pnControlLevel.ReadOnly);

        var status = await Send(handler, "status");
        var servers = await Send(handler, "servers", new JsonObject { ["country"] = "de" });
        var check = await Send(handler, "check");
        var disconnect = await Send(handler, "disconnect");
        var confirm = await Send(handler, "confirm");

        await status.Ok.Should().BeTrue();
        await V2pnControlJson.String(servers.Data?[0]?["country"]).Should().BeEqualTo("DE");
        await check.Ok.Should().BeTrue();
        await app.Checks.Should().BeEqualTo(1);
        await disconnect.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrForbidden);
        await confirm.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrForbidden);
        await app.State.Connected.Should().BeTrue();
    }

    [Test]
    public async Task EveryCommandIsLoggedWithItsSource_AndAChangeIsAnnounced()
    {
        var (handler, app, _, log) = Make();

        await Send(handler, "disconnect", source: "mcp");
        await Send(handler, "status");

        await log.Any(l => l.Contains("mcp disconnect") && l.Contains("done")).Should().BeTrue();
        await log.Any(l => l.Contains("cli status")).Should().BeTrue();
        await app.Notices.Count.Should().BeEqualTo(1);
        await app.Notices[0].Notice.Should().BeEqualTo(EV2pnControlNotice.Disconnected);
        await app.Notices[0].Source.Should().BeEqualTo("mcp");
    }

    [Test]
    public async Task NothingChanged_NothingIsAnnounced()
    {
        var (handler, app, _, _) = Make();

        var answer = await Send(handler, "connect");

        await answer.Ok.Should().BeTrue();
        await app.Notices.Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task AFailedChangeKeepsItsCode()
    {
        var (handler, app, _, _) = Make();
        app.State = app.State with { Connected = false };
        app.Next = V2pnControlResult.Fail(V2pnControlProtocol.ErrNeedsConfirmation, "VPN mode needs administrator rights.");

        var answer = await Send(handler, "connect", new JsonObject { ["confirmWithin"] = 30 });

        await answer.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrNeedsConfirmation);
        await V2pnControlProtocol.ExitCode(answer).Should().BeEqualTo(2);
        await handler.Waiting.Should().BeFalse();
    }

    [Test]
    public async Task AnUnconfirmedChange_IsPutBackWhenItsTimeRunsOut()
    {
        var (handler, app, clock, _) = Make();
        var before = app.State;

        var answer = await Send(handler, "disconnect", new JsonObject { ["confirmWithin"] = 20 }, "mcp");
        await answer.Ok.Should().BeTrue();
        await V2pnControlJson.Long(answer.Data?["confirmWithin"]).Should().BeEqualTo(20L);
        await V2pnControlJson.Long(answer.Data?["status"]?["pendingConfirm"]?["secondsLeft"]).Should().BeEqualTo(20L);

        clock.Advance(19);
        await handler.TickAsync();
        await app.Restored.Count.Should().BeEqualTo(0);
        await app.State.Connected.Should().BeFalse();

        clock.Advance(2);
        await handler.TickAsync();
        await app.Restored.Count.Should().BeEqualTo(1);
        await app.State.Should().BeEqualTo(before);
        await app.Notices[^1].Notice.Should().BeEqualTo(EV2pnControlNotice.Reverted);
        await handler.Waiting.Should().BeFalse();
    }

    [Test]
    public async Task AConfirmedChange_Stays()
    {
        var (handler, app, clock, _) = Make();

        await Send(handler, "use", new JsonObject { ["server"] = "s2", ["confirmWithin"] = 10 });
        var confirm = await Send(handler, "confirm");
        clock.Advance(60);
        await handler.TickAsync();

        await V2pnControlJson.Bool(confirm.Data?["confirmed"]).Should().BeEqualTo(true);
        await app.Restored.Count.Should().BeEqualTo(0);
        await app.State.Server.Should().BeEqualTo("s2");
    }

    [Test]
    public async Task ConfirmWithNothingWaiting_SaysSo()
    {
        var (handler, _, _, _) = Make();

        var answer = await Send(handler, "confirm");

        await answer.Ok.Should().BeTrue();
        await V2pnControlJson.Bool(answer.Data?["confirmed"]).Should().BeEqualTo(false);
    }

    [Test]
    public async Task ChangesWaitingTogether_GoBackToBeforeTheFirst()
    {
        var (handler, app, clock, _) = Make();
        var before = app.State;

        await Send(handler, "route", new JsonObject { ["route"] = "ru-direct", ["confirmWithin"] = 30 });
        clock.Advance(20);
        await Send(handler, "mode", new JsonObject { ["mode"] = "vpn", ["confirmWithin"] = 30 });
        clock.Advance(20);
        await handler.TickAsync();
        await app.Restored.Count.Should().BeEqualTo(0);

        clock.Advance(11);
        await handler.TickAsync();

        await app.Restored.Count.Should().BeEqualTo(1);
        await app.State.Should().BeEqualTo(before);
    }

    [Test]
    public async Task AChangeMadeSince_IsLeftAlone()
    {
        var (handler, app, clock, log) = Make();

        await Send(handler, "disconnect", new JsonObject { ["confirmWithin"] = 15 });
        // The person connects again in the window, to another server.
        app.State = app.State with { Connected = true, Server = "s9" };
        clock.Advance(16);
        await handler.TickAsync();

        await app.Restored.Count.Should().BeEqualTo(0);
        await app.State.Server.Should().BeEqualTo("s9");
        await log.Any(l => l.Contains("left as it is")).Should().BeTrue();
    }

    [Test]
    public async Task ConfirmWithin_IsChecked()
    {
        var (handler, _, _, _) = Make();

        var tooShort = await Send(handler, "disconnect", new JsonObject { ["confirmWithin"] = 1 });
        var notANumber = await Send(handler, "disconnect", new JsonObject { ["confirmWithin"] = "soon" });
        var onARead = await Send(handler, "status", new JsonObject { ["confirmWithin"] = 30 });

        await tooShort.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrBadRequest);
        await notANumber.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrBadRequest);
        await onARead.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrBadRequest);
    }

    [Test]
    public async Task WrongArguments_AndUnknownCommands_AreRefused()
    {
        var (handler, _, _, _) = Make();

        await (await Send(handler, "route", new JsonObject { ["route"] = "moon" })).ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrBadRequest);
        await (await Send(handler, "mode", new JsonObject { ["mode"] = "tunnel" })).ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrBadRequest);
        await (await Send(handler, "use")).ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrBadRequest);
        await (await Send(handler, "sub-add")).ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrUnknownCommand);
    }

    [Test]
    public async Task ABrokenLine_GetsAnAnswerToo()
    {
        var (handler, _, _, _) = Make();

        var answer = V2pnControlAnswer.Parse(await handler.HandleLineAsync("{not json"), 0);

        await answer.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrBadRequest);
    }

    [Test]
    public async Task ReadsAnswerWithWhatTheAppSays()
    {
        var (handler, app, _, _) = Make();

        var diag = await Send(handler, "diag");
        var open = await Send(handler, "open");
        app.State = app.State with { Connected = false };
        var check = await Send(handler, "check");

        await V2pnControlJson.String(diag.Data?["text"]).Should().BeEqualTo("V2pn diagnostics");
        await app.Shown.Should().BeEqualTo(1);
        await open.Ok.Should().BeTrue();
        await check.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrNotConnected);
    }
}
