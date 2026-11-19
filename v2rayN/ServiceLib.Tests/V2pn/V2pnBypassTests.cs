using ServiceLib.Services.CoreConfig;
using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

[NotInParallel(nameof(V2pnBypass))]
public class V2pnBypassTests
{
    private static ProfileItem Vless(string id, string security, string network = nameof(ETransport.raw), string fingerprint = "chrome",
        ECoreType core = ECoreType.Xray)
    {
        var node = new ProfileItem
        {
            IndexId = id,
            ConfigType = EConfigType.VLESS,
            CoreType = core,
            Remarks = id,
            Address = "example.com",
            Port = 443,
            Password = "01234567-89ab-cdef-0123-456789abcdef",
            Network = network,
            StreamSecurity = security,
            PublicKey = "test-public-key",
            Sni = "example.com",
            Fingerprint = fingerprint,
        };
        node.SetProtocolExtra(new ProtocolExtraItem { VlessEncryption = Global.None });
        node.SetTransportExtra(new TransportExtraItem { RawHeaderType = Global.None, Path = "/x" });
        return node;
    }

    private static Config On(Action<V2pnBypassItem>? set = null)
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        config.V2pnBypassItem = new V2pnBypassItem { Enabled = true };
        set?.Invoke(config.V2pnBypassItem);
        return config;
    }

    private static JsonObject XrayConfig(Config config, ProfileItem node, V2pnBypassPlan plan)
    {
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.Xray) with { V2pnBypass = plan };
        var result = new CoreConfigV2rayService(context).GenerateClientConfigContent();
        return (JsonObject)JsonNode.Parse(result.Data!.ToString()!)!;
    }

    private static JsonObject Outbound(JsonObject config, string tag) =>
        (JsonObject)config["outbounds"]!.AsArray().First(o => o?["tag"]?.GetValue<string>() == tag)!;

    private static Dictionary<string, V2pnBypassVariant> Ways(string id, V2pnBypassVariant variant) => new() { [id] = variant };

    [Test]
    public async Task Candidates_DependOnTheServer()
    {
        await V2pnBypass.CandidatesFor(Vless("r", Global.StreamSecurityReality), ECoreType.Xray).Select(v => v.Id).ToList()
            .Should().BeEquivalentTo(["fragment-tcp", "fragment-fine"]);
        await V2pnBypass.CandidatesFor(Vless("t", Global.StreamSecurity), ECoreType.Xray).Count.Should().BeEqualTo(3);
        await V2pnBypass.CandidatesFor(Vless("t", Global.StreamSecurity), ECoreType.sing_box).Select(v => v.Id).ToList()
            .Should().BeEquivalentTo(["fragment"]);
        await V2pnBypass.CandidatesFor(Vless("n", Global.None), ECoreType.Xray).Count.Should().BeEqualTo(0);
        var hy2 = new ProfileItem { IndexId = "h", ConfigType = EConfigType.Hysteria2, StreamSecurity = Global.StreamSecurity };
        await V2pnBypass.CandidatesFor(hy2, ECoreType.Xray).Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task Plan_IsOffUntilTheSwitchIsOn()
    {
        var config = On();
        V2pnBypass.Remember(config, "s1", V2pnBypass.AnyNetwork, "fragment");
        config.V2pnBypassItem!.Enabled = false;

        await V2pnBypassPlan.From(config).IsOff.Should().BeTrue();
    }

    [Test]
    public async Task Plan_TakesTheWayOfThisNetworkOnly()
    {
        var config = On(b => b.PerNetwork = false);
        V2pnBypass.Remember(config, "s1", V2pnBypass.AnyNetwork, "fragment-fine");
        V2pnBypass.Remember(config, "s2", "other-network", "fragment");

        var plan = V2pnBypassPlan.From(config);

        await plan.For(new ProfileItem { IndexId = "s1" })!.Id.Should().BeEqualTo("fragment-fine");
        await plan.For(new ProfileItem { IndexId = "s2" }).Should().BeNull();
    }

    [Test]
    public async Task Plan_TrialWinsOverWhatIsKept()
    {
        var config = On(b => b.PerNetwork = false);
        V2pnBypass.Remember(config, "s1", V2pnBypass.AnyNetwork, "fragment");
        try
        {
            V2pnBypass.SetTrial("s1", "fragment-tcp");
            await V2pnBypassPlan.From(config).For(new ProfileItem { IndexId = "s1" })!.Id.Should().BeEqualTo("fragment-tcp");

            V2pnBypass.SetTrial("s1", string.Empty);
            await V2pnBypassPlan.From(config).For(new ProfileItem { IndexId = "s1" }).Should().BeNull();
        }
        finally
        {
            V2pnBypass.SetTrial(null, null);
        }
    }

    [Test]
    public async Task Remember_ReplacesAndKeepsTheNewest()
    {
        var config = On();
        V2pnBypass.Remember(config, "s1", "n1", "fragment", now: 1);
        V2pnBypass.Remember(config, "s1", "n1", "fragment-fine", now: 2);
        await config.V2pnBypassItem!.Learned!.Count.Should().BeEqualTo(1);
        await V2pnBypass.Learned(config, "s1", "n1")!.Id.Should().BeEqualTo("fragment-fine");

        for (var i = 0; i < V2pnBypass.MaxLearned + 5; i++)
        {
            V2pnBypass.Remember(config, $"x{i}", "n1", "fragment", now: 10 + i);
        }
        await config.V2pnBypassItem.Learned.Count.Should().BeEqualTo(V2pnBypass.MaxLearned);
        await V2pnBypass.Learned(config, "s1", "n1").Should().BeNull();

        await V2pnBypass.Forget(config, $"x{V2pnBypass.MaxLearned + 4}", "n1").Should().BeTrue();
    }

    [Test]
    public async Task Xray_LearnedWay_SplitsTheHandshake()
    {
        var node = Vless("r1", Global.StreamSecurityReality);
        var plan = new V2pnBypassPlan(false, false, Ways("r1", V2pnBypass.FragmentTcp));

        var proxy = Outbound(XrayConfig(On(), node, plan), Global.ProxyTag);

        var mask = proxy["streamSettings"]!["finalmask"]!["tcp"]![0]!;
        await mask["type"]!.GetValue<string>().Should().BeEqualTo("fragment");
        await mask["settings"]!["packets"]!.GetValue<string>().Should().BeEqualTo("1-3");
    }

    [Test]
    public async Task Xray_NoWay_NoFragment()
    {
        var node = Vless("r1", Global.StreamSecurityReality);
        var proxy = Outbound(XrayConfig(On(), node, new V2pnBypassPlan(true, false, new Dictionary<string, V2pnBypassVariant>())), Global.ProxyTag);

        await (proxy["streamSettings"]?["finalmask"]?["tcp"]).Should().BeNull();
    }

    [Test]
    public async Task Xray_Calm_FillsFingerprintAndXmux()
    {
        var node = Vless("x1", Global.StreamSecurity, nameof(ETransport.xhttp), fingerprint: string.Empty);
        var proxy = Outbound(XrayConfig(On(), node, new V2pnBypassPlan(true, false, new Dictionary<string, V2pnBypassVariant>())), Global.ProxyTag);

        var stream = proxy["streamSettings"]!;
        await stream["tlsSettings"]!["fingerprint"]!.GetValue<string>().Should().BeEqualTo("chrome");
        await stream["xhttpSettings"]!["extra"]!["xmux"]!["hMaxRequestTimes"]!.GetValue<string>().Should().BeEqualTo("600-900");
    }

    [Test]
    public async Task Xray_Calm_KeepsWhatTheLinkSays()
    {
        var node = Vless("x1", Global.StreamSecurity, nameof(ETransport.xhttp), fingerprint: "firefox");
        var transport = node.GetTransportExtra() with { XhttpExtra = """{"xmux":{"maxConcurrency":"4"}}""" };
        node.SetTransportExtra(transport);
        var proxy = Outbound(XrayConfig(On(), node, new V2pnBypassPlan(true, false, new Dictionary<string, V2pnBypassVariant>())), Global.ProxyTag);

        var stream = proxy["streamSettings"]!;
        await stream["tlsSettings"]!["fingerprint"]!.GetValue<string>().Should().BeEqualTo("firefox");
        await stream["xhttpSettings"]!["extra"]!["xmux"]!["maxConcurrency"]!.GetValue<string>().Should().BeEqualTo("4");
    }

    [Test]
    public async Task Xray_DirectFragment_SplitsDirectSites()
    {
        var node = Vless("t1", Global.StreamSecurity);
        var direct = Outbound(XrayConfig(On(), node, new V2pnBypassPlan(false, true, new Dictionary<string, V2pnBypassVariant>())), Global.DirectTag);

        await direct["streamSettings"]!["finalmask"]!["tcp"]![0]!["settings"]!["packets"]!.GetValue<string>().Should().BeEqualTo("tlshello");
    }

    [Test]
    public async Task Singbox_LearnedWayAndCalm()
    {
        var config = On();
        config.CoreTypeItem = [new CoreTypeItem { ConfigType = EConfigType.VLESS, CoreType = ECoreType.sing_box }];
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = Vless("s1", Global.StreamSecurity, fingerprint: string.Empty, core: ECoreType.sing_box);
        var plan = new V2pnBypassPlan(true, false, Ways("s1", V2pnBypass.Fragment));
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.sing_box) with { V2pnBypass = plan };

        var result = new CoreConfigSingboxService(context).GenerateClientConfigContent();
        var json = (JsonObject)JsonNode.Parse(result.Data!.ToString()!)!;
        var proxy = json["outbounds"]!.AsArray().First(o => o?["tag"]?.GetValue<string>() == Global.ProxyTag)!;

        await proxy["tls"]!["fragment"]!.GetValue<bool>().Should().BeTrue();
        await proxy["tls"]!["utls"]!["fingerprint"]!.GetValue<string>().Should().BeEqualTo("chrome");
    }

    [Test]
    public async Task NetworkId_KeysAndHints()
    {
        await V2pnNetworkId.Compose("wifi", "AA-BB-CC-DD-EE-FF", "192.168.1.1")
            .Should().BeEqualTo(V2pnNetworkId.Compose("wifi", "aa:bb:cc:dd:ee:ff", "10.0.0.1"));
        await V2pnNetworkId.Compose("wifi", "aa:bb:cc:dd:ee:ff", "192.168.1.1")
            .Should().NotBeEqualTo(V2pnNetworkId.Compose("wifi", "aa:bb:cc:dd:ee:00", "192.168.1.1"));
        await V2pnNetworkId.Compose("wifi", null, "192.168.1.1")
            .Should().NotBeEqualTo(V2pnNetworkId.Compose("wifi", null, "192.168.0.1"));
        await V2pnNetworkId.NormalizeMac("0:1b:2c:3d:4e:5f").Should().BeEqualTo("00:1b:2c:3d:4e:5f");
        await V2pnNetworkId.NormalizeMac("00:00:00:00:00:00").Should().BeEqualTo(string.Empty);
        await V2pnNetworkId.ParseArp("? (192.168.1.1) at 0:1b:2c:3d:4e:5f on en0 ifscope [ethernet]").Should().BeEqualTo("0:1b:2c:3d:4e:5f");
        await V2pnNetworkId.ParseArp("192.168.1.1 (192.168.1.1) -- no entry").Should().BeNull();
        await V2pnNetworkId.IsMobile("Intel Wi-Fi 6", NetworkInterfaceType.Wireless80211, "172.20.10.1").Should().BeTrue();
        await V2pnNetworkId.IsMobile("Remote NDIS based Internet Sharing Device", NetworkInterfaceType.Ethernet, "192.168.42.129").Should().BeTrue();
        await V2pnNetworkId.IsMobile("Intel Wi-Fi 6", NetworkInterfaceType.Wireless80211, "192.168.1.1").Should().BeFalse();
    }
}
