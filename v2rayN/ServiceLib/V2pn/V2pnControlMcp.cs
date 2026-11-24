using System.Text.Json;
using System.Text.Json.Nodes;

namespace ServiceLib;

// V2pn: compiled into ServiceLib (for the tests) and into v2pnctl, like V2pnControlProtocol.cs.

/// <summary>
/// V2pn: "v2pnctl mcp", the commands of v2pnctl as tools of the Model Context Protocol, so an AI assistant (Claude Code,
/// Claude Desktop, Cursor, Codex) can read V2pn's state and change the connection. JSON-RPC 2.0 over standard input and
/// output, one message per line. Only the part of MCP these tools need is here (initialize, ping, tools/list,
/// tools/call), written out instead of taken from an SDK, so v2pnctl stays one small file.
/// <para>
/// Every change a tool makes is asked for with a confirmation window of a minute: an assistant that cut off its own
/// network cannot confirm, and V2pn puts the connection back by itself. Not offered on purpose: adding subscriptions (a
/// way to slip in someone else's server), anything that shows links, addresses or keys, deleting anything.
/// </para>
/// </summary>
public sealed class V2pnControlMcp(Func<string, JsonObject, Task<V2pnControlAnswer>> call, string serverVersion)
{
    /// <summary>The versions of MCP this server speaks, newest first.</summary>
    public static readonly string[] ProtocolVersions = ["2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05"];

    public const string Instructions =
        "V2pn is the VPN client on this computer, and these tools read and change its connection. " +
        "Disconnecting, or switching the server, the mode or the route, may cut off your own network, this conversation included. " +
        "Every change reverts by itself after 60 seconds unless v2pn_confirm is called: after a change, check that you can still reach " +
        "the network, then call v2pn_confirm. If you cannot reach it, do nothing and the connection comes back. " +
        "Server addresses, subscription links and keys are never shown.";

    private const int ParseError = -32700;
    private const int InvalidRequest = -32600;
    private const int MethodNotFound = -32601;
    private const int InvalidParams = -32602;

    private string _protocol = ProtocolVersions[0];

    /// <summary>The version agreed in initialize.</summary>
    public string Protocol => _protocol;

    private sealed record Tool(string Name, string Title, string Description, JsonObject Schema, bool ReadOnly, bool Destructive, bool Idempotent);

    private static JsonObject Schema(params (string Name, JsonObject Schema)[] properties)
    {
        var props = new JsonObject();
        foreach (var (name, schema) in properties)
        {
            props[name] = schema;
        }
        return new JsonObject { ["type"] = "object", ["properties"] = props, ["additionalProperties"] = false };
    }

    private static JsonObject Text(string description) => new() { ["type"] = "string", ["description"] = description };

    private static JsonObject Choice(string description, params string[] values) => new()
    {
        ["type"] = "string",
        ["enum"] = new JsonArray(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()),
        ["description"] = description,
    };

    private static readonly Tool[] All =
    [
        new("v2pn_status", "V2pn status",
            "Whether V2pn is connected now: the verdict, the server's name and country, Proxy or VPN mode, the route and the last check of the connection.",
            Schema(), ReadOnly: true, Destructive: false, Idempotent: true),
        new("v2pn_servers", "V2pn servers",
            "The servers V2pn has: id, name, country, last measured delay, favourite, which one is in use. No addresses.",
            Schema(("country", Text("Only servers of this country, a two-letter code such as DE."))), ReadOnly: true, Destructive: false, Idempotent: true),
        new("v2pn_subscriptions", "V2pn subscriptions",
            "The subscriptions: name, number of servers, last update and its result, expiry and traffic. No links.",
            Schema(), ReadOnly: true, Destructive: false, Idempotent: true),
        new("v2pn_diagnostics", "V2pn diagnostics",
            "V2pn's diagnostics report: versions, system, mode, the state of the core and of the last check. No addresses or links.",
            Schema(), ReadOnly: true, Destructive: false, Idempotent: true),
        new("v2pn_check_connection", "Check the connection",
            "Runs V2pn's check of the current connection again and returns the status with its result.",
            Schema(), ReadOnly: true, Destructive: false, Idempotent: true),
        new("v2pn_connect", "Connect V2pn",
            "Connects with the chosen server, mode and route, and waits for the check. Reverts after 60 seconds unless v2pn_confirm is called.",
            Schema(), ReadOnly: false, Destructive: false, Idempotent: true),
        new("v2pn_use_server", "Choose a V2pn server",
            "Chooses a server by its id or name (from v2pn_servers), or the fastest one, optionally of one country. A connected V2pn switches to it at once. Reverts after 60 seconds unless v2pn_confirm is called.",
            Schema(("server", Text("The server's id or name.")), ("fastest", new JsonObject { ["type"] = "boolean", ["description"] = "Test the servers and choose the quickest instead." }),
                ("country", Text("With fastest: only servers of this country, a two-letter code such as DE."))),
            ReadOnly: false, Destructive: false, Idempotent: true),
        new("v2pn_set_mode", "Set V2pn's mode",
            "Proxy mode (programs that use the system proxy) or VPN mode (the whole computer). VPN mode may need the person to confirm administrator rights on the screen. Reverts after 60 seconds unless v2pn_confirm is called.",
            Schema(("mode", Choice("proxy or vpn.", "proxy", "vpn"))), ReadOnly: false, Destructive: false, Idempotent: true),
        new("v2pn_set_route", "Set V2pn's route",
            "Which sites go through the server: ru-direct (Russian sites directly, the rest through the server), blocked-only (only blocked sites through the server) or all. Reverts after 60 seconds unless v2pn_confirm is called.",
            Schema(("route", Choice("ru-direct, blocked-only or all.", "ru-direct", "blocked-only", "all"))), ReadOnly: false, Destructive: false, Idempotent: true),
        new("v2pn_disconnect", "Disconnect V2pn",
            "Disconnects V2pn. This may cut off your own network. Reverts after 60 seconds unless v2pn_confirm is called.",
            Schema(), ReadOnly: false, Destructive: true, Idempotent: true),
        new("v2pn_confirm", "Keep the last change",
            "Keeps the last change made by these tools, so V2pn does not put it back. Call it once you have checked that the network still works.",
            Schema(), ReadOnly: false, Destructive: false, Idempotent: true),
    ];

