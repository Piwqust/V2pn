namespace ServiceLib.Tests.V2pn;

public class V2pnNetworkWatchTests
{
    // CI machines can be slow: wait for the expected state with room to spare, not a fixed pause.
    private static async Task WaitUntil(Func<bool> done, int ms = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (!done() && DateTime.UtcNow < until)
        {
            await Task.Delay(20);
        }
    }

    [Test]
    public async Task ABurstOfEventsEndsInOneCheck()
    {
        var reasons = new List<string>();
        using var watch = new V2pnNetworkWatch(r => { lock (reasons) { reasons.Add(r); } return Task.CompletedTask; },
            TimeSpan.FromMilliseconds(600));

        watch.Signal("network lost");
        await Task.Delay(40);
        watch.Signal("network address changed");
        await Task.Delay(40);
        watch.Signal("network is back");
        await WaitUntil(() => { lock (reasons) { return reasons.Count > 0; } });
        await Task.Delay(300);

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
        await WaitUntil(() => Volatile.Read(ref count) == 1);
        watch.Signal("b");
        await WaitUntil(() => Volatile.Read(ref count) == 2);

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
        // The second signal comes while the first check runs. A fixed pause was not enough on a busy CI machine:
        // when the first check had not started yet, both signals rightly became one check.
        await WaitUntil(() => Volatile.Read(ref running) == 1);
        watch.Signal("b");
        await WaitUntil(() => Volatile.Read(ref finished) == 2);

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
        await WaitUntil(() => { lock (reasons) { return reasons.Count > 0; } });
        await reasons.Count.Should().BeEqualTo(1);
        await reasons[0].Should().StartWith("woke");
    }
}
