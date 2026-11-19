using Microsoft.Win32;

namespace ServiceLib;

/// <summary>
/// One V2pn starts with Windows. v2rayN names its sign-in entry after the folder it runs from, so every
/// copy with autostart on in its settings (an old install, a test build with a copied config) adds its own
/// entry and nothing ever removes one. They then start together and fight over the local port and the
/// system proxy. V2pn uses one fixed name, removes entries that point nowhere or at throwaway copies,
/// and never registers a build-output or temp-folder copy.
/// </summary>
public static class V2pnAutostart
{
    private static readonly string _tag = nameof(V2pnAutostart);

    /// <summary>The name v2rayN's scheme gave this copy's entry.</summary>
    public static string LegacyName(string startupPath) => $"{Global.AutoRunName}_{Utils.GetMd5(startupPath)}";

    /// <summary>A copy that must never start with Windows: a build output folder or anything under temp.</summary>
    public static bool IsThrowawayCopy(string? exePath, string? tempPath = null)
    {
        if (exePath.IsNullOrEmpty())
        {
            return false;
        }
        var path = exePath.Replace('/', '\\');
        var temp = (tempPath ?? Path.GetTempPath()).Replace('/', '\\').TrimEnd('\\') + "\\";
        return path.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
               || Regex.IsMatch(path, @"\\bin\\(Debug|Release)\\", RegexOptions.IgnoreCase);
    }

