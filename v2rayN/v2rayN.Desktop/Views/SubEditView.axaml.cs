using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>
/// One subscription's form. The window around it (the subscriptions window, or SubEditWindow when the server
/// list edits a subscription) supplies Save: it calls <see cref="PrepareSave"/> and then the model's SaveCmd.
/// </summary>
public partial class SubEditView : ReactiveUserControl<SubEditViewModel>
{
    /// <summary>A new subscription with a link was saved; the main window updates it right away.</summary>
    public static event Action<string>? SubscriptionAdded;

    private bool _isNew;

    public SubEditView()
    {
        InitializeComponent();

        Loaded += View_Loaded;
        txtUrl.TextChanged += (_, _) => txtUrlError.IsVisible = false;
        btnSupport.Click += (s, e) => ProcUtils.ProcessStart(btnSupport.Tag as string);
        btnWebPage.Click += (s, e) => ProcUtils.ProcessStart(btnWebPage.Tag as string);
        btnAnnounceMore.Click += (s, e) =>
        {
            if (btnAnnounceMore.Tag is string url && SubPanelHeaders.ParseSecureLink(url) != null)
            {
                ProcUtils.ProcessStart(url);
            }
        };
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
        });
    }

    /// <summary>True for a subscription that is not saved yet.</summary>
    public bool IsNew => ViewModel?.SelectedSource.Id.IsNullOrEmpty() ?? false;

    /// <summary>Whether the page shows its own title and hint (a window does; the list beside it names the subscription).</summary>
    public bool ShowsTitle
    {
        get => txtPageTitle.IsVisible;
        set
        {
            txtPageTitle.IsVisible = value;
            txtPageHint.IsVisible = value;
            // Under a header of its own the form starts right away.
            pageBody.Margin = value ? new Thickness(28, 20, 28, 28) : new Thickness(28, 0, 28, 28);
        }
    }

    /// <summary>Runs before the model's SaveCmd: cleans the link, flags a broken one, fills an empty name, keeps the provider choices.</summary>
    public void PrepareSave()
    {
        // A pasted link often carries a trailing space or line break.
        txtUrl.Text = txtUrl.Text?.Trim();
        var url = txtUrl.Text;
        txtUrlError.Text = ResUI.InvalidUrlTip;
        txtUrlError.IsVisible = url.IsNotEmpty() && Utils.TryUri(url) == null;
        FillEmptyName();
        SaveProviderChoices();
    }

    public void FocusFirstField()
    {
        if (_isNew)
        {
            txtUrl.Focus();
        }
        else
        {
            txtRemarks.Focus();
        }
    }

    private void View_Loaded(object? sender, RoutedEventArgs e)
    {
        _isNew = IsNew;
        ShowProviderInfo();
        if (_isNew)
        {
            txtPageTitle.Text = L.NewSubscription;
            // V2pn: subscriptions keep themselves up to date unless the person turns it off; v2rayN's 0 never updates.
            // Posted: the bindings fill the fields after Loaded.
            Dispatcher.UIThread.Post(() =>
            {
                if (ViewModel?.SelectedSource is { AutoUpdateInterval: 0 } && txtAutoUpdateInterval.Text is null or "" or "0")
                {
                    txtAutoUpdateInterval.Text = V2pnSubs.NewSubUpdateMinutes.ToString(CultureInfo.InvariantCulture);
                }
            }, DispatcherPriority.Background);
        }
        FocusFirstField();
    }

    private bool HasExtras()
    {
        string?[] extras = [txtMoreUrl.Text, txtUserAgent.Text, txtRequestHeaders.Text, txtFilter.Text, cmbConvertTarget.SelectedValue as string,
            cmbPreResolve.Text, txtPrevProfile.Text, txtNextProfile.Text, txtPreSocksPort.Text, txtMemo.Text, cmbCustomCoreType.SelectedValue as string];
        return SettingsKit.AnySet(extras);
    }
}
