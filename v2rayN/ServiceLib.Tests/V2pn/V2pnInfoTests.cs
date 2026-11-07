namespace ServiceLib.Tests.V2pn;

public class V2pnInfoTests
{
    private const string Hash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    [Test]
    [Arguments(Hash)]
    [Arguments(Hash + "\n")]
    [Arguments(Hash + "  V2pn-windows-x64.zip\n")]
    [Arguments(Hash + " *V2pn-windows-x64.zip")]
    [Arguments("9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08")]
    public async Task ParseHash_ReadsBareAndSha256sumFormats(string text)
    {
        await V2pnInfo.ParseHash(text).Should().BeEqualTo(Hash);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("<html>Not Found</html>")]
    [Arguments("9f86d081")]
    [Arguments("zz86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")]
    public async Task ParseHash_RejectsAnythingElse(string? text)
    {
        await V2pnInfo.ParseHash(text).Should().BeNull();
    }

    [Test]
    public async Task VerifyPackage_AcceptsOnlyTheExactFile()
    {
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, "test");

            await V2pnInfo.VerifyPackage(file, Hash, out _).Should().BeTrue();

            await V2pnInfo.VerifyPackage(file, Hash.Replace('9', '8'), out var mismatch).Should().BeFalse();
            await mismatch.Contains("checksum").Should().BeTrue();

            // A release without a checksum must not be installed.
            await V2pnInfo.VerifyPackage(file, null, out var missing).Should().BeFalse();
            await missing.Length.Should().BeGreaterThan(0);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task Packages_ComeFromV2pnReleases_OnePerArchitecture()
    {
        await V2pnInfo.ReleasesUrl.Should().BeEqualTo($"https://github.com/{V2pnInfo.Repo}/releases");
        await V2pnInfo.PackageName(Architecture.X64).Should().BeEqualTo("V2pn-windows-x64.zip");
        await V2pnInfo.PackageName(Architecture.Arm64).Should().BeEqualTo("V2pn-windows-arm64.zip");
        await V2pnInfo.PackageName(Architecture.X86).Should().BeNull();
        await V2pnInfo.PackageUrl("v1.2.3", "V2pn-windows-x64.zip")
            .Should().BeEqualTo($"https://github.com/{V2pnInfo.Repo}/releases/download/v1.2.3/V2pn-windows-x64.zip");
    }

    [Test]
    public async Task AppUpdates_NeverGoThroughV2rayNsChannel()
    {
        // v2rayN's update entry would download v2rayN over V2pn.
        await CoreInfoManager.Instance.IsCheckUpdateSupported(ECoreType.v2rayN).Should().BeFalse();
        await CoreInfoManager.Instance.GetCheckUpdateCoreTypes().Contains(ECoreType.v2rayN).Should().BeFalse();
    }

    [Test]
    public async Task Versions_V2pnHasItsOwn_UpstreamStaysInTheUserAgent()
    {
        await new SemanticVersion(V2pnInfo.Version).ToString().Should().BeEqualTo(V2pnInfo.Version);
        await V2pnInfo.UpstreamVersion.Should().BeEqualTo(Utils.GetVersionInfo());
        // Panels choose the config format from this agent, so it must keep naming v2rayN.
        await Utils.GetVersion(false).Should().StartWith("v2rayN/");
    }
}
