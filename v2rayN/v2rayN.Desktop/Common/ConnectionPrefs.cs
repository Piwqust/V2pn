namespace v2rayN.Desktop.Common;

/// <summary>What Connect does in Proxy mode, shared by the main window and the settings.</summary>
internal static class ConnectionPrefs
{
    /// <summary>
    /// The saved choice; on first use it is taken from the mode v2rayN had (On, PAC or Keep),
    /// so an upgrade keeps behaving the same.
    /// </summary>
    public static ESysProxyType ProxyMethod(Config config)
    {
        var method = config.UiItem.V2pnProxyMethod ??= config.SystemProxyItem.SysProxyType switch
        {
            ESysProxyType.Pac or ESysProxyType.Unchanged => config.SystemProxyItem.SysProxyType,
            _ => ESysProxyType.ForcedChange,
        };
        if (method == ESysProxyType.ForcedClear || (method == ESysProxyType.Pac && !Utils.IsWindows()))
        {
            method = ESysProxyType.ForcedChange;
        }
        return method;
    }
}
