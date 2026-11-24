namespace ServiceLib.Tests.V2pn;

public class V2pnPartnerCatalogTests
{
    private static readonly string[] AllGrants = ["edition", "account", "actions", "lock"];

    private static V2pnPartnerPack ReadPack(Action<JsonObject> more, string[]? grants = null, string? api = "https://config.example.com/v2pn/api/")
    {
        var cert = TestPartner.Listing(grants: grants ?? AllGrants);
        var json = TestPartner.Pack(api: api, more: more);
        return V2pnPartnerTrust.ReadPack(json, cert, out _)!;
    }

    private static JsonArray Array(params JsonNode[] items) => new(items);

    private static JsonObject Action(string id, string label, string kind, Action<JsonObject>? more = null)
    {
        var action = new JsonObject { ["id"] = id, ["label"] = label, ["icon"] = "bolt", ["do"] = kind };
        more?.Invoke(action);
        return action;
    }

    [Test]
    public async Task Servers_AreReadWithTheirLimits()
    {
        var pack = ReadPack(p => p["servers"] = Array(
            new JsonObject
            {
                ["name"] = "🇩🇪 Германия 2", ["group"] = "Основные", ["country"] = "de", ["city"] = "Франкфурт",
                ["purpose"] = Array("youtube", "GAMES", "teleport", "youtube"), ["priority"] = 5, ["hidden"] = false, ["note"] = "Быстрый",
                ["load"] = 40, ["healthy"] = true,
            },
            new JsonObject { ["host"] = "NL1.Example.com.", ["group"] = new string('g', 25), ["country"] = "NLD", ["priority"] = 100000, ["hidden"] = true },
            new JsonObject { ["group"] = "nothing to match by" },
            new JsonObject { ["name"] = "Bad types", ["purpose"] = "ai", ["note"] = 42, ["city"] = new string('c', 33) }));

        await pack.ServerNotes.Count.Should().BeEqualTo(3);
        var de = pack.ServerNotes[0];
        await de.Country.Should().BeEqualTo("DE");
        await de.City.Should().BeEqualTo("Франкфурт");
        await de.Purposes.Should().BeEquivalentTo(new[] { "youtube", "games" });
        await de.Priority.Should().BeEqualTo(5);
        await de.Note.Should().BeEqualTo("Быстрый");
        // Load and health are live numbers: only the API gives them.
        await (de.Load == null).Should().BeTrue();
        await (de.Healthy == null).Should().BeTrue();

        var nl = pack.ServerNotes[1];
        await nl.Host.Should().BeEqualTo("nl1.example.com");
        await (nl.Group == null).Should().BeTrue();
        await (nl.Country == null).Should().BeTrue();
        await (nl.Priority == null).Should().BeTrue();
        await nl.IsHidden.Should().BeTrue();
        await nl.Matches("whatever", "nl1.example.com").Should().BeTrue();

        var bad = pack.ServerNotes[2];
        await bad.Purposes.Should().BeEquivalentTo(new[] { "ai" });
        await (bad.Note == null).Should().BeTrue();
        await (bad.City == null).Should().BeTrue();
    }

    [Test]
    public async Task ServersAndActions_NeedTheActionsGrant_AndLockNeedsTheLockGrant()
    {
        void More(JsonObject p)
        {
            p["servers"] = Array(new JsonObject { ["name"] = "A" });
            p["actions"] = Array(Action("fast", "Самый быстрый", "connect.best"));
            p["lock"] = true;
        }

        var without = ReadPack(More, grants: ["edition", "account"]);
        await without.ServerNotes.Count.Should().BeEqualTo(0);
        await without.ActionList.Count.Should().BeEqualTo(0);
        await without.Lock.Should().BeFalse();

        var with = ReadPack(More, grants: ["actions", "lock"], api: null);
        await with.ServerNotes.Count.Should().BeEqualTo(1);
        await with.ActionList.Count.Should().BeEqualTo(1);
        await with.Lock.Should().BeTrue();
    }

