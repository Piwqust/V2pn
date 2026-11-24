namespace ServiceLib;

/// <summary>What V2pn decides about the VPN (TUN) interface on top of v2rayN's settings.</summary>
public static class V2pnTun
{
    /// <summary>
    /// Whether the tunnel takes IPv6 too. v2rayN gives the sing-box tunnel an IPv6 address only when "IPv6 address" is
    /// on (off by default), and sing-box routes only the families its tunnel has an address for. On a network with
    /// global IPv6 (many mobile and some home providers) IPv6 then went past the tunnel on macOS, where sing-box has
    /// no strict route, and was cut off on Windows. Xray's tunnel already routes ::/0 in that case; this does the same
    /// for sing-box. A host with only link-local or ULA addresses has no IPv6 to lose, so nothing changes there.
    /// </summary>
    public static bool UsesIPv6(Config config, bool hasGlobalIPv6Address)
        => config.TunModeItem.EnableIPv6Address || hasGlobalIPv6Address;
}
