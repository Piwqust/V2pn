namespace ServiceLib.Tests.V2pn;

/// <summary>The IP card reads three services' answers; each shape, a refusal and junk are covered.</summary>
public class V2pnIpLookupTests
{
    private const string IpWho = """
        {
          "ip": "104.28.219.140", "success": true, "type": "IPv4",
          "country": "Netherlands", "country_code": "NL", "region": "Provincie Noord-Holland",
          "city": "Amsterdam", "latitude": 52.374028, "longitude": 4.8896881, "postal": "1012",
          "connection": { "asn": 13335, "org": "Cloudflare, Inc.", "isp": "Cloudflare, Inc.", "domain": "cloudflare.com" },
          "timezone": { "id": "Europe/Amsterdam", "abbr": "CET", "utc": "+01:00" }
        }
        """;

    [Test]
    public async Task ParseIpWho_ReadsEveryField()
    {
        var d = V2pnIpLookup.ParseIpWho(IpWho)!;

        await d.Ip.Should().BeEqualTo("104.28.219.140");
        await d.Version.Should().BeEqualTo("IPv4");
        await d.Country.Should().BeEqualTo("Netherlands");
        await d.CountryCode.Should().BeEqualTo("NL");
        await d.City.Should().BeEqualTo("Amsterdam");
        await d.Postal.Should().BeEqualTo("1012");
        await (Math.Abs(d.Latitude!.Value - 52.374) < 0.001).Should().BeTrue();
        await d.TimeZone.Should().BeEqualTo("Europe/Amsterdam");
        await d.UtcOffset.Should().BeEqualTo("+01:00");
        await d.Asn.Should().BeEqualTo(13335);
        await d.Isp.Should().BeEqualTo("Cloudflare, Inc.");
        await d.Domain.Should().BeEqualTo("cloudflare.com");
        await d.Source.Should().BeEqualTo("ipwho.is");
    }

    [Test]
    public async Task ParseIpWho_RefusalAndJunkAreNull()
    {
        await (V2pnIpLookup.ParseIpWho("""{"success": false, "message": "You've hit the monthly limit"}""") == null).Should().BeTrue();
        await (V2pnIpLookup.ParseIpWho("<html>Too many requests</html>") == null).Should().BeTrue();
        await (V2pnIpLookup.ParseIpWho("[]") == null).Should().BeTrue();
    }

    [Test]
    public async Task ParseIpSb_ReadsTheFallbackShape()
    {
        var d = V2pnIpLookup.ParseIpSb("""
            {"region":"North Holland","organization":"Cloudflare Warp","isp":"Cloudflare Warp","city":"Amsterdam",
             "asn_organization":"Cloudflare, Inc.","postal_code":"1012","asn":13335,"latitude":52.3716,"ip":"2a09:bac5::1",
             "offset":3600,"country":"The Netherlands","timezone":"Europe/Amsterdam","country_code":"NL","longitude":4.8883}
            """)!;

        await d.Ip.Should().BeEqualTo("2a09:bac5::1");
        await d.Version.Should().BeEqualTo("IPv6");
        await d.UtcOffset.Should().BeEqualTo("+01:00");
        await d.AsnOrg.Should().BeEqualTo("Cloudflare, Inc.");
        await d.Isp.Should().BeEqualTo("Cloudflare Warp");
        await d.Source.Should().BeEqualTo("ip.sb");
    }

    [Test]
    public async Task ParseReputation_ReadsTheVerdictUnderTheAddress()
    {
        const string json = """
            {"status": "ok", "104.28.219.140": {"asn": "AS13335", "risk": 66, "proxy": "yes", "type": "VPN"}}
            """;

        var r = V2pnIpLookup.ParseReputation(json, "104.28.219.140")!;
        await r.Proxy.Should().BeEqualTo(true);
        await r.Kind.Should().BeEqualTo("VPN");
        await r.Risk.Should().BeEqualTo(66);

        // Another address's verdict, or a refusal, says nothing about this one.
        await (V2pnIpLookup.ParseReputation(json, "1.1.1.1") == null).Should().BeTrue();
        await (V2pnIpLookup.ParseReputation("""{"status":"denied","message":"limit"}""", "1.1.1.1") == null).Should().BeTrue();
    }

    [Test]
    public async Task Offset_SignedHoursAndMinutes()
    {
        await V2pnIpLookup.Offset(10800).Should().BeEqualTo("+03:00");
        await V2pnIpLookup.Offset(-16200).Should().BeEqualTo("-04:30");
        await V2pnIpLookup.Offset(0).Should().BeEqualTo("+00:00");
    }

    [Test]
    public async Task Report_LeaksWhenBothAddressesMatch()
    {
        var a = V2pnIpLookup.ParseIpWho(IpWho)!;
        var other = a with { Ip = "95.24.1.2" };

        await new V2pnIpLookup.Report(a, null, a, true, true).Leaks.Should().BeTrue();
        await new V2pnIpLookup.Report(a, null, other, true, true).Leaks.Should().BeFalse();
        await new V2pnIpLookup.Report(a, null, null, true, false).Leaks.Should().BeFalse();
        await (new V2pnIpLookup.Report(null, null, other, false, true).Main == other).Should().BeTrue();
    }
}
