using System.Collections.Specialized;
using Avalonia.Data.Converters;

namespace v2rayN.Desktop.Views;

public partial class ClashProxiesView : ReactiveUserControl<ClashProxiesViewModel>
{
    // The view model writes the delay as "" (not tested), "-1" (no answer) or "123ms".
    private static int? Delay(string? name) =>
        string.IsNullOrEmpty(name) ? null
        : name == "-1" ? -1
        : int.TryParse(name.Replace("ms", string.Empty).Trim(), out var ms) ? ms : null;

    public static readonly IValueConverter PingText = new FuncValueConverter<string?, string>(s => Delay(s) switch
    {
        null => "—",
        < 0 => L.Timeout,
        var ms => L.Ms(ms.Value),
    });

    public static readonly IValueConverter IsGood = new FuncValueConverter<string?, bool>(s => Delay(s) is > 0 and <= 300);
    public static readonly IValueConverter IsMid = new FuncValueConverter<string?, bool>(s => Delay(s) is > 300 and <= 800);
    public static readonly IValueConverter IsBad = new FuncValueConverter<string?, bool>(s => Delay(s) is < 0 or > 800);

    public static readonly IValueConverter ModeName = new FuncValueConverter<string?, string>(m => m?.ToLowerInvariant() switch
    {
        "rule" => L.PxModeRule,
        "global" => L.PxModeGlobal,
        "direct" => L.PxModeDirect,
        _ => m ?? string.Empty,
    });

    public static readonly IValueConverter GroupType = new FuncValueConverter<string?, string>(t => t?.ToLowerInvariant() switch
    {
        "selector" => L.PxTypeSelector,
        "urltest" => L.PxTypeUrlTest,
        "fallback" => L.PxTypeFallback,
        "loadbalance" => L.PxTypeLoadBalance,
        _ => t ?? string.Empty,
    });

    // The core's catch-all group for the "all via proxy" mode.
    public static readonly IValueConverter GroupName = new FuncValueConverter<string?, string>(n => n == "GLOBAL" ? L.PxTypeGlobal : n ?? string.Empty);

