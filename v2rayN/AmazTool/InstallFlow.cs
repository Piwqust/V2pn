using System.Diagnostics;

namespace AmazTool;

/// <summary>
/// V2pn: the whole update, start to finish: wait for V2pn to close, install the package with
/// <see cref="Installer"/>, start V2pn and watch that it stays up. Whatever happens, V2pn is
/// started again: the new version, or the previous one after a rollback. A rollback leaves
/// failed.txt next to the package so the app does not try that version again on its own.
/// </summary>
internal static class InstallFlow
{
    private const string FailedMarker = "failed.txt";

    // A version that dies within this time of starting is taken back.
    private static readonly TimeSpan Probation = TimeSpan.FromSeconds(12);

    private static StreamWriter? _logFile;

    public static void Run(string zip, int? appPid)
    {
        var workDir = Path.GetDirectoryName(Path.GetFullPath(zip)) ?? Utils.StartupPath();
        OpenLog(workDir);
        var installer = new Installer(Installer.InstallRoot(Utils.StartupPath()), Log);
        var version = Path.GetFileNameWithoutExtension(zip).Replace("V2pn-", string.Empty);
        Log($"Installing {zip}");

        if (!File.Exists(zip))
        {
            Log(Resx.Resource.UpgradeFileNotFound);
            Start(installer);
            return;
        }

        WaitForApp(appPid);
        try
        {
            installer.Stage(zip);
            installer.Swap();
        }
        catch (Exception ex)
        {
            // Stage touches nothing and Swap puts everything back before throwing.
            Log($"{Resx.Resource.FailedUpgrade} {ex}");
            Fail(workDir, version, installer);
            return;
        }

        Log(Resx.Resource.Restartv2rayN);
        Process? app;
        try
        {
            app = Start(installer);
        }
        catch (Exception ex)
        {
            Log($"The new version did not start: {ex.Message}");
            app = null;
        }
        // Any exit this early counts, also a clean one: V2pn leaves with code 0 when it cannot
        // read its settings, and a version that does that to everyone must not stay installed.
        var died = app == null || app.WaitForExit((int)Probation.TotalMilliseconds);
        if (died && app != null)
        {
            // Unless it handed over to another copy of itself (a restart as administrator).
            Thread.Sleep(1500);
            died = !AppIsRunning(installer);
        }
        if (died)
        {
            Log(app == null ? "Rolling back." : $"The new version exited after starting (code {app.ExitCode}). Rolling back.");
            try
            {
                installer.Rollback();
            }
            catch (Exception ex)
            {
                Log($"Rollback failed: {ex}");
            }
            Fail(workDir, version, installer);
            return;
        }

        installer.Commit();
        TryDelete(zip);
        Log("Done.");
    }

    private static void Fail(string workDir, string version, Installer installer)
    {
        try
        {
            File.WriteAllText(Path.Combine(workDir, FailedMarker), version + "\n");
        }
        catch (Exception ex)
        {
            Log($"Could not write {FailedMarker}: {ex.Message}");
        }
        installer.DeleteWork();
        try
        {
            Start(installer);
        }
        catch (Exception ex)
        {
            Log($"Could not start V2pn: {ex.Message}");
        }
    }

    private static Process? Start(Installer installer)
    {
        var exe = installer.AppExecutable();
        return Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
        });
    }

    private static bool AppIsRunning(Installer installer)
    {
        try
        {
            var exe = installer.AppExecutable();
            foreach (var process in Process.GetProcessesByName(Utils.V2rayN))
            {
                if (string.Equals(ProcessPath(process), exe, Utils.GetPathComparison()))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Could not list processes: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// Where a process runs from. On Windows this also answers for a copy running as
    /// administrator, which Process.MainModule refuses to describe to a normal process.
    /// </summary>
    private static string? ProcessPath(Process process)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return process.MainModule?.FileName;
            }
            const uint QueryLimitedInformation = 0x1000;
            var handle = OpenProcess(QueryLimitedInformation, false, (uint)process.Id);
            if (handle == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                var buffer = new char[1024];
                var size = (uint)buffer.Length;
                return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }
        catch
        {
            return null;
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, [System.Runtime.InteropServices.Out] char[] name, ref uint size);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>V2pn closes itself after starting this program; anything left of it is stopped.</summary>
    private static void WaitForApp(int? pid)
    {
        Log(Resx.Resource.TryTerminateProcess);
        try
        {
            if (pid is > 0)
            {
                using var app = Process.GetProcessById(pid.Value);
                if (!app.WaitForExit(30_000))
                {
                    app.Kill();
                    app.WaitForExit(5_000);
                }
            }
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (Exception ex)
        {
            Log(Resx.Resource.FailedTerminateProcess + ex.Message);
        }

        try
        {
            var appDir = Utils.StartupPath();
            foreach (var process in Process.GetProcessesByName(Utils.V2rayN))
            {
                var path = ProcessPath(process) ?? string.Empty;
                if (path.StartsWith(appDir, Utils.GetPathComparison()))
                {
                    process.Kill();
                    process.WaitForExit(5_000);
                }
            }
        }
        catch (Exception ex)
        {
            // Access may be denied without admin rights.
            Log(Resx.Resource.FailedTerminateProcess + ex.Message);
        }
        // Lets the system release the files of a process that has just ended.
        Thread.Sleep(500);
    }

    private static void OpenLog(string workDir)
    {
        try
        {
            Directory.CreateDirectory(workDir);
            _logFile = new StreamWriter(Path.Combine(workDir, "update.log"), append: false) { AutoFlush = true };
        }
        catch
        {
            _logFile = null;
        }
    }

    private static void Log(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
        Console.WriteLine(line);
        try
        {
            _logFile?.WriteLine(line);
        }
        catch
        {
            // The log is a convenience; the update goes on without it.
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex)
        {
            Log($"Could not delete {file}: {ex.Message}");
        }
    }
}
