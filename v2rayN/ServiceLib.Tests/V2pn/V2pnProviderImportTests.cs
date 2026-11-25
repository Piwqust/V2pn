using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

/// <summary>A subscription that answers with whole Xray configs fills the list with servers and a group, not "Custom" profiles.</summary>
public class V2pnProviderImportTests
{
    private static async Task<string> NewSubscription()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        CoreConfigTestFactory.BindAppManagerConfig(config);
        TestDatabase.Initialize();
        var subId = $"sub-{Guid.NewGuid():N}";
        await SQLiteHelper.Instance.ReplaceAsync(new SubItem { Id = subId, Remarks = "provider", Url = "http://127.0.0.1/sub" });
        return subId;
    }

    [Test]
    public async Task WholeConfigs_BecomeServersAndAGroup()
    {
        var subId = await NewSubscription();

        var count = await ConfigDbHandler.AddBatchServers(V2pnProviderConfigsTests.Answer, subId, true);
        var profiles = await ConfigDbQuery.ProfileItems(subId) ?? [];

        await count.Should().BeEqualTo(4);
        await profiles.Should().HaveCount(4);
        await profiles.Any(p => p.ConfigType == EConfigType.Custom).Should().BeFalse();
        await profiles.Any(p => p.Remarks.Contains("Проверяйте")).Should().BeFalse();

        var servers = profiles.Where(p => p.ConfigType != EConfigType.PolicyGroup).ToList();
        await servers.Select(p => p.ConfigType).Order().ToList().Should().BeEquivalentTo([EConfigType.VLESS, EConfigType.VLESS, EConfigType.Hysteria2]);
        foreach (var server in servers)
        {
            await server.V2pnOutbound.Should().NotBeNull();
            await server.V2pnRules.Should().NotBeNull();
        }

        var group = profiles.Single(p => p.ConfigType == EConfigType.PolicyGroup);
        await group.Remarks.Should().BeEqualTo("🇪🇺 AUTO");
        await group.GetProtocolExtra().MultipleLoad.Should().BeEqualTo(EMultipleLoad.LeastLoad);
        var (children, _) = await GroupProfileManager.GetChildProfileItems(group);
        await children.Select(c => c.Remarks).Order().ToList().Should().BeEquivalentTo(servers.Select(s => s.Remarks).Order().ToList());
    }

    [Test]
    public async Task AnUpdate_KeepsTheRowsOfServersAndGroup()
    {
        var subId = await NewSubscription();

        await ConfigDbHandler.AddBatchServers(V2pnProviderConfigsTests.Answer, subId, true);
        var before = (await ConfigDbQuery.ProfileItems(subId) ?? []).Select(p => p.IndexId).Order().ToList();
        await ConfigDbHandler.AddBatchServers(V2pnProviderConfigsTests.Answer, subId, true);
        var after = (await ConfigDbQuery.ProfileItems(subId) ?? []).Select(p => p.IndexId).Order().ToList();

        await after.Should().BeEquivalentTo(before);
    }

    [Test]
    public async Task OnlyMadeUpServers_LeaveTheListAsItWas()
    {
        var subId = await NewSubscription();
        await ConfigDbHandler.AddBatchServers(V2pnProviderConfigsTests.Answer, subId, true);
        var before = (await ConfigDbQuery.ProfileItems(subId) ?? []).Count;

        // What a panel answers a client it refuses.
        const string refusal = "vless://00000000-0000-0000-0000-000000000000@0.0.0.0:1?encryption=none&type=tcp&security=none#App%20not%20supported";
        var count = await ConfigDbHandler.AddBatchServers(refusal, subId, true);

        await (count < 1).Should().BeTrue();
        await (await ConfigDbQuery.ProfileItems(subId) ?? []).Count.Should().BeEqualTo(before);
    }
}
