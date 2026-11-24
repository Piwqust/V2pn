using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

/// <summary>The rules of the three routing presets and the person's own sites on top of them.</summary>
public class V2pnPresetRulesTests
{
    private static string Describe(List<RulesItem> rules) => string.Join(" | ", rules.Select(r =>
        $"{r.OutboundTag}:{string.Join(",", (r.Domain ?? []).Concat(r.Ip ?? []).Concat(r.Protocol ?? []))}{r.Port}"));

    [Test]
    public async Task RussiaDirect_BlockedRuSitesGoThroughTheServer_BeforeTheRussianRules()
    {
        var rules = V2pnPresetRules.Rules(EV2pnPreset.RussiaDirect, blockAds: false);
        var text = Describe(rules);

        await (text.IndexOf("proxy:geosite:ru-blocked", StringComparison.Ordinal) < text.IndexOf("direct:geosite:ru-available-only-inside", StringComparison.Ordinal)).Should().BeTrue();
        await rules[^1].OutboundTag.Should().BeEqualTo(Global.ProxyTag);
        await rules[^1].Port.Should().BeEqualTo("0-65535");
    }

    [Test]
    public async Task BlockedOnly_EndsDirect_All_EndsThroughTheServer()
    {
        await V2pnPresetRules.Rules(EV2pnPreset.BlockedOnly, false)[^1].OutboundTag.Should().BeEqualTo(Global.DirectTag);
        await V2pnPresetRules.Rules(EV2pnPreset.All, false)[^1].OutboundTag.Should().BeEqualTo(Global.ProxyTag);
    }

    [Test]
    public async Task BlockingAds_ComesBeforeThePreset()
    {
        var rules = V2pnPresetRules.Rules(EV2pnPreset.All, blockAds: true);

        await rules[0].OutboundTag.Should().BeEqualTo(Global.BlockTag);
        await rules[0].Domain!.Single().Should().BeEqualTo("geosite:category-ads-all");
    }

    [Test]
    public async Task TheSites_ComeFirst_DirectThenThroughTheServer()
    {
        List<V2pnSiteRule> sites =
        [
            new() { Domain = "sberbank.ru", Direct = false, Subdomains = true },
            new() { Domain = "example.com", Direct = true, Subdomains = false },
        ];

        var rules = V2pnPresetRules.Rules(EV2pnPreset.RussiaDirect, blockAds: true, sites);

        await rules[0].OutboundTag.Should().BeEqualTo(Global.DirectTag);
        await rules[0].Domain!.Single().Should().BeEqualTo("full:example.com");
        await rules[1].OutboundTag.Should().BeEqualTo(Global.ProxyTag);
        await rules[1].Domain!.Single().Should().BeEqualTo("domain:sberbank.ru");
        await rules[2].OutboundTag.Should().BeEqualTo(Global.BlockTag);
    }

    [Test]
    public async Task ASiteThatIsNotAValidDomain_IsLeftOut()
    {
        List<V2pnSiteRule> sites = [new() { Domain = "Not A Domain", Direct = true }];

        await V2pnPresetRules.Rules(EV2pnPreset.All, false, sites).Any(r => r.Domain?.Any(d => d.Contains("Not", StringComparison.Ordinal)) == true).Should().BeFalse();
    }

    [Test]
    [Arguments("https://www.Example.com/path?q=1", "www.example.com")]
    [Arguments("example.com:8443", "example.com")]
    [Arguments(".example.com.", "example.com")]
    [Arguments("госуслуги.рф", "xn--c1aapkosapc.xn--p1ai")]
    [Arguments("ru", null)]
    [Arguments("192.168.1.1", null)]
    [Arguments("exa mple.com", null)]
    [Arguments("", null)]
    public async Task NormalizeDomain_TakesWhatPeopleType(string input, string? expected)
    {
        await V2pnPresetRules.NormalizeDomain(input).Should().BeEqualTo(expected);
    }

