namespace ServiceLib.Tests.V2pn;

/// <summary>When V2pn switches to a working server by itself.</summary>
public class V2pnAutoSwitchTests
{
    private static readonly DateTime T0 = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly AvailabilityCheckResult Ok = new(AvailabilityState.Available, 120);
    private static readonly AvailabilityCheckResult NoAnswer = new(AvailabilityState.NoResponse);
    private static readonly AvailabilityCheckResult Redirected = new(AvailabilityState.Redirected);

    [Test]
    public async Task OneFailure_IsCheckedAgain_TheSecondSearches()
    {
        var auto = new V2pnAutoSwitch();

        await auto.OnResult(true, true, "s1", NoAnswer, T0).Should().BeEqualTo(EV2pnAutoAction.RecheckSoon);
        await auto.OnResult(true, true, "s1", NoAnswer, T0.AddSeconds(15)).Should().BeEqualTo(EV2pnAutoAction.Search);
    }

    [Test]
    public async Task AnAnswerInBetween_StartsTheCountAgain()
    {
        var auto = new V2pnAutoSwitch();

        auto.OnResult(true, true, "s1", NoAnswer, T0);
        auto.OnResult(true, true, "s1", Ok, T0.AddSeconds(15));

        await auto.OnResult(true, true, "s1", NoAnswer, T0.AddMinutes(5)).Should().BeEqualTo(EV2pnAutoAction.RecheckSoon);
    }

    [Test]
    public async Task AfterASearch_TheNextWaitsForTheCooldown()
    {
        var auto = new V2pnAutoSwitch();
        auto.OnResult(true, true, "s1", NoAnswer, T0);
        auto.OnResult(true, true, "s1", NoAnswer, T0);
        auto.Searched(T0);

        auto.OnResult(true, true, "s2", NoAnswer, T0.AddMinutes(1));
        await auto.OnResult(true, true, "s2", NoAnswer, T0.AddMinutes(2)).Should().BeEqualTo(EV2pnAutoAction.None);
        auto.OnResult(true, true, "s2", NoAnswer, T0.AddMinutes(11));
        await auto.OnResult(true, true, "s2", NoAnswer, T0.AddMinutes(11)).Should().BeEqualTo(EV2pnAutoAction.Search);
    }

    [Test]
    public async Task Nothing_WhenOff_Disconnected_OrNotTheServersFault()
    {
        var auto = new V2pnAutoSwitch();

        await auto.OnResult(false, true, "s1", NoAnswer, T0).Should().BeEqualTo(EV2pnAutoAction.None);
        await auto.OnResult(true, false, "s1", NoAnswer, T0).Should().BeEqualTo(EV2pnAutoAction.None);
        // A captive portal or a broken check address is not the server's fault.
        await auto.OnResult(true, true, "s1", Redirected, T0).Should().BeEqualTo(EV2pnAutoAction.None);
    }

    [Test]
    public async Task AnotherServer_StartsItsOwnCount()
    {
        var auto = new V2pnAutoSwitch();
        auto.OnResult(true, true, "s1", NoAnswer, T0);

        await auto.OnResult(true, true, "s2", NoAnswer, T0.AddSeconds(5)).Should().BeEqualTo(EV2pnAutoAction.RecheckSoon);
    }

    [Test]
    public async Task On_UntilTurnedOff()
    {
        var config = new Config { UiItem = new UIItem() };

        await V2pnAutoSwitch.Enabled(config).Should().BeTrue();
        config.UiItem.V2pnAutoSwitch = false;
        await V2pnAutoSwitch.Enabled(config).Should().BeFalse();
    }
}
