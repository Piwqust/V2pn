using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

/// <summary>A subscription update keeps the user on the same server and leaves an unchanged connection alone.</summary>
[NotInParallel]
public class V2pnSubUpdateTests
{
    private const string Uuid = "b2e8239e-93c1-4ceb-8f30-c94cb3179d7f";

    private static string Link(string host, string remarks, string path = "/ws") =>
        $"vless://{Uuid}@{host}:443?encryption=none&security=tls&sni={host}&type=ws&host={host}&path={Uri.EscapeDataString(path)}#{Uri.EscapeDataString(remarks)}";

    private static string Sub(params string[] links) => string.Join("\n", links);

    private static async Task<(Config config, string subId)> SetupAsync(string firstUpdate)
    {
        var config = CoreConfigTestFactory.CreateConfig();
        CoreConfigTestFactory.BindAppManagerConfig(config);
        SQLiteHelper.Instance.CreateTable<SubItem>();
        SQLiteHelper.Instance.CreateTable<ProfileItem>();
        SQLiteHelper.Instance.CreateTable<ProfileExItem>();

        var subId = $"sub-{Guid.NewGuid():N}";
        await SQLiteHelper.Instance.ReplaceAsync(new SubItem { Id = subId, Remarks = "sub", Url = "http://127.0.0.1/sub" });
        await ConfigDbHandler.AddBatchServers(firstUpdate, subId, true);
        return (config, subId);
    }

    private static async Task<ProfileItem> SelectAsync(Config config, string subId, string remarks)
    {
        var item = (await ConfigDbQuery.ProfileItems(subId))!.First(t => t.Remarks == remarks);
        config.IndexId = item.IndexId;
        return item;
    }

    [Test]
    public async Task ReshuffledList_FollowsTheSameServer_NotTheSameName()
    {
        var (config, subId) = await SetupAsync(Sub(Link("a.example", "AUTO · 1/2"), Link("b.example", "AUTO · 2/2")));
        await SelectAsync(config, subId, "AUTO · 2/2");

        // The provider moved b.example to the first place and put another server under "2/2".
        await ConfigDbHandler.AddBatchServers(Sub(Link("b.example", "AUTO · 1/2"), Link("c.example", "AUTO · 2/2")), subId, true);

        var active = await ConfigDbQuery.GetProfileItem(config.IndexId);
        await active!.Address.Should().BeEqualTo("b.example");
        await (await ConfigDbQuery.ProfileItems(subId))!.Should().HaveCount(2);
    }

    [Test]
    public async Task ServerGone_SameNameIsAnotherServer_KeepsTheActiveOne()
    {
        var (config, subId) = await SetupAsync(Sub(Link("a.example", "AUTO · 1/2"), Link("b.example", "AUTO · 2/2")));
        var before = await SelectAsync(config, subId, "AUTO · 2/2");

        await ConfigDbHandler.AddBatchServers(Sub(Link("a.example", "AUTO · 1/2"), Link("c.example", "AUTO · 2/2")), subId, true);

        await config.IndexId.Should().BeEqualTo(before.IndexId);
        var active = await ConfigDbQuery.GetProfileItem(config.IndexId);
        await active!.Address.Should().BeEqualTo("b.example");
        // The two fresh servers plus the one in use.
        await (await ConfigDbQuery.ProfileItems(subId))!.Should().HaveCount(3);
    }

    [Test]
    public async Task SameNameAndAddress_NewSettings_KeepsTheIdAndTakesTheSettings()
    {
        var (config, subId) = await SetupAsync(Sub(Link("a.example", "Netherlands")));
        var before = await SelectAsync(config, subId, "Netherlands");

        await ConfigDbHandler.AddBatchServers(Sub(Link("a.example", "Netherlands", "/new")), subId, true);

        await config.IndexId.Should().BeEqualTo(before.IndexId);
        var active = await ConfigDbQuery.GetProfileItem(config.IndexId);
        await active!.GetTransportExtra().Path.Should().BeEqualTo("/new");
        await (await ConfigDbQuery.ProfileItems(subId))!.Should().HaveCount(1);
    }

