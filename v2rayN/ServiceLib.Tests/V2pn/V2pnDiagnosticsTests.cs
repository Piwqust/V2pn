namespace ServiceLib.Tests.V2pn;

public class V2pnDiagnosticsTests
{
    private const string Secret = "SECRET-sub.example.com-token-1234";

    private static V2pnDiagnostics.Snapshot Planted() => new()
    {
        Version = "1.2.3",
        Upstream = "7.25.6",
        Os = "Microsoft Windows 10.0.26300",
        Arch = "X64",
        Runtime = ".NET 10.0.0",
        Connected = true,
        Tun = true,
        TunStack = Secret,
        LocalPort = 10808,
        Route = "v2pn-ru-direct",
        Core = ECoreType.Xray,
        ServerType = EConfigType.VLESS,
        Transport = Secret,
        Security = "reality",
        CoreState = EV2pnCoreState.Failed,
        Failure = EV2pnFailure.StartFailed,
        FailureCause = EV2pnFailureCause.PortBusy,
        LastExitCode = 23,
        Check = new AvailabilityCheckResult(AvailabilityState.UnexpectedStatus, HttpStatus: 403),
        CheckedAt = DateTime.UtcNow.AddSeconds(-30),
        ServerCount = 3,
        Subs =
        [
            new(EV2pnSubUpdateResult.Unreachable, $"No connection could be made ({Secret}:443)", 1, 1, 0, 0, 0, 3),
            new(EV2pnSubUpdateResult.Unreachable, "HTTP 500", 1, 1, 0, 100, 50, 0),
        ],
    };

    [Test]
    public async Task FreeTextNeverReachesTheReport()
    {
        var text = V2pnDiagnostics.Format(Planted());

        await text.Should().NotContain("SECRET");
        await text.Should().NotContain("sub.example.com");
    }

    [Test]
    public async Task TheStageOfTheFailureCanBeReadFromIt()
    {
        var text = V2pnDiagnostics.Format(Planted());

        await text.Should().Contain("failure StartFailed, cause PortBusy");
        await text.Should().Contain("last exit code 23");
        await text.Should().Contain("Check: UnexpectedStatus, HTTP 403");
        await text.Should().Contain("Mode: VPN (TUN, stack other)");
        await text.Should().Contain("last update Unreachable (HTTP 500)");
        await text.Should().Contain("traffic 50% used");
    }
}
