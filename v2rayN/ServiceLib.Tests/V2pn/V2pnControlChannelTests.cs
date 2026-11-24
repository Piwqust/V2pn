using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using static ServiceLib.Tests.V2pn.V2pnControlHandlerTests;

namespace ServiceLib.Tests.V2pn;

/// <summary>
/// The channel end to end in this process (the pipe on Windows, the socket elsewhere), who may open it, v2pnctl's way
/// to a V2pn that is not running, and the MCP server over all of it.
/// </summary>
public class V2pnControlChannelTests
{
    private static V2pnControlEndpoint TestEndpoint()
    {
        var name = "test-" + Guid.NewGuid().ToString("N")[..12];
        return OperatingSystem.IsWindows()
            ? new V2pnControlEndpoint($"V2pn.control.{name}", null)
            : new V2pnControlEndpoint(null, Path.Combine(Path.GetTempPath(), $"v2pn-{name}.sock"));
    }

    private static (V2pnControlServer Server, V2pnControlHandler Handler, FakeApp App) Serve(V2pnControlEndpoint endpoint)
    {
        var app = new FakeApp();
        var handler = new V2pnControlHandler(app, () => DateTime.UtcNow, _ => { });
        var server = V2pnControlServer.Start(endpoint, handler.HandleLineAsync, _ => { })
                     ?? throw new InvalidOperationException("the server did not start");
        return (server, handler, app);
    }

