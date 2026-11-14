using System.Collections.Specialized;
using Avalonia.VisualTree;
using v2rayN.Desktop.Base;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.ViewModels;

namespace v2rayN.Desktop.Views;

/// <summary>
/// Picks servers for a group or a chain (ticks), or one server for a routing rule or a pinned program.
/// The rows look like the main window's list. Ticks survive a search and a switch to another
/// subscription, and the ticked servers come back in the order they were ticked.
/// </summary>
public partial class ProfilesSelectWindow : WindowBase<ProfilesSelectViewModel>
{
    // Ticked servers in the order they were ticked; rows come and go with the search, these stay.
    private readonly List<ProfileItemModel> _picked = [];
    private readonly DispatcherTimer _searchDelay;
    private List<SideServerRow> _rows = [];
    private bool _syncing;
    private bool _rebuildQueued;

    private bool Multi => ViewModel?.MultiSelect == true;

    public ProfilesSelectWindow()
    {
        InitializeComponent();

        btnCancel.Click += (_, _) => Close();
        btnSave.Click += (_, _) => Finish();
        btnSelectAll.Click += (_, _) => ToggleAllShown();
        lstProfiles.SelectionChanged += LstProfiles_SelectionChanged;
        lstProfiles.DoubleTapped += LstProfiles_DoubleTapped;
        lstProfiles.KeyDown += LstProfiles_KeyDown;
        txtServerFilter.KeyDown += TxtServerFilter_KeyDown;

        // The search runs as you type, a moment after the last key (v2rayN waited for Enter).
        _searchDelay = new DispatcherTimer(TimeSpan.FromMilliseconds(220), DispatcherPriority.Background, (_, _) =>
        {
            _searchDelay?.Stop();
            _ = ViewModel?.RefreshServers();
        });
        txtServerFilter.TextChanged += (_, _) =>
        {
            _searchDelay.Stop();
            _searchDelay.Start();
        };

        this.WhenActivated(disposables =>
        {
            var vm = ViewModel!;
            this.Bind(ViewModel, v => v.SelectedSub, v => v.lstGroup.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, v => v.ServerFilter, v => v.txtServerFilter.Text).DisposeWith(disposables);

            vm.ProfileItems.CollectionChanged += OnProfilesChanged;
            vm.SubItems.CollectionChanged += OnSubsChanged;
            new ActionDisposable(() =>
            {
                vm.ProfileItems.CollectionChanged -= OnProfilesChanged;
                vm.SubItems.CollectionChanged -= OnSubsChanged;
            }).DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.MultiSelect)
                .Subscribe(ApplyMode)
                .DisposeWith(disposables);
            // The view model points at the server in use (or the first) after every refresh.
            this.WhenAnyValue(v => v.ViewModel.SelectedProfile)
                .Subscribe(_ => SyncSingle())
                .DisposeWith(disposables);

            // The view model asks for focus after a subscription switch; the list keeps the keyboard anyway.
            vm.ProfilesFocusInteraction.RegisterHandler(i => i.SetOutput(RxVoid.Default)).DisposeWith(disposables);

            OnSubsChanged(null, null);
            Rebuild();
        });

