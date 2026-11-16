namespace ServiceLib.ViewModels;

public partial class ProfilesViewModel : MyReactiveObject
{
    public Interaction<string, bool> ShowYesNoInteraction { get; } = new();
    public Interaction<ProfileItem, bool> SaveFileDialogInteraction { get; } = new();
    public Interaction<string, RxVoid> SetClipboardDataInteraction { get; } = new();
    public Interaction<RxVoid, RxVoid> ProfilesFocusInteraction { get; } = new();
    public Interaction<string, RxVoid> ShareServerInteraction { get; } = new();
    public Interaction<RxVoid, RxVoid> DispatcherRefreshServersBizInteraction { get; } = new();
    public Interaction<RxVoid, RxVoid> AdjustMainLvColWidthInteraction { get; } = new();

    public EventChannel<RxVoid> ReloadRequested { get; } = new();
    public EventChannel<RxVoid> RefreshServersRequested { get; } = new();

    #region private prop

    private string _serverFilter = string.Empty;
    private readonly Dictionary<string, bool> _dicHeaderSort = new();
    private SpeedtestService? _speedtestService;
    private HashSet<string> _pingPending = [];

    /// <summary>Raised when a ping run finishes or is stopped.</summary>
    public EventChannel<RxVoid> PingTestFinished { get; } = new();

    [Reactive]
    public partial bool IsPingTesting { get; set; }

    [Reactive]
    public partial int PingTotal { get; set; }

    [Reactive]
    public partial int PingDone { get; set; }
    private string? _pendingSelectIndexId;

    #endregion private prop

    #region ObservableCollection

    public BulkObservableCollection<ProfileItemModel> ProfileItems { get; } = [];

    public BulkObservableCollection<SubItem> SubItems { get; } = [];

    /// <summary>Subscriptions with server counts, without the synthetic "all servers" entry.</summary>
    public BulkObservableCollection<SubItemModel> SubNavItems { get; } = [];

    /// <summary>Favorite servers across every subscription.</summary>
    public BulkObservableCollection<ProfileItemModel> FavoriteItems { get; } = [];

    [Reactive]
    public partial bool FavoritesOnly { get; set; }

    [Reactive]
    public partial int AllServersCount { get; set; }

    [Reactive]
    public partial int FavoritesCount { get; set; }

    [Reactive]
    public partial ProfileItemModel SelectedProfile { get; set; }

    public IList<ProfileItemModel> SelectedProfiles { get; set; }

    [Reactive]
    public partial SubItem SelectedSub { get; set; }

    [Reactive]
    public partial SubItem SelectedMoveToGroup { get; set; }

    [Reactive]
    public partial string ServerFilter { get; set; }

    #endregion ObservableCollection

    #region Menu

    //servers delete
    public ReactiveCommand<RxVoid, RxVoid> EditServerCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> RemoveServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveDuplicateServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> CopyServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SetDefaultServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> ShareServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> GenGroupAllServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> GenGroupRegionServerCmd { get; }

    //servers move
    public ReactiveCommand<RxVoid, RxVoid> MoveTopCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> MoveUpCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> MoveDownCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> MoveBottomCmd { get; }
    public ReactiveCommand<SubItem, RxVoid> MoveToGroupCmd { get; }

    //servers ping
    public ReactiveCommand<RxVoid, RxVoid> MixedTestServerCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> TcpingServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> RealPingServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> UdpTestServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SpeedServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SortServerResultCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveInvalidServerResultCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> FastRealPingCmd { get; }

    //servers export
    public ReactiveCommand<RxVoid, RxVoid> Export2ClientConfigCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> Export2ClientConfigClipboardCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> Export2ShareUrlCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> Export2ShareUrlBase64Cmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> Export2InnerUriCmd { get; }

    public ReactiveCommand<ProfileItemModel?, RxVoid> ToggleFavoriteCmd { get; }
    public ReactiveCommand<ProfileItemModel, RxVoid> ActivateServerCmd { get; }
    public ReactiveCommand<ProfileItemModel, RxVoid> PingServerCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> AddSubCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> EditSubCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteSubCmd { get; }

    #endregion Menu

    #region Init