    [Test]
    public async Task ARequestCrossesTheChannelAndItsAnswerComesBack()
    {
        var endpoint = TestEndpoint();
        var (server, _, app) = Serve(endpoint);
        using (server)
        {
            var request = new V2pnControlRequest(5, "disconnect", new JsonObject(), "cli");
            var line = await V2pnControlClient.ExchangeAsync(endpoint, request.ToLine(), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
            var answer = V2pnControlAnswer.Parse(line!, 5);

            await answer.Ok.Should().BeTrue();
            await answer.Id.Should().BeEqualTo(5L);
            await app.State.Connected.Should().BeFalse();

            // One connection, several requests.
            var pairs = await TwoOnOneConnectionAsync(endpoint);
            await pairs.Should().BeEqualTo(2);
        }
    }

    private static async Task<int> TwoOnOneConnectionAsync(V2pnControlEndpoint endpoint)
    {
        Stream stream;
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", endpoint.PipeName!, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(5000);
            stream = pipe;
        }
        else
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint.SocketPath!));
            stream = new NetworkStream(socket, ownsSocket: true);
        }
        await using (stream)
        {
            var reader = new V2pnControlLineReader(stream, V2pnControlProtocol.MaxLineBytes);
            var answered = 0;
            foreach (var id in new[] { 1, 2 })
            {
                await V2pnControlLineReader.WriteLineAsync(stream, new V2pnControlRequest(id, "status", new JsonObject(), "cli").ToLine(), CancellationToken.None);
                var line = await reader.ReadLineAsync(new CancellationTokenSource(5000).Token);
                if (V2pnControlAnswer.Parse(line!, id) is { Ok: true } answer && answer.Id == id)
                {
                    answered++;
                }
            }
            return answered;
        }
    }

    [Test]
    public async Task OnlyThisUserMayOpenThePipe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var endpoint = TestEndpoint();
        var (server, _, _) = Serve(endpoint);
        using (server)
        {
            await using var pipe = new NamedPipeClientStream(".", endpoint.PipeName!, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(5000);
            var security = pipe.GetAccessControl();
            using var me = WindowsIdentity.GetCurrent();
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();

            await (security.GetOwner(typeof(SecurityIdentifier)) == me.User).Should().BeTrue();
            await security.AreAccessRulesProtected.Should().BeTrue();
            await rules.Count.Should().BeEqualTo(1);
            await (rules[0].IdentityReference == me.User).Should().BeTrue();
            await rules[0].AccessControlType.Should().BeEqualTo(AccessControlType.Allow);
            // Everyone, which a default pipe lets read, is not there.
            var world = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var everyone = false;
            foreach (var rule in rules)
            {
                everyone |= rule.IdentityReference == world;
            }
            await everyone.Should().BeFalse();
        }
    }

    [Test]
    public async Task ANameAlreadyTaken_IsNotTakenOver()
    {
        var endpoint = TestEndpoint();
        var (server, _, _) = Serve(endpoint);
        using (server)
        {
            var second = V2pnControlServer.Start(endpoint, _ => Task.FromResult(""), _ => { });

            await second.Should().BeNull();
        }
    }

    [Test]
    public async Task NobodyListening_IsNotAnError()
    {
        var line = await V2pnControlClient.ExchangeAsync(TestEndpoint(), "{}", TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1));

        await line.Should().BeNull();
    }

    [Test]
    public async Task V2pnNotRunning_IsStarted_AndWhenItCannotBe_TheCodeSaysSo()
    {
        var endpoint = TestEndpoint();
        var started = 0;
        var cannot = new V2pnControlLink(endpoint, () => { started++; return false; }) { Folder = "nowhere" };

        var answer = await cannot.CallAsync("status", new JsonObject(), "cli");

        await started.Should().BeEqualTo(1);
        await answer.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrNotRunning);
        await V2pnControlProtocol.ExitCode(answer).Should().BeEqualTo(3);

        // "Started": the channel opens a moment later, and the command goes through once.
        V2pnControlServer? server = null;
        var link = new V2pnControlLink(endpoint, () =>
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(600);
                server = Serve(endpoint).Server;
            });
            return true;
        }) { StartWait = TimeSpan.FromSeconds(10) };
        try
        {
            var late = await link.CallAsync("status", new JsonObject(), "cli");
            await late.Ok.Should().BeTrue();
        }
        finally
        {
            server?.Dispose();
        }
    }

    [Test]
    public async Task TheLanguageCrossesTheChannel_AndNotRunningIsSaidInItToo()
    {
        var endpoint = TestEndpoint();
        var (server, _, app) = Serve(endpoint);
        using (server)
        {
            var link = new V2pnControlLink(endpoint, () => false);

            var ru = await link.CallAsync("disconnect", new JsonObject(), V2pnControlProtocol.SourceCli, V2pnControlText.Russian);
            var en = await link.CallAsync("connect", new JsonObject(), V2pnControlProtocol.SourceCli);

            await V2pnControlJson.String(ru.Data?["message"]).Should().BeEqualTo("Отключено.");
            await V2pnControlJson.String(en.Data?["message"]).Should().BeEqualTo("Connecting.");
            await app.AskedIn.Should().BeEqualTo(V2pnControlText.English);
        }

        var nowhere = new V2pnControlLink(TestEndpoint(), () => false) { Folder = "C:\\nowhere" };
        var notRunning = await nowhere.CallAsync("status", new JsonObject(), V2pnControlProtocol.SourceCli, V2pnControlText.Russian);
        await notRunning.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrNotRunning);
        await notRunning.ErrorMessage.Should().BeEqualTo("V2pn не запущен, и в папке C:\\nowhere нет V2pn, чтобы его запустить.");
    }

    [Test]
    public async Task ALineTooLong_IsNotRead()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 100_000) + "\n"));
        var reader = new V2pnControlLineReader(stream, 50_000);

        var refused = false;
        try
        {
            await reader.ReadLineAsync(CancellationToken.None);
        }
        catch (InvalidDataException)
        {
            refused = true;
        }

        await refused.Should().BeTrue();
    }

    private static (V2pnControlMcp Mcp, FakeApp App, List<(string Command, JsonObject Args)> Calls) Mcp(EV2pnControlLevel level = EV2pnControlLevel.Control)
    {
        var app = new FakeApp { Level = level };
        var handler = new V2pnControlHandler(app, () => DateTime.UtcNow, _ => { });
        var calls = new List<(string, JsonObject)>();
        var mcp = new V2pnControlMcp((command, args) =>
        {
            calls.Add((command, args));
            return handler.HandleAsync(new V2pnControlRequest(1, command, args, V2pnControlProtocol.SourceMcp));
        }, "1.2.3");
        return (mcp, app, calls);
    }

    private static async Task<JsonObject> Rpc(V2pnControlMcp mcp, string json) => (JsonObject)JsonNode.Parse((await mcp.HandleLineAsync(json))!)!;

    [Test]
    public async Task Mcp_Initialize_AgreesOnAVersion()
    {
        var (mcp, _, _) = Mcp();

        var known = await Rpc(mcp, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""");
        await V2pnControlJson.String(known["result"]?["protocolVersion"]).Should().BeEqualTo("2025-03-26");
        await V2pnControlJson.String(known["result"]?["serverInfo"]?["name"]).Should().BeEqualTo("v2pn");
        await V2pnControlJson.String(known["result"]?["instructions"])!.Should().Contain("v2pn_confirm");
        await (known["result"]?["capabilities"]?["tools"] != null).Should().BeTrue();

        var unknown = await Rpc(mcp, """{"jsonrpc":"2.0","id":"a","method":"initialize","params":{"protocolVersion":"2099-01-01"}}""");
        await V2pnControlJson.String(unknown["result"]?["protocolVersion"]).Should().BeEqualTo(V2pnControlMcp.ProtocolVersions[0]);
        await V2pnControlJson.String(unknown["id"]).Should().BeEqualTo("a");

        await (await mcp.HandleLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""")).Should().BeNull();
        var ping = await Rpc(mcp, """{"jsonrpc":"2.0","id":2,"method":"ping"}""");
        await (ping["result"] is JsonObject).Should().BeTrue();
    }

    [Test]
    public async Task Mcp_ToolsList_MarksWhatChangesThings()
    {
        var (mcp, _, _) = Mcp();

        var list = await Rpc(mcp, """{"jsonrpc":"2.0","id":3,"method":"tools/list"}""");
        var tools = (JsonArray)list["result"]!["tools"]!;
        JsonObject Tool(string name) => tools.OfType<JsonObject>().First(t => V2pnControlJson.String(t["name"]) == name);

        await tools.Count.Should().BeEqualTo(11);
        await V2pnControlJson.Bool(Tool("v2pn_status")["annotations"]?["readOnlyHint"]).Should().BeEqualTo(true);
        await V2pnControlJson.Bool(Tool("v2pn_disconnect")["annotations"]?["destructiveHint"]).Should().BeEqualTo(true);
        await V2pnControlJson.Bool(Tool("v2pn_connect")["annotations"]?["destructiveHint"]).Should().BeEqualTo(false);
        await V2pnControlJson.Bool(Tool("v2pn_connect")["annotations"]?["readOnlyHint"]).Should().BeEqualTo(false);
        await V2pnControlJson.Bool(Tool("v2pn_set_mode")["annotations"]?["idempotentHint"]).Should().BeEqualTo(true);
        await (Tool("v2pn_set_route")["inputSchema"]?["properties"]?["route"]?["enum"] is JsonArray { Count: 3 }).Should().BeTrue();
        // Not offered on purpose.
        await tools.Any(t => V2pnControlJson.String(t?["name"])!.Contains("sub", StringComparison.Ordinal)
                             && V2pnControlJson.String(t?["name"]) != "v2pn_subscriptions").Should().BeFalse();
    }

    [Test]
    public async Task Mcp_AChange_AlwaysAsksForAConfirmation()
    {
        var (mcp, app, calls) = Mcp();
        await Rpc(mcp, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}""");

        var result = await Rpc(mcp, """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"v2pn_disconnect","arguments":{}}}""");

        await calls[^1].Command.Should().BeEqualTo("disconnect");
        await V2pnControlJson.Long(calls[^1].Args["confirmWithin"]).Should().BeEqualTo(60L);
        await V2pnControlJson.Bool(result["result"]?["isError"]).Should().BeEqualTo(false);
        await V2pnControlJson.Long(result["result"]?["structuredContent"]?["confirmWithin"]).Should().BeEqualTo(60L);
        await app.State.Connected.Should().BeFalse();
        await app.Notices[^1].Source.Should().BeEqualTo("mcp");

        var route = await Rpc(mcp, """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"v2pn_set_route","arguments":{"route":"blocked-only"}}}""");
        await V2pnControlJson.String(calls[^1].Args["route"]).Should().BeEqualTo("blocked-only");
        await app.State.Route.Should().BeEqualTo("v2pn-blocked-only");
        await V2pnControlJson.Bool(route["result"]?["isError"]).Should().BeEqualTo(false);

        var confirm = await Rpc(mcp, """{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"v2pn_confirm","arguments":{}}}""");
        await V2pnControlJson.Bool(confirm["result"]?["structuredContent"]?["confirmed"]).Should().BeEqualTo(true);
    }

    [Test]
    public async Task Mcp_AReadGivesItsData_AndOldClientsGetTextAlone()
    {
        var (mcp, _, _) = Mcp();
        await Rpc(mcp, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05"}}""");

        var servers = await Rpc(mcp, """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"v2pn_servers","arguments":{"country":"de"}}}""");
        var diag = await Rpc(mcp, """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"v2pn_diagnostics","arguments":{}}}""");

        await V2pnControlJson.String(servers["result"]?["content"]?[0]?["text"])!.Should().Contain("\"DE\"");
        await (servers["result"]?["structuredContent"] == null).Should().BeTrue();
        await V2pnControlJson.String(diag["result"]?["content"]?[0]?["text"]).Should().BeEqualTo("V2pn diagnostics");
    }

    [Test]
    public async Task Mcp_ARefusal_IsAToolError_AndNonsense_IsAProtocolError()
    {
        var (mcp, app, _) = Mcp(EV2pnControlLevel.ReadOnly);

        var forbidden = await Rpc(mcp, """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"v2pn_disconnect"}}""");
        var badArgs = await Rpc(mcp, """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"v2pn_set_mode","arguments":{"mode":"warp"}}}""");
        var unknownTool = await Rpc(mcp, """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"v2pn_add_subscription","arguments":{}}}""");
        var unknownMethod = await Rpc(mcp, """{"jsonrpc":"2.0","id":4,"method":"resources/list"}""");
        var parse = await Rpc(mcp, "{oops");

        await V2pnControlJson.Bool(forbidden["result"]?["isError"]).Should().BeEqualTo(true);
        await V2pnControlJson.String(forbidden["result"]?["content"]?[0]?["text"])!.Should().Contain("forbidden");
        await app.State.Connected.Should().BeTrue();
        await V2pnControlJson.Bool(badArgs["result"]?["isError"]).Should().BeEqualTo(true);
        await V2pnControlJson.Long(unknownTool["error"]?["code"]).Should().BeEqualTo(-32602L);
        await V2pnControlJson.Long(unknownMethod["error"]?["code"]).Should().BeEqualTo(-32601L);
        await V2pnControlJson.Long(parse["error"]?["code"]).Should().BeEqualTo(-32700L);
    }

    [Test]
    public async Task Mcp_OverTheRealChannel_AndStdio()
    {
        var endpoint = TestEndpoint();
        var (server, _, app) = Serve(endpoint);
        using (server)
        {
            var link = new V2pnControlLink(endpoint, () => false);
            var mcp = new V2pnControlMcp((command, args) => link.CallAsync(command, args, V2pnControlProtocol.SourceMcp), "1.2.3");
            var input = new StringReader(string.Join("\n",
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}""",
                """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
                """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"v2pn_status","arguments":{}}}""",
                """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"v2pn_use_server","arguments":{"server":"s7"}}}""") + "\n");
            var output = new StringWriter();

            await mcp.RunAsync(input, output);
            var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

            await lines.Length.Should().BeEqualTo(3);
            await V2pnControlJson.Bool(JsonNode.Parse(lines[1])?["result"]?["structuredContent"]?["connected"]).Should().BeEqualTo(true);
            await V2pnControlJson.Bool(JsonNode.Parse(lines[2])?["result"]?["isError"]).Should().BeEqualTo(false);
            await app.State.Server.Should().BeEqualTo("s7");
            await app.Notices[^1].Source.Should().BeEqualTo("mcp");
        }
    }
}
