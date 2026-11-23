using System.Runtime.InteropServices;

namespace v2rayN.Desktop.Common;

internal static class MacAppUtils
{
    private const string LibObjC = "/usr/lib/libobjc.dylib";
    private const nint ActivationPolicyAccessory = 1;

    public static void SetActivationPolicyAccessory()
        => objc_msgSend(
            objc_msgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication")),
            sel_registerName("setActivationPolicy:"),
            ActivationPolicyAccessory);

    public static bool IsWindowMiniaturized(Window window)
        => window.TryGetPlatformHandle() is IMacOSTopLevelPlatformHandle { NSWindow: not 0 } handle
            && objc_msgSend_bool(handle.NSWindow, sel_registerName("isMiniaturized"));

    /// <summary>
    /// V2pn: puts the window's close, minimize and zoom buttons in the middle of a title bar of
    /// <paramref name="barHeight"/> points, as far from the left edge as from the top. macOS lays them out for its own
    /// 28-point bar, so over V2pn's taller one they sat above the line of the menu button and the name. AppKit puts
    /// them back whenever it lays the title bar out (a resize, a new title, leaving full screen); the window calls
    /// this again then.
    /// </summary>
    public static void CenterWindowButtons(Window window, double barHeight)
    {
        if (window.TryGetPlatformHandle() is not IMacOSTopLevelPlatformHandle { NSWindow: not 0 } handle)
        {
            return;
        }
        var standardButton = sel_registerName("standardWindowButton:");
        var buttons = new[] { 0, 1, 2 }.Select(kind => objc_msgSend(handle.NSWindow, standardButton, (nint)kind, 0)).ToArray();
        if (buttons.Any(b => b == 0))
        {
            return;
        }
        // The buttons live in the title bar's view, which lives in a container as tall as the system's title bar.
        // The container takes V2pn's height first, or buttons moved lower would leave it and stop taking clicks.
        // Any layout AppKit still owes the title bar (a new title, a new size) happens now, not after the buttons
        // are placed, where it would put them back.
        objc_msgSend(handle.NSWindow, sel_registerName("layoutIfNeeded"));
        var bar = objc_msgSend(buttons[0], sel_registerName("superview"));
        var container = bar == 0 ? 0 : objc_msgSend(bar, sel_registerName("superview"));
        if (container == 0)
        {
            return;
        }
        var windowFrame = GetFrame(handle.NSWindow);
        var containerFrame = GetFrame(container);
        objc_msgSend_setFrame(container, sel_registerName("setFrame:"),
            new CGRect(containerFrame.X, windowFrame.Height - barHeight, containerFrame.Width, barHeight));

        var first = GetFrame(buttons[0]);
        var step = GetFrame(buttons[1]).X - first.X;
        if (step <= 0)
        {
            return;
        }
        var top = (barHeight - first.Height) / 2;
        for (var i = 0; i < buttons.Length; i++)
        {
            objc_msgSend_setOrigin(buttons[i], sel_registerName("setFrameOrigin:"), new CGPoint(top + (i * step), top));
        }
    }

