using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ServiceLib;

// V2pn: this file is compiled into ServiceLib (the app's side) and into v2pnctl (V2pnCtl.csproj links it), so both
// ends of the control channel read and write the same lines. It uses nothing else from ServiceLib.

/// <summary>
/// V2pn: the language of the control channel between V2pn and v2pnctl. One JSON line per request,
/// <c>{"v":1,"id":1,"cmd":"status","args":{},"src":"cli"}</c>, and one per answer, <c>{"v":1,"id":1,"ok":true,"data":…}</c>
/// or <c>{"v":1,"id":1,"ok":false,"error":{"code":"…","message":"…"}}</c>. A line of another version is refused with a
/// plain reason, so a v2pnctl from an older build says "update" instead of printing nonsense.
/// </summary>
public static class V2pnControlProtocol
{
    public const int Version = 1;

    /// <summary>A request or an answer longer than this is not read: nothing V2pn sends or expects comes near it.</summary>
    public const int MaxLineBytes = 1024 * 1024;

    public const string Status = "status";
    public const string Servers = "servers";
    public const string Subs = "subs";
    public const string Check = "check";
    public const string Diag = "diag";
    public const string Open = "open";
    public const string Connect = "connect";
    public const string Disconnect = "disconnect";
    public const string Use = "use";
    public const string Mode = "mode";
    public const string Route = "route";
    public const string Confirm = "confirm";

    /// <summary>What "Read only" allows: nothing here changes how the computer reaches the internet.</summary>
    public static readonly string[] ReadCommands = [Status, Servers, Subs, Check, Diag, Open];

    /// <summary>What needs "Control".</summary>
    public static readonly string[] ControlCommands = [Connect, Disconnect, Use, Mode, Route, Confirm];

    /// <summary>The changes <c>--confirm-within</c> can put back.</summary>
    public static readonly string[] RevertibleCommands = [Connect, Disconnect, Use, Mode, Route];

    public static bool IsKnown(string? command) => ReadCommands.Contains(command) || ControlCommands.Contains(command);

    /// <summary>The routes by the names the command line uses, with the ids of V2pn's three presets.</summary>
    public static readonly (string Name, string Id)[] Routes =
    [
        ("ru-direct", "v2pn-ru-direct"),
        ("blocked-only", "v2pn-blocked-only"),
        ("all", "v2pn-all"),
    ];

    /// <summary>The shortest and longest wait for a confirmation, in seconds.</summary>
    public const int MinConfirmWithin = 5;

    public const int MaxConfirmWithin = 3600;

    /// <summary>The wait the MCP tools always ask for.</summary>
    public const int McpConfirmWithin = 60;

    public const string SourceCli = "cli";
    public const string SourceMcp = "mcp";

    public const string ErrBadRequest = "bad_request";
    public const string ErrVersion = "version";
    public const string ErrUnknownCommand = "unknown_command";
    public const string ErrDisabled = "disabled";
    public const string ErrForbidden = "forbidden";
    public const string ErrNeedsConfirmation = "needs_confirmation";
    public const string ErrNotFound = "not_found";
    public const string ErrAmbiguous = "ambiguous";
    public const string ErrNoServer = "no_server";
    public const string ErrNotConnected = "not_connected";
    public const string ErrNoAnswer = "no_answer";
    public const string ErrFailed = "failed";
    public const string ErrNotRunning = "not_running";

    public const int ExitOk = 0;
    public const int ExitError = 1;
    public const int ExitNeedsConfirmation = 2;
    public const int ExitNotRunning = 3;
    public const int ExitForbidden = 4;

    /// <summary>
    /// The exit code of v2pnctl for an answer: 0 done, 2 the person has to confirm on the screen, 3 V2pn is not running
    /// and could not be started, 4 the setting does not allow it, 1 anything else.
    /// </summary>
    public static int ExitCode(V2pnControlAnswer answer) => answer.Ok ? ExitOk : answer.ErrorCode switch
    {
        ErrNeedsConfirmation => ExitNeedsConfirmation,
        ErrNotRunning => ExitNotRunning,
        ErrDisabled or ErrForbidden => ExitForbidden,
        _ => ExitError,
    };

