namespace ServiceLib.Tests.V2pn;

/// <summary>What the control channel tells: never an address, a link, a UUID, a password or a key.</summary>
public class V2pnControlDataTests
{
    private const string Secret = "SECRET";

#pragma warning disable CS0618 // The old transport fields still hold secrets in older databases: planted too.
    private static ProfileItem Planted(string id, string remarks, string sub = "sub1") => new()
    {
        IndexId = id,
        ConfigType = EConfigType.VLESS,
        Subid = sub,
        Remarks = remarks,
        Address = "secret-host.example.com",
        Port = 443,
        Password = "SECRET-0b3c5e1a-uuid",
        Username = "SECRET-user",
        Network = "xhttp",
        Path = "/SECRET-path",
        RequestHost = "SECRET-request-host.example.com",
        StreamSecurity = "reality",
        Sni = "SECRET-sni.example.com",
        PublicKey = "SECRET-public-key",
        ShortId = "SECRET-sid",
        SpiderX = "/SECRET-spider",
        Extra = "{\"SECRET\":1}",
        Cert = "SECRET-cert",
    };
#pragma warning restore CS0618

    private static void NoSecrets(string json)
    {
        if (json.Contains(Secret, StringComparison.OrdinalIgnoreCase) || json.Contains("example.com", StringComparison.OrdinalIgnoreCase)
            || json.Contains("443"))
        {
            throw new Exception($"A secret got out: {json}");
        }
    }

    private static readonly V2pnConnectionSnapshot Connected =
        new(HasServer: true, EnableTun: true, SystemProxy: ESysProxyType.ForcedClear, Core: EV2pnCoreState.Running,
            Failure: EV2pnFailure.None, KillSwitchHolding: false, Check: AvailabilityState.Available);

    [Test]
    public async Task TheStatus_TellsNoSecret()
    {
        var server = Planted("s1", "🇩🇪 Frankfurt secret-host.example.com");
        var check = new AvailabilityCheckResult(AvailabilityState.Available, 120, Ip: "203.0.113.9 SECRET");

        var status = V2pnControlData.Status(Connected, check, new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc), preferTun: true,
            server, "de", "v2pn-ru-direct");
        var json = status.ToJsonString();

