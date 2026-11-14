using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

public class V2pnPinsTests
{
    [Test]
    public async Task ProgramNames_BareName_AddsLowerAndCapitalizedCase()
    {
        var names = V2pnPins.ProgramNames(["claude.exe", " Telegram.exe ", "", "# off", "\"C:\\Tools\\app.exe\""]);

        await names.Should().Contain("claude.exe");
        await names.Should().Contain("Claude.exe");
        await names.Should().Contain("Telegram.exe");
        await names.Should().Contain("telegram.exe");
        await names.Should().Contain("C:/Tools/app.exe");
        await names.Should().NotContain("# off");
        await names.Should().NotContain(string.Empty);
        await names.Distinct(StringComparer.Ordinal).Count().Should().BeEqualTo(names.Count);
    }

    [Test]
    public async Task SiteNames_BareName_CoversSubdomains_PrefixKept()
    {
        var names = V2pnPins.SiteNames(["claude.ai", ".anthropic.com", "geosite:youtube", "full:x.example", "#note", "Claude.ai"]);

        await names.Should().BeEquivalentTo(["domain:claude.ai", "domain:anthropic.com", "geosite:youtube", "full:x.example"]);
    }

    [Test]
    public async Task IsUsable_NeedsServerAndSomethingToMatch()
    {
        await V2pnPins.IsUsable(new V2pnPin { ServerId = "g", Programs = ["claude.exe"] }).Should().BeTrue();
        await V2pnPins.IsUsable(new V2pnPin { ServerName = "g", Sites = ["claude.ai"] }).Should().BeTrue();
        await V2pnPins.IsUsable(new V2pnPin { Programs = ["claude.exe"] }).Should().BeFalse();
        await V2pnPins.IsUsable(new V2pnPin { ServerId = "g", Programs = ["# off"] }).Should().BeFalse();
        await V2pnPins.IsUsable(new V2pnPin { ServerId = "g", Programs = ["claude.exe"], Enabled = false }).Should().BeFalse();
    }

    [Test]
    public async Task WithRulesFirst_PutsPinsAheadAndLeavesTheSetAlone()
    {
        var routing = new RoutingItem
        {
            Id = "r1",
            Remarks = "set",
            RuleSet = JsonUtils.Serialize(new List<RulesItem> { new() { Id = "a", OutboundTag = Global.DirectTag, Port = "0-65535" } }, false),
            RuleNum = 1,
            DomainStrategy = Global.IPOnDemand,
        };
        var pin = new RulesItem { Id = "p", OutboundTag = V2pnPins.TagPrefix + "g", Process = ["claude.exe"] };

        var merged = V2pnPins.WithRulesFirst(routing, [pin]);
        var rules = JsonUtils.Deserialize<List<RulesItem>>(merged.RuleSet)!;

        await rules.Select(r => r.Id).Should().BeEquivalentTo(["p", "a"]);
        await merged.RuleNum.Should().BeEqualTo(2);
        await merged.DomainStrategy.Should().BeEqualTo(Global.IPOnDemand);
        await JsonUtils.Deserialize<List<RulesItem>>(routing.RuleSet)!.Count.Should().BeEqualTo(1);
    }

    [Test]
    public async Task ApplyAsync_Xray_RoutesPinnedProgramsToTheGroupBeforeTheSet()
    {
        var (config, group, context) = await GroupContextAsync(ECoreType.Xray);

        var result = NodeValidatorResult.Empty();
        context = await V2pnPins.ApplyAsync(context, result);
        var generated = new CoreConfigV2rayService(context).GenerateClientConfigContent();

        await result.Warnings.Should().BeEmpty();
        await generated.Success.Should().BeTrue();
        var cfg = JsonUtils.Deserialize<V2rayConfig>(generated.Data!.ToString())!;
        var rules = cfg.routing.rules;
        var processRule = rules.FindIndex(r => r.process?.Contains("claude.exe") == true);
        var siteRule = rules.FindIndex(r => r.domain?.Contains("domain:claude.ai") == true);
        var catchAll = rules.FindIndex(r => r.port == "0-65535" && r.outboundTag == Global.DirectTag);

        await processRule.Should().BeGreaterThanOrEqualTo(0);
        await rules[processRule].process!.Should().Contain("Claude.exe");
        await rules[processRule].balancerTag!.Should().StartWith(group.IndexId);
        await siteRule.Should().BeGreaterThanOrEqualTo(0);
        await rules[siteRule].balancerTag!.Should().StartWith(group.IndexId);
        await processRule.Should().BeLessThan(catchAll);
        await cfg.routing.balancers!.Should().Contain(b => b.tag == rules[processRule].balancerTag);
    }

