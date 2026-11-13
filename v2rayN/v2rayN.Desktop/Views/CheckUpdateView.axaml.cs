using Avalonia.Data.Converters;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class CheckUpdateView : ReactiveUserControl<CheckUpdateViewModel>
{
    /// <summary>What each updatable part is called for a person, instead of its core type name.</summary>
    public static readonly FuncValueConverter<string?, string> PartName = new(name => name switch
    {
        nameof(ECoreType.v2rayN) => L.TlUpdApp,
        nameof(ECoreType.Xray) => L.TlUpdXray,
        nameof(ECoreType.mihomo) => L.TlUpdMihomo,
        nameof(ECoreType.sing_box) => L.TlUpdSingBox,
        "GeoFiles" => L.TlUpdGeo,
        _ => name ?? string.Empty,
    });

    /// <summary>v2rayN fills every row with "Check Update" until there is news; that repeat is noise.</summary>
    public static readonly FuncValueConverter<string?, bool> HasNews = new(remarks =>
        !remarks.IsNullOrEmpty() && remarks != ResUI.menuCheckUpdate);

    /// <summary>v2rayN's messages name the app "v2rayN"; here it is V2pn.</summary>
    public static readonly FuncValueConverter<string?, string> Plain = new(remarks =>
        remarks.IsNullOrEmpty() ? string.Empty : remarks.Replace("v2rayN", L.AppName, StringComparison.Ordinal));

    private readonly Config _config = AppManager.Instance.Config;
    private readonly V2pnAppUpdater _updater = V2pnAppUpdater.Instance;

    public CheckUpdateView()
    {
        InitializeComponent();
        btnCloseDialog.Click += (_, _) => DialogHostAvalonia.DialogHost.Close(null);

        txtAppBase.Text = string.Format(L.UpdBasedOn, V2pnInfo.UpstreamVersion);
        cardApp.IsVisible = _updater.Supported;
        togAutoUpdate.IsChecked = AppUpdates.Auto(_config);
        togAutoUpdate.IsCheckedChanged += async (_, _) =>
        {
            _config.UiItem.V2pnAutoUpdate = togAutoUpdate.IsChecked == true;
            await ConfigHandler.SaveConfig(_config);
        };
        btnAppAction.Click += async (_, _) => await OnAppActionAsync();
        btnWhatsNew.Click += (_, _) => ProcUtils.ProcessStart($"{V2pnInfo.ReleasesUrl}/tag/v{_updater.LatestVersion}");

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.CheckUpdateModels, v => v.lstCheckUpdates.ItemsSource).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.EnableUpdateViaProxy, v => v.togEnableUpdateViaProxy.IsChecked).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.CheckOnlyCmd, v => v.btnCheckOnly).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.CheckUpdateCmd, v => v.btnCheckUpdate).DisposeWith(disposables);

            _updater.WhenAnyValue(u => u.State, u => u.LatestVersion, u => u.Progress)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => RefreshApp())
                .DisposeWith(disposables);

            // Opening the window is a good moment to look, unless a look is already under way or done.
            if (_updater.State is EV2pnUpdateState.Idle or EV2pnUpdateState.Failed)
            {
                _ = AppUpdates.CheckAsync(_config);
            }
        });
    }

    private void RefreshApp()
    {
        var state = _updater.State;
        var latest = _updater.LatestVersion;
        txtAppVersion.Text = string.Format(L.UpdAppTitle, V2pnInfo.DisplayVersion);
        spinApp.IsVisible = state is EV2pnUpdateState.Checking or EV2pnUpdateState.Downloading;
        txtAppState.Text = state switch
        {
            EV2pnUpdateState.Checking => L.UpdChecking,
            EV2pnUpdateState.UpToDate => L.UpdUpToDate,
            EV2pnUpdateState.Available => string.Format(L.UpdAvailable, latest),
            EV2pnUpdateState.Downloading => string.Format(L.UpdDownloading, latest)
                + (_updater.Progress.IsNullOrEmpty() ? string.Empty : $"  {_updater.Progress}"),
            EV2pnUpdateState.Ready => string.Format(L.UpdReady, latest),
            EV2pnUpdateState.Failed => _updater.InstallFailed ? string.Format(L.UpdInstallFailed, latest)
                : _updater.DownloadFailed ? string.Format(L.UpdDownloadFailed, latest) : L.UpdFailed,
            _ => L.UpdIdle,
        };
        ToolTip.SetTip(txtAppState, state == EV2pnUpdateState.Failed ? _updater.Error : null);
        txtAppState.Classes.Set("warn", state == EV2pnUpdateState.Failed);

        btnAppAction.Content = state switch
        {
            EV2pnUpdateState.Available or EV2pnUpdateState.Downloading => L.UpdInstall,
            EV2pnUpdateState.Ready => L.UpdRestart,
            EV2pnUpdateState.Failed => L.UpdRetry,
            EV2pnUpdateState.UpToDate => L.UpdCheckAgain,
            _ => L.UpdCheckNow,
        };
        btnAppAction.IsEnabled = state is not (EV2pnUpdateState.Checking or EV2pnUpdateState.Downloading);
        // The solid button is for news; checking again stays quiet.
        btnAppAction.Classes.Set("primary", state is EV2pnUpdateState.Available or EV2pnUpdateState.Ready);
        btnWhatsNew.IsVisible = state is EV2pnUpdateState.Available or EV2pnUpdateState.Downloading or EV2pnUpdateState.Ready;
    }

    private async Task OnAppActionAsync()
    {
        switch (_updater.State)
        {
            case EV2pnUpdateState.Ready:
                await _updater.InstallAsync();
                break;

            case EV2pnUpdateState.Available:
                // The person asked for it, so it installs as soon as it is downloaded and checked.
                await _updater.CheckAsync(download: true);
                if (_updater.State == EV2pnUpdateState.Ready)
                {
                    await _updater.InstallAsync();
                }
                break;

            default:
                // Asking again is also how a person retries a version whose install was rolled back.
                await _updater.CheckAsync(download: AppUpdates.Auto(_config), retryFailed: true);
                break;
        }
    }
}
