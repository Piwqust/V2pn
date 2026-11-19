using DialogHostAvalonia;
using v2rayN.Desktop.Base;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Converters;

namespace v2rayN.Desktop.Views;

/// <summary>
/// Subscriptions: the list on the left, the chosen one (or a new one) on the right with Save below it.
/// Switching away from unsaved changes asks first.
/// </summary>
public partial class SubSettingWindow : WindowBase<SubSettingViewModel>
{
    private SubEditViewModel? _edit;
    private SubEditView? _form;
    private string? _shownId;
    private string _snapshot = string.Empty;
    private bool _syncing;
    private bool _closed;

    public SubSettingWindow()
    {
        InitializeComponent();

        Closing += SubSettingWindow_Closing;
        lstSubscription.SelectionChanged += async (_, _) => await OnPickedAsync();
        btnNewSub.Click += async (_, _) => await SwitchToAsync(null);
        btnCancelNew.Click += (_, _) => ShowFirst();
        btnSave.Click += (_, _) => Save();
        btnShare.Click += (_, _) => Share();
        menuSubDelete.Click += async (_, _) => await DeleteAsync();

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.SubItems, v => v.lstSubscription.ItemsSource).DisposeWith(disposables);

            ViewModel!.ShareSubInteraction.RegisterHandler(async interaction =>
            {
                if (interaction.Input.IsNotEmpty())
                {
                    await DialogHost.Show(new QrcodeView(interaction.Input), "dialogHostSub");
                }
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            var items = ViewModel.SubItems;
            System.Collections.Specialized.NotifyCollectionChangedEventHandler changed = (_, _) => Dispatcher.UIThread.Post(KeepChosen);
            items.CollectionChanged += changed;
            new ActionDisposable(() => items.CollectionChanged -= changed).DisposeWith(disposables);
            Dispatcher.UIThread.Post(KeepChosen);
        });
    }

    #region The list

    private void KeepChosen()
    {
        var items = ViewModel?.SubItems;
        if (items == null)
        {
            return;
        }
        txtNoSubs.IsVisible = items.Count == 0;
        var chosen = items.FirstOrDefault(s => s.Id == _shownId);
        _syncing = true;
        lstSubscription.SelectedItem = chosen;
        _syncing = false;
        if (_form == null)
        {
            _ = ShowAsync(chosen ?? items.FirstOrDefault());
        }
    }

    private void ShowFirst()
    {
        _snapshot = Snapshot();
        _ = ShowAsync(ViewModel?.SubItems.FirstOrDefault());
    }

    private async Task OnPickedAsync()
    {
        if (_syncing || lstSubscription.SelectedItem is not SubItem picked || picked.Id == _shownId)
        {
            return;
        }
        await SwitchToAsync(picked);
    }

    /// <summary>Leaves the form on the right for another subscription (null: a new one), asking about unsaved changes.</summary>
    private async Task SwitchToAsync(SubItem? next)
    {
        if (IsDirty())
        {
            var name = _edit!.SelectedSource.Remarks.IsNotEmpty() ? _edit.SelectedSource.Remarks : L.NewSubscription;
            var answer = await UI.ShowChoice(L.TlSubsTitle, string.Format(L.SubSaveAsk, name), L.Save, L.SubDontSave);
            if (answer == ButtonResult.Yes)
            {
                // Saving re-selects what was saved; the person picks the other one again.
                Save();
                return;
            }
            if (answer != ButtonResult.No)
            {
                KeepChosen();
                return;
            }
        }
        await ShowAsync(next);
    }

    #endregion The list

    #region The chosen subscription

    private async Task ShowAsync(SubItem? item)
    {
        var source = item == null ? new SubItem() : await ConfigDbQuery.GetSubItem(item.Id) ?? new SubItem();
        if (_edit != null)
        {
            _edit.RequestClose -= Edit_RequestClose;
        }
        _edit = new SubEditViewModel(source);
        _edit.RequestClose += Edit_RequestClose;
        _form = new SubEditView { ViewModel = _edit, ShowsTitle = false };
        _shownId = source.Id.IsNullOrEmpty() ? null : source.Id;
        formHost.Content = _form;
        paneScroll.Offset = default;

        var isNew = _shownId == null;
        txtTitle.Text = isNew ? L.NewSubscription : (SubNameOf(source));
        btnSave.Content = isNew ? L.AddServer : L.Save;
        btnCancelNew.IsVisible = isNew && ViewModel?.SubItems.Count > 0;
        btnShare.IsVisible = !isNew && source.Url.IsNotEmpty();
        btnSubMenu.IsVisible = !isNew;

        _syncing = true;
        lstSubscription.SelectedItem = isNew ? null : ViewModel?.SubItems.FirstOrDefault(s => s.Id == _shownId);
        _syncing = false;

        // The bindings and the form's own defaults land after layout; what is there then counts as unchanged.
        Dispatcher.UIThread.Post(() => _snapshot = Snapshot(), DispatcherPriority.Background);
    }

    private static string SubNameOf(SubItem source) =>
        V2pnConverters.SubName.Convert(source, typeof(string), null, CultureInfo.CurrentUICulture) as string ?? string.Empty;

    private string Snapshot() =>
        _edit == null ? string.Empty : $"{JsonUtils.Serialize(_edit.SelectedSource, false)}|{_edit.PrevProfile}|{_edit.NextProfile}|{_edit.CustomCoreType}";

    private bool IsDirty()
    {
        if (_edit == null)
        {
            return false;
        }
        // A new form counts only once something was typed into it.
        if (_shownId == null)
        {
            return _edit.SelectedSource.Url.IsNotEmpty() || _edit.SelectedSource.Remarks.IsNotEmpty();
        }
        return _snapshot.Length > 0 && Snapshot() != _snapshot;
    }

    private void Save()
    {
        if (_form == null || _edit == null)
        {
            return;
        }
        _form.PrepareSave();
        _edit.SaveCmd.Execute().Subscribe();
    }

    private async void Edit_RequestClose(object? sender, EventArgs e)
    {
        if (_edit == null || ViewModel == null)
        {
            return;
        }
        var saved = _edit.SelectedSource;
        _form?.AnnounceIfAdded();
        ViewModel.IsModified = true;
        _shownId = saved.Id;
        _form = null;
        await ViewModel.RefreshSubItems();
        await ShowAsync(ViewModel.SubItems.FirstOrDefault(s => s.Id == saved.Id));
    }

    private void Share()
    {
        if (ViewModel == null || _shownId == null)
        {
            return;
        }
        ViewModel.SelectedSource = ViewModel.SubItems.First(s => s.Id == _shownId);
        ViewModel.SubShareCmd.Execute().Subscribe();
    }

    private async Task DeleteAsync()
    {
        if (ViewModel == null || _shownId == null || _edit == null)
        {
            return;
        }
        var name = SubNameOf(_edit.SelectedSource);
        if (await UI.ShowYesNo(string.Format(L.SubDeleteAsk, name), L.TlSubDelete, danger: true) != ButtonResult.Yes)
        {
            return;
        }
        await ConfigDbHandler.DeleteSubItem(_shownId);
        ViewModel.IsModified = true;
        _shownId = null;
        _form = null;
        _snapshot = string.Empty;
        _edit = null;
        await ViewModel.RefreshSubItems();
        await ShowAsync(ViewModel.SubItems.FirstOrDefault());
    }

    #endregion The chosen subscription

    private async void SubSettingWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_closed)
        {
            return;
        }
        // Unsaved changes are asked about once; closing then tells the main window whether anything changed.
        e.Cancel = true;
        if (IsDirty())
        {
            var name = _edit!.SelectedSource.Remarks.IsNotEmpty() ? _edit.SelectedSource.Remarks : L.NewSubscription;
            var answer = await UI.ShowChoice(L.TlSubsTitle, string.Format(L.SubSaveAsk, name), L.Save, L.SubDontSave);
            if (answer == ButtonResult.Yes)
            {
                Save();
                return;
            }
            if (answer != ButtonResult.No)
            {
                return;
            }
        }
        _closed = true;
        Close(ViewModel?.IsModified == true);
    }
}