    public ClashProxiesView()
    {
        InitializeComponent();
        lstProxyDetails.DoubleTapped += LstProxyDetails_DoubleTapped;
        KeyDown += ClashProxiesView_KeyDown;
        AttachedToVisualTree += (_, _) => UpdateState();
        panelBody.SizeChanged += PanelBody_SizeChanged;

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.ProxyGroups, v => v.lstProxyGroups.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedGroup, v => v.lstProxyGroups.SelectedItem).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.ProxyDetails, v => v.lstProxyDetails.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedDetail, v => v.lstProxyDetails.SelectedItem).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.ProxiesReloadCmd, v => v.menuProxiesReload).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ProxiesReloadCmd, v => v.btnEmptyReload).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.GroupProxiesDelayTestCmd, v => v.menuGroupProxiesDelaytest).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.ProxyDelayTestCmd, v => v.menuProxyDelaytest).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ProxiesSelectActivityCmd, v => v.menuProxiesSelectActivity).DisposeWith(disposables);

            // The segmented control's items show their content as is, so they carry the label and keep the core's name in Tag.
            this.WhenAnyValue(v => v.ViewModel!.RuleModeSelected)
                .Subscribe(_ => SyncRuleMode())
                .DisposeWith(disposables);
            lstRuleMode.SelectionChanged += LstRuleMode_SelectionChanged;
            new ActionDisposable(() => lstRuleMode.SelectionChanged -= LstRuleMode_SelectionChanged).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SortingSelected, v => v.lstSorting.SelectedIndex).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AutoRefresh, v => v.togAutoRefresh.IsChecked).DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel!.SelectedGroup)
                .Subscribe(_ => UpdateState())
                .DisposeWith(disposables);

            if (ViewModel is { } vm)
            {
                vm.ProxyGroups.CollectionChanged += Items_CollectionChanged;
                vm.ProxyDetails.CollectionChanged += Items_CollectionChanged;
                vm.ClashModes.CollectionChanged += Modes_CollectionChanged;
                new ActionDisposable(() =>
                {
                    vm.ProxyGroups.CollectionChanged -= Items_CollectionChanged;
                    vm.ProxyDetails.CollectionChanged -= Items_CollectionChanged;
                    vm.ClashModes.CollectionChanged -= Modes_CollectionChanged;
                }).DisposeWith(disposables);
            }
            BuildRuleModes();
            UpdateState();
        });
    }

    private bool _syncingMode;
    private bool? _stacked;

    // In a narrow window the groups go above the chosen group: side by side the proxies got squeezed.
    private void PanelBody_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 700;
        if (_stacked == stacked)
        {
            return;
        }
        _stacked = stacked;
        gridBody.ColumnDefinitions = stacked ? new ColumnDefinitions("*") : new ColumnDefinitions("260,16,*");
        gridBody.RowDefinitions = stacked ? new RowDefinitions("Auto,16,*") : new RowDefinitions("*");
        Grid.SetColumn(cardDetails, stacked ? 0 : 2);
        Grid.SetRow(cardDetails, stacked ? 2 : 0);
        cardGroups.MaxHeight = stacked ? 260 : double.PositiveInfinity;
    }

    private void Modes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => BuildRuleModes();

    private void BuildRuleModes()
    {
        if (ViewModel == null)
        {
            return;
        }
        _syncingMode = true;
        try
        {
            lstRuleMode.ItemsSource = ViewModel.ClashModes
                .Select(m => new ListBoxItem
                {
                    Content = ModeName.Convert(m, typeof(string), null, CultureInfo.CurrentCulture),
                    Tag = m,
                })
                .ToList();
        }
        finally
        {
            _syncingMode = false;
        }
        SyncRuleMode();
    }

    private void SyncRuleMode()
    {
        if (lstRuleMode.ItemsSource is not IEnumerable<ListBoxItem> items)
        {
            return;
        }
        _syncingMode = true;
        try
        {
            lstRuleMode.SelectedItem = items.FirstOrDefault(i =>
                string.Equals(i.Tag as string, ViewModel?.RuleModeSelected, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _syncingMode = false;
        }
    }

    private void LstRuleMode_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_syncingMode && ViewModel != null && lstRuleMode.SelectedItem is ListBoxItem { Tag: string mode })
        {
            ViewModel.RuleModeSelected = mode;
        }
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateState();

    private void UpdateState()
    {
        if (ViewModel == null)
        {
            return;
        }
        var hasGroups = ViewModel.ProxyGroups.Count > 0;
        gridBody.IsVisible = hasGroups;
        panelEmpty.IsVisible = !hasGroups;
        txtEmptyHint.Text = AppManager.Instance.IsRunningCore(ECoreType.sing_box) ? L.PxEmptyHint : L.LiveNeedsSingBox;

        var group = ViewModel.SelectedGroup;
        var manual = string.Equals(group?.Type, "Selector", StringComparison.OrdinalIgnoreCase);
        txtGroupName.Text = GroupName.Convert(group?.Name, typeof(string), null, CultureInfo.CurrentCulture) as string;
        txtGroupHint.Text = group == null ? string.Empty : manual ? L.PxManual : L.PxAuto;
        lstProxyDetails.Classes.Set("auto", !manual);
        txtGroupEmpty.IsVisible = group != null && ViewModel.ProxyDetails.Count == 0;
    }

    private void ClashProxiesView_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F5:
                _ = ViewModel?.ProxiesReload();
                break;

            case Key.Enter:
                _ = UseSelected();
                break;
        }
    }

    private void LstProxyDetails_DoubleTapped(object? sender, TappedEventArgs e)
    {
        _ = UseSelected();
    }

    private async Task UseSelected()
    {
        if (ViewModel is not { SelectedGroup: not null, SelectedDetail: not null } vm)
        {
            return;
        }
        try
        {
            await vm.SetActiveProxy();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ClashProxiesView", ex);
        }
    }

    private async void RowUse_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ClashProxyModel item } && ViewModel != null)
        {
            ViewModel.SelectedDetail = item;
            await UseSelected();
        }
    }

    private void RowPing_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ClashProxyModel item } && ViewModel != null)
        {
            ViewModel.SelectedDetail = item;
            ViewModel.ProxyDelayTestCmd.Execute().Subscribe();
        }
    }
}
