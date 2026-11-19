namespace ServiceLib.ViewModels;

public partial class RoutingRuleSettingViewModel : MyReactiveObject, ICloseable
{
    public event EventHandler? RequestClose;

    public Interaction<string, bool> ShowYesNoInteraction { get; } = new();
    public Interaction<string, RxVoid> SetClipboardDataInteraction { get; } = new();
    public Interaction<RxVoid, string?> ReadTextFromClipboardInteraction { get; } = new();
    public Interaction<RxVoid, string?> BrowseRulesFileInteraction { get; } = new();

    private List<RulesItem> _rules;

    [Reactive]
    public partial RoutingItem SelectedRouting { get; set; }

    public BulkObservableCollection<RulesItemModel> RulesItems { get; } = [];

    [Reactive]
    public partial RulesItemModel SelectedSource { get; set; }

    public IList<RulesItemModel> SelectedSources { get; set; }

    public ReactiveCommand<RxVoid, RxVoid> RuleAddCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> ImportRulesFromFileCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> ImportRulesFromClipboardCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> ImportRulesFromUrlCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> RuleRemoveCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> RuleExportSelectedCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> MoveTopCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> MoveUpCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> MoveDownCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> MoveBottomCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> SaveCmd { get; }

    public RoutingRuleSettingViewModel(RoutingItem routingItem)
    {
        _config = AppManager.Instance.Config;

        var canEditRemove = this.WhenAnyValue(
            x => x.SelectedSource,
            selectedSource => selectedSource != null && !selectedSource.OutboundTag.IsNullOrEmpty());

        RuleAddCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await RuleEditAsync(true);
        });
        ImportRulesFromFileCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            var fileName = await BrowseRulesFileInteraction.HandleSafe(RxVoid.Default);
            await ImportRulesFromFileAsync(fileName);
        });
        ImportRulesFromClipboardCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ImportRulesFromClipboardAsync(null);
        });
        ImportRulesFromUrlCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ImportRulesFromUrl();
        });

        RuleRemoveCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await RuleRemoveAsync();
        }, canEditRemove);
        RuleExportSelectedCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await RuleExportSelectedAsync();
        }, canEditRemove);

        MoveTopCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await MoveRule(EMove.Top);
        }, canEditRemove);
        MoveUpCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await MoveRule(EMove.Up);
        }, canEditRemove);
        MoveDownCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await MoveRule(EMove.Down);
        }, canEditRemove);
        MoveBottomCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await MoveRule(EMove.Bottom);
        }, canEditRemove);

        SaveCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SaveRoutingAsync();
        });

        SelectedSource = new();
        SelectedRouting = routingItem;
        _rules = routingItem.Id.IsNullOrEmpty() ? [] : JsonUtils.Deserialize<List<RulesItem>>(SelectedRouting.RuleSet);

        RefreshRulesItems();
    }

    public void RefreshRulesItems()
    {
        var models = new List<RulesItemModel>();
        foreach (var item in _rules)
        {
            var it = new RulesItemModel()
            {
                Id = item.Id,
                RuleTypeName = item.RuleType?.ToString(),
                OutboundTag = item.OutboundTag,
                Port = item.Port,
                Network = item.Network,
                Protocols = Utils.List2String(item.Protocol),
                InboundTags = Utils.List2String(item.InboundTag),
                Domains = Utils.List2String((item.Domain ?? []).Concat(item.Ip ?? []).ToList().Concat(item.Process ?? []).ToList()),
                Enabled = item.Enabled,
                Remarks = item.Remarks,
            };
            models.Add(it);
        }
        RulesItems.ReplaceRange(models);
    }

    public async Task RuleEditAsync(bool blNew)
    {
        RulesItem? item;
        if (blNew)
        {
            item = new();
        }
        else
        {
            item = _rules.FirstOrDefault(t => t.Id == SelectedSource?.Id);
            if (item is null)
            {
                return;
            }
        }
        var routingRuleDetailsViewModel = new RoutingRuleDetailsViewModel(item);
        if (await AppManager.Instance.WindowDialog.ShowDialogAsync(routingRuleDetailsViewModel) == true)
        {
            if (blNew)
            {
                _rules.Insert(0, item);
            }
            RefreshRulesItems();
        }
    }

    public async Task RuleRemoveAsync()
    {
        if (SelectedSource is null || SelectedSource.OutboundTag.IsNullOrEmpty())
        {
            NoticeManager.Instance.Enqueue(ResUI.PleaseSelectRules);
            return;
        }
        if (await ShowYesNoInteraction.HandleSafe(ResUI.RemoveServer) == false)
        {
            return;
        }
        foreach (var it in SelectedSources ?? [SelectedSource])
        {
            var item = _rules.FirstOrDefault(t => t.Id == it?.Id);
            if (item != null)
            {
                _rules.Remove(item);
            }
        }

        RefreshRulesItems();
    }

    public async Task RuleExportSelectedAsync()
    {
        if (SelectedSource is null || SelectedSource.OutboundTag.IsNullOrEmpty())
        {
            NoticeManager.Instance.Enqueue(ResUI.PleaseSelectRules);
            return;
        }

        var lst = new List<RulesItem>();
        var sources = SelectedSources ?? [SelectedSource];
        foreach (var it in _rules)
        {
            if (sources.Any(t => t.Id == it?.Id))
            {
                var item2 = JsonUtils.DeepCopy(it);
                item2.Id = null;
                lst.Add(item2 ?? new());
            }
        }
        if (lst.Count > 0)
        {
            await SetClipboardDataInteraction.HandleSafe(JsonUtils.SerializeCamelCase(lst));
        }
    }

    public async Task MoveRule(EMove eMove)
    {
        if (SelectedSource is null || SelectedSource.OutboundTag.IsNullOrEmpty())
        {
            NoticeManager.Instance.Enqueue(ResUI.PleaseSelectRules);
            return;
        }

        var item = _rules.FirstOrDefault(t => t.Id == SelectedSource?.Id);
        if (item == null)
        {
            return;
        }
        var index = _rules.IndexOf(item);
        if (await ConfigHandler.MoveRoutingRule(_rules, index, eMove) == 0)
        {
            RefreshRulesItems();
        }
    }

    private async Task SaveRoutingAsync()
    {
        var remarks = SelectedRouting.Remarks;
        if (remarks.IsNullOrEmpty())
        {
            NoticeManager.Instance.Enqueue(ResUI.PleaseFillRemarks);
            return;
        }
        var item = SelectedRouting;
        foreach (var it in _rules)
        {
            it.Id = Utils.GetGuid(false);
        }
        item.RuleNum = _rules.Count;
        item.RuleSet = JsonUtils.Serialize(_rules, false);

        if (await ConfigHandler.SaveRoutingItem(_config, item) == 0)
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
        }
    }

    #region V2pn: editing in place

    // The rule-sets window shows one set beside the list and edits it in place: there is no Save
    // button, every change below is written at once. The dialog flow above stays for v2rayN's WPF views.

    /// <summary>Raised after a change was written, so the list of sets can show the new name and count.</summary>
    public event EventHandler? Saved;

    /// <summary>Imports save at once too (they wait for the Save button otherwise).</summary>
    public bool SaveOnChange { get; set; }

    public IReadOnlyList<RulesItem> Rules => _rules;

    /// <summary>An editor for a copy of the rule, or for a new one when <paramref name="id"/> is empty; Cancel leaves the set as it was.</summary>
    public RoutingRuleDetailsViewModel BeginRuleEdit(string? id)
    {
        var source = id.IsNullOrEmpty() ? null : _rules.FirstOrDefault(t => t.Id == id);
        var copy = source is null ? new RulesItem() : JsonUtils.DeepCopy(source) ?? new RulesItem();
        return new RoutingRuleDetailsViewModel(copy);
    }

    /// <summary>Puts an edited rule back in its place; a new one goes on top, where it wins over the rest.</summary>
    public async Task<bool> CommitRuleEditAsync(RulesItem item)
    {
        var index = _rules.FindIndex(t => t.Id == item.Id);
        if (index >= 0)
        {
            _rules[index] = item;
        }
        else
        {
            _rules.Insert(0, item);
        }
        return await SaveInPlaceAsync();
    }

    public async Task<bool> RemoveRuleAsync(string id)
    {
        _rules.RemoveAll(t => t.Id == id);
        return await SaveInPlaceAsync();
    }

    public async Task<bool> MoveRuleAsync(string id, EMove eMove)
    {
        var index = _rules.FindIndex(t => t.Id == id);
        if (index < 0 || await ConfigHandler.MoveRoutingRule(_rules, index, eMove) != 0)
        {
            return false;
        }
        return await SaveInPlaceAsync();
    }

    public async Task<bool> SetRuleEnabledAsync(string id, bool enabled)
    {
        var rule = _rules.FirstOrDefault(t => t.Id == id);
        if (rule is null || rule.Enabled == enabled)
        {
            return false;
        }
        rule.Enabled = enabled;
        return await SaveInPlaceAsync();
    }

    /// <summary>The rules as JSON for the clipboard, in the format the import reads back.</summary>
    public string ExportRules(string? id = null)
    {
        var list = _rules.Where(t => id.IsNullOrEmpty() || t.Id == id)
            .Select(t =>
            {
                var copy = JsonUtils.DeepCopy(t) ?? new RulesItem();
                copy.Id = null;
                return copy;
            })
            .ToList();
        return JsonUtils.SerializeCamelCase(list);
    }

    public async Task<bool> SaveInPlaceAsync()
    {
        if (SelectedRouting.Remarks.IsNullOrEmpty())
        {
            NoticeManager.Instance.Enqueue(ResUI.PleaseFillRemarks);
            return false;
        }
        // Ids stay as they are, so the rule the user is looking at stays selected; only missing or
        // repeated ones (imports, copies) get new ones.
        var seen = new HashSet<string>();
        foreach (var it in _rules)
        {
            if (it.Id.IsNullOrEmpty() || !seen.Add(it.Id))
            {
                it.Id = Utils.GetGuid(false);
                seen.Add(it.Id);
            }
        }
        SelectedRouting.RuleNum = _rules.Count;
        SelectedRouting.RuleSet = JsonUtils.Serialize(_rules, false);

        var ok = await ConfigHandler.SaveRoutingItem(_config, SelectedRouting) == 0;
        if (!ok)
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
        }
        RefreshRulesItems();
        Saved?.Invoke(this, EventArgs.Empty);
        return ok;
    }

    private async Task AfterImportAsync()
    {
        if (SaveOnChange)
        {
            await SaveInPlaceAsync();
        }
    }

    #endregion V2pn: editing in place

    #region Import rules

    public async Task ImportRulesFromFileAsync(string fileName)
    {
        if (fileName.IsNullOrEmpty())
        {
            return;
        }

        var result = EmbedUtils.LoadResource(fileName);
        if (result.IsNullOrEmpty())
        {
            return;
        }
        var ret = await AddBatchRoutingRulesAsync(SelectedRouting, result);
        if (ret == 0)
        {
            RefreshRulesItems();
            await AfterImportAsync();
            NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
        }
    }

    public async Task ImportRulesFromClipboardAsync(string? clipboardData)
    {
        var stringData = clipboardData;
        if (clipboardData == null)
        {
            var result = await ReadTextFromClipboardInteraction.HandleSafe(RxVoid.Default);
            if (result.IsNullOrEmpty())
            {
                NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
                return;
            }
            stringData = result;
        }
        var ret = await AddBatchRoutingRulesAsync(SelectedRouting, stringData);
        if (ret == 0)
        {
            RefreshRulesItems();
            await AfterImportAsync();
            NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
        }
    }

    private async Task ImportRulesFromUrl()
    {
        var url = SelectedRouting.Url;
        if (url.IsNullOrEmpty())
        {
            NoticeManager.Instance.Enqueue(ResUI.MsgNeedUrl);
            return;
        }

        var downloadHandle = new DownloadService();
        var result = await downloadHandle.TryDownloadString(url, true, "");
        var ret = await AddBatchRoutingRulesAsync(SelectedRouting, result);
        if (ret == 0)
        {
            RefreshRulesItems();
            await AfterImportAsync();
            NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
        }
    }

    private async Task<int> AddBatchRoutingRulesAsync(RoutingItem routingItem, string? clipboardData)
    {
        var blReplace = false;
        if (await ShowYesNoInteraction.HandleSafe(ResUI.AddBatchRoutingRulesYesNo) == false)
        {
            blReplace = true;
        }
        if (clipboardData.IsNullOrEmpty())
        {
            return -1;
        }
        var lstRules = JsonUtils.Deserialize<List<RulesItem>>(clipboardData);
        if (lstRules == null)
        {
            return -1;
        }
        foreach (var rule in lstRules)
        {
            rule.Id = Utils.GetGuid(false);
        }

        if (blReplace)
        {
            _rules = lstRules;
        }
        else
        {
            _rules.AddRange(lstRules);
        }
        return 0;
    }

    #endregion Import rules
}
