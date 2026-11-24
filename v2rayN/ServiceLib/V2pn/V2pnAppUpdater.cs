namespace ServiceLib;

public enum EV2pnUpdateState
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Ready,
    Failed,
}

/// <summary>What the updater trusts and where it looks. Comes from the build; tests swap it.</summary>
/// <param name="ByWinGet">winget installed this copy and replaces it; V2pn only reports new versions.</param>
/// <param name="BetaSources">Sources that also serve the beta channel, asked only when the person takes beta versions.</param>
public sealed record V2pnUpdateSettings(IReadOnlyList<string> Sources, IReadOnlyList<string> Keys, bool Supported, string CurrentVersion, bool ByWinGet = false,
    IReadOnlyList<string>? BetaSources = null)
{
    public static V2pnUpdateSettings FromBuild() =>
        new(V2pnInfo.UpdateSources, V2pnInfo.UpdateKeys, V2pnInfo.SelfUpdateSupported, V2pnInfo.Version, V2pnWinGet.IsInstall(), V2pnInfo.BetaSources);

    /// <summary>
    /// The manifests the updater asks, in order, each with the source its packages come from: with <paramref name="beta"/>
    /// the beta channel of every source that has one, then the latest release of every source. The highest signed
    /// version among them wins, so a beta replaces a release only when it is newer, and a release replaces a beta.
    /// </summary>
    public IReadOnlyList<(string Source, string Manifest)> Manifests(bool beta) =>
        (beta ? (BetaSources ?? []).Select(s => (s, V2pnInfo.BetaManifestUrl(s))) : [])
            .Concat(Sources.Select(s => (s, V2pnInfo.ManifestUrl(s))))
            .ToList();
}

/// <summary>
/// Keeps V2pn itself current. It reads the newest release's signed manifest from the first source
/// that answers (GitHub, then any mirror), downloads the package for this machine in the
/// background, refuses it unless it matches the manifest, and keeps it in guiTemps/V2pnUpdate
/// until it is installed: by the person (restart now) or at the next start. AmazTool does the
/// install, so the app is never replaced while it runs.
/// </summary>
public sealed partial class V2pnAppUpdater : ReactiveObject
{
    private const string Tag = nameof(V2pnAppUpdater);
    private const string PendingPrefix = "V2pn-";

    /// <summary>AmazTool writes the version here when it had to put the previous one back.</summary>
    public const string FailedMarker = "failed.txt";

    public static V2pnAppUpdater Instance { get; } = new();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _lastLoggedError;

    // Set while a person asks to leave a developer-mode build for the published release.
    private bool _returningToRelease;

    public V2pnUpdateSettings Settings { get; set; } = V2pnUpdateSettings.FromBuild();

    [Reactive] public partial EV2pnUpdateState State { get; set; }

    /// <summary>The newest version found, without the "v"; null when nothing is published yet.</summary>
    [Reactive] public partial string? LatestVersion { get; set; }

    /// <summary>Download progress as text, e.g. "12.4 MB / 61.6 MB".</summary>
    [Reactive] public partial string? Progress { get; set; }

    /// <summary>Why the last check, download or install failed, for the log and a tooltip.</summary>
    [Reactive] public partial string? Error { get; set; }

    /// <summary>The newest version was found but could not be downloaded.</summary>
    [Reactive] public partial bool DownloadFailed { get; set; }

    /// <summary>The newest version was installed, did not start, and the previous one was put back.</summary>
    [Reactive] public partial bool InstallFailed { get; set; }

    public string CurrentVersion => Settings.CurrentVersion;

    public bool Supported => Settings.Supported;

    /// <summary>winget replaces this copy: nothing is downloaded or installed here, the window offers winget instead.</summary>
    public bool ByWinGet => Settings.ByWinGet;

    /// <summary>
    /// The person takes beta versions (Updates → "Get beta versions"). Off: a beta is neither looked for nor installed,
    /// not even one downloaded while it was on; the beta already running stays until a newer release replaces it.
    /// </summary>
    public static bool Beta => ConfigTextHandler.Config?.UiItem?.V2pnBeta == true;

