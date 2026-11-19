using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace ServiceLib;

/// <summary>
/// V2pn: programs that only ever go out through V2pn, such as Claude. Their connections pass only to the computer
/// itself (V2pn's local proxy, 127.0.0.1) and through V2pn's tunnel; anything else they try is blocked. Unlike the
/// kill switch for the whole connection (<see cref="V2pnKillSwitch"/>), this holds always: with V2pn connected,
/// disconnected, closed or not yet started, so a program opened while the VPN is down never shows the real address.
///
/// The filters are persistent Windows Filtering Platform filters, made and removed with administrator rights: from
/// V2pn running as administrator, or by V2pn started once more with "--app-killswitch", which Windows confirms. They
/// match a program by the end of its path ("\claude.exe"), so new versions in new folders are covered too.
/// Removing V2pn removes them (V2pn.iss); otherwise the programs would stay cut off.
/// </summary>
public static class V2pnAppKillSwitch
{
    /// <summary>What "Claude" adds: the desktop app, its helper, and Claude Code.</summary>
    public static readonly IReadOnlyList<string> ClaudePreset = ["claude.exe", "claude-ssh-broker.exe"];

    public const string Command = "--app-killswitch";

    // Where V2pn remembers what it put in place, so it can be removed by an uninstaller that has no config to read.
    private const string MarkerKey = @"Software\V2pn";
    private const string MarkerValue = "AppKillSwitch";

    /// <summary>"Claude.EXE", "C:\…\claude.exe" or "claude" → "claude.exe"; empty or path-like nonsense → null.</summary>
    public static string? Normalize(string? name)
    {
        name = name?.Trim().Trim('"');
        if (name.IsNullOrEmpty())
        {
            return null;
        }
        name = Path.GetFileName(name.Replace('/', '\\'));
        if (name.IsNullOrEmpty() || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name += ".exe";
        }
        return name.ToLowerInvariant();
    }

    public static List<string> Names(IEnumerable<string>? names) =>
        (names ?? []).Select(Normalize).OfType<string>().Distinct().Order(StringComparer.Ordinal).ToList();

    /// <summary>The names V2pn should hold now: none unless the switch is on.</summary>
    public static List<string> Wanted(Config config)
    {
        var item = V2pnKillSwitch.Item(config);
        return item.AppsEnabled ? Names(item.Apps) : [];
    }

    /// <summary>
    /// The filters for these programs: on each layer, a permit for the computer itself and one for the tunnel, then a
    /// block, all matching any of the programs. Fixed keys, so that a later run can delete exactly these.
    /// </summary>
    public static List<V2pnFirewall.FilterSpec> Filters(IReadOnlyList<string> names, IReadOnlyList<(System.Net.IPAddress Address, int Prefix)> tunnel)
    {
        var filters = new List<V2pnFirewall.FilterSpec>();
        if (names.Count == 0)
        {
            return filters;
        }
        var programs = names.Select(n => new V2pnFirewall.Condition(V2pnFirewall.Field.AppNameSuffix, n)).ToList();
        foreach (var layer in Enum.GetValues<V2pnFirewall.Layer>())
        {
            var v6 = layer is V2pnFirewall.Layer.ConnectV6 or V2pnFirewall.Layer.AcceptV6;
            filters.Add(new("V2pn: only through V2pn (the computer itself)", layer, 15, true,
                [.. programs, new(V2pnFirewall.Field.LoopbackFlag, 1u)], Key(layer, "loopback")));
            var ownTunnel = tunnel
                .Where(n => n.Address.AddressFamily == (v6 ? System.Net.Sockets.AddressFamily.InterNetworkV6 : System.Net.Sockets.AddressFamily.InterNetwork))
                .Select(n => new V2pnFirewall.Condition(V2pnFirewall.Field.LocalAddress, n))
                .ToList();
            if (ownTunnel.Count > 0)
            {
                filters.Add(new("V2pn: only through V2pn (the tunnel)", layer, 14, true, [.. programs, .. ownTunnel], Key(layer, "tunnel")));
            }
            filters.Add(new("V2pn: only through V2pn (block)", layer, 0, false, programs, Key(layer, "block")));
        }
        return filters;
    }

    /// <summary>Every key V2pn may have used, whatever it put in place last time.</summary>
    public static IEnumerable<Guid> AllKeys() =>
        Enum.GetValues<V2pnFirewall.Layer>().SelectMany(l => new[] { Key(l, "loopback"), Key(l, "tunnel"), Key(l, "block") });

