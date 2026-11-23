namespace ServiceLib.Tests.V2pn;

/// <summary>What V2pn does differently on a Mac and can be checked without one: the sign-in agent, the About panel's text, core versions.</summary>
public class V2pnMacTests
{
    [Test]
    public async Task SignInAgent_StartsV2pnItself_NotAShell()
    {
        var plist = V2pnMacAutostart.Plist("/Applications/V2pn.app/Contents/MacOS/V2pn");

        // System Settings names a login item after its program: "sh" says nothing, "V2pn" does.
        await plist.Contains("/bin/sh").Should().BeFalse();
        await plist.Contains("pgrep").Should().BeFalse();
        await plist.Contains("<string>/Applications/V2pn.app/Contents/MacOS/V2pn</string>").Should().BeTrue();
        await plist.Contains($"<string>{Global.AutostartArg}</string>").Should().BeTrue();
        await plist.Contains($"<string>{V2pnMacAutostart.BundleId}</string>").Should().BeTrue();
        await plist.Contains("<key>RunAtLoad</key>").Should().BeTrue();
        await plist.StartsWith("<?xml").Should().BeTrue();
        await plist.Contains('\r').Should().BeFalse();
    }

    [Test]
    public async Task SignInAgent_EscapesThePath()
    {
        var plist = V2pnMacAutostart.Plist("/Users/a&b/<V2pn>.app/Contents/MacOS/V2pn");

        await plist.Contains("/Users/a&amp;b/&lt;V2pn&gt;.app/Contents/MacOS/V2pn").Should().BeTrue();
        await plist.Contains("a&b").Should().BeFalse();
    }

    [Test]
    [Arguments("/Volumes/V2pn/V2pn.app/Contents/MacOS/V2pn", true)]
    [Arguments("/private/var/folders/x/T/AppTranslocation/1234/d/V2pn.app/Contents/MacOS/V2pn", true)]
    [Arguments("", true)]
    [Arguments("/Applications/V2pn.app/Contents/MacOS/V2pn", false)]
    [Arguments("/Users/me/Applications/V2pn.app/Contents/MacOS/V2pn", false)]
    public async Task SignInAgent_IsNotWrittenForACopyThatWillBeGone(string exe, bool temporary)
    {
        await V2pnMacAutostart.IsTemporaryCopy(exe).Should().BeEqualTo(temporary);
    }

    [Test]
    [Arguments("1", true)]
    [Arguments("", false)]
    [Arguments("0", false)]
    public async Task SignInAgent_BelongsOnlyToTheCopyThatKeepsItsDataInTheUsersFolder(string flag, bool mayOwn)
    {
        // A test copy with its data beside itself must never replace the installed app's sign-in entry.
        await V2pnMacAutostart.MayOwnAgent(flag).Should().BeEqualTo(mayOwn);
    }

    [Test]
    public async Task SignInAgent_LivesInTheUsersLaunchAgents()
    {
        await V2pnMacAutostart.AgentPath("/Users/me").Replace(Path.DirectorySeparatorChar, '/').Should().BeEqualTo("/Users/me/Library/LaunchAgents/V2pn-LaunchAgent.plist");
    }

    [Test]
    public async Task Credits_AreAsciiRtf_WithLinksAndOtherAlphabets()
    {
        var rtf = V2pnCredits.Rtf(
        [
            ["Ядра: Xray 26.1.2"],
            [new V2pnCredits.Link("Сайт", "https://v2pn.angleford.co"), "  ·  ", @"a{b}\c"],
        ]);

        await rtf.All(c => c < 128).Should().BeTrue();
        await rtf.StartsWith(@"{\rtf1").Should().BeTrue();
        await rtf.EndsWith("}").Should().BeTrue();
        // "Я" is U+042F: 1071.
        await rtf.Contains(@"\u" + "1071 ").Should().BeTrue();
        await rtf.Contains(@"HYPERLINK ""https://v2pn.angleford.co""").Should().BeTrue();
        await rtf.Contains(@"\line ").Should().BeTrue();
        await rtf.Contains(@"a\{b\}\\c").Should().BeTrue();
        // "·" is U+00B7.
        await rtf.Contains(@"\u" + "183 ").Should().BeTrue();
    }

    [Test]
    public async Task Credits_WriteCharactersAboveTheSignedRangeAsNegativeNumbers()
    {
        // RTF's \u takes a signed 16-bit number: U+FF01 is -255.
        await V2pnCredits.Escape(((char)0xFF01).ToString()).Should().BeEqualTo(@"\u" + "-255 ");
    }

    [Test]
    [Arguments("Xray 26.11.9 (Xray, Penetrates Everything.) 676eaf6 (go1.27.3 darwin/arm64)", "26.11.9")]
    [Arguments("sing-box version 1.14.4\n\nEnvironment: go1.27 darwin/arm64", "1.14.4")]
    [Arguments("sing-box version 1.15.0-beta.3", "1.15.0-beta.3")]
    [Arguments("v2.1.0", "2.1.0")]
    public async Task CoreVersion_IsReadFromWhatTheCorePrints(string output, string version)
    {
        await V2pnCores.ParseVersion(output).Should().BeEqualTo(version);
    }

    [Test]
    [Arguments("")]
    [Arguments(null)]
    [Arguments("command not found")]
    public async Task CoreVersion_IsUnknownWhenTheCoreSaysNothingUseful(string? output)
    {
        await V2pnCores.ParseVersion(output).Should().BeNull();
    }
}
