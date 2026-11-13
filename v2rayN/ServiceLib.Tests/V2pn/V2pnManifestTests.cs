namespace ServiceLib.Tests.V2pn;

public class V2pnManifestTests
{
    private const string Hash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";
    private static readonly byte[] Package = "a V2pn package"u8.ToArray();

    [Test]
    public async Task SignedManifest_IsRead()
    {
        var release = new TestRelease();
        var json = TestRelease.Manifest("1.2.3", ("V2pn-windows-x64.zip", Package), ("V2pn-macos-arm64.zip", Package));

        var manifest = V2pnManifest.ReadVerified(json, release.Sign(json), [release.PublicKey], out var error);

        await error.Should().BeEqualTo(string.Empty);
        await manifest!.Version.Should().BeEqualTo("1.2.3");
        await manifest.Files.Count.Should().BeEqualTo(2);
        var file = manifest.File("V2pn-windows-x64.zip")!;
        await file.Size.Should().BeEqualTo(Package.Length);
        await file.Sha256.Should().BeEqualTo(Convert.ToHexStringLower(SHA256.HashData(Package)));
        await (manifest.File("V2pn-linux-x64.zip") == null).Should().BeTrue();
    }

    [Test]
    public async Task ManifestSignedWithAnotherKey_IsRefused()
    {
        var release = new TestRelease();
        var stranger = new TestRelease();
        var json = TestRelease.Manifest("1.2.3", ("V2pn-windows-x64.zip", Package));

        var manifest = V2pnManifest.ReadVerified(json, stranger.Sign(json), [release.PublicKey], out var error);

        await (manifest == null).Should().BeTrue();
        await error.Contains("not signed").Should().BeTrue();
    }

    [Test]
    public async Task ManifestChangedAfterSigning_IsRefused()
    {
        var release = new TestRelease();
        var json = TestRelease.Manifest("1.2.3", ("V2pn-windows-x64.zip", Package));
        var signature = release.Sign(json);
        var changed = TestRelease.Manifest("1.2.4", ("V2pn-windows-x64.zip", Package));

        await (V2pnManifest.ReadVerified(changed, signature, [release.PublicKey], out _) == null).Should().BeTrue();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("not base64 !!")]
    [Arguments("AAAA")]
    public async Task MissingOrBrokenSignature_IsRefused(string? signature)
    {
        var release = new TestRelease();
        var json = TestRelease.Manifest("1.2.3", ("V2pn-windows-x64.zip", Package));

        await (V2pnManifest.ReadVerified(json, signature, [release.PublicKey], out _) == null).Should().BeTrue();
    }

    [Test]
    public async Task AnyOfSeveralKeysMayHaveSigned_AndABrokenKeyIsSkipped()
    {
        var old = new TestRelease();
        var current = new TestRelease();
        var json = TestRelease.Manifest("1.2.3", ("V2pn-windows-x64.zip", Package));

        var manifest = V2pnManifest.ReadVerified(json, current.Sign(json), ["garbage", old.PublicKey, current.PublicKey], out _);

        await (manifest != null).Should().BeTrue();
    }

    [Test]
    [Arguments("{\"version\":\"1.2.3\",\"files\":[]}")]
    [Arguments("{\"version\":\"\",\"files\":[{\"name\":\"a.zip\",\"size\":1,\"sha256\":\"" + Hash + "\"}]}")]
    [Arguments("{\"version\":\"latest\",\"files\":[{\"name\":\"a.zip\",\"size\":1,\"sha256\":\"" + Hash + "\"}]}")]
    [Arguments("{\"version\":\"1.2.3\",\"files\":[{\"name\":\"../a.zip\",\"size\":1,\"sha256\":\"" + Hash + "\"}]}")]
    [Arguments("{\"version\":\"1.2.3\",\"files\":[{\"name\":\"dir/a.zip\",\"size\":1,\"sha256\":\"" + Hash + "\"}]}")]
    [Arguments("{\"version\":\"1.2.3\",\"files\":[{\"name\":\"a.zip\",\"size\":0,\"sha256\":\"" + Hash + "\"}]}")]
    [Arguments("{\"version\":\"1.2.3\",\"files\":[{\"name\":\"a.zip\",\"size\":1,\"sha256\":\"9f86\"}]}")]
    [Arguments("{\"version\":\"1.2.3\",\"files\":[{\"name\":\"a.zip\",\"size\":\"big\",\"sha256\":\"" + Hash + "\"}]}")]
    [Arguments("[1,2,3]")]
    [Arguments("<html>Not Found</html>")]
    public async Task MalformedManifest_IsRefusedEvenWhenSigned(string text)
    {
        var release = new TestRelease();
        var json = Encoding.UTF8.GetBytes(text);

        var manifest = V2pnManifest.ReadVerified(json, release.Sign(json), [release.PublicKey], out var error);

        await (manifest == null).Should().BeTrue();
        await error.Contains("malformed").Should().BeTrue();
    }

    [Test]
    public async Task TheKeyBuiltIntoTheApp_Parses()
    {
        await V2pnInfo.UpdateKeys.Count.Should().BeGreaterThan(0);
        foreach (var key in V2pnInfo.UpdateKeys)
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);
            await ecdsa.KeySize.Should().BeEqualTo(256);
        }
    }
}