    [Test]
    public async Task UnchangedServers_KeepTheirIds_PingAndStar()
    {
        var (config, subId) = await SetupAsync(Sub(Link("a.example", "Germany"), Link("b.example", "Finland")));
        var before = (await ConfigDbQuery.ProfileItems(subId))!;
        var a = before.First(t => t.Address == "a.example");
        var b = before.First(t => t.Address == "b.example");
        ProfileExManager.Instance.SetTestDelay(a.IndexId, 87);
        ProfileExManager.Instance.SetFavorite(b.IndexId, true);

        // The same two servers in another order, plus a new one.
        await ConfigDbHandler.AddBatchServers(Sub(Link("c.example", "Sweden"), Link("b.example", "Finland"), Link("a.example", "Germany")), subId, true);

        var after = (await ConfigDbQuery.ProfileItems(subId))!;
        await after.Should().HaveCount(3);
        await after.First(t => t.Address == "a.example").IndexId.Should().BeEqualTo(a.IndexId);
        await after.First(t => t.Address == "b.example").IndexId.Should().BeEqualTo(b.IndexId);
        var exs = await ProfileExManager.Instance.GetProfileExs();
        await exs.First(t => t.IndexId == a.IndexId).Delay.Should().BeEqualTo(87);
        await ProfileExManager.Instance.IsFavorite(b.IndexId).Should().BeTrue();
        // The subscription's order wins over the old one.
        var c = after.First(t => t.Address == "c.example");
        await (ProfileExManager.Instance.GetSort(c.IndexId) < ProfileExManager.Instance.GetSort(b.IndexId)).Should().BeTrue();
        await (ProfileExManager.Instance.GetSort(b.IndexId) < ProfileExManager.Instance.GetSort(a.IndexId)).Should().BeTrue();
    }

    [Test]
    public async Task Star_StaysWithTheServer_NotWithTheName()
    {
        var (config, subId) = await SetupAsync(Sub(Link("a.example", "AUTO · 1/2"), Link("b.example", "AUTO · 2/2")));
        var b = (await ConfigDbQuery.ProfileItems(subId))!.First(t => t.Address == "b.example");
        ProfileExManager.Instance.SetFavorite(b.IndexId, true);

        // b.example left the subscription; another server now carries its name.
        await ConfigDbHandler.AddBatchServers(Sub(Link("a.example", "AUTO · 1/2"), Link("c.example", "AUTO · 2/2")), subId, true);

        var c = (await ConfigDbQuery.ProfileItems(subId))!.First(t => t.Address == "c.example");
        await ProfileExManager.Instance.IsFavorite(c.IndexId).Should().BeFalse();
    }

    [Test]
    public async Task Update_LeavesNoEntriesOfRemovedServers()
    {
        var (config, subId) = await SetupAsync(Sub(Link("a.example", "Germany"), Link("b.example", "Finland")));
        var gone = (await ConfigDbQuery.ProfileItems(subId))!.First(t => t.Address == "b.example");

        await ConfigDbHandler.AddBatchServers(Sub(Link("a.example", "Germany")), subId, true);

        var exs = await ProfileExManager.Instance.GetProfileExs();
        await exs.Any(t => t.IndexId == gone.IndexId).Should().BeFalse();
    }

    [Test]
    public async Task ConnectionKey_IgnoresIdAndName_SeesTheServer()
    {
        var (config, subId) = await SetupAsync(Sub(Link("a.example", "AUTO · 1/2"), Link("b.example", "AUTO · 2/2")));
        var b = await SelectAsync(config, subId, "AUTO · 2/2");
        var a = (await ConfigDbQuery.ProfileItems(subId))!.First(t => t.Remarks == "AUTO · 1/2");
        var keyB = await V2pnSubUpdate.ConnectionKey(b);

        await ConfigDbHandler.AddBatchServers(Sub(Link("b.example", "AUTO · 1/2"), Link("c.example", "AUTO · 2/2")), subId, true);
        var movedB = await ConfigDbQuery.GetProfileItem(config.IndexId);

        await keyB.Should().NotBeNull();
        await (await V2pnSubUpdate.ConnectionKey(movedB)).Should().BeEqualTo(keyB);
        await (await V2pnSubUpdate.ConnectionKey(a)).Should().NotBeEqualTo(keyB);
    }
}
