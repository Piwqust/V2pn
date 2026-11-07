namespace ServiceLib;

/// <summary>
/// What sets V2pn's own releases apart from v2rayN's: its version, where its packages live and
/// how a downloaded package is checked before AmazTool installs it. v2rayN's update code calls in
/// here through a few marked hooks, so a merge from v2rayN keeps working on V2pn's channel.
/// </summary>
public static class V2pnInfo
{
    /// <summary>GitHub repository whose releases carry V2pn packages.</summary>
    public const string Repo = "Piwqust/V2pn";

    public const string PackageWin64 = "V2pn-windows-x64.zip";
    public const string PackageWinArm64 = "V2pn-windows-arm64.zip";

    /// <summary>Published next to every package; holds its SHA-256.</summary>
    public const string HashSuffix = ".sha256";

    // Points the update check at another copy of the releases page. tools/update-e2e.ps1 uses it
    // to install older builds and update them from a local server without publishing anything.
    private const string ReleasesUrlEnv = "V2PN_RELEASES_URL";

    public static string Version { get; } = typeof(V2pnInfo).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "V2pnVersion")?.Value ?? "0.0.0";

    /// <summary>The v2rayN release V2pn is built on; panels also see it in the User-Agent.</summary>
    public static string UpstreamVersion => Utils.GetVersionInfo();

    public static string ReleasesUrl
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable(ReleasesUrlEnv);
            return custom.IsNullOrEmpty() ? $"{Global.GithubUrl}/{Repo}/releases" : custom.TrimEnd('/');
        }
    }

    private static string ReleasesApiUrl
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable(ReleasesUrlEnv);
            return custom.IsNullOrEmpty() ? $"{Global.GithubApiUrl}/{Repo}/releases" : $"{custom.TrimEnd('/')}/api";
        }
    }

    /// <summary>
    /// V2pn publishes portable Windows packages only. Elsewhere, and where a package manager owns
    /// the install, the app does not offer to replace itself.
    /// </summary>
    public static bool SelfUpdateSupported => Utils.IsWindows() && !Utils.IsPackagedInstall();

    /// <summary>Points v2rayN's "update the app" entry at V2pn's releases instead of v2rayN's.</summary>
    public static void ApplyTo(CoreInfo info)
    {
        info.Url = ReleasesUrl;
        info.ReleaseApiUrl = ReleasesApiUrl;
        info.DownloadUrlWin64 = $"{ReleasesUrl}/download/{{0}}/{PackageWin64}";
        info.DownloadUrlWinArm64 = $"{ReleasesUrl}/download/{{0}}/{PackageWinArm64}";
        info.DownloadUrlLinux64 = null;
        info.DownloadUrlLinuxArm64 = null;
        info.DownloadUrlLinuxRiscV64 = null;
        info.DownloadUrlLinuxLoong64 = null;
        info.DownloadUrlOSX64 = null;
        info.DownloadUrlOSXArm64 = null;
    }

    /// <summary>Reads the SHA-256 published for a package, or null when there is none.</summary>
    public static async Task<string?> GetPublishedHashAsync(DownloadService download, string packageUrl, bool blProxy, CancellationToken cancellationToken = default)
    {
        var text = await download.TryDownloadString(packageUrl + HashSuffix, blProxy, Global.AppName, cancellationToken);
        return ParseHash(text);
    }

    /// <summary>
    /// Takes the hash from a checksum file in the usual formats: the bare hash, or
    /// "hash  file name" as written by sha256sum.
    /// </summary>
    public static string? ParseHash(string? text)
    {
        if (text.IsNullOrEmpty())
        {
            return null;
        }
        var token = text.Trim().Split((char[])[' ', '\t', '\r', '\n', '*'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return token is { Length: 64 } && token.All(Uri.IsHexDigit) ? token.ToLowerInvariant() : null;
    }

    /// <summary>
    /// True when the file matches the published hash. A package without one is refused: AmazTool
    /// overwrites the app with whatever it is given, so a cut-off or substituted download must
    /// never reach it.
    /// </summary>
    public static bool VerifyPackage(string fileName, string? expectedHash, out string error)
    {
        error = string.Empty;
        if (expectedHash.IsNullOrEmpty())
        {
            error = "V2pn: the release has no checksum, the update was not installed.";
            return false;
        }
        try
        {
            using var stream = File.OpenRead(fileName);
            var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (actual == expectedHash)
            {
                return true;
            }
            error = "V2pn: the downloaded update is damaged (checksum mismatch), it was not installed.";
            return false;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnInfo), ex);
            error = ex.Message;
            return false;
        }
    }
}
