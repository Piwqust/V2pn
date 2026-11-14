using System.Xml.Linq;

namespace ServiceLib;

/// <summary>
/// V2pn on Windows: who started this process, which other programs share its job, and what the event log
/// holds about a run that ended without a word. A V2pn started from a terminal or another app's session
/// lives in that app's job, and Windows closes it together with that app, for example when the app updates.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class V2pnWindowsTrail
{
    private const uint KillOnJobClose = 0x2000;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int MaxEventsPerLog = 40;

    // Where a chain of parents stops meaning anything: what users and Windows start programs from.
    private static readonly string[] Roots = ["explorer.exe", "svchost.exe", "services.exe", "wininit.exe", "winlogon.exe", "userinit.exe"];

    private sealed record Proc(int Pid, int ParentPid, string Name);

    private sealed record FoundEvent(DateTimeOffset Time, string Line, string? Package);

    #region Start

    public static string Lineage()
    {
        try
        {
            var all = Snapshot();
            var chain = new List<string>();
            var pid = Environment.ProcessId;
            var childStart = StartTime(pid);
            for (var depth = 0; depth < 6 && all.TryGetValue(pid, out var child); depth++)
            {
                var parentStart = all.ContainsKey(child.ParentPid) ? StartTime(child.ParentPid) : null;
                // A parent's pid can be taken by a program started later; that program is not the parent.
                if (!all.TryGetValue(child.ParentPid, out var parent) || parentStart > childStart)
                {
                    chain.Add($"a process that has exited (pid {child.ParentPid})");
                    break;
                }
                var package = PackageOf(parent.Pid);
                chain.Add(package is null ? $"{parent.Name} (pid {parent.Pid})" : $"{parent.Name} (pid {parent.Pid}, app package {package})");
                if (Roots.Contains(parent.Name, StringComparer.OrdinalIgnoreCase))
                {
                    break;
                }
                pid = parent.Pid;
                childStart = parentStart;
            }
            return chain.Count == 0 ? "started by an unknown process" : "started by " + string.Join(" <- ", chain);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnWindowsTrail), ex);
            return "started by an unknown process";
        }
    }

    /// <summary>A warning when this process shares its job with programs that are not its own.</summary>
    public static string? SharedJobWarning()
    {
        try
        {
            if (!IsProcessInJob(CurrentProcess, 0, out var inJob) || !inJob)
            {
                return null;
            }
            var all = Snapshot();
            var own = OwnTree(all);
            var others = JobMembers()
                .Where(pid => !own.Contains(pid))
                .Select(pid => all.TryGetValue(pid, out var p) ? p.Name : null)
                .OfType<string>()
                .ToList();
            if (others.Count == 0)
            {
                return null;
            }
            var names = string.Join(", ", others
                .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Take(8)
                .Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key));
            var together = (JobLimitFlags() & KillOnJobClose) != 0 ? " That job ends all its processes together." : string.Empty;
            return $"V2pn: runs in one Windows job with {others.Count} other processes ({names}).{together} "
                + "When the program that owns the job closes or is updated, Windows can close V2pn with it, and nothing reaches this log. "
                + "Start V2pn from its shortcut, the Start menu or at sign-in, not from a terminal, a script or another app.";
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnWindowsTrail), ex);
            return null;
        }
    }

    private static HashSet<int> OwnTree(Dictionary<int, Proc> all)
    {
        var own = new HashSet<int> { Environment.ProcessId };
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var p in all.Values)
            {
                if (own.Contains(p.ParentPid) && own.Add(p.Pid))
                {
                    grew = true;
                }
            }
        }
        return own;
    }

    #endregion Start

    #region Event log

    /// <summary>Writes to the log what Windows recorded between the two moments: crashes, hangs, shutdowns, app updates.</summary>
    public static void LogEvents(DateTimeOffset from, DateTimeOffset to)
    {
        try
        {
            var time = $"TimeCreated[@SystemTime>='{Utc(from)}' and @SystemTime<='{Utc(to)}']";
            var found = new List<FoundEvent>();
            found.AddRange(Query("Application", $"*[System[Provider[@Name='Application Error' or @Name='.NET Runtime' or @Name='Application Hang'] and {time}]]"));
            // 41 lost power, 6008 unexpected shutdown, 1074 restart asked for, 2004 low memory, 26 error popups.
            found.AddRange(Query("System", $"*[System[(EventID=41 or EventID=6008 or EventID=1074 or EventID=2004 or EventID=26) and {time}]]"));
            found.AddRange(Query("Microsoft-Windows-AppXDeploymentServer/Operational", $"*[System[EventID=10000 and {time}]]"));

            var span = $"{from.ToLocalTime():HH:mm} and {to.ToLocalTime():HH:mm}";
            if (found.Count == 0)
            {
                Logging.SaveLog($"V2pn: Windows recorded no crash, hang, shutdown or app update between {span}. "
                    + "The process was most likely ended from outside: Task Manager, a script, or the program that started it.");
                return;
            }
            var lines = found.OrderBy(e => e.Time).Take(MaxEventsPerLog).Select(e => "    " + e.Line);
            Logging.SaveLog($"V2pn: what Windows recorded between {span}:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}");

            var updated = found.Select(e => e.Package).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (updated.Count > 0)
            {
                Logging.SaveLog($"V2pn: Windows was updating the app {string.Join(", ", updated)} at that time. "
                    + "An update closes the app and the programs started from it.");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnWindowsTrail), ex);
        }
    }

    private static List<FoundEvent> Query(string log, string xpath)
    {
        var result = new List<FoundEvent>();
        var xml = RunWevtutil(log, xpath);
        if (xml.IsNullOrEmpty())
        {
            return result;
        }
        XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
        var root = XDocument.Parse("<Events>" + xml + "</Events>").Root;
        foreach (var ev in root?.Elements(ns + "Event") ?? [])
        {
            var system = ev.Element(ns + "System");
            var stamp = system?.Element(ns + "TimeCreated")?.Attribute("SystemTime")?.Value;
            if (!DateTimeOffset.TryParse(stamp, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var when))
            {
                continue;
            }
            var provider = system?.Element(ns + "Provider")?.Attribute("Name")?.Value ?? "?";
            var id = system?.Element(ns + "EventID")?.Value ?? "?";
            var data = ev.Element(ns + "EventData")?.Elements(ns + "Data").ToList() ?? [];
            var message = ev.Element(ns + "RenderingInfo")?.Element(ns + "Message")?.Value;
            if (message.IsNullOrEmpty())
            {
                message = string.Join("; ", data.Select(d => d.Value).Where(v => v.IsNotEmpty()));
            }
            message = Regex.Replace(message ?? string.Empty, @"\s+", " ").Trim();
            // A .NET crash keeps its stack in the message; the rest say what they need early on.
            var limit = provider == ".NET Runtime" ? 2000 : 500;
            if (message.Length > limit)
            {
                message = message[..limit] + "...";
            }
            string? package = null;
            if (provider.Contains("AppXDeployment", StringComparison.OrdinalIgnoreCase))
            {
                var full = data.FirstOrDefault(d => d.Attribute("Name")?.Value == "PackageFullName")?.Value;
                package = full.IsNullOrEmpty() ? null : full.Split('_')[0];
            }
            result.Add(new FoundEvent(when, $"{when.ToLocalTime():HH:mm:ss} {provider} #{id}: {message}", package));
        }
        return result;
    }

    private static string RunWevtutil(string log, string xpath)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    // By full path, as with schtasks: a bare name could run a file planted beside V2pn.exe.
                    FileName = Path.Combine(Environment.SystemDirectory, "wevtutil.exe"),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                },
            };
            foreach (var arg in new[] { "qe", log, "/q:" + xpath, "/f:RenderedXml", $"/c:{MaxEventsPerLog}" })
            {
                process.StartInfo.ArgumentList.Add(arg);
            }
            process.Start();
            // Both pipes are read before waiting, or a full one would block wevtutil and the wait with it.
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000))
            {
                process.Kill();
                return string.Empty;
            }
            return output.Result;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnWindowsTrail), ex);
            return string.Empty;
        }
    }

    private static string Utc(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);

    #endregion Event log

    #region Processes

    private static nint CurrentProcess => -1;

    private static Dictionary<int, Proc> Snapshot()
    {
        var result = new Dictionary<int, Proc>();
        var snapshot = CreateToolhelp32Snapshot(0x2, 0);
        if (snapshot == -1)
        {
            return result;
        }
        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            for (var ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
            {
                result[(int)entry.th32ProcessID] = new Proc((int)entry.th32ProcessID, (int)entry.th32ParentProcessID, entry.szExeFile);
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return result;
    }

    private static DateTime? StartTime(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.StartTime;
        }
        catch
        {
            return null;
        }
    }

    private static string? PackageOf(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == 0)
        {
            return null;
        }
        try
        {
            uint length = 256;
            var name = new StringBuilder((int)length);
            return GetPackageFullName(handle, ref length, name) == 0 ? name.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static List<int> JobMembers()
    {
        var result = new List<int>();
        const int capacity = 4096;
        var size = 8 + (capacity * IntPtr.Size);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            // ERROR_MORE_DATA still fills the buffer with as many ids as fit.
            if (!QueryInformationJobObject(0, 3, buffer, (uint)size, out _) && Marshal.GetLastWin32Error() != 234)
            {
                return result;
            }
            var count = Math.Min(Marshal.ReadInt32(buffer, 4), capacity);
            for (var i = 0; i < count; i++)
            {
                result.Add((int)Marshal.ReadIntPtr(buffer, 8 + (i * IntPtr.Size)));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return result;
    }

    private static uint JobLimitFlags()
    {
        return QueryInformationJobObject(0, 9, out JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>(), out _)
            ? info.BasicLimitInformation.LimitFlags
            : 0;
    }

    #endregion Processes

    #region Interop

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(nint snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(nint snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFullName(nint process, ref uint length, StringBuilder name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(nint process, nint job, [MarshalAs(UnmanagedType.Bool)] out bool result);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(nint job, int infoClass, nint info, uint length, out uint returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(nint job, int infoClass, out JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint length, out uint returned);

    #endregion Interop
}
