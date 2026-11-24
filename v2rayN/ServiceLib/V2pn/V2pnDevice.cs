using Microsoft.Win32;

namespace ServiceLib;

/// <summary>
/// V2pn: the device headers panels count devices by (Remnawave's HWID device limit, the convention Happ started):
/// x-hwid, x-device-os, x-ver-os, x-device-model, sent with every subscription download. A panel with the limit on
/// answers a client without x-hwid with nothing (Remnawave: 404), so V2pn got no servers from such providers at all.
/// <para>
/// The id is a hash of the computer's own id under a V2pn-only salt: the same after V2pn is reinstalled (a reinstall
/// does not take another device slot), never the computer's id itself, and not the id other apps send. Without a
/// computer id it is a random one kept in V2pn's settings folder. A subscription's own x-hwid header wins.
/// </para>
/// </summary>
public static class V2pnDevice
{
    public const string HwidHeader = "x-hwid";
    public const string OsHeader = "x-device-os";
    public const string OsVersionHeader = "x-ver-os";
    public const string ModelHeader = "x-device-model";

    private const string FallbackFile = "v2pn-device-id";
    private static readonly Lazy<string> _hwid = new(() => HwidFrom(MachineId() ?? StoredRandomId()));

    /// <summary>This device's id for panels: 32 lower-case hex characters (Remnawave takes [a-zA-Z0-9=-]{10,64}).</summary>
    public static string Hwid => _hwid.Value;

    /// <summary>The id a panel sees for a computer whose own id is <paramref name="machineId"/>.</summary>
    public static string HwidFrom(string machineId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("v2pn-hwid:" + machineId.Trim().ToLowerInvariant())))[..32];

    /// <summary>The headers for a subscription download, under the subscription's own headers (theirs win).</summary>
    public static Dictionary<string, string> Headers(IReadOnlyDictionary<string, string>? own)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [HwidHeader] = Hwid,
            [OsHeader] = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsLinux() ? "Linux" : "Desktop",
            [OsVersionHeader] = OsVersion(),
            [ModelHeader] = Model(),
        };
        foreach (var (name, value) in own ?? new Dictionary<string, string>())
        {
            headers[name] = value;
        }
        return headers;
    }

    private static string OsVersion()
    {
        var version = Environment.OSVersion.Version;
        return OperatingSystem.IsWindows() ? $"{version.Major}.{version.Minor}.{version.Build}" : $"{version.Major}.{version.Minor}";
    }

    private static string Model()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                if (bios?.GetValue("SystemProductName") is string product && Plain(product) is { } name)
                {
                    return name;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // No model, then.
            }
            return "PC";
        }
        return OperatingSystem.IsMacOS() ? "Mac" : "PC";
    }

    private static string? Plain(string text)
    {
        text = text.Trim();
        return text.Length is > 0 and <= 64 && !text.Any(char.IsControl) && text.All(c => c < 128) ? text : null;
    }

    /// <summary>The computer's own id: MachineGuid on Windows, IOPlatformUUID on a Mac; null when it cannot be read.</summary>
    private static string? MachineId()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                return key?.GetValue("MachineGuid") is string guid && guid.Length >= 16 ? guid : null;
            }
            if (OperatingSystem.IsMacOS())
            {
                using var process = Process.Start(new ProcessStartInfo("/usr/sbin/ioreg", "-rd1 -c IOPlatformExpertDevice")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                var output = process?.StandardOutput.ReadToEnd() ?? string.Empty;
                process?.WaitForExit(5000);
                var match = Regex.Match(output, "\"IOPlatformUUID\"\\s*=\\s*\"([0-9A-Fa-f-]{16,})\"");
                return match.Success ? match.Groups[1].Value : null;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnDevice), ex);
        }
        return null;
    }

    private static string StoredRandomId()
    {
        var path = Utils.GetConfigPath(FallbackFile);
        try
        {
            if (File.Exists(path) && File.ReadAllText(path).Trim() is { Length: >= 16 } kept)
            {
                return kept;
            }
            var made = Guid.NewGuid().ToString("N");
            File.WriteAllText(path, made);
            return made;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnDevice), ex);
            return Environment.MachineName;
        }
    }

    /// <summary>What a panel said about this device in its answer, from Remnawave's x-hwid-* headers.</summary>
    public static EV2pnDeviceRefusal Refusal(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        bool Set(string name) => headers.Any(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)
                                                  && h.Value.Any(v => string.Equals(v.Trim(), "true", StringComparison.OrdinalIgnoreCase)));
        return Set("x-hwid-max-devices-reached") || Set("x-hwid-limit") ? EV2pnDeviceRefusal.LimitReached
            : Set("x-hwid-not-supported") ? EV2pnDeviceRefusal.NotAccepted
            : EV2pnDeviceRefusal.None;
    }
}

/// <summary>Why a panel would not give this device the subscription.</summary>
public enum EV2pnDeviceRefusal
{
    None,

    /// <summary>The subscription has as many devices as its plan allows.</summary>
    LimitReached,

    /// <summary>The panel did not take this device's id.</summary>
    NotAccepted,
}
