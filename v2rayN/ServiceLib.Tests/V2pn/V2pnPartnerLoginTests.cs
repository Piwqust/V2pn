using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace ServiceLib.Tests.V2pn;

[NotInParallel(nameof(V2pnPartnerLogin))]
public class V2pnPartnerLoginTests
{
    private static readonly byte[] LoginPack = TestPartner.Pack(more: p => p["login"] = true);

    private static V2pnPartner Partner(byte[]? pack = null, string[]? grants = null)
    {
        var listing = TestPartner.Listing(grants: grants);
        var read = V2pnPartnerTrust.ReadPack(pack ?? LoginPack, listing, out var error) ?? throw new InvalidOperationException(error);
        return new V2pnPartner(listing, read, Path.GetTempPath());
    }

    private static byte[] Json(string text) => Encoding.UTF8.GetBytes(text);

    private static V2pnHttp.Result Answer(string json) => new(HttpStatusCode.OK, Json(json), null);

    private static readonly string Token = new('t', 43);

    private static string StartJson(string code = "482913", string? url = "https://t.me/configra_bot?start=v2pn-482913", JsonNode? expires = null, bool noTerm = false) =>
        new JsonObject
        {
            ["code"] = code,
            ["token"] = Token,
            ["url"] = url,
            ["expires"] = noTerm ? null : expires ?? 600,
        }.ToJsonString();

    /// <summary>Runs <paramref name="body"/> with the network answering <paramref name="get"/> (and <paramref name="post"/>), the questions written down.</summary>
    private static async Task WithApi(Func<string, V2pnHttp.Result> get, Func<Task> body, List<string> asked, Func<string, string, V2pnHttp.Result>? post = null)
    {
        var (savedGet, savedPost) = (V2pnPartnerLogin.Get, V2pnPartnerLogin.Post);
        V2pnPartnerLogin.Get = (url, _) =>
        {
            asked.Add("GET " + url);
            return Task.FromResult(get(url));
        };
        V2pnPartnerLogin.Post = (url, json, _) =>
        {
            asked.Add("POST " + url + " " + json);
            return Task.FromResult(post?.Invoke(url, json) ?? Answer(StartJson()));
        };
        try
        {
            await body();
        }
        finally
        {
            (V2pnPartnerLogin.Get, V2pnPartnerLogin.Post) = (savedGet, savedPost);
        }
    }

    #region The pack

    [Test]
    public async Task Login_IsOffered_OnlyWithTheFlag_TheApi_AndTheAccountGrant()
    {
        await V2pnPartnerLogin.Offers(Partner()).Should().BeTrue();
        // No flag, no API, or no account grant: no sign-in.
        await V2pnPartnerLogin.Offers(Partner(TestPartner.Pack())).Should().BeFalse();
        await V2pnPartnerLogin.Offers(Partner(TestPartner.Pack(api: null, more: p => p["login"] = true))).Should().BeFalse();
        await V2pnPartnerLogin.Offers(Partner(grants: ["edition"])).Should().BeFalse();
        // An API off the listing's domains is dropped, and the sign-in with it.
        await V2pnPartnerLogin.Offers(Partner(TestPartner.Pack(api: "https://elsewhere.example.net/api/", more: p => p["login"] = true))).Should().BeFalse();
        await V2pnPartnerLogin.Offers(Partner(TestPartner.Pack(more: p => p["login"] = "yes"))).Should().BeFalse();
    }

    #endregion The pack

    #region login/start

    [Test]
    public async Task Start_IsRead_WithTheBotLink_AndTheTerm()
    {
        var code = V2pnPartnerLogin.ParseStart(Json(StartJson()), Partner().Listing)!;

        await code.Code.Should().BeEqualTo("482913");
        await code.Token.Should().BeEqualTo(Token);
        await code.Url.Should().BeEqualTo("https://t.me/configra_bot?start=v2pn-482913");
        await code.Expires.Should().BeEqualTo(TimeSpan.FromMinutes(10));
    }

    [Test]
    public async Task Start_WithoutSixDigits_OrAShortToken_IsRefused()
    {
        var listing = Partner().Listing;
        foreach (var bad in new[] { StartJson(code: "48291"), StartJson(code: "48291a"), StartJson(code: "4829130"), """{"code":482913,"token":"x"}""", "[1]", "not json" })
        {
            await (V2pnPartnerLogin.ParseStart(Json(bad), listing) == null).Should().BeTrue();
        }
        var shortToken = new JsonObject { ["code"] = "482913", ["token"] = new string('t', 31) }.ToJsonString();
        await (V2pnPartnerLogin.ParseStart(Json(shortToken), listing) == null).Should().BeTrue();
        var spaced = new JsonObject { ["code"] = "482913", ["token"] = new string('t', 40) + " /" }.ToJsonString();
        await (V2pnPartnerLogin.ParseStart(Json(spaced), listing) == null).Should().BeTrue();
    }

