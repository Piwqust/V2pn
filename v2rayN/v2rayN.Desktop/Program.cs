using v2rayN.Desktop.Common;
using v2rayN.Desktop.Manager;

namespace v2rayN.Desktop;

internal class Program
{
    public static EventWaitHandle ProgramStarted;
    private static Mutex? _instanceMutex;

    // V2pn: started by the sign-in entry (Global.AutostartArg), not by the user.
    public static bool StartedAtSignIn { get; private set; }

    // V2pn: a v2pn:// (or happ://, v2raytun://, hiddify://) link V2pn was started with; the window asks about it once shown.
    public static V2pnAddLink? StartupLink { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // V2pn started elevated only to put the "only through V2pn" filters in place, or to remove them (the uninstaller).
        if (V2pnAppKillSwitch.TryRunCommand(args, out var killSwitchExit))
        {
            Environment.Exit(killSwitchExit);
            return;
        }

        if (OnStartup(args) == false)
        {
            Environment.Exit(0);
            return;
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static bool OnStartup(string[]? Args)
    {
        if (Utils.IsWindows())
        {
            var exePathKey = Utils.GetMd5(Utils.GetExePath());
            var rebootas = (Args ?? []).Any(t => t == Global.RebootAs);
            ProgramStarted = new EventWaitHandle(false, EventResetMode.AutoReset, exePathKey, out var bCreatedNew);
            if (!rebootas && !bCreatedNew)
            {
                // A click on a v2pn:// link while V2pn runs: hand the link to the running copy, then wake it.
                if ((Args ?? []).FirstOrDefault(a => V2pnDeepLink.Parse(a) != null) is { } link)
                {
                    V2pnDeepLink.Stash(link);
                }
                WindowsShell.AllowAnyForeground();
                ProgramStarted.Set();
                return false;
            }
        }
        else
        {
            // Keep the named mutex alive for the process lifetime. Discarding the handle lets
            // the runtime finalize it, so the next launch sees no running instance.
            _instanceMutex = new Mutex(true, "V2pn", out var bOnlyOneInstance);
            if (!bOnlyOneInstance)
            {
                return false;
            }
        }

        StartedAtSignIn = (Args ?? []).Any(t => t == Global.AutostartArg);
        StartupLink = V2pnDeepLink.FromArgs(Args);

        if (!AppManager.Instance.InitApp())
        {
            return false;
        }

        AppManager.Instance.WindowDialog = new WindowDialog();
        V2pnDeepLink.RegisterScheme();
        V2pnUninstallEntry.Refresh();
        return true;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
           .UsePlatformDetect()
           //.WithInterFont()
           .WithFontByDefault()
#if DEBUG
           .WithDeveloperTools()
#endif
           .LogToTrace()
           .UseReactiveUI(_ => { });

        if (OperatingSystem.IsMacOS())
        {
            var showInDock = Design.IsDesignMode || ConfigTextHandler.Config.UiItem.MacOSShowInDock;
            builder = builder.With(new MacOSPlatformOptions { ShowInDock = showInDock });
        }

        // V2pn: in background mode menus and flyouts are drawn inside their parked window; as separate
        // popup windows they are pushed back onto a monitor.
        if (OperatingSystem.IsWindows() && Common.OffscreenMode.IsOn)
        {
            builder = builder.With(new Win32PlatformOptions { OverlayPopups = true });
        }

        return builder;
    }
}
