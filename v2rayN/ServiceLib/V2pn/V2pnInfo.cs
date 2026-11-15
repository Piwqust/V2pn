namespace ServiceLib;

/// <summary>
/// What sets V2pn's own releases apart from v2rayN's: its version, where its packages live and
/// which keys sign them.
/// </summary>
public static class V2pnInfo
{
    /// <summary>GitHub repository whose releases carry V2pn packages.</summary>
    public const string Repo = "Piwqust/V2pn";

    /// <summary>The signed list of a release's packages; see <see cref="V2pnManifest"/>.</summary>
    public const string ManifestName = "release.json";

    public const string SignatureSuffix = ".sig";

    public static string Version { get; } = Metadata("V2pnVersion") ?? "0.0.0";

    /// <summary>
    /// True for packages made by tools/release.ps1. A local build is not: it must never unpack a
    /// release over its own build folder.
    /// </summary>
    public static bool IsRelease { get; } = Metadata("V2pnChannel") == "release";

    /// <summary>The version as a person sees it: "1.0.0", or "1.0.0-dev" for a local build.</summary>
    public static string DisplayVersion => IsRelease ? Version : $"{Version}-dev";

    /// <summary>The commit the package was built from, "+" when it had uncommitted changes; null when unknown.</summary>
    public static string? Commit { get; } = Metadata("V2pnCommit");

    /// <summary>The source folder of a developer-mode build; null in every published version.</summary>
    public static string? SourceDir { get; } = Metadata("V2pnSourceDir");

    /// <summary>
    /// A version the developer mode built from the sources on this computer ("1.0.1-local.202611141320").
    /// It installs like a release, but only the person who built it has it.
    /// </summary>
    public static bool IsLocalVersion(string? version) =>
        version?.IndexOf('-') is int dash and > 0 && version[(dash + 1)..].StartsWith(LocalTag + ".", StringComparison.Ordinal);

    public const string LocalTag = "local";

    /// <summary>The v2rayN release V2pn is built on; panels also see it in the User-Agent.</summary>
    public static string UpstreamVersion => Utils.GetVersionInfo();

    /// <summary>The releases page for people ("What's new").</summary>
    public static string ReleasesUrl => $"{Global.GithubUrl}/{Repo}/releases";

    /// <summary>Places that serve releases, in the order they are tried. Set at build time.</summary>
    public static IReadOnlyList<string> UpdateSources { get; } = Split(Metadata("V2pnUpdateSources")).Select(s => s.TrimEnd('/')).ToList();

    /// <summary>Public keys a release manifest may be signed with. Set at build time.</summary>
    public static IReadOnlyList<string> UpdateKeys { get; } = Split(Metadata("V2pnUpdateKeys"));

    /// <summary>
    /// Public root keys that sign partner certificates (V2pnPartner): the owner of V2pn vouches for a provider
    /// with them. Separate from the release keys, so a partner key can never sign an update. Set at build time.
    /// </summary>
    public static IReadOnlyList<string> PartnerKeys { get; } = Split(Metadata("V2pnPartnerKeys"));

    /// <summary>
    /// V2pn replaces itself only where it ships a portable package (Windows, macOS) and only in a
    /// release build. Where a package manager owns the install it does not offer to.
    /// </summary>
    public static bool SelfUpdateSupported => IsRelease && UpdateKeys.Count > 0 && UpdateSources.Count > 0
        && !Utils.IsPackagedInstall() && PackageName() != null;

    /// <summary>The package for this machine, or null for a system V2pn does not ship for.</summary>
    public static string? PackageName(Architecture? arch = null, OSPlatform? os = null)
    {
        var platform = os ?? (Utils.IsWindows() ? OSPlatform.Windows : Utils.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux);
        var system = platform == OSPlatform.Windows ? "windows" : platform == OSPlatform.OSX ? "macos" : null;
        var cpu = (arch ?? RuntimeInformation.ProcessArchitecture) switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null,
        };
        return system == null || cpu == null ? null : $"V2pn-{system}-{cpu}.zip";
    }

    /// <summary>
    /// The same app without the cores, published for Windows next to the full package. An update
    /// never replaces cores that are already installed, so downloading them again is wasted traffic.
    /// </summary>
    public static string? UpdatePackageName(Architecture? arch = null, OSPlatform? os = null) =>
        PackageName(arch, os)?.Replace(".zip", "-update.zip");

    /// <summary>What an update downloads: the package without cores when the release has one.</summary>
    public static V2pnManifestFile? UpdatePackage(V2pnManifest manifest) =>
        manifest.File(UpdatePackageName()) ?? manifest.File(PackageName());

    public static string ManifestUrl(string source) => $"{source}/latest/download/{ManifestName}";

    public static string PackageUrl(string source, string version, string packageName) => $"{source}/download/v{version}/{packageName}";

    /// <summary>True when the file has the size and SHA-256 the signed manifest lists for it.</summary>
    public static bool VerifyPackage(string fileName, V2pnManifestFile expected, out string error)
    {
        error = string.Empty;
        try
        {
            using var stream = File.OpenRead(fileName);
            if (stream.Length == expected.Size && Convert.ToHexStringLower(SHA256.HashData(stream)) == expected.Sha256)
            {
                return true;
            }
            error = "The downloaded package is damaged (it does not match the release manifest).";
            return false;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnInfo), ex);
            error = ex.Message;
            return false;
        }
    }

    private static string? Metadata(string key) => typeof(V2pnInfo).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == key)?.Value;

    private static List<string> Split(string? value) =>
        (value ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
