using Avalonia.Automation;
using Avalonia.Layout;
using v2rayN.Desktop.Base;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class RoutingSettingWindow : WindowBase<RoutingSettingViewModel>
{
    // Order and link: shown only with the extra fields.
    // Order and link: name, rules, order, link (the in-use mark is part of the name column).
    private static readonly int[] AdvancedColumns = [2, 3];

    public RoutingSettingWindow()
    {
        InitializeComponent();

        Loaded += Window_Loaded;
        Closing += RoutingSettingWindow_Closing;
        KeyDown += RoutingSettingWindow_KeyDown;
        lstRoutings.SelectionChanged += lstRoutings_SelectionChanged;
        lstRoutings.DoubleTapped += LstRoutings_DoubleTapped;
        menuRoutingAdvancedSelectAll.Click += menuRoutingAdvancedSelectAll_Click;
        menuRoutingEdit.Click += (_, _) => ViewModel?.RoutingAdvancedEditAsync(false);
        menuRoutingEdit2.Click += (_, _) => ViewModel?.RoutingAdvancedEditAsync(false);

        cmbdomainStrategy.ItemsSource = Global.DomainStrategies;
        cmbdomainStrategy4Singbox.ItemsSource = Global.DomainStrategies4Sbox;

        rowDomainStrategy.Classes.CollectionChanged += (_, _) => ShowAdvancedColumns(!rowDomainStrategy.Classes.Contains("advhidden"));
        SettingsKit.InstallMoreFields(this, btnMoreFields, HasExtras);

        btnPinClaude.Click += async (_, _) => await AddPinAsync(V2pnPins.NewClaude());
        btnPinAdd.Click += async (_, _) => await AddPinAsync(new V2pnPin { Id = Utils.GetGuid(false) });

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.RoutingItems, v => v.lstRoutings.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSource, v => v.lstRoutings.SelectedItem).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.DomainStrategy, v => v.cmbdomainStrategy.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.DomainStrategy4Singbox, v => v.cmbdomainStrategy4Singbox.SelectedValue).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.RoutingAdvancedAddCmd, v => v.menuRoutingAdvancedAdd).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RoutingAdvancedAddCmd, v => v.menuRoutingAdvancedAdd2).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RoutingAdvancedRemoveCmd, v => v.menuRoutingAdvancedRemove).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RoutingAdvancedSetDefaultCmd, v => v.menuRoutingAdvancedSetDefault).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RoutingAdvancedSetDefaultCmd, v => v.menuRoutingUse2).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RoutingAdvancedImportRulesCmd, v => v.menuRoutingAdvancedImportRules).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RoutingAdvancedImportRulesCmd, v => v.menuRoutingAdvancedImportRules2).DisposeWith(disposables);

            ViewModel.ShowYesNoInteraction.RegisterHandler(async interaction =>
            {
                var message = interaction.Input;
                var result = await UI.ShowYesNo(message);
                interaction.SetOutput(result == ButtonResult.Yes);
            }).DisposeWith(disposables);
        });
    }

    // AsIs is the default strategy, so only another choice counts as set.
    private bool HasExtras() =>
        SettingsKit.AnySet(cmbdomainStrategy.SelectedValue is string s && s.IsNotEmpty() && s != Global.DomainStrategies.First(),
            cmbdomainStrategy4Singbox.SelectedValue as string);

    private void ShowAdvancedColumns(bool show)
    {
        foreach (var index in AdvancedColumns.Where(i => i < lstRoutings.Columns.Count))
        {
            lstRoutings.Columns[index].IsVisible = show;
        }
    }

    private bool _closed = false;

    private void RoutingSettingWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_closed)
        {
            return;
        }

        // DomainStrategy is auto-saved reactively; just ensure the caller knows changes were made
        if (ViewModel?.IsModified == true)
        {
            e.Cancel = true;
            _closed = true;
            Close(true);
        }
    }

    private void RoutingSettingWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta)
        {
            switch (e.Key)
            {
                case Key.A:
                    lstRoutings.SelectAll();
                    break;
            }
        }
        else
        {
            switch (e.Key)
            {
                case Key.Enter:
                    //case Key.Return:
                    ViewModel?.RoutingAdvancedSetDefault();
                    break;

                case Key.Delete:
                case Key.Back:
                    ViewModel?.RoutingAdvancedRemoveAsync();
                    break;
            }
        }
    }

    private void menuRoutingAdvancedSelectAll_Click(object? sender, RoutedEventArgs e)
    {
        lstRoutings.SelectAll();
    }

    private void lstRoutings_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.SelectedSources = lstRoutings.SelectedItems.Cast<RoutingItemModel>().ToList();
        }
    }

    private void LstRoutings_DoubleTapped(object? sender, TappedEventArgs e)
    {
        ViewModel?.RoutingAdvancedEditAsync(false);
    }

    private void linkdomainStrategy_Click(object? sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart("https://xtls.github.io/config/routing.html");
    }

    private void linkdomainStrategy4Singbox_Click(object? sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart("https://sing-box.sagernet.org/zh/configuration/route/rule_action/#strategy");
    }

    private void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        _ = RenderPinsAsync();
    }

    #region Programs on their own server

    private static Config AppConfig => AppManager.Instance.Config;

    private readonly List<Control> _pinRows = [];
    private int _pinRender;

    private async Task RenderPinsAsync()
    {
        var render = ++_pinRender;
        var pins = V2pnPins.All(AppConfig).ToList();
        var rows = new List<Control>();
        foreach (var pin in pins)
        {
            rows.Add(PinRow(pin, await V2pnPins.FindServerAsync(pin)));
        }
        if (render != _pinRender)
        {
            return;
        }
        if (rows.Count == 0)
        {
            rows.Add(new Border
            {
                Classes = { "srow" },
                Child = new TextBlock { Classes = { "srowhint" }, Text = L.PinEmpty, VerticalAlignment = VerticalAlignment.Center },
            });
        }

        foreach (var row in _pinRows)
        {
            panelPins.Children.Remove(row);
        }
        _pinRows.Clear();
        _pinRows.AddRange(rows);
        for (var i = 0; i < rows.Count; i++)
        {
            panelPins.Children.Insert(i, rows[i]);
        }
        // SettingsKit.MarkFirst may have tagged the buttons row while it was the card's only row.
        rowPinActions.Classes.Set("first", false);
        btnPinClaude.IsVisible = !pins.Any(V2pnPins.IsClaude);
    }

    private Border PinRow(V2pnPin pin, ProfileItem? server)
    {
        var label = V2pnPins.Label(pin);
        var what = What(pin);
        var text = new StackPanel { Classes = { "srowtext" } };
        text.Children.Add(new TextBlock { Classes = { "srowtitle" }, Text = label });
        text.Children.Add(new TextBlock
        {
            Classes = { "srowhint" },
            Text = server is null ? what : $"{what} → {PinEditWindow.Chosen(server)}",
        });
        if (server is null)
        {
            text.Children.Add(new TextBlock
            {
                Classes = { "srowhint", "warn" },
                Text = pin.ServerId.IsNullOrEmpty() && pin.ServerName.IsNullOrEmpty()
                    ? L.PinNoServer
                    : string.Format(L.PinServerGone, pin.ServerName),
            });
        }
        Grid.SetColumn(text, 1);

        var glyph = new PathIcon();
        glyph.Bind(PathIcon.DataProperty, glyph.GetResourceObservable("SemiIconDesktop"));
        var icon = new Border { Classes = { "srowicon" }, Child = glyph };

        var toggle = new ToggleSwitch { IsChecked = pin.Enabled, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(toggle, $"{L.PinOn}: {label}");
        toggle.IsCheckedChanged += async (_, _) =>
        {
            pin.Enabled = toggle.IsChecked == true;
            await SavePinsAsync();
        };
        Grid.SetColumn(toggle, 2);

        var edit = RowButton("SemiIconEditStroked", $"{L.RtEdit}: {label}");
        edit.Click += async (_, _) => await EditPinAsync(pin);
        var delete = RowButton("SemiIconDeleteStroked", $"{L.RtDelete}: {label}");
        delete.Click += async (_, _) => await DeletePinAsync(pin);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { edit, delete } };
        Grid.SetColumn(actions, 3);

        return new Border
        {
            Classes = { "srow" },
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Children = { icon, text, toggle, actions } },
        };
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

    // "claude.exe · claude.ai, anthropic.com +2": enough to recognise the entry in one line.
    private static string What(V2pnPin pin)
    {
        static List<string> Live(List<string>? items) =>
            (items ?? []).Select(i => i.Trim()).Where(i => i.Length > 0 && !i.StartsWith('#')).ToList();
        static string Short(List<string> items, int max) => items.Count <= max
            ? string.Join(", ", items)
            : $"{string.Join(", ", items.Take(max))} +{items.Count - max}";

        var parts = new List<string>();
        var programs = Live(pin.Programs);
        var sites = Live(pin.Sites);
        if (programs.Count > 0)
        {
            parts.Add(Short(programs, 3));
        }
        if (sites.Count > 0)
        {
            parts.Add(Short(sites, 2));
        }
        return string.Join(" · ", parts);
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
