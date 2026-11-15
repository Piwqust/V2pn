using Avalonia.Controls.Primitives;
using Avalonia.Automation;
using Avalonia.Layout;
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
        Editions.Start();
    }

    private void RefreshEdition()
    {
        var partner = Editions.Partner;
        var variant = Editions.Variant;
        // With no verified partner the name is plain text and the title bar drags the window there too.
        btnEdition.IsHitTestVisible = Editions.Available.Count > 0;
        UpdateLockup(partner, variant);

        var key = partner != null && Editions.IsScene ? $"{partner.Id}:{variant}" : null;
        if (key != _sceneKey)
        {
            _sceneKey = key;
            _scene = key == null ? null : variant switch
            {
                V2pnPartnerVariants.Cabinet => new CabinetScene(partner!, this),
                V2pnPartnerVariants.Studio => new StudioScene(partner!, this),
                _ => new AsciiScene(partner!, this),
            };
            editionHost.Content = _scene;
        }
        // The studio look is light whatever the app's theme, so its title bar and dialogs are light with it.
        RequestedThemeVariant = partner != null && variant == V2pnPartnerVariants.Studio ? ThemeVariant.Light : null;
        RefreshSceneVisibility();
        PushEditionSnapshot();

        if (_scene != null)
        {
            _sceneTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
            {
                if (IsVisible)
                {
                    PushEditionSnapshot();
                }
            });
            _sceneTimer.Start();
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

        var light = variant == V2pnPartnerVariants.Studio;
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
        });
    }

    void IEditionActions.ToggleConnect() => btnConnect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    void IEditionActions.SetVpn(bool vpn) => lstMode.SelectedItem = vpn ? itemModeVpn : itemModeProxy;

    void IEditionActions.OpenRoute(Control anchor) => btnRoute.Flyout?.ShowAt(anchor);

    void IEditionActions.Select(string indexId) => _ = ConnectToAsync(indexId);
}
