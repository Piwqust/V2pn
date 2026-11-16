namespace ServiceLib.Tests.V2pn;

public class V2pnPresetTests
{
    [Test]
    public async Task Parse_TakesPlainAndAddLinks_SkipsTheRest()
    {
        var links = V2pnPreset.Parse("\uFEFF# Configra\r\nhttps://sub.example.com/s/abc\r\n\r\nv2pn://add?url=https%3A%2F%2Fsub.example.com%2Fs%2Fdef&name=X\nvless://uuid@host:443\nhttps://sub.example.com/s/abc\nfile:///C:/x\n");

        await links.Should().BeEquivalentTo(new[] { "https://sub.example.com/s/abc", "https://sub.example.com/s/def" });
    }

    [Test]
    public async Task Pending_IsEachLinkOnce()
    {
        var app = Directory.CreateTempSubdirectory().FullName;
        var config = Directory.CreateTempSubdirectory().FullName;
        try
        {
            await V2pnPreset.Pending(app, config).Should().BeEmpty();

            File.WriteAllText(Path.Combine(app, V2pnPreset.FileName), "https://sub.example.com/s/abc\n");
            await V2pnPreset.Pending(app, config).Should().BeEquivalentTo(new[] { "https://sub.example.com/s/abc" });

            V2pnPreset.MarkApplied(["https://sub.example.com/s/abc"], config);
            await V2pnPreset.Pending(app, config).Should().BeEmpty();

            // A newer build over the same folder brings another link: only that one is new.
            File.WriteAllText(Path.Combine(app, V2pnPreset.FileName), "https://sub.example.com/s/abc\nhttps://sub.example.com/s/new\n");
            await V2pnPreset.Pending(app, config).Should().BeEquivalentTo(new[] { "https://sub.example.com/s/new" });
        }
        finally
        {
            Directory.Delete(app, true);
            Directory.Delete(config, true);
        }
    }
}