    /// <summary>The channel's lines: anything beyond ASCII escaped, so no code page can garble them.</summary>
    public static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    /// <summary>JSON a person or an assistant reads: Cyrillic names and quotes as they are, one kind of line end.</summary>
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>
/// V2pn: the language of the words people read in v2pnctl and in V2pn's answers to it: English, or Russian when asked.
/// Only words: commands, options, route names, JSON keys and error codes are the same in both. <c>default</c> is English.
/// </summary>
public readonly record struct V2pnControlText(bool Ru)
{
    /// <summary>The value of a request's <c>lang</c> for Russian. English is no <c>lang</c> at all.</summary>
    public const string RussianCode = "ru";

    public static readonly V2pnControlText English = new(false);

    public static readonly V2pnControlText Russian = new(true);

    /// <summary>The words for <paramref name="lang"/> ("ru", "ru-RU", "ru_RU.UTF-8"); English for anything else or nothing.</summary>
    public static V2pnControlText Of(string? lang) =>
        lang?.Trim().StartsWith(RussianCode, StringComparison.OrdinalIgnoreCase) == true ? Russian : English;

    /// <summary>What a request carries as <c>lang</c>: "ru", or null for English.</summary>
    public string? Code => Ru ? RussianCode : null;

    public string T(string en, string ru) => Ru ? ru : en;

    /// <summary>
    /// The Russian word for <paramref name="n"/> things: <paramref name="one"/> for 1, 21, 31…, <paramref name="few"/>
    /// for 2–4, 22–24…, <paramref name="many"/> for the rest ("1 день", "3 дня", "11 дней").
    /// </summary>
    public static string Plural(long n, string one, string few, string many)
    {
        var (m10, m100) = (Math.Abs(n) % 10, Math.Abs(n) % 100);
        return m10 == 1 && m100 != 11 ? one
            : m10 is >= 2 and <= 4 && m100 is < 12 or > 14 ? few
            : many;
    }
}

/// <summary>
/// One request on the channel. <see cref="Source"/> says who asked: "cli" or "mcp", for the log and the toast.
/// <see cref="Lang"/> is "ru" when the answer's words should be Russian; a request without it (MCP, an older v2pnctl)
/// gets English. An older V2pn reads only the keys it knows, so the extra key costs nothing there.
/// </summary>
public sealed record V2pnControlRequest(long Id, string Command, JsonObject Args, string Source, string? Lang = null)
{
    /// <summary>The words of the answer.</summary>
    public V2pnControlText Text => V2pnControlText.Of(Lang);

    public string ToLine()
    {
        var json = new JsonObject
        {
            ["v"] = V2pnControlProtocol.Version,
            ["id"] = Id,
            ["cmd"] = Command,
            ["args"] = Args.DeepClone(),
            ["src"] = Source,
        };
        if (Text.Code is { } lang)
        {
            json["lang"] = lang;
        }
        return json.ToJsonString(V2pnControlProtocol.Compact);
    }

    /// <summary>The request in <paramref name="line"/>, or the answer that refuses it.</summary>
    public static V2pnControlRequest? Parse(string line, out V2pnControlAnswer? refusal)
    {
        refusal = null;
        JsonObject? json;
        try
        {
            json = JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            json = null;
        }
        if (json == null)
        {
            refusal = V2pnControlAnswer.Failure(0, V2pnControlProtocol.ErrBadRequest, "The request is not a JSON object.");
            return null;
        }
        var id = V2pnControlJson.Long(json["id"]) ?? 0;
        // Read first, so even a refusal is in the asker's language.
        var lang = V2pnControlText.Of(V2pnControlJson.String(json["lang"]));
        var version = V2pnControlJson.Long(json["v"]);
        if (version != V2pnControlProtocol.Version)
        {
            refusal = V2pnControlAnswer.Failure(id, V2pnControlProtocol.ErrVersion, lang.T(
                $"This V2pn speaks version {V2pnControlProtocol.Version} of the control channel and the request is version {version?.ToString() ?? "unknown"}. Use the v2pnctl that came with this V2pn.",
                $"Этот V2pn знает версию {V2pnControlProtocol.Version} канала управления, а запрос пришёл {(version == null ? "без версии" : $"в версии {version}")}. Используйте v2pnctl из папки этого V2pn."));
            return null;
        }
        if (V2pnControlJson.String(json["cmd"]) is not { Length: > 0 } command)
        {
            refusal = V2pnControlAnswer.Failure(id, V2pnControlProtocol.ErrBadRequest, lang.T("The request names no command.", "В запросе нет команды."));
            return null;
        }
        var args = json["args"] switch
        {
            null => new JsonObject(),
            JsonObject o => (JsonObject)o.DeepClone(),
            _ => null,
        };
        if (args == null)
        {
            refusal = V2pnControlAnswer.Failure(id, V2pnControlProtocol.ErrBadRequest, lang.T("The arguments are not a JSON object.", "Аргументы — не объект JSON."));
            return null;
        }
        var source = V2pnControlJson.String(json["src"]) == V2pnControlProtocol.SourceMcp ? V2pnControlProtocol.SourceMcp : V2pnControlProtocol.SourceCli;
        return new V2pnControlRequest(id, command, args, source, lang.Code);
    }
}

/// <summary>One answer on the channel.</summary>
public sealed record V2pnControlAnswer(long Id, bool Ok, JsonNode? Data, string? ErrorCode, string? ErrorMessage)
{
    public static V2pnControlAnswer Success(long id, JsonNode? data) => new(id, true, data, null, null);

