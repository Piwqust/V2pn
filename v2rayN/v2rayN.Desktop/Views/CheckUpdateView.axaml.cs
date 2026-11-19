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

    private readonly Config _config = ConfigTextHandler.Config;
    private readonly V2pnAppUpdater _updater = V2pnAppUpdater.Instance;
    private readonly V2pnDevBuild _dev = V2pnDevBuild.Instance;
    private string? _backError;

    public CheckUpdateView()
    {
        InitializeComponent();
        btnCloseDialog.Click += (_, _) => DialogHostAvalonia.DialogHost.Close(null);

        txtAppBase.Text = string.Format(L.UpdBasedOn, V2pnInfo.UpstreamVersion);
        cardApp.IsVisible = _updater.Supported;
        togAutoUpdate.IsChecked = AppUpdates.Auto(_config);
        // winget replaces a copy it installed; there is nothing to download here.
        rowAutoUpdate.IsVisible = !_updater.ByWinGet;
        togAutoUpdate.IsCheckedChanged += async (_, _) =>
        {
            _config.UiItem.V2pnAutoUpdate = togAutoUpdate.IsChecked == true;
            await ConfigTextHandler.SaveConfig();
        };
        btnAppAction.Click += async (_, _) => await OnAppActionAsync();
        btnWhatsNew.Click += (_, _) => ProcUtils.ProcessStart($"{V2pnInfo.ReleasesUrl}/tag/v{_updater.LatestVersion}");

        panelDev.IsVisible = V2pnDevBuild.Supported;
        togDevMode.IsChecked = _config.UiItem.V2pnDevMode;
        togDevMode.IsCheckedChanged += async (_, _) =>
        {
            _config.UiItem.V2pnDevMode = togDevMode.IsChecked == true;
            await ConfigTextHandler.SaveConfig();
            RefreshDev();
        };
        btnDevSource.Click += async (_, _) => await ChooseSourceAsync();
        btnDevBuild.Click += async (_, _) => await OnDevBuildAsync();
        btnDevLog.Click += (_, _) => OpenDevLog();
        btnDevRelease.Click += async (_, _) => await OnBackToReleaseAsync();

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.CheckUpdateModels, v => v.lstCheckUpdates.ItemsSource).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.EnableUpdateViaProxy, v => v.togEnableUpdateViaProxy.IsChecked).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.CheckOnlyCmd, v => v.btnCheckOnly).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.CheckUpdateCmd, v => v.btnCheckUpdate).DisposeWith(disposables);

            _updater.WhenAnyValue(u => u.State, u => u.LatestVersion, u => u.Progress)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    RefreshApp();
                    RefreshDev();
                })
                .DisposeWith(disposables);

            _dev.WhenAnyValue(d => d.State, d => d.Step, d => d.Error)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => RefreshDev())
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
            EV2pnUpdateState.UpToDate => V2pnInfo.IsLocalVersion(V2pnInfo.Version) ? L.UpdLocalUpToDate : L.UpdUpToDate,
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
            EV2pnUpdateState.Available when _updater.ByWinGet => L.UpdThroughWinGet,
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

            case EV2pnUpdateState.Available when _updater.ByWinGet:
                if (V2pnWinGet.StartUpgrade())
                {
                    await AppManager.Instance.AppExitAsync(true);
                }
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

    /// <summary>"commit abc1234", or with uncommitted changes when the build had them.</summary>
    private static string? BuiltFrom() => V2pnInfo.Commit is not { Length: > 0 } commit ? null
        : commit.EndsWith('+') ? string.Format(L.UpdBuiltFromChanged, commit.TrimEnd('+')) : string.Format(L.UpdBuiltFrom, commit);

    private void RefreshDev()
    {
        var on = togDevMode.IsChecked == true;
        panelDevBody.IsVisible = on;
        if (!on)
        {
            return;
        }

        // A build from the developer mode knows where it came from; that folder is the natural default.
        if (_config.UiItem.V2pnDevSource.IsNullOrEmpty() && V2pnDevBuild.IsSourceFolder(V2pnInfo.SourceDir))
        {
            _config.UiItem.V2pnDevSource = V2pnInfo.SourceDir;
            _ = ConfigTextHandler.SaveConfig();
        }
        var source = _config.UiItem.V2pnDevSource;
        var sourceOk = V2pnDevBuild.IsSourceFolder(source);
        txtDevSource.Text = source.IsNullOrEmpty() ? L.DevSourceNone : sourceOk ? source : $"{source}: {L.DevSourceBad}";
        txtDevSource.Classes.Set("warn", !source.IsNullOrEmpty() && !sourceOk);

        var state = _dev.State;
        var busy = state is EV2pnDevBuildState.Building or EV2pnDevBuildState.Installing;
        // A build that was installed and rolled back leaves its version behind until the next build.
        var rolledBack = state == EV2pnDevBuildState.Idle && V2pnDevBuild.FailedVersion() is { } failed && failed != V2pnInfo.Version ? failed : null;
        spinDev.IsVisible = busy;
        txtDevState.Text = state switch
        {
            EV2pnDevBuildState.Building => _dev.Step.IsNullOrEmpty() ? L.DevBuilding : string.Format(L.DevBuildingStep, _dev.Step),
            EV2pnDevBuildState.Installing => L.DevInstalling,
            EV2pnDevBuildState.Failed => string.Format(L.DevFailed, _dev.Error),
            _ when _backError != null => _backError,
            _ when rolledBack != null => string.Format(L.DevInstallFailed, rolledBack),
            _ => L.DevIdle,
        };
        txtDevState.Classes.Set("warn", state == EV2pnDevBuildState.Failed || (state == EV2pnDevBuildState.Idle && (_backError != null || rolledBack != null)));

        btnDevBuild.Content = state == EV2pnDevBuildState.Building ? L.DevCancel : L.DevBuild;
        btnDevBuild.IsEnabled = state == EV2pnDevBuildState.Building || (state != EV2pnDevBuildState.Installing && sourceOk);
        btnDevBuild.Classes.Set("primary", state != EV2pnDevBuildState.Building);
        btnDevLog.IsVisible = rolledBack != null ? File.Exists(V2pnDevBuild.InstallLogPath)
            : state is EV2pnDevBuildState.Building or EV2pnDevBuildState.Failed && File.Exists(V2pnDevBuild.LogPath);

        rowDevRelease.IsVisible = V2pnInfo.IsLocalVersion(V2pnInfo.Version);
        txtDevRelease.Text = string.Format(L.DevOnLocal, BuiltFrom() ?? V2pnInfo.Version);
        btnDevRelease.IsEnabled = !busy && _updater.State is not (EV2pnUpdateState.Checking or EV2pnUpdateState.Downloading);
    }

    private async Task ChooseSourceAsync()
    {
        // The picker belongs to the window this panel is in, so it opens over it.
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        string? folder = null;
        try
        {
            if (storage != null)
            {
                var picked = await storage.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = L.DevSource,
                    AllowMultiple = false,
                    SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(new Uri(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))),
                });
                if (picked.Count > 0)
                {
                    folder = Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(picked[0]);
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn developer mode: folder picker", ex);
        }
        if (folder.IsNullOrEmpty())
        {
            return;
        }
        _config.UiItem.V2pnDevSource = folder;
        await ConfigTextHandler.SaveConfig();
        RefreshDev();
    }

    private async Task OnDevBuildAsync()
    {
        if (_dev.State == EV2pnDevBuildState.Building)
        {
            _dev.Cancel();
            return;
        }
        _backError = null;
        // The build goes on, and installs, after this window is closed.
        await _dev.BuildAndInstallAsync(_config.UiItem.V2pnDevSource ?? string.Empty);
    }

    private void OpenDevLog()
    {
        var log = V2pnDevBuild.FailedVersion() != null && _dev.State == EV2pnDevBuildState.Idle ? V2pnDevBuild.InstallLogPath : V2pnDevBuild.LogPath;
        if (File.Exists(log))
        {
            ProcUtils.ProcessStart(log);
        }
    }

    private async Task OnBackToReleaseAsync()
    {
        _backError = null;
        btnDevRelease.IsEnabled = false;
        if (!await _updater.ReturnToReleaseAsync())
        {
            _backError = string.Format(L.DevBackFailed, _updater.Error ?? L.UpdFailed);
        }
        RefreshDev();
    }
}
