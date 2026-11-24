using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace ServiceLib;

// V2pn: compiled into ServiceLib (for the tests) and into v2pnctl, like V2pnControlProtocol.cs.

/// <summary>What v2pnctl was asked to do.</summary>
public sealed record V2pnCtlCommandLine(string Command, JsonObject Args, string? Instance, bool Json);

/// <summary>
/// V2pn: v2pnctl's words. The arguments become one request on the channel, and V2pn's answer becomes a few short lines
/// (or the answer itself with --json). Nothing here talks to V2pn, so all of it is tested.
/// </summary>
public static class V2pnControlCli
{
    public const string Help = "help";
    public const string VersionCommand = "version";
    public const string Mcp = "mcp";

    public const string Usage = """
        v2pnctl: control V2pn from a terminal or an AI assistant.

        Usage: v2pnctl [--instance <folder>] [--json] <command> [arguments]

        Reading:
          status                         Connected or not, the server, the mode, the route, the last check
          servers [--country XX]         The servers: name, country, last delay, favourite (no addresses)
          subs                           The subscriptions: name, last update, expiry, traffic (no links)
          check                          Check the connection again
          diag                           The diagnostics report
          open                           Show V2pn's window

        Changing (Settings > General > Control from the command line and AI > Control):
          connect                        Connect with the chosen server, mode and route
          disconnect                     Disconnect
          use <name or id>               Choose a server; a connected V2pn switches to it at once
          use --fastest [--country XX]   Test the servers and choose the quickest
          mode proxy|vpn                 Proxy mode or VPN mode
          route ru-direct|blocked-only|all
                                         Russian sites directly / only blocked sites through the server / everything
          confirm                        Keep a change made with --confirm-within

          --confirm-within <seconds>     With connect, disconnect, use, mode and route: V2pn puts the change back
                                         by itself unless "v2pnctl confirm" comes in time. For a change that may
                                         cut off your own connection.

          mcp                            Run as an MCP server on standard input and output, for AI assistants

        Options:
          --instance <folder>            The copy of V2pn to talk to (or V2PN_INSTANCE); by default the one
                                         v2pnctl came with
          --json                         Print V2pn's answer as JSON

        Exit codes: 0 done, 1 error, 2 needs a confirmation on the screen, 3 V2pn is not running and could not
        be started, 4 not allowed by V2pn's settings.
        """;

