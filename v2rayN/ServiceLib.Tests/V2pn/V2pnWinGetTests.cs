namespace ServiceLib.Tests.V2pn;

public class V2pnWinGetTests
{
    [Test]
    public async Task OnlyAWinGetPackageFolder_IsAWinGetInstall()
    {
        if (!Utils.IsWindows())
        {
            return;
        }
        await V2pnWinGet.IsInstall(@"C:\Users\a\AppData\Local\Microsoft\WinGet\Packages\Piwqust.V2pn_Microsoft.Winget.Source_8wekyb3d8bbwe\V2pn\").Should().BeTrue();
        await V2pnWinGet.IsInstall(@"C:\Program Files\WinGet\Packages\Piwqust.V2pn_Microsoft.Winget.Source_8wekyb3d8bbwe\V2pn\").Should().BeTrue();
        await V2pnWinGet.IsInstall(@"C:\Users\a\AppData\Local\Programs\V2pn\").Should().BeFalse();
        await V2pnWinGet.IsInstall(@"C:\Users\a\WinGet-notes\V2pn\").Should().BeFalse();
        await V2pnWinGet.UpgradeCommand.Should().BeEqualTo("winget upgrade --id Piwqust.V2pn --exact");
    }
}
