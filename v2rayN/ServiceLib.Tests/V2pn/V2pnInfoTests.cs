namespace ServiceLib.Tests.V2pn;

public class V2pnInfoTests
{
    [Test]
    public async Task VerifyPackage_AcceptsOnlyTheExactFile()
    {
        var file = Path.GetTempFileName();
        try
        {
            var content = "test"u8.ToArray();
            await File.WriteAllBytesAsync(file, content);
            var hash = Convert.ToHexStringLower(SHA256.HashData(content));

            await V2pnInfo.VerifyPackage(file, new V2pnManifestFile("a.zip", content.Length, hash), out _).Should().BeTrue();

            await V2pnInfo.VerifyPackage(file, new V2pnManifestFile("a.zip", content.Length, new string('0', 64)), out var mismatch).Should().BeFalse();
            await mismatch.Contains("damaged").Should().BeTrue();

            // The right bytes followed by more bytes are not the package either.
            await V2pnInfo.VerifyPackage(file, new V2pnManifestFile("a.zip", content.Length + 1, hash), out _).Should().BeFalse();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task Packages_OnePerSystemAndArchitecture()
    {
        await V2pnInfo.ReleasesUrl.Should().BeEqualTo($"https://github.com/{V2pnInfo.Repo}/releases");
        await V2pnInfo.PackageName(Architecture.X64, OSPlatform.Windows).Should().BeEqualTo("V2pn-windows-x64.zip");
        await V2pnInfo.PackageName(Architecture.Arm64, OSPlatform.Windows).Should().BeEqualTo("V2pn-windows-arm64.zip");
        await V2pnInfo.PackageName(Architecture.X64, OSPlatform.OSX).Should().BeEqualTo("V2pn-macos-x64.zip");
        await V2pnInfo.PackageName(Architecture.Arm64, OSPlatform.OSX).Should().BeEqualTo("V2pn-macos-arm64.zip");
        await V2pnInfo.PackageName(Architecture.X86, OSPlatform.Windows).Should().BeNull();
        await V2pnInfo.PackageName(Architecture.X64, OSPlatform.Linux).Should().BeNull();
    }

    [Test]
    public async Task Sources_AreLaidOutLikeAGitHubReleasesPage()
    {
        await V2pnInfo.UpdateSources[0].Should().BeEqualTo(V2pnInfo.ReleasesUrl);
        await V2pnInfo.ManifestUrl("https://example.com/v2pn").Should().BeEqualTo("https://example.com/v2pn/latest/download/release.json");
        await V2pnInfo.PackageUrl("https://example.com/v2pn", "1.2.3", "V2pn-windows-x64.zip")
            .Should().BeEqualTo("https://example.com/v2pn/download/v1.2.3/V2pn-windows-x64.zip");
    }

    [Test]
    public async Task LocalBuild_IsNotARelease_AndNeverReplacesItself()
    {
        await V2pnInfo.IsRelease.Should().BeFalse();
        await V2pnInfo.SelfUpdateSupported.Should().BeFalse();
        await V2pnInfo.DisplayVersion.Should().BeEqualTo(V2pnInfo.Version + "-dev");
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