        NoSecrets(json);
        await json.Should().NotContain("203.0.113.9");
        await V2pnControlJson.String(status["verdict"]).Should().BeEqualTo("vpn-connected");
        await V2pnControlJson.String(status["mode"]).Should().BeEqualTo("vpn");
        await V2pnControlJson.String(status["route"]).Should().BeEqualTo("ru-direct");
        await V2pnControlJson.String(status["server"]?["name"]).Should().BeEqualTo("Frankfurt …");
        await V2pnControlJson.String(status["server"]?["country"]).Should().BeEqualTo("DE");
        await V2pnControlJson.Long(status["check"]?["ms"]).Should().BeEqualTo(120L);
        await V2pnControlJson.String(status["check"]?["at"]).Should().BeEqualTo("2026-11-24T10:00:00Z");
        await V2pnControlJson.Bool(status["settled"]).Should().BeEqualTo(true);
    }

    [Test]
    public async Task TheStatus_IsNotSettledUntilTheCheckIsDone()
    {
        var starting = Connected with { Core = EV2pnCoreState.Starting, Check = AvailabilityState.Unchecked };
        var unchecked_ = Connected with { Check = AvailabilityState.Unchecked };
        var off = Connected with { EnableTun = false, Check = AvailabilityState.Unchecked };

        await V2pnControlJson.Bool(V2pnControlData.Status(starting, AvailabilityCheckResult.Unchecked, null, false, null, null, null)["settled"]).Should().BeEqualTo(false);
        await V2pnControlJson.Bool(V2pnControlData.Status(unchecked_, AvailabilityCheckResult.Unchecked, null, false, null, null, null)["settled"]).Should().BeEqualTo(false);
        var offStatus = V2pnControlData.Status(off, AvailabilityCheckResult.Unchecked, null, preferTun: true, null, null, "custom-id");
        await V2pnControlJson.Bool(offStatus["settled"]).Should().BeEqualTo(true);
        await V2pnControlJson.String(offStatus["verdict"]).Should().BeEqualTo("off");
        // Off, the mode is the one Connect will use.
        await V2pnControlJson.String(offStatus["mode"]).Should().BeEqualTo("vpn");
        await V2pnControlJson.String(offStatus["route"]).Should().BeEqualTo("custom");
    }

    [Test]
    public async Task TheServers_TellNoSecret()
    {
        var servers = new[]
        {
            Planted("s1", "Frankfurt 1"),
            Planted("s2", ""),
            Planted("s3", "secret-host.example.com"),
            Planted("s4", "Amsterdam SECRET-0b3c5e1a-uuid"),
        };
        var tests = new Dictionary<string, ProfileExItem>
        {
            ["s1"] = new() { IndexId = "s1", Delay = 120, IsFavorite = true },
            ["s2"] = new() { IndexId = "s2", Delay = -1 },
        };
        var subs = new Dictionary<string, string> { ["sub1"] = "Provider" };

        var list = V2pnControlData.Servers(servers, tests, subs, s => s.IndexId == "s4" ? "NL" : "de", "s1", null);
        var json = list.ToJsonString();

        NoSecrets(json);
        await list.Count.Should().BeEqualTo(4);
        await V2pnControlJson.String(list[0]?["name"]).Should().BeEqualTo("Frankfurt 1");
        await V2pnControlJson.Long(list[0]?["delayMs"]).Should().BeEqualTo(120L);
        await V2pnControlJson.Bool(list[0]?["favorite"]).Should().BeEqualTo(true);
        await V2pnControlJson.Bool(list[0]?["active"]).Should().BeEqualTo(true);
        await V2pnControlJson.String(list[0]?["subscription"]).Should().BeEqualTo("Provider");
        await V2pnControlJson.String(list[1]?["name"]).Should().BeEqualTo("VLESS server");
        await V2pnControlJson.String(list[1]?["test"]).Should().BeEqualTo("failed");
        await V2pnControlJson.String(list[2]?["name"]).Should().BeEqualTo("VLESS server");
        await V2pnControlJson.String(list[2]?["test"]).Should().BeEqualTo("untested");
        await V2pnControlJson.String(list[3]?["name"]).Should().BeEqualTo("Amsterdam …");
    }

    [Test]
    public async Task TheServers_OfOneCountry()
    {
        var servers = new[] { Planted("s1", "Frankfurt"), Planted("s2", "Amsterdam") };

        var list = V2pnControlData.Servers(servers, new Dictionary<string, ProfileExItem>(), new Dictionary<string, string>(),
            s => s.IndexId == "s1" ? "DE" : "NL", null, "nl");

        await list.Count.Should().BeEqualTo(1);
        await V2pnControlJson.String(list[0]?["id"]).Should().BeEqualTo("s2");
    }

    [Test]
    public async Task TheSubscriptions_TellNoLink()
    {
        var subs = new List<SubItem>
        {
            new() { Id = "a", Remarks = "", Url = "https://secret-sub.example.com/api/SECRET-token" },
            new() { Id = "b", Remarks = "https://secret-sub.example.com/api/SECRET-token", Url = "https://secret-sub.example.com/api/SECRET-token" },
            new() { Id = "c", Remarks = "My provider", Url = "https://secret-sub.example.com/c/SECRET", UpdateTime = 1_790_000_000 },
        };
        var infos = new Dictionary<string, SubInfoItem>
        {
            ["a"] = new() { Id = "a", PanelName = "Panel name", HasUserInfo = true, Upload = 1, Download = 2, Total = 100, Expire = 1_800_000_000,
                SupportUrl = "https://secret-support.example.com", WebPageUrl = "https://secret-page.example.com", AccountToken = "SECRET-account",
                LastResult = EV2pnSubUpdateResult.Ok, LastAttemptTime = 1_790_000_100 },
        };
        var counts = new Dictionary<string, int> { ["a"] = 40, ["c"] = 3 };

        var list = V2pnControlData.Subs(subs, id => infos.GetValueOrDefault(id), counts);
        var json = list.ToJsonString();

        NoSecrets(json);
        await V2pnControlJson.String(list[0]?["name"]).Should().BeEqualTo("Panel name");
        await V2pnControlJson.Long(list[0]?["servers"]).Should().BeEqualTo(40L);
        await V2pnControlJson.Long(list[0]?["trafficUsedBytes"]).Should().BeEqualTo(3L);
        await V2pnControlJson.String(list[0]?["lastResult"]).Should().BeEqualTo("Ok");
        await V2pnControlJson.String(list[1]?["name"]).Should().BeEqualTo("Subscription 2");
        await V2pnControlJson.String(list[2]?["name"]).Should().BeEqualTo("My provider");
        await (list[2]?["updated"] != null).Should().BeTrue();
    }

    [Test]
    public async Task AServerIsFoundByItsIdOrName_OrTheChoiceIsNamed()
    {
        var servers = new List<(string Id, string Name)> { ("a1", "Frankfurt 1"), ("a2", "Frankfurt 2"), ("b1", "Amsterdam"), ("c1", "frankfurt") };

        await V2pnControlData.FindServer(servers, "a2").Id.Should().BeEqualTo("a2");
        await V2pnControlData.FindServer(servers, "amster").Id.Should().BeEqualTo("b1");
        // The whole name wins over the names it is part of.
        await V2pnControlData.FindServer(servers, "Frankfurt").Id.Should().BeEqualTo("c1");
        var both = V2pnControlData.FindServer(servers, "Frankfurt ");
        await both.Id.Should().BeEqualTo("c1");
        var ambiguous = V2pnControlData.FindServer(servers, "furt ");
        await ambiguous.Code.Should().BeEqualTo(V2pnControlProtocol.ErrAmbiguous);
        await ambiguous.Message.Should().Contain("Frankfurt 1 (a1)");
        await V2pnControlData.FindServer(servers, "Tokyo").Code.Should().BeEqualTo(V2pnControlProtocol.ErrNotFound);
    }
}