    [Test]
    public async Task With_ReplacesAnEarlierChoiceForTheSameSite()
    {
        List<V2pnSiteRule> sites = [new() { Domain = "example.com", Direct = true }];

        var changed = V2pnPresetRules.With(sites, new V2pnSiteRule { Domain = "example.com", Direct = false });

        await changed.Count.Should().BeEqualTo(1);
        await changed[0].Direct.Should().BeFalse();
    }

    // Programs that always go directly (VPN mode only).

    private static readonly List<V2pnSiteRule> ASite = [new() { Domain = "example.com", Direct = false, Subdomains = true }];

    private static readonly List<V2pnAppRule> AnApp = [new() { Process = "SberBank.exe" }];

    [Test]
    public async Task ThePrograms_ComeFirst_ThenSites_ThenAds_ThenThePreset()
    {
        var rules = V2pnPresetRules.Rules(EV2pnPreset.RussiaDirect, blockAds: true, ASite, AnApp);

        await V2pnPresetRules.IsAppRule(rules[0]).Should().BeTrue();
        await rules[0].OutboundTag.Should().BeEqualTo(Global.DirectTag);
        await rules[0].Process!.Should().Contain("SberBank.exe");
        await rules[0].Domain.Should().BeNull();
        await rules[1].Domain!.Single().Should().BeEqualTo("domain:example.com");
        await rules[1].OutboundTag.Should().BeEqualTo(Global.ProxyTag);
        await rules[2].OutboundTag.Should().BeEqualTo(Global.BlockTag);
        await rules.Skip(1).Any(r => r.Process != null).Should().BeFalse();
    }

    [Test]
    public async Task NoPrograms_NoProcessRule()
    {
        await V2pnPresetRules.Rules(EV2pnPreset.All, false, ASite).Any(r => r.Process != null).Should().BeFalse();
        await V2pnPresetRules.Rules(EV2pnPreset.All, false, ASite, [new() { Process = "  " }]).Any(r => r.Process != null).Should().BeFalse();
    }

    [Test]
    public async Task OneProgramWrittenTwice_IsOneProgram_InTheFileCaseAndTheCommonOnes()
    {
        List<V2pnAppRule> apps = [new() { Process = "SberBank.exe" }, new() { Process = "sberbank.EXE" }, new() { Process = "steam.exe" }];

        var process = V2pnPresetRules.Rules(EV2pnPreset.All, false, null, apps)[0].Process!;

        await process.Should().Contain("SberBank.exe");
        await process.Should().Contain("sberbank.exe");
        await process.Should().Contain("Sberbank.exe");
        await process.Should().Contain("steam.exe");
        await process.Should().Contain("Steam.exe");
        await process.Should().NotContain("sberbank.EXE");
        await process.Distinct(StringComparer.Ordinal).Count().Should().BeEqualTo(process.Count);
    }

    [Test]
    [Arguments("SberBank.exe", "SberBank.exe")]
    [Arguments("  steam.exe ", "steam.exe")]
    [Arguments("C:\\Program Files\\Sber\\SberBank.exe", "SberBank.exe")]
    [Arguments("\"C:\\Games\\Some Game\\game.exe\"", "game.exe")]
    [Arguments("C:/Tools/app.exe", "app.exe")]
    [Arguments("/Applications/Telegram.app", "Telegram")]
    [Arguments("/Applications/Telegram.app/", "Telegram")]
    [Arguments("/Applications/Sber Online.app/Contents/MacOS/Sber Online", "Sber Online")]
    [Arguments("", null)]
    [Arguments("   ", null)]
    [Arguments("C:\\", null)]
    [Arguments("a|b.exe", null)]
    [Arguments("what?.exe", null)]
    [Arguments("# off", null)]
    [Arguments("..", null)]
    public async Task NormalizeProgram_TakesAFileNameOrAPath(string input, string? expected)
    {
        await V2pnPresetRules.NormalizeProgram(input).Should().BeEqualTo(expected);
    }

