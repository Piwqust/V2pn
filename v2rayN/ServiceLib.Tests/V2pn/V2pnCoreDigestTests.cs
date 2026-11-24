namespace ServiceLib.Tests.V2pn;

/// <summary>A core from the Updates window is checked against the SHA-256 GitHub keeps for the release file.</summary>
public class V2pnCoreDigestTests
{
    [Test]
    public async Task Locate_FindsTheReleaseAndTheFile()
    {
        var where = V2pnCoreDigest.Locate("https://github.com/XTLS/Xray-core/releases/download/v26.11.9/Xray-windows-64.zip");

        await where!.Value.ApiUrl.Should().BeEqualTo("https://api.github.com/repos/XTLS/Xray-core/releases/tags/v26.11.9");
        await where.Value.Asset.Should().BeEqualTo("Xray-windows-64.zip");
        await (V2pnCoreDigest.Locate("https://example.com/xray.zip") == null).Should().BeTrue();
    }

    [Test]
    public async Task DigestIn_ReadsTheAssetsHash()
    {
        const string json = """
            {"tag_name":"v1.14.4","assets":[
              {"name":"sing-box-1.14.4-windows-amd64.zip","digest":"sha256:2D6B25A583E5FF99656D799C52839FC3869A40B94DC659A92619FD551E32EC0A"},
              {"name":"other.zip","digest":"md5:abc"}]}
            """;

        await V2pnCoreDigest.DigestIn(json, "sing-box-1.14.4-windows-amd64.zip").Should()
            .BeEqualTo("2d6b25a583e5ff99656d799c52839fc3869a40b94dc659a92619fd551e32ec0a");
        await (V2pnCoreDigest.DigestIn(json, "other.zip") == null).Should().BeTrue();
        await (V2pnCoreDigest.DigestIn(json, "missing.zip") == null).Should().BeTrue();
        await (V2pnCoreDigest.DigestIn("not json", "x") == null).Should().BeTrue();
    }

    [Test]
    public async Task Matches_TheFile_OrAnythingWhenNoHashIsKnown()
    {
        var path = Path.Combine(Path.GetTempPath(), "v2pn-core-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(path, "abc");
        try
        {
            await V2pnCoreDigest.Matches(path, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad").Should().BeTrue();
            await V2pnCoreDigest.Matches(path, new string('0', 64)).Should().BeFalse();
            await V2pnCoreDigest.Matches(path, null).Should().BeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
