namespace ServiceLib;

/// <summary>
/// Which network the computer is on, as a short key: home Wi-Fi and a phone's hotspot get different keys, the same
/// router gets the same key every day. Made from the router's hardware address (the gateway's MAC), which needs no
/// permissions on either system, with the gateway's IP when the MAC cannot be read. Only a hash is kept, so the
/// config holds neither network names nor addresses.
/// </summary>
public static class V2pnNetworkId
{
    // iPhone hotspots always hand out 172.20.10.0/28; older Android ones 192.168.43.0/24.
    private static readonly string[] HotspotGateways = ["172.20.10.1", "192.168.43.1"];

    private static readonly string[] MobileAdapterWords =
        ["Remote NDIS", "RNDIS", "WWAN", "Mobile Broadband", "Modem", "iPhone", "Android", "LTE", "4G", "5G"];

    // Adapters of VPN clients (V2pn's own TUN among them): their gateway is not the network the computer is on.
    private static readonly string[] VirtualAdapterWords =
        ["Wintun", "TAP-", "WireGuard", "singbox", "sing-box", "utun", "Hyper-V", "VirtualBox", "VMware", "Loopback", "Npcap", "ZeroTier", "Tailscale"];

    /// <summary>The key of the network found last; empty until the first look.</summary>
    public static string Current { get; private set; } = string.Empty;

    /// <summary>The network looks like mobile internet: a modem, a phone over USB, or a phone's hotspot.</summary>
    public static bool LooksMobile { get; private set; }

    /// <summary>Looks at the network again; returns true when it is another network than before.</summary>
    public static async Task<bool> RefreshAsync()
    {
        try
        {
            var gateway = FindGateway();
            if (gateway == null)
            {
                var changed = Current.IsNotEmpty();
                Current = string.Empty;
                LooksMobile = false;
                return changed;
            }
            var mac = await GatewayMacAsync(gateway.Value.Address);
            var key = Compose(gateway.Value.Kind, mac, gateway.Value.Address.ToString());
            var mobile = IsMobile(gateway.Value.Description, gateway.Value.Type, gateway.Value.Address.ToString());
            var different = key != Current;
            Current = key;
            LooksMobile = mobile;
            return different;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnNetworkId), ex);
            return false;
        }
    }

    /// <summary>The key of a network: the kind of adapter and the router's MAC, or its IP when the MAC is unknown.</summary>
    public static string Compose(string kind, string? gatewayMac, string gatewayIp)
    {
        var mac = NormalizeMac(gatewayMac);
        var source = $"{kind}|{(mac.IsNotEmpty() ? mac : gatewayIp)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..12].ToLowerInvariant();
    }

    public static bool IsMobile(string? adapterDescription, NetworkInterfaceType type, string? gatewayIp) =>
        type is NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 or NetworkInterfaceType.Ppp
        || HotspotGateways.Contains(gatewayIp)
        || MobileAdapterWords.Any(w => adapterDescription?.Contains(w, StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>"aa:bb:cc:dd:ee:ff" from any common spelling; empty for an unknown or all-zero address.</summary>
    public static string NormalizeMac(string? mac)
    {
        if (mac.IsNullOrEmpty())
        {
            return string.Empty;
        }
        var parts = mac!.Trim().Split(':', '-');
        if (parts.Length != 6)
        {
            return string.Empty;
        }
        var bytes = new List<string>();
        foreach (var part in parts)
        {
            if (part.Length is 0 or > 2 || !int.TryParse(part, System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                return string.Empty;
            }
            bytes.Add(b.ToString("x2"));
        }
        var text = string.Join(':', bytes);
        return text is "00:00:00:00:00:00" or "ff:ff:ff:ff:ff:ff" ? string.Empty : text;
    }

    /// <summary>The MAC in macOS "arp -n" output: "? (192.168.1.1) at a:b:c:d:e:f on en0 ifscope [ethernet]".</summary>
    public static string? ParseArp(string? output)
    {
        var match = Regex.Match(output ?? string.Empty, @"\bat\s+([0-9a-fA-F]{1,2}(?::[0-9a-fA-F]{1,2}){5})\b");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static (IPAddress Address, string Kind, string Description, NetworkInterfaceType Type)? FindGateway()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel
                || VirtualAdapterWords.Any(w => nic.Description.Contains(w, StringComparison.OrdinalIgnoreCase)
                                                || nic.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            IPInterfaceProperties props;
            try
            {
                props = nic.GetIPProperties();
            }
            catch
            {
                continue;
            }
            var gateway = props.GatewayAddresses
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
            if (gateway == null)
            {
                continue;
            }
            var kind = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "wifi"
                : nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet ? "wired"
                : nic.NetworkInterfaceType.ToString().ToLowerInvariant();
            return (gateway, kind, nic.Description, nic.NetworkInterfaceType);
        }
        return null;
    }

    private static async Task<string?> GatewayMacAsync(IPAddress gateway)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return WindowsArp(gateway);
            }
            if (Utils.IsMacOS() || Utils.IsLinux())
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                return ParseArp(await Utils.GetCliWrapOutput("arp", ["-n", gateway.ToString()], cts.Token));
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnNetworkId), ex);
        }
        return null;
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref uint physicalAddrLen);

    // SendARP answers from the system's cache for the gateway, which is always there while it routes; no window, no process.
    [SupportedOSPlatform("windows")]
    private static string? WindowsArp(IPAddress gateway)
    {
        var mac = new byte[6];
        uint length = 6;
        var dest = BitConverter.ToUInt32(gateway.GetAddressBytes(), 0);
        return SendARP(dest, 0, mac, ref length) == 0 && length == 6
            ? string.Join(':', mac.Select(b => b.ToString("x2")))
            : null;
    }
}
