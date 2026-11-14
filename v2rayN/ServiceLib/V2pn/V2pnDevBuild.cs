namespace ServiceLib;

public enum EV2pnDevBuildState
{
    Idle,
    Building,
    Installing,
    Failed,
}

/// <summary>
/// V2pn's developer mode (Windows): builds V2pn from a source folder on this computer with
/// tools/release.ps1 and installs the result over itself, the way a downloaded update is
/// installed: AmazTool swaps the files and puts the previous version back if the new one does not
/// start. The build gets a version above the published one ("1.0.1-local.202611141320"), so
/// the regular updater leaves it alone until a newer release comes out, and
/// <see cref="V2pnAppUpdater.ReturnToReleaseAsync"/> goes back to the published release on request.
/// </summary>
public sealed partial class V2pnDevBuild : ReactiveObject
{
    private const string Tag = nameof(V2pnDevBuild);
    private const string FailedMarker = V2pnAppUpdater.FailedMarker;

    public static V2pnDevBuild Instance { get; } = new();

    private readonly object _lock = new();
    private Process? _process;
    private bool _cancelled;

    [Reactive] public partial EV2pnDevBuildState State { get; set; }

    /// <summary>The build step under way, as the build script names it ("win-x64: V2pn 1.0.1-local…").</summary>
    [Reactive] public partial string? Step { get; set; }

    /// <summary>Why the last build did not finish: the end of the build log.</summary>
    [Reactive] public partial string? Error { get; set; }

    /// <summary>Only an installed release replaces itself; a build folder never does.</summary>
    public static bool Supported => Utils.IsWindows() && V2pnAppUpdater.Instance.Supported && !V2pnAppUpdater.Instance.ByWinGet;

    public static string WorkDir => Utils.GetTempPath("V2pnDevBuild");

    public static string LogPath => Path.Combine(WorkDir, "build.log");

    /// <summary>What AmazTool wrote while installing the last build.</summary>
    public static string InstallLogPath => Path.Combine(WorkDir, "update.log");

    /// <summary>A folder holding the V2pn sources: the build script and the app project.</summary>
    public static bool IsSourceFolder(string? path) =>
        !path.IsNullOrEmpty()
        && File.Exists(Path.Combine(path, "tools", "release.ps1"))
        && File.Exists(Path.Combine(path, "v2rayN", "v2rayN.Desktop", "v2rayN.Desktop.csproj"));

    /// <summary>
    /// The version for a new local build: the next patch after both this V2pn and the newest
    /// published release, so that only a later release replaces it; a local build is followed by
    /// a local build of the same number. The suffix is the time, so each build is newer than the last.
    /// </summary>
    public static string NextVersion(string current, string? latestRelease, DateTime now)
    {
        var (major, minor, patch) = Core(current);
        if (!V2pnInfo.IsLocalVersion(current))
        {
            patch++;
        }
        if (Core(latestRelease) is var latest && latest.CompareTo((major, minor, patch)) >= 0)
        {
            (major, minor, patch) = (latest.Major, latest.Minor, latest.Patch + 1);
        }
        return $"{major}.{minor}.{patch}-{V2pnInfo.LocalTag}.{now:yyyyMMddHHmm}";
    }