    [Test]
    public async Task Start_BotLink_IsHttps_OnTelegram_OrThePartnersDomains_ElseOnlyTheDigits()
    {
        var listing = Partner().Listing;
        string? Url(string? url) => V2pnPartnerLogin.ParseStart(Json(StartJson(url: url)), listing)!.Url;

        await Url("https://config.example.com/login?code=482913").Should().BeEqualTo("https://config.example.com/login?code=482913");
        await (Url("http://t.me/configra_bot?start=v2pn-482913") == null).Should().BeTrue();
        await (Url("https://t.me.evil.example/bot") == null).Should().BeTrue();
        await (Url("https://user:pw@t.me/bot") == null).Should().BeTrue();
        await (Url("tg://resolve?domain=configra_bot") == null).Should().BeTrue();
        await (Url(null) == null).Should().BeTrue();
    }

    [Test]
    public async Task Start_Term_IsKeptBetweenOneAndThirtyMinutes()
    {
        var listing = Partner().Listing;
        TimeSpan Term(JsonNode? expires) => V2pnPartnerLogin.ParseStart(Json(StartJson(expires: expires, noTerm: expires == null)), listing)!.Expires;

        await Term(5).Should().BeEqualTo(TimeSpan.FromMinutes(1));
        await Term(86400).Should().BeEqualTo(TimeSpan.FromMinutes(30));
        await Term(null).Should().BeEqualTo(TimeSpan.FromMinutes(10));
        await Term("soon").Should().BeEqualTo(TimeSpan.FromMinutes(10));
    }

    [Test]
    public async Task Start_PostsTheDeviceAndTheApp_ToTheApi()
    {
        var asked = new List<string>();
        (V2pnLoginCode? Code, V2pnLoginProblem Problem) result = default;
        await WithApi(_ => Answer("{}"), async () => result = await V2pnPartnerLogin.StartAsync(Partner()), asked);

        await result.Code!.Code.Should().BeEqualTo("482913");
        await asked.Count.Should().BeEqualTo(1);
        await asked[0].StartsWith("POST https://config.example.com/v2pn/api/login/start {", StringComparison.Ordinal).Should().BeTrue();
        var body = JsonNode.Parse(asked[0][(asked[0].IndexOf('{'))..])!;
        await body["device"]!.GetValue<string>().Should().BeEqualTo(V2pnPartnerLogin.DeviceName());
        await body["app"]!.GetValue<string>().StartsWith("V2pn ", StringComparison.Ordinal).Should().BeTrue();
        await (V2pnPartnerLogin.DeviceName().Length <= 60).Should().BeTrue();
    }

    [Test]
    public async Task Start_ThatFails_SaysWhy()
    {
        var asked = new List<string>();
        (V2pnLoginCode? Code, V2pnLoginProblem Problem) silent = default, odd = default, notOffered = default;
        await WithApi(_ => Answer("{}"), async () =>
        {
            silent = await V2pnPartnerLogin.StartAsync(Partner());
        }, asked, (_, _) => new V2pnHttp.Result(null, null, "No answer in time."));
        await WithApi(_ => Answer("{}"), async () =>
        {
            odd = await V2pnPartnerLogin.StartAsync(Partner());
            notOffered = await V2pnPartnerLogin.StartAsync(Partner(TestPartner.Pack()));
        }, asked, (_, _) => Answer("""{"code":"12"}"""));

        await silent.Problem.Should().BeEqualTo(V2pnLoginProblem.NoAnswer);
        await odd.Problem.Should().BeEqualTo(V2pnLoginProblem.Malformed);
        await notOffered.Problem.Should().BeEqualTo(V2pnLoginProblem.Malformed);
        // A pack without the flag is never asked.
        await asked.Count.Should().BeEqualTo(2);
    }

    #endregion login/start

    #region login/poll