    /// <summary>The program of a Run value such as <c>"C:\V2pn\V2pn.exe" --autostart</c>.</summary>
    public static string? ExeOf(string? command)
    {
        if (command.IsNullOrEmpty())
        {
            return null;
        }
        var text = command.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }
        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? text[..(exe + 4)] : text;
    }

    public static bool SamePath(string? a, string? b) =>
        a.IsNotEmpty() && b.IsNotEmpty()
        && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A Run entry nobody should keep: its program is gone, it is a throwaway copy, or it is this copy
    /// under the old name.
    /// </summary>
    public static bool IsStale(string name, string? command, string thisExe, Func<string, bool> exists)
    {
        var exe = ExeOf(command);
        if (exe.IsNullOrEmpty() || !exists(exe) || IsThrowawayCopy(exe))
        {
            return true;
        }
        return SamePath(exe, thisExe) && name != Global.AutoRunName;
    }

    /// <summary>
    /// Brings the sign-in entries in line with the "start with Windows" setting. Runs at every start, in the
    /// background. Without administrator rights a task created with them cannot be replaced, so such a task
    /// is left to do the job and no Run value is added beside it (two entries would start V2pn twice).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static async Task TidyAsync(Config config)
    {
        try
        {
            var exe = Utils.GetExePath();
            if (IsThrowawayCopy(exe))
            {
                return;
            }
            var admin = Utils.IsAdministrator();
            var wanted = config.GuiItem.AutoRun;
            var legacy = LegacyName(Utils.StartupPath());
            var tasks = TaskNames();
            var ownTask = OwnTask(tasks, exe, legacy);

            if (!wanted)
            {
                RemoveRunEntries((name, command) => IsStale(name, command, exe, File.Exists) || SamePath(ExeOf(command), exe));
                if (admin && ownTask != null)
                {
                    AutoStartupHandler.AutoStartTaskService(ownTask, "", "");
                }
                return;
            }

            bool carriesArg;
            if (admin)
            {
                if (ownTask != Global.AutoRunName || !TaskXml(ownTask).Contains(Global.AutostartArg))
                {
                    AutoStartupHandler.AutoStartTaskService(Global.AutoRunName, exe, "", Global.AutostartArg);
                }
                // The old entries go only once the new task is really there: a failed create must not
                // leave V2pn with no sign-in entry at all.
                if (!TaskNames().Contains(Global.AutoRunName))
                {
                    Logging.SaveLog("V2pn: the sign-in task could not be created; older entries are kept");
                    return;
                }
                RemoveRunEntries((_, _) => true);
                foreach (var task in tasks.Where(t => t != Global.AutoRunName))
                {
                    AutoStartupHandler.AutoStartTaskService(task, "", "");
                }
                carriesArg = true;
            }
            else if (ownTask != null)
            {
                RemoveRunEntries((_, _) => true);
                carriesArg = TaskXml(ownTask).Contains(Global.AutostartArg);
            }
            else
            {
                var good = AutoStartupHandler.AutostartCommand(exe);
                RemoveRunEntries((name, command) => name != Global.AutoRunName || command != good);
                if (WindowsUtils.RegReadValue(Global.AutoRunRegPath, Global.AutoRunName, "") != good)
                {
                    WindowsUtils.RegWriteValue(Global.AutoRunRegPath, Global.AutoRunName, good);
                }
                carriesArg = true;
            }

            if (config.UiItem.V2pnAutostartArg != carriesArg)
            {
                config.UiItem.V2pnAutostartArg = carriesArg;
                await ConfigTextHandler.SaveConfig();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    /// <summary>The scheduled task that starts this copy, under the fixed or the old name.</summary>
    [SupportedOSPlatform("windows")]
    public static string? OwnTask(List<string>? tasks = null, string? exe = null, string? legacy = null)
    {
        tasks ??= TaskNames();
        exe ??= Utils.GetExePath();
        legacy ??= LegacyName(Utils.StartupPath());
        if (tasks.Contains(legacy))
        {
            return legacy;
        }
        return tasks.Contains(Global.AutoRunName) && TaskXml(Global.AutoRunName).Contains(exe, StringComparison.OrdinalIgnoreCase)
            ? Global.AutoRunName
            : null;
    }

    [SupportedOSPlatform("windows")]
    public static List<(string Name, string Command)> RunEntries()
    {
        var result = new List<(string, string)>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Global.AutoRunRegPath, false);
            foreach (var name in key?.GetValueNames() ?? [])
            {
                if (name.StartsWith(Global.AutoRunName, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add((name, key!.GetValue(name) as string ?? string.Empty));
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        return result;
    }

    [SupportedOSPlatform("windows")]
    public static void RemoveRunEntries(Func<string, string, bool> remove)
    {
        foreach (var (name, command) in RunEntries())
        {
            if (remove(name, command))
            {
                Logging.SaveLog($"V2pn: sign-in entry removed: {name}");
                WindowsUtils.RegWriteValue(Global.AutoRunRegPath, name, "");
            }
        }
    }

    /// <summary>Names of V2pn's scheduled tasks, without the leading backslash.</summary>
    [SupportedOSPlatform("windows")]
    public static List<string> TaskNames()
    {
        var (code, output) = Schtasks("/Query", "/FO", "CSV", "/NH");
        if (code != 0)
        {
            return [];
        }
        return output.Split('\n')
            .Select(line => line.Trim().TrimStart('"'))
            .Select(line => line.Contains('"') ? line[..line.IndexOf('"')] : line)
            .Select(name => name.TrimStart('\\'))
            .Where(name => name.StartsWith(Global.AutoRunName, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();
    }

    [SupportedOSPlatform("windows")]
    private static string TaskXml(string? name)
    {
        if (name.IsNullOrEmpty())
        {
            return string.Empty;
        }
        var (code, output) = Schtasks("/Query", "/TN", name, "/XML");
        return code == 0 ? output : string.Empty;
    }

    /// <summary>schtasks by its full path: a bare name would let a file planted beside V2pn.exe run instead.</summary>
    public static string SchtasksPath => Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    internal static (int Code, string Output) Schtasks(params string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = SchtasksPath,
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
                return (-1, string.Empty);
            }
            // Read before waiting: a full pipe would block schtasks and the wait with it.
            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return (-1, string.Empty);
        }
    }
}
