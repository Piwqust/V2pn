using Avalonia.Input.Platform;
using v2rayN.Desktop.Views;

namespace v2rayN.Desktop.Common;

internal class AvaUtils
{
    public static async Task<string?> GetClipboardData(Window owner)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
            if (clipboard == null)
            {
                return null;
            }

            return await clipboard.TryGetTextAsync();
        }
        catch
        {
            return null;
        }
    }

    public static async Task SetClipboardData(Visual? visual, string strData)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(visual)?.Clipboard;
            if (clipboard == null)
            {
                return;
            }

            await clipboard.SetTextAsync(strData);
        }
        catch
        {
        }
    }

    /// <summary>
    /// The tray and taskbar icon for a connection verdict, as the brand book defines them: graphite when
    /// off, Signal when connected (proxy or VPN), amber when it needs attention (a failed check, or only
    /// the local port is set). A NotifyIcon{n}.ico next to the program still overrides the built-in art.
    /// </summary>
    public static int AppIconIndex(ProtectionState state, bool warn) => state switch
    {
        ProtectionState.Unprotected => 1,
        ProtectionState.LocalOnly => 3,
        _ => warn ? 3 : 2,
    };

    public static WindowIcon GetAppIcon(int index)
    {
        var fileName = Utils.GetPath($"NotifyIcon{index}.ico");
        if (File.Exists(fileName))
        {
            return new(fileName);
        }

        // Straight from the stream: a Bitmap would keep one frame of the ICO and Windows would scale it
        // for the 20 and 24 px slots at 125/150%, instead of using the hand-sized frames.
        var uri = new Uri(Path.Combine(Global.AvaAssets, $"NotifyIcon{index}.ico"));
        using var stream = AssetLoader.Open(uri);
        return new(stream);
    }
}
