using v2rayN.Desktop.Base;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class SubEditWindow : WindowBase<SubEditViewModel>
{
    public SubEditWindow()
    {
        InitializeComponent();

        Loaded += Window_Loaded;
        btnCancel.Click += (s, e) => Close();
        btnMoreFields.Click += (_, _) => ShowMore(!_more);

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

    private bool _more;

    private void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        // Most subscriptions only need a name and a link; the rest opens on demand, or right away
        // when this subscription already uses one of those fields.
        ShowMore(HasExtras());
        // The view-model bindings may land after Loaded; check once more when they have.
        Dispatcher.UIThread.Post(() =>
        {
            if (!_more && HasExtras())
            {
                ShowMore(true);
            }
        }, DispatcherPriority.Background);
        txtRemarks.Focus();
    }

    private bool HasExtras()
    {
        string?[] extras = [txtMoreUrl.Text, txtUserAgent.Text, txtRequestHeaders.Text, txtFilter.Text, cmbConvertTarget.SelectedValue as string,
            cmbPreResolve.Text, txtPrevProfile.Text, txtNextProfile.Text, txtPreSocksPort.Text, txtMemo.Text, cmbCustomCoreType.SelectedValue as string];
        return extras.Any(t => t.IsNotEmpty() && t != "0");
    }

    private void ShowMore(bool show)
    {
        _more = show;
        SettingsKit.ApplyAdvanced(this, show);
        txtMoreFields.Text = show ? L.FewerFields : L.MoreFields;
        iconMoreFields.RenderTransform = show ? new RotateTransform(180) : null;
    }
}
