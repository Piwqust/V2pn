using Microsoft.Win32;

namespace ServiceLib;

/// <summary>
/// The entry tools/installer/V2pn.iss leaves in "Installed apps" (HKCU\…\Uninstall\V2pn_is1). V2pn updates itself
/// without the installer, so at every start it writes its own version there; otherwise Windows (and winget) would go
/// on showing the version that was first installed.
/// </summary>
public static class V2pnUninstallEntry
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\V2pn_is1";

    public static void Refresh()
    {
        if (!Utils.IsWindows() || !V2pnInfo.IsRelease)
        {
            return;
        }
        try
        {
            using var entry = Registry.CurrentUser.OpenSubKey(Key, true);
            // Only the entry of this copy: another folder's V2pn has an installer of its own.
            if (entry?.GetValue("InstallLocation") is not string location
                || !SameFolder(location, Utils.StartupPath()))
            {
                return;
            }
            var version = V2pnInfo.Version;
            if (entry.GetValue("DisplayVersion") as string != version)
            {
                entry.SetValue("DisplayVersion", version);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnUninstallEntry), ex);
        }
    }

    public static bool SameFolder(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