    [Test]
    public async Task ProgramKey_IgnoresCaseAndTheExeEnding()
    {
        await V2pnPresetRules.ProgramKey("SberBank.exe").Should().BeEqualTo("sberbank");
        await V2pnPresetRules.ProgramKey("sberbank.EXE").Should().BeEqualTo("sberbank");
        await V2pnPresetRules.ProgramKey("Telegram").Should().BeEqualTo("telegram");
    }

    [Test]
    public async Task WithAnApp_ReplacesTheSameProgramWrittenInAnotherCase_AndKeepsTheLimit()
    {
        var apps = V2pnPresetRules.With([new() { Process = "sberbank.exe" }, new() { Process = "steam.exe" }], new V2pnAppRule { Process = "SberBank.exe" });

        await apps.Select(a => a.Process).Should().BeEquivalentTo(["steam.exe", "SberBank.exe"]);

        var many = Enumerable.Range(0, V2pnPresetRules.MaxApps + 5).Aggregate(new List<V2pnAppRule>(),
            (list, i) => V2pnPresetRules.With(list, new V2pnAppRule { Process = $"app{i}.exe" }));
        await many.Count.Should().BeEqualTo(V2pnPresetRules.MaxApps);
        await many[^1].Process.Should().BeEqualTo($"app{V2pnPresetRules.MaxApps + 4}.exe");
    }

    [Test]
    public async Task IsAppRule_NeedsTheMarkAndAProgram()
    {
        await V2pnPresetRules.IsAppRule(new RulesItem { Remarks = V2pnPresetRules.AppsRemarks, Process = ["x.exe"], OutboundTag = Global.DirectTag }).Should().BeTrue();
        await V2pnPresetRules.IsAppRule(new RulesItem { Remarks = "mine", Process = ["x.exe"], OutboundTag = Global.DirectTag }).Should().BeFalse();
        await V2pnPresetRules.IsAppRule(new RulesItem { Remarks = V2pnPresetRules.AppsRemarks, OutboundTag = Global.DirectTag }).Should().BeFalse();
    }

    // The core configs: the programs' rule reaches only the core that runs the tunnel.

    private static CoreConfigContext PresetContext(ECoreType coreType, bool tun, ProfileItem? node = null)
    {
        var config = CoreConfigTestFactory.CreateConfig(coreType);
        config.TunModeItem.EnableTun = tun;
        CoreConfigTestFactory.BindAppManagerConfig(config);
        node ??= CoreConfigTestFactory.CreateVmessNode(coreType);
        var context = CoreConfigTestFactory.CreateContext(config, node, coreType);
        context.RoutingItem!.RuleSet = JsonUtils.Serialize(
            V2pnPresetRules.Rules(EV2pnPreset.BlockedOnly, blockAds: true, ASite, AnApp), false);
        context.RoutingItem.DomainStrategy = Global.IPOnDemand;
        return context;
    }

    private static List<Rule4Sbox> SingboxRules(CoreConfigContext context)
    {
        var result = new CoreConfigSingboxService(context).GenerateClientConfigContent();
        if (result.Success != true)
        {
            throw new InvalidOperationException(result.Msg);
        }
        return JsonUtils.Deserialize<SingboxConfig>(result.Data!.ToString())!.route.rules;
    }

    private static List<RulesItem4Ray> XrayRules(CoreConfigContext context)
    {
        var result = new CoreConfigV2rayService(context).GenerateClientConfigContent();
        if (result.Success != true)
        {
            throw new InvalidOperationException(result.Msg);
        }
        return JsonUtils.Deserialize<V2rayConfig>(result.Data!.ToString())!.routing.rules;
    }

