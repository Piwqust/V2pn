namespace ServiceLib;

/// <summary>
/// V2pn: explains an exit that left nothing in the log. A process that is killed (Task Manager, Windows
/// closing the program that started it, an app update that takes that program's children down) runs no
/// code on the way out. So V2pn notes every minute that it is alive, and on the next start says when the
/// previous run went quiet, who had started it, and what Windows recorded around that moment.
/// </summary>
public static class V2pnRunTrail
{
    private static readonly TimeSpan Beat = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan HealthEvery = TimeSpan.FromMinutes(30);
    private static readonly object Gate = new();
    private static bool _begun;
    private static DateTimeOffset _started;
    private static string _startedBy = string.Empty;

    private enum RunState
    {
        Running,
        Exiting,
        Closed,
        Crashed,
    }

    private sealed record RunRecord(int Pid, DateTimeOffset Started, DateTimeOffset Alive, RunState State, string StartedBy, string Detail);

    private static string RecordPath => Utils.GetLogPath("run.txt");

    /// <summary>Once per app start, after logging is set up and before the core starts.</summary>
    public static void Begin()
    {
        lock (Gate)
        {
            if (_begun)
            {
                return;
            }
            _begun = true;
            using var me = Process.GetCurrentProcess();
            _started = new DateTimeOffset(me.StartTime);
        }
        try
        {
            var previous = Read();
            LogStartContext();
            Write(RunState.Running, null);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => MarkClosed();
            if (previous is not null && previous.Pid != Environment.ProcessId)
            {
                ReportPrevious(previous);
            }
            Task.Run(BeatLoop);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnRunTrail), ex);
        }
    }

    public static void MarkExiting() => TryWrite(RunState.Exiting, null);

    public static void MarkClosed() => TryWrite(RunState.Closed, null);

    /// <summary>
    /// The process is going down on an unhandled exception. All of it goes to the log at once, type and
    /// inner exceptions included, because nothing after this line may run.
    /// </summary>
    public static void NoteCrash(string source, Exception ex)
    {
        try
        {
            Logging.SaveLog($"V2pn: unhandled exception in {source}, the app is closing: {ex}");
            TryWrite(RunState.Crashed, $"{ex.GetType().Name}: {ex.Message}");
            NLog.LogManager.Flush(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
    }

    private static void LogStartContext()
    {
        var parts = new List<string>();
        if (Utils.IsWindows())
        {
            _startedBy = V2pnWindowsTrail.Lineage();
            parts.Add(_startedBy);
            parts.Add(Utils.IsAdministrator() ? "as administrator" : "not as administrator");
        }
        var args = Environment.GetCommandLineArgs().Skip(1).ToList();
        parts.Add(args.Count == 0 ? "no arguments" : "arguments: " + string.Join(' ', args));
        Logging.SaveLog("V2pn: " + string.Join("; ", parts));

        if (Utils.IsWindows())
        {
            var warning = V2pnWindowsTrail.SharedJobWarning();
            if (warning is not null)
            {
                Logging.SaveLog(warning);
            }
        }
    }

    private static void ReportPrevious(RunRecord previous)
    {
        var what = $"the previous run (pid {previous.Pid}, started {Time(previous.Started)})";
        switch (previous.State)
        {
            case RunState.Closed:
                return;

            case RunState.Crashed:
                Logging.SaveLog($"V2pn: {what} stopped on an unhandled exception at {Time(previous.Alive)}: {previous.Detail}. The log of that day has the details.");
                return;

            case RunState.Running when StillRunning(previous):
                // A handover: restarting as administrator starts the new copy before the old one closes.
                return;
        }

        Logging.SaveLog(previous.State == RunState.Exiting
            ? $"V2pn: {what} began closing at {Time(previous.Alive)} and did not finish."
            : $"V2pn: {what} ended without closing: no exit and no error reached the log. Its last sign of life was at {Time(previous.Alive)}.");
        if (previous.StartedBy.IsNotEmpty())
        {
            Logging.SaveLog($"V2pn: that run was {previous.StartedBy}.");
        }

        if (Utils.IsWindows())
        {
            // GetTickCount64 keeps counting through sleep, so this is the real boot time.
            var boot = DateTimeOffset.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
            if (boot > previous.Alive)
            {
                Logging.SaveLog($"V2pn: Windows started at {Time(boot)}, after that moment: the computer was restarted or shut down.");
                return;
            }
            var from = previous.Alive - TimeSpan.FromMinutes(2);
            var to = previous.Alive + TimeSpan.FromMinutes(5);
            if (to > DateTimeOffset.Now)
            {
                to = DateTimeOffset.Now;
            }
            Task.Run(() =>
            {
                if (OperatingSystem.IsWindows())
                {
                    V2pnWindowsTrail.LogEvents(from, to);
                }
            });
        }
        else if (Utils.IsMacOS())
        {
            Task.Run(() => LogMacCrashReports(previous.Alive - TimeSpan.FromMinutes(2)));
        }
    }

    private static bool StillRunning(RunRecord previous)
    {
        try
        {
            using var other = Process.GetProcessById(previous.Pid);
            using var me = Process.GetCurrentProcess();
            return other.ProcessName == me.ProcessName
                && Math.Abs((new DateTimeOffset(other.StartTime) - previous.Started).TotalSeconds) < 2;
        }
        catch
        {
            return false;
        }
    }

    private static async Task BeatLoop()
    {
        using var timer = new PeriodicTimer(Beat);
        var lastBeat = DateTimeOffset.Now;
        var nextHealth = lastBeat + HealthEvery;
        while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
        {
            try
            {
                var now = DateTimeOffset.Now;
                if (now - lastBeat > Beat * 3)
                {
                    Logging.SaveLog($"V2pn: {Span(now - lastBeat)} passed since the previous heartbeat; the computer was probably asleep.");
                }
                lastBeat = now;
                if (!Write(RunState.Running, null))
                {
                    return;
                }
                if (now >= nextHealth)
                {
                    nextHealth = now + HealthEvery;
                    LogHealth(now);
                }
            }
            catch (Exception ex)
            {
                Logging.SaveLog(nameof(V2pnRunTrail), ex);
            }
        }
    }

    // A slow leak shows up here as numbers that only grow, long before the process runs out of memory.
    private static void LogHealth(DateTimeOffset now)
    {
        using var me = Process.GetCurrentProcess();
        Logging.SaveLog($"V2pn: running for {Span(now - _started)}; memory {Mb(me.WorkingSet64)} MB in use, "
            + $"{Mb(me.PrivateMemorySize64)} MB private, {Mb(GC.GetTotalMemory(false))} MB managed; "
            + $"{me.HandleCount} handles, {me.Threads.Count} threads");
    }

    private static void LogMacCrashReports(DateTimeOffset since)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "DiagnosticReports");
            if (!Directory.Exists(folder))
            {
                return;
            }
            var names = new[] { "V2pn", "xray", "sing-box", "mihomo" };
            var reports = new DirectoryInfo(folder).GetFiles()
                .Where(f => f.LastWriteTime >= since.LocalDateTime && names.Any(n => f.Name.StartsWith(n, StringComparison.OrdinalIgnoreCase)))
                .Select(f => f.FullName)
                .ToList();
            Logging.SaveLog(reports.Count == 0
                ? "V2pn: macOS wrote no crash report for V2pn or its core at that time."
                : "V2pn: macOS crash reports from that time: " + string.Join(", ", reports));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnRunTrail), ex);
        }
    }

    #region Record

    private static void TryWrite(RunState state, string? detail)
    {
        if (!_begun)
        {
            return;
        }
        try
        {
            Write(state, detail);
        }
        catch
        {
        }
    }

    /// <returns>false when a newer V2pn owns the record, after a handover; this one then stops writing.</returns>
    private static bool Write(RunState state, string? detail)
    {
        lock (Gate)
        {
            var onDisk = Read();
            if (onDisk is not null && onDisk.Pid != Environment.ProcessId && onDisk.Started > _started)
            {
                return false;
            }
            var path = RecordPath;
            var text = string.Join('\n',
                $"pid={Environment.ProcessId}",
                $"started={_started:o}",
                $"alive={DateTimeOffset.Now:o}",
                $"state={state}",
                $"startedBy={OneLine(_startedBy)}",
                $"detail={OneLine(detail)}");
            // Written beside and moved over, so a kill in the middle never leaves half a record.
            var temp = path + ".tmp";
            File.WriteAllText(temp, text);
            File.Move(temp, path, true);
            // The hourly log cleanup deletes files by creation time, which a replace keeps.
            File.SetCreationTime(path, DateTime.Now);
            return true;
        }
    }

    private static RunRecord? Read()
    {
        try
        {
            var path = RecordPath;
            if (!File.Exists(path))
            {
                return null;
            }
            var values = File.ReadAllLines(path)
                .Select(line => line.Split('=', 2))
                .Where(pair => pair.Length == 2)
                .GroupBy(pair => pair[0])
                .ToDictionary(g => g.Key, g => g.Last()[1]);
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            var roundtrip = System.Globalization.DateTimeStyles.RoundtripKind;
            if (!int.TryParse(values.GetValueOrDefault("pid"), out var pid)
                || !DateTimeOffset.TryParse(values.GetValueOrDefault("started"), invariant, roundtrip, out var started)
                || !DateTimeOffset.TryParse(values.GetValueOrDefault("alive"), invariant, roundtrip, out var alive)
                || !Enum.TryParse<RunState>(values.GetValueOrDefault("state"), out var state))
            {
                return null;
            }
            return new RunRecord(pid, started, alive, state, values.GetValueOrDefault("startedBy") ?? string.Empty, values.GetValueOrDefault("detail") ?? string.Empty);
        }
        catch
        {
            return null;
        }
    }

    #endregion Record

    #region Text

    private static string OneLine(string? text) => (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');

    private static string Time(DateTimeOffset time) => time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    private static long Mb(long bytes) => bytes / (1024 * 1024);

    private static string Span(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays} d {span.Hours} h";
        }
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes:00} min" : $"{(int)span.TotalMinutes} min";
    }

    #endregion Text
}
