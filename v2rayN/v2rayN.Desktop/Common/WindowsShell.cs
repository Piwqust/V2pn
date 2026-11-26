using System.Runtime.InteropServices;

namespace v2rayN.Desktop.Common;

/// <summary>The few user32 calls V2pn needs to behave like a native Windows app.</summary>
internal static class WindowsShell
{
    private const int AsfwAny = -1;

    // A second launch hands the foreground to the running copy, so its window comes to the front
    // instead of flashing in the taskbar.
    public static void AllowAnyForeground()
    {
        try
        {
            AllowSetForegroundWindow(AsfwAny);
        }
        catch
        {
        }
    }

    // While Windows signs out or shuts down, says why V2pn holds it up for a moment.
    public static void BlockShutdown(IntPtr hwnd, string reason)
    {
        try
        {
            ShutdownBlockReasonCreate(hwnd, reason);
        }
        catch
        {
        }
    }

    public static void UnblockShutdown(IntPtr hwnd)
    {
        try
        {
            ShutdownBlockReasonDestroy(hwnd);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Takes a window out of sight while Windows keeps composing it (it still draws, yet has no place on the
    /// screen), or brings it back. False when Windows refused.
    /// </summary>
    public static bool Cloak(IntPtr hwnd, bool cloak)
    {
        try
        {
            var value = cloak ? 1 : 0;
            return hwnd != IntPtr.Zero && DwmSetWindowAttribute(hwnd, DwmwaCloak, ref value, sizeof(int)) == 0;
        }
        catch
        {
            return false;
        }
    }

    private const int DwmwaCloak = 13;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool ShutdownBlockReasonCreate(IntPtr hWnd, string pwszReason);

    [DllImport("user32.dll")]
    private static extern bool ShutdownBlockReasonDestroy(IntPtr hWnd);
}