    [Test]
    public async Task Actions_KeepTheFirstFourUsable_AndDropWhatV2pnCannotDo()
    {
        var pack = ReadPack(p => p["actions"] = Array(
            Action("fast", "Самый быстрый", "connect.best"),
            Action("hack", "Run", "shell.exec"),
            Action("fast", "Duplicate id", "subscription.update"),
            Action("long", new string('x', 25), "subscription.update"),
            Action("Bad Id", "Bad id", "subscription.update"),
            Action("stable", "Самый стабильный", "connect.best", a =>
            {
                a["strategy"] = "stable";
                a["scope"] = new JsonObject { ["group"] = "main", ["purpose"] = "games", ["servers"] = Array("A", "B") };
                a["icon"] = "rocket";
            }),
            Action("weird", "Weird", "connect.best", a => a["strategy"] = "random"),
            Action("yt", "Для YouTube", "connect.recommend", a => a["purpose"] = "youtube"),
            Action("renew", "Продлить", "open.renew"),
            Action("more", "Fifth", "check.services")));

        await pack.ActionList.Select(a => a.Id).ToArray().Should().BeEquivalentTo(new[] { "fast", "stable", "yt", "renew" });
        var fast = pack.ActionList[0];
        await fast.Strategy.Should().BeEqualTo(V2pnStrategies.Ping);
        await (fast.Scope == null).Should().BeTrue();
        var stable = pack.ActionList[1];
        await (stable.Icon == null).Should().BeTrue();
        await stable.Scope!.Group.Should().BeEqualTo("main");
        await stable.Scope.Purpose.Should().BeEqualTo("games");
        await stable.Scope.Servers.Count.Should().BeEqualTo(2);
        await pack.ActionList[2].Purpose.Should().BeEqualTo("youtube");
    }

    [Test]
    public async Task Actions_ThatNeedTheApi_OrHaveABadPurposeOrLink_AreDropped()
    {
        void More(JsonObject p) => p["actions"] = Array(
            Action("yt", "Для YouTube", "connect.recommend", a => a["purpose"] = "youtube"),
            Action("card", "О тарифе", "info.card"),
            Action("bad", "Bad purpose", "connect.recommend", a => a["purpose"] = "teleport"),
            Action("scope", "Bad scope", "connect.best", a => a["scope"] = new JsonObject { ["purpose"] = "teleport" }),
            Action("own", "Свой сайт", "open.link", a => a["url"] = "https://config.example.com/promo"),
            Action("tme", "Бот", "open.link", a => a["url"] = "https://t.me/config_bot"),
            Action("evil", "Чужой", "open.link", a => a["url"] = "https://evil.example.net/"),
            Action("file", "Файл", "open.link", a => a["url"] = "file:///c:/windows/system32/calc.exe"),
            Action("srv", "Германия", "connect.server"),
            Action("grp", "Игры", "connect.group", a => a["group"] = "Игровые"));

        var noApi = ReadPack(More, api: null);
        await noApi.ActionList.Select(a => a.Id).ToArray().Should().BeEquivalentTo(new[] { "own", "tme", "grp" });

        var withApi = ReadPack(More);
        await withApi.ActionList.Select(a => a.Id).ToArray().Should().BeEquivalentTo(new[] { "yt", "card", "own", "tme" });
    }

    [Test]
    public async Task ActionLinks_AreHttpsOnTheCertificatesDomainsOrTelegram()
    {
        var cert = TestPartner.Listing();

        await V2pnPartnerCatalog.ActionLink(cert, "https://a.sub.example.com/x").Should().BeEqualTo("https://a.sub.example.com/x");
        await V2pnPartnerCatalog.ActionLink(cert, "tg://resolve?domain=config_bot").Should().BeEqualTo("tg://resolve?domain=config_bot");
        await (V2pnPartnerCatalog.ActionLink(cert, "http://config.example.com/") == null).Should().BeTrue();
        await (V2pnPartnerCatalog.ActionLink(cert, "https://user:pw@config.example.com/") == null).Should().BeTrue();
        await (V2pnPartnerCatalog.ActionLink(cert, "ms-msdt:/id PCWDiagnostic") == null).Should().BeTrue();
    }

    [Test]
    public async Task Purposes_HaveLabels()
    {
        foreach (var purpose in V2pnPurposes.All)
        {
            await (V2pnPurposes.Label(purpose).Length > 0).Should().BeTrue();
        }
        await V2pnPurposes.All.Count.Should().BeEqualTo(7);
        await V2pnActionIcons.All.Count.Should().BeEqualTo(12);
    }

