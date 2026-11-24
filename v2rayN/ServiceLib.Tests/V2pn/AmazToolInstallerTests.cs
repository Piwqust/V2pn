using System.IO.Compression;
using AmazTool;

namespace ServiceLib.Tests.V2pn;

/// <summary>
/// The update installer on real folders: an installed app, a package, and what is on disk after
/// a good install, a failed one and a rollback.
/// </summary>
public class AmazToolInstallerTests
{
    private static readonly string Exe = OperatingSystem.IsWindows() ? "V2pn.exe" : "V2pn";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "v2pn-installer-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];

    [After(Test)]
    public void Cleanup()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private string App => Path.Combine(_dir, "app");

    private void InstallOld()
    {
        Write(Path.Combine(App, Exe), "old app");
        Write(Path.Combine(App, "AmazTool.exe"), "old updater");
        Write(Path.Combine(App, "ru", "V2pn.resources.dll"), "old ru");
        Write(Path.Combine(App, "bin", "xray", "xray.exe"), "the person's core");
        Write(Path.Combine(App, "guiConfigs", "guiNConfig.json"), "settings");
    }

    private string Package(string top, params (string Name, string Content)[] files)
    {
        var zip = Path.Combine(_dir, "V2pn-2.0.0.zip");
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var (name, content) in files)
        {
            using var writer = new StreamWriter(archive.CreateEntry($"{top}/{name}").Open());
            writer.Write(content);
        }
        return zip;
    }

    private string NewPackage() => Package("V2pn",
        (Exe, "new app"),
        ("AmazTool.exe", "new updater"),
        ("ru/V2pn.resources.dll", "new ru"),
        ("de/V2pn.resources.dll", "new de"),
        ("bin/xray/xray.exe", "packaged core"),
        ("bin/sing_box/sing-box.exe", "packaged sing-box"));

    private static void Write(string file, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }

    private string Read(params string[] parts) => File.ReadAllText(Path.Combine([App, .. parts]));

    [Test]
    public async Task Install_ReplacesTheApp_KeepsSettingsAndExistingCores()
    {
        InstallOld();
        var installer = new Installer(App, _log.Add);

        installer.Stage(NewPackage());
        // Staging alone changes nothing.
        await Read(Exe).Should().BeEqualTo("old app");
        installer.Swap();
        installer.Commit();

        await Read(Exe).Should().BeEqualTo("new app");
        await Read("AmazTool.exe").Should().BeEqualTo("new updater");
        await Read("ru", "V2pn.resources.dll").Should().BeEqualTo("new ru");
        await Read("de", "V2pn.resources.dll").Should().BeEqualTo("new de");
        await Read("guiConfigs", "guiNConfig.json").Should().BeEqualTo("settings");
        // A core that is already there is the app's to update; a missing one comes from the package.
        await Read("bin", "xray", "xray.exe").Should().BeEqualTo("the person's core");
        await Read("bin", "sing_box", "sing-box.exe").Should().BeEqualTo("packaged sing-box");
        await Directory.Exists(Path.Combine(App, ".v2pn-update")).Should().BeFalse();
    }

    [Test]
    public async Task Rollback_PutsEveryFileBack_AndRemovesWhatTheUpdateAdded()
    {
        InstallOld();
        var installer = new Installer(App, _log.Add);
        installer.Stage(NewPackage());
        installer.Swap();

        installer.Rollback();
        installer.DeleteWork();

        await Read(Exe).Should().BeEqualTo("old app");
        await Read("AmazTool.exe").Should().BeEqualTo("old updater");
        await Read("ru", "V2pn.resources.dll").Should().BeEqualTo("old ru");
        await File.Exists(Path.Combine(App, "de", "V2pn.resources.dll")).Should().BeFalse();
        await File.Exists(Path.Combine(App, "bin", "sing_box", "sing-box.exe")).Should().BeFalse();
        await Read("bin", "xray", "xray.exe").Should().BeEqualTo("the person's core");
        await Read("guiConfigs", "guiNConfig.json").Should().BeEqualTo("settings");
    }

    [Test]
    public async Task FileThatCannotBeReplaced_UndoesTheWholeInstall()
    {
        if (!OperatingSystem.IsWindows())
        {
            // An open file blocks a rename only on Windows.
            return;
        }
        InstallOld();
        var installer = new Installer(App, _log.Add);
        installer.Stage(NewPackage());

        var failed = false;
        // Held open the way a program that did not close holds its own files.
        using (File.Open(Path.Combine(App, "ru", "V2pn.resources.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try
            {
                installer.Swap();
            }
            catch (IOException)
            {
                failed = true;
            }
        }

        await failed.Should().BeTrue();
        // Files replaced before the locked one are back as well: no mix of two versions.
        await Read(Exe).Should().BeEqualTo("old app");
        await Read("AmazTool.exe").Should().BeEqualTo("old updater");
        await Read("ru", "V2pn.resources.dll").Should().BeEqualTo("old ru");
        await File.Exists(Path.Combine(App, "de", "V2pn.resources.dll")).Should().BeFalse();
    }

    [Test]
    public async Task PackageWithAPathOutsideItsFolder_IsRefused()
    {
        InstallOld();
        var installer = new Installer(App, _log.Add);
        var zip = Package("V2pn", (Exe, "new app"), ("../../evil.txt", "x"));

        var refused = false;
        try
        {
            installer.Stage(zip);
        }
        catch (InvalidDataException)
        {
            refused = true;
        }

        await refused.Should().BeTrue();
        await File.Exists(Path.Combine(_dir, "evil.txt")).Should().BeFalse();
        await Read(Exe).Should().BeEqualTo("old app");
    }

    [Test]
    [Arguments("V2pn", "readme.txt")]
    [Arguments("V2pn.app", "Contents/MacOS/V2pn")]
    public async Task PackageWithoutTheApp_OrForAnotherSystem_IsRefused(string top, string file)
    {
        InstallOld();
        var installer = new Installer(App, _log.Add);

        var refused = false;
        try
        {
            installer.Stage(Package(top, (file, "x")));
        }
        catch (InvalidDataException)
        {
            refused = true;
        }

        await refused.Should().BeTrue();
        await Read(Exe).Should().BeEqualTo("old app");
    }

    [Test]
    public async Task MacBundle_IsSwappedWhole_AndRolledBackWhole()
    {
        var bundle = Path.Combine(_dir, "Applications", "V2pn.app");
        var macos = Path.Combine(bundle, "Contents", "MacOS");
        Write(Path.Combine(macos, "V2pn"), "old app");
        Write(Path.Combine(macos, "leftover-from-old-version.dylib"), "old");
        Write(Path.Combine(bundle, "Contents", "Info.plist"), "old plist");
        await Installer.InstallRoot(macos).Should().BeEqualTo(bundle);
        await Installer.InstallRoot(App).Should().BeEqualTo(App);

        var zip = Package("V2pn.app", ("Contents/MacOS/V2pn", "new app"), ("Contents/Info.plist", "new plist"));
        var installer = new Installer(bundle, _log.Add);
        installer.Stage(zip);
        installer.Swap();

        await File.ReadAllText(Path.Combine(macos, "V2pn")).Should().BeEqualTo("new app");
        await File.ReadAllText(Path.Combine(bundle, "Contents", "Info.plist")).Should().BeEqualTo("new plist");
        // Nothing of the old bundle stays inside the new one: its signature covers every file.
        await File.Exists(Path.Combine(macos, "leftover-from-old-version.dylib")).Should().BeFalse();

        installer.Rollback();
        await File.ReadAllText(Path.Combine(macos, "V2pn")).Should().BeEqualTo("old app");
        await File.Exists(Path.Combine(macos, "leftover-from-old-version.dylib")).Should().BeTrue();

        installer.DeleteWork();
        await Directory.GetFileSystemEntries(Path.Combine(_dir, "Applications")).Length.Should().BeEqualTo(1);
    }

    [Test]
    public async Task PartnerBuild_StaysOne_ThroughAFolderUpdate()
    {
        InstallOld();
        Write(Path.Combine(App, V2pnEdition.FileName), "configra");
        var installer = new Installer(App, _log.Add);
        installer.Stage(NewPackage());
        installer.Swap();
        installer.Commit();

        await Read(Exe).Should().BeEqualTo("new app");
        await Read(V2pnEdition.FileName).Should().BeEqualTo("configra");
    }

    [Test]
    public async Task PartnerBuild_StaysOne_ThroughABundleUpdate_UnderItsOwnName()
    {
        // The partner's macOS package is V2pn.app renamed; updates still come as V2pn.app.
        var bundle = Path.Combine(_dir, "Applications", "V2pn-configra.app");
        var macos = Path.Combine(bundle, "Contents", "MacOS");
        Write(Path.Combine(macos, "V2pn"), "old app");
        Write(Path.Combine(macos, V2pnEdition.FileName), "configra");
        var installer = new Installer(bundle, _log.Add);
        installer.Stage(Package("V2pn.app", ("Contents/MacOS/V2pn", "new app")));
        installer.Swap();
        installer.Commit();

        await File.ReadAllText(Path.Combine(macos, "V2pn")).Should().BeEqualTo("new app");
        await File.ReadAllText(Path.Combine(macos, V2pnEdition.FileName)).Should().BeEqualTo("configra");
        await Directory.Exists(Path.Combine(_dir, "Applications", "V2pn.app")).Should().BeFalse();
    }

    [Test]
    public async Task PlainBundle_GainsNoMarker()
    {
        var bundle = Path.Combine(_dir, "Applications", "V2pn.app");
        Write(Path.Combine(bundle, "Contents", "MacOS", "V2pn"), "old app");
        var installer = new Installer(bundle, _log.Add);
        installer.Stage(Package("V2pn.app", ("Contents/MacOS/V2pn", "new app")));
        installer.Swap();
        installer.Commit();

        await File.Exists(Path.Combine(bundle, "Contents", "MacOS", V2pnEdition.FileName)).Should().BeFalse();
    }
}

/// <summary>What AmazTool does when a file is gone right after it was unpacked (the Mac update of 1.3.0 to 1.4.0).</summary>
public class AmazToolVanishingFileTests
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "v2pn-vanish-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];

    [After(Test)]
    public void Cleanup()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static void Write(string file, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }

    private string Bundle(out string macos)
    {
        var bundle = Path.Combine(_dir, "Applications", "V2pn.app");
        macos = Path.Combine(bundle, "Contents", "MacOS");
        Write(Path.Combine(macos, "V2pn"), "old app");
        Write(Path.Combine(macos, "bin", "mihomo", "mihomo"), "installed mihomo");
        return bundle;
    }

    private string Package(params (string Name, string Content)[] files)
    {
        var zip = Path.Combine(_dir, "V2pn-2.0.0.zip");
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var (name, content) in files)
        {
            using var writer = new StreamWriter(archive.CreateEntry($"V2pn.app/{name}").Open());
            writer.Write(content);
        }
        return zip;
    }

    [Test]
    public async Task ACoreGoneAfterUnpacking_IsTakenFromTheInstalledApp_AndTheUpdateGoesOn()
    {
        var bundle = Bundle(out var macos);
        var zip = Package(("Contents/MacOS/V2pn", "new app"), ("Contents/MacOS/bin/mihomo/mihomo", "packaged mihomo"));
        var installer = new Installer(bundle, _log.Add)
        {
            Extracted = path =>
            {
                if (Path.GetFileName(path) == "mihomo")
                {
                    File.Delete(path);
                }
            },
        };

        installer.Stage(zip);
        installer.Swap();

        await File.ReadAllText(Path.Combine(macos, "V2pn")).Should().BeEqualTo("new app");
        await File.ReadAllText(Path.Combine(macos, "bin", "mihomo", "mihomo")).Should().BeEqualTo("installed mihomo");
        await _log.Any(l => l.Contains("installed core is kept", StringComparison.Ordinal)).Should().BeTrue();
        installer.DeleteWork();
    }

    [Test]
    public async Task AProgramFileGoneAfterUnpacking_StillStopsTheUpdate()
    {
        var bundle = Bundle(out var macos);
        var zip = Package(("Contents/MacOS/V2pn", "new app"));
        var installer = new Installer(bundle, _log.Add) { Extracted = File.Delete };

        var stopped = false;
        try
        {
            installer.Stage(zip);
        }
        catch (IOException)
        {
            stopped = true;
        }

        await stopped.Should().BeTrue();
        await File.ReadAllText(Path.Combine(macos, "V2pn")).Should().BeEqualTo("old app");
        installer.DeleteWork();
    }
}