    [Test]
    public async Task Poll_Done_TakesTheSubscription_OnlyOnThePartnersDomains()
    {
        var listing = Partner().Listing;
        V2pnLoginAnswer Poll(string json) => V2pnPartnerLogin.ParsePoll(Json(json), listing);

        var done = Poll("""{"state":"done","subscription":"https://a.sub.example.com/s/abc","name":"Configra\u0007"}""");
        await done.State.Should().BeEqualTo(V2pnLoginState.Done);
        await done.Subscription.Should().BeEqualTo("https://a.sub.example.com/s/abc");
        await done.Name.Should().BeEqualTo("Configra");

        foreach (var foreign in new[] { "https://evil.example.net/s/abc", "http://config.example.com/s/abc", "https://u:p@config.example.com/s", "vless://x@config.example.com:443", "https://config.example.com/s/a b" })
        {
            var answer = Poll(new JsonObject { ["state"] = "done", ["subscription"] = foreign }.ToJsonString());
            await answer.State.Should().BeEqualTo(V2pnLoginState.Failed);
            await answer.Problem.Should().BeEqualTo(V2pnLoginProblem.WrongDomain);
        }
        await Poll("""{"state":"done"}""").Problem.Should().BeEqualTo(V2pnLoginProblem.Malformed);
    }

    [Test]
    public async Task Poll_States_AreRead_AndAnUnknownOneWaits()
    {
        var listing = Partner().Listing;
        V2pnLoginState State(string json) => V2pnPartnerLogin.ParsePoll(Json(json), listing).State;

        await State("""{"state":"waiting"}""").Should().BeEqualTo(V2pnLoginState.Waiting);
        await State("""{"state":"expired"}""").Should().BeEqualTo(V2pnLoginState.Expired);
        await State("""{"state":"confirming","extra":1}""").Should().BeEqualTo(V2pnLoginState.Waiting);
        await State("""{"nostate":1}""").Should().BeEqualTo(V2pnLoginState.Failed);
        await State("not json").Should().BeEqualTo(V2pnLoginState.Failed);
    }

    [Test]
    public async Task Poll_AsksWithTheToken_AndA404_IsExpired()
    {
        var asked = new List<string>();
        V2pnLoginAnswer? answer = null;
        await WithApi(_ => new V2pnHttp.Result(HttpStatusCode.NotFound, null, "HTTP 404"),
            async () => answer = await V2pnPartnerLogin.PollAsync(Partner(), "a+b/c"), asked);

        await answer!.State.Should().BeEqualTo(V2pnLoginState.Expired);
        await asked[0].Should().BeEqualTo("GET https://config.example.com/v2pn/api/login/poll?token=a%2Bb%2Fc");
    }

    #endregion login/poll

    #region The session

    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    [Test]
    public async Task Session_PollsUntilDone_ThenAsksNothingMore()
    {
        var asked = new List<string>();
        var answers = new Queue<string>(["""{"state":"waiting"}""", """{"state":"waiting"}""", """{"state":"done","subscription":"https://config.example.com/s/abc"}"""]);
        V2pnLoginAnswer? result = null, again = null;
        var waits = 0;
        var session = new V2pnLoginSession(Partner(), V2pnPartnerLogin.ParseStart(Json(StartJson()), Partner().Listing)!);
        await WithApi(_ => Answer(answers.Count > 0 ? answers.Dequeue() : """{"state":"done","subscription":"https://config.example.com/s/abc"}"""), async () =>
        {
            result = await session.RunAsync(() => waits++, default, NoDelay);
            // A second "done" for the same token would be a second link: the token is good for one answer only.
            again = await session.PollOnceAsync();
        }, asked);

        await result!.State.Should().BeEqualTo(V2pnLoginState.Done);
        await result.Subscription.Should().BeEqualTo("https://config.example.com/s/abc");
        await waits.Should().BeEqualTo(2);
        await asked.Count.Should().BeEqualTo(3);
        await again!.State.Should().BeEqualTo(V2pnLoginState.Failed);
        await again.Problem.Should().BeEqualTo(V2pnLoginProblem.Used);
    }

    [Test]
    public async Task Session_StopsOnExpired_AndOn404()
    {
        foreach (var expired in new[] { Answer("""{"state":"expired"}"""), new V2pnHttp.Result(HttpStatusCode.NotFound, null, "HTTP 404") })
        {
            var asked = new List<string>();
            V2pnLoginAnswer? result = null;
            var session = new V2pnLoginSession(Partner(), V2pnPartnerLogin.ParseStart(Json(StartJson()), Partner().Listing)!);
            await WithApi(_ => expired, async () => result = await session.RunAsync(null, default, NoDelay), asked);

            await result!.State.Should().BeEqualTo(V2pnLoginState.Expired);
            await asked.Count.Should().BeEqualTo(1);
        }
    }

