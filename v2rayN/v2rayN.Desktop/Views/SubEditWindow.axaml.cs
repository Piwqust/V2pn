using v2rayN.Desktop.Base;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class SubEditWindow : WindowBase<SubEditViewModel>
{
    /// <summary>A new subscription with a link was saved; the main window updates it right away.</summary>
    public static event Action<string>? SubscriptionAdded;

    private bool _isNew;

    public SubEditWindow()
    {
        InitializeComponent();

        Loaded += Window_Loaded;
        btnCancel.Click += (s, e) => Close();
        // Click runs before the bound save command, so an empty name is filled in before v2rayN checks it.
        btnSave.Click += (s, e) =>
        {
            // A pasted link often carries a trailing space or line break.
            txtUrl.Text = txtUrl.Text?.Trim();
            FillEmptyName();
        };
        btnSupport.Click += (s, e) => ProcUtils.ProcessStart(btnSupport.Tag as string);
        btnWebPage.Click += (s, e) => ProcUtils.ProcessStart(btnWebPage.Tag as string);
        Closed += (s, e) => AnnounceIfAdded();
        SettingsKit.InstallMoreFields(this, btnMoreFields, HasExtras);

        cmbConvertTarget.ItemsSource = Global.SubConvertTargets;
        cmbPreResolve.ItemsSource = Global.DomainDNS4PreResolveAddress;
        cmbCustomCoreType.ItemsSource = Utils.GetEnumNames<ECoreType>().Where(t => t != nameof(ECoreType.v2rayN)).ToList().AppendEmpty();

        this.WhenActivated(disposables =>
        {
            this.Bind(ViewModel, vm => vm.SelectedSource.Remarks, v => v.txtRemarks.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSource.Url, v => v.txtUrl.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSource.MoreUrl, v => v.txtMoreUrl.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSource.Enabled, v => v.togEnable.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSource.AutoUpdateInterval, v => v.txtAutoUpdateInterval.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSource.UserAgent, v => v.txtUserAgent.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSource.RequestHeaders, v => v.txtRequestHeaders.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSource.Sort, v => v.txtSort.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSource.Filter, v => v.txtFilter.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSource.ConvertTarget, v => v.cmbConvertTarget.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.PrevProfile, v => v.txtPrevProfile.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.NextProfile, v => v.txtNextProfile.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSource.PreSocksPort, v => v.txtPreSocksPort.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSource.Memo, v => v.txtMemo.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CustomCoreType, v => v.cmbCustomCoreType.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSource.PreResolver, v => v.cmbPreResolve.Text).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.SelectPrevProfileCmd, v => v.btnSelectPrevProfile).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SelectNextProfileCmd, v => v.btnSelectNextProfile).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SaveCmd, v => v.btnSave).DisposeWith(disposables);
        });
    }

    private void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        _isNew = ViewModel?.SelectedSource.Id.IsNullOrEmpty() ?? false;
        ShowProviderInfo();
        // The name is optional now; a new subscription starts at the link.
        if (_isNew)
        {
            Title = txtPageTitle.Text = L.NewSubscription;
            btnSave.Content = L.AddServer;
            // V2pn: subscriptions keep themselves up to date unless the person turns it off; v2rayN's 0 never updates.
            // Posted: the bindings fill the fields after Loaded.
            Dispatcher.UIThread.Post(() =>
            {
                if (ViewModel?.SelectedSource is { AutoUpdateInterval: 0 } && txtAutoUpdateInterval.Text is null or "" or "0")
                {
                    txtAutoUpdateInterval.Text = V2pnSubs.NewSubUpdateMinutes.ToString(CultureInfo.InvariantCulture);
                }
            }, DispatcherPriority.Background);
            txtUrl.Focus();
        }
        else
        {
            txtRemarks.Focus();
        }
    }

    private bool HasExtras()
    {
        string?[] extras = [txtMoreUrl.Text, txtUserAgent.Text, txtRequestHeaders.Text, txtFilter.Text, cmbConvertTarget.SelectedValue as string,
            cmbPreResolve.Text, txtPrevProfile.Text, txtNextProfile.Text, txtPreSocksPort.Text, txtMemo.Text, cmbCustomCoreType.SelectedValue as string];
        return SettingsKit.AnySet(extras);
    }
}