    public static V2pnControlAnswer Failure(long id, string code, string message) => new(id, false, null, code, message);

    public JsonObject ToJson()
    {
        var json = new JsonObject { ["v"] = V2pnControlProtocol.Version, ["id"] = Id, ["ok"] = Ok };
        if (Ok)
        {
            json["data"] = Data?.DeepClone();
        }
        else
        {
            json["error"] = new JsonObject { ["code"] = ErrorCode, ["message"] = ErrorMessage };
        }
        return json;
    }

    public string ToLine() => ToJson().ToJsonString(V2pnControlProtocol.Compact);

    /// <summary>
    /// The answer in <paramref name="line"/>. A line V2pn could not have meant is a failure, never an exception, told
    /// in <paramref name="t"/>'s words.
    /// </summary>
    public static V2pnControlAnswer Parse(string line, long expectedId, V2pnControlText t = default)
    {
        JsonObject? json;
        try
        {
            json = JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            json = null;
        }
        if (json == null)
        {
            return Failure(expectedId, V2pnControlProtocol.ErrFailed, t.T("V2pn's answer is not a JSON object.", "Ответ V2pn — не объект JSON."));
        }
        var version = V2pnControlJson.Long(json["v"]);
        if (version != V2pnControlProtocol.Version)
        {
            return Failure(expectedId, V2pnControlProtocol.ErrVersion, t.T(
                $"This v2pnctl speaks version {V2pnControlProtocol.Version} of the control channel and V2pn answered in version {version?.ToString() ?? "unknown"}. Use the v2pnctl that came with this V2pn.",
                $"Этот v2pnctl знает версию {V2pnControlProtocol.Version} канала управления, а V2pn ответил {(version == null ? "без версии" : $"в версии {version}")}. Используйте v2pnctl из папки этого V2pn."));
        }
        var id = V2pnControlJson.Long(json["id"]) ?? 0;
        if (V2pnControlJson.Bool(json["ok"]) == true)
        {
            return Success(id, json["data"]?.DeepClone());
        }
        var error = json["error"] as JsonObject;
        return Failure(id, V2pnControlJson.String(error?["code"]) ?? V2pnControlProtocol.ErrFailed,
            V2pnControlJson.String(error?["message"]) ?? t.T("V2pn gave no reason.", "V2pn не назвал причину."));
    }
}

/// <summary>Reading values out of JSON whatever the sender put there, without exceptions.</summary>
public static class V2pnControlJson
{
    public static string? String(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public static bool? Bool(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;

    public static long? Long(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }
        if (value.TryGetValue<long>(out var number))
        {
            return number;
        }
        if (value.TryGetValue<int>(out var small))
        {
            return small;
        }
        if (value.TryGetValue<double>(out var real) && real is >= long.MinValue and <= long.MaxValue && real == Math.Floor(real))
        {
            return (long)real;
        }
        return value.TryGetValue<string>(out var text) && long.TryParse(text, out var parsed) ? parsed : null;
    }
}

/// <summary>
/// V2pn: where a copy of V2pn listens. The name comes from the folder the program runs from, as the lock that keeps one
/// V2pn per copy does (Program.OnStartup), so a sandbox copy and the V2pn in use never answer the same v2pnctl. The
/// folder is compared without case on Windows, where "C:\Users" and "c:\users" are one folder.
/// </summary>
public static class V2pnControlAddress
{
    /// <summary>The program's file in a copy's folder.</summary>
    public const string WindowsProgram = "V2pn.exe";

