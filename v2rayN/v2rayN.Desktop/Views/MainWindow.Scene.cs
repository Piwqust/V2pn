using Avalonia.Automation;
using Avalonia.Layout;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Views.EditionLooks;

namespace v2rayN.Desktop.Views;

// A partner whose theme names a layout of V2pn's own (theme.layout, e.g. the sticker book) gets that screen in its tab,
// over the sidebar and the stage. It reads what V2pn's own header and list show and acts through the same controls;
// a secondary page (log, settings) still opens over the stage, and the screen steps aside while it is up.
public partial class MainWindow : IEditionActions
{
    private EditionScene? _scene;
    private string? _sceneKey;
    private DispatcherTimer? _sceneTimer;
    private SceneAccount? _sceneAccount;
    // The partners whose screen the person put away for V2pn's own layout, until the app restarts.
    private readonly HashSet<string> _scenePlain = [];
    private Button? _btnScene;

    private void InitScene()
    {
        pageOverlay.GetObservable(IsVisibleProperty).Subscribe(_ => RefreshSceneVisibility());
        // In V2pn's own layout, the way back to the partner's screen sits under Connect and the partner's buttons.
        _btnScene = new Button
        {
            Classes = { "ghost" },
            Theme = this.FindResource("V2Button") as ControlTheme,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsVisible = false,
        };
        _btnScene.Click += (_, _) =>
        {
            if (Editions.Look is { } partner)
            {
                _scenePlain.Remove(partner.Id);
                RefreshScene();
            }
        };
        panelConnect.Children.Add(_btnScene);
    }

    /// <summary>The layout the open tab's partner asks for, if V2pn draws one for it.</summary>
    private static string? WantedLayout() =>
        Editions.Look is { } partner && Editions.ThemeOf(partner).Layout is var layout && layout != V2pnThemeLayouts.V2pn ? layout : null;

    private void RefreshScene()
    {
        var partner = Editions.Look;
        var layout = WantedLayout();
        var plain = partner != null && _scenePlain.Contains(partner.Id);
        var key = partner == null || layout == null || plain
            ? null
            : $"{partner.Id}|{partner.Folder}|{Editions.ThemeOf(partner).Key}";
        if (key != _sceneKey)
        {
            _sceneKey = key;
            _scene = key == null ? null : BuildScene(layout!, partner!);
            editionHost.Content = _scene;
        }
        if (_btnScene != null)
        {
            _btnScene.IsVisible = partner != null && layout != null && plain;
            _btnScene.Content = partner == null ? null : string.Format(L.StickerBack, partner.Name);
        }
        // The partner's screens are drawn at night: the title bar, menus and dialogs go dark with them.
        RequestedThemeVariant = _scene != null ? ThemeVariant.Dark : null;
        RefreshSceneVisibility();
        PushSceneSnapshot();

        if (_scene != null)
        {
            _sceneTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
            {
                if (IsVisible)
                {
                    PushSceneSnapshot();
                }
            });
            _sceneTimer.Start();
        }
        else
        {
            _sceneTimer?.Stop();
        }
    }

    /// <summary>A screen that fails to build leaves V2pn's own layout in place, not a broken window.</summary>
    private EditionScene? BuildScene(string layout, V2pnPartner partner)
    {
        try
        {
            return EditionScene.For(layout, partner, this);
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"V2pn partner layout {layout}", ex);
            return null;
        }
    }

    private void RefreshSceneVisibility()
    {
        var show = _scene != null && !pageOverlay.IsVisible;
        editionHost.IsVisible = show;
        // Covered, not hidden: the adaptive layout keeps deciding what the sidebar shows. Disabled so Tab never lands
        // on a control nobody can see.
        borderSidebar.IsEnabled = !show;
        panelStage.IsEnabled = !show;
    }

    /// <summary>What V2pn's own header and list show right now, for the partner's screen to show the same.</summary>
    private void PushSceneSnapshot()
    {
        if (_scene == null)
        {
            return;
        }
        var kind = new[] { "safe", "warn", "checking", "unsafe" }.FirstOrDefault(c => pillState.Classes.Contains(c)) ?? "unsafe";
        var profiles = ViewModel?.ProfilesViewModel;
        var current = profiles?.ProfileItems.FirstOrDefault(t => t.IndexId == _config.IndexId);
        // The servers of the subscription in use; before one is chosen, those the list shows (the partner's).
        var servers = profiles == null
            ? []
            : profiles.ProfileItems.Where(t => current == null || t.Subid == current.Subid).Take(24).ToList();
        var snapshot = new EditionSnapshot
        {
            StateKind = kind,
            StateLabel = AutomationProperties.GetName(txtStatePill) ?? txtStatePill.Text ?? string.Empty,
            On = CurrentState() != ProtectionState.Unprotected,
            HasServer = _hasServer,
            Title = txtHeaderTitle.Text ?? string.Empty,
            Subtitle = txtHeaderSubtitle.Text ?? string.Empty,
            CountryCode = _mapCode,
            Ping = current?.Delay > 0 ? current.Delay : 0,
            Vpn = lstMode.SelectedItem == itemModeVpn,
            VpnNeedsAdmin = badgeAdmin.IsVisible,
            VpnTip = ToolTip.GetTip(itemModeVpn) as string,
            Route = txtRoute.Text ?? string.Empty,
            Servers = servers.Select(t => new SceneServer(
                t.IndexId,
                CountryResolver.ServerName(t),
                V2pnPartnerLive.Instance.Describe(t.Subid, t.Remarks, t.Address)?.Country ?? CountryResolver.Resolve(t.Remarks),
                t.Delay,
                t.IndexId == _config.IndexId)).ToList(),
            BusyAction = RunningAction(Editions.Look),
            Account = _sceneAccount,
        };
        try
        {
            _scene.Update(snapshot);
        }
        catch (Exception ex)
        {
            // Once, not every second: the screen goes and V2pn's own layout stays until the partner's theme changes.
            Logging.SaveLog("V2pn partner layout", ex);
            _scene = null;
            editionHost.Content = null;
            RequestedThemeVariant = null;
            _sceneTimer?.Stop();
            RefreshSceneVisibility();
        }
    }

    void IEditionActions.ToggleConnect() => btnConnect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    void IEditionActions.SetVpn(bool vpn) => lstMode.SelectedItem = vpn ? itemModeVpn : itemModeProxy;

    void IEditionActions.OpenRoute(Control anchor) => btnRoute.Flyout?.ShowAt(anchor);

    void IEditionActions.Select(string indexId) => _ = ConnectToAsync(indexId);

    void IEditionActions.RunAction(V2pnPartnerAction action, Control anchor)
    {
        if (Editions.Look is { } partner)
        {
            _ = RunPartnerActionAsync(partner, action, anchor);
        }
    }

    void IEditionActions.Open(string url) => ProcUtils.ProcessStart(url);

    void IEditionActions.ShowPlain()
    {
        if (Editions.Look is { } partner)
        {
            _scenePlain.Add(partner.Id);
            RefreshScene();
        }
    }
}
