namespace ServiceLib.Handler;

public static class AutoStartupHandler
{
    private static readonly string _tag = "AutoStartupHandler";

    public static async Task<bool> UpdateTask(Config config)
    {
        if (Utils.IsWindows())
        {
            // V2pn: a build-output or temp-folder copy never starts with Windows, whatever its settings say.
            if (V2pnAutostart.IsThrowawayCopy(Utils.GetExePath()))
            {
                Logging.SaveLog("V2pn: this copy runs from a build or temp folder; no sign-in entry is written");
                return true;
            }
            await ClearTaskWindows();

            if (config.GuiItem.AutoRun)
            {
                await SetTaskWindows();
            }
            // V2pn: every entry written from now on carries the sign-in argument.
            config.UiItem.V2pnAutostartArg = true;
        }
        else if (Utils.IsLinux())
        {
            await ClearTaskLinux();

            if (config.GuiItem.AutoRun)
            {
                await SetTaskLinux();
            }
        }
        else if (Utils.IsMacOS())
        {
            // V2pn: started from the disk image or a quarantine copy, the path will not exist at the next sign-in.
            var exePath = Utils.GetExePath();
            if (exePath.Contains("/AppTranslocation/") || exePath.StartsWith("/Volumes/"))
            {
                Logging.SaveLog("V2pn: this copy runs from a disk image; move it to Applications to start at sign-in");
                return true;
            }
            await ClearTaskOSX();

            if (config.GuiItem.AutoRun)
            {
                await SetTaskOSX();
            }
        }

        return true;
    }

    /// <summary>
    /// V2pn: "start in the tray" applies to the sign-in launch only. A launch without the argument is
    /// manual, unless the sign-in entry is still one written before the argument existed (or the
    /// platform's entry has no argument at all): then it behaves as before and every launch starts hidden.
    /// </summary>
    public static bool StartsHidden(Config config, bool autostartArg)
    {
        if (!config.UiItem.AutoHideStartup)
        {
            return false;
        }
        // The Linux entry carries no argument. The macOS one always has (there was no V2pn for macOS
        // before the argument existed); on Windows an entry from an older build may not.
        return autostartArg || Utils.IsLinux() || (Utils.IsWindows() && !config.UiItem.V2pnAutostartArg);
    }

    /// <summary>The registry Run value: the quoted program and the sign-in argument.</summary>
    public static string AutostartCommand(string exePath) => $"{exePath.AppendQuotes()} {Global.AutostartArg}";

    /// <summary>
    /// V2pn: an entry written by an older build starts the app without the sign-in argument. Rewrite it
    /// once, in the form it already has: the registry value in place, the scheduled task only when running
    /// as administrator (the only way it can be replaced). Until that succeeds the flag stays off, and
    /// launches keep the old behaviour.
    /// </summary>
    public static async Task<bool> MigrateAutostartArg(Config config)
    {
        if (!Utils.IsWindows())
        {
            return false;
        }
        // V2pn: the old one-time rewrite grew into a tidy-up that runs at every start.
        await V2pnAutostart.TidyAsync(config);
        return true;
    }

    #region Windows

    [SupportedOSPlatform("windows")]
    private static async Task ClearTaskWindows()
    {
        // V2pn: every V2pn entry goes, under the fixed name or an old per-folder one.
        V2pnAutostart.RemoveRunEntries((_, _) => true);
        if (Utils.IsAdministrator())
        {
            foreach (var task in V2pnAutostart.TaskNames())
            {
                AutoStartTaskService(task, "", "");
            }
        }

        await Task.CompletedTask;
    }

