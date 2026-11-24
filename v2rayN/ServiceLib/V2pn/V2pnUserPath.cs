using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ServiceLib;

/// <summary>
/// V2pn: v2pnctl in every new terminal. The Windows installer puts V2pn's folder on the user's PATH, but V2pn updates
/// itself without the installer and install.ps1 never did, so a copy that came by an update had v2pnctl in its folder
/// and no terminal found it. At start, the installed copy (the folder the installer and install.ps1 install into) adds
/// its folder to the user's PATH once, the way the installer does: HKCU\Environment, the other entries kept as stored.
/// Once is remembered in HKCU\Software\V2pn, so a person who takes the folder off again is not overruled. Portable
/// copies, sandboxes, winget's copy (winget links the command itself) and builds of the developer mode are left alone.
/// </summary>
public static class V2pnUserPath
{
    private const string MarkerKey = @"Software\V2pn";
    private const string MarkerValue = "UserPath";

    /// <summary>The folder the installer and install.ps1 install V2pn into.</summary>
    public static string InstallFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "V2pn");

    /// <summary>
    /// The user's PATH as stored with <paramref name="folder"/> added at the end, or null when an entry already names it:
    /// in another case, with a trailing \ or quotes, or through a variable such as %LOCALAPPDATA% (<paramref name="expand"/>).
    /// </summary>
    public static string? WithFolder(string? stored, string folder, Func<string, string> expand)
    {
        var want = Comparable(folder);
        if ((stored ?? string.Empty).Split(';').Any(entry => entry.Trim().Length > 0 && Comparable(expand(entry.Trim())) == want))
        {
            return null;
        }
        var kept = (stored ?? string.Empty).TrimEnd(';', ' ');
        return kept.Length == 0 ? folder : $"{kept};{folder}";
    }

    private static string Comparable(string path) => path.Trim().Trim('"').TrimEnd('\\', '/').ToUpperInvariant();

    /// <summary>At start, after logging is set up. Never throws; the registry work runs off the start path.</summary>
    public static void Refresh()
    {
        if (!OperatingSystem.IsWindows() || !V2pnInfo.IsRelease || V2pnWinGet.IsInstall())
        {
            return;
        }
        var folder = Utils.StartupPath();
        _ = Task.Run(() =>
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Add(folder);
                }
            }
            catch (Exception ex)
            {
                Logging.SaveLog(nameof(V2pnUserPath), ex);
            }
        });
    }

    [SupportedOSPlatform("windows")]
    private static void Add(string folder)
    {
        if (!V2pnUninstallEntry.SameFolder(folder, InstallFolder))
        {
            return;
        }
        using var marker = Registry.CurrentUser.CreateSubKey(MarkerKey);
        if (marker.GetValue(MarkerValue) != null)
        {
            return;
        }
        using (var environment = Registry.CurrentUser.CreateSubKey("Environment"))
        {
            var stored = environment.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (WithFolder(stored, folder, Environment.ExpandEnvironmentVariables) is { } updated)
            {
                environment.SetValue("Path", updated, RegistryValueKind.ExpandString);
                // New terminals read the change; ones already open keep their PATH, as after the installer.
                SendMessageTimeout(new IntPtr(0xffff), 0x001A, UIntPtr.Zero, "Environment", 0x0002, 5000, out _);
                Logging.SaveLog($"V2pn: {folder} is now on the user's PATH, for v2pnctl");
            }
        }
        marker.SetValue(MarkerValue, folder);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam, string lParam, uint flags,
        uint timeout, out UIntPtr result);
}
