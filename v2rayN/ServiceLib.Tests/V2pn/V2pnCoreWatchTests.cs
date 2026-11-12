namespace ServiceLib.Tests.V2pn;

/// <summary>A core that dies is restarted with growing pauses, and not forever.</summary>
public class V2pnCoreWatchTests
{
    private static readonly DateTime _t0 = new(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Pauses_Grow_ThenItGivesUp()
    {
        var policy = new V2pnRestartPolicy();
        var pauses = new List<double?>();
        for (var i = 0; i <= policy.MaxAttempts; i++)
        {
            pauses.Add(policy.NextDelay(_t0.AddSeconds(i))?.TotalSeconds);
        }

        await pauses.Take(policy.MaxAttempts).All(p => p > 0).Should().BeTrue();
        await pauses.Take(policy.MaxAttempts).SequenceEqual(pauses.Take(policy.MaxAttempts).OrderBy(p => p)).Should().BeTrue();
        await pauses.Last().HasValue.Should().BeFalse();
    }

    [Test]
    public async Task AMinuteOfUptime_StartsTheCountAgain()
    {
        var policy = new V2pnRestartPolicy();
        for (var i = 0; i < policy.MaxAttempts; i++)
        {
            policy.NextDelay(_t0);
        }
        policy.Started(_t0);

        var afterLongRun = policy.NextDelay(_t0.AddSeconds(61));

        await afterLongRun.HasValue.Should().BeTrue();
        await afterLongRun!.Value.TotalSeconds.Should().BeEqualTo(1);
    }

    [Test]
    public async Task AShortRun_DoesNotStartTheCountAgain()
    {
        var policy = new V2pnRestartPolicy();
        for (var i = 0; i < policy.MaxAttempts; i++)
        {
            policy.NextDelay(_t0);
            policy.Started(_t0);
        }

        await policy.NextDelay(_t0.AddSeconds(5)).HasValue.Should().BeFalse();
    }

    [Test]
    public async Task AUserConnect_StartsTheCountAgain()
    {
        var policy = new V2pnRestartPolicy();
        for (var i = 0; i < policy.MaxAttempts; i++)
        {
            policy.NextDelay(_t0);
        }
        policy.Reset();

        await policy.NextDelay(_t0).HasValue.Should().BeTrue();
    }
}
