namespace ServiceLib.Tests.V2pn;

public class V2pnNetworkWatchTests
{
    [Test]
    public async Task ABurstOfEventsEndsInOneCheck()
    {
        var reasons = new List<string>();
        using var watch = new V2pnNetworkWatch(r => { lock (reasons) { reasons.Add(r); } return Task.CompletedTask; },
            TimeSpan.FromMilliseconds(150));

        watch.Signal("network lost");
        await Task.Delay(40);
        watch.Signal("network address changed");
        await Task.Delay(40);
        watch.Signal("network is back");
        await Task.Delay(500);

        await reasons.Count.Should().BeEqualTo(1);
        await reasons[0].Should().BeEqualTo("network lost");
    }

    [Test]
    public async Task EventsAfterTheQuietPeriodCheckAgain()
    {
        var count = 0;
        using var watch = new V2pnNetworkWatch(_ => { Interlocked.Increment(ref count); return Task.CompletedTask; },
            TimeSpan.FromMilliseconds(50));

        watch.Signal("a");
        await Task.Delay(300);
        watch.Signal("b");
        await Task.Delay(300);

        await count.Should().BeEqualTo(2);
    }

    [Test]
    public async Task ChecksNeverOverlap()
    {
        var running = 0;
        var overlapped = false;
        var finished = 0;
        using var watch = new V2pnNetworkWatch(async _ =>
        {
            if (Interlocked.Increment(ref running) > 1)
            {
                overlapped = true;
            }
            await Task.Delay(200);
            Interlocked.Decrement(ref running);
            Interlocked.Increment(ref finished);
        }, TimeSpan.FromMilliseconds(20));

        watch.Signal("a");
        await Task.Delay(80);
        watch.Signal("b");
        await Task.Delay(700);

        await overlapped.Should().BeFalse();
        await finished.Should().BeEqualTo(2);
    }

    [Test]
    public async Task ATimerGapMeansTheComputerSlept()
    {
        var reasons = new List<string>();
        using var watch = new V2pnNetworkWatch(r => { lock (reasons) { reasons.Add(r); } return Task.CompletedTask; },
            TimeSpan.FromMilliseconds(20));
        var start = DateTime.UtcNow;

        watch.CheckForWake(start);
        watch.CheckForWake(start.AddSeconds(5));
        await Task.Delay(150);
        await reasons.Count.Should().BeEqualTo(0);

        watch.CheckForWake(start.AddMinutes(45));
        await Task.Delay(150);
        await reasons.Count.Should().BeEqualTo(1);
        await reasons[0].Should().StartWith("woke");
    }
}
