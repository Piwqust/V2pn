using System.Net;
using System.Text;

namespace ServiceLib.Tests.V2pn;

public class V2pnPartnerApiTests
{
    [Test]
    public async Task Account_IsReadFieldByField_AndClamped()
    {
        var json = Encoding.UTF8.GetBytes("""
            {"plan":"Личный доступ","status":"ACTIVE","servers":{"healthy":20,"total":14},
             "devices":{"count":2,"limit":5},"until":"2027-01-31","note":"line\u0007one","extra":{"x":1}}
            """);

        var account = V2pnPartnerApi.ParseAccount(json)!;

        await account.Plan.Should().BeEqualTo("Личный доступ");
        // More healthy than there are is the partner's slip; the count stays possible.
        await account.Healthy.Should().BeEqualTo(14);
        await account.Total.Should().BeEqualTo(14);
        await account.Devices.Should().BeEqualTo(2);
        await account.DeviceLimit.Should().BeEqualTo(5);
        await account.Until.Should().BeEqualTo(new DateOnly(2027, 1, 31));
        await account.Note.Should().BeEqualTo("lineone");
    }

    [Test]
    public async Task Account_WithWrongTypes_KeepsWhatIsRight()
    {
        var json = Encoding.UTF8.GetBytes("""{"plan":42,"servers":{"healthy":"many","total":-3},"devices":[1],"until":"soon"}""");

        var account = V2pnPartnerApi.ParseAccount(json)!;

        await (account.Plan == null).Should().BeTrue();
        await (account.Total == null).Should().BeTrue();
        await (account.Devices == null).Should().BeTrue();
        await (account.Until == null).Should().BeTrue();
        await (V2pnPartnerApi.ParseAccount(Encoding.UTF8.GetBytes("[1,2]")) == null).Should().BeTrue();
        await (V2pnPartnerApi.ParseAccount(Encoding.UTF8.GetBytes("not json")) == null).Should().BeTrue();
    }

    [Test]
    public async Task Status_KeepsThreeIncidents_WithTitles()
    {
        var json = Encoding.UTF8.GetBytes("""
            {"incidents":[{"title":"Германия не отвечает","text":"Чиним","severity":"major"},{"text":"no title"},
             {"title":"b"},{"title":"c"},{"title":"d"}]}
            """);

        var incidents = V2pnPartnerApi.ParseStatus(json)!;

        await incidents.Count.Should().BeEqualTo(3);
        await incidents[0].Severe.Should().BeTrue();
        await incidents[1].Title.Should().BeEqualTo("b");
    }

    [Test]
    public async Task Account_GoesOnlyToTheCertifiedApi_WithTheToken()
    {
        var cert = TestPartner.Listing();
        var json = TestPartner.Pack();
        var pack = V2pnPartnerTrust.ReadPack(json, cert, out _)!;
        var partner = new V2pnPartner(cert, pack, Path.GetTempPath());
        var asked = new List<(string Url, string? Bearer)>();
        var saved = V2pnPartnerApi.Fetch;
        V2pnPartnerApi.Fetch = (url, bearer, _) =>
        {
            asked.Add((url, bearer));
            return Task.FromResult(new V2pnHttp.Result(HttpStatusCode.OK, Encoding.UTF8.GetBytes("""{"plan":"P"}"""), null));
        };
        try
        {
            var account = await V2pnPartnerApi.GetAccountAsync(partner, "tok-1");
            await account!.Plan.Should().BeEqualTo("P");
            await asked[0].Url.Should().BeEqualTo("https://config.example.com/v2pn/api/account");
            await asked[0].Bearer.Should().BeEqualTo("tok-1");

            // A token that is not a token is never sent.
            await (await V2pnPartnerApi.GetAccountAsync(partner, "a b") == null).Should().BeTrue();
            await asked.Count.Should().BeEqualTo(1);
        }
        finally
        {
            V2pnPartnerApi.Fetch = saved;
        }
    }
}
