using Avalonia.LogicalTree;
using DialogHostAvalonia;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class ProfilesView : ReactiveUserControl<ProfilesViewModel>
{
    private static Config _config;
    private MainWindowViewModel? _mainViewModel;

    public ProfilesView()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;

        menuSelectAll.Click += (_, _) => lstProfiles.SelectAll();
        txtServerFilter.KeyDown += TxtServerFilter_KeyDown;
        lstProfiles.KeyDown += LstProfiles_KeyDown;
        lstProfiles.SelectionChanged += LstProfiles_SelectionChanged;
        lstProfiles.DoubleTapped += LstProfiles_DoubleTapped;
        lstProfiles.ContextRequested += LstProfiles_ContextRequested;
        menuStopTests.Click += (_, _) => ViewModel?.ServerSpeedtestStop();
        menuMoveToGroup.AddHandler(MenuItem.ClickEvent, MenuMoveToGroup_Click);
        btnEmptyPaste.Click += async (_, _) => await PasteFromClipboardAsync();
        menuAddServerViaClipboard.Click += async (_, _) => await PasteFromClipboardAsync();

        foreach (var btn in new[] { btnSortName, btnSortType, btnSortSub, btnSortPing })
        {
            btn.Click += SortHeader_Click;
        }

        if (!Utils.IsWindows())
        {
            menuAddServerViaScan.IsVisible = false;
        }

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.ProfileItems, v => v.lstProfiles.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedProfile, v => v.lstProfiles.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.ServerFilter, v => v.txtServerFilter.Text).DisposeWith(disposables);

            //servers
            this.BindCommand(ViewModel, vm => vm.EditServerCmd, v => v.menuEditServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RemoveServerCmd, v => v.menuRemoveServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RemoveDuplicateServerCmd, v => v.menuRemoveDuplicateServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.CopyServerCmd, v => v.menuCopyServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SetDefaultServerCmd, v => v.menuSetDefaultServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ShareServerCmd, v => v.menuShareServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.GenGroupAllServerCmd, v => v.menuGenGroupAllServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.GenGroupRegionServerCmd, v => v.menuGenGroupRegionServer).DisposeWith(disposables);
            menuToggleFavorite.Command = ViewModel.ToggleFavoriteCmd;
            menuToggleFavorite.CommandParameter = null;

            //move
            this.BindCommand(ViewModel, vm => vm.MoveTopCmd, v => v.menuMoveTop).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.MoveUpCmd, v => v.menuMoveUp).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.MoveDownCmd, v => v.menuMoveDown).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.MoveBottomCmd, v => v.menuMoveBottom).DisposeWith(disposables);

            //tests
            this.BindCommand(ViewModel, vm => vm.FastRealPingCmd, v => v.btnFastRealPing).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.MixedTestServerCmd, v => v.menuMixedTestServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.TcpingServerCmd, v => v.menuTcpingServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RealPingServerCmd, v => v.menuRealPingServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.UdpTestServerCmd, v => v.menuUdpTestServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SpeedServerCmd, v => v.menuSpeedServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SortServerResultCmd, v => v.menuSortServerResult).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RemoveInvalidServerResultCmd, v => v.menuRemoveInvalidServerResult).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.TcpingServerCmd, v => v.menuTcpingServer2).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RealPingServerCmd, v => v.menuRealPingServer2).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.UdpTestServerCmd, v => v.menuUdpTestServer2).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SpeedServerCmd, v => v.menuSpeedServer2).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SortServerResultCmd, v => v.menuSortServerResult2).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RemoveInvalidServerResultCmd, v => v.menuRemoveInvalidServerResult2).DisposeWith(disposables);

            //export
            this.BindCommand(ViewModel, vm => vm.Export2ClientConfigCmd, v => v.menuExport2ClientConfig).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.Export2ClientConfigClipboardCmd, v => v.menuExport2ClientConfigClipboard).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.Export2ShareUrlCmd, v => v.menuExport2ShareUrl).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.Export2ShareUrlBase64Cmd, v => v.menuExport2ShareUrlBase64).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.Export2InnerUriCmd, v => v.menuExport2InnerUri).DisposeWith(disposables);

            //subscriptions
            this.BindCommand(ViewModel, vm => vm.AddSubCmd, v => v.menuAddSubscription).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddSubCmd, v => v.btnEmptyAddSub).DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.SelectedSub, v => v.ViewModel.FavoritesOnly, v => v.ViewModel.FavoritesCount)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => UpdateHeader())
                .DisposeWith(disposables);

            ViewModel.ProfileItems.CollectionChanged += (_, _) => UpdateHeader();

            ViewModel.ShowYesNoInteraction.RegisterHandler(async interaction =>
            {
                var result = await UI.ShowYesNo(interaction.Input);
                interaction.SetOutput(result == ButtonResult.Yes);
            }).DisposeWith(disposables);

            ViewModel.SaveFileDialogInteraction.RegisterHandler(async interaction =>
            {
                var viewModel = ViewModel;
                if (viewModel is null)
                {
                    interaction.SetOutput(false);
                    return;
                }
                var fileName = await UI.SaveFileDialog("");
                if (fileName.IsNullOrEmpty())
                {
                    interaction.SetOutput(false);
                    return;
                }
                await viewModel.Export2ClientConfigResult(fileName, interaction.Input);
                interaction.SetOutput(true);
            }).DisposeWith(disposables);

            ViewModel.SetClipboardDataInteraction.RegisterHandler(async interaction =>
            {
                await AvaUtils.SetClipboardData(this, interaction.Input);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.ProfilesFocusInteraction.RegisterHandler(interaction =>
            {
                lstProfiles.Focus();
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.ShareServerInteraction.RegisterHandler(async interaction =>
            {
                if (interaction.Input.IsNotEmpty())
                {
                    await DialogHost.Show(new QrcodeView(interaction.Input));
                }
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.DispatcherRefreshServersBizInteraction.RegisterHandler(interaction =>
            {
                Dispatcher.UIThread.Post(RefreshServersBiz, DispatcherPriority.Default);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.AdjustMainLvColWidthInteraction.RegisterHandler(interaction =>
            {
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);
        });
    }

    /// <summary>
    /// The window's view model; it owns the add-server and subscription-update commands shown in this page's header.
    /// </summary>
    public MainWindowViewModel? MainViewModel
    {
        get => _mainViewModel;
        set
        {
            _mainViewModel = value;
            if (value == null)
            {
                return;
            }

            btnUpdateSub.Command = value.SubGroupUpdateCmd;
            menuUpdateSubViaProxy.Command = value.SubGroupUpdateViaProxyCmd;
            menuAddServerViaScan.Command = value.AddServerViaScanCmd;
            menuAddServerViaImage.Command = value.AddServerViaImageCmd;
            menuAddVmessServer.Command = value.AddVmessServerCmd;
            menuAddVlessServer.Command = value.AddVlessServerCmd;
            menuAddShadowsocksServer.Command = value.AddShadowsocksServerCmd;
            menuAddTrojanServer.Command = value.AddTrojanServerCmd;
            menuAddHysteria2Server.Command = value.AddHysteria2ServerCmd;
            menuAddTuicServer.Command = value.AddTuicServerCmd;
            menuAddWireguardServer.Command = value.AddWireguardServerCmd;
            menuAddAnytlsServer.Command = value.AddAnytlsServerCmd;
            menuAddNaiveServer.Command = value.AddNaiveServerCmd;
            menuAddMasqueServer.Command = value.AddMasqueServerCmd;
            menuAddSocksServer.Command = value.AddSocksServerCmd;
            menuAddHttpServer.Command = value.AddHttpServerCmd;
            menuAddCustomServer.Command = value.AddCustomServerCmd;
            menuAddPolicyGroupServer.Command = value.AddPolicyGroupServerCmd;
            menuAddProxyChainServer.Command = value.AddProxyChainServerCmd;
            menuAddCustomOutboundServer.Command = value.AddCustomOutboundServerCmd;
        }
    }

    #region Header

    private void UpdateHeader()
    {
        var vm = ViewModel;
        if (vm == null)
        {
            return;
        }

        var sub = vm.SelectedSub;
        var isSub = !vm.FavoritesOnly && sub?.Id.IsNotEmpty() == true;
        var count = vm.ProfileItems.Count;

        runTitle.Text = vm.FavoritesOnly ? L.Favorites : isSub ? sub!.Remarks : L.AllServers;
        runTitleCount.Text = $" {count}";

        if (vm.FavoritesOnly)
        {
            txtSubtitle.Text = L.FavoritesHint;
        }
        else if (isSub)
        {
            var parts = new List<string>();
            var host = GetHost(sub!.Url);
            if (host.IsNotEmpty())
            {
                parts.Add(host);
            }
            parts.Add(Fmt.SubUpdated(sub));
            if (sub.AutoUpdateInterval > 0)
            {
                parts.Add(string.Format(L.AutoUpdateEvery, sub.AutoUpdateInterval));
            }
            txtSubtitle.Text = string.Join("  ·  ", parts);
        }
        else
        {
            txtSubtitle.Text = L.LocalServers;
        }

        btnUpdateSub.IsVisible = !vm.FavoritesOnly;
        panelFavorites.IsVisible = !vm.FavoritesOnly && vm.FavoritesCount > 0;

        var empty = count == 0;
        panelEmpty.IsVisible = empty;
        if (empty)
        {
            var noFilter = vm.ServerFilter.IsNullOrEmpty();
            txtEmptyTitle.Text = vm.FavoritesOnly ? L.EmptyFavoritesTitle : L.EmptyTitle;
            txtEmptyHint.Text = vm.FavoritesOnly ? L.FavoritesHint : L.EmptyHint;
            panelEmptyActions.IsVisible = !vm.FavoritesOnly && noFilter;
        }
    }

    private static string GetHost(string? url)
    {
        if (url.IsNullOrEmpty())
        {
            return string.Empty;
        }
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
    }


    #endregion Header

    #region Event

    private async Task PasteFromClipboardAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window window)
        {
            return;
        }
        var data = await AvaUtils.GetClipboardData(window);
        if (data.IsNotEmpty() && _mainViewModel != null)
        {
            await _mainViewModel.AddServerViaClipboardAsync(data);
        }
    }

    public void RefreshServersBiz()
    {
        if (lstProfiles.SelectedItem != null)
        {
            lstProfiles.ScrollIntoView(lstProfiles.SelectedItem);
        }
        UpdateHeader();
    }

    private void LstProfiles_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.SelectedProfiles = lstProfiles.SelectedItems?.Cast<ProfileItemModel>().ToList() ?? [];
        }
    }

    private void LstProfiles_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control c && c.FindLogicalAncestorOfType<Button>(true) != null)
        {
            return;
        }

        if (_config.UiItem.DoubleClick2Activate)
        {
            ViewModel?.SetDefaultServer();
        }
        else
        {
            ViewModel?.EditServerAsync();
        }
    }

    private void LstProfiles_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        // Right-click on an unselected row acts on that row alone.
        if (e.Source is StyledElement element
            && (element.FindLogicalAncestorOfType<ListBoxItem>(true)?.DataContext ?? element.DataContext) is ProfileItemModel item
            && !lstProfiles.SelectedItems!.Contains(item))
        {
            lstProfiles.SelectedItem = item;
        }
    }

    private void RowMore_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ProfileItemModel item } button)
        {
            return;
        }
        if (!lstProfiles.SelectedItems!.Contains(item))
        {
            lstProfiles.SelectedItem = item;
        }
        ctxProfiles.Open(button);
    }

    private void MenuMoveToGroup_Click(object? sender, RoutedEventArgs e)
    {
        if (e.Source is MenuItem { DataContext: SubItem sub } && ViewModel != null && sub.Id.IsNotEmpty())
        {
            ViewModel.MoveToGroupCmd.Execute(sub).Subscribe();
        }
    }

    private async void SortHeader_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string col } && ViewModel != null)
        {
            await ViewModel.SortServer(col);
        }
    }

    private void LstProfiles_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta)
        {
            switch (e.Key)
            {
                case Key.A:
                    lstProfiles.SelectAll();
                    break;

                case Key.C:
                    ViewModel?.Export2ShareUrlAsync(false);
                    break;

                case Key.D:
                    ViewModel?.EditServerAsync();
                    break;

                case Key.F:
                    ViewModel?.ShareServerAsync();
                    break;

                case Key.O:
                    ViewModel?.ServerSpeedtest(ESpeedActionType.Tcping);
                    break;

                case Key.R:
                    ViewModel?.ServerSpeedtest(ESpeedActionType.Realping);
                    break;

                case Key.T:
                    ViewModel?.ServerSpeedtest(ESpeedActionType.Speedtest);
                    break;

                case Key.E:
                    ViewModel?.ServerSpeedtest(ESpeedActionType.Mixedtest);
                    break;
            }
        }
        else
        {
            switch (e.Key)
            {
                case Key.Enter:
                    ViewModel?.SetDefaultServer();
                    break;

                case Key.Delete:
                case Key.Back:
                    ViewModel?.RemoveServerAsync();
                    break;

                case Key.T:
                    ViewModel?.MoveServer(EMove.Top);
                    break;

                case Key.U:
                    ViewModel?.MoveServer(EMove.Up);
                    break;

                case Key.D:
                    ViewModel?.MoveServer(EMove.Down);
                    break;

                case Key.B:
                    ViewModel?.MoveServer(EMove.Bottom);
                    break;

                case Key.Escape:
                    ViewModel?.ServerSpeedtestStop();
                    break;
            }
        }
    }

    private void TxtServerFilter_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            ViewModel?.RefreshServers();
        }
    }

    #endregion Event
}
