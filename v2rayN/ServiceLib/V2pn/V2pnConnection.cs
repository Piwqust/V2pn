using Microsoft.Win32;

namespace ServiceLib;

/// <summary>
/// What "Disconnect" means in V2pn. In v2rayN, "clear system proxy" leaves the core running on its
/// local port and wipes whatever proxy Windows has, even one another VPN client set. In V2pn,
/// disconnected means off: the core stops, and the Windows proxy is cleared only when it is V2pn's own.
/// </summary>
public static class V2pnConnection
{
    private const string RegPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    /// <summary>Neither the system proxy nor TUN is on: the core has no reason to run.</summary>
    public static bool IsOff(Config config) =>
        !config.TunModeItem.EnableTun && config.SystemProxyItem.SysProxyType == ESysProxyType.ForcedClear;

    /// <summary>
    /// Stops cores left running by a V2pn that was force-quit or crashed. Windows ends them with the
    /// app (a job object); elsewhere they keep the local port, and the next connect would find it
    /// busy. Only programs from V2pn's own bin folder are touched. A core started with sudo for VPN
    /// mode cannot be stopped without the password; it is logged and left.
    /// </summary>
    public static void StopLeftoverCores()
    {
        var bin = Utils.GetBinPath(string.Empty);
        if (bin.IsNullOrEmpty())
        {
            return;
        }
        foreach (var info in CoreInfoManager.Instance.GetCoreInfo())
        {
            foreach (var name in info.CoreExes ?? [])
            {
                Process[] running;
                try
                {
                    running = Process.GetProcessesByName(name);
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(nameof(V2pnConnection), ex);
                    continue;
                }
                foreach (var process in running)
                {
                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (path?.StartsWith(bin, StringComparison.Ordinal) == true)
                        {
                            Logging.SaveLog($"V2pn: stopping a leftover core, {path} (pid {process.Id})");
                            process.Kill();
                            process.WaitForExit(3000);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.SaveLog($"V2pn: a leftover {name} (pid {process.Id}) could not be stopped: {ex.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
        }
    }

    /// <summary>
    /// macOS: true when the system proxy is off or points at this V2pn's local port, so clearing it
    /// can only remove what V2pn put there. A Mac that already runs v2rayN (or any other client) has
    /// its proxy set to that client's port; V2pn starting next to it must not switch it off.
    /// </summary>
    [SupportedOSPlatform("macos")]
    public static async Task<bool> MacProxyIsOursAsync(int port)
    {
        try
        {
            return MacProxyIsOurs(await Utils.GetCliWrapOutput("/usr/sbin/scutil", "--proxy"), port);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnConnection), ex);
            return true;
        }
    }

    /// <summary>Reads the output of "scutil --proxy". When it cannot be read, clearing stays allowed, as in v2rayN.</summary>
    public static bool MacProxyIsOurs(string? scutilProxy, int port)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in (scutilProxy ?? string.Empty).Split((char)10))
        {
            var separator = line.IndexOf(" : ", StringComparison.Ordinal);
            if (separator > 0)
            {
                values[line[..separator].Trim()] = line[(separator + 3)..].Trim();
            }
        }
        foreach (var kind in (string[])["HTTP", "HTTPS", "SOCKS"])
        {
            if (!values.TryGetValue(kind + "Enable", out var enabled) || enabled != "1")
            {
                continue;
            }
            values.TryGetValue(kind + "Proxy", out var host);
            values.TryGetValue(kind + "Port", out var itsPort);
            var local = host is "127.0.0.1" or "localhost" or "::1";
            if (!local || itsPort != port.ToString())
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// At least one server is stored. Read from the database, because the UI's server count stays 0 until
    /// the list has loaded, and an empty list switches V2pn off.
    /// </summary>
    public static async Task<bool> HasAnyServerAsync() =>
        await SQLiteHelper.Instance.TableAsync<ProfileItem>().CountAsync() > 0;

    /// <summary>
    /// True when the Windows proxy points at this V2pn's ports (or nothing is set), so clearing it
    /// can only remove what V2pn put there.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static bool WindowsProxyIsOurs(int socksPort, int pacPort)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegPath, false);
            if (key == null)
            {
                return true;
            }
            var ours = IsOurs(
                key.GetValue("ProxyEnable") is int e && e != 0,
                key.GetValue("ProxyServer") as string,
                key.GetValue("AutoConfigURL") as string,
                socksPort,
                pacPort);
            // The original v2rayN uses the same 10808. When V2pn's core is not running but something
            // still answers on that port, the proxy belongs to that other client: clearing it would cut it off.
            // When nothing answers, the proxy is dead and clearing it gives the internet back.
            if (ours && !CoreManager.Instance.IsCoreAlive && LocalPortBusy(socksPort))
            {
                Logging.SaveLog($"V2pn: the system proxy points at port {socksPort}, which another program serves; left as is");
                return false;
            }
            return ours;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnConnection), ex);
            // Unknown: leave the setting alone rather than risk removing someone else's proxy.
            return false;
        }
    }

    /// <summary>The decision behind <see cref="WindowsProxyIsOurs"/>, given the registry values.</summary>
    public static bool IsOurs(bool enabled, string? server, string? pac, int socksPort, int pacPort)
    {
        if (enabled && !PointsAt(server, socksPort))
        {
            return false;
        }
        if (pac.IsNotEmpty() && !PointsAt(pac, pacPort))
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Something answers on 127.0.0.1:<paramref name="port"/>. Tested by connecting: on this Windows build the
    /// listener table (GetActiveTcpListeners) misses listeners, and a test bind succeeds even while xray holds
    /// the port. An open port answers in milliseconds; a closed one makes Windows retry for about 2 s, so the
    /// wait is capped and a timeout counts as free.
    /// </summary>
    public static bool LocalPortBusy(int port)
    {
        if (port is <= 0 or > IPEndPoint.MaxPort)
        {
            return false;
        }
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), cts.Token).AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool PointsAt(string? value, int port) =>
        port > 0
        && value.IsNotEmpty()
        && Regex.IsMatch(value, $@"(?:{Regex.Escape(Global.Loopback)}|localhost):{port}(?!\d)", RegexOptions.IgnoreCase);
}