        Loaded += (_, _) => txtServerFilter.Focus();
    }

    private void ApplyMode(bool multi)
    {
        lstProfiles.SelectionMode = multi ? SelectionMode.Multiple | SelectionMode.Toggle : SelectionMode.Single;
        lstProfiles.Classes.Set("single", !multi);
        Title = tbTitle.Text = multi ? L.PickTitleMany : L.PickTitleOne;
        tbHint.Text = multi ? L.PickHintMany : L.PickHintOne;
        btnSave.Content = multi ? L.PickAdd : L.PickChoose;
        Rebuild();
    }

    // ReplaceRange can arrive as several notifications; one rebuild after them is enough.
    private void OnProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_rebuildQueued)
        {
            return;
        }
        _rebuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rebuildQueued = false;
            Rebuild();
        }, DispatcherPriority.Background);
    }

    // "All" plus one subscription is no choice at all.
    private void OnSubsChanged(object? sender, NotifyCollectionChangedEventArgs? e)
    {
        lstGroup.IsVisible = (ViewModel?.SubItems.Count ?? 0) > 2;
    }

    private void Rebuild()
    {
        var vm = ViewModel;
        if (vm == null)
        {
            return;
        }
        // Under "All" the subscription tells apart servers with the same name; inside one it repeats.
        var all = vm.SelectedSub == null || vm.SelectedSub.Id.IsNullOrEmpty();
        _rows = vm.ProfileItems.Select(m => new SideServerRow(m, showSubscription: all, indent: false)).ToList();
        _syncing = true;
        try
        {
            lstProfiles.ItemsSource = _rows;
            if (Multi)
            {
                var picked = _picked.Select(p => p.IndexId).ToHashSet();
                foreach (var row in _rows.Where(r => picked.Contains(r.Model.IndexId)))
                {
                    lstProfiles.SelectedItems?.Add(row);
                }
            }
        }
        finally
        {
            _syncing = false;
        }
        SyncSingle();

        txtEmpty.IsVisible = _rows.Count == 0;
        txtEmpty.Text = txtServerFilter.Text.IsNotEmpty() ? L.PickNoMatch : L.PickEmpty;
        RefreshFooter();
    }

    private void SyncSingle()
    {
        if (Multi || ViewModel?.SelectedProfile?.IndexId is not { Length: > 0 } id)
        {
            return;
        }
        var row = _rows.FirstOrDefault(r => r.Model.IndexId == id);
        if (row == null || ReferenceEquals(lstProfiles.SelectedItem, row))
        {
            return;
        }
        _syncing = true;
        try
        {
            lstProfiles.SelectedItem = row;
        }
        finally
        {
            _syncing = false;
        }
        lstProfiles.ScrollIntoView(row);
        RefreshFooter();
    }

    private void LstProfiles_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || ViewModel == null)
        {
            return;
        }
        if (Multi)
        {
            foreach (var row in e.RemovedItems.OfType<SideServerRow>())
            {
                _picked.RemoveAll(p => p.IndexId == row.Model.IndexId);
            }
            foreach (var row in e.AddedItems.OfType<SideServerRow>())
            {
                if (!_picked.Any(p => p.IndexId == row.Model.IndexId))
                {
                    _picked.Add(row.Model);
                }
            }
            ViewModel.SelectedProfiles = _picked.ToList();
            if (_picked.Count > 0)
            {
                // SelectFinish asks for a selected profile before it closes.
                ViewModel.SelectedProfile = _picked[0];
            }
        }
        else if (lstProfiles.SelectedItem is SideServerRow row)
        {
            ViewModel.SelectedProfile = row.Model;
        }
        RefreshFooter();
    }

    private void RefreshFooter()
    {
        var multi = Multi;
        var shownPicked = lstProfiles.SelectedItems?.Count ?? 0;
        btnSelectAll.IsVisible = multi && _rows.Count > 0;
        btnSelectAll.Content = multi && _rows.Count > 0 && shownPicked == _rows.Count ? L.PickClear : L.PickSelectAll;
        txtCount.IsVisible = multi;
        txtCount.Text = string.Format(L.PickCount, _picked.Count);
        btnSave.IsEnabled = multi ? _picked.Count > 0 : lstProfiles.SelectedItem != null;
    }

    // Selects every server shown, or clears them when all are already ticked. Ticks elsewhere stay.
    private void ToggleAllShown()
    {
        if ((lstProfiles.SelectedItems?.Count ?? 0) == _rows.Count)
        {
            lstProfiles.SelectedItems?.Clear();
        }
        else
        {
            lstProfiles.SelectAll();
        }
    }

    private void Finish()
    {
        if (ViewModel == null || (Multi && _picked.Count == 0))
        {
            return;
        }
        ViewModel.SelectFinish();
    }

    private void LstProfiles_DoubleTapped(object? sender, TappedEventArgs e)
    {
        // A double click picks one server at once; with ticks it would only untick and tick again.
        if (!Multi && e.Source is Visual source && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) != null)
        {
            e.Handled = true;
            Finish();
        }
    }

    private void LstProfiles_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            Finish();
        }
    }

    private void TxtServerFilter_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter or Key.Return:
                _searchDelay.Stop();
                _ = ViewModel?.RefreshServers();
                e.Handled = true;
                break;

            // From the search straight into the list.
            case Key.Down when _rows.Count > 0:
                var target = lstProfiles.SelectedItem ?? _rows[0];
                lstProfiles.ScrollIntoView(target);
                (lstProfiles.ContainerFromItem(target) as Control)?.Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;
        }
    }
}
