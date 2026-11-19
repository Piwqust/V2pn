namespace ServiceLib.Tests.V2pn;

public class V2pnBlockCheckTests
{
    [Test]
    public async Task Decide_ReadsTheChecks()
    {
        // Connected and both downloads went through.
        await V2pnBlockCheck.Decide(true, true, true, true, true, true, 262144).Should().BeEqualTo(EV2pnBlockVerdict.Fine);
        // Nothing opens.
        await V2pnBlockCheck.Decide(true, false, false, false, null, null, 0).Should().BeEqualTo(EV2pnBlockVerdict.NoInternet);
        // Russian sites only: a whitelist.
        await V2pnBlockCheck.Decide(true, true, false, false, false, null, 0).Should().BeEqualTo(EV2pnBlockVerdict.Whitelist);
        // The internet is fine, the server's port is not.
        await V2pnBlockCheck.Decide(true, true, true, false, false, null, 0).Should().BeEqualTo(EV2pnBlockVerdict.ServerBlocked);
        // Small passes, large stops at 16 KB.
        await V2pnBlockCheck.Decide(true, true, true, true, true, false, 16 * 1024).Should().BeEqualTo(EV2pnBlockVerdict.Freeze);
        // In VPN mode only the tunnel is checked.
        await V2pnBlockCheck.Decide(false, null, null, null, true, false, 20000).Should().BeEqualTo(EV2pnBlockVerdict.Freeze);
        // The port opens, nothing passes.
        await V2pnBlockCheck.Decide(true, true, true, true, false, null, 0).Should().BeEqualTo(EV2pnBlockVerdict.ProxyCut);
        // Disconnected, all fine.
        await V2pnBlockCheck.Decide(true, true, true, true, null, null, 0).Should().BeEqualTo(EV2pnBlockVerdict.Fine);
        // A slow download that stopped late is not the freeze.
        await V2pnBlockCheck.Decide(true, true, true, true, true, false, 200000).Should().BeEqualTo(EV2pnBlockVerdict.Unknown);
    }

    private static ProfileItem Server(EConfigType type, string security, string network, int port = 443, string sni = "my.example.org")
    {
        var node = new ProfileItem
        {
            ConfigType = type,
            StreamSecurity = security,
            Network = network,
            Port = port,
            Sni = sni,
            Address = "1.2.3.4",
        };
        return node;
    }

    [Test]
    public async Task Advice_NamesWhatGetsBlocked()
    {
        await V2pnServerAdvice.Assess(Server(EConfigType.VLESS, Global.StreamSecurityReality, nameof(ETransport.raw)))
            .Should().BeEquivalentTo([EV2pnAdvice.RealityRawTcp]);
        await V2pnServerAdvice.Assess(Server(EConfigType.VLESS, Global.StreamSecurityReality, nameof(ETransport.xhttp), sni: "www.microsoft.com"))
            .Should().BeEquivalentTo([EV2pnAdvice.BigBrandSni]);
        await V2pnServerAdvice.Assess(Server(EConfigType.VLESS, Global.StreamSecurity, nameof(ETransport.xhttp), port: 8443))
            .Should().BeEquivalentTo([EV2pnAdvice.NonStandardPort]);
        await V2pnServerAdvice.Assess(Server(EConfigType.VMess, string.Empty, nameof(ETransport.ws)))
            .Should().BeEquivalentTo([EV2pnAdvice.NoTls]);
        await V2pnServerAdvice.Assess(Server(EConfigType.WireGuard, string.Empty, string.Empty, port: 51820))
            .Should().BeEquivalentTo([EV2pnAdvice.KnownProtocol, EV2pnAdvice.Udp]);
        await V2pnServerAdvice.Assess(Server(EConfigType.VLESS, Global.StreamSecurity, nameof(ETransport.xhttp))).Should().BeEmpty();
        await V2pnServerAdvice.IsBigBrand("microsoft.com.evil.org").Should().BeFalse();
    }
}