    [Test]
    public async Task Session_StopsAtItsTerm_WithoutAsking()
    {
        var asked = new List<string>();
        var now = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        var session = new V2pnLoginSession(Partner(), V2pnPartnerLogin.ParseStart(Json(StartJson(expires: 120)), Partner().Listing)!, () => now);
        V2pnLoginAnswer? result = null;
        await WithApi(_ => Answer("""{"state":"waiting"}"""), async () =>
        {
            await session.PollOnceAsync();
            await session.Left.Should().BeEqualTo(TimeSpan.FromMinutes(2));
            now = now.AddMinutes(2);
            result = await session.RunAsync(null, default, NoDelay);
        }, asked);

        await result!.State.Should().BeEqualTo(V2pnLoginState.Expired);
        await asked.Count.Should().BeEqualTo(1);
        await session.Left.Should().BeEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task Session_GivesUp_WhenThePartnerStaysSilent()
    {
        var asked = new List<string>();
        V2pnLoginAnswer? result = null;
        var session = new V2pnLoginSession(Partner(), V2pnPartnerLogin.ParseStart(Json(StartJson()), Partner().Listing)!);
        await WithApi(_ => new V2pnHttp.Result(null, null, "No answer in time."), async () => result = await session.RunAsync(null, default, NoDelay), asked);

        await result!.Problem.Should().BeEqualTo(V2pnLoginProblem.NoAnswer);
        await asked.Count.Should().BeEqualTo(V2pnLoginSession.MaxFailures);
    }

    [Test]
    public async Task Session_TakesNoForeignSubscription()
    {
        var asked = new List<string>();
        V2pnLoginAnswer? result = null;
        var session = new V2pnLoginSession(Partner(), V2pnPartnerLogin.ParseStart(Json(StartJson()), Partner().Listing)!);
        await WithApi(_ => Answer("""{"state":"done","subscription":"https://evil.example.net/s/abc"}"""), async () => result = await session.RunAsync(null, default, NoDelay), asked);

        await result!.Problem.Should().BeEqualTo(V2pnLoginProblem.WrongDomain);
        await (result.Subscription == null).Should().BeTrue();
    }

    [Test]
    public async Task Session_StopsWhenTheViewCloses()
    {
        using var closed = new CancellationTokenSource();
        var session = new V2pnLoginSession(Partner(), V2pnPartnerLogin.ParseStart(Json(StartJson()), Partner().Listing)!);
        var asked = new List<string>();
        var threw = false;
        await WithApi(_ => Answer("""{"state":"waiting"}"""), async () =>
        {
            try
            {
                await session.RunAsync(() => closed.Cancel(), closed.Token, (_, token) => Task.Delay(TimeSpan.Zero, token));
            }
            catch (OperationCanceledException)
            {
                threw = true;
            }
        }, asked);

        await threw.Should().BeTrue();
        await asked.Count.Should().BeEqualTo(1);
    }

    #endregion The session

    #region Offers

    [Test]
    public async Task Offers_ListPartnersWithLogin_EvenWithoutTheirSubscriptionHere_WithoutKeepingThem()
    {
        var p = new TestPartner();
        var site = new TestSite();
        var withLogin = TestPartner.Zip(LoginPack);
        var plain = TestPartner.Zip(TestPartner.Pack(id: "plain", name: "Plain"));
        p.Publish(site, DateTimeOffset.UtcNow,
            (TestPartner.Listing(zip: withLogin), withLogin),
            (TestPartner.Listing(partner: "plain", domains: ["plain.example.com"], zip: plain), plain));
        var root = Path.Combine(Path.GetTempPath(), "v2pn-login-offers-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var manager = p.Manager(site, root, []);
            var offers = await manager.LoginOffersAsync();

            await offers.Count.Should().BeEqualTo(1);
            await offers[0].Id.Should().BeEqualTo("configra");
            // Its pictures are on disk for the sign-in to show.
            await File.Exists(Path.Combine(offers[0].Folder, "logo.png")).Should().BeTrue();
            // Shown only: not a partner of this computer, nothing kept for it.
            await manager.All.Count.Should().BeEqualTo(0);
            await Directory.Exists(Path.Combine(root, "configra")).Should().BeFalse();

            // Asked again: the same pack, not downloaded twice.
            var zips = site.Asked.Count(a => a.EndsWith(".zip", StringComparison.Ordinal));
            await (await manager.LoginOffersAsync()).Count.Should().BeEqualTo(1);
            await site.Asked.Count(a => a.EndsWith(".zip", StringComparison.Ordinal)).Should().BeEqualTo(zips);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    #endregion Offers
}
