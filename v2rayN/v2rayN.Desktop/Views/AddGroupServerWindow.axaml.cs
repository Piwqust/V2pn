using System.Collections.Specialized;
using Avalonia.Controls.Templates;
using v2rayN.Desktop.Base;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.ViewModels;

namespace v2rayN.Desktop.Views;

/// <summary>A server of a group or a chain as its row shows it.</summary>
public sealed class GroupMemberRow
{
    public required ProfileItem Item { get; init; }
    public required SideServerRow Look { get; init; }
    public string Number { get; init; } = string.Empty;
    public bool Numbered { get; init; }
    public bool IsFirst { get; init; }
    public bool CanUp { get; init; }
    public bool CanDown { get; init; }
}

/// <summary>
/// A server group (V2pn picks one of its servers) or a chain (traffic goes through all of them). The
/// servers picked by hand are rows with their own up, down and remove buttons; a group can also take
/// every server of a subscription. v2rayN showed three tabs of tables and hid the order behind a right click.
/// </summary>
public partial class AddGroupServerWindow : WindowBase<AddGroupServerViewModel>
{
    // Every server as the main list knows it (readable name, subscription, last ping), for the rows.
    private readonly Dictionary<string, ProfileItemModel> _models = [];
    private bool _chain;
    private bool _rebuildQueued;

    public AddGroupServerWindow()
    {
        InitializeComponent();

        Loaded += (_, _) => txtRemarks.Focus();
        btnCancel.Click += (_, _) => Close();

        cmbCoreType.ItemsSource = Global.CoreTypes;
        cmbPolicyGroupType.ItemsSource = new List<string>
        {
            ResUI.TbLeastPing,
            ResUI.TbFallback,
            ResUI.TbRandom,
            ResUI.TbRoundRobin,
            ResUI.TbLeastLoad,
        };
        cmbFilter.ItemsSource = Global.PolicyGroupDefaultFilterList;

        // The view model stores the v2rayN labels; only what the person sees is reworded.
        var policyTemplate = new FuncDataTemplate<string>((s, _) => new TextBlock { Text = PolicyLabel(s) });
        cmbPolicyGroupType.ItemTemplate = policyTemplate;
        cmbPolicyGroupType.SelectionBoxItemTemplate = policyTemplate;
        // The empty entry the view model adds means "no subscription".
        var subTemplate = new FuncDataTemplate<SubItem>((s, _) => new TextBlock
        {
            Text = s == null || s.Id.IsNullOrEmpty() ? L.TlSubNone : s.Remarks,
        });
        cmbSubChildItems.ItemTemplate = subTemplate;
        cmbSubChildItems.SelectionBoxItemTemplate = subTemplate;

        SettingsKit.InstallMoreFields(this, btnMoreFields, HasExtras);

        this.WhenActivated(disposables =>
        {
            var vm = ViewModel!;
            this.WhenAnyValue(v => v.ViewModel.SelectedSource)
                .KeepNotNull()
                .Subscribe(InitializeData)
                .DisposeWith(disposables);

            this.Bind(ViewModel, v => v.SelectedSource.Remarks, v => v.txtRemarks.Text).DisposeWith(disposables);
            this.Bind(ViewModel, v => v.CoreType, v => v.cmbCoreType.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, v => v.PolicyGroupType, v => v.cmbPolicyGroupType.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, v => v.SelectedSubItem, v => v.cmbSubChildItems.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, v => v.Filter, v => v.cmbFilter.Text).DisposeWith(disposables);

            this.BindCommand(ViewModel, v => v.AddCmd, v => v.btnAddChild).DisposeWith(disposables);
            this.BindCommand(ViewModel, v => v.SaveCmd, v => v.btnSave).DisposeWith(disposables);

            vm.ChildItemsObs.CollectionChanged += OnMembersChanged;
            vm.SubItems.CollectionChanged += OnSubsChanged;
            new ActionDisposable(() =>
            {
                vm.ChildItemsObs.CollectionChanged -= OnMembersChanged;
                vm.SubItems.CollectionChanged -= OnSubsChanged;
            }).DisposeWith(disposables);

            // How many servers the subscription brings, once the choice or the name filter settles.
            this.WhenAnyValue(v => v.ViewModel.SelectedSubItem, v => v.ViewModel.Filter)
                .Throttle(TimeSpan.FromMilliseconds(300))
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .SubscribeAsync(async _ => await RefreshSubCountAsync())
                .DisposeWith(disposables);

            RebuildMembers();
            _ = LoadModelsAsync();
        });
    }

    private void InitializeData(ProfileItem profileItem)
    {
        _chain = profileItem.ConfigType == EConfigType.ProxyChain;
        Title = tbPageTitle.Text = _chain ? L.TlChainTitle : L.TlGroupTitle;
        tbPageHint.Text = _chain ? L.TlChainHint : L.TlGroupHint;
        txtRemarks.PlaceholderText = _chain ? L.TlChainNamePlaceholder : L.TlGroupNamePlaceholder;
        // A chain has no "how to pick": it uses every server, in order; nor a subscription to fill it.
        gridPolicyGroup.IsVisible = !_chain;
        groupFromSub.IsVisible = !_chain;
        RebuildMembers();
    }

