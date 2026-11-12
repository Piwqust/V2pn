namespace ServiceLib.Tests.V2pn;

/// <summary>Disconnect clears the Windows proxy only when V2pn is the one that set it.</summary>
public class V2pnConnectionTests
{
    [Test]
    [Arguments(false, null, null)]
    [Arguments(false, "127.0.0.1:10808", "")]
    [Arguments(true, "127.0.0.1:20808", null)]
    [Arguments(true, "http=127.0.0.1:20808;https=127.0.0.1:20808", null)]
    [Arguments(true, "localhost:20808", null)]
    [Arguments(false, "", "http://127.0.0.1:20810/pac?t=1")]
    public async Task IsOurs_WhenNothingIsSetOrItPointsAtV2pn(bool enabled, string? server, string? pac)
    {
        await V2pnConnection.IsOurs(enabled, server, pac, 20808, 20810).Should().BeTrue();
    }

    [Test]
    [Arguments(true, "127.0.0.1:10808", null)]
    [Arguments(true, "proxy.corp.local:8080", null)]
    [Arguments(true, "127.0.0.1:208080", null)]
    [Arguments(false, "", "http://127.0.0.1:7890/pac")]
    [Arguments(true, "127.0.0.1:10808", "http://127.0.0.1:20810/pac")]
    public async Task IsNotOurs_WhenAnotherAppSetIt(bool enabled, string? server, string? pac)
    {
        await V2pnConnection.IsOurs(enabled, server, pac, 20808, 20810).Should().BeFalse();
    }

    [Test]
    public async Task LocalPortBusy_SeesAListenerOnLoopback()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            await V2pnConnection.LocalPortBusy(port).Should().BeTrue();
        }
        finally
        {
            listener.Stop();
        }
        await V2pnConnection.LocalPortBusy(port).Should().BeFalse();
        await V2pnConnection.LocalPortBusy(0).Should().BeFalse();
    }

    [Test]
    public async Task HasAnyServer_SeesAStoredServer()
    {
        var config = ServiceLib.Tests.CoreConfig.CoreConfigTestFactory.CreateConfig();
        ServiceLib.Tests.CoreConfig.CoreConfigTestFactory.BindAppManagerConfig(config);
        SQLiteHelper.Instance.CreateTable<ProfileItem>();
        var profile = ServiceLib.Tests.CoreConfig.CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray, $"profile-{Guid.NewGuid():N}", "any");
        await SQLiteHelper.Instance.ReplaceAsync(profile);
        try
        {
            await (await V2pnConnection.HasAnyServerAsync()).Should().BeTrue();
        }
        finally
        {
            await SQLiteHelper.Instance.DeleteAsync(profile);
        }
    }
}