    private static string PendingDir => Utils.GetTempPath("V2pnUpdate");

    private sealed record Release(string Source, bool ViaProxy, V2pnManifest Manifest, byte[] Json, string Signature);

    /// <summary>
    /// Looks for a newer V2pn. With <paramref name="download"/> a newer package is fetched and
    /// verified right away, so installing it later takes seconds. <paramref name="retryFailed"/>
    /// is for a person asking again after an install that was rolled back. Concurrent calls are dropped,
    /// unless <paramref name="wait"/> asks to queue behind the one under way.
    /// </summary>
    public async Task CheckAsync(bool download, bool retryFailed = false, CancellationToken cancellationToken = default, bool wait = false)
    {
        if (!Supported || !await _gate.WaitAsync(wait ? Timeout.Infinite : 0, cancellationToken))
        {
            return;
        }
        download &= !ByWinGet;
        try
        {
            Error = null;
            DownloadFailed = false;
            InstallFailed = false;
            State = EV2pnUpdateState.Checking;
            if (retryFailed)
            {
                DeleteFile(Path.Combine(PendingDir, FailedMarker));
            }

            var preferProxy = ConfigTextHandler.Config.CheckUpdateItem.UpdateViaProxy;
            var (release, nothingPublished, error) = await FindReleaseAsync(preferProxy, cancellationToken);
            if (release == null)
            {
                if (nothingPublished)
                {
                    LatestVersion = null;
                    State = EV2pnUpdateState.UpToDate;
                }
                else
                {
                    Fail(error ?? "V2pn releases did not answer.");
                }
                return;
            }

            var version = release.Manifest.Version;
            LatestVersion = version;
            var package = V2pnInfo.UpdatePackage(release.Manifest);
            if (package == null || !IsWanted(version))
            {
                DeleteStalePackages();
                State = EV2pnUpdateState.UpToDate;
                return;
            }
            if (FailedInstall() is { } failed && failed.Version == version)
            {
                InstallFailed = true;
                Fail($"V2pn {version} did not start after it was installed; the previous version was put back.");
                _ = V2pnUpdateReport.SendAsync(ConfigTextHandler.Config, CurrentVersion, version, failed.Stage, failed.Code);
                return;
            }
            if (FindPending()?.Version == version)
            {
                State = EV2pnUpdateState.Ready;
                return;
            }
            State = EV2pnUpdateState.Available;
            if (download)
            {
                await DownloadAsync(release, package, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            State = EV2pnUpdateState.Idle;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
            Fail(ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The package waiting to be installed, if it is newer than this V2pn. It counts only with a
    /// manifest signed by the release key next to it: a zip that some other program dropped into
    /// the folder is not an update.
    /// </summary>
    public (string Version, string File)? FindPending()
    {
        try
        {
            if (!Directory.Exists(PendingDir))
            {
                return null;
            }
            var failed = FailedVersion();
            return Directory.GetFiles(PendingDir, PendingPrefix + "*.zip")
                .Select(f => (Version: Path.GetFileNameWithoutExtension(f)[PendingPrefix.Length..], File: f))
                .Where(p => p.Version != failed && IsWanted(p.Version) && PendingPackage(p.File, p.Version) != null)
                .OrderByDescending(p => new SemanticVersion(p.Version))
                .Select(p => ((string, string)?)p)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
            return null;
        }
    }

    /// <summary>
    /// Hands the waiting package to AmazTool and closes V2pn; AmazTool restarts it as the new
    /// version. Returns false, leaving V2pn running, when there is nothing safe to install.
    /// </summary>
    public async Task<bool> InstallAsync()
    {
        var pending = ByWinGet ? null : FindPending();
        if (pending == null)
        {
            return false;
        }
        var (version, file) = pending.Value;
        var expected = PendingPackage(file, version);
        if (expected == null || !V2pnInfo.VerifyPackage(file, expected, out var error))
        {
            DeletePackage(file);
            Fail("The downloaded package is damaged.", download: true);
            _ = V2pnUpdateReport.SendAsync(ConfigTextHandler.Config, CurrentVersion, version, "verify", expected == null ? "no_manifest" : "damaged");
            return false;
        }
        return await StartInstallerAsync(file, version, Fail);
    }

    /// <summary>
    /// Leaves a build made by the developer mode for the newest published release, even though that
    /// release has a lower version number: downloads it, checks it against the signed manifest and
    /// installs it. Returns false, with the reason in <see cref="Error"/>, when that did not happen.
    /// </summary>
    public async Task<bool> ReturnToReleaseAsync()
    {
        if (!await DownloadReleaseAsync())
        {
            return false;
        }
        _returningToRelease = true;
        try
        {
            return await InstallAsync();
        }
        finally
        {
            _returningToRelease = false;
        }
    }

    /// <summary>
    /// The first half of <see cref="ReturnToReleaseAsync"/>: the published release, downloaded and
    /// verified. It waits only for that request; on its own it never replaces a developer-mode build.
    /// </summary>
    public async Task<bool> DownloadReleaseAsync()
    {
        if (!V2pnInfo.IsLocalVersion(CurrentVersion))
        {
            return false;
        }
        _returningToRelease = true;
        try
        {
            await CheckAsync(download: true, retryFailed: true, wait: true);
            return State == EV2pnUpdateState.Ready;
        }
        finally
        {
            _returningToRelease = false;
        }
    }

    /// <summary>
    /// Hands a package to AmazTool and closes V2pn; AmazTool restarts it as the new version, or puts
    /// the previous one back when the new one does not start (and writes failed.txt next to the
    /// package). The package must already be trusted: this checks nothing.
    /// </summary>
    public static async Task<bool> StartInstallerAsync(string file, string version, Action<string, bool> fail)
    {
        if (!Utils.UpgradeAppExists(out var upgrader))
        {
            fail(ResUI.UpgradeAppNotExistTip, true);
            return false;
        }

        Logging.SaveLog($"{Tag}: installing V2pn {version}");
        try
        {
            // No console window: AmazTool logs to update.log next to the package and always
            // brings V2pn back, as the new version or, if that one does not start, the old one.
            var start = new ProcessStartInfo
            {
                FileName = upgrader,
                WorkingDirectory = Utils.GetBaseDirectory(),
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("install");
            start.ArgumentList.Add(file);
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            using var process = Process.Start(start);
            if (process == null)
            {
                fail("AmazTool did not start.", true);
                return false;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
            fail("AmazTool did not start.", true);
            return false;
        }
        await AppManager.Instance.AppExitAsync(true);
        return true;
    }

    /// <summary>
    /// Asks every source for the newest release and takes the highest version among the manifests with a valid
    /// signature; for one version the source asked first wins. Every source is asked, not only the first that answers:
    /// an old release is still validly signed, so a source that keeps serving one (V2pn's server taken over, a stale
    /// mirror) could otherwise hold every copy on an old version, security fixes and all.
    /// A source that answers "not found" has published nothing yet, which is not an error.
    /// </summary>
    private async Task<(Release? Release, bool NothingPublished, string? Error)> FindReleaseAsync(bool preferProxy, CancellationToken cancellationToken)
    {
        Release? best = null;
        var nothingPublished = false;
        string? error = null;
        await foreach (var found in AskSourcesAsync(preferProxy, cancellationToken))
        {
            if (found.Release is { } release)
            {
                if (best == null || new SemanticVersion(release.Manifest.Version) > new SemanticVersion(best.Manifest.Version))
                {
                    best = release;
                }
            }
            nothingPublished |= found.NothingPublished;
            error = found.Error ?? error;
        }
        return best != null ? (best, false, null) : (null, nothingPublished, error);
    }

    /// <summary>Each source's answer: its verified release, "nothing published", or why it gave neither.</summary>
    private async IAsyncEnumerable<(Release? Release, bool NothingPublished, string? Error)> AskSourcesAsync(bool preferProxy,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var proxy = await V2pnHttp.LocalProxyAsync(cancellationToken);
        var manifests = Settings.Manifests(Beta);
        foreach (var ((source, url), i) in manifests.Select((m, i) => (m, i)))
        {
            var host = Uri.TryCreate(source, UriKind.Absolute, out var uri) ? uri.Host : source;
            // V2pn's server comes first and GitHub after it: a source with a fallback is not waited on for long.
            var limits = i < manifests.Count - 1 ? V2pnHttp.Quick : null;
            foreach (var viaProxy in Routes(preferProxy, proxy != null))
            {
                var route = viaProxy ? proxy : null;
                var json = await V2pnHttp.GetAsync(url, route, V2pnManifest.MaxBytes, cancellationToken, limits: limits);
                if (json.NotFound)
                {
                    yield return (null, true, null);
                    break;
                }
                if (!json.Ok)
                {
                    yield return (null, false, $"{host}: {json.Error}");
                    continue;
                }
                var signature = await V2pnHttp.GetAsync(url + V2pnInfo.SignatureSuffix, route, 4096, cancellationToken, limits: limits);
                var signatureText = signature.Ok ? Encoding.ASCII.GetString(signature.Body!) : null;
                var manifest = V2pnManifest.ReadVerified(json.Body, signatureText, Settings.Keys, out var why);
                // Not worth asking the same source another way: it answered, with a release or with something that is not one.
                yield return manifest == null
                    ? (null, false, $"{host}: {why}")
                    : (new Release(source, viaProxy, manifest, json.Body!, signatureText!), false, null);
                break;
            }
        }
    }

    /// <summary>Direct and through the local proxy, the configured preference first.</summary>
    private static IEnumerable<bool> Routes(bool preferProxy, bool proxyRuns)
    {
        if (!proxyRuns)
        {
            return [false];
        }
        return preferProxy ? [true, false] : [false, true];
    }

    private async Task DownloadAsync(Release release, V2pnManifestFile package, CancellationToken cancellationToken)
    {
        State = EV2pnUpdateState.Downloading;
        Progress = null;
        var version = release.Manifest.Version;
        Directory.CreateDirectory(PendingDir);
        var target = Path.Combine(PendingDir, $"{PendingPrefix}{version}.zip");
        var part = target + ".part";
        var proxyRuns = await V2pnHttp.LocalProxyAsync(cancellationToken) != null;

        // The source and route that just served the manifest go first.
        var attempts = Settings.Sources.Where(s => s != release.Source).Prepend(release.Source)
            .SelectMany(s => Routes(release.ViaProxy, proxyRuns).Select(viaProxy => (Source: s, ViaProxy: viaProxy)));
        var error = "The download did not finish.";
        var done = false;
        var mismatched = false;
        foreach (var (source, viaProxy) in attempts)
        {
            DeleteFile(part);
            string? downloadError = null;
            using var service = await DownloadService.CreateDownloadServiceWithCheck(viaProxy);
            service.UpdateCompleted += (_, args) =>
            {
                // The first message names the URL; the rest are "speed | done/total".
                if (!args.Success && args.Msg?.Contains('|') == true)
                {
                    Progress = args.Msg.Split('|').Last().Trim().Replace("/", " / ");
                }
            };
            service.Error += (_, args) => downloadError ??= args.GetException().Message;
            await service.DownloadFileAsync(new() { FileUrl = V2pnInfo.PackageUrl(source, version, package.Name), FilePath = part }, cancellationToken);

            if (!File.Exists(part))
            {
                error = downloadError ?? error;
                continue;
            }
            if (V2pnInfo.VerifyPackage(part, package, out var mismatch))
            {
                done = true;
                break;
            }
            error = mismatch;
            mismatched = true;
        }
        if (!done)
        {
            DeleteFile(part);
            Fail(error, download: true);
            _ = V2pnUpdateReport.SendAsync(ConfigTextHandler.Config, CurrentVersion, version,
                mismatched ? "verify" : "download", mismatched ? "hash_mismatch" : error);
            return;
        }

        File.Move(part, target, true);
        DeleteStalePackages();
        await File.WriteAllBytesAsync(ManifestPath(target), release.Json, cancellationToken);
        await File.WriteAllTextAsync(ManifestPath(target) + V2pnInfo.SignatureSuffix, release.Signature, cancellationToken);
        Progress = null;
        State = EV2pnUpdateState.Ready;
        Logging.SaveLog($"{Tag}: V2pn {version} downloaded and verified");
    }

    private static string ManifestPath(string package) => Path.ChangeExtension(package, ".json");

    /// <summary>
    /// Whether a version should replace this one: a newer one always, a beta only while the person takes betas; an older
    /// published release only when the person asked to leave a developer-mode build for it.
    /// </summary>
    private bool IsWanted(string version)
    {
        if (!SemanticVersion.TryParse(version, out var candidate) || candidate == null || (V2pnInfo.IsBetaVersion(version) && !Beta))
        {
            return false;
        }
        var current = new SemanticVersion(CurrentVersion);
        return candidate > current
            || (_returningToRelease && V2pnInfo.IsLocalVersion(CurrentVersion) && !V2pnInfo.IsLocalVersion(version) && candidate.CompareTo(current) != 0);
    }

    /// <summary>What the signed manifest stored next to a waiting package says about it, or null.</summary>
    private V2pnManifestFile? PendingPackage(string package, string version)
    {
        try
        {
            var path = ManifestPath(package);
            if (!File.Exists(path) || !File.Exists(path + V2pnInfo.SignatureSuffix) || new FileInfo(path).Length > V2pnManifest.MaxBytes)
            {
                return null;
            }
            var manifest = V2pnManifest.ReadVerified(File.ReadAllBytes(path), File.ReadAllText(path + V2pnInfo.SignatureSuffix), Settings.Keys, out _);
            return manifest?.Version == version ? V2pnInfo.UpdatePackage(manifest) : null;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
            return null;
        }
    }

    private static string? FailedVersion() => FailedInstall()?.Version;

    /// <summary>
    /// What AmazTool left in failed.txt after it put the previous version back: the version, and from this release on
    /// the step that failed and a short code (for the opt-in report).
    /// </summary>
    internal static (string Version, string Stage, string Code)? FailedInstall()
    {
        try
        {
            var marker = Path.Combine(PendingDir, FailedMarker);
            if (!File.Exists(marker))
            {
                return null;
            }
            var lines = File.ReadAllLines(marker).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            return lines.Count == 0 ? null : (lines[0], lines.ElementAtOrDefault(1) ?? "rollback", lines.ElementAtOrDefault(2) ?? "unknown");
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
            return null;
        }
    }

    /// <summary>
    /// Removes packages that are not newer than this V2pn (the one just installed), interrupted
    /// downloads and what AmazTool leaves behind.
    /// </summary>
    private void DeleteStalePackages()
    {
        try
        {
            if (!Directory.Exists(PendingDir))
            {
                return;
            }
            var current = new SemanticVersion(CurrentVersion);
            foreach (var file in Directory.GetFiles(PendingDir, PendingPrefix + "*.zip"))
            {
                var version = Path.GetFileNameWithoutExtension(file)[PendingPrefix.Length..];
                if (!IsWanted(version))
                {
                    DeletePackage(file);
                }
            }
            foreach (var file in Directory.GetFiles(PendingDir, "*.part"))
            {
                DeleteFile(file);
            }
            // On Windows AmazTool cannot delete its own previous file while it runs.
            var leftover = Path.Combine(Utils.GetBaseDirectory(), ".v2pn-update");
            if (Directory.Exists(leftover))
            {
                Directory.Delete(leftover, true);
            }
            if (FailedVersion() is { } failed && new SemanticVersion(failed) <= current)
            {
                DeleteFile(Path.Combine(PendingDir, FailedMarker));
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
        }
    }

    private static void DeletePackage(string file)
    {
        DeleteFile(file);
        DeleteFile(ManifestPath(file));
        DeleteFile(ManifestPath(file) + V2pnInfo.SignatureSuffix);
    }

    private static void DeleteFile(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
        }
    }

    private void Fail(string error, bool download = false)
    {
        Error = error;
        DownloadFailed = download;
        Progress = null;
        State = EV2pnUpdateState.Failed;
        // The check repeats for as long as the app runs; the same reason is logged once.
        if (error != _lastLoggedError)
        {
            _lastLoggedError = error;
            Logging.SaveLog($"{Tag}: {error}");
        }
    }
}
