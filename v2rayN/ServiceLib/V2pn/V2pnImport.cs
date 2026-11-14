using Microsoft.Win32;

namespace ServiceLib;

/// <summary>A v2rayN found on this computer: where its data is and how much it holds.</summary>
public sealed record V2rayNInstall(string DataDir, string? ExePath, int Servers, int Subscriptions)
{
    public string ConfigDir => Path.Combine(DataDir, V2pnImport.ConfigFolder);
}

/// <summary>
/// V2pn: moving to V2pn from v2rayN. V2pn is built on v2rayN and keeps its files (guiConfigs/guiNConfig.json
/// and the guiNDB.db database), so servers, subscriptions, routing and settings come over by copying them.
///
/// The copy is made at start, before V2pn reads its own settings, never under a running V2pn: a person
/// asks for it in the window, V2pn notes where from (<see cref="Schedule"/>) and restarts. "--import-v2rayn"
/// (the install script passes it) does the same on a first start without asking. v2rayN is closed first,
/// its sign-in entry removed so the two do not fight over the port and the system proxy at the next
/// sign-in; its own files are left as they are. What V2pn had before is kept in guiTemps.
/// </summary>
public static class V2pnImport
{
    public const string Arg = "--import-v2rayn";
    public const string ConfigFolder = "guiConfigs";
    private const string DbName = "guiNDB.db";
    private const string MarkerName = "V2pnImport.txt";
    private const string V2rayNExe = "v2rayN";
    private const string V2rayNAutoRun = "v2rayNAutoRun";
    private static readonly string _tag = nameof(V2pnImport);

    /// <summary>The install this start copied from, for the window to say so; null when nothing was copied.</summary>
    public static V2rayNInstall? Imported { get; private set; }

