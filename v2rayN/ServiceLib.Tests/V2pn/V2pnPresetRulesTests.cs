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
}
