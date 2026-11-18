using System.Net;
using ServiceLib.Handler;
using ServiceLib.Models.Dto;
using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

public class V2pnAvailabilityTests
{
    private const string StandardUrl = "https://www.google.com/generate_204";
    private const string CustomUrl = "https://check.example/path";

    [Test]
    [Arguments(StandardUrl, 204, AvailabilityState.Available)]
    [Arguments(StandardUrl, 200, AvailabilityState.UnexpectedStatus)]
    [Arguments(CustomUrl, 200, AvailabilityState.Available)]
    [Arguments(CustomUrl, 500, AvailabilityState.UnexpectedStatus)]
    [Arguments(StandardUrl, 302, AvailabilityState.Redirected)]
    public async Task ProbeRequiresTheExpectedResponse(string url, int status, AvailabilityState expected)
    {
        using var client = new HttpClient(new ProbeHandler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status))));
        var result = await ConnectionHandler.ProbeHttpAsync(client, url, TimeSpan.FromSeconds(2));

        await result.State.Should().BeEqualTo(expected);
        await (result.Time > 0).Should().BeEqualTo(expected == AvailabilityState.Available);
        if (result.IsFailure)
        {
            await result.HttpStatus.Should().BeEqualTo(status);
            await result.CanSearchForReplacement.Should().BeFalse();
        }
    }

    [Test]
    public async Task DeadlineIsReportedSeparatelyFromAnHttpError()
    {
        using var client = new HttpClient(new ProbeHandler(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        var result = await ConnectionHandler.ProbeHttpAsync(client, StandardUrl, TimeSpan.FromMilliseconds(35));

        await result.State.Should().BeEqualTo(AvailabilityState.TimedOut);
        await result.CanSearchForReplacement.Should().BeTrue();
    }

    private sealed class ProbeHandler(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(cancellationToken);
    }
}

public class V2pnAvailabilityProbeConfigTests
{
    private static readonly AvailabilityProbeEndpoint Probe = new(43210, "secret");

    [Test]
    public async Task XrayRoutesTheProbeInboundAheadOfTheRules()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.Xray) with { AvailabilityProbe = Probe };

        var result = new CoreConfigV2rayService(context).GenerateClientConfigContent();

        await result.Success.Should().BeTrue();
        var cfg = JsonUtils.Deserialize<V2rayConfig>(result.Data!.ToString())!;
        var inbound = cfg.inbounds.Single(i => i.tag == AvailabilityProbeEndpoint.Tag);
        await inbound.listen.Should().BeEqualTo(Global.Loopback);
        await inbound.port.Should().BeEqualTo(Probe.Port);
        await inbound.settings!.auth.Should().BeEqualTo("password");
        await inbound.settings.accounts!.Single().pass.Should().BeEqualTo(Probe.Password);
        var first = cfg.routing.rules.First();
        await first.inboundTag!.Single().Should().BeEqualTo(AvailabilityProbeEndpoint.Tag);
        await first.outboundTag.Should().BeEqualTo(Global.ProxyTag);
    }

    [Test]
    public async Task SingboxRoutesTheProbeInboundAheadOfTheRules()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.sing_box);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateSocksNode(ECoreType.sing_box);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.sing_box) with { AvailabilityProbe = Probe };

        var result = new CoreConfigSingboxService(context).GenerateClientConfigContent();

        await result.Success.Should().BeTrue().Because($"ret msg: {result.Msg}");
        var cfg = JsonUtils.Deserialize<SingboxConfig>(result.Data!.ToString())!;
        var inbound = cfg.inbounds.Single(i => i.tag == AvailabilityProbeEndpoint.Tag);
        await inbound.listen.Should().BeEqualTo(Global.Loopback);
        await inbound.listen_port.Should().BeEqualTo(Probe.Port);
        await inbound.users.Single().password.Should().BeEqualTo(Probe.Password);
        var first = cfg.route.rules.First();
        await first.inbound!.Single().Should().BeEqualTo(AvailabilityProbeEndpoint.Tag);
        await first.outbound.Should().BeEqualTo(Global.ProxyTag);
    }

    [Test]
    public async Task NoProbeLeavesTheConfigWithoutTheExtraInbound()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.Xray);

        var result = new CoreConfigV2rayService(context).GenerateClientConfigContent();

        await result.Data!.ToString().Should().NotContain(AvailabilityProbeEndpoint.Tag);
    }
}
