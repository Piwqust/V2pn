namespace v2rayN.Desktop.Common;

/// <summary>What Connect does in Proxy mode, shared by the main window and the settings (V2pnConnectionService).</summary>
internal static class ConnectionPrefs
{
    public static ESysProxyType ProxyMethod(Config config) => V2pnConnectionService.ProxyMethod(config);
}
