using Avalonia.Automation;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.VisualTree;
using v2rayN.Desktop.Base;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>
/// Rule sets and the programs on their own server, as a list beside the chosen entry. A set's rules are
/// edited right here (v2rayN opened a window for the set and another one for each rule), and every change
/// is saved at once. Closing the window tells the main window to reload the core when anything changed.
/// </summary>
public partial class RoutingSettingWindow : WindowBase<RoutingSettingViewModel>
{
    private enum Pane { Set, Pin, Match }

    private static Config AppConfig => AppManager.Instance.Config;

    private Pane _pane = Pane.Set;
    private string? _setId;
    private string? _pinId;
    private bool _syncingLists;

    // The set on the right and the rule open under its list.
    private RoutingRuleSettingViewModel? _set;
    private readonly List<IDisposable> _setHandlers = [];
    private RoutingRuleDetailsViewModel? _rule;
    private RuleEditorView? _editor;
    private string? _editingId;
    private bool _loadingSetFields;
    private readonly Action<bool> _showSetFields;

    public RoutingSettingWindow()
    {
        InitializeComponent();

        Loaded += Window_Loaded;
        Closing += RoutingSettingWindow_Closing;
        KeyDown += RoutingSettingWindow_KeyDown;

        cmbdomainStrategy.ItemsSource = Global.DomainStrategies;
        cmbdomainStrategy4Singbox.ItemsSource = Global.DomainStrategies4Sbox;
        cmbSetDomainStrategy.ItemsSource = Global.DomainStrategies.AppendEmpty();
        cmbSetDomainStrategySb.ItemsSource = Global.DomainStrategies4Sbox;

        lstRoutings.SelectionChanged += (_, _) => OnListPicked(lstRoutings);
        lstPins.SelectionChanged += (_, _) => OnListPicked(lstPins);
        lstMatch.SelectionChanged += (_, _) => OnListPicked(lstMatch);
        lstRoutings.KeyDown += LstRoutings_KeyDown;

        btnNewSet.Click += async (_, _) => await NewSetAsync();
        btnUse.Click += async (_, _) => await UseSetAsync();
        btnAddRule.Click += (_, _) => OpenEditor(null);
        btnAddFirstRule.Click += (_, _) => OpenEditor(null);
        btnRuleSave.Click += (_, _) => _rule?.SaveCmd.Execute().Subscribe();
        btnRuleCancel.Click += (_, _) => CloseEditor();

        menuDuplicate.Click += async (_, _) => await DuplicateSetAsync();
        menuCopyRules.Click += async (_, _) => await CopyAsync(_set?.ExportRules());
        menuImportFile.Click += (_, _) => _set?.ImportRulesFromFileCmd.Execute().Subscribe();
        menuImportClipboard.Click += (_, _) => _set?.ImportRulesFromClipboardCmd.Execute().Subscribe();
        menuImportUrl.Click += (_, _) => _set?.ImportRulesFromUrlCmd.Execute().Subscribe();
        menuDeleteSet.Click += async (_, _) => await DeleteSetAsync();

        txtSetName.LostFocus += async (_, _) => await RenameSetAsync();
        txtSetName.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                if (e.Key == Key.Escape)
                {
                    txtSetName.Text = RoutingPresets.DisplayName(_set?.SelectedRouting.Remarks);
                }
                e.Handled = true;
                lstRoutings.Focus();
            }
        };
        txtUrl.LostFocus += async (_, _) => await SaveSetFieldsAsync();
        txtSort.LostFocus += async (_, _) => await SaveSetFieldsAsync();
        txtRulesetPath.LostFocus += async (_, _) => await SaveSetFieldsAsync();
        cmbSetDomainStrategy.SelectionChanged += async (_, _) => await SaveSetFieldsAsync();
        cmbSetDomainStrategySb.SelectionChanged += async (_, _) => await SaveSetFieldsAsync();
        btnBrowseRuleset.Click += async (_, _) =>
        {
            var fileName = await UI.OpenFileDialog(null);
            if (fileName.IsNotEmpty())
            {
                txtRulesetPath.Text = fileName;
                await SaveSetFieldsAsync();
            }
        };
        _showSetFields = SettingsKit.InstallMoreFields(boxSetFields, btnSetFields, HasSetExtras);

        btnPinClaude.Click += async (_, _) => await AddPinAsync(V2pnPins.NewClaude());
        btnPinAdd.Click += async (_, _) => await AddPinAsync(new V2pnPin { Id = Utils.GetGuid(false) });

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.RoutingItems, v => v.lstRoutings.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.DomainStrategy, v => v.cmbdomainStrategy.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.DomainStrategy4Singbox, v => v.cmbdomainStrategy4Singbox.SelectedValue).DisposeWith(disposables);

            // The list is rebuilt after every change; the chosen set stays chosen.
            var items = ViewModel!.RoutingItems;
            System.Collections.Specialized.NotifyCollectionChangedEventHandler changed = (_, _) => Dispatcher.UIThread.Post(KeepSetChosen);
            items.CollectionChanged += changed;
            new ActionDisposable(() => items.CollectionChanged -= changed).DisposeWith(disposables);
        });
    }

    private void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        _ = RenderPinsAsync();
        KeepSetChosen();
    }

    #region The list

    // Three lists share one choice: picking in one clears the others.
    private void OnListPicked(ListBox list)
    {
        if (_syncingLists || list.SelectedItem == null)
        {
            return;
        }
        _syncingLists = true;
        foreach (var other in new[] { lstRoutings, lstPins, lstMatch }.Where(l => l != list))
        {
            other.SelectedItem = null;
        }
        _syncingLists = false;

        if (list == lstRoutings && lstRoutings.SelectedItem is RoutingItemModel set)
        {
            if (_pane != Pane.Set || set.Id != _setId || _set == null)
            {
                _ = ShowSetAsync(set.Id);
            }
        }
        else if (list == lstPins && lstPins.SelectedItem is ListBoxItem { Tag: V2pnPin pin })
        {
            ShowPin(pin);
        }
        else if (list == lstMatch)
        {
            ShowPane(Pane.Match);
        }
    }

    private void ShowPane(Pane pane)
    {
        _pane = pane;
        paneSet.IsVisible = pane == Pane.Set;
        panePin.IsVisible = pane == Pane.Pin;
        paneMatch.IsVisible = pane == Pane.Match;
        paneScroll.Offset = default;
    }

    private void KeepSetChosen()
    {
        var items = ViewModel?.RoutingItems;
        if (items == null || items.Count == 0 || _pane != Pane.Set)
        {
            return;
        }
        var chosen = items.FirstOrDefault(t => t.Id == _setId) ?? items.FirstOrDefault(t => t.IsActive) ?? items[0];
        _syncingLists = true;
        lstRoutings.SelectedItem = chosen;
        _syncingLists = false;
        if (chosen.Id != _setId || _set == null)
        {
            _ = ShowSetAsync(chosen.Id);
        }
        else
        {
            UpdateSetHeader();
        }
    }

    private async void LstRoutings_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Delete or Key.Back)
        {
            e.Handled = true;
            await DeleteSetAsync();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await UseSetAsync();
        }
    }

    #endregion The list

    #region One set

    private async Task ShowSetAsync(string id)
    {
        var item = await AppManager.Instance.GetRoutingItem(id);
        if (item is null)
        {
            return;
        }
        CloseEditor();
        DropSetHandlers();
        _setId = id;
        _set = new RoutingRuleSettingViewModel(item) { SaveOnChange = true };
        HookSet(_set);
        ShowPane(Pane.Set);

        _loadingSetFields = true;
        txtSetName.Text = RoutingPresets.DisplayName(item.Remarks);
        txtUrl.Text = item.Url;
        txtSort.Text = item.Sort.ToString();
        cmbSetDomainStrategy.SelectedValue = item.DomainStrategy ?? string.Empty;
        cmbSetDomainStrategySb.SelectedValue = item.DomainStrategy4Singbox;
        txtRulesetPath.Text = item.CustomRulesetPath4Singbox;
        _loadingSetFields = false;
        _showSetFields(HasSetExtras());

        UpdateSetHeader();
        RenderRules();
    }

    private void HookSet(RoutingRuleSettingViewModel set)
    {
        // Only imports ask (append or replace); the rest asks in the window's own words.
        set.ShowYesNoInteraction.RegisterHandler(async interaction =>
        {
            interaction.SetOutput(await UI.ShowYesNo(interaction.Input) == ButtonResult.Yes);
        }).AddTo(_setHandlers);
        set.SetClipboardDataInteraction.RegisterHandler(async interaction =>
        {
            await AvaUtils.SetClipboardData(this, interaction.Input);
            interaction.SetOutput(RxVoid.Default);
        }).AddTo(_setHandlers);
        set.ReadTextFromClipboardInteraction.RegisterHandler(async interaction =>
        {
            interaction.SetOutput(await AvaUtils.GetClipboardData(this));
        }).AddTo(_setHandlers);
        set.BrowseRulesFileInteraction.RegisterHandler(async interaction =>
        {
            interaction.SetOutput(await UI.OpenFileDialog(null));
        }).AddTo(_setHandlers);

        EventHandler saved = async (_, _) =>
        {
            RenderRules();
            UpdateSetHeader();
            if (ViewModel != null)
            {
                ViewModel.IsModified = true;
                await ViewModel.RefreshRoutingItems();
            }
        };
        set.Saved += saved;
        new ActionDisposable(() => set.Saved -= saved).AddTo(_setHandlers);
    }

    private void DropSetHandlers()
    {
        _setHandlers.ForEach(h => h.Dispose());
        _setHandlers.Clear();
    }

    private void UpdateSetHeader()
    {
        if (_set == null)
        {
            return;
        }
        var active = ViewModel?.RoutingItems.FirstOrDefault(t => t.Id == _setId)?.IsActive == true;
        pillInUse.IsVisible = active;
        btnUse.IsVisible = !active;
        menuDeleteSet.IsEnabled = !active;
        ToolTip.SetTip(menuDeleteSet, active ? L.RtActiveCantDelete : null);

        var summary = string.Format(L.RtRuleCount, _set.Rules.Count);
        if (_set.SelectedRouting.Url.IsNotEmpty())
        {
            summary += " · " + string.Format(L.RtLinkNote, _set.SelectedRouting.Url);
        }
        txtSetSummary.Text = summary;
    }

    // AsIs is what v2rayN writes into its own sets, so only another choice counts as set.
    private bool HasSetExtras() =>
        SettingsKit.AnySet(txtUrl.Text, cmbSetDomainStrategySb.SelectedValue as string, txtRulesetPath.Text, txtSort.Text,
            cmbSetDomainStrategy.SelectedValue is string s && s.IsNotEmpty() && s != Global.DomainStrategies.First());

    private async Task RenameSetAsync()
    {
        if (_set == null)
        {
            return;
        }
        var name = txtSetName.Text?.Trim() ?? string.Empty;
        var current = _set.SelectedRouting.Remarks;
        if (name.IsNullOrEmpty())
        {
            txtSetName.Text = RoutingPresets.DisplayName(current);
            return;
        }
        // A built-in set keeps its original name until it is really renamed.
        if (name == RoutingPresets.DisplayName(current))
        {
            return;
        }
        _set.SelectedRouting.Remarks = name;
        await _set.SaveInPlaceAsync();
    }

    private async Task SaveSetFieldsAsync()
    {
        if (_set == null || _loadingSetFields)
        {
            return;
        }
        var item = _set.SelectedRouting;
        var url = txtUrl.Text?.Trim();
        var sort = int.TryParse(txtSort.Text?.Trim(), out var n) ? n : item.Sort;
        var xray = cmbSetDomainStrategy.SelectedValue as string;
        var singbox = cmbSetDomainStrategySb.SelectedValue as string;
        var ruleset = txtRulesetPath.Text?.Trim();
        if (item.Url == url && item.Sort == sort && item.DomainStrategy == xray
            && item.DomainStrategy4Singbox == singbox && item.CustomRulesetPath4Singbox == ruleset)
        {
            return;
        }
        item.Url = url;
        item.Sort = sort;
        item.DomainStrategy = xray;
        item.DomainStrategy4Singbox = singbox;
        item.CustomRulesetPath4Singbox = ruleset;
        await _set.SaveInPlaceAsync();
    }

    private async Task NewSetAsync()
    {
        if (ViewModel == null)
        {
            return;
        }
        var id = await ViewModel.CreateRoutingAsync(L.RtNewSet);
        if (id == null)
        {
            return;
        }
        _setId = id;
        _pane = Pane.Set;
        KeepSetChosen();
        await ShowSetAsync(id);
        txtSetName.Focus();
        txtSetName.SelectAll();
    }

    private async Task UseSetAsync()
    {
        if (ViewModel != null && _setId != null && _pane == Pane.Set)
        {
            await ViewModel.UseRoutingAsync(_setId);
        }
    }

    private async Task DuplicateSetAsync()
    {
        if (ViewModel == null || _set == null || _setId == null)
        {
            return;
        }
        var name = string.Format(L.RtCopyName, RoutingPresets.DisplayName(_set.SelectedRouting.Remarks));
        var id = await ViewModel.DuplicateRoutingAsync(_setId, name);
        if (id != null)
        {
            _setId = id;
            KeepSetChosen();
        }
    }

    private async Task DeleteSetAsync()
    {
        if (ViewModel == null || _set == null || _setId == null || _pane != Pane.Set)
        {
            return;
        }
        if (ViewModel.RoutingItems.FirstOrDefault(t => t.Id == _setId)?.IsActive == true)
        {
            NoticeManager.Instance.Enqueue(L.RtActiveCantDelete);
            return;
        }
        var name = RoutingPresets.DisplayName(_set.SelectedRouting.Remarks);
        if (await UI.ShowYesNo(string.Format(L.RtDeleteSetAsk, name), L.RtDelete, danger: true) != ButtonResult.Yes)
        {
            return;
        }
        var id = _setId;
        _setId = null;
        _set = null;
        await ViewModel.RemoveRoutingAsync(id);
    }

    private async Task CopyAsync(string? text)
    {
        if (text.IsNullOrEmpty())
        {
            return;
        }
        await AvaUtils.SetClipboardData(this, text);
        NoticeManager.Instance.Enqueue(L.RtCopied);
    }

    #endregion One set

    #region Rules

    private void RenderRules()
    {
        panelRules.Children.Clear();
        var rules = _set?.Rules ?? [];
        for (var i = 0; i < rules.Count; i++)
        {
            var row = RuleRow(rules[i], i, rules.Count);
            row.Classes.Set("first", i == 0);
            panelRules.Children.Add(row);
        }
        panelNoRules.IsVisible = rules.Count == 0;
    }

    private Border RuleRow(RulesItem rule, int index, int count)
    {
        var (actionText, actionClass) = rule.OutboundTag switch
        {
            Global.ProxyTag => (L.RtThroughVpn, "vpn"),
            Global.DirectTag => (L.RtDirect, "direct"),
            Global.BlockTag => (L.RtBlock, "block"),
            var tag => (tag ?? string.Empty, "other"),
        };
        var action = new Border { Classes = { "action", actionClass }, Child = new TextBlock { Text = actionText } };
        ToolTip.SetTip(action, actionText);

        var (title, detail) = Describe(rule);
        var text = new StackPanel { Margin = new Thickness(12, 0), VerticalAlignment = VerticalAlignment.Center };
        // A dock, not a horizontal stack: a stack gives the title all the width it wants and it never trims.
        var titleLine = new DockPanel();
        if (!rule.Enabled)
        {
            var off = new TextBlock { Classes = { "srowhint" }, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Text = L.RtRuleOff };
            DockPanel.SetDock(off, Dock.Right);
            titleLine.Children.Add(off);
        }
        titleLine.Children.Add(new TextBlock { Classes = { "srowtitle" }, Text = title, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(titleLine);
        if (detail.IsNotEmpty())
        {
            text.Children.Add(new TextBlock { Classes = { "mono" }, Text = detail });
        }
        Grid.SetColumn(text, 1);

        var edit = RowButton("SemiIconEditStroked", $"{L.RtEdit}: {title}");
        edit.Click += (_, _) => OpenEditor(rule.Id);
        var more = RowButton("SemiIconMore", $"{L.RtRuleMenu}: {title}");
        more.Flyout = RuleMenu(rule, index, count, title);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { edit, more } };
        Grid.SetColumn(actions, 2);

        var row = new Border
        {
            Classes = { "srow", "rule" },
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { action, text, actions } },
        };
        row.Classes.Set("off", !rule.Enabled);
        row.Classes.Set("editing", _editor != null && rule.Id == _editingId);
        row.Tapped += (_, e) =>
        {
            // The row's buttons have their own jobs.
            if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) != null)
            {
                return;
            }
            OpenEditor(rule.Id);
        };
        return row;
    }

    private MenuFlyout RuleMenu(RulesItem rule, int index, int count, string title)
    {
        MenuItem Item(string header, Func<Task> act, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += async (_, _) => await act();
            return item;
        }
        var id = rule.Id ?? string.Empty;
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        menu.Items.Add(Item(L.RtMoveTop, () => MoveAsync(id, EMove.Top), index > 0));
        menu.Items.Add(Item(L.RtMoveUp, () => MoveAsync(id, EMove.Up), index > 0));
        menu.Items.Add(Item(L.RtMoveDown, () => MoveAsync(id, EMove.Down), index < count - 1));
        menu.Items.Add(Item(L.RtMoveBottom, () => MoveAsync(id, EMove.Bottom), index < count - 1));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(rule.Enabled ? L.RtTurnOff : L.RtTurnOn, async () =>
        {
            if (_set != null)
            {
                await _set.SetRuleEnabledAsync(id, !rule.Enabled);
            }
        }));
        menu.Items.Add(Item(L.RtCopyRule, () => CopyAsync(_set?.ExportRules(id))));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(L.RtDelete, async () =>
        {
            if (_set != null && await UI.ShowYesNo(string.Format(L.RtDeleteRuleAsk, title), L.RtDelete, danger: true) == ButtonResult.Yes)
            {
                if (_editingId == id)
                {
                    CloseEditor();
                }
                await _set.RemoveRuleAsync(id);
            }
        }));
        return menu;
    }

    private async Task MoveAsync(string id, EMove move)
    {
        if (_set != null)
        {
            await _set.MoveRuleAsync(id, move);
        }
    }

    // "Russian sites" or, without a name, what the rule catches; the full list goes on the second line.
    private static (string Title, string Detail) Describe(RulesItem rule)
    {
        static List<string> Live(List<string>? items) =>
            (items ?? []).Select(i => i.Trim()).Where(i => i.Length > 0 && !i.StartsWith('#')).ToList();

        var targets = Live(rule.Domain).Concat(Live(rule.Ip)).Concat(Live(rule.Process)).ToList();
        var extras = new List<string>();
        if (rule.Port.IsNotEmpty())
        {
            extras.Add(string.Format(L.RtPortsShort, rule.Port));
        }
        if (rule.Network.IsNotEmpty())
        {
            extras.Add(rule.Network!);
        }
        extras.AddRange(Live(rule.Protocol));
        extras.AddRange(Live(rule.InboundTag).Select(t => "in: " + t));

        var detail = string.Join(" · ", new[] { string.Join(", ", targets) }.Concat(extras).Where(s => s.Length > 0));
        if (rule.Remarks.IsNotEmpty())
        {
            return (rule.Remarks!, detail);
        }
        if (targets.Count > 0)
        {
            // A long list: its first entry and how many more as the title, the whole list below.
            return targets.Count <= 2
                ? (string.Join(", ", targets), string.Join(" · ", extras))
                : ($"{targets[0]} +{targets.Count - 1}", detail);
        }
        // A catch-all (all ports, nothing else) reads as what it is.
        if (rule.Port is "0-65535" or "1-65535" && extras.Count == 1)
        {
            return (L.RtAnything, detail);
        }
        return (extras.Count > 0 ? string.Join(" · ", extras) : L.RtAnything, string.Empty);
    }

    private void OpenEditor(string? id)
    {
        if (_set == null)
        {
            return;
        }
        CloseEditor();
        _editingId = id;
        _rule = _set.BeginRuleEdit(id);
        _rule.RequestClose += Rule_RequestClose;
        _editor = new RuleEditorView { ViewModel = _rule };
        editorHost.Content = _editor;
        var current = id == null ? null : _set.Rules.FirstOrDefault(r => r.Id == id);
        txtEditorTitle.Text = current == null ? L.RtNewRule : $"{L.RtRuleTitle}: {Describe(current).Title}";
        panelEditor.IsVisible = true;
        RenderRules();
        Dispatcher.UIThread.Post(() =>
        {
            panelEditor.BringIntoView();
            _editor?.FocusFirstField();
        }, DispatcherPriority.Background);
    }

    private async void Rule_RequestClose(object? sender, EventArgs e)
    {
        if (_set == null || _rule == null)
        {
            return;
        }
        var item = _rule.SelectedSource;
        CloseEditor();
        await _set.CommitRuleEditAsync(item);
        // Show the rule where it landed (a new one goes on top).
        var index = _set.Rules.ToList().FindIndex(r => r.Id == item.Id);
        if (index >= 0 && index < panelRules.Children.Count)
        {
            var row = panelRules.Children[index];
            Dispatcher.UIThread.Post(() => row.BringIntoView(), DispatcherPriority.Background);
        }
    }

    private void CloseEditor()
    {
        if (_rule != null)
        {
            _rule.RequestClose -= Rule_RequestClose;
        }
        _rule = null;
        _editor = null;
        _editingId = null;
        editorHost.Content = null;
        panelEditor.IsVisible = false;
        if (_set != null)
        {
            RenderRules();
        }
    }

    #endregion Rules

    #region Window

    private bool _closed;

    private void RoutingSettingWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_closed)
        {
            return;
        }
        DropSetHandlers();
        // Everything is saved already; the caller only needs to know that something changed.
        if (ViewModel?.IsModified == true)
        {
            e.Cancel = true;
            _closed = true;
            Close(true);
        }
    }

    private void RoutingSettingWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _editor != null)
        {
            e.Handled = true;
            CloseEditor();
        }
    }

    private void linkdomainStrategy_Click(object? sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart("https://xtls.github.io/config/routing.html");
    }

    private void linkdomainStrategy4Singbox_Click(object? sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart("https://sing-box.sagernet.org/zh/configuration/route/rule_action/#strategy");
    }

    private void linkCustomRulesetPath4Singbox_Click(object? sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart("https://github.com/2dust/v2rayCustomRoutingList/blob/master/singbox_custom_ruleset_example.json");
    }

    private static Button RowButton(string icon, string name)
    {
        var glyph = new PathIcon { Classes = { "i14" } };
        glyph.Bind(PathIcon.DataProperty, glyph.GetResourceObservable(icon));
        var button = new Button { Classes = { "ghost", "small", "icon" }, Content = glyph };
        button.Bind(StyledElement.ThemeProperty, button.GetResourceObservable("V2Button"));
        ToolTip.SetTip(button, name);
        AutomationProperties.SetName(button, name);
        return button;
    }

    #endregion Window

    #region Programs on their own server

    private int _pinRender;

    private async Task RenderPinsAsync()
    {
        var render = ++_pinRender;
        var pins = V2pnPins.All(AppConfig).ToList();
        var items = new List<ListBoxItem>();
        foreach (var pin in pins)
        {
            var dot = new Ellipse { Classes = { "setdot" } };
            dot.Classes.Set(pin.Enabled ? "on" : "off", true);
            var label = new TextBlock
            {
                Text = V2pnPins.Label(pin),
                Margin = new Thickness(10, 9),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(label, 1);
            items.Add(new ListBoxItem
            {
                Tag = pin,
                Content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { dot, label } },
            });
        }
        await Task.CompletedTask;
        if (render != _pinRender)
        {
            return;
        }
        _syncingLists = true;
        lstPins.ItemsSource = items;
        _syncingLists = false;
        btnPinClaude.IsVisible = !pins.Any(V2pnPins.IsClaude);

        if (_pane == Pane.Pin)
        {
            var again = items.FirstOrDefault(i => (i.Tag as V2pnPin)?.Id == _pinId);
            if (again == null)
            {
                _pane = Pane.Set;
                KeepSetChosen();
            }
            else
            {
                lstPins.SelectedItem = again;
                ShowPin((V2pnPin)again.Tag!);
            }
        }
    }

    private async void ShowPin(V2pnPin pin)
    {
        CloseEditor();
        _pinId = pin.Id;
        ShowPane(Pane.Pin);
        var server = await V2pnPins.FindServerAsync(pin);
        var label = V2pnPins.Label(pin);

        panePin.Children.Clear();
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var glyph = new PathIcon();
        glyph.Bind(PathIcon.DataProperty, glyph.GetResourceObservable("SemiIconDesktop"));
        head.Children.Add(new Border { Classes = { "srowicon" }, Child = glyph });
        var title = new TextBlock { Classes = { "spagetitle" }, FontSize = 20, Text = label, Margin = new Thickness(12, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(title, 1);
        head.Children.Add(title);
        var toggle = new ToggleSwitch { IsChecked = pin.Enabled, OnContent = string.Empty, OffContent = string.Empty, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, $"{L.PinOn}: {label}");
        toggle.IsCheckedChanged += async (_, _) =>
        {
            pin.Enabled = toggle.IsChecked == true;
            await SavePinsAsync();
        };
        Grid.SetColumn(toggle, 2);
        head.Children.Add(toggle);
        panePin.Children.Add(head);
        panePin.Children.Add(new TextBlock { Classes = { "spagehint" }, Margin = new Thickness(0, 10, 0, 0), Text = L.PinHint });

        var card = new StackPanel();
        card.Children.Add(InfoRow(L.PinServer, server is null
            ? (pin.ServerId.IsNullOrEmpty() && pin.ServerName.IsNullOrEmpty() ? L.PinNoServer : string.Format(L.PinServerGone, pin.ServerName))
            : PinEditWindow.Chosen(server), warn: server is null));
        var programs = string.Join(", ", (pin.Programs ?? []).Where(p => p.Trim().Length > 0 && !p.TrimStart().StartsWith('#')));
        var sites = string.Join(", ", (pin.Sites ?? []).Where(p => p.Trim().Length > 0 && !p.TrimStart().StartsWith('#')));
        if (programs.IsNotEmpty())
        {
            card.Children.Add(InfoRow(L.RtPrograms, programs));
        }
        if (sites.IsNotEmpty())
        {
            card.Children.Add(InfoRow(L.RtSites, sites));
        }
        ((Border)card.Children[0]).Classes.Add("first");
        panePin.Children.Add(new Border { Classes = { "sgroupcard" }, Margin = new Thickness(0, 20, 0, 0), Child = card });

        var edit = new Button { Classes = { "primary" }, Content = L.RtEdit, MinWidth = 100 };
        edit.Bind(StyledElement.ThemeProperty, edit.GetResourceObservable("V2Button"));
        ToolTip.SetTip(edit, L.RtPinEditHint);
        edit.Click += async (_, _) => await EditPinAsync(pin);
        var delete = new Button { Classes = { "ghost" }, Content = L.RtDelete };
        delete.Bind(StyledElement.ThemeProperty, delete.GetResourceObservable("V2Button"));
        delete.Click += async (_, _) => await DeletePinAsync(pin);
        panePin.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 16, 0, 0), Children = { edit, delete } });
    }

    private static Border InfoRow(string title, string value, bool warn = false)
    {
        var text = new StackPanel { Classes = { "srowtext" } };
        text.Children.Add(new TextBlock { Classes = { "srowtitle" }, Text = title });
        var hint = new TextBlock { Classes = { "srowhint" }, Text = value };
        hint.Classes.Set("warn", warn);
        text.Children.Add(hint);
        return new Border { Classes = { "srow" }, Child = text };
    }

    private async Task<bool> EditInDialogAsync(V2pnPin pin) =>
        await new PinEditWindow { ViewModel = new PinEditModel(pin) }.ShowDialog<bool>(this);

    private async Task AddPinAsync(V2pnPin pin)
    {
        if (!await EditInDialogAsync(pin))
        {
            return;
        }
        (AppConfig.RoutingBasicItem.V2pnPins ??= []).Add(pin);
        _pinId = pin.Id;
        _pane = Pane.Pin;
        await SavePinsAsync();
    }

    private async Task EditPinAsync(V2pnPin pin)
    {
        // The dialog writes into a copy, so Cancel leaves the entry as it was.
        var copy = JsonUtils.DeepCopy(pin);
        if (copy is null || !await EditInDialogAsync(copy))
        {
            return;
        }
        var pins = V2pnPins.All(AppConfig);
        var index = pins.FindIndex(p => ReferenceEquals(p, pin));
        if (index < 0)
        {
            return;
        }
        pins[index] = copy;
        await SavePinsAsync();
    }

    private async Task DeletePinAsync(V2pnPin pin)
    {
        if (await UI.ShowYesNo(string.Format(L.PinDeleteAsk, V2pnPins.Label(pin)), L.RtDelete, danger: true) != ButtonResult.Yes)
        {
            return;
        }
        V2pnPins.All(AppConfig).Remove(pin);
        await SavePinsAsync();
    }

    // Saved at once; closing the window then reloads the core (IsModified), as for the sets.
    private async Task SavePinsAsync()
    {
        await ConfigHandler.SaveConfig(AppConfig);
        if (ViewModel != null)
        {
            ViewModel.IsModified = true;
        }
        await RenderPinsAsync();
    }

    #endregion Programs on their own server
}

internal static class DisposableListExtensions
{
    public static void AddTo(this IDisposable disposable, List<IDisposable> list) => list.Add(disposable);
}
