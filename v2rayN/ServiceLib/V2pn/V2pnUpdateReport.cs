namespace ServiceLib;

/// <summary>
/// V2pn: an opt-in note to V2pn's server when an update fails (Updates → "Tell V2pn when an update fails", off by
/// default). It says only which version failed to replace which, on what system and at which step, with a short code
/// such as "filenotfoundexception": no address of the computer is kept, no id of the copy, no settings, no servers.
/// A failed update used to be found only by chance (the Mac update of 1.3.0 to 1.4.0).
/// One note per version and step; the server counts them by day.
/// </summary>
public static class V2pnUpdateReport
{
    public const string Endpoint = V2pnInfo.SiteUrl + "/api/report/update";

    public static readonly IReadOnlyList<string> Stages = ["download", "verify", "stage", "swap", "start", "rollback"];

    private static readonly Regex VersionPattern = new(@"^\d{1,4}\.\d{1,4}\.\d{1,6}(-[0-9A-Za-z.-]{1,20})?$");
    private static readonly Regex CodeCleaner = new("[^a-z0-9_-]+");

    /// <summary>A code the server takes ([a-z0-9_-], 1–40) from whatever names the failure.</summary>
    public static string Code(string? what)
    {
        var code = CodeCleaner.Replace((what ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_'), string.Empty).Trim('_', '-');
        if (code.Length > 40)
        {
            code = code[..40];
        }
        return code.Length > 0 ? code : "unknown";
    }

    /// <summary>The note's JSON, or null when a part is not what the server takes.</summary>
    public static string? Body(string from, string to, string stage, string code)
    {
        if (!VersionPattern.IsMatch(from) || !VersionPattern.IsMatch(to) || !Stages.Contains(stage))
        {
            return null;
        }
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : null;
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null,
        };
        if (os == null || arch == null)
        {
            return null;
        }
        return new JsonObject
        {
            ["from"] = from,
            ["to"] = to,
            ["os"] = os,
            ["arch"] = arch,
            ["stage"] = stage,
            ["code"] = Code(code),
        }.ToJsonString();
    }

    /// <summary>Whether this note may go: the person turned the notes on, and this version and step were not told yet.</summary>
    public static bool Allowed(Config config, string to, string stage) =>
        config.UiItem.V2pnReportUpdateFailures && !(config.UiItem.V2pnReportedFailures ?? []).Contains($"{to}|{stage}");

    /// <summary>Sends the note when it is allowed; never throws, never waits long.</summary>
    public static async Task SendAsync(Config config, string from, string to, string stage, string? what, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Allowed(config, to, stage) || Body(from, to, stage, what ?? string.Empty) is not { } body)
            {
                return;
            }
            var result = await V2pnHttp.PostJsonAsync(Endpoint, body, null, 1024, cancellationToken);
            if (result.Status is { } status && (int)status is >= 200 and < 300)
            {
                var told = config.UiItem.V2pnReportedFailures ??= [];
                told.Add($"{to}|{stage}");
                // A handful is enough to remember; older ones would only grow the settings file.
                if (told.Count > 20)
                {
                    told.RemoveRange(0, told.Count - 20);
                }
                await ConfigTextHandler.SaveConfig();
            }
            Logging.SaveLog($"V2pn update report {to}/{stage}: {(result.Status is { } s ? (int)s : result.Error)}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logging.SaveLog(nameof(V2pnUpdateReport), ex);
        }
    }
}
