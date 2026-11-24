namespace ServiceLib.Tests.V2pn;

/// <summary>Which running programs "Add a running program…" offers, and a Mac app's program name.</summary>
public class V2pnRunningProgramsTests
{
    private static V2pnRunningProgram P(string process, string? path, bool window = false) => new(process, process, path, window);

    [Test]
    public async Task Pick_Windows_LeavesOutSystemAndV2pnAndNamelessOnes_OneEntryPerProgram_WindowsFirst()
    {
        List<V2pnRunningProgram> all =
        [
            P("svchost.exe", @"C:\Windows\System32\svchost.exe"),
            P("explorer.exe", @"C:\Windows\explorer.exe", window: true),
            P("sing-box.exe", @"C:\Users\me\AppData\Local\Programs\V2pn\bin\sing_box\sing-box.exe"),
            P("V2pn.exe", @"C:\Users\me\AppData\Local\Programs\V2pn\V2pn.exe", window: true),
            P("System", null),
            P("steamwebhelper.exe", @"C:\Program Files (x86)\Steam\bin\cef\steamwebhelper.exe"),
            P("Telegram.exe", @"C:\Users\me\AppData\Roaming\Telegram Desktop\Telegram.exe"),
            P("telegram.exe", @"C:\Users\me\AppData\Roaming\Telegram Desktop\Telegram.exe", window: true),
            P("Taskmgr.exe", null, window: true),
            P("WindowsApps.exe", @"C:\WindowsApps\x.exe"),
        ];

        var picked = V2pnRunningPrograms.Pick(all, @"C:\Windows", @"C:\Users\me\AppData\Local\Programs\V2pn\", mac: false);

        await picked.Select(p => p.Process).Should().BeEquivalentTo(["Taskmgr.exe", "telegram.exe", "steamwebhelper.exe", "WindowsApps.exe"]);
        await picked[0].HasWindow.Should().BeTrue();
        await picked[1].HasWindow.Should().BeTrue();
    }

    [Test]
    public async Task Pick_Mac_KeepsAppsNotTheirHelpersNorTheSystem_TitleIsTheBundleName()
    {
        List<V2pnRunningProgram> all =
        [
            P("Telegram", "/Applications/Telegram.app/Contents/MacOS/Telegram"),
            P("Google Chrome Helper", "/Applications/Google Chrome.app/Contents/Frameworks/Google Chrome Framework.framework/Versions/1/Helpers/Google Chrome Helper.app/Contents/MacOS/Google Chrome Helper"),
            P("Finder", "/System/Library/CoreServices/Finder.app/Contents/MacOS/Finder"),
            P("launchd", "/sbin/launchd"),
            P("V2pn", "/Applications/V2pn.app/Contents/MacOS/V2pn"),
            P("SberBankOnline", "/Users/me/Applications/Sber Online.app/Contents/MacOS/SberBankOnline"),
        ];

        var picked = V2pnRunningPrograms.Pick(all, null, "/Applications/V2pn.app/Contents/MacOS/", mac: true);

        await picked.Select(p => p.Process).Should().BeEquivalentTo(["SberBankOnline", "Telegram"]);
        await picked[0].Title.Should().BeEqualTo("Sber Online");
    }

    [Test]
    [Arguments("/Applications/Telegram.app/Contents/MacOS/Telegram", "/Applications/Telegram.app")]
    [Arguments("/Applications/A.app/Contents/Frameworks/B.app/Contents/MacOS/B", null)]
    [Arguments("/usr/bin/curl", null)]
    public async Task MacBundleOf_FindsTheAppAProgramRunsFrom(string path, string? expected)
    {
        await V2pnRunningPrograms.MacBundleOf(path).Should().BeEqualTo(expected);
    }

    [Test]
    public async Task MacExecutable_ReadsInfoPlist_OrTheOnlyProgramInTheBundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2pn-apps-" + Guid.NewGuid().ToString("N"));
        try
        {
            var xml = Bundle(root, "Sber Online.app", ["SberBankOnline", "helper-tool"]);
            File.WriteAllText(Path.Combine(xml, "Contents", "Info.plist"), """
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>CFBundleName</key>
                    <string>Sber Online</string>
                    <key>CFBundleExecutable</key>
                    <string>SberBankOnline</string>
                </dict>
                </plist>
                """);
            var binary = Bundle(root, "Telegram.app", ["Telegram"]);
            File.WriteAllBytes(Path.Combine(binary, "Contents", "Info.plist"), "bplist00\u00d1\u0001\u0002"u8.ToArray());

            await V2pnRunningPrograms.MacExecutable(xml).Should().BeEqualTo("SberBankOnline");
            await V2pnRunningPrograms.MacExecutable(xml + "/").Should().BeEqualTo("SberBankOnline");
            await V2pnRunningPrograms.MacExecutable(binary).Should().BeEqualTo("Telegram");
            await V2pnRunningPrograms.MacExecutable(Path.Combine(root, "Missing.app")).Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Bundle(string root, string name, string[] programs)
    {
        var bundle = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(bundle, "Contents", "MacOS"));
        foreach (var program in programs)
        {
            File.WriteAllText(Path.Combine(bundle, "Contents", "MacOS", program), string.Empty);
        }
        return bundle;
    }
}
