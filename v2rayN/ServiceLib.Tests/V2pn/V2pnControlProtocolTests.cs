namespace ServiceLib.Tests.V2pn;

/// <summary>The lines of the control channel, the address of a copy of V2pn, and v2pnctl's words.</summary>
public class V2pnControlProtocolTests
{
    [Test]
    public async Task ARequestReadsBackAsItWasWritten()
    {
        var line = new V2pnControlRequest(7, "route", new JsonObject { ["route"] = "all", ["confirmWithin"] = 20 }, "mcp").ToLine();

        var request = V2pnControlRequest.Parse(line, out var refusal);

        await refusal.Should().BeNull();
        await request!.Id.Should().BeEqualTo(7L);
        await request.Command.Should().BeEqualTo("route");
        await request.Source.Should().BeEqualTo("mcp");
        await V2pnControlJson.String(request.Args["route"]).Should().BeEqualTo("all");
        await V2pnControlJson.Long(request.Args["confirmWithin"]).Should().BeEqualTo(20L);
        await line.Should().Contain("\"v\":1");
        await line.Should().NotContain("\n");
    }

    [Test]
    public async Task ARequestOfAnotherVersionIsRefusedWithAReason()
    {
        V2pnControlRequest.Parse("""{"v":2,"id":3,"cmd":"status"}""", out var refusal);

        await refusal!.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrVersion);
        await refusal.Id.Should().BeEqualTo(3L);
        await refusal.ErrorMessage!.Should().Contain("version 2");
    }

    [Test]
    public async Task NotJsonOrNoCommandIsABadRequest()
    {
        V2pnControlRequest.Parse("status please", out var notJson);
        V2pnControlRequest.Parse("""{"v":1,"id":1}""", out var noCommand);
        V2pnControlRequest.Parse("""{"v":1,"id":1,"cmd":"use","args":[1]}""", out var badArgs);

        await notJson!.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrBadRequest);
        await noCommand!.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrBadRequest);
        await badArgs!.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrBadRequest);
    }

    [Test]
    public async Task AnUnknownSourceCountsAsTheTerminal()
    {
        var request = V2pnControlRequest.Parse("""{"v":1,"id":1,"cmd":"status","src":"evil"}""", out _);

        await request!.Source.Should().BeEqualTo(V2pnControlProtocol.SourceCli);
    }

    [Test]
    public async Task AnAnswerReadsBack_AndAnotherVersionIsAFailure()
    {
        var ok = V2pnControlAnswer.Parse(V2pnControlAnswer.Success(4, new JsonObject { ["message"] = "hi" }).ToLine(), 4);
        var failed = V2pnControlAnswer.Parse(V2pnControlAnswer.Failure(4, "disabled", "off").ToLine(), 4);
        var future = V2pnControlAnswer.Parse("""{"v":9,"id":4,"ok":true,"data":{}}""", 4);
        var garbage = V2pnControlAnswer.Parse("<html>", 4);

        await ok.Ok.Should().BeTrue();
        await V2pnControlJson.String(ok.Data?["message"]).Should().BeEqualTo("hi");
        await failed.ErrorCode.Should().BeEqualTo("disabled");
        await failed.ErrorMessage.Should().BeEqualTo("off");
        await future.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrVersion);
        await garbage.Ok.Should().BeFalse();
    }

    [Test]
    public async Task ExitCodesSayWhatHappened()
    {
        static int Code(string? error) => V2pnControlProtocol.ExitCode(error == null ? V2pnControlAnswer.Success(1, null) : V2pnControlAnswer.Failure(1, error, ""));

        await Code(null).Should().BeEqualTo(0);
        await Code(V2pnControlProtocol.ErrFailed).Should().BeEqualTo(1);
        await Code(V2pnControlProtocol.ErrNeedsConfirmation).Should().BeEqualTo(2);
        await Code(V2pnControlProtocol.ErrNotRunning).Should().BeEqualTo(3);
        await Code(V2pnControlProtocol.ErrDisabled).Should().BeEqualTo(4);
        await Code(V2pnControlProtocol.ErrForbidden).Should().BeEqualTo(4);
    }

    [Test]
    public async Task OneCopyHasOneAddress_AnotherCopyAnother()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2pn-ctl-key");
        var copy = Path.Combine(root, "a");

        await V2pnControlAddress.Key(copy + Path.DirectorySeparatorChar).Should().BeEqualTo(V2pnControlAddress.Key(copy));
        await V2pnControlAddress.Key(copy).Should().NotBeEqualTo(V2pnControlAddress.Key(Path.Combine(root, "b")));
        if (OperatingSystem.IsWindows())
        {
            await V2pnControlAddress.Key(copy.ToUpperInvariant()).Should().BeEqualTo(V2pnControlAddress.Key(copy));
            await V2pnControlEndpoint.For(copy).PipeName!.Should().StartWith("V2pn.control.");
        }
        else
        {
            // A Mac allows 104 bytes for a socket's path.
            await (Encoding.UTF8.GetByteCount(V2pnControlEndpoint.For(copy).SocketPath!) < 104).Should().BeTrue();
        }
    }

    [Test]
    public async Task TheCopyIsFoundFromItsFolderOrItsProgram()
    {
        var folder = Path.Combine(Path.GetTempPath(), "v2pn-ctl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var exe = Path.Combine(folder, "V2pn.exe");
                await File.WriteAllTextAsync(exe, "");
                await V2pnControlAddress.ResolveFolder(exe, "C:\\elsewhere").Should().BeEqualTo(folder);
                await V2pnControlAddress.ResolveFolder(folder, "C:\\elsewhere").Should().BeEqualTo(folder);
                // Without a choice: the copy v2pnctl sits in.
                await V2pnControlAddress.ResolveFolder(null, folder).Should().BeEqualTo(folder);
                await V2pnControlAddress.ResolveFolder(null, Path.Combine(folder, "none")).Should()
                    .BeEqualTo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "V2pn"));
            }
            else
            {
                var macos = Path.Combine(folder, "V2pn.app", "Contents", "MacOS");
                Directory.CreateDirectory(macos);
                await V2pnControlAddress.ResolveFolder(folder, "/x").Should().BeEqualTo(macos);
                await V2pnControlAddress.ResolveFolder(Path.Combine(folder, "V2pn.app"), "/x").Should().BeEqualTo(macos);
            }
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static V2pnCtlCommandLine Parse(string text, out string? error) =>
        V2pnControlCli.Parse(text.Split(' ', StringSplitOptions.RemoveEmptyEntries), out error);

    [Test]
    public async Task TheCommandLineBecomesOneRequest()
    {
        var route = Parse("--instance C:\\sandbox --json route all --confirm-within 20", out var e1);
        var use = Parse("use Frankfurt 2", out var e2);
        var fastest = Parse("use --fastest --country de", out var e3);
        var servers = Parse("servers --country=nl", out var e4);
        var mode = Parse("mode VPN", out var e5);

        await e1.Should().BeNull();
        await route.Instance.Should().BeEqualTo("C:\\sandbox");
        await route.Json.Should().BeTrue();
        await route.Command.Should().BeEqualTo("route");
        await V2pnControlJson.String(route.Args["route"]).Should().BeEqualTo("all");
        await V2pnControlJson.Long(route.Args["confirmWithin"]).Should().BeEqualTo(20L);
        await e2.Should().BeNull();
        await V2pnControlJson.String(use.Args["server"]).Should().BeEqualTo("Frankfurt 2");
        await e3.Should().BeNull();
        await V2pnControlJson.Bool(fastest.Args["fastest"]).Should().BeEqualTo(true);
        await V2pnControlJson.String(fastest.Args["country"]).Should().BeEqualTo("DE");
        await e4.Should().BeNull();
        await V2pnControlJson.String(servers.Args["country"]).Should().BeEqualTo("NL");
        await e5.Should().BeNull();
        await V2pnControlJson.String(mode.Args["mode"]).Should().BeEqualTo("vpn");
    }

    [Test]
    public async Task AWrongCommandLineSaysWhy()
    {
        foreach (var text in new[]
                 {
                     "route everywhere", "mode tunnel", "use", "use x --fastest", "status --confirm-within 30",
                     "disconnect --confirm-within 2", "disconnect --confirm-within soon", "servers --country Germany",
                     "connect now", "teleport", "status --verbose", "--instance",
                 })
        {
            Parse(text, out var error);
            await error.Should().NotBeNull();
        }
    }

    [Test]
    public async Task NoCommandOrHelpShowsTheUsage()
    {
        var none = Parse("", out var e1);
        var help = Parse("status --help", out var e2);

        await e1.Should().BeNull();
        await none.Command.Should().BeEqualTo(V2pnControlCli.Help);
        await e2.Should().BeNull();
        await help.Command.Should().BeEqualTo(V2pnControlCli.Help);
        await V2pnControlCli.Usage.Should().Contain("--confirm-within");
    }

    [Test]
    public async Task TheStatusReadsAsAFewLines()
    {
        var status = new JsonObject
        {
            ["verdict"] = "vpn-connected",
            ["mode"] = "vpn",
            ["server"] = new JsonObject { ["name"] = "Frankfurt 1", ["country"] = "DE" },
            ["route"] = "ru-direct",
            ["check"] = new JsonObject { ["state"] = "Available", ["ms"] = 120, ["at"] = "2026-11-24T10:00:00Z" },
            ["pendingConfirm"] = new JsonObject { ["secondsLeft"] = 42 },
        };

        var text = V2pnControlCli.Format("status", V2pnControlAnswer.Success(1, status), new DateTime(2026, 11, 24, 10, 0, 30, DateTimeKind.Utc));

        await text.Should().Contain("Connected in VPN mode");
        await text.Should().Contain("Server: Frankfurt 1 (DE)");
        await text.Should().Contain("Russian sites directly");
        await text.Should().Contain("Check: Available, 120 ms, 30 s ago");
        await text.Should().Contain("42 s left");
    }
}
