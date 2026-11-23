namespace ServiceLib;

/// <summary>
/// "Start with the system" on a Mac: a launch agent in ~/Library/LaunchAgents that starts V2pn itself at sign-in.
/// v2rayN's agent ran a shell line (/bin/sh -c "if ! pgrep ...; then V2pn; fi"), which System Settings › Login Items
/// lists as "sh" from an unidentified developer; nobody can tell what that is or whether it is safe to switch off.
/// Started directly, the entry carries V2pn's name. The shell's check for a running copy is not needed: a second V2pn
/// leaves at once (Program.OnStartup).
/// </summary>
public static class V2pnMacAutostart
{
    /// <summary>The bundle id tools/package-macos.sh gives the app; System Settings shows the agent under that app.</summary>
    public const string BundleId = "io.github.piwqust.v2pn";

    public static string Label => $"{Global.AppName}-LaunchAgent";

    public static string AgentPath(string? home = null) =>
        Path.Combine(home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", $"{Label}.plist");

    /// <summary>A copy that will not be at the same path at the next sign-in: opened from the disk image, or moved aside by Gatekeeper.</summary>
    public static bool IsTemporaryCopy(string? exePath) =>
        exePath.IsNullOrEmpty() || exePath.Contains("/AppTranslocation/") || exePath.StartsWith("/Volumes/");

    /// <summary>
    /// The agent's file is one per user and belongs to the installed V2pn, which keeps its data in the user's folder.
    /// A copy that keeps its data beside itself (a test build made by tools/mac-sandbox.sh) must leave it alone: on
    /// 23 November 2026 such a copy, given v2rayN's settings with "start with the system" on, rewrote the agent to
    /// start itself in place of the installed app.
    /// </summary>
    public static bool MayOwnAgent(string? localAppDataFlag = null) =>
        (localAppDataFlag ?? Environment.GetEnvironmentVariable(Global.LocalAppData)) == "1";

    public static string Plist(string exePath)
    {
        var program = System.Security.SecurityElement.Escape(exePath);
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Label</key>
                <string>{Label}</string>
                <key>ProgramArguments</key>
                <array>
                    <string>{program}</string>
                    <string>{Global.AutostartArg}</string>
                </array>
                <key>AssociatedBundleIdentifiers</key>
                <array>
                    <string>{BundleId}</string>
                </array>
                <key>LimitLoadToSessionType</key>
                <string>Aqua</string>
                <key>ProcessType</key>
                <string>Interactive</string>
                <key>RunAtLoad</key>
                <true/>
                <key>KeepAlive</key>
                <false/>
            </dict>
            </plist>

            """.ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Runs at every start: an agent written by an older build (the shell line), or for a copy that has since moved,
    /// is rewritten for this copy. Only the file changes; launchd reads it at the next sign-in.
    /// </summary>
    [SupportedOSPlatform("macos")]
    public static async Task TidyAsync(Config config)
    {
        try
        {
            var path = AgentPath();
            var exe = Utils.GetExePath();
            if (!config.GuiItem.AutoRun || IsTemporaryCopy(exe) || !MayOwnAgent() || !File.Exists(path))
            {
                return;
            }
            var wanted = Plist(exe);
            if (await File.ReadAllTextAsync(path) != wanted)
            {
                await File.WriteAllTextAsync(path, wanted);
                Logging.SaveLog("V2pn: the sign-in agent was rewritten to start V2pn directly");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnMacAutostart), ex);
        }
    }
}