    [SupportedOSPlatform("windows")]
    private static async Task SetTaskWindows()
    {
        try
        {
            var autoRunName = GetAutoRunNameWindows();
            var exePath = Utils.GetExePath();
            if (Utils.IsAdministrator())
            {
                AutoStartTaskService(autoRunName, exePath, "", Global.AutostartArg);
            }
            // V2pn: a task made with administrator rights cannot be removed without them. It already
            // starts this copy; a Run value beside it would start V2pn a second time.
            else if (V2pnAutostart.OwnTask() == null)
            {
                WindowsUtils.RegWriteValue(Global.AutoRunRegPath, autoRunName, AutostartCommand(exePath));
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// Auto Start via TaskService
    /// </summary>
    /// <param name="taskName"></param>
    /// <param name="fileName"></param>
    /// <param name="description"></param>
    /// <param name="arguments">Command-line arguments of the task (V2pn: the sign-in argument).</param>
    /// <exception cref="ArgumentNullException"></exception>
    [SupportedOSPlatform("windows")]
    public static void AutoStartTaskService(string taskName, string fileName, string description, string arguments = "")
    {
#if false
        if (taskName.IsNullOrEmpty())
        {
            return;
        }

        var logonUser = WindowsIdentity.GetCurrent().Name;
        using var taskService = new Microsoft.Win32.TaskScheduler.TaskService();
        var tasks = taskService.RootFolder.GetTasks(new Regex(taskName));
        if (fileName.IsNullOrEmpty())
        {
            foreach (var t in tasks)
            {
                taskService.RootFolder.DeleteTask(t.Name);
            }
            return;
        }

        var task = taskService.NewTask();
        task.RegistrationInfo.Description = description;
        task.Settings.DisallowStartIfOnBatteries = false;
        task.Settings.StopIfGoingOnBatteries = false;
        task.Settings.RunOnlyIfIdle = false;
        task.Settings.IdleSettings.StopOnIdleEnd = false;
        task.Settings.ExecutionTimeLimit = TimeSpan.Zero;
        task.Settings.Priority = ProcessPriorityClass.Normal;
        task.Triggers.Add(new Microsoft.Win32.TaskScheduler.LogonTrigger { UserId = logonUser });
        task.Principal.RunLevel = Microsoft.Win32.TaskScheduler.TaskRunLevel.Highest;
        task.Actions.Add(new Microsoft.Win32.TaskScheduler.ExecAction(fileName.AppendQuotes(), null, Path.GetDirectoryName(fileName)));

        taskService.RootFolder.RegisterTaskDefinition(taskName, task);
#endif
        if (string.IsNullOrEmpty(taskName))
        {
            return;
        }

        // V2pn: one argument per element. As a single string schtasks rejected it, so no task was ever deleted.
        RunSchtasks("/Delete", "/TN", taskName, "/F");

        if (string.IsNullOrEmpty(fileName))
        {
            return;
        }

        var workingDir = Path.GetDirectoryName(fileName) ?? string.Empty;

        var taskXml = EmbedUtils.GetEmbedText(Global.TaskXmlTemplateFileName);

        taskXml = taskXml
            .Replace("{{DESCRIPTION}}", System.Security.SecurityElement.Escape(description ?? ""))
            .Replace("{{EXEC_PATH}}", System.Security.SecurityElement.Escape(fileName ?? ""))
            .Replace("{{ARGUMENTS}}", System.Security.SecurityElement.Escape(arguments ?? ""))
            .Replace("{{WORKING_DIRECTORY}}", System.Security.SecurityElement.Escape(workingDir ?? ""));

        var taskXmlFileName = Utils.GetTempPath($"{Utils.GetGuid(false)}.xml");

        File.WriteAllText(taskXmlFileName, taskXml);

        RunSchtasks(
            "/Create",
            "/TN", taskName,
            "/XML", taskXmlFileName,
            "/F"
        );
        // V2pn: the definition of a task that runs with the highest rights should not stay in a temp folder.
        try
        {
            File.Delete(taskXmlFileName);
        }
        catch
        {
        }
    }

    private static bool RunSchtasks(params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = V2pnAutostart.SchtasksPath, // V2pn: full path
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi);

        if (process == null)
        {
            return false;
        }

        process.WaitForExit();

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!string.IsNullOrEmpty(output))
        {
            Logging.SaveLog($"RunSchtasks output: {output}");
        }
        if (!string.IsNullOrEmpty(error))
        {
            Logging.SaveLog($"RunSchtasks error: {error}");
        }

        return process.ExitCode == 0;
    }

    private static string GetAutoRunNameWindows()
    {
        return Global.AutoRunName; // V2pn: one fixed name, see V2pnAutostart
    }

    #endregion Windows

    #region Linux

    [SupportedOSPlatform("linux")]
    private static async Task ClearTaskLinux()
    {
        try
        {
            File.Delete(GetHomePathLinux());
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        await Task.CompletedTask;
    }

    [SupportedOSPlatform("linux")]
    private static async Task SetTaskLinux()
    {
        try
        {
            var linuxConfig = EmbedUtils.GetEmbedText(Global.LinuxAutostartConfig);
            if (linuxConfig.IsNotEmpty())
            {
                linuxConfig = linuxConfig.Replace("$ExecPath$", Utils.GetExePath());
                Logging.SaveLog(linuxConfig);

                var homePath = GetHomePathLinux();
                await File.WriteAllTextAsync(homePath, linuxConfig);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    [SupportedOSPlatform("linux")]
    private static string GetHomePathLinux()
    {
        var homePath = Path.Combine(Utils.GetHomePath(), ".config", "autostart", $"{Global.AppName}.desktop");
        Directory.CreateDirectory(Path.GetDirectoryName(homePath));
        return homePath;
    }

    #endregion Linux

    #region macOS

    [SupportedOSPlatform("macos")]
    private static async Task ClearTaskOSX()
    {
        try
        {
            var launchAgentPath = GetLaunchAgentPathMacOS();
            if (File.Exists(launchAgentPath))
            {
                var args = new[] { "-c", $"launchctl unload -w \"{launchAgentPath}\"" };
                await Utils.GetCliWrapOutput(Global.LinuxBash, args);

                File.Delete(launchAgentPath);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    [SupportedOSPlatform("macos")]
    private static async Task SetTaskOSX()
    {
        try
        {
            var plistContent = GenerateLaunchAgentPlist();
            var launchAgentPath = GetLaunchAgentPathMacOS();
            await File.WriteAllTextAsync(launchAgentPath, plistContent);

            var args = new[] { "-c", $"launchctl load -w \"{launchAgentPath}\"" };
            await Utils.GetCliWrapOutput(Global.LinuxBash, args);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    [SupportedOSPlatform("macos")]
    private static string GetLaunchAgentPathMacOS()
    {
        var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var launchAgentPath = Path.Combine(homePath, "Library", "LaunchAgents", $"{Global.AppName}-LaunchAgent.plist");
        Directory.CreateDirectory(Path.GetDirectoryName(launchAgentPath));
        return launchAgentPath;
    }

    [SupportedOSPlatform("macos")]
    private static string GenerateLaunchAgentPlist()
    {
        var exePath = Utils.GetExePath();
        var appName = Path.GetFileNameWithoutExtension(exePath);
        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>Label</key>
    <string>{Global.AppName}-LaunchAgent</string>
    <key>ProgramArguments</key>
    <array>
        <string>/bin/sh</string>
        <string>-c</string>
        <string>if ! pgrep -x ""{appName}"" > /dev/null; then ""{exePath}"" {Global.AutostartArg}; fi</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <false/>
</dict>
</plist>";
    }

    #endregion macOS
}
