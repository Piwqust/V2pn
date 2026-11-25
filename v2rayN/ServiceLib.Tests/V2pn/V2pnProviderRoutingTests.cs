using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

/// <summary>The provider's rules, chosen for a subscription, take the place of the person's rule set.</summary>
public class V2pnProviderRoutingTests
{
    private const string Rules = """
        { "domainStrategy": "IPIfNonMatch", "rules": [
          { "type": "field", "domain": [ "geosite:category-ru", "geosite:whitelist", "domain:example.ru" ], "outboundTag": "direct" },
          { "type": "field", "domain": [ "geosite:whitelist" ], "outboundTag": "direct" },
          { "type": "field", "ip": [ "geoip:ru", "10.0.0.0/8" ], "outboundTag": "direct" },
          { "type": "field", "protocol": [ "bittorrent" ], "outboundTag": "block" },
          { "type": "field", "domain": [ "domain:other" ], "outboundTag": "somewhere" }
        ] }
        """;

    private static bool Has(string kind, string name) => name is "category-ru" or "ru";

    [Test]
    public async Task Parse_LeavesOutListsTheCoreHasNot()
    {
        var routing = V2pnProviderRouting.Parse(Rules, Has)!;

        await routing.DomainStrategy.Should().BeEqualTo("IPIfNonMatch");
        // The rule with only an unknown list goes whole; the one naming another outbound too.
        await routing.Rules.Count.Should().BeEqualTo(3);
        await routing.Rules[0].domain.Should().BeEquivalentTo(["geosite:category-ru", "domain:example.ru"]);
        await routing.Rules[1].ip.Should().BeEquivalentTo(["geoip:ru", "10.0.0.0/8"]);
        await routing.Rules[2].outboundTag.Should().BeEqualTo("block");
    }

    [Test]
    public async Task ProviderRules_ReplaceThePersonsRuleSet_ButNotTheirPins()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray);
        var own = new List<RulesItem>
        {
            new() { Id = "v2pn-pin-1", Enabled = true, Domain = ["pinned.example"], OutboundTag = Global.DirectTag },
            new() { Id = "mine", Enabled = true, Domain = ["mine.example"], OutboundTag = Global.BlockTag },
        };
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.Xray) with
        {
            RoutingItem = new RoutingItem { Id = "r", Remarks = "r", RuleSet = JsonUtils.Serialize(own), DomainStrategy = Global.AsIs },
            V2pnProviderRouting = V2pnProviderRouting.Parse(Rules, Has),
        };

        var generated = JsonNode.Parse(new CoreConfigV2rayService(context).GenerateClientConfigContent().Data!.ToString()!)!;
        var rules = generated["routing"]!["rules"]!.ToJsonString();

        await rules.Should().Contain("pinned.example");
        await rules.Should().NotContain("mine.example");
        await rules.Should().Contain("geosite:category-ru");
        await rules.Should().Contain("bittorrent");
        await generated["routing"]!["domainStrategy"]!.ToString().Should().BeEqualTo("IPIfNonMatch");
    }

    [Test]
    public async Task ByDefault_ThePersonsRuleSetStays()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray);
        node.V2pnRules = Rules;
        var own = new List<RulesItem> { new() { Id = "mine", Enabled = true, Domain = ["mine.example"], OutboundTag = Global.BlockTag } };
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.Xray) with
        {
            RoutingItem = new RoutingItem { Id = "r", Remarks = "r", RuleSet = JsonUtils.Serialize(own), DomainStrategy = Global.AsIs },
            V2pnProviderRouting = V2pnProviderRouting.For(node),
        };

        var rules = JsonNode.Parse(new CoreConfigV2rayService(context).GenerateClientConfigContent().Data!.ToString()!)!["routing"]!["rules"]!.ToJsonString();

        await rules.Should().Contain("mine.example");
        await rules.Should().NotContain("bittorrent");
    }

    [Test]
    public async Task GeoTags_ReadsTheListNames()
    {
        static byte[] Entry(string name, int extra)
        {
            var nameBytes = Encoding.UTF8.GetBytes(name);
            var body = new List<byte> { 0x0a, (byte)nameBytes.Length };
            body.AddRange(nameBytes);
            // A second field of the entry (the domains or addresses), skipped.
            body.AddRange([0x12, (byte)extra]);
            body.AddRange(new byte[extra]);
            return [0x0a, (byte)body.Count, .. body];
        }

        byte[] data = [.. Entry("CATEGORY-RU", 5), .. Entry("private", 0), .. Entry("ru", 30)];

        var names = V2pnGeoTags.Read(data);

        await names.Should().HaveCount(3);
        await names.Contains("category-ru").Should().BeTrue();
        await names.Contains("whitelist").Should().BeFalse();
    }
}