    private static CGRect GetFrame(nint view)
    {
        // A structure this large comes back through a hidden pointer on Intel and in registers on Apple silicon.
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            objc_msgSend_stret(out var frame, view, sel_registerName("frame"));
            return frame;
        }
        return objc_msgSend_frame(view, sel_registerName("frame"));
    }

    /// <summary>V2pn: the person asked macOS for less motion (Accessibility > Display > Reduce motion).</summary>
    public static bool ReduceMotion()
    {
        try
        {
            var workspace = objc_msgSend(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
            return workspace != 0 && objc_msgSend_bool(workspace, sel_registerName("accessibilityDisplayShouldReduceMotion"));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(ReduceMotion), ex);
            return false;
        }
    }

    /// <summary>V2pn: whether V2pn is the app in front (its menus are in the menu bar).</summary>
    public static bool IsAppActive() =>
        objc_msgSend_bool(objc_msgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication")), sel_registerName("isActive"));

    /// <summary>
    /// V2pn: the system's own About panel (app menu → About V2pn): the app's icon, name and version from the bundle,
    /// and under them <paramref name="creditsRtf"/> (see <see cref="V2pnCredits"/>), where links can be clicked.
    /// </summary>
    public static void ShowAboutPanel(string version, string creditsRtf)
    {
        var app = objc_msgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
        var options = objc_msgSend(objc_getClass("NSMutableDictionary"), sel_registerName("dictionary"));
        var setObject = sel_registerName("setObject:forKey:");
        objc_msgSend(options, setObject, NSString(Global.AppName), NSString("ApplicationName"));
        objc_msgSend(options, setObject, NSString(version), NSString("ApplicationVersion"));
        // The build number would repeat the version in brackets; an empty one leaves "Version 1.4.0".
        objc_msgSend(options, setObject, NSString(string.Empty), NSString("Version"));

        var bytes = Encoding.ASCII.GetBytes(creditsRtf);
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var data = objc_msgSend(objc_getClass("NSData"), sel_registerName("dataWithBytes:length:"), handle.AddrOfPinnedObject(), bytes.Length);
            var credits = objc_msgSend(
                objc_msgSend(objc_getClass("NSMutableAttributedString"), sel_registerName("alloc")),
                sel_registerName("initWithRTF:documentAttributes:"), data, 0);
            if (credits != 0)
            {
                // Text read from RTF is black; the label colour and the system font follow light and dark.
                var length = objc_msgSend(credits, sel_registerName("length"));
                var addAttribute = sel_registerName("addAttribute:value:range:");
                var color = objc_msgSend(objc_getClass("NSColor"), sel_registerName("labelColor"));
                objc_msgSend(credits, addAttribute, NSString("NSColor"), color, 0, length);
                var font = objc_msgSend_double(objc_getClass("NSFont"), sel_registerName("systemFontOfSize:"), 11);
                objc_msgSend(credits, addAttribute, NSString("NSFont"), font, 0, length);
                objc_msgSend(options, setObject, credits, NSString("Credits"));
                objc_msgSend(credits, sel_registerName("autorelease"));
            }
        }
        finally
        {
            handle.Free();
        }
        // Asked for by the person, from the menu bar or the status item: the panel comes to the front.
        objc_msgSend(app, sel_registerName("activateIgnoringOtherApps:"), 1);
        objc_msgSend(app, sel_registerName("orderFrontStandardAboutPanelWithOptions:"), options);
    }

    /// <summary>
    /// V2pn: a notification in the corner of the screen and in Notification Centre, under V2pn's name and icon.
    /// False when this macOS or this copy cannot post one (no bundle, as a build run from a folder), which changes
    /// nothing else: the icon in the menu bar still tells the state.
    /// </summary>
    public static bool Notify(string title, string text)
    {
        try
        {
            var centerClass = objc_getClass("NSUserNotificationCenter");
            var noteClass = objc_getClass("NSUserNotification");
            if (centerClass == 0 || noteClass == 0)
            {
                return false;
            }
            var center = objc_msgSend(centerClass, sel_registerName("defaultUserNotificationCenter"));
            if (center == 0)
            {
                return false;
            }
            var note = objc_msgSend(objc_msgSend(noteClass, sel_registerName("alloc")), sel_registerName("init"));
            objc_msgSend(note, sel_registerName("setTitle:"), NSString(title));
            objc_msgSend(note, sel_registerName("setInformativeText:"), NSString(text));
            objc_msgSend(center, sel_registerName("deliverNotification:"), note);
            objc_msgSend(note, sel_registerName("autorelease"));
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(Notify), ex);
            return false;
        }
    }

    /// <summary>
    /// V2pn, for checks: every window of this app that is on screen (popups and menus drawn by Avalonia included), as
    /// macOS itself composes it, with the title bar's buttons and at the display's resolution, saved as
    /// <paramref name="folder"/>/mac-N.png. An app may picture its own windows without the screen-recording
    /// permission. Returns how many were saved.
    /// </summary>
    public static int CaptureOwnWindows(string folder)
    {
        var saved = 0;
        var app = objc_msgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
        var windows = objc_msgSend(app, sel_registerName("windows"));
        var count = unchecked((int)objc_msgSend(windows, sel_registerName("count")));
        for (var i = 0; i < count; i++)
        {
            var window = objc_msgSend(windows, sel_registerName("objectAtIndex:"), (nint)i, 0);
            if (window == 0 || !objc_msgSend_bool(window, sel_registerName("isVisible")))
            {
                continue;
            }
            // A window that is not on a display yet has the number -1.
            var id = objc_msgSend(window, sel_registerName("windowNumber"));
            if (id <= 0)
            {
                continue;
            }
            var number = unchecked((uint)id);
            // The one window, without its shadow, at the best resolution: kCGWindowListOptionIncludingWindow,
            // kCGWindowImageBoundsIgnoreFraming | kCGWindowImageBestResolution.
            var image = CGWindowListCreateImage(new CGRect(double.PositiveInfinity, double.PositiveInfinity, 0, 0), 1 << 3, number, 1 | (1 << 3));
            if (image == 0)
            {
                continue;
            }
            var url = objc_msgSend(objc_getClass("NSURL"), sel_registerName("fileURLWithPath:"), NSString(Path.Combine(folder, $"mac-{++saved}.png")), 0);
            var destination = CGImageDestinationCreateWithURL(url, NSString("public.png"), 1, 0);
            if (destination != 0)
            {
                CGImageDestinationAddImage(destination, image, 0);
                CGImageDestinationFinalize(destination);
                CFRelease(destination);
            }
            CFRelease(image);
        }
        return saved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGRect(double x, double y, double width, double height)
    {
        public readonly double X = x;
        public readonly double Y = y;
        public readonly double Width = width;
        public readonly double Height = height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGPoint(double x, double y)
    {
        public readonly double X = x;
        public readonly double Y = y;
    }

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern CGRect objc_msgSend_frame(nint receiver, nint selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend_stret")]
    private static extern void objc_msgSend_stret(out CGRect result, nint receiver, nint selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_setFrame(nint receiver, nint selector, CGRect frame);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_setOrigin(nint receiver, nint selector, CGPoint origin);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern nint CGWindowListCreateImage(CGRect bounds, uint listOption, uint windowId, uint imageOption);

    [DllImport("/System/Library/Frameworks/ImageIO.framework/ImageIO")]
    private static extern nint CGImageDestinationCreateWithURL(nint url, nint type, nint count, nint options);

    [DllImport("/System/Library/Frameworks/ImageIO.framework/ImageIO")]
    private static extern void CGImageDestinationAddImage(nint destination, nint image, nint properties);

    [DllImport("/System/Library/Frameworks/ImageIO.framework/ImageIO")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGImageDestinationFinalize(nint destination);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(nint value);

    private static nint NSString(string text) =>
        objc_msgSend_utf8(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), text);

    [DllImport(LibObjC)]
    private static extern nint objc_getClass(string name);

    [DllImport(LibObjC)]
    private static extern nint sel_registerName(string name);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend(nint receiver, nint selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool objc_msgSend_bool(nint receiver, nint selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend(nint receiver, nint selector, nint argument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend(nint receiver, nint selector, nint first, nint second);

    // A range (location, length) travels as two integers, the same way the two fields of NSRange do.
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend(nint receiver, nint selector, nint first, nint second, nint location, nint length);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_double(nint receiver, nint selector, double argument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_utf8(nint receiver, nint selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
}
