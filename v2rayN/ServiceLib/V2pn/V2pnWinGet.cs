namespace ServiceLib;

/// <summary>
/// V2pn installed by winget (<c>winget install Piwqust.V2pn</c>). winget unpacks the release zip into
/// %LOCALAPPDATA%\Microsoft\WinGet\Packages\Piwqust.V2pn_...\ and keeps hashes of what it placed there,
/// so in that folder winget, not V2pn, replaces the app: V2pn reports a new version and hands the
/// update to <c>winget upgrade</c>. The data lives in %LOCALAPPDATA%\V2pn (v2rayN's rule for a WinGet
/// folder, Utils.HasWritePermission), so at start the cores of the package are copied there, and the
/// Start menu gets the shortcut winget does not make for a portable app.
/// </summary>
public static class V2pnWinGet
{
    public const string PackageId = "Piwqust.V2pn";
    private static readonly string _tag = nameof(V2pnWinGet);

    /// <summary>True when the app runs from a WinGet package folder (per user or per machine).</summary>
    public static bool IsInstall(string? baseDir = null) =>
        Utils.IsWindows() && (baseDir ?? Utils.GetBaseDirectory()).Replace('/', '\\')
            .Contains(@"\WinGet\Packages\", StringComparison.OrdinalIgnoreCase);

    /// <summary>The command that updates V2pn, for people and for the window.</summary>
    public static string UpgradeCommand => $"winget upgrade --id {PackageId} --exact";

    /// <summary>At start, after logging is set up: cores where V2pn looks for them, and a Start menu shortcut.</summary>
    public static void PrepareAtStart()
    {
        if (!IsInstall())
        {
            return;
        }
        try
        {
            SeedCores();
            EnsureShortcut();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    /// <summary>
    /// Closes V2pn and runs <see cref="UpgradeCommand"/> in a window the person can watch, then starts
    /// V2pn again. winget cannot replace the files of a running app, hence the order.
    /// </summary>
    public static bool StartUpgrade()
    {
        try
        {
            var exe = Utils.GetExePath().Replace("'", "''");
            var command = $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; "
                + $"{UpgradeCommand} --accept-source-agreements --accept-package-agreements; "
                + $"Start-Process -FilePath '{exe}'";
            var start = new ProcessStartInfo
            {
                FileName = PowerShell,
                UseShellExecute = false,
            };
            foreach (var argument in new[] { "-NoProfile", "-Command", command })
            {
                start.ArgumentList.Add(argument);
            }
            using var process = Process.Start(start);
            Logging.SaveLog($"V2pn: updating through winget ({UpgradeCommand})");
            return process != null;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return false;
        }
    }

    private static string PowerShell => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>Copies the package's cores that the data folder lacks; ones already there are kept, as an update keeps them.</summary>
    private static void SeedCores()
    {
        var from = Path.Combine(Utils.GetBaseDirectory(), "bin");
        var to = Path.Combine(Utils.StartupPath(), "bin");
        if (!Directory.Exists(from) || string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var added = 0;
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            if (File.Exists(target))
            {
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
            added++;
        }
        if (added > 0)
        {
            Logging.SaveLog($"V2pn: {added} core files copied from the winget package to {to}");
        }
    }

    private static void EnsureShortcut()
    {
        var link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), $"{Global.AppName}.lnk");
        if (File.Exists(link))
        {
            return;
        }
        var exe = Utils.GetExePath();
        var script = $"$s = (New-Object -ComObject WScript.Shell).CreateShortcut('{link.Replace("'", "''")}'); "
            + $"$s.TargetPath = '{exe.Replace("'", "''")}'; $s.WorkingDirectory = '{Path.GetDirectoryName(exe)!.Replace("'", "''")}'; $s.Save()";
        var start = new ProcessStartInfo
        {
            FileName = PowerShell,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", script })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start);
        process?.WaitForExit(15_000);
        Logging.SaveLog($"V2pn: Start menu shortcut {(File.Exists(link) ? "added" : "could not be added")}: {link}");
    }
}
