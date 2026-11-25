using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

/// <summary>A server taken out of a provider's whole config runs on Xray with the provider's own outbound.</summary>
public class V2pnProviderOutboundTests
{
    private static ProfileItem Server()
    {
        var server = V2pnProviderConfigs.Unpack(V2pnProviderConfigsTests.Answer)!.Servers[0];
        var item = FmtHandler.ResolveConfig(server.Link, out _)!;
        item.IndexId = "nl2";
        item.CoreType = ECoreType.Xray;
        item.V2pnOutbound = server.Outbound;
        return item;
    }

    private static async Task<JsonObject> Proxy(ProfileItem node, V2pnBypassPlan? plan = null, ECoreType core = ECoreType.Xray)
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var context = CoreConfigTestFactory.CreateContext(config, node, core) with { V2pnBypass = plan ?? V2pnBypassPlan.Off };
        await CoreConfigContextBuilder.ResolveNodeAsync(context, node, true);
        var result = new CoreConfigV2rayService(context).GenerateClientConfigContent();
        var generated = (JsonObject)JsonNode.Parse(result.Data!.ToString()!)!;
        return (JsonObject)generated["outbounds"]!.AsArray().First(o => o?["tag"]?.GetValue<string>() == Global.ProxyTag)!;
    }

    [Test]
    public async Task TheProvidersOutbound_RunsWithWhatNoLinkHolds()
    {
        var proxy = await Proxy(Server());

        await proxy["streamSettings"]!["wsSettings"]!["headers"]!["X-Version"]!.ToString().Should().BeEqualTo("1.9.1");
        await proxy["settings"]!["vnext"]![0]!["address"]!.ToString().Should().BeEqualTo("nl.example.net");
    }

    [Test]
    public async Task WithoutIt_V2pnBuildsTheOutbound()
    {
        var node = Server();
        node.V2pnOutbound = null;

        var proxy = await Proxy(node);

        await (proxy["streamSettings"]!["wsSettings"]!["headers"]?["X-Version"]).Should().BeNull();
        await proxy["streamSettings"]!["wsSettings"]!["path"]!.ToString().Should().BeEqualTo("/websocket");
    }

    [Test]
    public async Task AWayTheBypassFound_KeepsV2pnsOutbound()
    {
        var plan = new V2pnBypassPlan(true, false, new Dictionary<string, V2pnBypassVariant> { ["nl2"] = V2pnBypass.FragmentTcp });

        var proxy = await Proxy(Server(), plan);

        await (proxy["streamSettings"]!["wsSettings"]!["headers"]?["X-Version"]).Should().BeNull();
    }

    [Test]
    public async Task CalmBypass_FillsOnlyWhatTheProviderLeftOut()
    {
        var plan = new V2pnBypassPlan(true, false, new Dictionary<string, V2pnBypassVariant>());

        var proxy = await Proxy(Server(), plan);

        await proxy["streamSettings"]!["wsSettings"]!["headers"]!["X-Version"]!.ToString().Should().BeEqualTo("1.9.1");
        await proxy["streamSettings"]!["tlsSettings"]!["fingerprint"]!.ToString().Should().BeEqualTo("firefox");
    }

    [Test]
    public async Task CalmProviderOutbound_AddsFingerprintAndXmux()
    {
        const string xhttp = """{ "protocol": "vless", "streamSettings": { "network": "xhttp", "xhttpSettings": { "path": "/" }, "security": "reality", "realitySettings": { "publicKey": "k" } } }""";

        var calm = JsonNode.Parse(CoreConfigV2rayService.CalmProviderOutbound(xhttp))!;

        await calm["streamSettings"]!["realitySettings"]!["fingerprint"]!.ToString().Should().BeEqualTo(CoreConfigV2rayService.CalmFingerprint);
        await calm["streamSettings"]!["xhttpSettings"]!["extra"]!["xmux"].Should().NotBeNull();
    }
}
