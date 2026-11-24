namespace ServiceLib.Tests.V2pn;

public class V2pnDeepLinkTests
{
    [Test]
    public async Task V2pnAdd_WithEncodedUrlAndName_IsRead()
    {
        var link = V2pnDeepLink.Parse("v2pn://add?url=https%3A%2F%2Fsub.angleford.co%2Fs%2Fabc%3Fx%3D1&name=Configra")!;

        await link.SubscriptionUrl.Should().BeEqualTo("https://sub.angleford.co/s/abc?x=1");
        await link.Name.Should().BeEqualTo("Configra");
        await link.Scheme.Should().BeEqualTo("v2pn");
    }

    [Test]
    public async Task OtherClientsAddLinks_AreRead()
    {
        var happ = V2pnDeepLink.Parse("happ://add/https://sub.example.com/s/abc")!;
        await happ.SubscriptionUrl.Should().BeEqualTo("https://sub.example.com/s/abc");

        var tun = V2pnDeepLink.Parse("v2raytun://import/https%3A%2F%2Fsub.example.com%2Fs%2Fabc")!;
        await tun.SubscriptionUrl.Should().BeEqualTo("https://sub.example.com/s/abc");

        var hiddify = V2pnDeepLink.Parse("hiddify://import/https://sub.example.com/s/abc#My%20VPN")!;
        await hiddify.SubscriptionUrl.Should().BeEqualTo("https://sub.example.com/s/abc");
        await hiddify.Name.Should().BeEqualTo("My VPN");
    }

    [Test]
    public async Task AnythingElse_IsNotALink()
    {
        foreach (var text in new[]
                 {
                     "v2pn://remove?url=https://sub.example.com",
                     "v2pn://add?url=file:///C:/Windows/win.ini",
                     "v2pn://add?url=javascript:alert(1)",
                     "happ://crypt5/abcdef",
                     "vless://uuid@host:443",
                     "https://sub.example.com/s/abc",
                     "",
                     "v2pn://add?url=" + new string('a', 5000),
                 })
        {
            await (V2pnDeepLink.Parse(text) == null).Should().BeTrue();
        }
    }

    [Test]
    public async Task ALongOrControlName_IsDropped_ButTheLinkStays()
    {
        var link = V2pnDeepLink.Parse("v2pn://add?url=https%3A%2F%2Fsub.example.com%2Fs&name=" + new string('x', 80))!;
        await link.SubscriptionUrl.Should().BeEqualTo("https://sub.example.com/s");
        await (link.Name == null).Should().BeTrue();
    }

    [Test]
    [NotInParallel("v2pn-link-stash")]
    public async Task TwoQuickClicks_BothLinksReachTheRunningCopy_InOrder()
    {
        V2pnDeepLink.TakeStashed();

        V2pnDeepLink.Stash("v2pn://add?url=https%3A%2F%2Fone.example.com%2Fs");
        V2pnDeepLink.Stash("v2pn://add?url=https%3A%2F%2Ftwo.example.com%2Fs");
        var links = V2pnDeepLink.TakeStashed();

        await string.Join(",", links.Select(l => l.SubscriptionUrl))
            .Should().BeEqualTo("https://one.example.com/s,https://two.example.com/s");
        await V2pnDeepLink.TakeStashed().Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task FromArgs_FindsTheLinkAmongOtherArguments()
    {
        var link = V2pnDeepLink.FromArgs(["--autostart", "v2pn://add?url=https%3A%2F%2Fsub.example.com%2Fs"]);
        await link!.SubscriptionUrl.Should().BeEqualTo("https://sub.example.com/s");
        await (V2pnDeepLink.FromArgs(["--autostart"]) == null).Should().BeTrue();
    }
}
