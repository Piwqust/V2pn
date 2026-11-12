namespace ServiceLib.Tests.V2pn;

/// <summary>Only an installed copy starts with Windows, and entries that point nowhere are recognised.</summary>
public class V2pnAutostartTests
{
    private const string Temp = @"C:\Users\me\AppData\Local\Temp\";
    private const string Installed = @"C:\Users\me\AppData\Local\Programs\V2pn\V2pn.exe";

    [Test]
    [Arguments(@"C:\Users\me\AppData\Local\Temp\claude\scratch\devbuild\V2pn.exe", true)]
    [Arguments(@"C:\code\V2pn\v2rayN\v2rayN.Desktop\bin\Debug\net10.0\V2pn.exe", true)]
    [Arguments(@"C:\code\V2pn\v2rayN\v2rayN.Desktop\bin\Release\net10.0\V2pn.exe", true)]
    [Arguments(Installed, false)]
    [Arguments(@"D:\Portable\V2pn\V2pn.exe", false)]
    public async Task BuildAndTempCopies_AreThrowaway(string exe, bool expected)
    {
        await V2pnAutostart.IsThrowawayCopy(exe, Temp).Should().BeEqualTo(expected);
    }

    [Test]
    public async Task ExeOf_ReadsQuotedAndBareCommands()
    {
        await V2pnAutostart.ExeOf("\"C:\\Program Files\\V2pn\\V2pn.exe\" --autostart").Should().BeEqualTo(@"C:\Program Files\V2pn\V2pn.exe");
        await V2pnAutostart.ExeOf(@"C:\V2pn\V2pn.exe --autostart").Should().BeEqualTo(@"C:\V2pn\V2pn.exe");
        await V2pnAutostart.ExeOf("").Should().BeNull();
    }

    [Test]
    public async Task Stale_WhenTheProgramIsGone_OrThrowaway_OrThisCopyUnderTheOldName()
    {
        static bool Everything(string _) => true;
        static bool Nothing(string _) => false;
        var legacy = V2pnAutostart.LegacyName(@"C:\Users\me\AppData\Local\Programs\V2pn\");

        await V2pnAutostart.IsStale(legacy, $"\"{Installed}\"", Installed, Nothing).Should().BeTrue();
        await V2pnAutostart.IsStale("V2pnAutoRun_x", "\"C:\\code\\V2pn\\bin\\Debug\\net10.0\\V2pn.exe\"", Installed, Everything).Should().BeTrue();
        await V2pnAutostart.IsStale(legacy, $"\"{Installed}\"", Installed, Everything).Should().BeTrue();
        // This copy under the fixed name, and another installed copy, are left alone.
        await V2pnAutostart.IsStale(Global.AutoRunName, $"\"{Installed}\" --autostart", Installed, Everything).Should().BeFalse();
        await V2pnAutostart.IsStale("V2pnAutoRun_y", "\"D:\\Other\\V2pn.exe\"", Installed, Everything).Should().BeFalse();
    }
}
