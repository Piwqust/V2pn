using v2rayN.Desktop.Base;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class OptionSettingWindow : WindowBase<OptionSettingViewModel>
{
    private static Config _config;

    public OptionSettingWindow()
    {
        InitializeComponent();

        SettingsKit.ApplyPlatform(this);
        // V2pn: administrator rights and the VPN-mode block are Windows' (the rows are tagged "win"; this line is not a row).
        txtKillSwitchAdminNote.IsVisible = Utils.IsWindows();
        _advanced = ConfigTextHandler.Config.UiItem.V2pnSettingsAdvanced;
        togAdvanced.IsChecked = _advanced;
        SettingsKit.ApplyAdvanced(this, _advanced);
        togAdvanced.IsCheckedChanged += (_, _) => SetAdvanced(togAdvanced.IsChecked == true);
        lstNav.SelectionChanged += (_, _) => ShowPage(lstNav.SelectedIndex);
        txtSearch.TextChanged += (_, _) => ApplySearch();

        Loaded += Window_Loaded;
        btnCancel.Click += (s, e) => Close();
        InitMoreLinks();
        _config = ConfigTextHandler.Config;
        InitBypass();

        clbdestOverride.SelectionChanged += ClbdestOverride_SelectionChanged;
        btnBrowseCustomSystemProxyPacPath.Click += BtnBrowseCustomSystemProxyPacPath_Click;
        btnAppKillSwitchClaude.Click += (_, _) => AddKillSwitchApps(V2pnAppKillSwitch.ClaudePreset);
        btnAppKillSwitchPick.Click += BtnAppKillSwitchPick_Click;
        var appliedApps = V2pnAppKillSwitch.Applied();
        txtAppKillSwitchState.Text = appliedApps.Count == 0 ? L.AppKillSwitchNone : L.AppKillSwitchNow(string.Join(", ", appliedApps));
        if (ViewModel != null)
        {
            ViewModel.AppKillSwitchFailedText = L.AppKillSwitchFailed;
        }
        btnBrowseCustomSystemProxyScriptPath.Click += BtnBrowseCustomSystemProxyScriptPath_Click;

        clbdestOverride.ItemsSource = Global.destOverrideProtocols;
        _config.Inbound.First().DestOverride?.ForEach(it =>
        {
            clbdestOverride.SelectedItems.Add(it);
        });

        cmbsystemProxyAdvancedProtocol.ItemsSource = Global.IEProxyProtocols;
        cmbloglevel.ItemsSource = Global.LogLevels;
        cmbdefFingerprint.ItemsSource = Global.Fingerprints;
        cmbdefUserAgent.ItemsSource = Global.UserAgent;
        cmbmux4SboxProtocol.ItemsSource = Global.SingboxMuxs;
        cmbmux4RayXudpProxyUDP443.ItemsSource = new[] { "reject", "skip" };
        cmbMtu.ItemsSource = Global.TunMtus;
        cmbStack.ItemsSource = Global.TunStacks;
        cmbDnsMode.ItemsSource = Global.TunDnsModes;
        cmbIcmpRoutingPolicy.ItemsSource = Global.TunIcmpRoutingPolicies;
        cmbIpv4Address.ItemsSource = Global.TunIPv4Address;
        cmbIpv6Address.ItemsSource = Global.TunIPv6Address;
        cmbFragmentPackets.ItemsSource = Global.FragmentPacketsOptions;

        cmbCoreType1.ItemsSource = Global.CoreTypes;
        cmbCoreType2.ItemsSource = Global.CoreTypes;
        cmbCoreType3.ItemsSource = Global.CoreTypes;
        cmbCoreType4.ItemsSource = Global.CoreTypes;
        cmbCoreType5.ItemsSource = Global.CoreTypes;
        cmbCoreType6.ItemsSource = Global.CoreTypes;
        cmbCoreType7.ItemsSource = Global.CoreTypes;
        cmbCoreType9.ItemsSource = Global.CoreTypes;
        cmbCoreType14.ItemsSource = Global.CoreTypes;

        cmbMixedConcurrencyCount.ItemsSource = Enumerable.Range(Global.SpeedTestConcurrencyCountMin, 20).ToList();
        cmbSpeedTestTimeout.ItemsSource = Enumerable.Range(2, 5).Select(i => i * 5).ToList();
        cmbSpeedTestUrl.ItemsSource = Global.SpeedTestUrls;
        cmbSpeedPingTestUrl.ItemsSource = Global.SpeedPingTestUrls;
        cmbUdpTestTarget.ItemsSource = Global.UdpTestTargets;
        cmbSubConvertUrl.ItemsSource = Global.SubConvertUrls;
        cmbGetFilesSourceUrl.ItemsSource = Global.GeoFilesSources;
        cmbSrsFilesSourceUrl.ItemsSource = Global.SingboxRulesetSources;
        cmbRoutingRulesSourceUrl.ItemsSource = Global.RoutingRulesSources;
        cmbIPAPIUrl.ItemsSource = Global.IPAPIUrls;
        cmbRootCertificateProvider.ItemsSource = Global.RootCertProviders;
        cmbbindInterface.ItemsSource = NetworkUtils.GetBindInterfaceNames();

        cmbMainGirdOrientation.ItemsSource = Utils.GetEnumNames<EGirdOrientation>();

        _ = InitSettingFont();
        InitConnectionModes();
        InitControlLevel();

        this.WhenActivated(disposables =>
        {
            this.Bind(ViewModel, vm => vm.LocalPort, v => v.txtlocalPort.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SecondLocalPortEnabled, v => v.togSecondLocalPortEnabled.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.UdpEnabled, v => v.togudpEnabled.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SniffingEnabled, v => v.togsniffingEnabled.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.RouteOnly, v => v.togrouteOnly.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AllowLANConn, v => v.togAllowLANConn.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.NewPort4LAN, v => v.togNewPort4LAN.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.NewPort4LAN, v => v.txtuser.IsEnabled).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.NewPort4LAN, v => v.txtpass.IsEnabled).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.User, v => v.txtuser.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Pass, v => v.txtpass.Text).DisposeWith(disposables);
            // Without the separate port and credentials the main port listens on every interface, unprotected.
            this.WhenAnyValue(v => v.ViewModel.AllowLANConn, v => v.ViewModel.NewPort4LAN, v => v.ViewModel.User, v => v.ViewModel.Pass,
                    (lan, ownPort, user, pass) => lan && !(ownPort && user.IsNotEmpty() && pass.IsNotEmpty()))
                .Subscribe(open => txtLanWarning.IsVisible = open)
                .DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.LogEnabled, v => v.toglogEnabled.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Loglevel, v => v.cmbloglevel.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.DefFingerprint, v => v.cmbdefFingerprint.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.DefUserAgent, v => v.cmbdefUserAgent.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.BindInterface, v => v.cmbbindInterface.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Mux4RayConcurrency, v => v.txtmux4RayConcurrency.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Mux4RayXudpConcurrency, v => v.txtmux4RayXudpConcurrency.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Mux4RayXudpProxyUDP443, v => v.cmbmux4RayXudpProxyUDP443.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SendThrough, v => v.txtsendThrough.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Mux4SboxProtocol, v => v.cmbmux4SboxProtocol.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableCacheFile4Sbox, v => v.togenableCacheFile4Sbox.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.HyUpMbps, v => v.txtUpMbps.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.HyDownMbps, v => v.txtDownMbps.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableFragment, v => v.togenableFragment.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableFinalFragment, v => v.togenableFinalFragment.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.FragmentPackets, v => v.cmbFragmentPackets.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.FragmentLengths, v => v.txtFragmentLengths.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.FragmentDelays, v => v.txtFragmentDelays.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.FragmentMaxSplit, v => v.txtFragmentMaxSplit.Text).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.AutoRun, v => v.togAutoRun.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableStatistics, v => v.togEnableStatistics.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.DisplayRealTimeSpeed, v => v.togDisplayRealTimeSpeed.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.KeepOlderDedupl, v => v.togKeepOlderDedupl.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AutoHideStartup, v => v.togAutoHideStartup.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Hide2TrayWhenClose, v => v.togHide2TrayWhenClose.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.MacOSShowInDock, v => v.togMacOSShowInDock.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableDragDropSort, v => v.togEnableDragDropSort.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.DoubleClick2Activate, v => v.togDoubleClick2Activate.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AutoUpdateInterval, v => v.txtautoUpdateInterval.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CurrentFontFamily, v => v.cmbcurrentFontFamily.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SubConvertUrl, v => v.cmbSubConvertUrl.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.MainGirdOrientation, view => view.cmbMainGirdOrientation.SelectedIndex).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.GeoFileSourceUrl, v => v.cmbGetFilesSourceUrl.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SrsFileSourceUrl, v => v.cmbSrsFilesSourceUrl.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.RoutingRulesSourceUrl, v => v.cmbRoutingRulesSourceUrl.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.RootCertProvider, v => v.cmbRootCertificateProvider.SelectedValue).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.SpeedTestTimeout, v => v.cmbSpeedTestTimeout.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SpeedTestUrl, v => v.cmbSpeedTestUrl.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SpeedPingTestUrl, v => v.cmbSpeedPingTestUrl.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.UdpTestTarget, v => v.cmbUdpTestTarget.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.MixedConcurrencyCount, v => v.cmbMixedConcurrencyCount.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.IPAPIUrl, v => v.cmbIPAPIUrl.Text).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.NotProxyLocalAddress, v => v.tognotProxyLocalAddress.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SystemProxyAdvancedProtocol, v => v.cmbsystemProxyAdvancedProtocol.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SystemProxyExceptions, v => v.txtsystemProxyExceptions.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CustomSystemProxyPacPath, v => v.txtCustomSystemProxyPacPath.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CustomSystemProxyScriptPath, v => v.txtCustomSystemProxyScriptPath.Text).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.TunAutoRoute, v => v.togAutoRoute.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunStrictRoute, v => v.togStrictRoute.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunStack, v => v.cmbStack.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunMtu, v => v.cmbMtu.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunEnableIPv6Address, v => v.togEnableIPv6Address.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunIcmpRouting, v => v.cmbIcmpRoutingPolicy.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunEnableLegacyProtect, v => v.togEnableLegacyProtect.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunDnsMode, v => v.cmbDnsMode.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunRouteExcludeAddress, v => v.txtRouteExcludeAddress.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunIPv4Address, v => v.cmbIpv4Address.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunIPv6Address, v => v.cmbIpv6Address.SelectedValue).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.CoreType1, v => v.cmbCoreType1.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CoreType2, v => v.cmbCoreType2.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CoreType3, v => v.cmbCoreType3.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CoreType4, v => v.cmbCoreType4.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CoreType5, v => v.cmbCoreType5.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CoreType6, v => v.cmbCoreType6.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CoreType7, v => v.cmbCoreType7.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CoreType9, v => v.cmbCoreType9.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CoreType14, v => v.cmbCoreType14.SelectedValue).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.KillSwitchEnabled, v => v.togKillSwitch.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AppKillSwitchEnabled, v => v.togAppKillSwitch.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AppKillSwitchApps, v => v.txtAppKillSwitchApps.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.AppKillSwitchEnabled, v => v.rowAppKillSwitchList.IsEnabled).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.KillSwitchAllowLan, v => v.togKillSwitchLan.IsChecked).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.KillSwitchEnabled, v => v.rowKillSwitchLan.IsEnabled).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.BypassEnabled, v => v.togBypassEnabled.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.BypassCalm, v => v.togBypassCalm.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.BypassAutoTune, v => v.togBypassAutoTune.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.BypassPerNetwork, v => v.togBypassPerNetwork.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.BypassDirectFragment, v => v.togBypassDirectFragment.IsChecked).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BypassEnabled, v => v.panelBypassOptions.IsEnabled).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.SaveCmd, v => v.btnSave).DisposeWith(disposables);
        });
    }

    private async Task InitSettingFont()
    {
        // The list the main window gathered in the background after the start, else gathered now.
        var lstFonts = _fonts is { IsCompletedSuccessfully: true } ready ? [.. ready.Result] : await GetFonts();

        lstFonts.Add(string.Empty);
        cmbcurrentFontFamily.ItemsSource = lstFonts;
    }

    private static Task<List<string>>? _fonts;

    /// <summary>
    /// Gathers the installed fonts off the window's thread, once. The first opening of Settings read them there and
    /// waited (about 70 ms on an ordinary system, far more with thousands of fonts). Avalonia keeps the system font
    /// list for the whole run anyway, so a font installed later shows only after a restart, as it did before.
    /// </summary>
    public static void PrefetchFonts() => _fonts ??= Task.Run(GetFonts);

    private static async Task<List<string>> GetFonts()
    {
        await Task.CompletedTask;

        var lstFonts = new List<string>();
        try
        {
            var lst = FontManager.Current.SystemFonts
                .Select(t => t.Name)
                .Append(GetDefaultFontFamilyName())
                .Where(IsInstalledFontFamilyName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t)
                .ToList();
            return lst;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("GetFonts", ex);
        }
        return lstFonts;
    }

    private static string GetDefaultFontFamilyName() => "Noto Sans SC";

    private static bool IsInstalledFontFamilyName(string fontFamilyName)
    {
        return fontFamilyName.IsNotEmpty()
               && !IsCssFontFamilyAlias(fontFamilyName);
    }

    private static bool IsCssFontFamilyAlias(string fontFamilyName)
    {
        return fontFamilyName.StartsWith("-", StringComparison.Ordinal)
               || fontFamilyName.Equals("BlinkMacSystemFont", StringComparison.OrdinalIgnoreCase);
    }

    private void ClbdestOverride_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.DestOverride = clbdestOverride.SelectedItems.Cast<string>().ToList();
        }
    }

    private void AddKillSwitchApps(IEnumerable<string> names)
    {
        if (ViewModel == null)
        {
            return;
        }
        var list = V2pnAppKillSwitch.Names((ViewModel.AppKillSwitchApps ?? string.Empty).Split('\n').Concat(names));
        ViewModel.AppKillSwitchApps = string.Join(Environment.NewLine, list);
        ViewModel.AppKillSwitchEnabled = true;
    }

    private async void BtnAppKillSwitchPick_Click(object? sender, RoutedEventArgs e)
    {
        var fileName = await UI.OpenFileDialog(null);
        if (V2pnAppKillSwitch.Normalize(fileName) is { } name)
        {
            AddKillSwitchApps([name]);
        }
    }

    private async void BtnBrowseCustomSystemProxyPacPath_Click(object? sender, RoutedEventArgs e)
    {
        var fileName = await UI.OpenFileDialog(null);
        if (fileName.IsNullOrEmpty())
        {
            return;
        }

        txtCustomSystemProxyPacPath.Text = fileName;
    }

    private async void BtnBrowseCustomSystemProxyScriptPath_Click(object? sender, RoutedEventArgs e)
    {
        var fileName = await UI.OpenFileDialog(null);
        if (fileName.IsNullOrEmpty())
        {
            return;
        }

        txtCustomSystemProxyScriptPath.Text = fileName;
    }

    private void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        // Again once the list has realised its items: preparing them resets their visibility.
        SettingsKit.ApplyAdvanced(this, _advanced);
        btnCancel.Focus();
        if (ViewModel?.StartPage is > 0 and var page)
        {
            lstNav.SelectedIndex = page;
            if (page == 1)
            {
                txtlocalPort.Focus();
                txtlocalPort.SelectAll();
            }
        }
    }

    private Control[] Pages => [pageGeneral, pageInbound, pageSysProxy, pageTun, pageKillSwitch, pageBypass, pageTraffic, pageTests, pageUpdates, pageCores, pageLogs];

    private void ShowPage(int index)
    {
        if (txtSearch.Text.IsNotEmpty() || index < 0)
        {
            return;
        }
        var pages = Pages;
        for (var i = 0; i < pages.Length; i++)
        {
            pages[i].IsVisible = i == index;
        }
        svContent.Offset = default;
    }

    private bool _advanced;

    // The main window's commands open these, so a saved change reloads the core as it did from the old
    // menu. The dialog service gives the new window the active one, this window, as its owner.
    private void InitMoreLinks()
    {
        btnMoreHotkeys.IsVisible = Utils.IsWindows();
        btnMoreTemplate.IsVisible = _advanced;
        btnMoreRouting.Click += (_, _) => RunMain(vm => vm.RoutingSettingCmd.Execute().Subscribe());
        btnMoreDns.Click += (_, _) => RunMain(vm => vm.DNSSettingCmd.Execute().Subscribe());
        btnMoreHotkeys.Click += (_, _) => RunMain(vm => vm.GlobalHotkeySettingCmd.Execute().Subscribe());
        btnMoreTemplate.Click += (_, _) => RunMain(vm => vm.FullConfigTemplateCmd.Execute().Subscribe());
    }

    // V2pn: "Обход блокировок". The network check opens in the main window once this one is closed: the main
    // window's dialogs show inside it, so one opened from here would sit behind this window.
    private void InitBypass()
    {
        RefreshBypassKept();
        btnBypassForgetAll.Click += async (_, _) =>
        {
            V2pnBypass.ForgetAll(_config);
            await ConfigTextHandler.SaveConfig();
            RefreshBypassKept();
        };
        btnBypassCheck.Click += (_, _) =>
        {
            if ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is MainWindow main)
            {
                Close();
                main.OpenBypass();
            }
        };
    }

    private void RefreshBypassKept()
    {
        var count = _config.V2pnBypassItem?.Learned?.Count ?? 0;
        txtBypassKept.Text = count > 0 ? string.Format(L.BypassKept, count) : L.BypassNoneKept;
        btnBypassForgetAll.IsEnabled = count > 0;
    }

    private static void RunMain(Action<MainWindowViewModel> action)
    {
        if ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is MainWindow { ViewModel: { } vm })
        {
            action(vm);
        }
    }

    private void SetAdvanced(bool show)
    {
        _advanced = show;
        // The config template is an expert tool, like the advanced pages.
        btnMoreTemplate.IsVisible = show;
        _config.UiItem.V2pnSettingsAdvanced = show;
        SettingsKit.ApplyAdvanced(this, show || txtSearch.Text.IsNotEmpty());
        // Leaving an expert-only page when going back to simple settings.
        if (!show && lstNav.SelectedItem is ListBoxItem { } item && item.Classes.Contains("adv"))
        {
            lstNav.SelectedIndex = 0;
        }
    }

    // While searching every page is shown, filtered down to the matching rows; search also reaches
    // the advanced rows, so a setting can always be found by name.
    private void ApplySearch()
    {
        var query = txtSearch.Text;
        SettingsKit.ApplyAdvanced(this, _advanced || query.IsNotEmpty());
        var found = SettingsKit.Search(Pages, query);
        if (query.IsNullOrEmpty())
        {
            ShowPage(Math.Max(lstNav.SelectedIndex, 0));
        }
        else
        {
            foreach (var page in Pages)
            {
                page.IsVisible = !page.Classes.Contains("nomatch");
            }
            svContent.Offset = default;
        }
        txtNoResults.IsVisible = !found;
    }

    // V2pn: the main window's Proxy / VPN switch; these rows say what each one does on Connect.
    private static readonly ESysProxyType[] ProxyMethods = Utils.IsWindows()
        ? [ESysProxyType.ForcedChange, ESysProxyType.Pac, ESysProxyType.Unchanged]
        : [ESysProxyType.ForcedChange, ESysProxyType.Unchanged];

    private void InitConnectionModes()
    {
        cmbProxyMethod.ItemsSource = ProxyMethods.Select(m => m switch
        {
            ESysProxyType.Pac => L.ProxyMethodPac,
            ESysProxyType.Unchanged => L.ProxyModeKeep,
            _ => L.ProxyMethodSystem,
        }).ToList();
        var current = ConnectionPrefs.ProxyMethod(_config);
        cmbProxyMethod.SelectedIndex = Math.Max(0, Array.IndexOf(ProxyMethods, current));
        btnSave.Click += (_, _) => SaveProxyMethod();
        // Saved with the rest by the Save command, which runs after Click.
        togAutoSwitch.IsChecked = V2pnAutoSwitch.Enabled(_config);
        btnSave.Click += (_, _) => _config.UiItem.V2pnAutoSwitch = togAutoSwitch.IsChecked == true;

        var admin = Utils.IsAdministrator();
        txtTunAdminState.Text = admin ? L.TunAdminYes : L.TunAdminNo;
        btnTunRestartAdmin.IsVisible = !admin;
        btnTunRestartAdmin.Click += async (_, _) => await AppManager.Instance.RebootAsAdmin();
    }

    // V2pn: Off, Read only, Control, in the order of EV2pnControlLevel. Saved with the rest by the Save command.
    private void InitControlLevel()
    {
        cmbControl.ItemsSource = new[] { L.ControlOff, L.ControlReadOnly, L.ControlFull };
        cmbControl.SelectedIndex = Math.Clamp((int)_config.UiItem.V2pnControl, 0, 2);
        btnSave.Click += (_, _) => _config.UiItem.V2pnControl = (EV2pnControlLevel)Math.Max(0, cmbControl.SelectedIndex);
    }

    // Connected in Proxy mode, the live mode switches too, so the change shows right away (V2pnConnectionService).
    private static void SaveProxyMethodTo(ESysProxyType method) => V2pnConnectionService.Instance.SetProxyMethod(method);

    // Runs before the Save command (Click is raised before Command executes), so the command saves it.
    private void SaveProxyMethod()
    {
        if (cmbProxyMethod.SelectedIndex >= 0)
        {
            SaveProxyMethodTo(ProxyMethods[cmbProxyMethod.SelectedIndex]);
        }
    }
}
