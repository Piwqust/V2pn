namespace ServiceLib.Tests.V2pn;

/// <summary>v2pn-partner.txt beside the program: a partner's build, or plain V2pn when it is missing or does not read.</summary>
public class V2pnEditionTests
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "v2pn-edition-" + Guid.NewGuid().ToString("N")[..8]);

    [After(Test)]
    public void Cleanup()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Test]
    [Arguments("configra", "configra")]
    [Arguments("configra\r\n", "configra")]
    [Arguments("﻿configra\n", "configra")]
    [Arguments("# the build of Configra\n\n  configra  \n", "configra")]
    [Arguments("kestrel-2", "kestrel-2")]
    public async Task Marker_WithAPartnerId_IsThatPartnersBuild(string text, string id)
    {
        await V2pnEdition.Parse(text).Should().BeEqualTo(id);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   \r\n\n")]
    [Arguments("# only a comment")]
    [Arguments("Configra")]
    [Arguments("c")]
    [Arguments("../configra")]
    [Arguments("configra extra")]
    [Arguments("\u0000\u0001PK\u0003\u0004")]
    [Arguments("a-name-that-is-far-too-long-for-a-partner-id")]
    public async Task Marker_WithoutAPartnerId_IsPlainV2pn(string? text)
    {
        await (V2pnEdition.Parse(text) == null).Should().BeTrue();
    }

    [Test]
    public async Task Read_MissingEmptyGarbageOrLarge_IsPlain_AnIdIsAPartner()
    {
        await (V2pnEdition.Read(_dir) == null).Should().BeTrue();

        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, V2pnEdition.FileName);
        await (V2pnEdition.Read(_dir) == null).Should().BeTrue();

        File.WriteAllBytes(file, []);
        await (V2pnEdition.Read(_dir) == null).Should().BeTrue();

        File.WriteAllBytes(file, [0xFF, 0xFE, 0x00, 0x41, 0x89, 0x50]);
        await (V2pnEdition.Read(_dir) == null).Should().BeTrue();

        File.WriteAllText(file, "configra\n" + new string('#', 2000));
        await (V2pnEdition.Read(_dir) == null).Should().BeTrue();

        File.WriteAllText(file, "configra\n");
        await V2pnEdition.Read(_dir).Should().BeEqualTo("configra");

        // An id V2pn does not list is still a partner's build: it just never finds that partner (V2pnPartnerTests).
        File.WriteAllText(file, "nobody");
        await V2pnEdition.Read(_dir).Should().BeEqualTo("nobody");
    }

    [Test]
    public async Task TheTestRunner_IsPlainV2pn()
    {
        await (V2pnEdition.PartnerId == null).Should().BeTrue();
        await V2pnEdition.InstallName.Should().BeEqualTo("V2pn");
        await V2pnEdition.AutoRunName.Should().BeEqualTo(Global.AutoRunName);
    }

    [Test]
    public async Task APartnersBuild_HasNamesOfItsOwn_ThatNeverStartWithPlainV2pns()
    {
        await V2pnEdition.NameFor("configra").Should().BeEqualTo("V2pn-configra");
        var plain = V2pnEdition.AutoRunNameFor(null);
        var partner = V2pnEdition.AutoRunNameFor("configra");
        // Each build tidies the sign-in entries whose names start with its own (V2pnAutostart): never the other's.
        await partner.StartsWith(plain, StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        await plain.StartsWith(partner, StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        await V2pnEdition.AutoRunNameFor("kestrel").StartsWith(partner, StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }
}
