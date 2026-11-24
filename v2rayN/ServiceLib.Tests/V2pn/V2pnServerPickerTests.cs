namespace ServiceLib.Tests.V2pn;

/// <summary>Which server "Fastest" means, what a quick recheck tests, and "Recent".</summary>
public class V2pnServerPickerTests
{
    private sealed record S(string Id, int Delay, bool Flaky = false);

    private static S? Fastest(params S[] servers) => V2pnServerPicker.PickFastest(servers, s => s.Delay, s => s.Flaky);

    [Test]
    public async Task Fastest_IsTheQuickestThatAnswered()
    {
        await Fastest(new("a", 90), new("b", 40), new("c", -1), new("d", 0))!.Id.Should().BeEqualTo("b");
        await (Fastest(new("a", -1), new("b", 0)) == null).Should().BeTrue();
    }

    [Test]
    public async Task Fastest_PassesOverAFlakyServer_UnlessAllAreFlaky()
    {
        await Fastest(new("quick-but-flaky", 20, Flaky: true), new("steady", 60))!.Id.Should().BeEqualTo("steady");
        await Fastest(new("a", 20, Flaky: true), new("b", 60, Flaky: true))!.Id.Should().BeEqualTo("a");
    }

    [Test]
    public async Task Candidates_AreTheQuickest_ThenFavorites_ThenRecent_EachOnce_AtMost24()
    {
        var all = Enumerable.Range(1, 40).Select(i => new S($"s{i}", i * 10)).ToList();
        var favorites = new[] { all[30], all[0] };
        var recent = new HashSet<string> { "s35", "s1" };

        var picked = V2pnServerPicker.Candidates(all, favorites, recent, s => s.Id, s => s.Delay, items => items);

        await picked.Take(12).Select(s => s.Id).SequenceEqual(Enumerable.Range(1, 12).Select(i => $"s{i}")).Should().BeTrue();
        await picked.Select(s => s.Id).Contains("s31").Should().BeTrue();
        await picked.Select(s => s.Id).Contains("s35").Should().BeTrue();
        await (picked.Count(s => s.Id == "s1") == 1).Should().BeTrue();

        var many = Enumerable.Range(1, 40).Select(i => new S($"f{i}", -1)).ToList();
        await V2pnServerPicker.Candidates(many, many, new HashSet<string>(), s => s.Id, s => s.Delay, items => items).Count.Should().BeEqualTo(24);
    }

    [Test]
    public async Task Remember_PutsTheServerFirst_KeepsEight()
    {
        var recent = Enumerable.Range(1, 8).Select(i => $"s{i}").ToList();

        await V2pnServerPicker.Remember(recent, "s5").Should().BeTrue();
        await string.Join(",", recent).Should().BeEqualTo("s5,s1,s2,s3,s4,s6,s7,s8");
        await V2pnServerPicker.Remember(recent, "s9").Should().BeTrue();
        await recent.Count.Should().BeEqualTo(8);
        await recent[0].Should().BeEqualTo("s9");
        await V2pnServerPicker.Remember(recent, "s9").Should().BeFalse();
        await V2pnServerPicker.Remember(recent, null).Should().BeFalse();
    }
}