    [Test]
    public async Task ApiServers_CarryLoadAndHealth_AndOverrideThePackFieldByField()
    {
        var json = Encoding.UTF8.GetBytes("""
            {"servers":[{"name":"Германия 2","load":120,"healthy":false,"group":"Резерв"},{"host":"nl.example.com","load":35,"hidden":false}]}
            """);

        var live = V2pnPartnerCatalog.ParseServersAnswer(json)!;

        await live.Count.Should().BeEqualTo(2);
        await (live[0].Load == null).Should().BeTrue();
        await live[0].Healthy.Should().BeEqualTo(false);
        await live[1].Load.Should().BeEqualTo(35);

        IReadOnlyList<V2pnServerNote> pack =
        [
            new("Германия 2", null, "Основные", "DE", "Франкфурт", ["youtube"], 3, null, "Быстрый"),
            new(null, "nl.example.com", "Основные", "NL", null, [], null, true, null),
        ];
        var de = V2pnPartnerChoice.Describe(pack, live, "Германия 2", "1.2.3.4")!;
        await de.Group.Should().BeEqualTo("Резерв");
        await de.City.Should().BeEqualTo("Франкфурт");
        await de.Purposes.Should().BeEquivalentTo(new[] { "youtube" });
        await de.Healthy.Should().BeEqualTo(false);
        var nl = V2pnPartnerChoice.Describe(pack, live, "Amsterdam", "NL.example.com")!;
        // The API said "not hidden" after the pack hid it: the live word wins.
        await nl.IsHidden.Should().BeFalse();
        await (V2pnPartnerChoice.Describe(pack, null, "Unknown", "9.9.9.9") == null).Should().BeTrue();
        await (V2pnPartnerCatalog.ParseServersAnswer(Encoding.UTF8.GetBytes("[1]")) == null).Should().BeTrue();
    }

    [Test]
    public async Task ApiRecommendAndCard_AreParsedWithLimits()
    {
        var recommend = V2pnPartnerCatalog.ParseRecommendAnswer(Encoding.UTF8.GetBytes("""{"servers":["Германия 2",42,"nl.example.com","Германия 2",""]}"""))!;
        await recommend.Should().BeEquivalentTo(new[] { "Германия 2", "nl.example.com" });
        await (V2pnPartnerCatalog.ParseRecommendAnswer(Encoding.UTF8.GetBytes("""{"best":"x"}""")) == null).Should().BeTrue();

        var card = V2pnPartnerCatalog.ParseCardAnswer(Encoding.UTF8.GetBytes(
            "{\"title\":\"" + new string('t', 80) + "\",\"text\":\"Строка 1\\r\\nСтрока 2\\n\\n\\n\\nКонец\\u0007\"}"))!;
        await card.Title!.Length.Should().BeEqualTo(V2pnPartnerCatalog.MaxCardTitle);
        await card.Text.Should().BeEqualTo("Строка 1\nСтрока 2\n\nКонец");
        var longText = V2pnPartnerCatalog.ParseCardAnswer(Encoding.UTF8.GetBytes("{\"text\":\"" + new string('a', 900) + "\"}"))!;
        await longText.Text.Length.Should().BeEqualTo(V2pnPartnerCatalog.MaxCardText);
        await (V2pnPartnerCatalog.ParseCardAnswer(Encoding.UTF8.GetBytes("""{"title":"Only a title"}""")) == null).Should().BeTrue();
    }

    private static V2pnServerNote Note(string? group = null, string[]? purposes = null, int? priority = null, bool hidden = false, int? load = null, bool? healthy = null) =>
        new(null, null, group, null, null, purposes ?? [], priority, hidden, null, load, healthy);

    [Test]
    public async Task Ping_TakesTheLowestDelay_WithPriorityBreakingATie_AndNeverAHiddenServer()
    {
        var servers = V2pnPartnerChoice.InScope(
        [
            new V2pnCandidate("a", "A", null, 120, Note(priority: 1)),
            new V2pnCandidate("b", "B", null, 80, Note(priority: 1)),
            new V2pnCandidate("c", "C", null, 80, Note(priority: 9)),
            new V2pnCandidate("d", "D", null, 10, Note(hidden: true)),
            new V2pnCandidate("e", "E", null, -1),
            new V2pnCandidate("f", "F", null, 0),
        ], null);

        var choice = V2pnPartnerChoice.Best(servers, V2pnStrategies.Ping)!;

        await choice.Server.Id.Should().BeEqualTo("c");
        await choice.Reason.Should().BeEqualTo(V2pnChoiceReason.Fastest);
        await (V2pnPartnerChoice.Best([new V2pnCandidate("x", "X", null, -1)], V2pnStrategies.Ping) == null).Should().BeTrue();
    }