    [Test]
    public async Task ApplyAsync_Singbox_RoutesPinnedProgramsToTheGroup()
    {
        var (_, group, context) = await GroupContextAsync(ECoreType.sing_box);

        var result = NodeValidatorResult.Empty();
        context = await V2pnPins.ApplyAsync(context, result);
        var generated = new CoreConfigSingboxService(context).GenerateClientConfigContent();

        await result.Warnings.Should().BeEmpty();
        await generated.Success.Should().BeTrue();
        var cfg = JsonUtils.Deserialize<SingboxConfig>(generated.Data!.ToString())!;
        var rule = cfg.route.rules.FirstOrDefault(r => r.process_name?.Contains("claude.exe") == true);

        await rule.Should().NotBeNull();
        await rule!.process_name!.Should().Contain("Claude.exe");
        await rule.outbound!.Should().StartWith(group.IndexId);
        await cfg.outbounds.Should().Contain(o => o.tag == rule.outbound);
    }

    [Test]
    public async Task ApplyAsync_ServerGone_WarnsAndLeavesTheSetAsIs()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var pin = V2pnPins.NewClaude();
        pin.ServerId = NewId("gone");
        pin.ServerName = NewId("gone-name");
        config.RoutingBasicItem.V2pnPins = [pin];
        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.Xray);
        var before = context.RoutingItem!.RuleSet;

        var result = NodeValidatorResult.Empty();
        var applied = await V2pnPins.ApplyAsync(context, result);

        await result.Warnings.Should().HaveCount(1);
        await result.Warnings[0].Should().Contain(pin.ServerName);
        await applied.RoutingItem!.RuleSet.Should().BeEqualTo(before);
    }

    [Test]
    public async Task ApplyAsync_IdChanged_FindsTheServerByName()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var server = CoreConfigTestFactory.CreateSocksNode(ECoreType.Xray, NewId("srv"), NewId("srv-name"));
        await UpsertProfilesAsync(server);
        var pin = V2pnPins.NewClaude();
        pin.ServerId = NewId("old-id");
        pin.ServerName = server.Remarks;
        config.RoutingBasicItem.V2pnPins = [pin];
        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.Xray);

        var result = NodeValidatorResult.Empty();
        context = await V2pnPins.ApplyAsync(context, result);
        var cfg = JsonUtils.Deserialize<V2rayConfig>(new CoreConfigV2rayService(context).GenerateClientConfigContent().Data!.ToString())!;

        await result.Warnings.Should().BeEmpty();
        await cfg.routing.rules.Should().Contain(r => r.process != null && r.process.Contains("claude.exe")
                                                      && r.outboundTag != null && r.outboundTag.StartsWith(server.IndexId));
    }

    private static async Task<(Config, ProfileItem, CoreConfigContext)> GroupContextAsync(ECoreType coreType)
    {
        var config = CoreConfigTestFactory.CreateConfig(coreType);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var n1 = CoreConfigTestFactory.CreateSocksNode(coreType, NewId("n1"), "pool-1");
        var n2 = CoreConfigTestFactory.CreateSocksNode(coreType, NewId("n2"), "pool-2");
        var group = CoreConfigTestFactory.CreatePolicyGroupNode(coreType, NewId("pool"), NewId("Claude pool"), [n1.IndexId, n2.IndexId]);
        await UpsertProfilesAsync(n1, n2, group);

        var pin = V2pnPins.NewClaude();
        pin.ServerId = group.IndexId;
        pin.ServerName = group.Remarks;
        config.RoutingBasicItem.V2pnPins = [pin];

        var main = CoreConfigTestFactory.CreateVmessNode(coreType);
        var context = CoreConfigTestFactory.CreateContext(config, main, coreType);
        context.RoutingItem!.RuleSet = JsonUtils.Serialize(new List<RulesItem>
        {
            new() { Id = "direct-all", OutboundTag = Global.DirectTag, Port = "0-65535" },
        }, false);
        return (config, group, context);
    }

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static async Task UpsertProfilesAsync(params ProfileItem[] profiles)
    {
        SQLiteHelper.Instance.CreateTable<ProfileItem>();
        foreach (var profile in profiles)
        {
            await SQLiteHelper.Instance.ReplaceAsync(profile);
        }
    }
}
