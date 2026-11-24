namespace ServiceLib.Tests.V2pn;

/// <summary>runetfreedom's rule lists from more than one place, each checked against the publisher's hash.</summary>
public class V2pnGeoMirrorTests
{
    [Test]
    [Arguments("f5381b4b2e5a2f1a38dfee35656d711c66ad216cb5ea4d7155ef43e67c4ec531  geosite.dat\n", "f5381b4b2e5a2f1a38dfee35656d711c66ad216cb5ea4d7155ef43e67c4ec531")]
    [Arguments("F5381B4B2E5A2F1A38DFEE35656D711C66AD216CB5EA4D7155EF43E67C4EC531", "f5381b4b2e5a2f1a38dfee35656d711c66ad216cb5ea4d7155ef43e67c4ec531")]
    [Arguments("<html>not found</html>", null)]
    [Arguments("", null)]
    public async Task ParseHash_ReadsTheSha256sumFile(string text, string? expected)
    {
        await V2pnGeoMirror.ParseHash(text).Should().BeEqualTo(expected);
    }

    [Test]
    public async Task Mirrors_StartWithTheReleaseBranch_AndIncludeV2pnsOwnCopy()
    {
        var mirrors = V2pnGeoMirror.Mirrors("geosite.dat");

        await mirrors[0].Should().BeEqualTo("https://raw.githubusercontent.com/runetfreedom/russia-v2ray-rules-dat/release/geosite.dat");
        await mirrors.Contains($"{V2pnInfo.SiteUrl}/geo/runetfreedom/geosite.dat").Should().BeTrue();
        await V2pnGeoMirror.HashSources("geoip.dat").All(u => u.EndsWith("geoip.dat.sha256sum", StringComparison.Ordinal)).Should().BeTrue();
    }

    [Test]
    public async Task Applies_OnlyToRunetfreedomsLists()
    {
        var config = new Config { ConstItem = new ConstItem { GeoSourceUrl = Global.GeoFilesSources[1] } };
        await V2pnGeoMirror.Applies(config).Should().BeTrue();
        config.ConstItem.GeoSourceUrl = Global.GeoFilesSources[2];
        await V2pnGeoMirror.Applies(config).Should().BeFalse();
    }

    [Test]
    public async Task Sha256_IsTheFilesHash()
    {
        var path = Path.Combine(Path.GetTempPath(), "v2pn-geo-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(path, "abc");
        try
        {
            await V2pnGeoMirror.Sha256(path).Should().BeEqualTo("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