    private static (int Major, int Minor, int Patch) Core(string? version)
    {
        var match = Regex.Match(version ?? string.Empty, @"^v?(\d+)\.(\d+)\.(\d+)");
        return match.Success
            ? (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value))
            : (0, 0, 0);
    }

    /// <summary>The version of the last build that was installed and did not start, or null.</summary>
    public static string? FailedVersion()
    {
        try
        {
            var marker = Path.Combine(WorkDir, FailedMarker);
            return File.Exists(marker) ? File.ReadAllText(marker).Trim() : null;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
            return null;
        }
    }

    /// <summary>
    /// Builds V2pn from <paramref name="source"/> and installs it: V2pn closes and AmazTool starts the
    /// new build. The connection stays up while the build runs. Returns false, with the reason in
    /// <see cref="Error"/>, when the build failed or was cancelled; V2pn then keeps running as it was.
    /// </summary>
    public async Task<bool> BuildAndInstallAsync(string source)
    {
        lock (_lock)
        {
            if (State is EV2pnDevBuildState.Building or EV2pnDevBuildState.Installing)
            {
                return false;
            }
            State = EV2pnDevBuildState.Building;
            _cancelled = false;
        }
        Error = null;
        Step = null;
        try
        {
            if (!Supported)
            {
                return Fail("The developer mode works in an installed V2pn on Windows.");
            }
            if (!IsSourceFolder(source))
            {
                return Fail($"{source} does not hold the V2pn sources (tools/release.ps1 is missing).");
            }

            var version = NextVersion(V2pnAppUpdater.Instance.CurrentVersion, V2pnAppUpdater.Instance.LatestVersion, DateTime.Now);
            if (Directory.Exists(WorkDir))
            {
                Directory.Delete(WorkDir, true);
            }
            var outDir = Path.Combine(WorkDir, "out");
            Directory.CreateDirectory(outDir);
            Logging.SaveLog($"{Tag}: building V2pn {version} from {source}");

            var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            var exitCode = await RunScriptAsync(source, version, arch, outDir);
            if (_cancelled)
            {
                Logging.SaveLog($"{Tag}: build cancelled");
                State = EV2pnDevBuildState.Idle;
                return false;
            }
            var built = Path.Combine(outDir, $"V2pn-windows-{arch}-update.zip");
            if (exitCode != 0 || !File.Exists(built))
            {
                return Fail(LogTail() ?? $"The build stopped with code {exitCode}.");
            }

            // AmazTool reads the version from the file name, and writes update.log and failed.txt next to it.
            var package = Path.Combine(WorkDir, $"V2pn-{version}.zip");
            File.Move(built, package);
            Directory.Delete(outDir, true);

            State = EV2pnDevBuildState.Installing;
            Step = null;
            return await V2pnAppUpdater.StartInstallerAsync(package, version, (error, _) => Fail(error));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
            return Fail(ex.Message);
        }
    }

    /// <summary>Stops a build under way; nothing is installed.</summary>
    public void Cancel()
    {
        lock (_lock)
        {
            if (State != EV2pnDevBuildState.Building)
            {
                return;
            }
            _cancelled = true;
            try
            {
                _process?.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Logging.SaveLog(Tag, ex);
            }
        }
    }

    private async Task<int> RunScriptAsync(string source, string version, string arch, string outDir)
    {
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            WorkingDirectory = source,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                     "-File", Path.Combine(source, "tools", "release.ps1"),
                     "-Version", version, "-Arch", arch, "-NoCores", "-UpdateOnly", "-NoManifest", "-OutDir", outDir,
                 })
        {
            start.ArgumentList.Add(argument);
        }
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";

        await using var log = new StreamWriter(LogPath, append: false, Encoding.UTF8) { AutoFlush = true };
        var logGate = new object();
        void Write(string? line)
        {
            if (line == null)
            {
                return;
            }
            lock (logGate)
            {
                log.WriteLine(line);
            }
            if (line.StartsWith("== ", StringComparison.Ordinal))
            {
                Step = line[3..].Trim();
            }
        }

        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => Write(e.Data);
        process.ErrorDataReceived += (_, e) => Write(e.Data);
        lock (_lock)
        {
            process.Start();
            _process = process;
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync();
            // Lets the last redirected lines reach the log.
            process.WaitForExit();
            return process.ExitCode;
        }
        finally
        {
            lock (_lock)
            {
                _process = null;
            }
        }
    }

    /// <summary>The last lines of the build log that say something, for the window.</summary>
    private static string? LogTail()
    {
        try
        {
            var lines = File.ReadAllLines(LogPath)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("At ", StringComparison.Ordinal) && !l.StartsWith("+ ", StringComparison.Ordinal))
                .ToList();
            var errors = lines.Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)).Distinct().TakeLast(3).ToList();
            var tail = errors.Count > 0 ? errors : lines.TakeLast(3).ToList();
            return tail.Count == 0 ? null : string.Join(Environment.NewLine, tail);
        }
        catch
        {
            return null;
        }
    }

    private bool Fail(string error)
    {
        Error = error;
        Step = null;
        State = EV2pnDevBuildState.Failed;
        Logging.SaveLog($"{Tag}: {error}");
        return false;
    }
}
