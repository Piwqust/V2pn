namespace ServiceLib.Tests.V2pn;

public class V2pnServerUsageTests
{
    private static readonly DateTime T0 = new(2026, 11, 15, 12, 0, 0);

    [Test]
    public async Task Tick_SameServer_AddsTimeAndOneSession()
    {
        var book = new ServerUsageBook();
        book.Tick("a", T0);
        book.Tick("a", T0.AddSeconds(15));
        book.Tick("a", T0.AddSeconds(30.6));

        var a = book.Get("a")!;
        await a.Sessions.Should().BeEqualTo(1);
        await a.ConnectedSeconds.Should().BeEqualTo(30L);
        await book.LastDays(1, T0)[0].Seconds.Should().BeEqualTo(30L);
    }

    [Test]
    public async Task Tick_FractionsCarryOver()
    {
        var book = new ServerUsageBook();
        book.Tick("a", T0);
        for (var i = 1; i <= 4; i++)
        {
            book.Tick("a", T0.AddSeconds(i * 1.5));
        }

        await book.Get("a")!.ConnectedSeconds.Should().BeEqualTo(6L);
    }

    [Test]
    public async Task Tick_SwitchAndBreak_StartNewSessions()
    {
        var book = new ServerUsageBook();
        book.Tick("a", T0);
        book.Tick("b", T0.AddSeconds(15));
        book.Tick("b", T0.AddSeconds(30));
        book.Tick(null, T0.AddSeconds(45));
        book.Tick("b", T0.AddSeconds(60));

        await book.Get("a")!.Sessions.Should().BeEqualTo(1);
        await book.Get("a")!.ConnectedSeconds.Should().BeEqualTo(0L);
        await book.Get("b")!.Sessions.Should().BeEqualTo(2);
        await book.Get("b")!.ConnectedSeconds.Should().BeEqualTo(15L);
    }

    [Test]
    public async Task Tick_AfterSleep_GapNotCounted()
    {
        var book = new ServerUsageBook();
        book.Tick("a", T0);
        book.Tick("a", T0.AddHours(3));

        await book.Get("a")!.ConnectedSeconds.Should().BeEqualTo(0L);
        await book.Get("a")!.Sessions.Should().BeEqualTo(2);
    }

    [Test]
    public async Task RecordTest_CountsAnswersAverageAndBest()
    {
        var book = new ServerUsageBook();
        book.RecordTest("a", 100);
        book.RecordTest("a", 300);
        book.RecordTest("a", -1);
        book.RecordTest("a", 0);

        var a = book.Get("a")!;
        await a.Tests.Should().BeEqualTo(3);
        await a.Answers.Should().BeEqualTo(2);
        await a.AverageDelay.Should().BeEqualTo(200);
        await a.BestDelay.Should().BeEqualTo(100);
    }

    [Test]
    public async Task MostReliable_NeedsTests_ThenRateThenSpeed()
    {
        var book = new ServerUsageBook();
        foreach (var d in new[] { 200, 200, 200 })
        {
            book.RecordTest("slow", d);
        }
        foreach (var d in new[] { 50, 50, 50 })
        {
            book.RecordTest("fast", d);
        }
        foreach (var d in new[] { 40, 40, -1 })
        {
            book.RecordTest("flaky", d);
        }
        book.RecordTest("once", 10);

        var ids = ServerUsageBook.MostReliable(book.Items, 5).Select(t => t.IndexId).ToList();
        await ids.Should().BeEquivalentTo(["fast", "slow", "flaky"]);
        await ids[0].Should().BeEqualTo("fast");
        await ids[2].Should().BeEqualTo("flaky");
    }

    [Test]
    public async Task LastDays_FillsMissingDaysWithZero_PruneDropsOld()
    {
        var book = new ServerUsageBook();
        book.Load([], [new UsageDayItem { Day = "2026-11-13", Seconds = 60 }, new UsageDayItem { Day = "2026-06-01", Seconds = 5 }]);

        var days = book.LastDays(3, T0);
        await days.Select(d => d.Seconds).ToList().Should().BeEquivalentTo([60L, 0L, 0L]);
        await days[0].Day.Should().BeEqualTo(new DateTime(2026, 11, 13));

        var old = book.PruneDays(T0, 90);
        await old.Should().BeEquivalentTo(["2026-06-01"]);
        await book.Day("2026-11-13").Should().NotBeNull();
    }
}
