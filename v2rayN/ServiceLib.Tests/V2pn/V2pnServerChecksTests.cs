namespace ServiceLib.Tests.V2pn;

public class V2pnServerChecksTests
{
    [Test]
    [Arguments(nameof(ETransport.kcp))]
    [Arguments(nameof(ETransport.xhttp))]
    [Arguments(nameof(ETransport.xdrive))]
    public async Task SingBox_CannotCarryXrayOnlyTransports(string network)
    {
        var error = V2pnServerChecks.TransportError(EConfigType.VLESS, network, ECoreType.sing_box);

        await error.Should().BeEqualTo(string.Format(ResUI.MsgCoreNotSupportNetwork, nameof(ECoreType.sing_box), network));
    }

    [Test]
    [Arguments(nameof(ETransport.kcp))]
    [Arguments(nameof(ETransport.xhttp))]
    [Arguments(nameof(ETransport.xdrive))]
    public async Task Xray_CarriesThem(string network)
    {
        await V2pnServerChecks.TransportError(EConfigType.VLESS, network, ECoreType.Xray).Should().BeNull();
    }

    [Test]
    [Arguments(nameof(ETransport.raw))]
    [Arguments(nameof(ETransport.ws))]
    [Arguments(nameof(ETransport.grpc))]
    public async Task SingBox_CarriesTheCommonOnes(string network)
    {
        await V2pnServerChecks.TransportError(EConfigType.VLESS, network, ECoreType.sing_box).Should().BeNull();
    }

    [Test]
    public async Task OnlyTheTransportCounts_NotTheRestOfTheServer()
    {
        // A Shadowsocks probe has no password or method; that is the form's own check, not the transport's.
        await V2pnServerChecks.TransportError(EConfigType.Shadowsocks, nameof(ETransport.ws), ECoreType.sing_box).Should().BeNull();
        await V2pnServerChecks.TransportError(EConfigType.Shadowsocks, nameof(ETransport.grpc), ECoreType.sing_box).Should().NotBeNull();
    }

    [Test]
    public async Task EffectiveCore_FormChoiceFirst_ThenTheProtocolsCore()
    {
        await V2pnServerChecks.EffectiveCore(EConfigType.VLESS, nameof(ECoreType.sing_box)).Should().BeEqualTo(ECoreType.sing_box);
        await V2pnServerChecks.EffectiveCore(EConfigType.VLESS, nameof(ECoreType.Xray)).Should().BeEqualTo(ECoreType.Xray);
        await V2pnServerChecks.EffectiveCore(EConfigType.VLESS, string.Empty)
            .Should().BeEqualTo(AppManager.Instance.GetCoreType(null, EConfigType.VLESS));
    }
}
