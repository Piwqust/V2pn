using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

/// <summary>A pasted subscription link becomes a readable subscription, and a failed update is noticed.</summary>
public class V2pnSubsTests
{
    private static Config Setup()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        CoreConfigTestFactory.BindAppManagerConfig(config);
        SQLiteHelper.Instance.CreateTable<SubItem>();
        return config;
    }

    [Test]
    public async Task SubscriptionLinks_OnlyWhenEveryLineIsAnHttpLink()
    {
        await V2pnSubs.SubscriptionLinks("  https://panel.example.com/sub/abc \r\n")!.Should().BeEquivalentTo(new[] { "https://panel.example.com/sub/abc" }, StringComparer.Ordinal);
        await V2pnSubs.SubscriptionLinks("https://a.example.com/s\nhttp://b.example.com/s\nhttps://a.example.com/s")!.Count.Should().BeEqualTo(2);
        await V2pnSubs.SubscriptionLinks("https://a.example.com/s\nvless://id@host:443#x").Should().BeNull();
        await V2pnSubs.SubscriptionLinks("vless://id@host:443#x").Should().BeNull();
        await V2pnSubs.SubscriptionLinks("hello world").Should().BeNull();
        await V2pnSubs.SubscriptionLinks("https://").Should().BeNull();
        await V2pnSubs.SubscriptionLinks("   ").Should().BeNull();
        await V2pnSubs.SubscriptionLinks(null).Should().BeNull();
    }

    [Test]
    public async Task AddSubItemFromUrl_NamesItByHost()
    {
        var config = Setup();
        var url = $"https://panel.example.com/sub/{Guid.NewGuid():N}";

        await (await ConfigHandler.AddSubItem(config, url)).Should().BeEqualTo(0);
        var saved = (await AppManager.Instance.SubItems())!.Single(s => s.Url == url);

        await saved.Remarks.Should().BeEqualTo("panel.example.com");
        await SubInfoManager.IsPanelName(saved, null).Should().BeTrue();
    }

    [Test]
    public async Task AddSubscriptionAsync_CreatesNamedSubscriptionOnce()
    {
        var config = Setup();
        var url = $"https://vpn.example.org/s/{Guid.NewGuid():N}";

        var first = await V2pnSubs.AddSubscriptionAsync(config, url);
        var again = await V2pnSubs.AddSubscriptionAsync(config, " " + url + " ");

        await (first != null).Should().BeTrue();
        await first!.Value.Existed.Should().BeFalse();
        await first.Value.Item.Id.IsNotEmpty().Should().BeTrue();
        await first.Value.Item.Remarks.Should().BeEqualTo("vpn.example.org");
        await first.Value.Item.AutoUpdateInterval.Should().BeEqualTo(V2pnSubs.NewSubUpdateMinutes);
        await again!.Value.Existed.Should().BeTrue();
        await again.Value.Item.Id.Should().BeEqualTo(first.Value.Item.Id);
        await (await AppManager.Instance.SubItems())!.Count(s => s.Url == url).Should().BeEqualTo(1);
    }

    [Test]
    public async Task NotUpdatedAsync_ReportsSubscriptionsWhoseUpdateTimeStayed()
    {
        Setup();
        var ok = new SubItem { Id = $"ok-{Guid.NewGuid():N}", Remarks = "ok", Url = "https://ok.example.com/s", UpdateTime = 100 };
        var bad = new SubItem { Id = $"bad-{Guid.NewGuid():N}", Remarks = "bad", Url = "https://bad.example.com/s", UpdateTime = 0 };
        var off = new SubItem { Id = $"off-{Guid.NewGuid():N}", Remarks = "off", Url = "https://off.example.com/s", Enabled = false };
        await SQLiteHelper.Instance.ReplaceAsync(ok);
        await SQLiteHelper.Instance.ReplaceAsync(bad);
        await SQLiteHelper.Instance.ReplaceAsync(off);

        var before = (await V2pnSubs.SnapshotAsync(null))
            .Where(p => p.Key == ok.Id || p.Key == bad.Id || p.Key == off.Id)
            .ToDictionary(p => p.Key, p => p.Value);
        ok.UpdateTime = 200;
        await SQLiteHelper.Instance.ReplaceAsync(ok);
        var failed = await V2pnSubs.NotUpdatedAsync(before);

        await before.ContainsKey(off.Id).Should().BeFalse();
        await failed.Select(s => s.Id).ToList().Should().BeEquivalentTo(new[] { bad.Id }, StringComparer.Ordinal);
        await (await V2pnSubs.SnapshotAsync(bad.Id)).Keys.ToList().Should().BeEquivalentTo(new[] { bad.Id }, StringComparer.Ordinal);
    }

    [Test]
    public async Task ReadableName_ReplacesPlaceholderWithHost()
    {
        await SubInfoManager.ReadableName(new SubItem { Remarks = "import_sub", Url = "https://panel.example.com/x" }).Should().BeEqualTo("panel.example.com");
        await SubInfoManager.ReadableName(new SubItem { Remarks = "", Url = "https://panel.example.com/x" }).Should().BeEqualTo("panel.example.com");
        await SubInfoManager.ReadableName(new SubItem { Remarks = "My VPN", Url = "https://panel.example.com/x" }).Should().BeEqualTo("My VPN");
        await SubInfoManager.ReadableName(new SubItem { Remarks = "import_sub", Url = "" }).Should().BeNull();
    }
}