    [Test]
    public async Task Stable_PrefersMoreAnswers_ThenTheLowerMedian_ThenTheSmallerSpread()
    {
        IReadOnlyList<V2pnCandidate> servers =
        [
            // Quick but drops every other check.
            new("flaky", "Flaky", null, 40, null, [40, -1, 42, -1, 41, -1]),
            // Always answers, slower.
            new("steady", "Steady", null, 90, null, [90, 95, 92, 91, 93, 90]),
            // Always answers, same median, wider spread.
            new("jumpy", "Jumpy", null, 92, null, [60, 92, 140, 92, 92, 92]),
        ];

        var choice = V2pnPartnerChoice.Best(servers, V2pnStrategies.Stable)!;

        await choice.Server.Id.Should().BeEqualTo("steady");
        await choice.Reason.Should().BeEqualTo(V2pnChoiceReason.Steadiest);
        await V2pnPartnerChoice.Stability([90, 95, -1, 91]).Should().BeEqualTo((3, 91, 5));

        // Nothing measured yet: the quickest answer now.
        var fresh = V2pnPartnerChoice.Best([new V2pnCandidate("a", "A", null, 70), new V2pnCandidate("b", "B", null, 50)], V2pnStrategies.Stable)!;
        await fresh.Server.Id.Should().BeEqualTo("b");
        await fresh.Reason.Should().BeEqualTo(V2pnChoiceReason.Fastest);
    }

    [Test]
    public async Task Load_TakesTheLightestHealthyServer_OrFallsBackToPing()
    {
        IReadOnlyList<V2pnCandidate> servers =
        [
            new("a", "A", null, 50, Note(load: 70)),
            new("b", "B", null, 120, Note(load: 20)),
            new("c", "C", null, 30, Note(load: 5, healthy: false)),
            new("d", "D", null, -1, Note(load: 1)),
        ];

        var choice = V2pnPartnerChoice.Best(servers, V2pnStrategies.Load)!;
        await choice.Server.Id.Should().BeEqualTo("b");
        await choice.Reason.Should().BeEqualTo(V2pnChoiceReason.LeastLoaded);

        var noNumbers = V2pnPartnerChoice.Best([new V2pnCandidate("a", "A", null, 50), new V2pnCandidate("b", "B", null, 40)], V2pnStrategies.Load)!;
        await noNumbers.Server.Id.Should().BeEqualTo("b");
        await noNumbers.Reason.Should().BeEqualTo(V2pnChoiceReason.Fastest);
    }

    [Test]
    public async Task Scope_LimitsByGroupPurposeAndNames()
    {
        IReadOnlyList<V2pnCandidate> servers =
        [
            new("a", "Германия 1", "de1.example.com", 50, Note(group: "Main", purposes: ["youtube"])),
            new("b", "Германия 2", "de2.example.com", 40, Note(group: "main", purposes: ["games"])),
            new("c", "Нидерланды", "nl.example.com", 30, Note(group: "Reserve", purposes: ["youtube"])),
            new("d", "Скрытый", "x.example.com", 10, Note(group: "Main", hidden: true)),
        ];

        await V2pnPartnerChoice.InScope(servers, new V2pnActionScope("main", null, [])).Select(s => s.Id).ToArray().Should().BeEquivalentTo(new[] { "a", "b" });
        await V2pnPartnerChoice.InScope(servers, new V2pnActionScope(null, "youtube", [])).Select(s => s.Id).ToArray().Should().BeEquivalentTo(new[] { "a", "c" });
        await V2pnPartnerChoice.InScope(servers, new V2pnActionScope(null, null, ["NL.example.com", "Германия 1", "Скрытый"])).Select(s => s.Id).ToArray().Should().BeEquivalentTo(new[] { "a", "c" });
        var best = V2pnPartnerChoice.Best(V2pnPartnerChoice.InScope(servers, new V2pnActionScope("Main", "youtube", [])), V2pnStrategies.Ping)!;
        await best.Server.Id.Should().BeEqualTo("a");
    }