    public static JsonArray Tools() => new(All.Select(t => (JsonNode?)new JsonObject
    {
        ["name"] = t.Name,
        ["title"] = t.Title,
        ["description"] = t.Description,
        ["inputSchema"] = t.Schema.DeepClone(),
        ["annotations"] = new JsonObject
        {
            ["title"] = t.Title,
            ["readOnlyHint"] = t.ReadOnly,
            ["destructiveHint"] = t.Destructive,
            ["idempotentHint"] = t.Idempotent,
            ["openWorldHint"] = false,
        },
    }).ToArray());

    /// <summary>Serves until the input ends.</summary>
    public async Task RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        while (await input.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }
            if (await HandleLineAsync(line) is { } answer)
            {
                await output.WriteLineAsync(answer);
                await output.FlushAsync(cancellationToken);
            }
        }
    }

    /// <summary>One message in, its response out; null for a notification, which gets none.</summary>
    public async Task<string?> HandleLineAsync(string line)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            return Error(null, ParseError, "Parse error");
        }
        if (node is not JsonObject message || V2pnControlJson.String(message["jsonrpc"]) != "2.0")
        {
            return Error(null, InvalidRequest, "Invalid Request");
        }
        var method = V2pnControlJson.String(message["method"]);
        if (!message.ContainsKey("id"))
        {
            // notifications/initialized, notifications/cancelled and the like: nothing to answer.
            return null;
        }
        var id = message["id"];
        if (method == null)
        {
            return Error(id, InvalidRequest, "Invalid Request");
        }
        var parameters = message["params"] as JsonObject ?? new JsonObject();
        switch (method)
        {
            case "initialize":
                var asked = V2pnControlJson.String(parameters["protocolVersion"]);
                // The version asked for when this server speaks it; otherwise the newest it does, and the client decides.
                _protocol = asked != null && ProtocolVersions.Contains(asked) ? asked : ProtocolVersions[0];
                return Result(id, new JsonObject
                {
                    ["protocolVersion"] = _protocol,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                    ["serverInfo"] = new JsonObject { ["name"] = "v2pn", ["title"] = "V2pn", ["version"] = serverVersion },
                    ["instructions"] = Instructions,
                });

            case "ping":
                return Result(id, new JsonObject());

            case "tools/list":
                return Result(id, new JsonObject { ["tools"] = Tools() });

            case "tools/call":
                var name = V2pnControlJson.String(parameters["name"]);
                var arguments = parameters["arguments"] as JsonObject ?? new JsonObject();
                if (name == null || All.All(t => t.Name != name))
                {
                    return Error(id, InvalidParams, $"Unknown tool: {name}");
                }
                return Result(id, await CallToolAsync(name, arguments));

            default:
                return Error(id, MethodNotFound, $"Method not found: {method}");
        }
    }

    /// <summary>The channel request a tool stands for, or the reason its arguments are wrong.</summary>
    public static (string Command, JsonObject Args)? Request(string tool, JsonObject arguments, out string? error)
    {
        error = null;
        var args = new JsonObject();
        var guarded = new JsonObject { ["confirmWithin"] = V2pnControlProtocol.McpConfirmWithin };
        string? Country()
        {
            var country = V2pnControlJson.String(arguments["country"])?.Trim().ToUpperInvariant();
            return country is { Length: 2 } && country.All(c => c is >= 'A' and <= 'Z') ? country : null;
        }
        switch (tool)
        {
            case "v2pn_status":
                return (V2pnControlProtocol.Status, args);

            case "v2pn_servers":
                if (arguments["country"] != null)
                {
                    if (Country() is not { } country)
                    {
                        error = "country must be a two-letter code such as DE.";
                        return null;
                    }
                    args["country"] = country;
                }
                return (V2pnControlProtocol.Servers, args);

            case "v2pn_subscriptions":
                return (V2pnControlProtocol.Subs, args);

            case "v2pn_diagnostics":
                return (V2pnControlProtocol.Diag, args);

            case "v2pn_check_connection":
                return (V2pnControlProtocol.Check, args);

            case "v2pn_connect":
                return (V2pnControlProtocol.Connect, guarded);

            case "v2pn_disconnect":
                return (V2pnControlProtocol.Disconnect, guarded);

            case "v2pn_confirm":
                return (V2pnControlProtocol.Confirm, args);

            case "v2pn_use_server":
                var fastest = V2pnControlJson.Bool(arguments["fastest"]) == true;
                var server = V2pnControlJson.String(arguments["server"])?.Trim();
                if (fastest == server is { Length: > 0 })
                {
                    error = "Give either server (an id or a name from v2pn_servers) or fastest: true.";
                    return null;
                }
                if (fastest)
                {
                    guarded["fastest"] = true;
                    if (arguments["country"] != null)
                    {
                        if (Country() is not { } country)
                        {
                            error = "country must be a two-letter code such as DE.";
                            return null;
                        }
                        guarded["country"] = country;
                    }
                }
                else
                {
                    guarded["server"] = server;
                }
                return (V2pnControlProtocol.Use, guarded);

            case "v2pn_set_mode":
                if (V2pnControlJson.String(arguments["mode"]) is not ("proxy" or "vpn") || arguments["mode"] is not { } mode)
                {
                    error = "mode must be proxy or vpn.";
                    return null;
                }
                guarded["mode"] = V2pnControlJson.String(mode);
                return (V2pnControlProtocol.Mode, guarded);

            case "v2pn_set_route":
                var route = V2pnControlJson.String(arguments["route"]);
                if (!V2pnControlProtocol.Routes.Any(r => r.Name == route))
                {
                    error = "route must be ru-direct, blocked-only or all.";
                    return null;
                }
                guarded["route"] = route;
                return (V2pnControlProtocol.Route, guarded);
        }
        error = $"Unknown tool: {tool}";
        return null;
    }

    private async Task<JsonObject> CallToolAsync(string tool, JsonObject arguments)
    {
        if (Request(tool, arguments, out var error) is not { } request)
        {
            return ToolResult(error!, null, isError: true);
        }
        V2pnControlAnswer answer;
        try
        {
            answer = await call(request.Command, request.Args);
        }
        catch (Exception ex)
        {
            return ToolResult($"V2pn could not be reached: {ex.Message}", null, isError: true);
        }
        if (!answer.Ok)
        {
            return ToolResult($"{answer.ErrorMessage} ({answer.ErrorCode})", null, isError: true);
        }
        // The report is text already; everything else is JSON, shown as text too for clients without structured results.
        var text = request.Command == V2pnControlProtocol.Diag && V2pnControlJson.String(answer.Data?["text"]) is { } report
            ? report
            : answer.Data?.ToJsonString(V2pnControlProtocol.Indented) ?? "{}";
        var structured = answer.Data switch
        {
            JsonObject o => (JsonObject)o.DeepClone(),
            JsonArray a => new JsonObject { [request.Command] = a.DeepClone() },
            _ => null,
        };
        return ToolResult(text, structured, isError: false);
    }

    private JsonObject ToolResult(string text, JsonObject? structured, bool isError)
    {
        var result = new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = isError,
        };
        // Structured results came with the 2025-06-18 version; older clients get the text alone.
        if (structured != null && string.CompareOrdinal(_protocol, "2025-06-18") >= 0)
        {
            result["structuredContent"] = structured;
        }
        return result;
    }

    private static string Result(JsonNode? id, JsonObject result) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["result"] = result,
    }.ToJsonString(V2pnControlProtocol.Compact);

    private static string Error(JsonNode? id, int code, string message) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    }.ToJsonString(V2pnControlProtocol.Compact);
}
