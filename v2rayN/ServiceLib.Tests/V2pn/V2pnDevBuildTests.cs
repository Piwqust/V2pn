namespace ServiceLib.Tests.V2pn;

public class V2pnDevBuildTests
{
    private static readonly DateTime At = new(2026, 11, 14, 13, 20, 0);

    [Test]
    public async Task NextVersion_IsAboveThisVersionAndTheReleaseButBelowTheNextRelease()
    {
        await V2pnDevBuild.NextVersion("1.0.0", null, At).Should().BeEqualTo("1.0.1-local.202611141320");
        await V2pnDevBuild.NextVersion("1.0.0", "1.0.0", At).Should().BeEqualTo("1.0.1-local.202611141320");
        await V2pnDevBuild.NextVersion("1.0.0", "1.2.0", At).Should().BeEqualTo("1.2.1-local.202611141320");
        // A local build is followed by a local build of the same number, until that number is released.
        await V2pnDevBuild.NextVersion("1.0.1-local.202611141000", "1.0.0", At).Should().BeEqualTo("1.0.1-local.202611141320");
        await V2pnDevBuild.NextVersion("1.0.1-local.202611141000", "1.0.1", At).Should().BeEqualTo("1.0.2-local.202611141320");

        var version = new SemanticVersion(V2pnDevBuild.NextVersion("1.0.0", "1.0.0", At));
        await (version > new SemanticVersion("1.0.0")).Should().BeTrue();
        await (version < new SemanticVersion("1.0.1")).Should().BeTrue();
        await (new SemanticVersion(V2pnDevBuild.NextVersion("1.0.0", null, At.AddMinutes(1))) > version).Should().BeTrue();
    }

    [Test]
    public async Task OnlyDeveloperModeVersions_AreLocal()
    {
        await V2pnInfo.IsLocalVersion("1.0.1-local.202611141320").Should().BeTrue();
        await V2pnInfo.IsLocalVersion("1.0.1").Should().BeFalse();
        await V2pnInfo.IsLocalVersion("1.0.1-beta.1").Should().BeFalse();
        await V2pnInfo.IsLocalVersion("1.0.1-localx.1").Should().BeFalse();
        await V2pnInfo.IsLocalVersion(null).Should().BeFalse();
    }

    [Test]
    public async Task SourceFolder_NeedsTheBuildScriptAndTheApp()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2pn-src-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "tools"));
            await V2pnDevBuild.IsSourceFolder(root).Should().BeFalse();
            await File.WriteAllTextAsync(Path.Combine(root, "tools", "release.ps1"), string.Empty);
            await V2pnDevBuild.IsSourceFolder(root).Should().BeFalse();
            Directory.CreateDirectory(Path.Combine(root, "v2rayN", "v2rayN.Desktop"));
            await File.WriteAllTextAsync(Path.Combine(root, "v2rayN", "v2rayN.Desktop", "v2rayN.Desktop.csproj"), string.Empty);
            await V2pnDevBuild.IsSourceFolder(root).Should().BeTrue();
            await V2pnDevBuild.IsSourceFolder(null).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