    [Test]
    public async Task Recommend_TakesTheFirstOfTheSubscriptionThatAnswers_OrNothingForTheFallback()
    {
        IReadOnlyList<V2pnCandidate> servers =
        [
            new("a", "Германия 1", "de1.example.com", -1, Note(purposes: ["youtube"])),
            new("b", "Германия 2", "de2.example.com", 80, Note(purposes: ["youtube"])),
            new("h", "Скрытый", "hidden.example.com", 60, Note(hidden: true)),
        ];

        // Not in the subscription, then one that did not answer, then the one that did.
        var choice = V2pnPartnerChoice.Recommended(["foreign.example.org", "Германия 1", "de2.example.com"], servers)!;
        await choice.Server.Id.Should().BeEqualTo("b");
        await choice.Reason.Should().BeEqualTo(V2pnChoiceReason.ByPartner);
        // The partner may name a server it hides from the list.
        await V2pnPartnerChoice.Recommended(["hidden.example.com"], servers)!.Server.Id.Should().BeEqualTo("h");
        await V2pnPartnerChoice.RecommendedIn(["x", "Германия 2", "Германия 1"], servers).Select(s => s.Id).ToArray().Should().BeEquivalentTo(new[] { "b", "a" });

        // None answered: the window falls back to the fastest within the purpose.
        await (V2pnPartnerChoice.Recommended(["Германия 1", "foreign"], servers) == null).Should().BeTrue();
        var fallback = V2pnPartnerChoice.Best(V2pnPartnerChoice.InScope(servers, new V2pnActionScope(null, "youtube", [])), V2pnStrategies.Ping)!;
        await fallback.Server.Id.Should().BeEqualTo("b");
    }

    [Test]
    public async Task Lock_NeedsTheLockGrant()
    {
        var hidden = new SubInfoItem { HideSettings = true };
        var hiddenUrl = new SubInfoItem { HideUrl = true };
        var withLock = TestPartner.Listing(grants: AllGrants);
        var withoutLock = TestPartner.Listing(grants: ["edition", "account", "actions"]);
        var json = TestPartner.Pack();
        var pack = V2pnPartnerTrust.ReadPack(json, withLock, out _)!;

        var locked = V2pnLockState.For(withLock, pack, hidden, "Configra");
        await locked.Settings.Should().BeTrue();
        await locked.UrlLocked.Should().BeTrue();
        await locked.HideUrl.Should().BeFalse();
        await locked.PartnerName.Should().BeEqualTo("Configra");

        var url = V2pnLockState.For(withLock, pack, hiddenUrl, "Configra");
        await url.Settings.Should().BeFalse();
        await url.HideUrl.Should().BeTrue();
        await url.UrlLocked.Should().BeTrue();

        // Without the grant the flags do nothing, and neither does a subscription without a verified partner.
        await V2pnLockState.For(withoutLock, pack, hidden, "Configra").Any.Should().BeFalse();
        await V2pnLockState.For(null, null, hidden, null).Any.Should().BeFalse();
        await V2pnLockState.For(withLock, pack, new SubInfoItem(), "Configra").Any.Should().BeFalse();

        // The pack's own "lock" closes the settings without the header.
        var lockedPack = pack with { Lock = true };
        await V2pnLockState.For(withLock, lockedPack, null, "Configra").Settings.Should().BeTrue();
    }

