namespace ServiceLib.Tests.V2pn;

public class V2pnSubDefaultsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static SubInfoItem Info(TimeSpan? expiresIn = null) => new()
    {
        Id = "s1",
        HasUserInfo = true,
        Expire = expiresIn is { } left ? (Now + left).ToUnixTimeSeconds() : 0,
    };

    private static V2pnPartnerListing Cert(params string[] domains) =>
        new("kestrel", "Kestrel", domains, [], new DateOnly(2027, 1, 1), new V2pnPartnerZip("partner-kestrel.zip", 1, new string('0', 64)));

    [Test]
    public async Task ExpiryBanner_ShouldShowWithinThreeDaysAndAfterTheEnd()
    {
        await V2pnSubDefaults.ExpiryBanner(Info(TimeSpan.FromDays(4)), Now).Should().BeNull();
        await V2pnSubDefaults.ExpiryBanner(Info(TimeSpan.FromDays(3) + TimeSpan.FromMinutes(1)), Now).Should().BeNull();
        var soon = V2pnSubDefaults.ExpiryBanner(Info(TimeSpan.FromDays(2)), Now);
        await soon.Should().NotBeNull();
        await soon!.Text.Should().BeEqualTo(string.Format(ResUI.V2pnSubEndsInDays, 2));
        await soon.Tone.Should().BeEqualTo(SubPanelHeaders.ToneAttention);
        await soon.IsExpiry.Should().BeTrue();
        await V2pnSubDefaults.ExpiryBanner(Info(TimeSpan.FromHours(5)), Now)!.Text.Should().BeEqualTo(ResUI.V2pnSubEndsWithinDay);
        await V2pnSubDefaults.ExpiryBanner(Info(TimeSpan.FromHours(47)), Now)!.Text.Should().BeEqualTo(ResUI.V2pnSubEndsInOneDay);
        await V2pnSubDefaults.ExpiryBanner(Info(TimeSpan.FromDays(-1)), Now)!.Text.Should().BeEqualTo(ResUI.V2pnSubEnded);
    }

    [Test]
    public async Task ExpiryBanner_ShouldNotShowWithoutEndOrWhenTurnedOff()
    {
        await V2pnSubDefaults.ExpiryBanner(Info(), Now).Should().BeNull();
        await V2pnSubDefaults.ExpiryBanner(null, Now).Should().BeNull();
        var off = Info(TimeSpan.FromDays(1));
        off.ExpiryNotice = -1;
        await V2pnSubDefaults.ExpiryBanner(off, Now).Should().BeNull();
    }

    [Test]
    public async Task ExpiryBanner_ShouldUseProviderWordsAndRenewLink()
    {
        var info = Info(TimeSpan.FromDays(-2));
        info.ExpiryText = "Renew your plan";
        info.ExpiryButtonText = "Pay";
        info.ExpiryButtonUrl = "https://example.com/renew";
        info.WebPageUrl = "https://example.com/";
        var banner = V2pnSubDefaults.ExpiryBanner(info, Now)!;
        await banner.Text.Should().BeEqualTo("Renew your plan");
        await banner.ButtonText.Should().BeEqualTo("Pay");
        await banner.ButtonUrl.Should().BeEqualTo("https://example.com/renew");
    }

    [Test]
    public async Task ExpiryBanner_RenewShouldFallBackToTheSiteThenToNothing()
    {
        var info = Info(TimeSpan.FromDays(1));
        info.WebPageUrl = "https://example.com/cabinet";
        var banner = V2pnSubDefaults.ExpiryBanner(info, Now)!;
        await banner.ButtonUrl.Should().BeEqualTo("https://example.com/cabinet");
        await banner.ButtonText.Should().BeEqualTo(ResUI.V2pnSubRenew);

        info.WebPageUrl = null;
        banner = V2pnSubDefaults.ExpiryBanner(info, Now)!;
        await banner.ButtonUrl.Should().BeNull();
        await banner.ButtonText.Should().BeNull();
    }

    [Test]
    public async Task Banner_ExpiryShouldWinOverInfo()
    {
        var info = Info(TimeSpan.FromDays(1));
        info.InfoText = "Promo";
        info.InfoTone = SubPanelHeaders.ToneOk;
        await V2pnSubDefaults.Banner(info, Now)!.IsExpiry.Should().BeTrue();
        info.Expire = (Now + TimeSpan.FromDays(30)).ToUnixTimeSeconds();
        var banner = V2pnSubDefaults.Banner(info, Now)!;
        await banner.IsExpiry.Should().BeFalse();
        await banner.Text.Should().BeEqualTo("Promo");
        await banner.Tone.Should().BeEqualTo(SubPanelHeaders.ToneOk);
        await banner.ButtonText.Should().BeNull();
    }

    [Test]
    public async Task InfoBanner_ShouldSayMoreWhenTheLinkHasNoLabel()
    {
        var info = new SubInfoItem { InfoText = "News", InfoButtonUrl = "https://example.com/news", InfoTone = "#ff0000" };
        var banner = V2pnSubDefaults.InfoBanner(info)!;
        await banner.ButtonText.Should().BeEqualTo(ResUI.V2pnSubMore);
        await banner.Tone.Should().BeEqualTo(SubPanelHeaders.ToneInfo);
    }

    [Test]
    public async Task SortOf_ShouldFollowPersonThenProviderThenPing()
    {
        await V2pnSubDefaults.SortOf(null).Should().BeEqualTo(SubPanelHeaders.SortPing);
        var info = new SubInfoItem { SortType = SubPanelHeaders.SortAlphabet };
        await V2pnSubDefaults.SortOf(info).Should().BeEqualTo(SubPanelHeaders.SortAlphabet);
        info.OwnSort = SubPanelHeaders.SortWithout;
        await V2pnSubDefaults.SortOf(info).Should().BeEqualTo(SubPanelHeaders.SortWithout);
    }

    [Test]
    public async Task ClosedSettings_KeepTheProvidersOrderAndStart()
    {
        var info = new SubInfoItem { Id = "a", SortType = SubPanelHeaders.SortAlphabet, OwnSort = SubPanelHeaders.SortWithout, Autoconnect = true, OwnAutoconnect = true };
        SubInfoItem? Get(string _) => info;

        await V2pnSubDefaults.SortOf(info, locked: true).Should().BeEqualTo(SubPanelHeaders.SortAlphabet);
        await V2pnSubDefaults.AutoconnectOn(info, locked: true).Should().BeTrue();
        await V2pnSubDefaults.StartPlan(false, [Sub("a")], "a", Get, _ => true, _ => true).Should().NotBeNull();
        await V2pnSubDefaults.StartPlan(false, [Sub("a")], "a", Get, _ => true, _ => false).Should().BeNull();
    }

    [Test]
    public async Task Order_ShouldSortByPingNameOrNothing()
    {
        var servers = new[] { ("Germany 10", 0), ("Germany 2", 300), ("Austria", 120), ("Finland", -1) };
        string Names(string sort) => string.Join(",", V2pnSubDefaults.Order(servers, sort, s => s.Item1, s => s.Item2).Select(s => s.Item1));
        await Names(SubPanelHeaders.SortPing).Should().BeEqualTo("Austria,Germany 2,Germany 10,Finland");
        await Names(SubPanelHeaders.SortAlphabet).Should().BeEqualTo("Austria,Finland,Germany 2,Germany 10");
        await Names(SubPanelHeaders.SortWithout).Should().BeEqualTo("Germany 10,Germany 2,Austria,Finland");
    }

    private static SubItem Sub(string id, bool enabled = true) => new() { Id = id, Remarks = id, Url = "https://example.com/" + id, Enabled = enabled };

    [Test]
    public async Task StartPlan_ShouldUseTheChosenServersSubscription()
    {
        var infos = new Dictionary<string, SubInfoItem>
        {
            ["a"] = new() { Id = "a" },
            ["b"] = new() { Id = "b", Autoconnect = true, AutoconnectType = SubPanelHeaders.ConnectLowestDelay },
        };
        var subs = new[] { Sub("a"), Sub("b") };
        var plan = V2pnSubDefaults.StartPlan(false, subs, "b", id => infos.GetValueOrDefault(id), _ => true);
        await plan.Should().NotBeNull();
        await plan!.SubId.Should().BeEqualTo("b");
        await plan.Type.Should().BeEqualTo(SubPanelHeaders.ConnectLowestDelay);
        // The chosen server's subscription did not ask.
        await V2pnSubDefaults.StartPlan(false, subs, "a", id => infos.GetValueOrDefault(id), _ => true).Should().BeNull();
    }

    [Test]
    public async Task StartPlan_ShouldTakeTheFirstSubscriptionWithServersWhenNothingIsChosen()
    {
        var infos = new Dictionary<string, SubInfoItem>
        {
            ["a"] = new() { Id = "a", Autoconnect = true },
            ["b"] = new() { Id = "b", Autoconnect = true },
        };
        var plan = V2pnSubDefaults.StartPlan(false, [Sub("a"), Sub("b")], null, id => infos.GetValueOrDefault(id), id => id == "b");
        await plan!.SubId.Should().BeEqualTo("b");
        await plan.Type.Should().BeEqualTo(SubPanelHeaders.ConnectLastUsed);
    }

    [Test]
    public async Task StartPlan_ShouldStepAsideForThePerson()
    {
        var info = new SubInfoItem { Id = "a", Autoconnect = true };
        SubInfoItem? Get(string _) => info;
        // Left connected: the app connects as before, the provider changes nothing.
        await V2pnSubDefaults.StartPlan(true, [Sub("a")], "a", Get, _ => true).Should().BeNull();
        // A server of the person's own is their choice.
        await V2pnSubDefaults.StartPlan(false, [Sub("a")], "", Get, _ => true).Should().BeNull();
        // A disabled subscription, or one without servers.
        await V2pnSubDefaults.StartPlan(false, [Sub("a", enabled: false)], "a", Get, _ => true).Should().BeNull();
        await V2pnSubDefaults.StartPlan(false, [Sub("a")], "a", Get, _ => false).Should().BeNull();
        // The person chose to decide themselves.
        info.OwnAutoconnect = true;
        await V2pnSubDefaults.StartPlan(false, [Sub("a")], "a", Get, _ => true).Should().BeNull();
    }

    [Test]
    public async Task PickServer_LowestDelayShouldTakeTheQuickestThatAnswered()
    {
        var servers = new List<(string, int)> { ("x", -1), ("y", 250), ("z", 90), ("w", 0) };
        await V2pnSubDefaults.PickServer(SubPanelHeaders.ConnectLowestDelay, servers, [], null, new Random(1)).Should().BeEqualTo("z");
        var dead = new List<(string, int)> { ("x", -1), ("w", 0) };
        await V2pnSubDefaults.PickServer(SubPanelHeaders.ConnectLowestDelay, dead, [], null, new Random(1)).Should().BeNull();
    }

    [Test]
    public async Task PickServer_LastUsedShouldTakeRecentThenCurrentThenFirst()
    {
        var servers = new List<(string, int)> { ("x", 0), ("y", 0), ("z", 0) };
        await V2pnSubDefaults.PickServer(SubPanelHeaders.ConnectLastUsed, servers, ["other", "z", "y"], "y", new Random(1)).Should().BeEqualTo("z");
        await V2pnSubDefaults.PickServer(SubPanelHeaders.ConnectLastUsed, servers, ["other"], "y", new Random(1)).Should().BeEqualTo("y");
        await V2pnSubDefaults.PickServer(SubPanelHeaders.ConnectLastUsed, servers, [], "elsewhere", new Random(1)).Should().BeEqualTo("x");
        await V2pnSubDefaults.PickServer(SubPanelHeaders.ConnectLastUsed, [], [], null, new Random(1)).Should().BeNull();
    }

    [Test]
    public async Task PickServer_RandomShouldStayInTheSubscription()
    {
        var servers = new List<(string, int)> { ("x", 0), ("y", 0), ("z", 0) };
        var random = new Random(7);
        var picked = Enumerable.Range(0, 50).Select(_ => V2pnSubDefaults.PickServer(SubPanelHeaders.ConnectRandom, servers, [], null, random)).ToHashSet();
        await picked.Should().BeEquivalentTo(new[] { "x", "y", "z" });
    }

    [Test]
    public async Task MovedUrl_ShouldNeedAVerifiedPartnerAndACertifiedHost()
    {
        const string current = "https://old.kestrel.example/sub/abc";
        var cert = Cert("old.kestrel.example", "*.kestrel.example");
        await V2pnSubDefaults.MovedUrl(current, "https://new.kestrel.example/sub/abc", null, cert).Should().BeEqualTo("https://new.kestrel.example/sub/abc");
        // No certificate: anyone answering on the old address could take the subscription away.
        await V2pnSubDefaults.MovedUrl(current, "https://new.kestrel.example/sub/abc", null, null).Should().BeNull();
        // A host the certificate does not name.
        await V2pnSubDefaults.MovedUrl(current, "https://evil.example/sub/abc", null, cert).Should().BeNull();
        await V2pnSubDefaults.MovedUrl(current, null, "evil.example", cert).Should().BeNull();
        // Plain http, and a move to the same host.
        await V2pnSubDefaults.MovedUrl(current, "http://new.kestrel.example/sub/abc", null, cert).Should().BeNull();
        await V2pnSubDefaults.MovedUrl(current, "https://old.kestrel.example/other", null, cert).Should().BeNull();
    }

    [Test]
    public async Task MovedUrl_NewDomainShouldKeepPortPathAndQuery()
    {
        var cert = Cert("*.kestrel.example");
        await V2pnSubDefaults.MovedUrl("https://a.kestrel.example:8443/sub/token%2F?key=a+b#x", null, "b.kestrel.example", cert)
            .Should().BeEqualTo("https://b.kestrel.example:8443/sub/token%2F?key=a+b#x");
        await V2pnSubDefaults.MovedUrl("https://a.kestrel.example/sub", null, "B.Kestrel.Example", cert)
            .Should().BeEqualTo("https://b.kestrel.example/sub");
    }

    [Test]
    public async Task MovedUrl_BadNewUrlShouldNotFallBackToNewDomain()
    {
        var cert = Cert("*.kestrel.example");
        await V2pnSubDefaults.MovedUrl("https://a.kestrel.example/sub", "not a url", "b.kestrel.example", cert).Should().BeNull();
        await V2pnSubDefaults.MovedUrl("https://a.kestrel.example/sub", "https://c.kestrel.example/s", "b.kestrel.example", cert)
            .Should().BeEqualTo("https://c.kestrel.example/s");
    }

    [Test]
    public async Task CopyCompat_ShouldKeepThePersonsChoices()
    {
        var info = new SubInfoItem { Id = "s1", OwnAutoconnect = true, OwnSort = SubPanelHeaders.SortAlphabet, InfoText = "old" };
        var panel = SubPanelHeaders.Parse(new Dictionary<string, string>
        {
            ["subscription-autoconnect"] = "1",
            ["subscriptions-sort-type"] = "ping",
            ["sub-expire"] = "0",
            ["hide-settings"] = "1",
        });
        SubInfoManager.CopyCompat(panel, info);
        await info.Autoconnect.Should().BeTrue();
        await info.SortType.Should().BeEqualTo(SubPanelHeaders.SortPing);
        await info.ExpiryNotice.Should().BeEqualTo(-1);
        await info.HideSettings.Should().BeTrue();
        // The banner went away with the update; the person's choices stay.
        await info.InfoText.Should().BeNull();
        await info.OwnAutoconnect.Should().BeTrue();
        await info.OwnSort.Should().BeEqualTo(SubPanelHeaders.SortAlphabet);
        await V2pnSubDefaults.AutoconnectOn(info).Should().BeFalse();
        await V2pnSubDefaults.SortOf(info).Should().BeEqualTo(SubPanelHeaders.SortAlphabet);
    }
}
