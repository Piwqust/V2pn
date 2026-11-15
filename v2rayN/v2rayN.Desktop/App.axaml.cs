using v2rayN.Desktop.Common;
using v2rayN.Desktop.Views;

namespace v2rayN.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Common.SemiRounding.Apply(this);
        Common.Motion.Install(this);
        Common.Appearance.Install(this); // V2pn
        Common.Icons.Install(this); // V2pn: before any window resolves its icons
        if (OperatingSystem.IsWindows())
        {
            Common.TrayMenuPlacement.Install(); // V2pn
        }

        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        // V2pn: an exception in a click, timer or "async void" handler used to close the whole app and
        // leave Windows pointing at a proxy nobody serves. Log it and keep running.
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Logging.SaveLog("Dispatcher_UnhandledException", e.Exception);
            e.Handled = true;
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var viewLocator = SimpleViewLocator.Instance;
        DataTemplates.Add(viewLocator);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!Design.IsDesignMode)
            {
                AppManager.Instance.InitComponents();
                V2pnRunTrail.Begin();
                DataContext = StatusBarViewModel.Instance;
            }

            var mainWindowViewModel = new MainWindowViewModel();
            var mainWindow = (MainWindow)viewLocator.Build(mainWindowViewModel);
            mainWindow.ViewModel = mainWindowViewModel;
            desktop.MainWindow = mainWindow;

            if (OperatingSystem.IsMacOS())
            {
                Current?.TryGetFeature<IActivatableLifetime>()?.Activated += OnMacOSActivated;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    #region MacOS Activation

    private void OnMacOSActivated(object? sender, ActivatedEventArgs args)
    {
        // V2pn: a click on a v2pn:// link (Info.plist claims the scheme). macOS hands it to the running app, also
        // the one it starts for the click, instead of passing it as an argument.
        if (args.Kind == ActivationKind.OpenUri && args is ProtocolActivatedEventArgs protocol)
        {
            if (V2pnDeepLink.Parse(protocol.Uri.OriginalString) is { } link
                && (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is MainWindow window)
            {
                Dispatcher.UIThread.Post(() => window.QueueAddLink(link));
            }
            return;
        }
        if (args.Kind != ActivationKind.Reopen)
        {
            return;
        }

        if ((ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not MainWindow mainWindow)
        {
            return;
        }

        var isMiniaturized = MacAppUtils.IsWindowMiniaturized(mainWindow);

        Dispatcher.UIThread.Post(() =>
        {
            if (isMiniaturized)
            {
                RestoreMacOSAccessoryPolicyAfterMiniaturize(mainWindow);
                mainWindow.ShowHideWindow(true);
                return;
            }

            if (!AppManager.Instance.Config.UiItem.MacOSShowInDock)
            {
                MacAppUtils.SetActivationPolicyAccessory();
            }

            mainWindow.ShowHideWindow(true);
        });
    }

    private static void RestoreMacOSAccessoryPolicyAfterMiniaturize(MainWindow mainWindow)
    {
        if (AppManager.Instance.Config.UiItem.MacOSShowInDock)
        {
            return;
        }

        mainWindow
            .GetObservable(Window.WindowStateProperty)
            .Skip(1)
            .Where(state => state != WindowState.Minimized)
            .Take(1)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ => QueueMacOSAccessoryPolicyRestore(mainWindow));
    }

    private static void QueueMacOSAccessoryPolicyRestore(MainWindow mainWindow)
    {
        // AppKit may keep isMiniaturized set until the Dock restore animation finishes.
        DispatcherTimer.RunOnce(() => RestoreMacOSAccessoryPolicy(mainWindow), TimeSpan.FromMilliseconds(300));
    }

    private static void RestoreMacOSAccessoryPolicy(MainWindow mainWindow)
    {
        if (AppManager.Instance.Config.UiItem.MacOSShowInDock || MacAppUtils.IsWindowMiniaturized(mainWindow))
        {
            return;
        }

        MacAppUtils.SetActivationPolicyAccessory();
        mainWindow.Activate();
        mainWindow.Focus();
    }

    #endregion MacOS Activation

    #region App Event

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            if (e.IsTerminating)
            {
                V2pnRunTrail.NoteCrash("CurrentDomain_UnhandledException", ex);
            }
            else
            {
                Logging.SaveLog("CurrentDomain_UnhandledException", ex);
            }
        }
        if (e.IsTerminating)
        {
            ClearOwnProxyBeforeDying();
        }
    }

    /// <summary>
    /// V2pn: the process is going down and its cores go with it. Take V2pn's own system proxy off first,
    /// or Windows keeps sending every program to a dead port until someone notices.
    /// </summary>
    private static void ClearOwnProxyBeforeDying()
    {
        try
        {
            var config = AppManager.Instance.Config;
            if (config == null || V2pnConnection.IsOff(config))
            {
                return;
            }
            Task.Run(async () => await ServiceLib.Handler.SysProxy.SysProxyHandler.UpdateSysProxy(config, true)).Wait(2000);
            Logging.SaveLog("V2pn: system proxy cleared before an abnormal exit");
        }
        catch
        {
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logging.SaveLog("TaskScheduler_UnobservedTaskException", e.Exception);
    }

    private static MainWindow? TrayWindow =>
        (Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;

    private async void MenuAddServerViaClipboardClick(object? sender, EventArgs e)
    {
        try
        {
            // The same path as the window's paste: subscription links are added and downloaded, failures are explained.
            // The window comes up first, so its toasts and the new subscription's spinner are seen.
            if (TrayWindow is { } mainWindow)
            {
                mainWindow.ShowHideWindow(true);
                await mainWindow.AddServerViaClipboardAsync();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("MenuAddServerViaClipboardClick", ex);
        }
    }

    private void MenuToggleConnectionClick(object? sender, EventArgs e)
    {
        TrayWindow?.ToggleConnectionFromTray();
    }

    private async void MenuRouteRussiaClick(object? sender, EventArgs e) => await SelectRouteAsync(RoutingPresetKind.RussiaDirect);

    private async void MenuRouteBlockedClick(object? sender, EventArgs e) => await SelectRouteAsync(RoutingPresetKind.BlockedOnly);

    private async void MenuRouteAllClick(object? sender, EventArgs e) => await SelectRouteAsync(RoutingPresetKind.All);

    private static async Task SelectRouteAsync(RoutingPresetKind kind)
    {
        try
        {
            if (TrayWindow is { } mainWindow)
            {
                await mainWindow.SelectPresetFromTrayAsync(kind);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Tray routing preset", ex);
        }
    }

    private async void MenuExit_Click(object? sender, EventArgs e)
    {
        await AppManager.Instance.AppExitAsync(false);
        AppManager.Instance.Shutdown(true);
    }

    #endregion App Event
}
