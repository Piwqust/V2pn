using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

/// <summary>The Russian rule sources count only when the geo files really arrived; otherwise nothing changed.</summary>
public class V2pnRussianSourcesTests
{
    [Test]
    public async Task FilesRefreshed_OnlyWhenEveryFileWasRewritten()
    {
        var dir = Path.Combine(Path.GetTempPath(), "v2pn-geo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var site = Path.Combine(dir, "geosite.dat");
            var ip = Path.Combine(dir, "geoip.dat");
            await File.WriteAllTextAsync(site, "old");
            File.SetLastWriteTimeUtc(site, DateTime.UtcNow.AddDays(-3));

            var before = V2pnRussianSources.StampFiles([site, ip]);
            // Nothing downloaded: one file unchanged, the other still missing.
            await V2pnRussianSources.FilesRefreshed(before).Should().BeFalse();

            await File.WriteAllTextAsync(ip, "new");
            await V2pnRussianSources.FilesRefreshed(before).Should().BeFalse();

            await File.WriteAllTextAsync(site, "new");
            await V2pnRussianSources.FilesRefreshed(before).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task FilesRefreshed_FalseForNoFiles()
    {
        await V2pnRussianSources.FilesRefreshed(new Dictionary<string, DateTime?>()).Should().BeFalse();
    }

    [Test]
    public async Task Restore_PutsBackSourcesAndDns()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        CoreConfigTestFactory.BindAppManagerConfig(config);
        SQLiteHelper.Instance.CreateTable<RoutingItem>();
        SQLiteHelper.Instance.CreateTable<DNSItem>();
        config.ConstItem.GeoSourceUrl = "";
        config.ConstItem.SrsSourceUrl = "";
        config.ConstItem.RouteRulesTemplateSourceUrl = "";
        var bootstrap = config.SimpleDNSItem.BootstrapDNS;

        var snapshot = await V2pnRussianSources.TakeAsync(config, []);
        config.ConstItem.GeoSourceUrl = Global.GeoFilesSources[1];
        config.ConstItem.SrsSourceUrl = Global.SingboxRulesetSources[1];
        config.ConstItem.RouteRulesTemplateSourceUrl = Global.RoutingRulesSources[1];
        config.SimpleDNSItem = new SimpleDNSItem { BootstrapDNS = "9.9.9.9" };

        await V2pnRussianSources.RestoreAsync(config, snapshot);

        await config.ConstItem.GeoSourceUrl.Should().BeEqualTo("");
        await config.ConstItem.SrsSourceUrl.Should().BeEqualTo("");
        await config.ConstItem.RouteRulesTemplateSourceUrl.Should().BeEqualTo("");
        await config.SimpleDNSItem.BootstrapDNS.Should().BeEqualTo(bootstrap);
    }
}
