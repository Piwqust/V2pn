namespace ServiceLib;

/// <summary>What in a server's settings makes it easy to block in Russia, by what the forums and measurements of 2026 show.</summary>
public enum EV2pnAdvice
{
    /// <summary>REALITY straight over TCP: since February 2026 such connections often freeze after ~16 KB.</summary>
    RealityRawTcp,

    /// <summary>The name of a big foreign site as SNI: the filter compares the name with the address it goes to.</summary>
    BigBrandSni,

    /// <summary>TLS on a port other than 443, where real HTTPS lives.</summary>
    NonStandardPort,

    /// <summary>No TLS at all: the protocol is seen as it is.</summary>
    NoTls,

    /// <summary>Plain WireGuard or Shadowsocks, both blocked across the country since 2025.</summary>
    KnownProtocol,

    /// <summary>A UDP transport: QUIC and UDP are often cut on mobile networks.</summary>
    Udp,

    /// <summary>Encrypted Client Hello, which Russia blocks.</summary>
    Ech,
}

public static class V2pnServerAdvice
{
    // Foreign sites whose names the filter is known to check against the address (habr, November 2026), and their kin.
    private static readonly string[] BigBrands =
    [
        "microsoft.com", "apple.com", "icloud.com", "google.com", "googleapis.com", "cloudflare.com", "amazon.com",
        "aws.amazon.com", "yahoo.com", "bing.com", "live.com", "office.com", "facebook.com", "instagram.com",
        "whatsapp.com", "youtube.com", "twitter.com", "x.com", "netflix.com", "speedtest.net", "dl.google.com",
    ];

    public static IReadOnlyList<EV2pnAdvice> Assess(ProfileItem server)
    {
        var advice = new List<EV2pnAdvice>();
        if (server.ConfigType.IsGroupType() || server.ConfigType is EConfigType.Custom or EConfigType.Outbound)
        {
            return advice;
        }
        var udp = V2pnBlockCheck.UsesUdp(server);
        var secure = server.StreamSecurity is Global.StreamSecurity or Global.StreamSecurityReality;
        if (server.ConfigType is EConfigType.WireGuard
            || (server.ConfigType == EConfigType.Shadowsocks && !secure))
        {
            advice.Add(EV2pnAdvice.KnownProtocol);
        }
        else if (!secure && !udp && server.ConfigType is EConfigType.VLESS or EConfigType.VMess or EConfigType.Trojan
                     or EConfigType.SOCKS or EConfigType.HTTP)
        {
            advice.Add(EV2pnAdvice.NoTls);
        }
        if (udp)
        {
            advice.Add(EV2pnAdvice.Udp);
        }
        if (server.StreamSecurity == Global.StreamSecurityReality && server.GetNetwork() is nameof(ETransport.raw) or "tcp")
        {
            advice.Add(EV2pnAdvice.RealityRawTcp);
        }
        if (secure && IsBigBrand(server.Sni))
        {
            advice.Add(EV2pnAdvice.BigBrandSni);
        }
        if (secure && !udp && server.Port is > 0 and not 443)
        {
            advice.Add(EV2pnAdvice.NonStandardPort);
        }
        if (server.EchConfigList.IsNotEmpty())
        {
            advice.Add(EV2pnAdvice.Ech);
        }
        return advice;
    }

    public static bool IsBigBrand(string? sni)
    {
        var name = sni?.Trim().TrimEnd('.').ToLowerInvariant();
        if (name.IsNullOrEmpty())
        {
            return false;
        }
        return BigBrands.Any(b => name == b || name!.EndsWith("." + b, StringComparison.Ordinal));
    }
}
