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
    private ThemeBuilderWindow? _builderWindow;
    private MenuItem? _menuBuilder;

    private void InitDebug()
    {
        DebugOverrides.Changed += () => Dispatcher.UIThread.Post(PushEditionSnapshot);
        var modifiers = (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control) | KeyModifiers.Shift;
        if (btnAppMenu.Flyout is MenuFlyout menu)
        {
            _menuDebug = new MenuItem { Header = "Отладка…", InputGesture = new KeyGesture(Key.D, modifiers), IsVisible = false };
            _menuDebug.Click += (_, _) => OpenDebugWindow();
            _menuBuilder = new MenuItem { Header = L.BuilderMenu, IsVisible = false };
            _menuBuilder.Click += (_, _) => OpenThemeBuilder();
            var at = menu.Items.IndexOf(menuClose);
            menu.Items.Insert(Math.Max(0, at - 1), _menuDebug);
            menu.Items.Insert(Math.Max(0, at - 1), _menuBuilder);
            // Developer mode can be switched on while V2pn runs (About → updates).
            menu.Opening += (_, _) => _menuDebug.IsVisible = _menuBuilder.IsVisible = V2pnPartnerTrust.DebugAllowed;
        }
        btnPreview.Click += (_, _) => Editions.ClosePreview();
        ToolTip.SetTip(btnPreview, L.BuilderPreviewHint + " " + L.BuilderRemovePreview + ".");
        AutomationProperties.SetName(btnPreview, L.BuilderRemovePreview);
        // V2PN_BUILDER opens the theme builder with the window, with that pack folder when it names one (scripted checks).
        if (Environment.GetEnvironmentVariable("V2PN_BUILDER") is { Length: > 0 } builder)
        {
            Opened += (_, _) => Dispatcher.UIThread.Post(() => OpenThemeBuilder(Directory.Exists(builder) ? builder : null), DispatcherPriority.Background);
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

    /// <summary>The theme builder (developer mode only): a provider's theme from nothing to a signed pack and a certificate request.</summary>
    internal void OpenThemeBuilder(string? folder = null)
    {
        if (!V2pnPartnerTrust.DebugAllowed)
        {
            return;
        }
        if (_builderWindow is { IsVisible: true })
        {
            // Asked for by the person: bringing it forward is the point.
            _builderWindow.Activate();
            return;
        }
        _builderWindow = new ThemeBuilderWindow(this, folder);
        _builderWindow.Closed += (_, _) => _builderWindow = null;
        _builderWindow.Show();
    }

    internal void DebugToast(ToastKind kind, string text) => ShowToast(kind, text);

    internal void DebugStartOnboarding() => ShowOnboarding((ObStep)0);

    internal void DebugShowIntro(V2pnPartner partner)
    {
        if (DialogHost.IsDialogOpen(null))
        {
            DialogHost.Close(null);
        }
        _ = DialogHost.Show(new PartnerIntroView(partner));
    }

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

    internal void DebugReplayReveal()
    {
        CaptureEditionShot();
        Dispatcher.UIThread.Post(PlayEditionReveal, DispatcherPriority.Background);
    }

    internal void DebugOpenIp() => _ = ShowRoomyDialog(new IpInfoView());

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
        PushEditionSnapshot();
    }

    internal string DebugApiState()
    {
        if (_apiPartner == null)
        {
            return "API: не спрашивали (нет открытого оформления партнёра с API)";
        }
        var a = _partnerAccount;
        var account = a == null
            ? "account: нет ответа"
            : $"account: тариф {a.Plan ?? "—"}, серверы {a.Healthy?.ToString() ?? "—"} из {a.Total?.ToString() ?? "—"}, устройств {a.Devices?.ToString() ?? "—"}, до {a.Until?.ToString("yyyy-MM-dd") ?? "—"}";
        var status = _partnerIncidents == null ? "status: нет ответа" : $"status: сбоев {_partnerIncidents.Count}";
        var asked = _accountAsked == default ? "ещё не спрашивали" : $"спрошено {(DateTime.UtcNow - _accountAsked).TotalMinutes:0} мин назад";
        return $"API {_apiPartner}: {account}; {status}; {asked}";
    }

    internal IReadOnlyList<SubItem> DebugSubscriptions() => ViewModel?.ProfilesViewModel.SubItems.Where(s => s.Id.IsNotEmpty()).ToList() ?? [];
}
