using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using v2rayN.Desktop.Common;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;

namespace v2rayN.Desktop.Views;

/// <summary>
/// V2pn: the first-start walkthrough offers Connect on the import screen. Route and mode are optional
/// steps. Every choice is the same one the main screen offers later, made through the
/// same code, so nothing here is a separate setting. The step is saved, so a restart the walkthrough
/// itself causes (a language switch) comes back to it. Skipping leaves the first-run card on the map,
/// as before; people who had servers before the walkthrough existed never see it.
/// </summary>
public partial class MainWindow
{
    private enum ObStep
    {
        Welcome,
        Servers,
        Route,
        Mode,
        Done,
    }

    private const int ObStepCount = 5;

    private ObStep _obStep;
    private ContentControl? _obBody;
    private StackPanel? _obProgress;
    private TextBlock? _obProgressText;
    private Button? _obBack;
    private Button? _obSecondary;
    private Button? _obPrimary;
    private Action? _obPrimaryAction;
    private DispatcherTimer? _obTimer;
    private bool _obFinishing;

    // Servers step.
    private readonly List<Button> _obImportRows = [];
    private Button? _obMove;
    private Panel? _obStatus;
    private TextBlock? _obStatusText;
    private ShapePath? _obStatusSpin;
    private PathIcon? _obStatusTick;
    private StackPanel? _obImportOptions;
    private StackPanel? _obConnectionSummary;
    private TextBlock? _obSummaryServer;
    private TextBlock? _obSummaryRoute;
    private TextBlock? _obSummaryMode;
    private TextBlock? _obImportMessage;
    private string? _obImportError;

    // Route and mode steps.
    private readonly Dictionary<RoutingPresetKind, Button> _obRouteRows = [];
    private Panel? _obRouteBusy;
    private TextBlock? _obRouteBusyText;
    private readonly Dictionary<bool, Button> _obModeRows = [];

    private bool OnboardingShown => onboardingHost.IsVisible;

    private int ServerCount() => ViewModel?.ProfilesViewModel.AllServersCount ?? 0;

    /// <summary>On the first paint: the walkthrough for a fresh install, or for one a walkthrough restart left halfway.</summary>
    private async Task MaybeStartOnboardingAsync()
    {
        try
        {
            if (_config.UiItem.V2pnOnboarded)
            {
                return;
            }
            var hasServers = ((await AppManager.Instance.ProfileItems(string.Empty))?.Count ?? 0) > 0;
            if (hasServers && _config.UiItem.V2pnOnboardingStep == 0)
            {
                // Set up before the walkthrough existed (or moved whole from v2rayN): nothing to walk through.
                _config.UiItem.V2pnOnboarded = true;
                await ConfigHandler.SaveConfig(_config);
                return;
            }
            ShowOnboarding((ObStep)Math.Clamp(_config.UiItem.V2pnOnboardingStep, 0, ObStepCount - 1));
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn onboarding", ex);
        }
    }

    #region Shell