    private async Task LoadModelsAsync()
    {
        try
        {
            var models = await AppManager.Instance.ProfileModels(string.Empty, string.Empty) ?? [];
            var delays = (await ProfileExManager.Instance.GetProfileExs())
                .GroupBy(x => x.IndexId)
                .ToDictionary(g => g.Key, g => g.First().Delay);
            foreach (var model in models)
            {
                model.Delay = delays.GetValueOrDefault(model.IndexId);
                _models[model.IndexId] = model;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn group rows", ex);
        }
        RebuildMembers();
    }

    private void OnMembersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_rebuildQueued)
        {
            return;
        }
        _rebuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rebuildQueued = false;
            RebuildMembers();
            _ = RefreshSubCountAsync();
        }, DispatcherPriority.Background);
    }

    // A new group starts with "Don't take" chosen, so saving it empty is caught instead of let through.
    private void OnSubsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (ViewModel is { SelectedSubItem: null } vm && vm.SubItems.FirstOrDefault(s => s.Id.IsNullOrEmpty()) is { } none)
        {
            vm.SelectedSubItem = none;
        }
    }

    private void RebuildMembers()
    {
        var items = ViewModel?.ChildItemsObs.ToList() ?? [];
        var rows = items.Select((item, i) => new GroupMemberRow
        {
            Item = item,
            Look = new SideServerRow(ModelOf(item), showSubscription: true, indent: false),
            Number = (i + 1).ToString(CultureInfo.CurrentCulture),
            Numbered = _chain,
            IsFirst = i == 0,
            CanUp = i > 0,
            CanDown = i < items.Count - 1,
        }).ToList();
        lstMembers.ItemsSource = rows;

        txtMembersLabel.Text = rows.Count > 0 ? string.Format(L.TlMembersCount, rows.Count) : L.TlGrpServers;
        txtMembersEmpty.IsVisible = rows.Count == 0;
        txtMembersEmpty.Text = _chain ? L.TlMembersEmptyChain : L.TlMembersEmptyGroup;
        txtOrderNote.IsVisible = rows.Count > 1;
        txtOrderNote.Text = _chain ? L.TlChainOrder : L.TlGroupOrder;
    }

    // A server the list query missed (just imported, say) still gets a row from its own fields.
    private ProfileItemModel ModelOf(ProfileItem item) =>
        _models.GetValueOrDefault(item.IndexId) ?? new ProfileItemModel
        {
            IndexId = item.IndexId,
            ConfigType = item.ConfigType,
            Remarks = item.Remarks,
            Address = item.Address,
            Port = item.Port,
            Network = item.Network,
            StreamSecurity = item.StreamSecurity,
            Subid = item.Subid,
        };

    private async Task RefreshSubCountAsync()
    {
        var vm = ViewModel;
        if (vm == null || _chain || vm.SelectedSubItem?.Id.IsNullOrEmpty() != false)
        {
            txtSubCount.IsVisible = false;
            return;
        }
        try
        {
            await vm.UpdatePreviewList();
            var count = Math.Max(0, vm.AllProfilePreviewItemsObs.Count - vm.ChildItemsObs.Count(c => c.IndexId.IsNotEmpty()));
            txtSubCount.Text = string.Format(L.TlSubCount, count);
            txtSubCount.IsVisible = true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn group preview", ex);
        }
    }

    private async Task MoveAsync(object? sender, EMove move)
    {
        if ((sender as Control)?.DataContext is not GroupMemberRow row || ViewModel == null)
        {
            return;
        }
        ViewModel.SelectedChild = row.Item;
        await ViewModel.MoveServer(move);
    }

    private async void MemberUp_Click(object? sender, RoutedEventArgs e) => await MoveAsync(sender, EMove.Up);

    private async void MemberDown_Click(object? sender, RoutedEventArgs e) => await MoveAsync(sender, EMove.Down);

    private async void MemberRemove_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not GroupMemberRow row || ViewModel == null)
        {
            return;
        }
        ViewModel.SelectedChild = row.Item;
        ViewModel.SelectedChildren = [row.Item];
        await ViewModel.ChildRemoveAsync();
    }

    private static string PolicyLabel(string? value) => value switch
    {
        _ when value == ResUI.TbLeastPing => L.TlPolicyFastest,
        _ when value == ResUI.TbFallback => L.TlPolicyFallback,
        _ when value == ResUI.TbRandom => L.TlPolicyRandom,
        _ when value == ResUI.TbRoundRobin => L.TlPolicyRoundRobin,
        _ when value == ResUI.TbLeastLoad => L.TlPolicyStable,
        _ => value ?? string.Empty,
    };

    private bool HasExtras() =>
        SettingsKit.AnySet(cmbFilter.Text, cmbCoreType.SelectedValue is string core && core.IsNotEmpty() && core != Global.CoreTypes[0]);
}
