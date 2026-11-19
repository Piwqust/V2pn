namespace ServiceLib.Tests.V2pn;

/// <summary>
/// The installer writes InstallLocation with a trailing backslash; the app's folder comes without one.
/// </summary>
public class V2pnUninstallEntryTests
{
    [Test]
    [Arguments(@"C:\Users\me\AppData\Local\Programs\V2pn\", @"C:\Users\me\AppData\Local\Programs\V2pn", true)]
    [Arguments(@"c:\users\me\appdata\local\programs\v2pn", @"C:\Users\me\AppData\Local\Programs\V2pn\", true)]
    [Arguments(@"C:\Users\me\AppData\Local\Programs\V2pn\", @"D:\Portable\V2pn", false)]
    [Arguments(@"C:\Apps\V2pn\", @"C:\Apps\V2pn2", false)]
    public async Task SameFolder_IgnoresCaseAndTheTrailingSlash(string a, string b, bool expected)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        await V2pnUninstallEntry.SameFolder(a, b).Should().BeEqualTo(expected);
    }
}
