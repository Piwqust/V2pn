namespace ServiceLib;

/// <summary>
/// What sets V2pn's own releases apart from v2rayN's: its version, where its packages live and how
/// a downloaded package is checked before AmazTool installs it.
/// </summary>
public static class V2pnInfo
{
    /// <summary>GitHub repository whose releases carry V2pn packages.</summary>
    public const string Repo = "Piwqust/V2pn";

    /// <summary>Published next to every package; holds its SHA-256.</summary>
    public const string HashSuffix = ".sha256";

    // Points update checks at another copy of the releases page. tools/update-e2e.ps1 uses it to
    // install older builds and update them from a local server without publishing anything.
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

    /// <summary>
    /// V2pn publishes portable Windows packages only. Elsewhere, and where a package manager owns
    /// the install, the app does not offer to replace itself.
    /// </summary>
    public static bool SelfUpdateSupported => Utils.IsWindows() && !Utils.IsPackagedInstall() && PackageName() != null;

    /// <summary>The package for this machine, or null for an architecture V2pn does not ship.</summary>
    public static string? PackageName(Architecture? arch = null) => (arch ?? RuntimeInformation.ProcessArchitecture) switch
    {
        Architecture.X64 => "V2pn-windows-x64.zip",
        Architecture.Arm64 => "V2pn-windows-arm64.zip",
        _ => null,
    };

    public static string PackageUrl(string tag, string packageName) => $"{ReleasesUrl}/download/{tag}/{packageName}";

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
            error = "The release has no checksum.";
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
            error = "The downloaded package is damaged (checksum mismatch).";
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
