using Avalonia.Automation;
using DialogHostAvalonia;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

// The debug window (DebugWindow.cs): only in a development build or with developer mode on. ☰ → "Отладка…" or
// Ctrl+Shift+D (⌘⇧D). What it does to the window goes through these methods.
public partial class MainWindow
{
    private DebugWindow? _debugWindow;
    private MenuItem? _menuDebug;

    private void InitDebug()
    {
        var modifiers = (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control) | KeyModifiers.Shift;
        if (btnAppMenu.Flyout is MenuFlyout menu)
        {
            _menuDebug = new MenuItem { Header = "Отладка…", InputGesture = new KeyGesture(Key.D, modifiers), IsVisible = false };
            _menuDebug.Click += (_, _) => OpenDebugWindow();
            var at = menu.Items.IndexOf(menuClose);
            menu.Items.Insert(Math.Max(0, at - 1), _menuDebug);
            // Developer mode can be switched on while V2pn runs (About → updates).
            menu.Opening += (_, _) => _menuDebug.IsVisible = V2pnPartnerTrust.DebugAllowed;
        }
        // V2PN_DEBUG=1 opens it with the window, for scripted checks (tools/sandbox.ps1 passes the environment on).
        if (Environment.GetEnvironmentVariable("V2PN_DEBUG") == "1")
        {
            Opened += (_, _) => Dispatcher.UIThread.Post(OpenDebugWindow, DispatcherPriority.Background);
        }
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.D && e.KeyModifiers == modifiers && V2pnPartnerTrust.DebugAllowed)
            {
                OpenDebugWindow();
                e.Handled = true;
            }
        };
        InitDebugShots();
        InitDebugPerf();
    }

    // V2PN_PERF=<folder>, where debugging is allowed: a file named hide, show, toggle, minimize or restore dropped
    // into the folder does that to the window, as the tray icon and the taskbar would. Every time the window comes
    // up, a line in <folder>/perf.log says how long it took: until the call returned, until the first frame with the
    // window's content was drawn, and until the window had nothing left to do. The times start at the moment the
    // request reached the window (a tray click, a second start of V2pn), counted on the clock every process of this
    // computer shares, so a script that started a second V2pn can add its own part. tools/showperf.ps1 uses it.
    private string? _perfFolder;
    private long _perfShowAsked;
    private string? _perfShowWhy;

    private void InitDebugPerf()
    {
        var folder = Environment.GetEnvironmentVariable("V2PN_PERF");
        if (folder.IsNullOrEmpty() || !V2pnPartnerTrust.DebugAllowed || !Directory.Exists(folder))
        {
            return;
        }
        _perfFolder = folder;
        // A watcher, not a timer: a timer would keep the hidden window's code in memory, and how fast a window that
        // was left alone comes back is what is measured.
        var watcher = new FileSystemWatcher(folder) { EnableRaisingEvents = true };
        watcher.Created += (_, e) =>
        {
            // "show.7" is "show": a script that names each file anew never meets one left from a stalled command.
            var name = Path.GetFileName(e.FullPath);
            var dot = name.IndexOf('.');
            if (dot > 0)
            {
                name = name[..dot];
            }
            if (name is not ("hide" or "show" or "toggle" or "minimize" or "restore"))
            {
                return;
            }
            var asked = System.Diagnostics.Stopwatch.GetTimestamp();
            if (Environment.GetEnvironmentVariable("V2PN_PERF_TRACE") == "1")
            {
                DebugPerfLine(FormattableString.Invariant($"{DateTime.Now:HH:mm:ss.fff} event {Path.GetFileName(e.FullPath)}"));
            }
            Dispatcher.UIThread.Post(() =>
            {
                var waited = System.Diagnostics.Stopwatch.GetElapsedTime(asked).TotalMilliseconds;
                var began = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    File.Delete(e.FullPath);
                }
                catch
                {
                }
                switch (name)
                {
                    case "hide":
                        ShowHideWindow(false);
                        break;
                    case "minimize":
                        WindowState = WindowState.Minimized;
                        break;
                    case "restore":
                        DebugNoteShowAsked(asked, name);
                        WindowState = _restoreState;
                        break;
                    default:
                        DebugNoteShowAsked(asked, name);
                        ShowHideWindow(name == "show" ? true : null);
                        break;
                }
                // A command that waited for the window's thread, or held it, is a stall worth seeing.
                var took = System.Diagnostics.Stopwatch.GetElapsedTime(began).TotalMilliseconds;
                if (waited > 100 || took > 250)
                {
                    DebugPerfLine(FormattableString.Invariant($"{DateTime.Now:HH:mm:ss.fff} stall {name} waited={waited:0} took={took:0}"));
                }
            });
        };
        Closed += (_, _) => watcher.Dispose();
        // V2PN_PERF_BEAT=<ms>: a thread of its own asks the window's thread for a turn that often and writes down
        // every turn that came late. It keeps the window's thread awake, so it is for finding stalls, not for timing.
        if (int.TryParse(Environment.GetEnvironmentVariable("V2PN_PERF_BEAT"), out var beat) && beat > 0)
        {
            new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(beat);
                    var asked = System.Diagnostics.Stopwatch.GetTimestamp();
                    Dispatcher.UIThread.Post(() =>
                    {
                        var waited = System.Diagnostics.Stopwatch.GetElapsedTime(asked).TotalMilliseconds;
                        if (waited > 200)
                        {
                            DebugPerfLine(FormattableString.Invariant($"{DateTime.Now:HH:mm:ss.fff} beat waited={waited:0}"));
                        }
                    });
                }
            })
            { IsBackground = true, Name = "V2pn perf beat" }.Start();
        }
    }

    /// <summary>When a request to bring the window up arrived, before the window's thread took it.</summary>
    private void DebugNoteShowAsked(long asked, string why)
    {
        if (_perfFolder != null && _perfShowAsked == 0)
        {
            _perfShowAsked = asked;
            _perfShowWhy = why;
        }
    }

    /// <summary>The window has just become seen (shown from the tray or restored from the taskbar): time it.</summary>
    private async void DebugTraceSeen()
    {
        if (_perfFolder is not { } folder)
        {
            return;
        }
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var asked = _perfShowAsked == 0 ? now : _perfShowAsked;
        var why = _perfShowWhy ?? "window";
        _perfShowAsked = 0;
        _perfShowWhy = null;
        try
        {
            static double Ms(long from) => System.Diagnostics.Stopwatch.GetElapsedTime(from).TotalMilliseconds;
            var seen = Ms(asked);
            // The rest of Show (the platform's own call, activation) runs before this continues.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Send);
            var compositor = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(this)?.Compositor;
            if (compositor == null)
            {
                return;
            }
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
            var returned = Ms(asked);
            await compositor.RequestCompositionBatchCommitAsync().Rendered;
            var frame = Ms(asked);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await compositor.RequestCompositionBatchCommitAsync().Rendered;
            var settled = Ms(asked);
            DebugPerfLine(FormattableString.Invariant($"{DateTime.Now:HH:mm:ss.fff} {why} asked={asked} freq={System.Diagnostics.Stopwatch.Frequency} seen={seen:0.0} returned={returned:0.0} frame={frame:0.0} settled={settled:0.0}"));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(DebugTraceSeen), ex);
        }
    }

    private readonly Lock _perfLogGate = new();

    private void DebugPerfLine(string line)
    {
        try
        {
            if (_perfFolder is { } folder)
            {
                lock (_perfLogGate)
                {
                    File.AppendAllText(Path.Combine(folder, "perf.log"), line + Environment.NewLine);
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(DebugPerfLine), ex);
        }
    }

    // V2PN_SHOTS=<folder>, where debugging is allowed: when a file named "shoot" appears in the folder, every open
    // window is drawn into <folder>/window-<n>-<its class>.png and the status item's three pictures into
    // status-<n>.png, then the file goes. For checks on a Mac over ssh, where the screen cannot be captured
    // (tools/mac-sandbox.sh): the windows draw themselves, as tools/shot.ps1 has Windows do.
    private void InitDebugShots()
    {
        var folder = Environment.GetEnvironmentVariable("V2PN_SHOTS");
        if (folder.IsNullOrEmpty() || !V2pnPartnerTrust.DebugAllowed)
        {
            return;
        }
        var trigger = Path.Combine(folder, "shoot");
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) =>
        {
            if (!File.Exists(trigger))
            {
                return;
            }
            try
            {
                var windows = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows ?? [];
                var n = 0;
                foreach (var window in windows.Where(w => w.IsVisible && w.Bounds.Width > 0 && w.Bounds.Height > 0))
                {
                    // At the display's own scale the dialogs' shadows and blur are drawn out of place; one pixel a point is enough to check a layout.
                    const double scale = 1;
                    var size = new PixelSize((int)(window.Bounds.Width * scale), (int)(window.Bounds.Height * scale));
                    using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
                    bitmap.Render(window);
                    bitmap.Save(Path.Combine(folder, $"window-{++n}-{window.GetType().Name}.png"));
                }
                if (OperatingSystem.IsMacOS())
                {
                    // What Avalonia draws is not all a person sees on a Mac: the title bar's buttons, the popups'
                    // own windows and the way macOS puts them together come only in the system's picture.
                    var real = MacAppUtils.CaptureOwnWindows(folder);
                    Logging.SaveLog($"V2pn debug shots: {real} window(s) as macOS shows them");
                    for (var index = 1; index <= 3; index++)
                    {
                        using var picture = MacStatusIcons.Render(index);
                        picture.Save(Path.Combine(folder, $"status-{index}.png"));
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.SaveLog(nameof(InitDebugShots), ex);
            }
            finally
            {
                File.Delete(trigger);
            }
        });
        timer.Start();
    }

    internal void OpenDebugWindow()
    {
        if (!V2pnPartnerTrust.DebugAllowed)
        {
            return;
        }
        if (_debugWindow is { IsVisible: true })
        {
            // Asked for by the person: bringing it forward is the point.
            _debugWindow.Activate();
            return;
        }
        _debugWindow = new DebugWindow(this);
        _debugWindow.Closed += (_, _) => _debugWindow = null;
        _debugWindow.Show(this);
    }

    internal void DebugToast(ToastKind kind, string text) => ShowToast(kind, text);

    internal void DebugStartOnboarding() => ShowOnboarding((ObStep)0);

    internal void DebugOpenLink(string text)
    {
        if (V2pnDeepLink.Parse(text.Trim()) is { } link)
        {
            _ = AskToAddLinkAsync(link);
        }
        else
        {
            ShowToast(ToastKind.Warning, "Это не ссылка на подписку: v2pn://add?url=…, happ://add/…, v2raytun://import/…, hiddify://import/…");
        }
    }

    internal void DebugResize(double width, double height)
    {
        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }
        MinHeight = Math.Min(MinHeight, height);
        PlaceWindow(new Size(width, height), null);
    }

    /// <summary>Plain V2pn's light or dark, as the Appearance page sets it (saved, as there).</summary>
    internal void DebugSetMode(bool light)
    {
        if (_themeSettings != null)
        {
            _themeSettings.CurrentTheme = light ? nameof(ETheme.Light) : nameof(ETheme.Dark);
        }
    }

    internal void DebugOpenIp() => _ = ShowRoomyDialog(new IpInfoView());

    internal void DebugOpenTable() => ShowPage(L.MenuTable, TablePage);

    internal void DebugOpenConnections() => ShowPage(L.MenuClashConnections, ClashConnectionsPage);

    internal void DebugOpenProxies() => ShowPage(L.MenuClashProxies, ClashProxiesPage);

    internal void DebugOpenLog() => ShowPage(L.MenuLog, MsgPage);

    internal void DebugOpenAppearance()
    {
        if (_themeSettings != null)
        {
            _ = DialogHost.Show(new AppearanceView(_themeSettings));
        }
    }

    internal void DebugUpdateSubscriptions() => ViewModel?.SubUpdateCmd.Execute().Subscribe();

    /// <summary>Asks the partner API again now, instead of at its next turn.</summary>
    internal void DebugRefreshApi()
    {
        _accountAsked = default;
        _statusAsked = default;
        RefreshPartnerAccount();
    }

    internal string DebugApiState()
    {
        if (_apiPartner == null)
        {
            return "API: не спрашивали (в окне нет партнёра с API)";
        }
        var a = _partnerAccount;
        var account = a == null
            ? "account: нет ответа"
            : $"account: тариф {a.Plan ?? "—"}, серверы {a.Healthy?.ToString() ?? "—"} из {a.Total?.ToString() ?? "—"}, устройств {a.Devices?.ToString() ?? "—"}, до {a.Until?.ToString("yyyy-MM-dd") ?? "—"}";
        var status = _partnerIncidents == null ? "status: нет ответа" : $"status: сбоев {_partnerIncidents.Count}";
        var asked = _accountAsked == default ? "ещё не спрашивали" : $"спрошено {(DateTime.UtcNow - _accountAsked).TotalMinutes:0} мин назад";
        return $"API {_apiPartner}: {account}; {status}; {asked}";
    }

    internal string DebugPlanState() => $"план на сцене: {(passHost.IsVisible ? "да" : "нет")} · {_planFit}";

    internal IReadOnlyList<SubItem> DebugSubscriptions() => ViewModel?.ProfilesViewModel.SubItems.Where(s => s.Id.IsNotEmpty()).ToList() ?? [];
}
