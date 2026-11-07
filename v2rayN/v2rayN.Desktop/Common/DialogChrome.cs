using System.Runtime.InteropServices;

namespace v2rayN.Desktop.Common;

/// <summary>
/// Dialogs keep the native Windows title bar (snap, system menu, real caption buttons) but paint
/// it in the dialog's own surface colour, so the bar and the window read as one piece in both themes.
/// Windows 11 honours the caption and text colours; Windows 10 only takes the dark-mode flag.
/// </summary>
internal static class DialogChrome
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Attach(Window window)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        window.Opened += (_, _) => Apply(window);
        window.ActualThemeVariantChanged += (_, _) => Apply(window);
        // Re-assert on focus changes: the frame is redrawn for the inactive state.
        window.Activated += (_, _) => Apply(window);
        window.Deactivated += (_, _) => Apply(window);
    }

    private static void Apply(Window window)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }
        try
        {
            var dark = window.ActualThemeVariant == ThemeVariant.Dark || window.ActualThemeVariant.InheritVariant == ThemeVariant.Dark;
            var flag = dark ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref flag, sizeof(int));

            if (ColorOf(window, "V2DialogBackground") is { } caption)
            {
                var value = ColorRef(caption);
                DwmSetWindowAttribute(handle, DwmCaptionColor, ref value, sizeof(int));
                DwmSetWindowAttribute(handle, DwmBorderColor, ref value, sizeof(int));
            }
            if (ColorOf(window, "V2Ink") is { } text)
            {
                var value = ColorRef(text);
                DwmSetWindowAttribute(handle, DwmTextColor, ref value, sizeof(int));
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("DialogChrome", ex);
        }
    }

    private static Color? ColorOf(Window window, string key) =>
        window.TryFindResource(key, window.ActualThemeVariant, out var value) && value is ISolidColorBrush brush ? brush.Color : null;

    // COLORREF is 0x00BBGGRR.
    private static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
}