/// <summary>AmazTool checks the package against the signed manifest beside it before it unpacks anything.</summary>
public class AmazToolPackageCheckTests
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "v2pn-check-" + Guid.NewGuid().ToString("N"));
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private string Public => Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());

    [After(Test)]
    public void Cleanup()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private string Release(string version, byte[] package, string? manifestVersion = null, ECDsa? signer = null)
    {
        Directory.CreateDirectory(_dir);
        var zip = Path.Combine(_dir, $"V2pn-{version}.zip");
        File.WriteAllBytes(zip, package);
        var json = Encoding.UTF8.GetBytes(new JsonObject
        {
            ["version"] = manifestVersion ?? version,
            ["files"] = new JsonArray(new JsonObject
            {
                ["name"] = "V2pn-windows-x64-update.zip",
                ["size"] = package.Length,
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(package)),
            }),
        }.ToJsonString());
        File.WriteAllBytes(Path.ChangeExtension(zip, ".json"), json);
        var signature = (signer ?? _key).SignData(json, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        File.WriteAllText(Path.ChangeExtension(zip, ".json") + ".sig", Convert.ToBase64String(signature));
        return zip;
    }

    private string? Check(string zip, string version)
    {
        using var stream = new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.Read);
        return PackageCheck.Verify(stream, zip, version, [Public]);
    }

    [Test]
    public async Task TheSignedPackage_Passes()
    {
        var zip = Release("2.0.0", "a V2pn package"u8.ToArray());

        await (Check(zip, "2.0.0") == null).Should().BeTrue();
    }

    [Test]
    public async Task AnotherZipPutInItsPlace_IsRefused()
    {
        var zip = Release("2.0.0", "a V2pn package"u8.ToArray());
        File.WriteAllBytes(zip, "something else"u8.ToArray());

        await (Check(zip, "2.0.0") != null).Should().BeTrue();
    }

    [Test]
    public async Task AnOlderSignedReleaseUnderTheNewName_IsRefused()
    {
        var zip = Release("2.0.0", "the 1.0.0 package"u8.ToArray(), manifestVersion: "1.0.0");

        await (Check(zip, "2.0.0") != null).Should().BeTrue();
    }

    [Test]
    public async Task AManifestSignedWithAnotherKey_IsRefused()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var zip = Release("2.0.0", "a V2pn package"u8.ToArray(), signer: other);

        await (Check(zip, "2.0.0") != null).Should().BeTrue();
    }

    [Test]
    public async Task AmazToolCarriesTheReleaseKeys()
    {
        await (PackageCheck.Keys.Count > 0).Should().BeTrue();
        await PackageCheck.Keys.SequenceEqual(V2pnInfo.UpdateKeys).Should().BeTrue();
    }
}
