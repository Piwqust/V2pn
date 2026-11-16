using System.IO.Compression;

namespace ServiceLib.Tests.V2pn;

/// <summary>tools/partner-packages.cs: a partner's package is the standard one with v2pn-partner.txt, never in release.json.</summary>
public class V2pnPartnerBuildsTests
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "v2pn-partner-builds-" + Guid.NewGuid().ToString("N")[..8]);

    [After(Test)]
    public void Cleanup()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private string Standard(string name, params (string Name, string Content, int Mode)[] files)
    {
        Directory.CreateDirectory(_dir);
        var zip = Path.Combine(_dir, name);
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var (file, content, mode) in files)
        {
            var entry = archive.CreateEntry(file);
            entry.ExternalAttributes = mode << 16;
            entry.LastWriteTime = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
            if (!file.EndsWith('/'))
            {
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }
        return zip;
    }

    private static Dictionary<string, (string Content, int Mode)> Read(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        return archive.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var reader = new StreamReader(e.Open());
            return (reader.ReadToEnd(), (e.ExternalAttributes >> 16) & 0xFFFF);
        });
    }

    [Test]
    public async Task WindowsPackage_GetsTheMarkerBesideTheProgram()
    {
        var source = Standard("V2pn-windows-x64.zip", ("V2pn/V2pn.exe", "app", 0), ("V2pn/AmazTool.exe", "updater", 0), ("V2pn/bin/xray/xray.exe", "core", 0));
        var name = V2pnPartnerBuilds.PackageName("configra", "V2pn-windows-x64.zip");
        var target = Path.Combine(_dir, name);

        V2pnPartnerBuilds.Repack(source, target, "configra");

        await name.Should().BeEqualTo("V2pn-configra-windows-x64.zip");
        var entries = Read(target);
        await entries.Count.Should().BeEqualTo(4);
        await entries["V2pn/V2pn.exe"].Content.Should().BeEqualTo("app");
        await entries["V2pn/bin/xray/xray.exe"].Content.Should().BeEqualTo("core");
        await V2pnEdition.Parse(entries["V2pn/" + V2pnEdition.FileName].Content).Should().BeEqualTo("configra");
    }

    [Test]
    public async Task MacPackage_KeepsItsName_ModesAndLinks_AndGetsTheMarkerInsideTheBundle()
    {
        const int Exec = 0x81ED; // regular file, rwxr-xr-x
        const int Link = 0xA1FF; // symbolic link
        var source = Standard("V2pn-macos-arm64.zip",
            ("V2pn.app/", "", 0x41ED),
            ("V2pn.app/Contents/MacOS/V2pn", "app", Exec),
            ("V2pn.app/Contents/MacOS/libcore.dylib", "libcore.1.dylib", Link),
            ("V2pn.app/Contents/Info.plist", "plist", 0x81A4));
        var target = Path.Combine(_dir, V2pnPartnerBuilds.PackageName("configra", "V2pn-macos-arm64.zip"));

        V2pnPartnerBuilds.Repack(source, target, "configra");

        var entries = Read(target);
        // The same V2pn.app as everyone's: one program, with the marker that gives it Configra's tab.
        await entries.Keys.All(k => k.StartsWith("V2pn.app/", StringComparison.Ordinal)).Should().BeTrue();
        await entries["V2pn.app/Contents/MacOS/V2pn"].Mode.Should().BeEqualTo(Exec);
        await entries["V2pn.app/Contents/MacOS/libcore.dylib"].Should().BeEqualTo(("libcore.1.dylib", Link));
        var marker = entries["V2pn.app/Contents/MacOS/" + V2pnEdition.FileName];
        await V2pnEdition.Parse(marker.Content).Should().BeEqualTo("configra");
        await marker.Mode.Should().BeEqualTo(0x81A4);
    }

    [Test]
    public async Task NotAStandardPackage_OrOneWithAMarker_IsRefused()
    {
        var other = Standard("other.zip", ("Something/V2pn.exe", "app", 0));
        var marked = Standard("V2pn-windows-arm64.zip", ("V2pn/V2pn.exe", "app", 0), ("V2pn/" + V2pnEdition.FileName, "kestrel", 0));
        foreach (var source in new[] { other, marked })
        {
            var refused = false;
            try
            {
                V2pnPartnerBuilds.Repack(source, Path.Combine(_dir, "out.zip"), "configra");
            }
            catch (InvalidDataException)
            {
                refused = true;
            }
            await refused.Should().BeTrue();
        }
    }

    [Test]
    [Arguments("V2pn-configra-windows-x64.zip", true)]
    [Arguments("V2pn-configra-macos-arm64.zip.sha256", true)]
    [Arguments("V2pn-kestrel-2-windows-arm64.zip", true)]
    [Arguments("V2pn-windows-x64.zip", false)]
    [Arguments("V2pn-windows-x64-update.zip", false)]
    [Arguments("V2pn-macos-arm64.dmg", false)]
    [Arguments("V2pn-macos-x64.zip", false)]
    public async Task PartnerPackages_AreToldFromTheStandardOnes(string name, bool partner)
    {
        await V2pnPartnerBuilds.IsPartnerPackage(name).Should().BeEqualTo(partner);
    }

    [Test]
    public async Task Sha256Line_IsWhatSha256sumReads()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "V2pn-configra-windows-x64.zip");
        File.WriteAllText(file, "abc");
        await V2pnPartnerBuilds.Sha256Line(file).Should().BeEqualTo("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad  V2pn-configra-windows-x64.zip\n");
    }
}