    private static Guid Key(V2pnFirewall.Layer layer, string kind) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes($"V2pn app kill switch|{layer}|{kind}")));

    // ================================================================== what is in place

    /// <summary>The programs whose block is in place, as V2pn recorded it; empty when there is none.</summary>
    public static List<string> Applied()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(MarkerKey);
            return Names((key?.GetValue(MarkerValue) as string)?.Split('|'));
        }
        catch
        {
            return [];
        }
    }

    private static void Record(IReadOnlyList<string> names)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(MarkerKey);
        if (names.Count == 0)
        {
            key.DeleteValue(MarkerValue, false);
        }
        else
        {
            key.SetValue(MarkerValue, string.Join("|", names));
        }
    }

    // ================================================================== making it so

    /// <summary>
    /// Brings the filters in line with the settings. Without administrator rights V2pn starts itself once more
    /// elevated, and Windows asks the person. Null when done, else why not (also when the person said no).
    /// </summary>
    public static async Task<string?> SyncAsync(Config config)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        var wanted = Wanted(config);
        var tunnel = TunnelNetworks(config);
        var signature = Signature(wanted, tunnel);
        if (signature == AppliedSignature())
        {
            return null;
        }
        string? error;
        if (Utils.IsAdministrator())
        {
            error = await Task.Run(() => Run(wanted, tunnel));
        }
        else
        {
            error = await RunElevatedAsync(wanted, tunnel);
        }
        if (error == null)
        {
            Record(wanted);
            SetAppliedSignature(signature);
        }
        return error;
    }

    public static List<(System.Net.IPAddress Address, int Prefix)> TunnelNetworks(Config config)
    {
        var list = new List<(System.Net.IPAddress, int)>();
        // Every address V2pn may give its tunnel: the setting can change while the filters stay.
        foreach (var cidr in Global.TunIPv4Address.Prepend(config.TunModeItem.IPv4Address)
                     .Concat(Global.TunIPv6Address.Prepend(config.TunModeItem.IPv6Address)))
        {
            if (V2pnFirewall.ParseNetwork(cidr) is { } network && !list.Contains(network))
            {
                list.Add(network);
            }
        }
        return list;
    }

    /// <summary>Replaces the filters with ones for these programs; no programs removes them all. Needs administrator rights.</summary>
    public static string? Run(IReadOnlyList<string> names, IReadOnlyList<(System.Net.IPAddress, int)> tunnel)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        var engine = IntPtr.Zero;
        try
        {
            engine = V2pnFirewall.Native.Open(dynamic: false);
            // The old filters go first, in their own transaction: their keys are reused.
            V2pnFirewall.Native.Delete(engine, AllKeys(), names.Count == 0 ? V2pnFirewall.Native.AppsSubLayer : null);
            if (names.Count > 0)
            {
                V2pnFirewall.Native.Install(engine, Filters(names, tunnel), V2pnFirewall.Native.AppsSubLayer, "V2pn: programs only through V2pn", persistent: true);
            }
            Logging.SaveLog($"V2pn: programs only through V2pn: {(names.Count == 0 ? "none" : string.Join(", ", names))}");
            return null;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn: programs only through V2pn", ex);
            return ex.Message;
        }
        finally
        {
            if (engine != IntPtr.Zero)
            {
                V2pnFirewall.Native.Close(engine);
            }
        }
    }

    private static async Task<string?> RunElevatedAsync(IReadOnlyList<string> names, IReadOnlyList<(System.Net.IPAddress Address, int Prefix)> tunnel)
    {
        var arguments = Arguments(names, tunnel);
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Utils.GetExePath(), arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process == null)
            {
                return "V2pn could not be started with administrator rights.";
            }
            await process.WaitForExitAsync();
            return process.ExitCode == 0 ? null : $"exit code {process.ExitCode}";
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return "Windows did not get permission.";
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn: programs only through V2pn, elevated", ex);
            return ex.Message;
        }
    }

    public static string Arguments(IReadOnlyList<string> names, IReadOnlyList<(System.Net.IPAddress Address, int Prefix)> tunnel) =>
        $"{Command} --names \"{string.Join("|", names)}\" --tunnel \"{string.Join("|", tunnel.Select(n => $"{n.Address}/{n.Prefix}"))}\"";

    /// <summary>
    /// The elevated run: "V2pn.exe --app-killswitch --names a.exe|b.exe --tunnel 172.18.0.0/30|…". No names removes
    /// everything. Program.Main calls it before anything else; true when the arguments were this command.
    /// </summary>
    public static bool TryRunCommand(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || args[0] != Command)
        {
            return false;
        }
        string? Value(string name)
        {
            var at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }
        var names = Names(Value("--names")?.Split('|', StringSplitOptions.RemoveEmptyEntries));
        var tunnel = (Value("--tunnel")?.Split('|', StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(V2pnFirewall.ParseNetwork).OfType<(System.Net.IPAddress, int)>().ToList();
        exitCode = Run(names, tunnel) == null ? 0 : 1;
        return true;
    }

    // What was last put in place, so an unchanged setting asks Windows for nothing.
    private const string SignatureValue = "AppKillSwitchApplied";

    private static string Signature(IReadOnlyList<string> names, IReadOnlyList<(System.Net.IPAddress Address, int Prefix)> tunnel) =>
        names.Count == 0 ? string.Empty : string.Join("|", names) + "#" + string.Join("|", tunnel.Select(n => $"{n.Address}/{n.Prefix}"));

    private static string AppliedSignature()
    {
        if (!OperatingSystem.IsWindows())
        {
            return string.Empty;
        }
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(MarkerKey);
            return key?.GetValue(SignatureValue) as string ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void SetAppliedSignature(string signature)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(MarkerKey);
        if (signature.Length == 0)
        {
            key.DeleteValue(SignatureValue, false);
        }
        else
        {
            key.SetValue(SignatureValue, signature);
        }
    }
}
