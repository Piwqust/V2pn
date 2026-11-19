using v2rayN.Desktop.Common;
using v2rayN.Desktop.ViewModels;

namespace v2rayN.Desktop.Views;

// V2pn: "Обход блокировок" in the main window: its window from the menu and from a failure, and what the
// automatic search says while it works. Nothing of it shows while the bypass is off.
public partial class MainWindow
{
    private bool _bypassToastShown;

    private IDisposable InitBypass(MainWindowViewModel vm)
    {
        vm.BypassTuned += OnBypassTuned;
        vm.BypassChecked += OnBypassChecked;
        var progress = vm.WhenAnyValue(v => v.BypassTune)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(step =>
            {
                // The automatic search says once that it started; the window's own search shows its steps there.
                if (step == null)
                {
                    _bypassToastShown = false;
                }
                else if (!_bypassToastShown && !_bypassDialogOpen)
                {
                    _bypassToastShown = true;
                    ShowToast(ToastKind.Info, string.Format(L.BypassTuning, L.BypassWayName(step.Variant?.Id), step.Index, step.Count));
                }
            });
        RefreshBypassUi(false);
        return new Unsubscribe(() =>
        {
            progress.Dispose();
            vm.BypassTuned -= OnBypassTuned;
            vm.BypassChecked -= OnBypassChecked;
        });
    }

    private sealed class Unsubscribe(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    private bool _bypassDialogOpen;

    /// <summary>From the settings window, which closes first.</summary>
    internal void OpenBypass() => Dispatcher.UIThread.Post(() => _ = ShowBypassAsync());

    private async Task ShowBypassAsync()
    {
        if (ViewModel is not { } vm || _bypassDialogOpen)
        {
            return;
        }
        _bypassDialogOpen = true;
        try
        {
            await ShowRoomyDialog(new BypassView(vm, ServerCountry, ProbeReplacementAsync,
                id => vm.ProfilesViewModel.SetDefaultServer(id)));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(ShowBypassAsync), ex);
        }
        finally
        {
            _bypassDialogOpen = false;
        }
    }

    /// <summary>The menu item while the bypass is on; the button next to a failure as well.</summary>
    private void RefreshBypassUi(bool failure)
    {
        var on = V2pnBypass.On(_config);
        menuBypass.IsVisible = on;
        btnBypass.IsVisible = on && failure;
    }

    private void OnBypassTuned(V2pnTuneOutcome outcome, bool manual)
    {
        if (manual)
        {
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            if (outcome.Cancelled)
            {
                return;
            }
            if (outcome.Worked)
            {
                ShowToast(ToastKind.Success, outcome.Variant is { } v ? string.Format(L.BypassFound, L.BypassWayName(v.Id)) : L.BypassFoundNone);
            }
            else
            {
                ShowToast(ToastKind.Warning, L.BypassNotFound);
            }
        });
    }

    private void OnBypassChecked(V2pnBlockReport report)
    {
        if (BypassView.Headline(report) is not { } headline)
        {
            return;
        }
        Dispatcher.UIThread.Post(() => ShowToast(ToastKind.Warning, string.Format(L.BypassAutoVerdict, headline)));
    }
}