    public static string Key(string programFolder)
    {
        var full = Path.GetFullPath(programFolder).TrimEnd('/', '\\');
        if (OperatingSystem.IsWindows())
        {
            full = full.ToLowerInvariant();
        }
        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(full)))[..16];
    }

    /// <summary>The named pipe on Windows.</summary>
    public static string PipeName(string programFolder) => $"V2pn.control.{Key(programFolder)}";

    /// <summary>
    /// The socket on a Mac: in the user's own temporary folder, which only they can open. A socket's path may be at most
    /// 104 bytes there, and the data folder in Library is often longer, so it does not live with the data.
    /// </summary>
    public static string SocketPath(string programFolder) => Path.Combine(UserTempFolder(), $"v2pn-{Key(programFolder)}.sock");

    /// <summary>
    /// The user's temporary folder as the system names it: on a Mac asked of the system itself, so V2pn (started by
    /// launchd) and a terminal over ssh (which may not set TMPDIR) agree on it.
    /// </summary>
    public static string UserTempFolder()
    {
        if (OperatingSystem.IsMacOS() && DarwinUserTempFolder() is { Length: > 0 } darwin)
        {
            return darwin;
        }
        return Environment.GetEnvironmentVariable("TMPDIR") is { Length: > 0 } tmp ? tmp : Path.GetTempPath();
    }

    // _CS_DARWIN_USER_TEMP_DIR: the per-user folder under /var/folders that TMPDIR normally points to.
    private const int DarwinUserTempDir = 65537;

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "confstr")]
    private static extern nuint Confstr(int name, byte[]? buffer, nuint length);

    private static string? DarwinUserTempFolder()
    {
        try
        {
            var buffer = new byte[1024];
            var length = Confstr(DarwinUserTempDir, buffer, (nuint)buffer.Length);
            if (length == 0 || length > (nuint)buffer.Length)
            {
                return null;
            }
            return Encoding.UTF8.GetString(buffer, 0, (int)length - 1);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The folder of the copy v2pnctl talks to. <paramref name="chosen"/> (--instance, or V2PN_INSTANCE) may name the
    /// folder, the program in it, a Mac bundle or a sandbox folder that holds one. Without a choice: the copy v2pnctl
    /// came with, and when it came with none (a build of its own), the installed V2pn.
    /// </summary>
    public static string ResolveFolder(string? chosen, string selfFolder)
    {
        if (chosen is { Length: > 0 })
        {
            var path = Path.GetFullPath(chosen.Trim().Trim('"'));
            if (OperatingSystem.IsWindows())
            {
                return File.Exists(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(path)! : path;
            }
            var trimmed = path.TrimEnd('/');
            if (trimmed.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(trimmed, "Contents", "MacOS");
            }
            if (Directory.Exists(Path.Combine(trimmed, "V2pn.app")))
            {
                return Path.Combine(trimmed, "V2pn.app", "Contents", "MacOS");
            }
            if (Directory.Exists(Path.Combine(trimmed, "Contents", "MacOS")))
            {
                return Path.Combine(trimmed, "Contents", "MacOS");
            }
            return File.Exists(trimmed) ? Path.GetDirectoryName(trimmed)! : trimmed;
        }
        var self = Path.GetFullPath(selfFolder);
        if (OperatingSystem.IsWindows())
        {
            return File.Exists(Path.Combine(self, WindowsProgram))
                ? self
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "V2pn");
        }
        if (OperatingSystem.IsMacOS())
        {
            return self.TrimEnd('/').EndsWith(Path.Combine(".app", "Contents", "MacOS"), StringComparison.OrdinalIgnoreCase)
                ? self
                : "/Applications/V2pn.app/Contents/MacOS";
        }
        return self;
    }
}
