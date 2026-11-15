using Avalonia.Controls.Primitives;
using Avalonia.Automation;
using Avalonia.Layout;
using DialogHostAvalonia;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Views.EditionLooks;

namespace v2rayN.Desktop.Views;

// Partner editions (Common/Editions.cs): the switcher behind the name in the title bar, the partner's logo
// beside V2pn's, and the looks with a layout of their own, which read the window's state and act through it.
public partial class MainWindow : IEditionActions
{
    private EditionScene? _scene;
    private string? _sceneKey;
    private DispatcherTimer? _sceneTimer;
    private IReadOnlyList<SceneServer> _catalog = [];
    private IReadOnlyList<SceneSub> _catalogSubs = [];
    private bool _catalogBusy;
    private int _sceneTicks;

    private void InitEdition()
    {
        btnEdition.Click += (_, _) => ShowEditionMenu();
        KeyDown += OnEditionKey;
        // A secondary page (log, Clash) opens inside the stage; the scene steps aside while it is up.
        pageOverlay.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                RefreshSceneVisibility();
            }
        };
        Editions.Changed += RefreshEdition;
        Editions.Changed += () => Dispatcher.UIThread.Post(MaybeShowPartnerIntro, DispatcherPriority.Background);
        // A look verified while the window was in the tray is offered when it comes back.
        Activated += (_, _) => Dispatcher.UIThread.Post(MaybeShowPartnerIntro, DispatcherPriority.Background);
        Editions.Closed += (partner, why) => ShowToast(why == V2pnPartnerGone.Unlinked ? ToastKind.Info : ToastKind.Warning, why switch
        {
            V2pnPartnerGone.Expired => string.Format(L.EditionGoneExpired, partner.Name),
            V2pnPartnerGone.Revoked => string.Format(L.EditionGoneRevoked, partner.Name),
            _ => string.Format(L.EditionGoneUnlinked, partner.Name),
        });
        ActualThemeVariantChanged += (_, _) => UpdateLockup(Editions.Partner, Editions.Variant);
        Editions.Start();
    }

    private void RefreshEdition()
    {
        var partner = Editions.Partner;
        var variant = Editions.Variant;
        // With no verified partner the name is plain text and the title bar drags the window there too.
        btnEdition.IsHitTestVisible = Editions.Available.Count > 0;
        UpdateLockup(partner, variant);

        // The look as the person tuned it on the Appearance page; any change there builds the scene again.
        var look = new SceneLook(Editions.Mode != "light", Editions.AccentHex, Editions.Corners, Editions.DefaultCorners(variant), Editions.Font);
        var key = partner != null && Editions.IsScene ? $"{partner.Id}:{variant}:{look}" : null;
        if (key != _sceneKey)
        {
            _sceneKey = key;
            Scene.Current = look;
            _scene = key == null ? null : variant switch
            {
                V2pnPartnerVariants.Cabinet => new CabinetScene(partner!, this, look),
                V2pnPartnerVariants.Studio => new StudioScene(partner!, this, look),
                _ => new AsciiScene(partner!, this, look),
            };
            editionHost.Content = _scene;
        }
        RefreshSceneVisibility();
        PushEditionSnapshot();

        if (_scene != null)
        {
            _sceneTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
            {
                if (!IsVisible)
                {
                    return;
                }
                // The list (every server, with its last ping) is read from the database every few seconds.
                if (++_sceneTicks % 3 == 0)
                {
                    _ = RefreshEditionCatalogAsync();
                }
                PushEditionSnapshot();
            });
            _sceneTimer.Start();
            _ = RefreshEditionCatalogAsync();
        }
        else
        {
            _sceneTimer?.Stop();
        }
    }

    private void RefreshSceneVisibility()
    {
        var show = _scene != null && !pageOverlay.IsVisible;
        editionHost.IsVisible = show;
        // Covered, not hidden: the adaptive layout keeps deciding what the sidebar shows. Disabled so Tab never
        // lands on a control nobody can see.
        borderSidebar.IsEnabled = !show;
        panelStage.IsEnabled = !show;
    }

    /// <summary>
    /// V2pn's fork and name, with the partner beside it: "V2pn × Configra" for the looks built on V2pn's layout,
    /// "Configra · на V2pn" for the looks of the partner's own. V2pn's mark is never recoloured.
    /// </summary>
    private void UpdateLockup(V2pnPartner? partner, string variant)
    {
        while (panelBrand.Children.Count > 2)
        {
            panelBrand.Children.RemoveAt(2);
        }
        pathBrand.IsVisible = true;
        txtBrand.IsVisible = true;
        AutomationProperties.SetName(btnEdition, partner == null ? L.AppName : $"{L.AppName} × {partner.Name}");
        ToolTip.SetTip(btnEdition, Editions.Available.Count > 0 ? L.EditionSwitchTip : null);
        if (partner == null)
        {
            if (Editions.Available.Count > 0)
            {
                panelBrand.Children.Add(Chevron());
            }
            return;
        }

        // The partner's logo for a light title bar where the look (or plain V2pn's mode) is light.
        var light = Editions.IsTuned ? Editions.Mode == "light" : ActualThemeVariant == ThemeVariant.Light;
        var bitmap = Scene.Picture(partner, light ? partner.Pack.LogoOnLight ?? partner.Pack.Logo : partner.Pack.Logo);
        Control logo = bitmap == null
            ? new TextBlock { Text = partner.Name, FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }
            : new Image { Source = bitmap, Height = 16, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };

        if (variant is V2pnPartnerVariants.Signature or V2pnPartnerVariants.Warm)
        {
            panelBrand.Children.Add(new TextBlock { Text = "×", FontSize = 13, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center });
            panelBrand.Children.Add(logo);
        }
        else
        {
            pathBrand.IsVisible = false;
            txtBrand.IsVisible = false;
            panelBrand.Children.Add(logo);
            panelBrand.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x59, 0x80, 0x80, 0x80)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(7, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 5,
                    Children =
                    {
                        new TextBlock { Text = L.EditionOn, FontSize = 10.5, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center },
                        new Avalonia.Controls.Shapes.Path
                        {
                            Width = 9,
                            Height = 10,
                            Stretch = Stretch.Uniform,
                            Data = this.FindResource("V2BrandFork") as Geometry,
                            Fill = new SolidColorBrush(Color.Parse("#FF3D1F")),
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                        new TextBlock { Text = L.AppName, FontSize = 10.5, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            });
        }
        panelBrand.Children.Add(Chevron());
    }

    /// <summary>
    /// Offers a partner's look once, the first time it is available: after a subscription from a partner was added and
    /// its pack checked out. Waits for the window to be on screen with nothing else open (walkthrough, a dialog).
    /// </summary>
    private void MaybeShowPartnerIntro()
    {
        if (!IsVisible || WindowState == WindowState.Minimized || OnboardingShown || DialogHost.IsDialogOpen(null))
        {
            return;
        }
        if (Editions.DueIntro() is not { } partner)
        {
            return;
        }
        // Once, however the dialog is closed (a button, Esc or a click outside).
        Editions.MarkIntroduced(partner.Id);
        _ = DialogHost.Show(new PartnerIntroView(partner));
    }

    private static TextBlock Chevron() => new() { Text = "▾", FontSize = 11, Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center };

    private void ShowEditionMenu()
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var entries = new List<(string? Id, string Name)> { (null, L.AppName) };
        entries.AddRange(Editions.Available.Select(p => ((string?)p.Id, p.Name)));
        for (var i = 0; i < entries.Count; i++)
        {
            var (id, name) = entries[i];
            var item = new MenuItem
            {
                Header = name,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = Editions.Partner?.Id == id,
                InputGesture = i < 9 ? new KeyGesture(Key.D1 + i, OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control) : null,
            };
            item.Click += (_, _) => Editions.Select(id);
            menu.Items.Add(item);
        }
        if (Editions.Partner is { } partner && partner.Pack.Variants.Count > 1)
        {
            menu.Items.Add(new Separator());
            var looks = new MenuItem { Header = string.Format(L.EditionLook, partner.Name) };
            foreach (var variant in partner.Pack.Variants)
            {
                var look = new MenuItem
                {
                    Header = L.EditionVariant(variant),
                    ToggleType = MenuItemToggleType.Radio,
                    IsChecked = variant == Editions.Variant,
                };
                look.Click += (_, _) => Editions.SetVariant(variant);
                looks.Items.Add(look);
            }
            menu.Items.Add(looks);
        }
        menu.ShowAt(btnEdition);
    }

    private void OnEditionKey(object? sender, KeyEventArgs e)
    {
        var modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.KeyModifiers != modifier || e.Key < Key.D1 || e.Key > Key.D9 || Editions.Available.Count == 0)
        {
            return;
        }
        var index = e.Key - Key.D1;
        if (index == 0)
        {
            Editions.Select(null);
            e.Handled = true;
        }
        else if (index - 1 < Editions.Available.Count)
        {
            Editions.Select(Editions.Available[index - 1].Id);
            e.Handled = true;
        }
    }

    /// <summary>Every server V2pn has, in the plain list's order, with its last ping and favourite mark.</summary>
    private async Task RefreshEditionCatalogAsync()
    {
        if (_catalogBusy || _scene == null)
        {
            return;
        }
        _catalogBusy = true;
        try
        {
            var all = await AppManager.Instance.ProfileModels(string.Empty, string.Empty) ?? [];
            var extra = (await ProfileExManager.Instance.GetProfileExs())
                .GroupBy(t => t.IndexId)
                .ToDictionary(g => g.Key, g => g.First());
            var subs = await AppManager.Instance.SubItems() ?? [];
            _catalog = all.Select(t =>
            {
                extra.TryGetValue(t.IndexId, out var ex);
                return new SceneServer(t.IndexId, CountryResolver.ServerName(t), CountryResolver.Resolve(t.Remarks), ex?.Delay ?? 0, false, t.Subid, t.SubRemarks, ex?.IsFavorite == true);
            }).ToList();
            var counts = all.GroupBy(t => t.Subid ?? string.Empty).ToDictionary(g => g.Key, g => g.Count());
            _catalogSubs = subs
                .Where(t => t.Id.IsNotEmpty() && counts.GetValueOrDefault(t.Id) > 0)
                .Select(t => new SceneSub(t.Id, t.Remarks.IsNotEmpty() ? t.Remarks : t.Url, counts[t.Id]))
                .ToList();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn edition list", ex);
        }
        finally
        {
            _catalogBusy = false;
        }
        PushEditionSnapshot();
    }

    /// <summary>What the plain header shows right now, for the scene to show the same.</summary>
    private void PushEditionSnapshot()
    {
        if (_scene == null)
        {
            return;
        }
        var kind = new[] { "safe", "warn", "checking", "unsafe" }.FirstOrDefault(c => pillState.Classes.Contains(c)) ?? "unsafe";
        var profiles = ViewModel?.ProfilesViewModel;
        var current = profiles?.ProfileItems.FirstOrDefault(t => t.IndexId == _config.IndexId);
        var sameSub = current == null || profiles == null
            ? []
            : profiles.ProfileItems.Where(t => t.Subid == current.Subid).Take(40).ToList();
        var state = CurrentState();
        _scene.Update(new EditionSnapshot
        {
            StateKind = kind,
            StateLabel = AutomationProperties.GetName(txtStatePill) ?? txtStatePill.Text ?? string.Empty,
            Protected = kind == "safe",
            HasServer = _hasServer,
            Title = txtHeaderTitle.Text ?? string.Empty,
            Subtitle = txtHeaderSubtitle.Text ?? string.Empty,
            CountryCode = _mapCode,
            Subscription = current?.SubRemarks,
            Ping = current?.Delay > 0 ? current.Delay : 0,
            Vpn = lstMode.SelectedItem == itemModeVpn,
            Route = txtRoute.Text ?? string.Empty,
            NodesOk = sameSub.Count(t => t.Delay > 0),
            NodesTotal = sameSub.Count(t => t.Delay != 0),
            ConnectLabel = state == ProtectionState.Unprotected ? L.Connect : L.Disconnect,
            Servers = sameSub.Select(t => new SceneServer(t.IndexId, CountryResolver.ServerName(t), CountryResolver.Resolve(t.Remarks), t.Delay, t.IndexId == _config.IndexId)).ToList(),
            Catalog = _catalog,
            Subs = _catalogSubs,
            SelectedId = _config.IndexId,
            AnyServers = _catalog.Count > 0 || (profiles?.AllServersCount ?? 0) > 0,
            CanImport = btnWelcomeImport.IsVisible && panelWelcomeImport.IsVisible,
        });
    }

    void IEditionActions.ToggleConnect() => btnConnect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    void IEditionActions.SetVpn(bool vpn) => lstMode.SelectedItem = vpn ? itemModeVpn : itemModeProxy;

    void IEditionActions.OpenRoute(Control anchor) => btnRoute.Flyout?.ShowAt(anchor);

    void IEditionActions.Select(string indexId) => _ = ConnectToAsync(indexId);

    void IEditionActions.PingAll() => _ = PingAllAsync();

    void IEditionActions.UpdateSubscriptions() => ViewModel?.SubUpdateCmd.Execute().Subscribe();

    void IEditionActions.Add(Control anchor) => btnAdd.Flyout?.ShowAt(anchor);

    void IEditionActions.Paste() => _ = AddServerViaClipboardAsync();

    void IEditionActions.AddSubscription() => ViewModel?.ProfilesViewModel.AddSubCmd.Execute().Subscribe();

    void IEditionActions.Import() => btnWelcomeImport.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    void IEditionActions.OpenSettings() => ViewModel?.OptionSettingCmd.Execute().Subscribe();

    void IEditionActions.OpenIp() => _ = ShowRoomyDialog(new IpInfoView());
}
