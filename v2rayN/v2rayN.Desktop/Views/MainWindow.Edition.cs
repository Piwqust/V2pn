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
    private PassCard? _passCard;
    private string? _passKey;
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
        Editions.Changing += CaptureEditionShot;
        // Where the circle of a change of look starts: the last place the pointer was.
        AddHandler(PointerMovedEvent, (_, e) => _lastPointer = e.GetPosition(this), Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
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

    // The last state the tray and the title showed; a change of look renames both without waiting for the next one.
    private (ProtectionState State, string? Problem)? _trayState;
    private NativeMenuItem? _trayEditions;

    private Point? _lastPointer;
    private RenderTargetBitmap? _editionShot;
    private DispatcherTimer? _revealTimer;

    /// <summary>A picture of the window as it is, laid over it until the new look has opened.</summary>
    private void CaptureEditionShot()
    {
        StopEditionReveal();
        if (!IsVisible || imgEditionShot.Parent is not Visual body || body.Bounds.Width < 1 || body.Bounds.Height < 1)
        {
            return;
        }
        try
        {
            var scaling = RenderScaling;
            var pixels = new PixelSize((int)Math.Ceiling(body.Bounds.Width * scaling), (int)Math.Ceiling(body.Bounds.Height * scaling));
            var shot = new RenderTargetBitmap(pixels, new Vector(96 * scaling, 96 * scaling));
            shot.Render(body);
            _editionShot = shot;
            imgEditionShot.Source = shot;
            imgEditionShot.Width = body.Bounds.Width;
            imgEditionShot.Height = body.Bounds.Height;
            imgEditionShot.Clip = null;
            imgEditionShot.Opacity = 1;
            imgEditionShot.IsVisible = true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn edition shot", ex);
            StopEditionReveal();
        }
    }

    /// <summary>
    /// The new look opens as a circle from where the pointer was, in about 0.55 s; with animations off in Windows, a
    /// short fade instead. The old picture is gone at the end either way.
    /// </summary>
    private void PlayEditionReveal()
    {
        if (_editionShot == null || _revealTimer != null)
        {
            return;
        }
        var size = new Size(imgEditionShot.Width, imgEditionShot.Height);
        var origin = _lastPointer is { } p && p.X >= 0 && p.Y >= 0 && p.X <= size.Width && p.Y <= size.Height
            ? p
            : new Point(size.Width / 2, size.Height / 2);
        var reach = new[] { new Point(0, 0), new Point(size.Width, 0), new Point(0, size.Height), new Point(size.Width, size.Height) }
            .Max(c => Math.Sqrt(Math.Pow(c.X - origin.X, 2) + Math.Pow(c.Y - origin.Y, 2)));
        var circle = Motion.Enabled;
        var duration = circle ? 560.0 : 160.0;
        var started = DateTime.UtcNow;
        _revealTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) =>
        {
            var t = Math.Clamp((DateTime.UtcNow - started).TotalMilliseconds / duration, 0, 1);
            if (t >= 1)
            {
                StopEditionReveal();
                return;
            }
            var eased = Motion.InOut.Ease(t);
            if (circle)
            {
                var r = reach * eased;
                imgEditionShot.Clip = new CombinedGeometry(GeometryCombineMode.Exclude,
                    new RectangleGeometry(new Rect(size)),
                    new EllipseGeometry(new Rect(origin.X - r, origin.Y - r, r * 2, r * 2)));
            }
            else
            {
                imgEditionShot.Opacity = 1 - eased;
            }
        });
        _revealTimer.Start();
    }

    private void StopEditionReveal()
    {
        _revealTimer?.Stop();
        _revealTimer = null;
        imgEditionShot.IsVisible = false;
        imgEditionShot.Source = null;
        imgEditionShot.Clip = null;
        _editionShot?.Dispose();
        _editionShot = null;
    }

    /// <summary>"V2pn × Configra" for the looks on V2pn's layout, "Configra на V2pn" for the partner's own; else V2pn.</summary>
    private static string EditionTitle() => Editions.Partner is not { } partner
        ? L.AppName
        : Editions.Variant == V2pnPartnerVariants.Pass
            ? $"{L.AppName} × {partner.Name}"
            : string.Format(L.EditionTitleOn, partner.Name, L.AppName);

    /// <summary>The tray menu's "Look" submenu: plain V2pn and each partner, the open one ticked. Absent without partners.</summary>
    private void RefreshTrayEditions()
    {
        if (Application.Current is not { } app || TrayIcon.GetIcons(app) is not { Count: > 0 } icons || icons[0].Menu is not { } menu)
        {
            return;
        }
        var available = Editions.Available;
        if (available.Count == 0)
        {
            if (_trayEditions != null)
            {
                menu.Items.Remove(_trayEditions);
                _trayEditions = null;
            }
            return;
        }
        var looks = new NativeMenu();
        void Add(string? id, string name)
        {
            var item = new NativeMenuItem(name) { ToggleType = MenuItemToggleType.Radio, IsChecked = Editions.Partner?.Id == id };
            item.Click += (_, _) => Editions.Select(id);
            looks.Items.Add(item);
        }
        Add(null, L.AppName);
        foreach (var partner in available)
        {
            Add(partner.Id, V2pnPartners.IsPreview(partner.Id) ? string.Format(L.BuilderPreviewName, partner.Name) : partner.Name);
        }
        if (_trayEditions == null)
        {
            _trayEditions = new NativeMenuItem(L.TrayEditions);
            // Under Connect, with the everyday actions.
            var connect = menu.Items.OfType<NativeMenuItem>().FirstOrDefault(i => i.Header == L.Connect || i.Header == L.Disconnect);
            var at = connect != null ? menu.Items.IndexOf(connect) + 1 : Math.Min(4, menu.Items.Count);
            menu.Items.Insert(at, _trayEditions);
        }
        _trayEditions.Menu = looks;
    }

    private void RefreshEdition()
    {
        var partner = Editions.Partner;
        var variant = Editions.Variant;
        RefreshTrayEditions();
        if (_trayState is { } tray)
        {
            RefreshTray(tray.State, tray.Problem);
        }
        // With no verified partner the name is plain text and the title bar drags the window there too.
        btnEdition.IsHitTestVisible = Editions.Available.Count > 0;
        btnPreview.IsVisible = Editions.IsPreview;
        UpdateLockup(partner, variant);

        // The look as the person tuned it on the Appearance page; any change there builds the scene again.
        var theme = Editions.Theme;
        var look = new SceneLook(Editions.Mode != "light", Editions.AccentHex, Editions.Corners, "normal", Editions.Font);
        // The folder too: the theme builder's preview comes back from another folder whenever it changes.
        var key = partner != null && Editions.IsScene ? $"{partner.Id}:{variant}:{look}:{theme?.Key}:{partner.Folder}" : null;
        if (key != _sceneKey)
        {
            _sceneKey = key;
            Scene.Current = look;
            _scene = key == null ? null : new NeonScene(partner!, this, look, theme!, Editions.ThemeColors(look.Dark)!);
            editionHost.Content = _scene;
        }
        RefreshPassCard(partner, variant);
        RefreshSceneVisibility();
        // A partner's Neon made for the mini window opens in it the first time; the person may widen it after.
        if (_tier != BodyTier.Stacked && Editions.TakeMiniStart())
        {
            Dispatcher.UIThread.Post(() => _ = SnapAsync(), DispatcherPriority.Background);
        }
        PushEditionSnapshot();
        PlayEditionReveal();

        if (_scene != null || _passCard != null)
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
        // In a mini window the title bar has room for one name: the words go, the marks stay (with the full name as tip).
        var compact = partner != null && _tier == BodyTier.Stacked;
        pathBrand.IsVisible = true;
        txtBrand.IsVisible = !compact;
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

        if (logo is TextBlock && compact)
        {
            logo.MaxWidth = 90;
            ((TextBlock)logo).TextTrimming = TextTrimming.CharacterEllipsis;
        }
        if (variant == V2pnPartnerVariants.Pass)
        {
            panelBrand.Children.Add(new TextBlock { Text = "×", FontSize = 13, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center });
            panelBrand.Children.Add(logo);
        }
        else if (compact)
        {
            // The partner's logo, then V2pn's fork alone: still "on V2pn", in a tenth of the room.
            pathBrand.IsVisible = false;
            txtBrand.IsVisible = false;
            panelBrand.Children.Add(logo);
            var fork = new Avalonia.Controls.Shapes.Path
            {
                Width = 9,
                Height = 10,
                Stretch = Stretch.Uniform,
                Data = this.FindResource("V2BrandFork") as Geometry,
                Fill = new SolidColorBrush(Color.Parse("#FF3D1F")),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(fork, $"{L.EditionOn} {L.AppName}");
            panelBrand.Children.Add(fork);
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
        if (Editions.DueIntro() is not { } partner)
        {
            return;
        }
        if (!IsVisible || WindowState == WindowState.Minimized || OnboardingShown || DialogHost.IsDialogOpen(null))
        {
            // Something is in the way (the walkthrough, a dialog, the tray): look again in a few seconds.
            _introRetry ??= new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) =>
            {
                _introRetry?.Stop();
                _introRetry = null;
                MaybeShowPartnerIntro();
            });
            _introRetry.Start();
            return;
        }
        // Once, however the dialog is closed (a button, Esc or a click outside).
        Editions.MarkIntroduced(partner.Id);
        _ = DialogHost.Show(new PartnerIntroView(partner));
    }

    private DispatcherTimer? _introRetry;

    private static TextBlock Chevron() => new() { Text = "▾", FontSize = 11, Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center };

    private void ShowEditionMenu()
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var entries = new List<(string? Id, string Name)> { (null, L.AppName) };
        entries.AddRange(Editions.Available.Select(p => ((string?)p.Id, V2pnPartners.IsPreview(p.Id) ? string.Format(L.BuilderPreviewName, p.Name) : p.Name)));
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
        if (_catalogBusy || (_scene == null && _passCard == null))
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
            // V2pn: a partner's hidden servers are not offered; its own country for a server wins over a guess from the name.
            _catalog = all.Select(t =>
            {
                extra.TryGetValue(t.IndexId, out var ex);
                var note = V2pnPartnerLive.Instance.Describe(t.Subid, t.Remarks, t.Address);
                return note?.IsHidden == true ? null
                    : new SceneServer(t.IndexId, CountryResolver.ServerName(t), note?.Country ?? CountryResolver.Resolve(t.Remarks), ex?.Delay ?? 0, false, t.Subid, t.SubRemarks, ex?.IsFavorite == true,
                        note?.Group, note == null ? null : NonEmpty(string.Join(" · ", new[] { note.City, note.Note }.Concat(note.Purposes.Select(V2pnPurposes.Label)).Where(x => x.IsNotEmpty()))));
            }).OfType<SceneServer>().ToList();
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

    /// <summary>
    /// What the plain header shows right now, for the scene to show the same, plus the partner's side: its
    /// subscriptions (the list starts with their servers), whether the server in use is someone else's, and what its
    /// panel says about the subscription.
    /// </summary>
    // The partner's API, if it has one: asked when its look opens, then every 30 min (account) and 15 min (status)
    // while the window shows the look.
    private V2pnPartnerAccount? _partnerAccount;
    private IReadOnlyList<V2pnPartnerIncident>? _partnerIncidents;
    private string? _apiPartner;
    private DateTime _accountAsked;
    private DateTime _statusAsked;
    private bool _apiBusy;

    private async Task RefreshPartnerApiAsync(V2pnPartner partner, IReadOnlyList<SubItem> partnerSubs)
    {
        var now = DateTime.UtcNow;
        if (_apiPartner != partner.Id)
        {
            (_apiPartner, _partnerAccount, _partnerIncidents, _accountAsked, _statusAsked) = (partner.Id, null, null, default, default);
        }
        var wantAccount = now - _accountAsked > TimeSpan.FromMinutes(30);
        var wantStatus = now - _statusAsked > TimeSpan.FromMinutes(15);
        if (_apiBusy || partner.Pack.ApiUrl == null || (!wantAccount && !wantStatus))
        {
            return;
        }
        _apiBusy = true;
        try
        {
            if (wantAccount)
            {
                _accountAsked = now;
                var token = partnerSubs.Select(t => SubInfoManager.Instance.Get(t.Id)?.AccountToken).FirstOrDefault(t => t.IsNotEmpty());
                if (token != null && await Task.Run(() => V2pnPartnerApi.GetAccountAsync(partner, token)) is { } account && _apiPartner == partner.Id)
                {
                    _partnerAccount = account;
                }
            }
            if (wantAccount)
            {
                // The partner's live word about its servers, on the account's schedule (V2pnPartnerLive keeps its own clock).
                await RefreshPartnerLiveAsync(partner, force: false);
            }
            if (wantStatus)
            {
                _statusAsked = now;
                if (await Task.Run(() => V2pnPartnerApi.GetStatusAsync(partner)) is { } incidents && _apiPartner == partner.Id)
                {
                    _partnerIncidents = incidents;
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn partner API", ex);
        }
        finally
        {
            _apiBusy = false;
        }
    }

    private void PushEditionSnapshot()
    {
        if (_scene == null && _passCard == null)
        {
            return;
        }
        var kind = new[] { "safe", "warn", "checking", "unsafe" }.FirstOrDefault(c => pillState.Classes.Contains(c)) ?? "unsafe";
        var profiles = ViewModel?.ProfilesViewModel;
        var listed = profiles?.ProfileItems.FirstOrDefault(t => t.IndexId == _config.IndexId);
        // The catalog knows every server; the sidebar's list only the ones its filter shows.
        var current = _catalog.FirstOrDefault(t => t.IndexId == _config.IndexId);
        var currentSub = current?.SubId ?? listed?.Subid;

        var partner = Editions.Partner;
        var partnerSubs = partner == null || profiles == null
            ? []
            : profiles.SubItems.Where(t => t.Id.IsNotEmpty() && V2pnPartners.Instance.ForSubscription(t)?.Id == partner.Id).ToList();
        var partnerSubIds = partnerSubs.Select(t => t.Id).ToList();
        var partnerServers = _catalog.Where(t => t.SubId != null && partnerSubIds.Contains(t.SubId)).ToList();

        // The node squares count the partner's servers in its look, else the subscription of the server in use.
        var counted = partnerServers.Count > 0 ? partnerServers : _catalog.Where(t => t.SubId == currentSub).ToList();
        var sameSub = _catalog.Where(t => t.SubId == currentSub).Take(40).ToList();
        var best = partnerServers.Where(t => t.Delay > 0).OrderBy(t => t.Delay).FirstOrDefault() ?? partnerServers.FirstOrDefault();

        SceneAccount? account = null;
        var api = partner != null && _apiPartner == partner.Id ? _partnerAccount : null;
        if (partner != null)
        {
            if (IsVisible && partnerSubs.Count > 0)
            {
                _ = RefreshPartnerApiAsync(partner, partnerSubs);
            }
            var info = partnerSubs.Select(t => SubInfoManager.Instance.Get(t.Id)).FirstOrDefault(t => t != null);
            var usage = SubUsage.From(info);
            var incident = _apiPartner == partner.Id ? _partnerIncidents?.FirstOrDefault() : null;
            account = new SceneAccount(
                usage?.HasTraffic == true ? usage.TrafficText : null,
                usage?.Fraction ?? 0,
                usage?.ShowBar == true,
                usage?.HasExpiry == true ? usage.ExpiryLong : api?.Until is { } until ? string.Format(L.PartnerUntil, until.ToDateTime(TimeOnly.MinValue).ToString("d MMMM yyyy", CultureInfo.CurrentUICulture)) : null,
                usage?.TrafficWarn == true || usage?.ExpiryWarn == true,
                usage?.TrafficBad == true || usage?.ExpiryBad == true,
                NonEmpty(api?.Note) ?? (info?.Announce.IsNotEmpty() == true ? info.Announce : null),
                NonEmpty(info?.WebPageUrl) ?? NonEmpty(partner.Pack.CabinetUrl),
                NonEmpty(info?.SupportUrl) ?? NonEmpty(partner.Pack.SupportUrl),
                NonEmpty(api?.Plan),
                api?.Devices is { } count ? api.DeviceLimit is { } limit ? string.Format(L.PartnerDevicesOf, count, limit) : string.Format(L.PartnerDevices, count) : null,
                incident == null ? null : incident.Text.IsNullOrEmpty() ? incident.Title : $"{incident.Title}. {incident.Text}",
                incident?.Severe == true,
                // The announce-url belongs to the panel's announcement, not to the API's note.
                NonEmpty(api?.Note) == null ? NonEmpty(info?.AnnounceUrl) : null,
                V2pnSubDefaults.Banner(info, DateTimeOffset.Now),
                usage?.DaysLeft ?? (api?.Until is { } end ? Math.Max(0, end.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber) : null));
        }
        // The partner's own checks of its servers say more than V2pn's pings from one computer.
        var byPartner = api is { Healthy: { } partnerOk, Total: { } partnerTotal } && partnerTotal > 0;

        var state = CurrentState();
        var snapshot = DebugOverrides.Apply(new EditionSnapshot
        {
            StateKind = kind,
            StateLabel = AutomationProperties.GetName(txtStatePill) ?? txtStatePill.Text ?? string.Empty,
            Protected = kind == "safe",
            HasServer = _hasServer,
            Title = txtHeaderTitle.Text ?? string.Empty,
            Subtitle = txtHeaderSubtitle.Text ?? string.Empty,
            CountryCode = _mapCode,
            Subscription = current?.Sub ?? listed?.SubRemarks,
            Ping = current?.Delay > 0 ? current.Delay : listed?.Delay > 0 ? listed.Delay : 0,
            Vpn = lstMode.SelectedItem == itemModeVpn,
            Route = txtRoute.Text ?? string.Empty,
            NodesOk = byPartner ? api!.Healthy!.Value : counted.Count(t => t.Delay > 0),
            NodesTotal = byPartner ? api!.Total!.Value : counted.Count(t => t.Delay != 0),
            NodesByPartner = byPartner,
            ConnectLabel = state == ProtectionState.Unprotected ? L.Connect : L.Disconnect,
            Servers = sameSub.Select(t => t with { Selected = t.IndexId == _config.IndexId }).ToList(),
            Catalog = _catalog,
            Subs = _catalogSubs,
            SelectedId = _config.IndexId,
            AnyServers = _catalog.Count > 0 || (profiles?.AllServersCount ?? 0) > 0,
            CanImport = btnWelcomeImport.IsVisible && panelWelcomeImport.IsVisible,
            PartnerName = partner?.Name,
            PartnerSubIds = partnerSubIds,
            ForeignServer = partnerServers.Count > 0 && _hasServer && currentSub != null && !partnerSubIds.Contains(currentSub),
            BestPartnerServer = best?.IndexId,
            Account = account is { IsEmpty: false } ? account : null,
            Actions = partner?.Pack.ActionList ?? [],
            ActionRunning = RunningAction(partner),
        });
        _scene?.Update(snapshot);
        _passCard?.Update(snapshot.Account, partner?.Name ?? string.Empty);
        UpdatePassPlace();
    }

    /// <summary>
    /// The Pass: the partner's card on V2pn's own stage, over the status card. Built again when the partner, its
    /// theme or light and dark change; gone in plain V2pn and in Neon.
    /// </summary>
    private void RefreshPassCard(V2pnPartner? partner, string variant)
    {
        var light = ActualThemeVariant == ThemeVariant.Light;
        var key = partner != null && variant == V2pnPartnerVariants.Pass ? $"{partner.Id}:{Editions.Theme?.Key}:{light}:{partner.Folder}" : null;
        if (key == _passKey)
        {
            return;
        }
        _passKey = key;
        _passCard = key == null || Editions.ThemeColors(!light) is not { } colours
            ? null
            : new PassCard(partner!, colours, url => ((IEditionActions)this).OpenUrl(url));
        passHost.Content = _passCard;
        if (_passCard != null)
        {
            PushEditionSnapshot();
        }
    }

    /// <summary>The card sits just over the status card (or at the stage's bottom without it), wide or stacked as the stage allows.</summary>
    private void UpdatePassPlace()
    {
        if (_passCard == null)
        {
            return;
        }
        var status = contentStatusBarView.IsVisible && contentStatusBarView.Bounds.Height > 0 ? contentStatusBarView.Bounds.Height + 12 : 0;
        passHost.Margin = new Thickness(16, 0, 16, 16 + status);
        _passCard.Arrange(wide: passHost.Bounds.Width >= 540 || (passHost.Bounds.Width == 0 && _tier != BodyTier.Stacked));
    }

    private static string? NonEmpty(string? text) => text.IsNullOrEmpty() ? null : text;

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

    void IEditionActions.OpenUrl(string url) => ProcUtils.ProcessStart(url);
}
