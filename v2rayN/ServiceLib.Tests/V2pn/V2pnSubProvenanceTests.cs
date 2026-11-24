using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

/// <summary>A provider's whole config stays filtered after the person moves it out of its subscription.</summary>
public class V2pnSubProvenanceTests
{
    private const string FullXrayConfig = """
        [{"remarks":"custom-1","inbounds":[{"tag":"socks","listen":"0.0.0.0","port":10808,"protocol":"socks"}],"outbounds":[{"tag":"proxy","protocol":"vless","settings":{"vnext":[{"address":"127.0.0.1","port":443,"users":[{"id":"00000000-0000-0000-0000-000000000001","encryption":"none"}]}]}}]}]
        """;

    [Test]
    public async Task AConfigFromASubscription_MovedToNoGroup_IsStillFiltered()
    {
        CoreConfigTestFactory.BindAppManagerConfig(CoreConfigTestFactory.CreateConfig());
        TestDatabase.Initialize();
        var subId = $"sub-prov-{Guid.NewGuid():N}";
        await SQLiteHelper.Instance.ReplaceAsync(new SubItem { Id = subId, Remarks = "sub", Url = "https://sub.example.com/s" });

        await ConfigDbHandler.AddBatchServers(FullXrayConfig, subId, true);
        await V2pnSubProvenance.MarkAsync(subId);
        var imported = (await ConfigDbQuery.ProfileItems(subId))!.Single();
        await ConfigDbHandler.MoveToGroup([imported], string.Empty);
        var moved = (await ConfigDbQuery.GetProfileItem(imported.IndexId))!;

        await moved.Subid.IsNullOrEmpty().Should().BeTrue();
        await moved.V2pnFromSub.Should().BeTrue();
        await V2pnCoreConfigFilter.FromSubscription(moved).Should().BeTrue();
    }

    [Test]
    public async Task ThePersonsOwnConfig_InNoGroup_IsNotFiltered()
    {
        var own = new ProfileItem { IndexId = "own", ConfigType = EConfigType.Custom, Subid = string.Empty };

        await V2pnCoreConfigFilter.FromSubscription(own).Should().BeFalse();
    }
}
