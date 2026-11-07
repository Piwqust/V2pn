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

/// <summary>
/// Keeps V2pn itself current. It reads the newest tag from V2pn's releases, downloads the package
/// for this machine in the background, refuses it unless it matches the published SHA-256, and
/// keeps it in guiTemps/V2pnUpdate until it is installed: by the person (restart now) or at the
/// next start. AmazTool does the install, so the app is never replaced while it runs.
/// </summary>
public sealed partial class V2pnAppUpdater : ReactiveObject
{
    private const string Tag = nameof(V2pnAppUpdater);
    private const string PendingPrefix = "V2pn-";

    public static V2pnAppUpdater Instance { get; } = new();

    private readonly SemaphoreSlim _gate = new(1, 1);

    [Reactive] public partial EV2pnUpdateState State { get; set; }

    /// <summary>The newest version found, without the "v".</summary>
    [Reactive] public partial string? LatestVersion { get; set; }

    /// <summary>Download progress as text, e.g. "12.4 MB / 61.6 MB".</summary>
    [Reactive] public partial string? Progress { get; set; }

    /// <summary>Why the last check or download failed, for the log and a tooltip.</summary>
    [Reactive] public partial string? Error { get; set; }

    /// <summary>The newest version was found but could not be downloaded or installed.</summary>
    [Reactive] public partial bool DownloadFailed { get; set; }

    public string CurrentVersion => V2pnInfo.Version;

    public bool Supported => V2pnInfo.SelfUpdateSupported;

    private static string PendingDir => Utils.GetTempPath("V2pnUpdate");

    /// <summary>
    /// Looks for a newer V2pn. With <paramref name="download"/> a newer package is fetched and
    /// verified right away, so installing it later takes seconds. Concurrent calls are dropped.
    /// </summary>
    public async Task CheckAsync(bool download, CancellationToken cancellationToken = default)
    {
        if (!Supported || !await _gate.WaitAsync(0, cancellationToken))
        {
            return;
        }
        try
        {
            Error = null;
            DownloadFailed = false;
            State = EV2pnUpdateState.Checking;
            var blProxy = AppManager.Instance.Config.CheckUpdateItem.UpdateViaProxy;
            var service = new DownloadService();

            var tag = await GetLatestTagAsync(service, blProxy, cancellationToken);
            if (tag.IsNullOrEmpty())
            {
                Fail("V2pn releases did not answer.");
                return;
            }
            var latest = new SemanticVersion(tag);
            LatestVersion = latest.ToStandardVersionString();
            if (latest <= new SemanticVersion(CurrentVersion))
            {
                DeleteStalePackages();
                State = EV2pnUpdateState.UpToDate;
                return;
            }
            if (FindPending()?.Version == LatestVersion)
            {
                State = EV2pnUpdateState.Ready;
                return;
            }
            State = EV2pnUpdateState.Available;
            if (download)
            {
                await DownloadAsync(service, tag, blProxy, cancellationToken);
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
    /// The verified package waiting to be installed, if it is newer than this V2pn.
    /// </summary>
    public (string Version, string File)? FindPending()
    {
        try
        {
            if (!Directory.Exists(PendingDir))
            {
                return null;
            }
            var current = new SemanticVersion(CurrentVersion);
            return Directory.GetFiles(PendingDir, PendingPrefix + "*.zip")
                .Select(f => (Version: Path.GetFileNameWithoutExtension(f)[PendingPrefix.Length..], File: f))
                .Where(p => File.Exists(p.File + V2pnInfo.HashSuffix) && new SemanticVersion(p.Version) > current)
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
        var pending = FindPending();
        if (pending == null)
        {
            return false;
        }
        var (version, file) = pending.Value;
        var expected = V2pnInfo.ParseHash(await File.ReadAllTextAsync(file + V2pnInfo.HashSuffix));
        if (!V2pnInfo.VerifyPackage(file, expected, out var error))
        {
            DeletePackage(file);
            Fail(error, download: true);
            return false;
        }
        if (!Utils.UpgradeAppExists(out var upgrader))
        {
            Fail(ResUI.UpgradeAppNotExistTip, download: true);
            return false;
        }

        Logging.SaveLog($"{Tag}: installing V2pn {version}");
        // AmazTool takes the package path URL-encoded, as v2rayN passes it, so spaces survive.
        var id = ProcUtils.ProcessStart(upgrader, Utils.UrlEncode(file), Utils.StartupPath());
        if (id is not > 0)
        {
            Fail("AmazTool did not start.", download: true);
            return false;
        }
        await AppManager.Instance.AppExitAsync(true);
        return true;
    }

    private async Task<string?> GetLatestTagAsync(DownloadService service, bool blProxy, CancellationToken cancellationToken)
    {
        // GitHub answers /releases/latest with a redirect to /releases/tag/<tag>; no API rate limit.
        var location = await service.UrlRedirectAsync($"{V2pnInfo.ReleasesUrl}/latest", blProxy, cancellationToken);
        var tag = location?.Split("/tag/").LastOrDefault()?.Trim('/');
        return tag.IsNullOrEmpty() || tag == location ? null : Uri.UnescapeDataString(tag);
    }

    private async Task DownloadAsync(DownloadService service, string tag, bool blProxy, CancellationToken cancellationToken)
    {
        var packageName = V2pnInfo.PackageName()!;
        var url = V2pnInfo.PackageUrl(tag, packageName);
        var expected = await V2pnInfo.GetPublishedHashAsync(service, url, blProxy, cancellationToken);
        if (expected == null)
        {
            Fail("The release has no checksum.", download: true);
            return;
        }

        State = EV2pnUpdateState.Downloading;
        Progress = null;
        var version = new SemanticVersion(tag).ToStandardVersionString();
        var target = Path.Combine(PendingDir, $"{PendingPrefix}{version}.zip");
        var part = target + ".part";
        string? downloadError = null;
        service.UpdateCompleted += (_, args) =>
        {
            // The first message names the URL; the rest are "speed | done/total".
            if (!args.Success && args.Msg?.Contains('|') == true)
            {
                Progress = args.Msg.Split('|').Last().Trim().Replace("/", " / ");
            }
        };
        service.Error += (_, args) => downloadError ??= args.GetException().Message;

        DeletePackage(part);
        await service.DownloadFileAsync(new() { FileUrl = url, FilePath = part }, blProxy, cancellationToken);

        if (!File.Exists(part))
        {
            Fail(downloadError ?? "The download did not finish.", download: true);
            return;
        }
        if (!V2pnInfo.VerifyPackage(part, expected, out var error))
        {
            DeletePackage(part);
            Fail(error, download: true);
            return;
        }
        DeleteStalePackages();
        File.Move(part, target, true);
        await File.WriteAllTextAsync(target + V2pnInfo.HashSuffix, $"{expected}  {packageName}\n", cancellationToken);
        Progress = null;
        State = EV2pnUpdateState.Ready;
        Logging.SaveLog($"{Tag}: V2pn {version} downloaded and verified");
    }

    /// <summary>Removes packages that are not newer than this V2pn, e.g. the one just installed.</summary>
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
                if (new SemanticVersion(version) <= current)
                {
                    DeletePackage(file);
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
        }
    }

    private static void DeletePackage(string file)
    {
        try
        {
            File.Delete(file);
            File.Delete(file + V2pnInfo.HashSuffix);
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
        Logging.SaveLog($"{Tag}: {error}");
    }
}
