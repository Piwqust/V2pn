using v2rayN.Desktop.Base;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class RoutingRuleDetailsWindow : WindowBase<RoutingRuleDetailsViewModel>
{
    // The plain "what to do" choice writes one of these into the outbound tag; any other tag
    // (a server name, a custom outbound) leaves it unselected and lives in the advanced row.
    private static readonly string[] ActionTags = [Global.ProxyTag, Global.DirectTag, Global.BlockTag];

    private bool _syncingAction;

    public RoutingRuleDetailsWindow()
    {
        InitializeComponent();

        Loaded += Window_Loaded;
        btnCancel.Click += (s, e) => Close();
        clbProtocol.SelectionChanged += ClbProtocol_SelectionChanged;
        clbInboundTag.SelectionChanged += ClbInboundTag_SelectionChanged;
        lstAction.SelectionChanged += LstAction_SelectionChanged;
        cmbOutboundTag.GetObservable(ComboBox.TextProperty).Subscribe(SyncAction);
        SettingsKit.InstallMoreFields(this, btnMoreFields, HasExtras);

        cmbOutboundTag.ItemsSource = Global.OutboundTags;
        clbProtocol.ItemsSource = Global.RuleProtocols;
        clbInboundTag.ItemsSource = Global.InboundTags;
        cmbNetwork.ItemsSource = Global.RuleNetworks;
        cmbRuleType.ItemsSource = Utils.GetEnumNames<ERuleType>().AppendEmpty();

        this.WhenActivated(disposables =>
        {
            this.WhenAnyValue(v => v.ViewModel.SelectedSource)
                .KeepNotNull()
                .Subscribe(InitializeData)
                .DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.OutboundTag, v => v.cmbOutboundTag.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Remarks, v => v.txtRemarks.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.OutboundTag, v => v.cmbOutboundTag.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Port, v => v.txtPort.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Network, v => v.cmbNetwork.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Enabled, v => v.togEnabled.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Domain, v => v.txtDomain.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.IP, v => v.txtIP.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Process, v => v.txtProcess.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AutoSort, v => v.chkAutoSort.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.RuleType, v => v.cmbRuleType.SelectedValue).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.SelectProfileCmd, v => v.btnSelectProfile).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SaveCmd, v => v.btnSave).DisposeWith(disposables);
        });
    }

    private void InitializeData(RulesItem rulesItem)
    {
        if (rulesItem.Id.IsNullOrEmpty())
        {
            return;
        }
        rulesItem.Protocol?.ForEach(it =>
        {
            clbProtocol?.SelectedItems?.Add(it);
        });
        rulesItem.InboundTag?.ForEach(it =>
        {
            clbInboundTag?.SelectedItems?.Add(it);
        });
    }

    private void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        // The name sits behind "Show more fields"; with it hidden, start in the sites list.
        if (!txtRemarks.IsEffectivelyVisible || !txtRemarks.Focus())
        {
            txtDomain.Focus();
        }
    }

    private static bool IsPlainAction(string? tag) => ActionTags.Contains(tag ?? string.Empty, StringComparer.Ordinal);

    private void SyncAction(string? tag)
    {
        _syncingAction = true;
        lstAction.SelectedIndex = Array.IndexOf(ActionTags, tag ?? string.Empty);
        _syncingAction = false;
    }

    private void LstAction_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingAction || lstAction.SelectedIndex < 0)
        {
            return;
        }
        cmbOutboundTag.Text = ActionTags[lstAction.SelectedIndex];
    }

    private bool HasExtras()
    {
        var customOutbound = cmbOutboundTag.Text.IsNotEmpty() && !IsPlainAction(cmbOutboundTag.Text);
        return SettingsKit.AnySet(customOutbound, txtPort.Text, cmbNetwork.SelectedValue as string, cmbRuleType.SelectedValue as string,
            clbProtocol.SelectedItems?.Count ?? 0, clbInboundTag.SelectedItems?.Count ?? 0);
    }

    private void ClbProtocol_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.ProtocolItems = clbProtocol.SelectedItems.Cast<string>().ToList();
        }
    }

    private void ClbInboundTag_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.InboundTagItems = clbInboundTag.SelectedItems.Cast<string>().ToList();
        }
    }

    private void linkRuleobjectDoc_Click(object? sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart("https://xtls.github.io/config/routing.html#ruleobject");
    }
}