    /// <summary>The command line, or the reason it cannot be one.</summary>
    public static V2pnCtlCommandLine Parse(IReadOnlyList<string> argv, out string? error)
    {
        error = null;
        string? instance = null;
        string? country = null;
        int? confirmWithin = null;
        var json = false;
        var fastest = false;
        var words = new List<string>();
        for (var i = 0; i < argv.Count; i++)
        {
            var arg = argv[i];
            string? inline = null;
            if (arg.StartsWith("--", StringComparison.Ordinal) && arg.IndexOf('=') is var eq and > 2)
            {
                inline = arg[(eq + 1)..];
                arg = arg[..eq];
            }
            string? Value()
            {
                if (inline != null)
                {
                    return inline;
                }
                return i + 1 < argv.Count ? argv[++i] : null;
            }
            switch (arg)
            {
                case "--instance":
                    instance = Value();
                    if (string.IsNullOrWhiteSpace(instance))
                    {
                        error = "--instance needs a folder.";
                        return Fail();
                    }
                    break;

                case "--json":
                    json = true;
                    break;

                case "-h" or "--help" or "-?" or "/?":
                    words.Insert(0, Help);
                    break;

                case "--version":
                    words.Insert(0, VersionCommand);
                    break;

                case "--fastest":
                    fastest = true;
                    break;

                case "--country":
                    country = Value()?.Trim().ToUpperInvariant();
                    if (country is not { Length: 2 } || !country.All(c => c is >= 'A' and <= 'Z'))
                    {
                        error = "--country needs a two-letter country code, such as DE.";
                        return Fail();
                    }
                    break;

                case "--confirm-within":
                    if (!int.TryParse(Value(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                        || seconds < V2pnControlProtocol.MinConfirmWithin || seconds > V2pnControlProtocol.MaxConfirmWithin)
                    {
                        error = $"--confirm-within needs a number of seconds from {V2pnControlProtocol.MinConfirmWithin} to {V2pnControlProtocol.MaxConfirmWithin}.";
                        return Fail();
                    }
                    confirmWithin = seconds;
                    break;

                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal) && arg.Length > 2)
                    {
                        error = $"Unknown option {arg}.";
                        return Fail();
                    }
                    words.Add(argv[i]);
                    break;
            }
        }

        if (words.Count == 0)
        {
            words.Add(Help);
        }
        var command = words[0].ToLowerInvariant();
        var rest = words.Skip(1).ToList();
        var args = new JsonObject();

        if (command is Help or VersionCommand)
        {
            return new V2pnCtlCommandLine(command, args, instance, json);
        }
        if (!V2pnControlProtocol.IsKnown(command) && command != Mcp)
        {
            error = $"Unknown command {words[0]}. Run \"v2pnctl help\" for the list.";
            return Fail();
        }
        if (confirmWithin != null && !V2pnControlProtocol.RevertibleCommands.Contains(command))
        {
            error = "--confirm-within goes only with connect, disconnect, use, mode and route.";
            return Fail();
        }
        if (fastest && command != V2pnControlProtocol.Use)
        {
            error = "--fastest goes only with use.";
            return Fail();
        }
        if (country != null && command != V2pnControlProtocol.Servers && !(command == V2pnControlProtocol.Use && fastest))
        {
            error = "--country goes only with servers and use --fastest.";
            return Fail();
        }

        switch (command)
        {
            case V2pnControlProtocol.Servers:
                if (rest.Count > 0)
                {
                    error = "servers takes no words; filter with --country XX.";
                    return Fail();
                }
                if (country != null)
                {
                    args["country"] = country;
                }
                break;

            case V2pnControlProtocol.Use:
                if (fastest == (rest.Count > 0))
                {
                    error = "use needs a server's name or id, or --fastest.";
                    return Fail();
                }
                if (fastest)
                {
                    args["fastest"] = true;
                    if (country != null)
                    {
                        args["country"] = country;
                    }
                }
                else
                {
                    args["server"] = string.Join(' ', rest);
                }
                break;

            case V2pnControlProtocol.Mode:
                if (rest.Count != 1 || rest[0].ToLowerInvariant() is not ("proxy" or "vpn"))
                {
                    error = "mode needs proxy or vpn.";
                    return Fail();
                }
                args["mode"] = rest[0].ToLowerInvariant();
                break;

            case V2pnControlProtocol.Route:
                if (rest.Count != 1 || !V2pnControlProtocol.Routes.Any(r => r.Name == rest[0].ToLowerInvariant()))
                {
                    error = "route needs ru-direct, blocked-only or all.";
                    return Fail();
                }
                args["route"] = rest[0].ToLowerInvariant();
                break;

            default:
                if (rest.Count > 0)
                {
                    error = $"{command} takes no words.";
                    return Fail();
                }
                break;
        }
        if (confirmWithin is { } within)
        {
            args["confirmWithin"] = within;
        }
        return new V2pnCtlCommandLine(command, args, instance, json);

        V2pnCtlCommandLine Fail() => new(Help, new JsonObject(), instance, json);
    }

    /// <summary>The answer as a person reads it. A failure is its message alone; the caller prints it to the error output.</summary>
    public static string Format(string command, V2pnControlAnswer answer, DateTime nowUtc)
    {
        if (!answer.Ok)
        {
            return answer.ErrorMessage ?? answer.ErrorCode ?? "Failed.";
        }
        var data = answer.Data;
        switch (command)
        {
            case V2pnControlProtocol.Status:
                return data is JsonObject status ? FormatStatus(status, nowUtc) : string.Empty;

            case V2pnControlProtocol.Servers:
                return data is JsonArray servers ? FormatServers(servers) : string.Empty;

            case V2pnControlProtocol.Subs:
                return data is JsonArray subs ? FormatSubs(subs, nowUtc) : string.Empty;

            case V2pnControlProtocol.Diag:
                return V2pnControlJson.String(data?["text"]) ?? string.Empty;
        }
        var sb = new StringBuilder();
        if (V2pnControlJson.String(data?["message"]) is { Length: > 0 } message)
        {
            sb.AppendLine(message);
        }
        if (data?["status"] is JsonObject after)
        {
            sb.AppendLine(FormatStatus(after, nowUtc));
        }
        return sb.ToString().TrimEnd();
    }

    private static string Verdict(string? verdict) => verdict switch
    {
        "kill-switch" => "Off: the connection fell, and the kill switch keeps the internet closed",
        "core-stopped" => "Off: the core kept stopping, and V2pn gave up restarting it",
        "core-failed" => "Off: the core did not start",
        "off" => "Off",
        "reconnecting" => "Reconnecting",
        "connecting" => "Connecting",
        "checking" => "Checking the connection",
        "local-only" => "On, the local proxy port only",
        "vpn-connected" => "Connected in VPN mode",
        "proxy-connected" => "Connected in proxy mode",
        "check-problem" => "On, but the check of the connection did not pass",
        _ => verdict ?? "Unknown",
    };