    [Test]
    public async Task DelayHistory_KeepsTheLastTen_AndSurvivesARestart()
    {
        var file = Path.Combine(Path.GetTempPath(), "v2pn-delays-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var history = new V2pnDelayHistory(() => file);
            for (var i = 1; i <= 12; i++)
            {
                history.Record("a", i * 10);
            }
            history.Record("a", 0);
            history.Record("b", -1);
            history.Save();

            var again = new V2pnDelayHistory(() => file);
            var a = again.Get("a");
            await a.Count.Should().BeEqualTo(V2pnDelayHistory.Keep);
            await a[0].Should().BeEqualTo(40);
            await a[^1].Should().BeEqualTo(-1);
            await again.Get("b").Should().BeEquivalentTo(new[] { -1 });
            await again.Get("missing").Count.Should().BeEqualTo(0);

            again.Retain(["b"]);
            await new V2pnDelayHistory(() => file).Get("a").Count.Should().BeEqualTo(0);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task DelayHistory_KeepsWhenTheLastResultCame_AndOldFilesStillRead()
    {
        var file = Path.Combine(Path.GetTempPath(), "v2pn-delays-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            // A file from V2pn 1.4: histories only, no times.
            await File.WriteAllTextAsync(file, """{"a":[50,60],"b":[-1]}""");
            var old = new V2pnDelayHistory(() => file);
            await old.Get("a").Count.Should().BeEqualTo(2);
            await (old.MeasuredAt("a") == null).Should().BeTrue();

            var at = new DateTime(2026, 11, 24, 9, 30, 0, DateTimeKind.Utc);
            old.Record("a", 40, at);
            old.Record("c", -1, at.AddMinutes(5));
            old.Save();

            var again = new V2pnDelayHistory(() => file);
            await again.MeasuredAt("a").Should().BeEqualTo(at);
            await again.MeasuredAt("c").Should().BeEqualTo(at.AddMinutes(5));
            await (again.MeasuredAt("b") == null).Should().BeTrue();
            await (again.Get("@at").Count == 0).Should().BeTrue();

            again.Retain(["c"]);
            await (new V2pnDelayHistory(() => file).MeasuredAt("a") == null).Should().BeTrue();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task DelayHistory_FlakyIsFewerThanHalfOfFourOrMore()
    {
        await V2pnDelayHistory.Flaky([30, -1, -1, -1]).Should().BeTrue();
        await V2pnDelayHistory.Flaky([30, 40, -1, -1]).Should().BeFalse();
        await V2pnDelayHistory.Flaky([-1, -1, 30]).Should().BeFalse();
        await V2pnDelayHistory.Flaky([]).Should().BeFalse();
    }

    [Test]
    public async Task Cooldown_HoldsEachButtonForThreeSeconds()
    {
        var cooldown = new V2pnActionCooldown(V2pnActionCooldown.Default);
        var t = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

        await cooldown.TryStart("configra:fast", t).Should().BeTrue();
        await cooldown.TryStart("configra:fast", t.AddSeconds(2)).Should().BeFalse();
        await cooldown.TryStart("configra:yt", t.AddSeconds(2)).Should().BeTrue();
        await cooldown.TryStart("configra:fast", t.AddSeconds(3.5)).Should().BeTrue();
    }

    [Test]
    public async Task ServiceCheck_CountsAnyAnswerAsOpen()
    {
        var saved = V2pnServiceCheck.Probe;
        try
        {
            V2pnServiceCheck.Probe = (url, _, _) => Task.FromResult<HttpStatusCode?>(url.Contains("youtube") ? HttpStatusCode.NoContent
                : url.Contains("telegram") ? null
                : HttpStatusCode.UnavailableForLegalReasons);

            var results = await V2pnServiceCheck.RunAsync(null, CancellationToken.None);

            await results.Select(r => r.Name).ToArray().Should().BeEquivalentTo(new[] { "YouTube", "Telegram", "ChatGPT" });
            await results[0].Opens.Should().BeTrue();
            await results[1].Opens.Should().BeFalse();
            await results[2].Opens.Should().BeFalse();
        }
        finally
        {
            V2pnServiceCheck.Probe = saved;
        }
    }

    [Test]
    public async Task Live_AsksNoMoreOftenThanTheAccountCall()
    {
        var cert = TestPartner.Listing(grants: AllGrants);
        var json = TestPartner.Pack();
        var partner = new V2pnPartner(cert, V2pnPartnerTrust.ReadPack(json, cert, out _)!, "x");
        var live = new V2pnPartnerLive();
        var asked = 0;
        live.FetchServers = (_, token, _) =>
        {
            asked++;
            return Task.FromResult<IReadOnlyList<V2pnServerNote>?>([new V2pnServerNote("A", null, null, null, null, [], null, null, null, Load: 10 * asked)]);
        };
        var t = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

        await (await live.RefreshAsync(partner, "tok", t)).Should().BeTrue();
        await (await live.RefreshAsync(partner, "tok", t.AddMinutes(10))).Should().BeFalse();
        await asked.Should().BeEqualTo(1);
        await live.Describe(partner, "A", null)!.Load.Should().BeEqualTo(10);
        await (await live.RefreshAsync(partner, "tok", t.AddMinutes(31))).Should().BeTrue();
        await live.Describe(partner, "A", null)!.Load.Should().BeEqualTo(20);
        await (await live.RefreshAsync(partner, "tok", t.AddMinutes(32), force: true)).Should().BeTrue();
        await asked.Should().BeEqualTo(3);
    }
}
