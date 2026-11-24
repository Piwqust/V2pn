namespace ServiceLib.Tests.V2pn;

/// <summary>The device headers panels with a device limit count by (Remnawave, Happ's convention).</summary>
public class V2pnDeviceTests
{
    private static readonly Regex RemnawaveHwid = new("^[a-zA-Z0-9=-]{10,64}$");

    [Test]
    public async Task Hwid_IsWhatRemnawaveTakes_AndStable()
    {
        await RemnawaveHwid.IsMatch(V2pnDevice.Hwid).Should().BeTrue();
        await V2pnDevice.Hwid.Should().BeEqualTo(V2pnDevice.Hwid);
    }

    [Test]
    public async Task HwidFrom_HidesTheComputersOwnId()
    {
        const string machine = "6F9619FF-8B86-D011-B42D-00C04FC964FF";

        var hwid = V2pnDevice.HwidFrom(machine);

        await hwid.Contains("6f9619ff", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        await hwid.Should().BeEqualTo(V2pnDevice.HwidFrom(machine.ToLowerInvariant()));
        await (hwid != V2pnDevice.HwidFrom("another computer")).Should().BeTrue();
    }

    [Test]
    public async Task Headers_CarryTheDevice_TheSubscriptionsOwnWin()
    {
        var plain = V2pnDevice.Headers(null);
        var own = V2pnDevice.Headers(new Dictionary<string, string> { ["X-HWID"] = "my-own-device-id", ["X-Extra"] = "1" });

        await plain[V2pnDevice.HwidHeader].Should().BeEqualTo(V2pnDevice.Hwid);
        await plain.ContainsKey(V2pnDevice.OsHeader).Should().BeTrue();
        await plain.ContainsKey(V2pnDevice.OsVersionHeader).Should().BeTrue();
        await plain.ContainsKey(V2pnDevice.ModelHeader).Should().BeTrue();
        await own[V2pnDevice.HwidHeader].Should().BeEqualTo("my-own-device-id");
        await own["X-Extra"].Should().BeEqualTo("1");
    }

    [Test]
    public async Task Refusal_IsReadFromRemnawavesHeaders()
    {
        static IEnumerable<KeyValuePair<string, IEnumerable<string>>> H(params (string, string)[] pairs) =>
            pairs.Select(p => new KeyValuePair<string, IEnumerable<string>>(p.Item1, [p.Item2]));

        await V2pnDevice.Refusal(H(("x-hwid-active", "true"), ("x-hwid-max-devices-reached", "true"))).Should().BeEqualTo(EV2pnDeviceRefusal.LimitReached);
        await V2pnDevice.Refusal(H(("X-Hwid-Limit", "true"))).Should().BeEqualTo(EV2pnDeviceRefusal.LimitReached);
        await V2pnDevice.Refusal(H(("x-hwid-not-supported", "true"))).Should().BeEqualTo(EV2pnDeviceRefusal.NotAccepted);
        await V2pnDevice.Refusal(H(("x-hwid-active", "true"))).Should().BeEqualTo(EV2pnDeviceRefusal.None);
    }

    [Test]
    public async Task AnUpdateWithoutServers_SaysTheDeviceLimit_WhenThePanelSaidSo()
    {
        var (limit, _) = SubscriptionHandler.Classify(false, "", true, 200, null, EV2pnDeviceRefusal.LimitReached);
        var (notAccepted, _) = SubscriptionHandler.Classify(false, null, false, 404, null, EV2pnDeviceRefusal.NotAccepted);
        var (ok, _) = SubscriptionHandler.Classify(true, "vless://x", true, 200, null, EV2pnDeviceRefusal.LimitReached);

        await limit.Should().BeEqualTo(EV2pnSubUpdateResult.DeviceLimit);
        await notAccepted.Should().BeEqualTo(EV2pnSubUpdateResult.DeviceNotAccepted);
        await ok.Should().BeEqualTo(EV2pnSubUpdateResult.Ok);
    }
}