    public static string FormatStatus(JsonObject status, DateTime nowUtc)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Verdict(V2pnControlJson.String(status["verdict"])));
        var mode = V2pnControlJson.String(status["mode"]) == "vpn" ? "VPN" : "proxy";
        if (status["server"] is JsonObject server)
        {
            var country = V2pnControlJson.String(server["country"]);
            sb.AppendLine($"Server: {V2pnControlJson.String(server["name"])}{(country != null ? $" ({country})" : string.Empty)}");
        }
        else
        {
            sb.AppendLine("Server: none chosen");
        }
        sb.AppendLine($"Mode: {mode}");
        sb.AppendLine($"Route: {RouteName(V2pnControlJson.String(status["route"]))}");
        if (status["check"] is JsonObject check)
        {
            var line = $"Check: {V2pnControlJson.String(check["state"])}";
            if (V2pnControlJson.Long(check["ms"]) is { } ms)
            {
                line += $", {ms} ms";
            }
            if (V2pnControlJson.String(check["at"]) is { } at
                && DateTime.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when))
            {
                line += $", {Ago(nowUtc - when)}";
            }
            sb.AppendLine(line);
        }
        if (status["pendingConfirm"] is JsonObject pending)
        {
            sb.AppendLine($"Waiting for \"v2pnctl confirm\": {V2pnControlJson.Long(pending["secondsLeft"])} s left, then V2pn puts the change back");
        }
        return sb.ToString().TrimEnd();
    }

    private static string RouteName(string? route) => route switch
    {
        "ru-direct" => "Russian sites directly (ru-direct)",
        "blocked-only" => "only blocked sites through the server (blocked-only)",
        "all" => "everything through the server (all)",
        "custom" => "a rule set of your own",
        _ => "none",
    };

    private static string Ago(TimeSpan span) =>
        span.TotalSeconds < 5 ? "just now"
        : span.TotalMinutes < 1 ? $"{(int)span.TotalSeconds} s ago"
        : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min ago"
        : span.TotalDays < 2 ? $"{(int)span.TotalHours} h ago"
        : $"{(int)span.TotalDays} days ago";

    public static string FormatServers(JsonArray servers)
    {
        if (servers.Count == 0)
        {
            return "No servers.";
        }
        var rows = new List<string[]> { new[] { " ", "COUNTRY", "DELAY", "FAV", "NAME", "ID" } };
        foreach (var node in servers.OfType<JsonObject>())
        {
            var delay = V2pnControlJson.Long(node["delayMs"]) is { } ms ? $"{ms} ms"
                : V2pnControlJson.String(node["test"]) == "failed" ? "failed" : "-";
            rows.Add(
            [
                V2pnControlJson.Bool(node["active"]) == true ? ">" : " ",
                V2pnControlJson.String(node["country"]) ?? "-",
                delay,
                V2pnControlJson.Bool(node["favorite"]) == true ? "*" : "",
                V2pnControlJson.String(node["name"]) ?? string.Empty,
                V2pnControlJson.String(node["id"]) ?? string.Empty,
            ]);
        }
        return Table(rows);
    }

    public static string FormatSubs(JsonArray subs, DateTime nowUtc)
    {
        if (subs.Count == 0)
        {
            return "No subscriptions.";
        }
        var rows = new List<string[]> { new[] { "NAME", "SERVERS", "UPDATED", "EXPIRES", "TRAFFIC" } };
        foreach (var node in subs.OfType<JsonObject>())
        {
            var updated = V2pnControlJson.String(node["updated"]) is { } at && DateTime.TryParse(at, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when)
                ? Ago(nowUtc - when) : "never";
            if (V2pnControlJson.String(node["lastResult"]) is { } result && result != "None")
            {
                updated += $" ({result})";
            }
            var expires = V2pnControlJson.String(node["expires"]) is { } exp && DateTime.TryParse(exp, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var end)
                ? end < nowUtc ? "expired" : $"in {(int)(end - nowUtc).TotalDays} days" : "-";
            var total = V2pnControlJson.Long(node["trafficTotalBytes"]) ?? 0;
            var used = V2pnControlJson.Long(node["trafficUsedBytes"]) ?? 0;
            var traffic = total > 0 ? $"{Gb(used)} of {Gb(total)} GB" : used > 0 ? $"{Gb(used)} GB used" : "-";
            rows.Add([V2pnControlJson.String(node["name"]) ?? string.Empty, (V2pnControlJson.Long(node["servers"]) ?? 0).ToString(CultureInfo.InvariantCulture), updated, expires, traffic]);
        }
        return Table(rows);
    }

    private static string Gb(long bytes) => (bytes / 1073741824.0).ToString("0.#", CultureInfo.InvariantCulture);

    private static string Table(List<string[]> rows)
    {
        var widths = new int[rows[0].Length];
        foreach (var row in rows)
        {
            for (var i = 0; i < row.Length; i++)
            {
                widths[i] = Math.Max(widths[i], row[i].Length);
            }
        }
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join("  ", row.Select((cell, i) => i == row.Length - 1 ? cell : cell.PadRight(widths[i]))).TrimEnd());
        }
        return sb.ToString().TrimEnd();
    }
}
