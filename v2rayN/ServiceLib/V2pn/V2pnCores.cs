namespace ServiceLib;

/// <summary>
/// Which versions of the cores are installed beside V2pn: a person or a support chat asks "what Xray is this", and
/// the answer is what the program itself says, not what the package was built with (cores update on their own).
/// </summary>
public static partial class V2pnCores
{
    private static readonly (ECoreType Type, string Name)[] _shown = [(ECoreType.Xray, "Xray"), (ECoreType.sing_box, "sing-box")];

    [GeneratedRegex(@"v?(?<version>\d+\.\d+\.\d+(?:-[0-9a-zA-Z.-]+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();

    /// <summary>The version in what a core prints for its version argument: "Xray 25.10.15 (Xray, Penetrates...)" gives "25.10.15".</summary>
    public static string? ParseVersion(string? output)
    {
        var match = VersionRegex().Match(output ?? string.Empty);
        return match.Success ? match.Groups["version"].Value : null;
    }

    /// <summary>"Xray 25.10.15, sing-box 1.12.0" for the cores that are installed and answer; empty when none does.</summary>
    public static async Task<string> InstalledSummaryAsync(TimeSpan? timeout = null)
    {
        var found = new List<string>();
        foreach (var (type, name) in _shown)
        {
            if (await InstalledVersionAsync(type, timeout) is { } version)
            {
                found.Add($"{name} {version}");
            }
        }
        return string.Join(", ", found);
    }

    public static async Task<string?> InstalledVersionAsync(ECoreType type, TimeSpan? timeout = null)
    {
        try
        {
            var info = CoreInfoManager.Instance.GetCoreInfo(type);
            var file = info?.CoreExes?
                .Select(name => Utils.GetBinPath(Utils.GetExeName(name), info.CoreType.ToString()))
                .FirstOrDefault(File.Exists);
            if (file == null || info?.VersionArg.IsNullOrEmpty() != false)
            {
                return null;
            }
            // A core that hangs must not hold the About panel.
            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3));
            return ParseVersion(await Utils.GetCliWrapOutput(file, info.VersionArg, cts.Token));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnCores), ex);
            return null;
        }
    }
}
