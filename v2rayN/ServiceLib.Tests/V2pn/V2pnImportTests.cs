namespace ServiceLib.Tests.V2pn;

public class V2pnImportTests
{
    private static string NewV2rayN(int servers, int subscriptions, bool withExe = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "v2rayN-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "guiConfigs"));
        if (withExe)
        {
            File.WriteAllText(Path.Combine(root, "v2rayN.exe"), string.Empty);
        }
        using var db = new SQLiteConnection(Path.Combine(root, "guiConfigs", "guiNDB.db"), false);
        db.CreateTable<ProfileItem>();
        db.CreateTable<SubItem>();
        for (var i = 0; i < servers; i++)
        {
            db.Insert(new ProfileItem { IndexId = Guid.NewGuid().ToString("N"), Remarks = $"server {i}" });
        }
        for (var i = 0; i < subscriptions; i++)
        {
            db.Insert(new SubItem { Id = Guid.NewGuid().ToString("N"), Remarks = $"sub {i}" });
        }
        return root;
    }

    [Test]
    public async Task Describe_CountsServersAndSubscriptions()
    {
        var root = NewV2rayN(3, 1);
        try
        {
            var install = V2pnImport.Describe(root);
            await install.Servers.Should().BeEqualTo(3);
            await install.Subscriptions.Should().BeEqualTo(1);
            await install.ConfigDir.Should().BeEqualTo(Path.Combine(root, "guiConfigs"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task DataIsNextToTheExe_WhenItIsThere()
    {
        var root = NewV2rayN(1, 0);
        try
        {
            await V2pnImport.HasData(root).Should().BeTrue();
            await V2pnImport.DataDirOf(Path.Combine(root, "v2rayN.exe")).Should().BeEqualTo(root);
            await V2pnImport.HasData(Path.Combine(root, "guiConfigs")).Should().BeFalse();
            await V2pnImport.HasData(null).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task Find_NeverOffersV2pnItself()
    {
        var own = Path.GetFullPath(Utils.StartupPath()).TrimEnd(Path.DirectorySeparatorChar);
        foreach (var install in V2pnImport.Find())
        {
            await V2pnImport.HasData(install.DataDir).Should().BeTrue();
            await string.Equals(install.DataDir, own, StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        }
    }
}