    /// <summary>v2rayN installs with data, newest first. Never V2pn's own folder.</summary>
    public static List<V2rayNInstall> Find()
    {
        var own = Normalize(Utils.StartupPath());
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = new List<(V2rayNInstall Install, DateTime Used)>();
        void Consider(string? dataDir, string? exe)
        {
            if (dataDir.IsNullOrEmpty())
            {
                return;
            }
            var dir = Normalize(dataDir);
            if (dir == own || !HasData(dir) || !seen.Add(dir))
            {
                return;
            }
            var install = Describe(dir, exe ?? ExeFor(dir));
            found.Add((install, File.GetLastWriteTimeUtc(Path.Combine(dir, ConfigFolder, DbName))));
        }

        try
        {
            foreach (var exe in CandidateExes())
            {
                Consider(DataDirOf(exe), exe);
            }
            foreach (var dir in CandidateDataDirs())
            {
                Consider(dir, null);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        return found.OrderByDescending(f => f.Used).Select(f => f.Install).ToList();
    }

    /// <summary>A folder holding v2rayN's data: guiConfigs with the database in it.</summary>
    public static bool HasData(string? dir) =>
        !dir.IsNullOrEmpty() && File.Exists(Path.Combine(dir, ConfigFolder, DbName));

    /// <summary>The install at <paramref name="dataDir"/>, with the number of servers and subscriptions it holds.</summary>
    public static V2rayNInstall Describe(string dataDir, string? exe = null)
    {
        var servers = Count(dataDir, "ProfileItem");
        var subscriptions = Count(dataDir, "SubItem");
        return new V2rayNInstall(Normalize(dataDir), exe, servers, subscriptions);
    }

    /// <summary>
    /// v2rayN keeps its data next to v2rayN.exe, unless that folder is not writable or is a WinGet
    /// package: then in %LOCALAPPDATA%\v2rayN (the rule V2pn inherited, see Utils.HasWritePermission).
    /// </summary>
    public static string? DataDirOf(string? exe)
    {
        var dir = Path.GetDirectoryName(exe ?? string.Empty);
        if (dir.IsNullOrEmpty())
        {
            return null;
        }
        if (HasData(dir))
        {
            return dir;
        }
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "v2rayN");
        return HasData(local) ? local : dir;
    }

    /// <summary>Asks the next start to copy from <paramref name="dataDir"/>, once this V2pn has closed.</summary>
    public static void Schedule(string dataDir)
    {
        File.WriteAllLines(MarkerPath, [dataDir, Environment.ProcessId.ToString()]);
        Logging.SaveLog($"V2pn: moving from v2rayN at {dataDir} at the next start");
    }

    /// <summary>
    /// At start, before the settings are read: makes the copy a person asked for, or the copy a fresh
    /// V2pn makes from the newest v2rayN found, asked for with <see cref="Arg"/> or with
    /// <see cref="AutoMarker"/> in guiTemps/V2pnImport.txt (the install script writes it, because it
    /// starts V2pn through Explorer, which passes no arguments). Returns the install copied from, or null.
    /// </summary>
    public static V2rayNInstall? ApplyAtStart(IEnumerable<string> args)
    {
        try
        {
            if (File.Exists(MarkerPath))
            {
                var lines = File.ReadAllLines(MarkerPath).Select(l => l.Trim()).ToArray();
                File.Delete(MarkerPath);
                if (lines.Length > 1 && int.TryParse(lines[1], out var previous))
                {
                    WaitForExit(previous);
                }
                if (lines.Length > 0 && lines[0] == AutoMarker)
                {
                    return Imported = ImportNewest();
                }
                if (lines.Length > 0 && HasData(lines[0]))
                {
                    var asked = Describe(lines[0], ExeFor(lines[0]));
                    Stop(asked);
                    return Imported = Apply(asked) ? asked : null;
                }
                return null;
            }

            if (args.Contains(Arg))
            {
                return Imported = ImportNewest();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        return null;
    }

    /// <summary>What the install script writes to guiTemps/V2pnImport.txt: move from the newest v2rayN found.</summary>
    public const string AutoMarker = "auto";

    /// <summary>A fresh V2pn takes over the newest v2rayN with servers; one that has its own keeps them.</summary>
    private static V2rayNInstall? ImportNewest()
    {
        if (OwnDataExists())
        {
            Logging.SaveLog("V2pn: already has servers, nothing moved from v2rayN");
            return null;
        }
        var newest = Find().FirstOrDefault(i => i.Servers > 0 || i.Subscriptions > 0);
        if (newest == null)
        {
            Logging.SaveLog("V2pn: no v2rayN to move from");
            return null;
        }
        if (!Stop(newest))
        {
            Logging.SaveLog("V2pn: v2rayN could not be closed; copying its data anyway");
        }
        return Apply(newest) ? newest : null;
    }

    /// <summary>
    /// Closes v2rayN and the cores it started. False when some of it is still running, for example a
    /// v2rayN that runs as administrator while V2pn does not: the person then closes it from its tray icon.
    /// </summary>
    public static bool Stop(V2rayNInstall install)
    {
        var roots = new[] { install.DataDir, Path.GetDirectoryName(install.ExePath ?? string.Empty) }
            .Where(r => !r.IsNullOrEmpty())
            .Select(r => Normalize(r!) + Path.DirectorySeparatorChar)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var allGone = true;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var path = ImagePath(process);
                var mine = path != null && roots.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase));
                // A v2rayN whose path cannot be read (it runs as administrator) still counts when it is the only kind there is.
                // On a Mac its data is apart from the app (~/Library/Application Support/v2rayN), so v2rayN.app counts by name.
                var named = process.ProcessName.Equals(V2rayNExe, StringComparison.OrdinalIgnoreCase)
                            && (path == null || !OperatingSystem.IsWindows());
                if (!mine && !named)
                {
                    continue;
                }
                try
                {
                    // Its cores go with it, or they keep the local port.
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                    Logging.SaveLog($"V2pn: closed {process.ProcessName} ({process.Id}) of v2rayN");
                }
                catch (Exception ex)
                {
                    allGone = false;
                    Logging.SaveLog($"V2pn: could not close {process.ProcessName} ({process.Id}) of v2rayN: {ex.Message}");
                }
            }
        }
        if (allGone)
        {
            // Lets the system release the database and the port of a process that has just ended.
            Thread.Sleep(500);
        }
        return allGone;
    }

    /// <summary>True when v2rayN of this install is running.</summary>
    public static bool IsRunning(V2rayNInstall install)
    {
        foreach (var process in Process.GetProcessesByName(V2rayNExe))
        {
            using (process)
            {
                var path = ImagePath(process);
                if (path == null || Normalize(DataDirOf(path) ?? string.Empty) == install.DataDir)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// The copy itself. V2pn's settings are saved first to guiTemps/V2pnImportBackup-*; v2rayN's
    /// guiConfigs replaces them, cores V2pn lacks are added, and v2rayN's sign-in entry is removed.
    /// </summary>
    public static bool Apply(V2rayNInstall install)
    {
        try
        {
            var target = Utils.GetConfigPath();
            Directory.CreateDirectory(target);
            var backup = Utils.GetTempPath($"V2pnImportBackup-{DateTime.Now:yyyyMMdd-HHmmss}");
            CopyTree(target, backup, overwrite: true);

            // A journal left from V2pn's own database must not be replayed into v2rayN's.
            foreach (var leftover in new[] { "-wal", "-shm", "-journal" })
            {
                var file = Path.Combine(target, DbName + leftover);
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            CopyTree(install.ConfigDir, target, overwrite: true, skip: name => name.Contains(".bak", StringComparison.OrdinalIgnoreCase));

            var cores = CopyTree(Path.Combine(install.DataDir, "bin"), Path.Combine(Utils.StartupPath(), "bin"), overwrite: false);
            if (OperatingSystem.IsWindows())
            {
                RemoveAutostart(install);
            }
            else if (OperatingSystem.IsMacOS())
            {
                RemoveLaunchAgent();
            }
            Logging.SaveLog($"V2pn: moved from v2rayN at {install.DataDir}: {install.Servers} servers, {install.Subscriptions} subscriptions, {cores} core files added; V2pn's previous settings are in {backup}");
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return false;
        }
    }

    private static string MarkerPath => Path.Combine(Utils.GetTempPath(), MarkerName);

    /// <summary>V2pn already has servers or subscriptions of its own (an empty database is a fresh V2pn).</summary>
    private static bool OwnDataExists()
    {
        var own = Utils.StartupPath();
        return HasData(own) && (Count(own, "ProfileItem") > 0 || Count(own, "SubItem") > 0);
    }

    private static int Count(string dataDir, string table)
    {
        try
        {
            var path = Path.Combine(dataDir, ConfigFolder, DbName);
            using var db = new SQLiteConnection(new SQLiteConnectionString(path, SQLiteOpenFlags.ReadOnly, false));
            return db.ExecuteScalar<int>($"SELECT count(*) FROM \"{table}\"");
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}: {table} in {dataDir}: {ex.Message}");
            return 0;
        }
    }

    /// <summary>Copies a folder over another; returns how many files were written.</summary>
    private static int CopyTree(string from, string to, bool overwrite, Func<string, bool>? skip = null)
    {
        if (!Directory.Exists(from))
        {
            return 0;
        }
        var written = 0;
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, file);
            if (skip?.Invoke(Path.GetFileName(file)) == true)
            {
                continue;
            }
            var destination = Path.Combine(to, relative);
            if (!overwrite && File.Exists(destination))
            {
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
            written++;
        }
        return written;
    }

    /// <summary>Waits for the V2pn that asked for the move: it saves its settings on the way out.</summary>
    private static void WaitForExit(int pid)
    {
        if (pid <= 0 || pid == Environment.ProcessId)
        {
            return;
        }
        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(30_000);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (Exception ex)
        {
            // Not worth giving up the move for; at worst the old copy's last save is lost.
            Logging.SaveLog($"{_tag}: {ex.Message}");
        }
    }

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string? ExeFor(string dataDir)
    {
        var exe = Path.Combine(dataDir, Utils.GetExeName(V2rayNExe));
        if (File.Exists(exe))
        {
            return exe;
        }
        return CandidateExes().FirstOrDefault(e => Normalize(DataDirOf(e) ?? string.Empty) == Normalize(dataDir));
    }

    /// <summary>v2rayN.exe files this computer points at: sign-in entries, running copies, the usual folders.</summary>
    private static IEnumerable<string> CandidateExes()
    {
        var result = new List<string>();
        if (!Utils.IsWindows())
        {
            return result;
        }
        if (OperatingSystem.IsWindows())
        {
            result.AddRange(RunEntries().Select(e => V2pnAutostart.ExeOf(e.Command)).OfType<string>());
        }
        foreach (var process in Process.GetProcessesByName(V2rayNExe))
        {
            using (process)
            {
                if (ImagePath(process) is { } path)
                {
                    result.Add(path);
                }
            }
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        result.AddRange(Search(Path.Combine(local, "Microsoft", "WinGet", "Packages"), 3, dir => dir.Contains("v2rayN", StringComparison.OrdinalIgnoreCase)));
        result.Add(Path.Combine(home, "scoop", "apps", "v2rayN", "current", "v2rayN.exe"));
        foreach (var root in new[]
                 {
                     Path.Combine(home, "Downloads"),
                     Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                     Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                     Path.Combine(local, "Programs"),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     home,
                 })
        {
            result.AddRange(Search(root, 3));
        }
        return result.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Data folders v2rayN uses when it does not keep data next to its exe.</summary>
    private static IEnumerable<string> CandidateDataDirs()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "v2rayN");
        yield return Path.Combine(home, "scoop", "persist", "v2rayN");
        if (Utils.IsMacOS())
        {
            yield return Path.Combine(home, "Library", "Application Support", "v2rayN");
            yield return Path.Combine(home, ".local", "share", "v2rayN");
            yield return "/Applications/v2rayN.app/Contents/MacOS";
            yield return Path.Combine(home, "Applications", "v2rayN.app", "Contents", "MacOS");
        }
    }

    /// <summary>v2rayN.exe within <paramref name="depth"/> levels of <paramref name="root"/>, skipping folders it cannot read.</summary>
    private static IEnumerable<string> Search(string root, int depth, Func<string, bool>? enter = null)
    {
        var result = new List<string>();
        void Walk(string dir, int level)
        {
            try
            {
                var exe = Path.Combine(dir, "v2rayN.exe");
                if (File.Exists(exe))
                {
                    result.Add(exe);
                }
                if (level >= depth)
                {
                    return;
                }
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(sub);
                    // Large trees that never hold an app; the first level filter keeps the walk short.
                    if (name.StartsWith('.') || name is "node_modules" or "AppData" or "Windows" or "$Recycle.Bin")
                    {
                        continue;
                    }
                    if (level == 0 && enter != null && !enter(name))
                    {
                        continue;
                    }
                    Walk(sub, level + 1);
                }
            }
            catch
            {
                // Folders without access are skipped.
            }
        }
        if (Directory.Exists(root))
        {
            Walk(root, 0);
        }
        return result;
    }

    [SupportedOSPlatform("windows")]
    private static List<(string Name, string Command)> RunEntries()
    {
        var result = new List<(string, string)>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Global.AutoRunRegPath, false);
            foreach (var name in key?.GetValueNames() ?? [])
            {
                if (name.StartsWith(V2rayNAutoRun, StringComparison.OrdinalIgnoreCase))
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

    /// <summary>v2rayN's sign-in entries for this install: Run values and, when it ran as administrator, tasks.</summary>
    [SupportedOSPlatform("windows")]
    private static void RemoveAutostart(V2rayNInstall install)
    {
        bool Ours(string? exe) => exe != null && Normalize(DataDirOf(exe) ?? string.Empty) == install.DataDir;

        foreach (var (name, command) in RunEntries())
        {
            if (Ours(V2pnAutostart.ExeOf(command)))
            {
                WindowsUtils.RegWriteValue(Global.AutoRunRegPath, name, "");
                Logging.SaveLog($"V2pn: v2rayN's sign-in entry removed: {name}");
            }
        }

        var (code, output) = V2pnAutostart.Schtasks("/Query", "/FO", "CSV", "/NH");
        if (code != 0)
        {
            return;
        }
        var tasks = output.Split('\n')
            .Select(line => line.Trim().TrimStart('"'))
            .Select(line => line.Contains('"') ? line[..line.IndexOf('"')] : line)
            .Select(task => task.TrimStart('\\'))
            .Where(task => task.StartsWith(V2rayNAutoRun, StringComparison.OrdinalIgnoreCase))
            .Distinct();
        foreach (var task in tasks)
        {
            var (_, xml) = V2pnAutostart.Schtasks("/Query", "/TN", task, "/XML");
            var command = Regex.Match(xml, "<Command>(.*?)</Command>").Groups[1].Value.Trim('"');
            if (!Ours(command))
            {
                continue;
            }
            var (deleted, _) = V2pnAutostart.Schtasks("/Delete", "/TN", task, "/F");
            Logging.SaveLog(deleted == 0
                ? $"V2pn: v2rayN's sign-in task removed: {task}"
                : $"V2pn: v2rayN's sign-in task {task} could not be removed (it needs administrator rights)");
        }
    }

    /// <summary>
    /// v2rayN's login item on a Mac: ~/Library/LaunchAgents/v2rayN-LaunchAgent.plist (AutoStartupHandler
    /// of v2rayN). It has no KeepAlive, so with the file gone v2rayN does not start at the next login.
    /// </summary>
    [SupportedOSPlatform("macos")]
    private static void RemoveLaunchAgent()
    {
        const string label = "v2rayN-LaunchAgent";
        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", $"{label}.plist");
        if (!File.Exists(file))
        {
            return;
        }
        try
        {
            using (var launchctl = Process.Start(new ProcessStartInfo("launchctl", ["remove", label]) { UseShellExecute = false, CreateNoWindow = true }))
            {
                launchctl?.WaitForExit(5000);
            }
            File.Delete(file);
            Logging.SaveLog($"V2pn: v2rayN's login item removed: {file}");
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"V2pn: v2rayN's login item {file} could not be removed: {ex.Message}");
        }
    }

    /// <summary>
    /// Where a process runs from. On Windows this also answers for a process running as administrator,
    /// which Process.MainModule refuses to describe to a normal one; null when even that is refused.
    /// </summary>
    private static string? ImagePath(Process process)
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, [Out] char[] name, ref uint size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
