namespace ServiceLib.Tests.V2pn;

/// <summary>
/// Whose system proxy a Mac has, read from "scutil --proxy". The first sample is the real output of
/// a Mac that runs v2rayN on port 10808.
/// </summary>
public class V2pnMacProxyTests
{
    private static string Scutil(params string[] lines) =>
        "<dictionary> {\n" + string.Join("\n", lines.Select(l => "  " + l)) + "\n}\n";

    private static readonly string AnotherClientOn10808 = Scutil(
        "ExceptionsList : <array> {",
        "  0 : localhost",
        "  1 : 127.0.0.0/8",
        "}",
        "FTPPassive : 1",
        "HTTPEnable : 1",
        "HTTPPort : 10808",
        "HTTPProxy : 127.0.0.1",
        "HTTPSEnable : 1",
        "HTTPSPort : 10808",
        "HTTPSProxy : 127.0.0.1",
        "SOCKSEnable : 1",
        "SOCKSPort : 10808",
        "SOCKSProxy : 127.0.0.1");

    [Test]
    public async Task ProxyOnAnotherClientsPort_IsNotOurs()
    {
        await V2pnConnection.MacProxyIsOurs(AnotherClientOn10808, 20808).Should().BeFalse();
    }

    [Test]
    public async Task ProxyOnOurPort_IsOurs()
    {
        await V2pnConnection.MacProxyIsOurs(AnotherClientOn10808, 10808).Should().BeTrue();
    }

    [Test]
    public async Task NoProxy_OrOnlySwitchedOffEntries_MayBeCleared()
    {
        await V2pnConnection.MacProxyIsOurs(Scutil("FTPPassive : 1"), 10808).Should().BeTrue();
        await V2pnConnection.MacProxyIsOurs(Scutil("HTTPEnable : 0", "HTTPPort : 8080", "HTTPProxy : proxy.corp"), 10808).Should().BeTrue();
    }

    [Test]
    public async Task ProxyOnAnotherMachine_IsNotOurs_EvenOnOurPort()
    {
        await V2pnConnection.MacProxyIsOurs(Scutil("HTTPEnable : 1", "HTTPPort : 10808", "HTTPProxy : 10.0.0.5"), 10808).Should().BeFalse();
    }

    [Test]
    public async Task OneForeignEntryAmongOurs_KeepsTheProxy()
    {
        var mixed = Scutil(
            "HTTPEnable : 1", "HTTPPort : 10808", "HTTPProxy : 127.0.0.1",
            "SOCKSEnable : 1", "SOCKSPort : 1080", "SOCKSProxy : 127.0.0.1");

        await V2pnConnection.MacProxyIsOurs(mixed, 10808).Should().BeFalse();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("scutil: command not found")]
    public async Task UnreadableOutput_KeepsTheOldBehaviour(string? output)
    {
        await V2pnConnection.MacProxyIsOurs(output, 10808).Should().BeTrue();
    }
}
