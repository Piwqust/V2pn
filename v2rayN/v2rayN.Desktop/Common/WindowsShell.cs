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

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool ShutdownBlockReasonCreate(IntPtr hWnd, string pwszReason);

    [DllImport("user32.dll")]
    private static extern bool ShutdownBlockReasonDestroy(IntPtr hWnd);
}
