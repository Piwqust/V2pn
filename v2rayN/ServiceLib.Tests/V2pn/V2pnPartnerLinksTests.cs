namespace ServiceLib.Tests.V2pn;

/// <summary>A hub partner's cabinet and support links: the same rule as the hub's (hub/lib/validate.mjs).</summary>
public class V2pnPartnerLinksTests
{
    [Test]
    [Arguments("https://sub.nordline.com/cabinet", true)]
    [Arguments("https://my.sub.nordline.com/", true)]
    [Arguments("https://nordline.com/login", true)]
    [Arguments("https://lk.nordline.com/", true)]
    [Arguments("https://t.me/nordline_bot", true)]
    [Arguments("https://telegram.me/nordline_bot", true)]
    [Arguments("https://nordline.com.evil.com/", false)]
    [Arguments("https://evil.com/", false)]
    [Arguments("http://nordline.com/", false)]
    [Arguments("https://user:pass@nordline.com/", false)]
    public async Task ForSubNordlineCom(string url, bool allowed)
    {
        await V2pnPartnerLinks.Allowed(url, ["sub.nordline.com"]).Should().BeEqualTo(allowed);
    }

    [Test]
    public async Task APublicSuffix_IsNeverTheSite()
    {
        await V2pnPartnerLinks.Site("sub.nordline.co.uk").Should().BeEqualTo("nordline.co.uk");
        await V2pnPartnerLinks.Allowed("https://nordline.co.uk/", ["sub.nordline.co.uk"]).Should().BeTrue();
        await V2pnPartnerLinks.Allowed("https://x.co.uk/", ["sub.nordline.co.uk"]).Should().BeFalse();
        await (V2pnPartnerLinks.Site("co.uk") == null).Should().BeTrue();
        await V2pnPartnerLinks.Site("vpn.example.github.io").Should().BeEqualTo("example.github.io");
    }

    [Test]
    public async Task TheSuffixList_IsTheHubs()
    {
        var validate = Path.Combine(Repo(), "hub", "lib", "validate.mjs");
        if (!File.Exists(validate))
        {
            return;
        }
        var text = File.ReadAllText(validate);
        var start = text.IndexOf("export const PUBLIC_SUFFIXES", StringComparison.Ordinal);
        var end = text.IndexOf("]);", start, StringComparison.Ordinal);
        var hub = Regex.Matches(text[start..end], "'([a-z0-9.-]+)'").Select(m => m.Groups[1].Value).Order().ToList();

        await string.Join(",", hub).Should().BeEqualTo(string.Join(",", V2pnPartnerLinks.PublicSuffixes.Order()));
    }

    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "hub")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? string.Empty;
    }
}