    [Test]
    public async Task Singbox_VpnMode_SendsTheProgramsDirectly_BeforeTheSitesAndThePreset()
    {
        var rules = SingboxRules(PresetContext(ECoreType.sing_box, tun: true));

        var app = rules.FindIndex(r => r.process_name?.Contains("SberBank.exe") == true);
        var site = rules.FindIndex(r => r.domain_suffix?.Contains("example.com") == true);
        var dnsServers = rules.FindIndex(r => r.ip_cidr?.Contains("1.1.1.1/32") == true || r.ip_cidr?.Contains("1.1.1.1") == true);
        var catchAll = rules.FindLastIndex(r => r.outbound == Global.DirectTag && r.port_range?.Contains("0:65535") == true);

        await app.Should().BeGreaterThanOrEqualTo(0);
        await rules[app].outbound.Should().BeEqualTo(Global.DirectTag);
        await rules[app].process_name!.Should().Contain("sberbank.exe");
        await site.Should().BeGreaterThan(app);
        await dnsServers.Should().BeGreaterThan(site);
        await catchAll.Should().BeGreaterThan(dnsServers);
    }

    [Test]
    public async Task Singbox_ProxyMode_LeavesTheProgramsAlone_AndKeepsTheSites()
    {
        var rules = SingboxRules(PresetContext(ECoreType.sing_box, tun: false));

        await rules.Any(r => r.process_name?.Contains("SberBank.exe") == true).Should().BeFalse();
        await rules.Any(r => r.domain_suffix?.Contains("example.com") == true).Should().BeTrue();
    }

    [Test]
    public async Task Xray_VpnModeOnItsOwnTunnel_SendsTheProgramsDirectly_BeforeTheSitesAndThePreset()
    {
        var rules = XrayRules(PresetContext(ECoreType.Xray, tun: true));

        var app = rules.FindIndex(r => r.process?.Contains("SberBank.exe") == true);
        var site = rules.FindIndex(r => r.domain?.Contains("domain:example.com") == true);
        var catchAll = rules.FindLastIndex(r => r.outboundTag == Global.DirectTag && r.port == "0-65535");

        await app.Should().BeGreaterThanOrEqualTo(0);
        await rules[app].outboundTag.Should().BeEqualTo(Global.DirectTag);
        await rules[app].domain.Should().BeNull();
        await site.Should().BeGreaterThan(app);
        await catchAll.Should().BeGreaterThan(site);
    }

    [Test]
    public async Task Xray_ProxyMode_LeavesTheProgramsAlone_AndKeepsTheSites()
    {
        var rules = XrayRules(PresetContext(ECoreType.Xray, tun: false));

        await rules.Any(r => r.process?.Contains("SberBank.exe") == true).Should().BeFalse();
        await rules.Any(r => r.domain?.Contains("domain:example.com") == true).Should().BeTrue();
    }

    [Test]
    public async Task XrayBehindSingboxTunnel_LeavesTheProgramsToSingbox()
    {
        // VPN mode with an Xray server and "Legacy TUN protection" on (the default): sing-box runs the tunnel
        // from configPre.json and hands the server's traffic to Xray's local port; CoreConfigContextBuilder.BuildAll
        // turns IsTunEnabled off for Xray. Only sing-box can tell which program a connection comes from there.
        var xray = PresetContext(ECoreType.Xray, tun: true) with { IsTunEnabled = false };
        var preSocks = new ProfileItem
        {
            IndexId = "pre-socks",
            CoreType = ECoreType.sing_box,
            ConfigType = EConfigType.SOCKS,
            Address = Global.Loopback,
            Port = 10808,
        };
        var singbox = PresetContext(ECoreType.sing_box, tun: true, preSocks);

        await XrayRules(xray).Any(r => r.process?.Contains("SberBank.exe") == true).Should().BeFalse();
        var rules = SingboxRules(singbox);
        await rules.Should().Contain(r => r.process_name != null && r.process_name.Contains("SberBank.exe") && r.outbound == Global.DirectTag);
    }
}