    private void ShowOnboarding(ObStep step)
    {
        if (!OnboardingShown)
        {
            // Its servers step offers to move from the apps found.
            _ = SearchImportSourcesAsync();
            BuildOnboardingShell();
            // The first-run card would show through the scrim; it comes back if the walkthrough is skipped.
            scrollWelcome.Opacity = 0;
            // The window under the scrim is out of reach for the mouse already; this takes it out of the
            // Tab order and screen readers too, so Connect cannot be pressed behind the walkthrough.
            gridBody.IsEnabled = false;
            onboardingHost.IsVisible = true;
            onboardingHost.Opacity = 1;
            _obTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => OnboardingTick());
            _obTimer.Start();
        }
        GoTo(step);
    }

    private void BuildOnboardingShell()
    {
        // The map stays faintly in sight behind: the walkthrough is part of the app, not a separate window.
        var scrim = new Border { Opacity = 0.96 };
        scrim.Bind(Border.BackgroundProperty, this.GetResourceObservable("V2ContentBackground"));

        _obProgress = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        _obProgressText = new TextBlock { Classes = { "muted", "small" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var skip = TextButton(L.ObSkip, "ghost");
        skip.Click += async (_, _) => await FinishOnboardingAsync(connect: false);
        var header = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 22) };
        header.Children.Add(_obProgress);
        Grid.SetColumn(_obProgressText, 1);
        header.Children.Add(_obProgressText);
        Grid.SetColumn(skip, 2);
        header.Children.Add(skip);

        _obBack = TextButton(L.Back, "ghost");
        _obBack.Click += (_, _) => GoTo(_obStep - 1);
        _obSecondary = TextButton(string.Empty, "ghost");
        _obPrimary = new Button
        {
            Theme = this.FindResource("V2Button") as ControlTheme,
            Height = 42,
            MinWidth = 140,
            Padding = new Thickness(22, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _obPrimary.Click += (_, _) => _obPrimaryAction?.Invoke();
        var footer = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 24, 0, 0) };
        footer.Children.Add(_obBack);
        // How the servers are coming along: in the footer, so it stays in sight while the step scrolls.
        _obStatusSpin = new ShapePath { Classes = { "spinner" }, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        _obStatusTick = Icon("SemiIconTickCircle", "i14");
        _obStatusTick.Margin = new Thickness(0, 0, 8, 0);
        _obStatusTick.Bind(ForegroundProperty, this.GetResourceObservable("V2PingGood"));
        _obStatusText = new TextBlock { Classes = { "small" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _obStatus = new DockPanel
        {
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            Children = { _obStatusSpin, _obStatusTick, _obStatusText },
        };
        DockPanel.SetDock(_obStatusSpin, Dock.Left);
        DockPanel.SetDock(_obStatusTick, Dock.Left);
        Grid.SetColumn(_obStatus, 1);
        footer.Children.Add(_obStatus);
        Grid.SetColumn(_obSecondary, 2);
        footer.Children.Add(_obSecondary);
        Grid.SetColumn(_obPrimary, 3);
        footer.Children.Add(_obPrimary);

        _obBody = new ContentControl();
        // Only the middle scrolls: the steps and the way forward stay in sight however long a step is.
        var bodyScroll = new ScrollViewer
        {
            Content = _obBody,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 4, 0),
        };
        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(header);
        layout.Children.Add(footer);
        layout.Children.Add(bodyScroll);

        var card = new Border
        {
            Classes = { "floating" },
            Width = 540,
            Margin = new Thickness(16),
            Padding = new Thickness(32, 26, 32, 26),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = layout,
        };
        // One width for every step, so the card does not jump between them; narrower only in a narrow window.
        void Fit(Size size)
        {
            card.MaxHeight = Math.Max(320, size.Height - 32);
            card.Width = size.Width > 0 ? Math.Min(540, size.Width - 32) : 540;
        }
        onboardingHost.SizeChanged += (_, e) => Fit(e.NewSize);
        Fit(onboardingHost.Bounds.Size);
        onboardingHost.Children.Clear();
        onboardingHost.Children.Add(scrim);
        onboardingHost.Children.Add(card);
    }

    private void GoTo(ObStep step)
    {
        if (_obBody == null || _obProgress == null)
        {
            return;
        }
        _obStep = (ObStep)Math.Clamp((int)step, 0, ObStepCount - 1);
        _config.UiItem.V2pnOnboardingStep = (int)_obStep;
        _ = ConfigHandler.SaveConfig(_config);

        // Bars, not dots: the length of the road is visible at once, and the filled part grows.
        _obProgress.Children.Clear();
        var visibleSteps = _obStep <= ObStep.Servers ? 2 : ObStepCount;
        for (var i = 0; i < visibleSteps; i++)
        {
            var bar = new Border { Width = 22, Height = 4, CornerRadius = new CornerRadius(2) };
            bar.Bind(Border.BackgroundProperty, this.GetResourceObservable(i <= (int)_obStep ? "V2Accent" : "V2Border"));
            _obProgress.Children.Add(bar);
        }
        _obProgressText!.Text = string.Format(L.ObStepOf, (int)_obStep + 1, visibleSteps);

        _obBack!.IsVisible = _obStep != ObStep.Welcome;
        _obSecondary!.IsVisible = false;
        _obStatus!.IsVisible = false;
        _obBody.Content = _obStep switch
        {
            ObStep.Welcome => BuildWelcomeStep(),
            ObStep.Servers => BuildServersStep(),
            ObStep.Route => BuildRouteStep(),
            ObStep.Mode => BuildModeStep(),
            _ => BuildDoneStep(),
        };
        if (_obBody.Content is Control content)
        {
            _ = Motion.Enter(content, dy: 8, ms: 220);
        }
    }

    private void SetPrimary(string text, bool accent, Action action)
    {
        _obPrimary!.Content = text;
        _obPrimary.Classes.Set("accent", accent);
        _obPrimaryAction = action;
    }

    private void OnboardingTick()
    {
        if (!OnboardingShown)
        {
            _obTimer?.Stop();
            return;
        }
        if (_obStep == ObStep.Servers)
        {
            RefreshServersStatus();
        }
    }

    private async Task FinishOnboardingAsync(bool connect)
    {
        if (_obFinishing || (connect && !_hasServer))
        {
            return;
        }
        _obFinishing = true;
        _config.UiItem.V2pnOnboarded = true;
        _config.UiItem.V2pnOnboardingStep = 0;
        await ConfigHandler.SaveConfig(_config);
        _obTimer?.Stop();
        onboardingHost.Transitions ??= [new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(200) }];
        onboardingHost.Opacity = 0;
        await Task.Delay(210);
        onboardingHost.IsVisible = false;
        onboardingHost.Children.Clear();
        scrollWelcome.Opacity = 1;
        gridBody.IsEnabled = true;
        if (connect && ServerCount() > 0)
        {
            BtnConnect_Click(null, new RoutedEventArgs());
        }
    }

    #endregion Shell

    #region Steps

    private Control BuildWelcomeStep()
    {
        var language = new ListBox
        {
            Classes = { "mode" },
            Theme = this.FindResource("V2Segmented") as ControlTheme,
            HorizontalAlignment = HorizontalAlignment.Left,
            Items =
            {
                new ListBoxItem { Content = "English", Tag = "en" },
                new ListBoxItem { Content = "Русский", Tag = "ru" },
            },
            SelectedIndex = CultureInfo.CurrentUICulture.Name.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
        };
        AutomationProperties.SetName(language, L.ObLanguage);
        language.SelectionChanged += async (_, _) =>
        {
            if (language.SelectedItem is ListBoxItem { Tag: string code } && !CultureInfo.CurrentUICulture.Name.StartsWith(code, StringComparison.OrdinalIgnoreCase))
            {
                await SwitchLanguageAsync(code);
            }
        };

        SetPrimary(L.ObStart, accent: true, () => GoTo(ObStep.Servers));
        return new StackPanel
        {
            Spacing = 0,
            Children =
            {
                new ShapePath
                {
                    Width = 34,
                    Height = 36,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Stretch = Stretch.Uniform,
                    Data = this.FindResource("V2BrandFork") as Geometry,
                    [!ShapePath.FillProperty] = this.GetResourceObservable("V2Accent").ToBinding(),
                    Margin = new Thickness(0, 4, 0, 20),
                },
                StepTitle(L.ObWelcomeTitle),
                StepLead(L.ObWelcomeLead),
                new TextBlock { Classes = { "sectionlabel" }, Text = L.ObLanguage, Margin = new Thickness(0, 26, 0, 8) },
                language,
            },
        };
    }

    /// <summary>
    /// The interface strings are read once when the window is built, so a new language needs a restart;
    /// the walkthrough comes back on its first step, in the new language.
    /// </summary>
    private async Task SwitchLanguageAsync(string code)
    {
        try
        {
            _config.UiItem.CurrentLanguage = code;
            _config.UiItem.V2pnOnboardingStep = (int)ObStep.Welcome;
            await ConfigHandler.SaveConfig(_config);
            CultureInfo.CurrentUICulture = new CultureInfo(code);
            ShowToast(ToastKind.Info, L.ObLanguageRestart);
            await Task.Delay(500);
            if (ProcUtils.RebootAsAdmin(false))
            {
                await AppManager.Instance.AppExitAsync(true);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn onboarding language", ex);
        }
    }

    private Control BuildServersStep()
    {
        var sources = _importSearch is { IsCompletedSuccessfully: true } done ? done.Result : _importSources ?? [];
        _importSources = sources;
        var usable = sources.Where(s => !s.Locked).ToList();
        var step = new StackPanel { Children = { StepTitle(L.ObServersTitle) } };
        var panel = _obImportOptions = new StackPanel();
        step.Children.Add(panel);
        panel.Children.Add(StepLead(usable.Count > 0 ? L.ObServersLeadFound : L.ObServersLead));

        _obImportRows.Clear();
        _obMove = null;
        // A provider's subscription without copying its link: the code confirmed in its bot (MainWindow.ProviderLogin.cs).
        // A person with an older partner build's marker gets it first, with that partner's mark: that is why they have V2pn.
        // Nothing is downloaded for the row; the list of providers comes when it is clicked.
        var loginFirst = V2pnEdition.Instance.Marker != null;
        if (loginFirst)
        {
            panel.Children.Add(new Border { Classes = { "routegroup" }, Margin = new Thickness(-4, 18, -4, sources.Count > 0 ? 0 : 10), Child = ProviderLoginRow() });
        }
        if (_importSearch is { IsCompleted: false })
        {
            // The search takes a few seconds on a first start; the step redraws itself when it ends.
            panel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(2, 16, 0, 0),
                Children =
                {
                    new ShapePath { Classes = { "spinner" }, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Classes = { "muted", "small" }, Text = L.ObLooking, VerticalAlignment = VerticalAlignment.Center },
                },
            });
            _ = _importSearch.ContinueWith(_ => Dispatcher.UIThread.Post(() =>
            {
                if (OnboardingShown && _obStep == ObStep.Servers)
                {
                    GoTo(ObStep.Servers);
                }
            }));
        }
        if (sources.Count > 0)
        {
            var rows = new StackPanel { Spacing = 2 };
            var ticks = usable.Count > 1;
            var all = usable.Concat(sources.Where(s => s.Locked)).ToList();
            // Four rows at most until asked: the ways to add by hand must not drop out of sight.
            var visible = all.Count > 5 ? 4 : all.Count;
            var hidden = new List<Control>();
            foreach (var (source, index) in all.Select((s, i) => (s, i)))
            {
                var row = ImportRow(source, ticks && !source.Locked, UpdateObMove);
                if (!source.Locked)
                {
                    _obImportRows.Add(row);
                }
                row.IsVisible = index < visible;
                if (!row.IsVisible)
                {
                    hidden.Add(row);
                }
                rows.Children.Add(row);
            }
            if (hidden.Count > 0)
            {
                var more = TextButton(string.Format(L.ImportMoreApps, hidden.Count) + "  ⌄", "subtle");
                more.Classes.Add("small");
                more.HorizontalAlignment = HorizontalAlignment.Left;
                more.Margin = new Thickness(4, 2, 0, 0);
                more.Click += (_, _) =>
                {
                    hidden.ForEach(r => r.IsVisible = true);
                    more.IsVisible = false;
                };
                rows.Children.Add(more);
            }
            panel.Children.Add(new Border { Classes = { "routegroup" }, Margin = new Thickness(-4, 18, -4, 0), Child = rows });
            if (usable.Count > 0)
            {
                _obMove = new Button
                {
                    Theme = this.FindResource("V2Button") as ControlTheme,
                    Classes = { "accent" },
                    Height = 42,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 14, 0, 0),
                };
                _obMove.Click += async (_, _) => await ObMoveAsync();
                panel.Children.Add(_obMove);
                UpdateObMove();
            }
            panel.Children.Add(Divider(L.ImportOrByHand));
        }

        if (!loginFirst)
        {
            panel.Children.Add(new Border { Classes = { "routegroup" }, Margin = new Thickness(-4, sources.Count > 0 ? 0 : 18, -4, 10), Child = ProviderLoginRow() });
        }
        var qr = Utils.IsWindows();
        var tiles = new Grid { ColumnDefinitions = new(qr ? "*,*,*" : "*,*"), ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 0) };
        var paste = HandTile("SemiIconCopy", L.HandPaste, L.ObPasteHint);
        paste.Click += async (_, _) =>
        {
            _obImportError = null;
            await AddServerViaClipboardAsync();
            RefreshServersStatus();
        };
        var sub = HandTile("SemiIconLink", L.HandSubscription, L.ObSubHint);
        sub.Click += (_, _) =>
        {
            _obImportError = null;
            ViewModel?.ProfilesViewModel.AddSubCmd.Execute().Subscribe();
        };
        tiles.Children.Add(paste);
        Grid.SetColumn(sub, 1);
        tiles.Children.Add(sub);
        if (qr)
        {
            var scan = HandTile("SemiIconScan", L.HandQr, L.ObQrHint);
            scan.Click += (_, _) => ViewModel?.AddServerViaScanCmd.Execute().Subscribe();
            Grid.SetColumn(scan, 2);
            tiles.Children.Add(scan);
        }
        panel.Children.Add(tiles);

        _obImportMessage = new TextBlock { Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
        _obImportMessage.Bind(ForegroundProperty, this.GetResourceObservable("V2Unsafe"));
        step.Children.Add(_obImportMessage);

        _obSummaryServer = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _obSummaryRoute = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _obSummaryMode = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var editConnection = TextButton(L.ObEditConnection, "ghost");
        editConnection.HorizontalAlignment = HorizontalAlignment.Left;
        editConnection.Click += (_, _) => GoTo(ObStep.Route);
        _obConnectionSummary = new StackPanel
        {
            Margin = new Thickness(0, 16, 0, 0),
            Spacing = 6,
            Children = { _obSummaryServer, _obSummaryMode, _obSummaryRoute, editConnection },
        };
        step.Children.Add(_obConnectionSummary);

        RefreshServersStatus();
        return step;
    }

    private void UpdateObMove()
    {
        if (_obMove == null)
        {
            return;
        }
        if (_obImportRows.Count == 1)
        {
            _obMove.Content = string.Format(L.ImportMoveFrom, ((ImportSource)_obImportRows[0].Tag!).Name);
            _obMove.IsEnabled = true;
            return;
        }
        var picked = _obImportRows.Count(r => r.Classes.Contains("active"));
        _obMove.Content = picked > 0 ? L.ImportMoveApps(picked) : L.ImportMoveSelected;
        _obMove.IsEnabled = picked > 0;
    }

    private async Task ObMoveAsync()
    {
        var picked = _obImportRows.Count == 1
            ? [(ImportSource)_obImportRows[0].Tag!]
            : _obImportRows.Where(r => r.Classes.Contains("active")).Select(r => (ImportSource)r.Tag!).ToList();
        if (picked.Count == 0 || _obMove == null)
        {
            return;
        }
        if (picked is [{ V2rayN: { } install }])
        {
            // The whole-folder move restarts V2pn with v2rayN's settings, routing included.
            await MoveFromV2rayNAsync(install, fromWelcome: true);
            return;
        }
        _obMove.IsEnabled = false;
        _obImportError = null;
        _obMove.Content = L.ObMoving;
        var moving = MoveForeignAsync(picked);
        // The subscriptions download one after another; the status line under the tiles counts the servers meanwhile.
        RefreshServersStatus();
        if (await moving)
        {
            _obMove.Content = "✓ " + L.ObMoved;
            _obImportRows.ForEach(r => r.IsEnabled = false);
        }
        else
        {
            UpdateObMove();
        }
        RefreshServersStatus();
    }

    private void RefreshServersStatus()
    {
        if (_obStatus == null || _obStep != ObStep.Servers)
        {
            return;
        }
        var count = ServerCount();
        var downloading = _subUpdatesRunning > 0;
        if (_obImportOptions != null)
        {
            _obImportOptions.IsVisible = count == 0;
        }
        if (_obImportMessage != null)
        {
            _obImportMessage.IsVisible = !downloading && count == 0 && _obImportError.IsNotEmpty();
            _obImportMessage.Text = _obImportError;
        }
        if (_obConnectionSummary != null)
        {
            _obConnectionSummary.IsVisible = count > 0;
            _obSummaryServer!.Text = string.Format(L.ObServerSummary, _serverName ?? L.NoServer);
            _obSummaryMode!.Text = string.Format(L.ObModeSummary, OnboardingConnectionMode());
            _obSummaryRoute!.Text = string.Format(L.ObRouteSummary, OnboardingRoute());
        }
        _obStatus.IsVisible = downloading || count > 0;
        _obStatusSpin!.IsVisible = downloading;
        _obStatusTick!.IsVisible = !downloading && count > 0;
        _obStatusText!.Text = downloading
            ? count > 0 ? $"{L.ObDownloading} {L.ServerCount(count)}" : L.ObDownloading
            : string.Format(L.ObServersReady, L.ServerCount(count));
        if (count > 0)
        {
            SetPrimary(L.ObConnect, accent: true, () => _ = FinishOnboardingAsync(connect: true));
        }
        else
        {
            SetPrimary(L.ObLater, accent: false, () => _ = FinishOnboardingAsync(connect: false));
        }
    }

    private string OnboardingConnectionMode() => _config.UiItem.V2pnPreferTun ? L.ModeVpn
        : ConnectionPrefs.ProxyMethod(_config) == ESysProxyType.Unchanged ? L.LocalOnly : L.ModeProxy;

    private string OnboardingRoute() =>
        RoutingPresets.KindOf(ViewModel?.StatusBarViewModel.SelectedRouting?.Id) is { } kind
            ? RoutingPresets.Title(kind)
            : ViewModel?.StatusBarViewModel.SelectedRouting?.Remarks ?? "—";

    private Control BuildRouteStep()
    {
        var rows = new StackPanel { Spacing = 2 };
        _obRouteRows.Clear();
        foreach (var (kind, icon, hint) in new[]
                 {
                     (RoutingPresetKind.RussiaDirect, "SemiIconBranch", L.RouteRussiaDirectHint),
                     (RoutingPresetKind.BlockedOnly, "SemiIconFilterStroked", L.RouteBlockedOnlyHint),
                     (RoutingPresetKind.All, "SemiIconGlobeStroke", L.RouteAllHint),
                 })
        {
            var row = OptionRow(icon, RoutingPresets.Title(kind), hint, recommended: kind == RoutingPresetKind.RussiaDirect);
            row.Click += async (_, _) => await SelectPresetAsync(kind);
            _obRouteRows[kind] = row;
            rows.Children.Add(row);
        }

        var ads = new ToggleSwitch
        {
            IsChecked = togBlockAds.IsChecked,
            OnContent = null,
            OffContent = null,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(ads, L.BlockAds);
        // The main screen's switch does the work, so both stay in step.
        ads.IsCheckedChanged += (_, _) => togBlockAds.IsChecked = ads.IsChecked;
        var adsText = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Classes = { "routetitle" }, Text = L.BlockAds },
                new TextBlock { Classes = { "routehint" }, Text = L.BlockAdsHint, TextWrapping = TextWrapping.Wrap },
            },
        };
        var adsIcon = new Border { Classes = { "routeicon", "plain" }, Child = Icon("SemiIconShield", "i16") };
        Grid.SetColumn(adsText, 1);
        Grid.SetColumn(ads, 2);
        var adsRow = new Grid
        {
            ColumnDefinitions = new("Auto,*,Auto"),
            ColumnSpacing = 12,
            Margin = new Thickness(8, 10, 4, 0),
            Children = { adsIcon, adsText, ads },
        };

        _obRouteBusyText = new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        _obRouteBusy = new DockPanel
        {
            Margin = new Thickness(2, 12, 0, 0),
            IsVisible = false,
            Children = { new ShapePath { Classes = { "spinner" }, Margin = new Thickness(0, 0, 8, 0), [DockPanel.DockProperty] = Dock.Left }, _obRouteBusyText },
        };

        SetPrimary(L.ObNext, accent: true, () => GoTo(ObStep.Mode));
        RefreshOnboardingRoute();
        return new StackPanel
        {
            Children =
            {
                StepTitle(L.ObRouteTitle),
                StepLead(L.ObRouteLead),
                new Border { Classes = { "routegroup" }, Margin = new Thickness(-4, 18, -4, 0), Child = rows },
                _obRouteBusy,
                adsRow,
            },
        };
    }

    /// <summary>The route rows follow the active rule set, whoever changed it.</summary>
    private void RefreshOnboardingRoute()
    {
        if (_obRouteRows.Count == 0)
        {
            return;
        }
        var kind = RoutingPresets.KindOf(ViewModel?.StatusBarViewModel.SelectedRouting?.Id);
        foreach (var (k, row) in _obRouteRows)
        {
            row.Classes.Set("active", k == kind);
        }
    }

    /// <summary>The Russian mode downloads its lists first; the step says so instead of looking stuck.</summary>
    private void OnboardingRouteBusy(string? text, bool failed)
    {
        if (_obRouteBusy == null || _obStep != ObStep.Route)
        {
            return;
        }
        _obRouteBusy.IsVisible = text != null;
        _obRouteBusyText!.Text = text;
        if (_obRouteBusy.Children.FirstOrDefault() is ShapePath spin)
        {
            spin.IsVisible = !failed;
        }
        foreach (var row in _obRouteRows.Values)
        {
            row.IsEnabled = text == null || failed;
        }
    }

    private Control BuildModeStep()
    {
        var needsAdmin = Utils.IsWindows() && !Utils.IsAdministrator();
        var rows = new StackPanel { Spacing = 2 };
        _obModeRows.Clear();
        var proxy = OptionRow("SemiIconGlobeStroke", L.ModeProxy, L.ObModeProxyHint);
        var vpn = OptionRow("SemiIconShieldStroked", L.ModeVpn, needsAdmin ? $"{L.ObModeVpnHint} {L.ObModeVpnAdmin}" : L.ObModeVpnHint);
        _obModeRows[false] = proxy;
        _obModeRows[true] = vpn;
        foreach (var (tun, row) in _obModeRows)
        {
            // Only the choice is saved: the administrator question comes with the first connection,
            // the way the switch under Connect does it, instead of restarting in the middle of the walkthrough.
            row.Click += async (_, _) =>
            {
                _config.UiItem.V2pnPreferTun = tun;
                await ConfigHandler.SaveConfig(_config);
                SyncModeSelection();
                RefreshOnboardingMode();
            };
            rows.Children.Add(row);
        }
        SetPrimary(L.ObNext, accent: true, () => GoTo(ObStep.Done));
        RefreshOnboardingMode();
        return new StackPanel
        {
            Children =
            {
                StepTitle(L.ObModeTitle),
                StepLead(L.ObModeLead),
                new Border { Classes = { "routegroup" }, Margin = new Thickness(-4, 18, -4, 0), Child = rows },
            },
        };
    }

    private void RefreshOnboardingMode()
    {
        foreach (var (tun, row) in _obModeRows)
        {
            row.Classes.Set("active", tun == _config.UiItem.V2pnPreferTun);
        }
    }

    private Control BuildDoneStep()
    {
        var count = ServerCount();
        var ready = count > 0;
        var badge = new Border
        {
            Width = 52,
            Height = 52,
            CornerRadius = new CornerRadius(26),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 2, 0, 18),
            Child = Icon(ready ? "SemiIconTickCircle" : "SemiIconInfoCircle", "i24"),
        };
        badge.Bind(Border.BackgroundProperty, this.GetResourceObservable(ready ? "V2PingGoodSoft" : "V2AccentSoft"));
        if (badge.Child is PathIcon icon)
        {
            icon.Width = icon.Height = 26;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Bind(ForegroundProperty, this.GetResourceObservable(ready ? "V2PingGood" : "V2Accent"));
        }

        var summary = new StackPanel
        {
            Spacing = 0,
            Children =
            {
                SummaryRow(L.ObSumServers, ready ? L.ServerCount(count) : L.ObNone, ObStep.Servers),
                SummaryRow(L.ObSumServer, ready && txtHeaderTitle.Text.IsNotEmpty() ? txtHeaderTitle.Text! : "—", null),
                SummaryRow(L.ObSumRoute, OnboardingRoute(), ObStep.Route),
                SummaryRow(L.ObSumMode, OnboardingConnectionMode(), ObStep.Mode),
            },
        };
        var summaryBox = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 4),
            Margin = new Thickness(0, 20, 0, 0),
            Child = summary,
        };
        summaryBox.Bind(Border.BackgroundProperty, this.GetResourceObservable("V2CardMutedBackground"));

        if (ready)
        {
            SetPrimary(L.ObConnect, accent: true, () => _ = FinishOnboardingAsync(connect: true));
            _obSecondary!.Content = L.ObNotNow;
            _obSecondary.IsVisible = true;
            _obSecondary.Click -= ObNotNow;
            _obSecondary.Click += ObNotNow;
        }
        else
        {
            SetPrimary(L.ObFinish, accent: true, () => _ = FinishOnboardingAsync(connect: false));
        }
        return new StackPanel
        {
            Children =
            {
                badge,
                StepTitle(ready ? L.ObDoneTitle : L.ObDoneNoServersTitle),
                StepLead(ready ? L.ObDoneLead : L.ObDoneNoServersLead),
                summaryBox,
            },
        };
    }

    private void ObNotNow(object? sender, RoutedEventArgs e) => _ = FinishOnboardingAsync(connect: false);

    #endregion Steps

    #region Pieces

    private static TextBlock StepTitle(string text) =>
        new() { Classes = { "display", "h3" }, Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 24 };

    private static TextBlock StepLead(string text) =>
        new() { Classes = { "muted" }, Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(0, 8, 0, 0) };

    private Button TextButton(string text, string cls) =>
        new() { Theme = this.FindResource("V2Button") as ControlTheme, Classes = { cls }, Content = text, VerticalAlignment = VerticalAlignment.Center };

    private PathIcon Icon(string key, string cls) =>
        new() { Classes = { cls }, Data = this.TryFindResource(key, out var geometry) ? geometry as Geometry : null };

    private static Grid Divider(string text)
    {
        var line = () => new Border { Classes = { "hairline" }, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Classes = { "muted", "small" }, Text = text };
        var right = line();
        Grid.SetColumn(label, 1);
        Grid.SetColumn(right, 2);
        return new Grid
        {
            ColumnDefinitions = new("*,Auto,*"),
            ColumnSpacing = 10,
            Margin = new Thickness(0, 18, 0, 14),
            Children = { line(), label, right },
        };
    }

    /// <summary>A way to add a server: an icon, what it is and a hint, in a tile of its own.</summary>
    private Button HandTile(string icon, string title, string hint)
    {
        var glyph = Icon(icon, "i16");
        glyph.Width = glyph.Height = 20;
        glyph.HorizontalAlignment = HorizontalAlignment.Left;
        var tile = new Button
        {
            Theme = this.FindResource("V2Button") as ControlTheme,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 14),
            Content = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    glyph,
                    new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Classes = { "muted", "small" }, Text = hint, TextWrapping = TextWrapping.Wrap },
                },
            },
        };
        AutomationProperties.SetName(tile, title);
        return tile;
    }

    /// <summary>An option in the same look as the routing modes: icon, title, hint, radio.</summary>
    private Button OptionRow(string icon, string title, string hint, bool recommended = false)
    {
        var titleLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleLine.Children.Add(new TextBlock { Classes = { "routetitle" }, Text = title });
        if (recommended)
        {
            titleLine.Children.Add(new Border { Classes = { "recommend" }, Child = new TextBlock { Text = L.Recommended } });
        }
        var text = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { titleLine, new TextBlock { Classes = { "routehint" }, Text = hint, TextWrapping = TextWrapping.Wrap } },
        };
        var radio = new Panel
        {
            Classes = { "routeradio" },
            Children = { new Ellipse { Classes = { "routering" } }, new Ellipse { Classes = { "routedot" } } },
        };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(radio, 2);
        var row = new Button
        {
            Theme = this.FindResource("V2SideRow") as ControlTheme,
            Classes = { "routeoption" },
            Content = new Grid
            {
                ColumnDefinitions = new("Auto,*,Auto"),
                ColumnSpacing = 12,
                Children = { new Border { Classes = { "routeicon" }, Child = Icon(icon, "i16") }, text, radio },
            },
        };
        AutomationProperties.SetName(row, title);
        return row;
    }

    private Grid SummaryRow(string label, string value, ObStep? step)
    {
        var change = TextButton(L.ObChange, "ghost");
        change.Classes.Add("small");
        change.Bind(ForegroundProperty, this.GetResourceObservable("V2Accent"));
        change.IsVisible = step != null;
        change.Click += (_, _) => GoTo(step!.Value);
        var name = new TextBlock { Classes = { "muted" }, Text = label, VerticalAlignment = VerticalAlignment.Center };
        var shown = new TextBlock { Text = value, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(shown, 1);
        Grid.SetColumn(change, 2);
        return new Grid
        {
            ColumnDefinitions = new("120,*,Auto"),
            ColumnSpacing = 8,
            MinHeight = 44,
            Children = { name, shown, change },
        };
    }

    #endregion Pieces
}