    public ProfilesViewModel()
    {
        _config = AppManager.Instance.Config;

        #region WhenAnyValue && ReactiveCommand

        var canEditRemove = this.WhenAnyValue(
           x => x.SelectedProfile,
           selectedSource => selectedSource != null && !selectedSource.IndexId.IsNullOrEmpty());

        this.WhenAnyValue(x => x.SelectedSub)
            .Where(y => y != null && !y.Remarks.IsNullOrEmpty() && _config.SubIndexId != y.Id)
            .SubscribeAsync(async _ => await SubSelectedChangedAsync());
        this.WhenAnyValue(x => x.SelectedMoveToGroup)
            .Where(y => y != null && !y.Remarks.IsNullOrEmpty())
            .SubscribeAsync(async _ => await MoveToGroup());

        this.WhenAnyValue(x => x.FavoritesOnly)
            .Skip(1)
            .SubscribeAsync(async _ => await RefreshServers());

        this.WhenAnyValue(x => x.ServerFilter)
            .Where(y => y != null && _serverFilter != y)
            .SubscribeAsync(async _ => await ServerFilterChanged());

        //servers delete
        EditServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await EditServerAsync();
        }, canEditRemove);
        RemoveServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await RemoveServerAsync();
        }, canEditRemove);
        RemoveDuplicateServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await RemoveDuplicateServer();
        });
        CopyServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await CopyServer();
        }, canEditRemove);
        SetDefaultServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetDefaultServer();
        }, canEditRemove);
        ShareServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ShareServerAsync();
        }, canEditRemove);
        GenGroupAllServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await GenGroupAllServer();
        }, canEditRemove);
        GenGroupRegionServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await GenGroupRegionServer();
        }, canEditRemove);

        //servers move
        MoveTopCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await MoveServer(EMove.Top);
        }, canEditRemove);
        MoveUpCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await MoveServer(EMove.Up);
        }, canEditRemove);
        MoveDownCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await MoveServer(EMove.Down);
        }, canEditRemove);
        MoveBottomCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await MoveServer(EMove.Bottom);
        }, canEditRemove);
        MoveToGroupCmd = ReactiveCommand.CreateFromTask<SubItem>(async sub =>
        {
            SelectedMoveToGroup = sub;
        });

        //servers ping
        FastRealPingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ServerSpeedtest(ESpeedActionType.FastRealping);
        });
        MixedTestServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ServerSpeedtest(ESpeedActionType.Mixedtest);
        });
        TcpingServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ServerSpeedtest(ESpeedActionType.Tcping);
        }, canEditRemove);
        RealPingServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ServerSpeedtest(ESpeedActionType.Realping);
        }, canEditRemove);
        UdpTestServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ServerSpeedtest(ESpeedActionType.UdpTest);
        }, canEditRemove);
        SpeedServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ServerSpeedtest(ESpeedActionType.Speedtest);
        }, canEditRemove);
        SortServerResultCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SortServer(nameof(EServerColName.DelayVal));
        });
        RemoveInvalidServerResultCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await RemoveInvalidServerResult();
        });
        //servers export
        Export2ClientConfigCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await Export2ClientConfigAsync(false);
        }, canEditRemove);
        Export2ClientConfigClipboardCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await Export2ClientConfigAsync(true);
        }, canEditRemove);
        Export2ShareUrlCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await Export2ShareUrlAsync(false);
        }, canEditRemove);
        Export2ShareUrlBase64Cmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await Export2ShareUrlAsync(true);
        }, canEditRemove);
        Export2InnerUriCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await Export2InnerUrlAsync();
        }, canEditRemove);

        ToggleFavoriteCmd = ReactiveCommand.CreateFromTask<ProfileItemModel?>(async item =>
        {
            await ToggleFavoriteAsync(item);
        });
        ActivateServerCmd = ReactiveCommand.CreateFromTask<ProfileItemModel>(async item =>
        {
            await SetDefaultServer(item?.IndexId);
        });
        PingServerCmd = ReactiveCommand.CreateFromTask<ProfileItemModel>(async item =>
        {
            await PingProfileAsync(item);
        });

        //Subscription
        AddSubCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await EditSubAsync(true);
        });
        EditSubCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await EditSubAsync(false);
        });
        DeleteSubCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await DeleteSubAsync();
        });

        #endregion WhenAnyValue && ReactiveCommand

        #region AppEvents

        AppEvents.DispatcherStatisticsRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async result => await UpdateStatistics(result));

        #endregion AppEvents

        _ = Init();
    }

    private async Task Init()
    {
        SelectedProfile = new();
        SelectedSub = new();
        SelectedMoveToGroup = new();

        await RefreshSubscriptions();
        //await RefreshServers();
    }

    #endregion Init

    #region Actions

    private void Reload()
    {
        ReloadRequested.Publish();
    }

    public async Task SetSpeedTestResult(SpeedTestResult result)
    {
        if (result.IndexId.IsNullOrEmpty())
        {
            NoticeManager.Instance.SendMessageEx(result.Delay);
            var finished = result.Delay == ResUI.SpeedtestingCompleted || result.Delay == ResUI.SpeedtestingStop;
            if (finished)
            {
                // The sidebar shows progress and completion inline, so no toast for the routine ending.
                IsPingTesting = false;
                _pingPending.Clear();
                _config.UiItem.V2pnLastPing = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                PingTestFinished.Publish(RxVoid.Default);
                _ = ServerUsageManager.Instance.SaveAsync();
            }
            else
            {
                NoticeManager.Instance.Enqueue(result.Delay);
            }
            return;
        }
        if (result.Delay.IsNotEmpty() && result.Delay != ResUI.Speedtesting && _pingPending.Remove(result.IndexId))
        {
            PingDone = PingTotal - _pingPending.Count;
        }
        var favorite = FavoriteItems.FirstOrDefault(it => it.IndexId == result.IndexId);
        if (favorite != null && result.Delay.IsNotEmpty())
        {
            favorite.Delay = result.Delay.ToInt();
            favorite.DelayVal = result.Delay ?? string.Empty;
        }

        var item = ProfileItems.FirstOrDefault(it => it.IndexId == result.IndexId);
        if (item == null)
        {
            return;
        }

        if (result.Delay.IsNotEmpty())
        {
            item.Delay = result.Delay.ToInt();
            item.DelayVal = result.Delay ?? string.Empty;
        }
        if (result.Speed.IsNotEmpty())
        {
            item.SpeedVal = result.Speed ?? string.Empty;
        }
        if (result.IpInfo.IsNotEmpty())
        {
            item.IpInfo = result.IpInfo ?? string.Empty;
        }
        await Task.CompletedTask;
    }

    public async Task UpdateStatistics(ServerSpeedItem update)
    {
        if (!_config.GuiItem.EnableStatistics
            || (update.ProxyUp + update.ProxyDown) <= 0
            || DateTime.Now.Second % 3 != 0)
        {
            return;
        }

        try
        {
            var item = ProfileItems.FirstOrDefault(it => it.IndexId == update.IndexId);
            if (item != null)
            {
                item.TodayDown = Utils.HumanFy(update.TodayDown);
                item.TodayUp = Utils.HumanFy(update.TodayUp);
                item.TotalDown = Utils.HumanFy(update.TotalDown);
                item.TotalUp = Utils.HumanFy(update.TotalUp);
            }
        }
        catch
        {
        }
        await Task.CompletedTask;
    }

    #endregion Actions

    #region Servers && Groups

    private async Task SubSelectedChangedAsync()
    {
        _config.SubIndexId = SelectedSub?.Id;

        await RefreshServers();

        await ProfilesFocusInteraction.HandleSafe(RxVoid.Default);
    }

    private async Task ServerFilterChanged()
    {
        _serverFilter = ServerFilter;
        if (_serverFilter.IsNullOrEmpty())
        {
            await RefreshServers();
        }
    }

    public async Task RefreshServers()
    {
        RefreshServersRequested.Publish();

        // await Task.Delay(200);

        await Task.CompletedTask;
    }

    public async Task RefreshServersBiz()
    {
        var lstModel = await GetProfileItemsEx(_config.SubIndexId, _serverFilter);
        // V2pn: servers a partner hides are not offered (its own recommendation can still connect to one).
        lstModel = lstModel?.Where(m => !V2pnPartnerLive.Instance.Hidden(m.Subid, m.Remarks, m.Address)).ToList();

        ProfileItems.ReplaceRange(lstModel ?? []);
        if (lstModel?.Count > 0)
        {
            ProfileItemModel? selected = null;
            if (!_pendingSelectIndexId.IsNullOrEmpty())
            {
                selected = lstModel.FirstOrDefault(t => t.IndexId == _pendingSelectIndexId);
                _pendingSelectIndexId = null;
            }
            selected ??= lstModel.FirstOrDefault(t => t.IndexId == _config.IndexId);
            SelectedProfile = selected ?? lstModel.First();
        }

        await RefreshNavCounts();

        await DispatcherRefreshServersBizInteraction.HandleSafe(RxVoid.Default);
    }

    /// <summary>
    /// Recomputes sidebar counters and the favorites strip from every stored server.
    /// </summary>
    public async Task RefreshNavCounts()
    {
        var all = await AppManager.Instance.ProfileModels(string.Empty, string.Empty) ?? [];
        var lstProfileExs = await ProfileExManager.Instance.GetProfileExs();
        var favoriteIds = lstProfileExs.Where(t => t.IsFavorite).Select(t => t.IndexId).ToHashSet();

        AllServersCount = all.Count;

        // V2pn: a server its partner hides is not counted either.
        var counts = all.Where(t => !V2pnPartnerLive.Instance.Hidden(t.Subid, t.Remarks, t.Address))
            .GroupBy(t => t.Subid ?? string.Empty).ToDictionary(g => g.Key, g => g.Count());
        // An update writes UpdateTime to the database; the cached items need it too for "updated 5 min ago".
        var stored = (await AppManager.Instance.SubItems() ?? []).ToDictionary(t => t.Id ?? string.Empty, t => t.UpdateTime);
        foreach (var sub in SubNavItems)
        {
            sub.Count = counts.GetValueOrDefault(sub.Id ?? string.Empty);
            if (stored.TryGetValue(sub.Id ?? string.Empty, out var updated))
            {
                sub.Item.UpdateTime = updated;
            }
        }

        var favorites = (from t in all
                         where favoriteIds.Contains(t.IndexId) && !V2pnPartnerLive.Instance.Hidden(t.Subid, t.Remarks, t.Address)
                         join t3 in lstProfileExs on t.IndexId equals t3.IndexId into t3b
                         from t33 in t3b.DefaultIfEmpty()
                         orderby t33?.Sort ?? 0
                         select new ProfileItemModel
                         {
                             IndexId = t.IndexId,
                             ConfigType = t.ConfigType,
                             Remarks = t.Remarks,
                             Address = t.Address,
                             Port = t.Port,
                             Network = t.Network,
                             StreamSecurity = t.StreamSecurity,
                             Subid = t.Subid,
                             SubRemarks = t.SubRemarks,
                             IsActive = t.IndexId == _config.IndexId,
                             IsFavorite = true,
                             Sort = t33?.Sort ?? 0,
                             Delay = t33?.Delay ?? 0,
                             DelayVal = t33?.Delay != 0 ? $"{t33?.Delay}" : string.Empty,
                         }).ToList();
        FavoritesCount = favorites.Count;
        FavoriteItems.ReplaceRange(favorites);
    }

    /// <summary>
    /// Switches the server list to a subscription, to every server, or to favorites only.
    /// </summary>
    public void SelectNav(SubItem? sub, bool favoritesOnly)
    {
        var target = favoritesOnly || sub == null
            ? SubItems.FirstOrDefault()
            : SubItems.FirstOrDefault(t => t.Id == sub.Id) ?? sub;
        SelectedSub = target;
        FavoritesOnly = favoritesOnly;
    }

    public async Task ToggleFavoriteAsync(ProfileItemModel? item)
    {
        List<ProfileItemModel> targets = item != null
            ? [item]
            : SelectedProfiles?.Where(t => t != null).ToList() ?? [];
        if (targets.Count == 0)
        {
            return;
        }

        // A mixed selection becomes all favorites; an all-favorite selection is cleared.
        var value = item != null ? !item.IsFavorite : targets.Any(t => !t.IsFavorite);
        foreach (var target in targets)
        {
            ProfileExManager.Instance.SetFavorite(target.IndexId, value);
            target.IsFavorite = value;
            var listed = ProfileItems.FirstOrDefault(t => t.IndexId == target.IndexId);
            if (listed != null)
            {
                listed.IsFavorite = value;
            }
        }
        await ProfileExManager.Instance.SaveTo();

        if (FavoritesOnly && !value)
        {
            await RefreshServers();
        }
        else
        {
            await RefreshNavCounts();
        }
    }

    /// <summary>Real-ping test for a set of servers, with progress in IsPingTesting / PingDone / PingTotal.</summary>
    public async Task PingProfilesAsync(IEnumerable<ProfileItemModel> items)
    {
        var ids = items.Select(t => t.IndexId).Where(t => t.IsNotEmpty()).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }
        var profiles = await AppManager.Instance.GetProfileItemsOrderedByIndexIds(ids);
        if (profiles.Count == 0)
        {
            return;
        }
        _pingPending = profiles.Select(t => t.IndexId).ToHashSet();
        PingTotal = _pingPending.Count;
        PingDone = 0;
        IsPingTesting = true;
        await RunSpeedtest(ESpeedActionType.Realping, profiles);
    }

    public async Task PingProfileAsync(ProfileItemModel? item)
    {
        if (item == null || item.IndexId.IsNullOrEmpty())
        {
            return;
        }
        var profile = await AppManager.Instance.GetProfileItem(item.IndexId);
        if (profile == null)
        {
            return;
        }
        await RunSpeedtest(ESpeedActionType.Realping, [profile]);
    }

    public async Task RefreshSubscriptions()
    {
        var subItems = await AppManager.Instance.SubItems();
        subItems.Insert(0, new SubItem { Remarks = ResUI.AllGroupServers });

        SubItems.ReplaceRange(subItems);
        SubNavItems.ReplaceRange(subItems.Skip(1).Select(t => new SubItemModel(t)));
        // V2pn: fresh models start at zero servers; count them again so the sidebar never shows "0 servers".
        await RefreshNavCounts();

        SelectedSub = (_config.SubIndexId.IsNotEmpty()
                        ? subItems.FirstOrDefault(t => t.Id == _config.SubIndexId)
                        : null) ?? subItems.FirstOrDefault();
    }

    public async Task AdjustMainLvColWidth()
    {
        await AdjustMainLvColWidthInteraction.HandleSafe(RxVoid.Default);
    }

    private async Task<List<ProfileItemModel>?> GetProfileItemsEx(string subid, string filter)
    {
        var lstModel = await AppManager.Instance.ProfileModels(_config.SubIndexId, filter);

        await ConfigHandler.SetDefaultServer(_config, lstModel);

        var lstServerStat = (_config.GuiItem.EnableStatistics ? StatisticsManager.Instance.ServerStat : null) ?? [];
        var lstProfileExs = await ProfileExManager.Instance.GetProfileExs();
        lstModel = (from t in lstModel
                    join t2 in lstServerStat on t.IndexId equals t2.IndexId into t2b
                    from t22 in t2b.DefaultIfEmpty()
                    join t3 in lstProfileExs on t.IndexId equals t3.IndexId into t3b
                    from t33 in t3b.DefaultIfEmpty()
                    select new ProfileItemModel
                    {
                        IndexId = t.IndexId,
                        ConfigType = t.ConfigType,
                        Remarks = t.Remarks,
                        Address = t.Address,
                        Port = t.Port,
                        //Security = t.Security,
                        Network = t.Network,
                        StreamSecurity = t.StreamSecurity,
                        Subid = t.Subid,
                        SubRemarks = t.SubRemarks,
                        IsActive = t.IndexId == _config.IndexId,
                        IsFavorite = t33?.IsFavorite ?? false,
                        Sort = t33?.Sort ?? 0,
                        Delay = t33?.Delay ?? 0,
                        Speed = t33?.Speed ?? 0,
                        DelayVal = t33?.Delay != 0 ? $"{t33?.Delay}" : string.Empty,
                        SpeedVal = t33?.Speed > 0 ? $"{t33?.Speed}" : t33?.Message ?? string.Empty,
                        IpInfo = t33?.IpInfo ?? string.Empty,
                        TodayDown = t22 == null ? "" : Utils.HumanFy(t22.TodayDown),
                        TodayUp = t22 == null ? "" : Utils.HumanFy(t22.TodayUp),
                        TotalDown = t22 == null ? "" : Utils.HumanFy(t22.TotalDown),
                        TotalUp = t22 == null ? "" : Utils.HumanFy(t22.TotalUp)
                    }).OrderBy(t => t.Sort).ToList();

        if (FavoritesOnly)
        {
            lstModel = lstModel.Where(t => t.IsFavorite).ToList();
        }

        return lstModel;
    }

    #endregion Servers && Groups

    #region Add Servers

    private async Task<List<ProfileItem>?> GetProfileItems(bool latest)
    {
        var lstSelected = new List<ProfileItem>();
        if (SelectedProfiles == null || SelectedProfiles.Count <= 0)
        {
            return null;
        }

        var orderProfiles = SelectedProfiles?.OrderBy(t => t.Sort).ToList();
        if (latest)
        {
            lstSelected.AddRange(await AppManager.Instance.GetProfileItemsOrderedByIndexIds(orderProfiles.Select(sp => sp?.IndexId)));
        }
        else
        {
            lstSelected = JsonUtils.Deserialize<List<ProfileItem>>(JsonUtils.Serialize(orderProfiles));
        }

        return lstSelected;
    }

    /// <summary>
    /// V2pn: true (and the person is told why) when a partner closed the settings of any of <paramref name="items"/>
    /// (V2pnLocks): its servers are not edited, copied, shared, exported or moved out of its subscription.
    /// </summary>
    private static async Task<bool> ClosedByPartnerAsync(IEnumerable<ProfileItem> items)
    {
        var list = items.ToList();
        if (!await V2pnLocks.Instance.AnyClosedAsync(list))
        {
            return false;
        }
        var name = list.Select(i => V2pnLocks.Instance.ForSub(i.Subid).PartnerName).FirstOrDefault(n => n != null);
        NoticeManager.Instance.Enqueue(name != null ? string.Format(ResUI.V2pnLockedServer, name) : ResUI.V2pnLockedServerNoName);
        return true;
    }

    public async Task EditServerAsync()
    {
        if (string.IsNullOrEmpty(SelectedProfile?.IndexId))
        {
            return;
        }
        var item = await AppManager.Instance.GetProfileItem(SelectedProfile.IndexId);
        if (item is null)
        {
            NoticeManager.Instance.Enqueue(ResUI.PleaseSelectServer);
            return;
        }
        if (await ClosedByPartnerAsync([item]))
        {
            return;
        }
        var eConfigType = item.ConfigType;

        bool? ret = false;
        if (eConfigType is EConfigType.Custom or EConfigType.Outbound)
        {
            var addServer2ViewModel = new AddServer2ViewModel(item);
            ret = await AppManager.Instance.WindowDialog.ShowDialogAsync(addServer2ViewModel);
        }
        else if (eConfigType.IsGroupType())
        {
            var addGroupServerViewModel = new AddGroupServerViewModel(item);
            ret = await AppManager.Instance.WindowDialog.ShowDialogAsync(addGroupServerViewModel);
        }
        else
        {
            var addServerViewModel = new AddServerViewModel(item);
            ret = await AppManager.Instance.WindowDialog.ShowDialogAsync(addServerViewModel);
        }
        if (ret == true)
        {
            await RefreshServers();
            if (item.IndexId == _config.IndexId)
            {
                Reload();
            }
        }
    }

    public async Task RemoveServerAsync()
    {
        var lstSelected = await GetProfileItems(true);
        if (lstSelected == null)
        {
            return;
        }
        if (await ShowYesNoInteraction.HandleSafe(ResUI.RemoveServer) == false)
        {
            return;
        }
        var exists = lstSelected.Exists(t => t.IndexId == _config.IndexId);

        await ConfigHandler.RemoveServers(_config, lstSelected);
        NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
        if (lstSelected.Count == ProfileItems.Count)
        {
            ProfileItems.Clear();
        }
        await RefreshServers();
        if (exists)
        {
            Reload();
        }
    }

    private async Task RemoveDuplicateServer()
    {
        if (await ShowYesNoInteraction.HandleSafe(ResUI.RemoveServer) == false)
        {
            return;
        }

        var tuple = await ConfigHandler.DedupServerList(_config, _config.SubIndexId);
        if (tuple.Item1 > 0 || tuple.Item2 > 0)
        {
            await RefreshServers();
            Reload();
        }
        NoticeManager.Instance.Enqueue(string.Format(ResUI.RemoveDuplicateServerResult, tuple.Item1, tuple.Item2));
    }

    private async Task CopyServer()
    {
        var lstSelected = await GetProfileItems(false);
        if (lstSelected == null || await ClosedByPartnerAsync(lstSelected))
        {
            return;
        }
        if (await ConfigHandler.CopyServer(_config, lstSelected) == 0)
        {
            await RefreshServers();
            NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
        }
    }

    public async Task SetDefaultServer()
    {
        if (string.IsNullOrEmpty(SelectedProfile?.IndexId))
        {
            return;
        }
        await SetDefaultServer(SelectedProfile.IndexId);
    }

    public async Task SetDefaultServer(string? indexId)
    {
        if (indexId.IsNullOrEmpty())
        {
            return;
        }
        if (indexId == _config.IndexId)
        {
            return;
        }
        var item = await AppManager.Instance.GetProfileItem(indexId);
        if (item is null)
        {
            NoticeManager.Instance.Enqueue(ResUI.PleaseSelectServer);
            return;
        }

        if (await ConfigHandler.SetDefaultServerIndex(_config, indexId) == 0)
        {
            await RefreshServers();
            Reload();
        }
    }

    public async Task ShareServerAsync()
    {
        var item = await AppManager.Instance.GetProfileItem(SelectedProfile.IndexId);
        if (item is null)
        {
            NoticeManager.Instance.Enqueue(ResUI.PleaseSelectServer);
            return;
        }
        if (await ClosedByPartnerAsync([item]))
        {
            return;
        }
        var url = FmtHandler.GetShareUri(item);
        if (url.IsNullOrEmpty())
        {
            return;
        }

        await ShareServerInteraction.HandleSafe(url);
    }

    private async Task GenGroupAllServer()
    {
        var ret = await ConfigHandler.AddGroupAllServer(_config, SelectedSub);
        if (ret.Success != true)
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
            return;
        }
        _pendingSelectIndexId = ret.Data?.ToString();
        await RefreshServers();
    }

    private async Task GenGroupRegionServer()
    {
        var ret = await ConfigHandler.AddGroupRegionServer(_config, SelectedSub);
        if (ret.Success != true)
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
            return;
        }
        var indexIdList = ret.Data as List<string>;
        _pendingSelectIndexId = indexIdList?.FirstOrDefault();
        await RefreshServers();
    }

    public async Task SortServer(string colName)
    {
        if (colName.IsNullOrEmpty())
        {
            return;
        }

        _dicHeaderSort.TryAdd(colName, true);
        _dicHeaderSort.TryGetValue(colName, out var asc);
        if (await ConfigHandler.SortServers(_config, _config.SubIndexId, colName, asc) != 0)
        {
            return;
        }
        _dicHeaderSort[colName] = !asc;
        await RefreshServers();
    }

    public async Task RemoveInvalidServerResult()
    {
        var count = await ConfigHandler.RemoveInvalidServerResult(_config, _config.SubIndexId);
        await RefreshServers();
        NoticeManager.Instance.Enqueue(string.Format(ResUI.RemoveInvalidServerResultTip, count));
    }

    //move server
    private async Task MoveToGroup()
    {
        var lstSelected = await GetProfileItems(true);
        if (lstSelected == null || await ClosedByPartnerAsync(lstSelected))
        {
            return;
        }

        await ConfigHandler.MoveToGroup(_config, lstSelected, SelectedMoveToGroup.Id);
        NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);

        await RefreshServers();
        SelectedMoveToGroup = null;
        SelectedMoveToGroup = new();
    }

    public async Task MoveServer(EMove eMove)
    {
        var lstProfile = ProfileItems?.Select(t => t.IndexId).ToList() ?? [];
        var index = lstProfile.IndexOf(SelectedProfile.IndexId);
        if (index < 0)
        {
            NoticeManager.Instance.Enqueue(ResUI.PleaseSelectServer);
            return;
        }

        if (await ConfigHandler.MoveServer(_config, lstProfile, index, eMove) == 0)
        {
            await RefreshServers();
        }
    }

    public async Task MoveServerTo(int startIndex, ProfileItemModel targetItem)
    {
        var targetIndex = ProfileItems.IndexOf(targetItem);
        if (startIndex >= 0 && targetIndex >= 0 && startIndex != targetIndex)
        {
            var lstProfile = ProfileItems?.Select(t => t.IndexId).ToList() ?? [];
            if (await ConfigHandler.MoveServer(_config, lstProfile, startIndex, EMove.Position, targetIndex) == 0)
            {
                await RefreshServers();
            }
        }
    }

    public async Task MoveServersTo(
        IReadOnlyList<int> sourceIndices,
        int insertIndex)
    {
        if (sourceIndices.Count == 0 ||
            insertIndex < 0 ||
            insertIndex > ProfileItems.Count)
        {
            return;
        }

        var orderedProfileIds = ProfileItems
            .Select(item => item.IndexId)
            .ToList();

        var result = await ConfigHandler.MoveServersTo(
            _config,
            orderedProfileIds,
            sourceIndices,
            insertIndex);

        if (result == 0)
        {
            await RefreshServers();
        }
    }

    public async Task ServerSpeedtest(ESpeedActionType actionType)
    {
        List<ProfileItem>? lstSelected;
        if (actionType is ESpeedActionType.Mixedtest or ESpeedActionType.FastRealping)
        {
            if (actionType == ESpeedActionType.FastRealping)
            {
                actionType = ESpeedActionType.Realping;
            }

            lstSelected = JsonUtils.Deserialize<List<ProfileItem>>(JsonUtils.Serialize(ProfileItems?.OrderBy(t => t.Sort).ToList()));
        }
        else
        {
            lstSelected = await GetProfileItems(false);
        }

        await RunSpeedtest(actionType, lstSelected);
    }

    private async Task RunSpeedtest(ESpeedActionType actionType, List<ProfileItem>? lstSelected)
    {
        if (lstSelected is null || lstSelected.Count <= 0)
        {
            return;
        }

        _speedtestService ??= new SpeedtestService(_config, async (SpeedTestResult result) =>
        {
            RxSchedulers.MainThreadScheduler.Schedule(() =>
            {
                _ = SetSpeedTestResult(result);
            });
            await Task.CompletedTask;
        });
        // V2pn: on the thread pool. Started from here, every await of the run came back to the UI thread, which built
        // the test config for all the servers (about a second for 1000) while the window stood still. Results reach
        // the UI through the callback above.
        var service = _speedtestService;
        _ = Task.Run(() => service?.RunLoop(actionType, lstSelected) ?? Task.CompletedTask);
        await Task.CompletedTask;
    }

    public void ServerSpeedtestStop()
    {
        _speedtestService?.ExitLoop();
    }

    private async Task Export2ClientConfigAsync(bool blClipboard)
    {
        var item = await AppManager.Instance.GetProfileItem(SelectedProfile.IndexId);
        if (item is null)
        {
            NoticeManager.Instance.Enqueue(ResUI.PleaseSelectServer);
            return;
        }
        if (await ClosedByPartnerAsync([item]))
        {
            return;
        }

        var (context, validatorResult) = await CoreConfigContextBuilder.Build(_config, item);
        if (NoticeManager.Instance.NotifyValidatorResult(validatorResult) && !validatorResult.Success)
        {
            return;
        }

        if (blClipboard)
        {
            var result = await CoreConfigHandler.GenerateClientConfig(context, null);
            if (result.Success != true)
            {
                NoticeManager.Instance.Enqueue(result.Msg);
            }
            else
            {
                await SetClipboardDataInteraction.HandleSafe((string)result.Data);
                NoticeManager.Instance.SendMessage(ResUI.OperationSuccess);
            }
        }
        else
        {
            await SaveFileDialogInteraction.HandleSafe(item);
        }
    }

    public async Task Export2ClientConfigResult(string fileName, ProfileItem item)
    {
        if (fileName.IsNullOrEmpty() || await ClosedByPartnerAsync([item]))
        {
            return;
        }
        var (context, validatorResult) = await CoreConfigContextBuilder.Build(_config, item);
        if (NoticeManager.Instance.NotifyValidatorResult(validatorResult) && !validatorResult.Success)
        {
            return;
        }
        var result = await CoreConfigHandler.GenerateClientConfig(context, fileName);
        if (result.Success != true)
        {
            NoticeManager.Instance.Enqueue(result.Msg);
        }
        else
        {
            NoticeManager.Instance.SendMessageAndEnqueue(string.Format(ResUI.SaveClientConfigurationIn, fileName));
        }
    }

    public async Task Export2ShareUrlAsync(bool blEncode)
    {
        var lstSelected = await GetProfileItems(true);
        if (lstSelected == null || await ClosedByPartnerAsync(lstSelected))
        {
            return;
        }

        StringBuilder sb = new();
        foreach (var it in lstSelected)
        {
            var url = FmtHandler.GetShareUri(it);
            if (url.IsNullOrEmpty())
            {
                continue;
            }
            sb.Append(url);
            sb.AppendLine();
        }
        if (sb.Length > 0)
        {
            if (blEncode)
            {
                await SetClipboardDataInteraction.HandleSafe(Utils.Base64Encode(sb.ToString()));
            }
            else
            {
                await SetClipboardDataInteraction.HandleSafe(sb.ToString());
            }
            NoticeManager.Instance.SendMessage(ResUI.BatchExportURLSuccessfully);
        }
    }

    public async Task Export2InnerUrlAsync()
    {
        var lstSelected = await GetProfileItems(true);
        if (lstSelected == null || await ClosedByPartnerAsync(lstSelected))
        {
            return;
        }

        var result = string.Empty;

        await Task.Run(() =>
        {
            result = InnerFmt.ToUri(lstSelected);
        });

        if (!result.IsNullOrEmpty())
        {
            await SetClipboardDataInteraction.HandleSafe(result);
            NoticeManager.Instance.SendMessage(ResUI.BatchExportURLSuccessfully);
        }
        else
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
        }
    }

    #endregion Add Servers

    #region Subscription

    private async Task EditSubAsync(bool blNew)
    {
        SubItem item;
        if (blNew)
        {
            item = new();
        }
        else
        {
            item = await AppManager.Instance.GetSubItem(_config.SubIndexId);
            if (item is null)
            {
                return;
            }
        }
        var subEditViewModel = new SubEditViewModel(item);
        if (await AppManager.Instance.WindowDialog.ShowDialogAsync(subEditViewModel) == true)
        {
            await RefreshSubscriptions();
            await SubSelectedChangedAsync();
        }
    }

    private async Task DeleteSubAsync()
    {
        var item = await AppManager.Instance.GetSubItem(_config.SubIndexId);
        if (item is null)
        {
            return;
        }

        if (await ShowYesNoInteraction.HandleSafe(ResUI.RemoveServer) == false)
        {
            return;
        }
        await ConfigHandler.DeleteSubItem(_config, item.Id);

        await RefreshSubscriptions();
        await SubSelectedChangedAsync();
    }

    #endregion Subscription
}
