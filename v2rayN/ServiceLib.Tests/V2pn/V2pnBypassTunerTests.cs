using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

[NotInParallel(nameof(V2pnBypass))]
public class V2pnBypassTunerTests
{
    private static ProfileItem Reality() => new()
    {
        IndexId = "srv",
        ConfigType = EConfigType.VLESS,
        StreamSecurity = Global.StreamSecurityReality,
        Network = nameof(ETransport.raw),
    };

    private static Config Config()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        config.V2pnBypassItem = new V2pnBypassItem { Enabled = true, PerNetwork = false };
        return config;
    }

    private static Task NoWait(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    [Test]
    public async Task KeepsTheFirstWayThatWorks()
    {
        var config = Config();
        var connectedWith = new List<string?>();
        Task Reload()
        {
            connectedWith.Add(V2pnBypassPlan.From(config).For(Reality())?.Id);
            return Task.CompletedTask;
        }
        Task<bool> Works(CancellationToken _) => Task.FromResult(connectedWith[^1] == "fragment-fine");

        var outcome = await V2pnBypassTuner.TuneAsync(config, Reality(), ECoreType.Xray, Reload, Works, delay: NoWait);

        await outcome.Worked.Should().BeTrue();
        await outcome.Variant!.Id.Should().BeEqualTo("fragment-fine");
        await connectedWith.Should().BeEquivalentTo(["fragment-tcp", "fragment-fine"]);
        await V2pnBypass.Learned(config, "srv", V2pnBypass.AnyNetwork)!.Id.Should().BeEqualTo("fragment-fine");
        await V2pnBypass.Trial.Should().BeNull();
    }

    [Test]
    public async Task NothingWorks_ReconnectsAsBefore()
    {
        var config = Config();
        var connectedWith = new List<string?>();
        Task Reload()
        {
            connectedWith.Add(V2pnBypassPlan.From(config).For(Reality())?.Id);
            return Task.CompletedTask;
        }

        var outcome = await V2pnBypassTuner.TuneAsync(config, Reality(), ECoreType.Xray, Reload, _ => Task.FromResult(false), delay: NoWait);

        await outcome.Worked.Should().BeFalse();
        await outcome.Tried.Should().BeEqualTo(2);
        await connectedWith.Should().BeEquivalentTo(["fragment-tcp", "fragment-fine", null]);
        await (config.V2pnBypassItem!.Learned ?? []).Should().BeEmpty();
    }

    [Test]
    public async Task KeptWayStoppedWorking_TriesWithoutOneLast()
    {
        var config = Config();
        V2pnBypass.Remember(config, "srv", V2pnBypass.AnyNetwork, "fragment-tcp");
        var connectedWith = new List<string?>();
        Task Reload()
        {
            connectedWith.Add(V2pnBypassPlan.From(config).For(Reality())?.Id);
            return Task.CompletedTask;
        }
        Task<bool> Works(CancellationToken _) => Task.FromResult(connectedWith[^1] == null);

        var outcome = await V2pnBypassTuner.TuneAsync(config, Reality(), ECoreType.Xray, Reload, Works, delay: NoWait);

        await outcome.Worked.Should().BeTrue();
        await outcome.Variant.Should().BeNull();
        await connectedWith.Should().BeEquivalentTo(["fragment-fine", null]);
        await V2pnBypass.Learned(config, "srv", V2pnBypass.AnyNetwork).Should().BeNull();
    }

    [Test]
    public async Task RussianEntry_ByCountryOrName()
    {
        await V2pnRussianEntry.LooksLikeEntry(new ProfileItem { Remarks = "Москва" }, "RU").Should().BeTrue();
        await V2pnRussianEntry.LooksLikeEntry(new ProfileItem { Remarks = "🇷🇺 → 🇩🇪 Германия" }, "DE").Should().BeTrue();
        await V2pnRussianEntry.LooksLikeEntry(new ProfileItem { Remarks = "Обход белых списков" }, null).Should().BeTrue();
        await V2pnRussianEntry.LooksLikeEntry(new ProfileItem { Remarks = "Германия 1" }, "DE").Should().BeFalse();
    }
}
