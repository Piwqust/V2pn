using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using DialogHostAvalonia;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>
/// V2pn: programs that always go directly, past the server, while VPN mode is on (V2pnPresetRules): a bank's app, a
/// game, a work program. Programs are added from those running now or by their file; each change is applied at once,
/// like the sites (SiteRulesView). The view has two pages: the list, and the running programs to add from.
/// </summary>
public sealed class AppRulesView : UserControl
{
    private readonly Config _config;
    private readonly Func<Task> _applied;
    private readonly Func<bool> _vpnMode;
    private readonly Func<bool> _presetInUse;
    private readonly StackPanel _list = new() { Spacing = 6 };
    private readonly TextBlock _state;
    private readonly TextBlock _problem;
    private readonly Control _listPage;
    private readonly Control _runningPage;
    private readonly StackPanel _running = new() { Spacing = 2 };
    private readonly TextBox _filter;
    private readonly TextBlock _runningState;
    private List<(V2pnRunningProgram Program, Bitmap? Icon)> _programs = [];

    public AppRulesView(Config config, Func<Task> applied, Func<bool> vpnMode, Func<bool> presetInUse)
    {
        _config = config;
        _applied = applied;
        _vpnMode = vpnMode;
        _presetInUse = presetInUse;
        Width = 600;

        _state = new TextBlock { Classes = { "srowhint" }, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkOrange, IsVisible = false };
        _problem = new TextBlock { Classes = { "srowhint" }, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed, IsVisible = false };

        var addRunning = Button(L.AppsAddRunning, accent: true);
        addRunning.Click += async (_, _) => await ShowRunningAsync();
        var chooseFile = Button(L.AppsChooseFile, accent: false);
        chooseFile.Click += async (_, _) => await ChooseFileAsync();
        var close = Button(L.Close, accent: false);
        close.Classes.Add("ghost");
        close.Click += (_, _) => DialogHost.Close(null);

        _listPage = new StackPanel
        {
            Margin = new Thickness(28, 22, 28, 22),
            Spacing = 12,
            Children =
            {
                new TextBlock { Classes = { "spagetitle" }, Text = L.AppsTitle },
                new TextBlock { Classes = { "spagehint" }, Text = L.AppsLead, TextWrapping = TextWrapping.Wrap },
                _state,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { addRunning, chooseFile } },
                _problem,
                new Border { Classes = { "hairline" } },
                new ScrollViewer { MaxHeight = 300, Content = _list },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { close } },
            },
        };

        _filter = new TextBox { PlaceholderText = L.AppsRunningFilter };
        AutomationProperties.SetName(_filter, L.AppsRunningFilter);
        _filter.TextChanged += (_, _) => FillRunning();
        _runningState = new TextBlock { Classes = { "srowhint" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var back = Button(L.Back, accent: false);
        back.Classes.Add("ghost");
        back.Click += (_, _) => ShowList();
        _runningPage = new StackPanel
        {
            Margin = new Thickness(28, 22, 28, 22),
            Spacing = 12,
            Children =
            {
                new TextBlock { Classes = { "spagetitle" }, Text = L.AppsRunningTitle },
                new TextBlock { Classes = { "spagehint" }, Text = L.AppsRunningHint, TextWrapping = TextWrapping.Wrap },
                _filter,
                _runningState,
                new ScrollViewer { MaxHeight = 360, Content = _running },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { back } },
            },
        };

        Content = _listPage;
        Refresh();
    }

    private Button Button(string text, bool accent)
    {
        var button = new Button { Content = text };
        if (accent)
        {
            button.Classes.Add("accent");
        }
        button.Bind(ThemeProperty, button.GetResourceObservable("V2Button"));
        return button;
    }

    private List<V2pnAppRule> Apps => _config.UiItem.V2pnDirectApps ?? [];

    private bool Listed(string process) =>
        Apps.Any(a => V2pnPresetRules.ProgramKey(a.Process) == V2pnPresetRules.ProgramKey(process));

    private async Task AddAsync(string process)
    {
        _config.UiItem.V2pnDirectApps = V2pnPresetRules.With(_config.UiItem.V2pnDirectApps, new V2pnAppRule { Process = process });
        await SaveAsync();
    }

    private async Task RemoveAsync(V2pnAppRule app)
    {
        var key = V2pnPresetRules.ProgramKey(app.Process);
        _config.UiItem.V2pnDirectApps = Apps.Where(a => V2pnPresetRules.ProgramKey(a.Process) != key).ToList();
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        await ConfigTextHandler.SaveConfig();
        await _applied();
        Refresh();
    }

    // Why the list may do nothing now: Proxy mode, a rule set of the person's own, or Xray's tunnel on a Mac.
    private string? StateText()
    {
        if (!_vpnMode())
        {
            return L.AppsVpnOff;
        }
        if (!_presetInUse())
        {
            return L.AppsOwnRules;
        }
        return OperatingSystem.IsMacOS() && !_config.TunModeItem.EnableLegacyProtect ? L.AppsMacXrayTunnel : null;
    }

    private void Refresh()
    {
        var state = Apps.Count > 0 ? StateText() : null;
        _state.Text = state;
        _state.IsVisible = state != null;

        _list.Children.Clear();
        if (Apps.Count == 0)
        {
            _list.Children.Add(new TextBlock { Classes = { "srowhint" }, Text = L.AppsNone, TextWrapping = TextWrapping.Wrap });
            return;
        }
        foreach (var app in Apps.OrderBy(a => a.Process, StringComparer.CurrentCultureIgnoreCase))
        {
            var remove = Button("✕", accent: false);
            remove.Classes.Add("ghost");
            AutomationProperties.SetName(remove, string.Format(L.SitesRemove, app.Process));
            ToolTip.SetTip(remove, string.Format(L.SitesRemove, app.Process));
            remove.Click += async (_, _) => await RemoveAsync(app);
            var icon = Icon(_programs.FirstOrDefault(p => V2pnPresetRules.ProgramKey(p.Program.Process) == V2pnPresetRules.ProgramKey(app.Process)).Icon);
            var name = new TextBlock
            {
                Classes = { "srowtitle" },
                Text = app.Process,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
            Grid.SetColumn(name, 1);
            Grid.SetColumn(remove, 2);
            row.Children.Add(icon);
            row.Children.Add(name);
            row.Children.Add(remove);
            _list.Children.Add(row);
        }
    }

    private static Control Icon(Bitmap? bitmap) => bitmap != null
        ? new Image { Source = bitmap, Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center }
        : new PathIcon
        {
            Data = Application.Current?.FindResource("SemiIconDesktop") as Geometry,
            Width = 16,
            Height = 16,
            Margin = new Thickness(2),
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center,
        };

    private void ShowList()
    {
        Content = _listPage;
        Refresh();
    }

    private async Task ShowRunningAsync()
    {
        _problem.IsVisible = false;
        _filter.Text = string.Empty;
        _running.Children.Clear();
        _runningState.Text = L.AppsRunningLoading;
        _runningState.IsVisible = true;
        Content = _runningPage;
        _programs = await Task.Run(() => V2pnRunningPrograms.Snapshot()
            .Select(p => (p, ProgramIcons.Load(p.Path)))
            .ToList());
        FillRunning();
    }

    private void FillRunning()
    {
        _running.Children.Clear();
        var filter = _filter.Text?.Trim() ?? string.Empty;
        var shown = _programs
            .Where(p => filter.Length == 0
                        || p.Program.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                        || p.Program.Process.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        _runningState.Text = L.AppsRunningNone;
        _runningState.IsVisible = shown.Count == 0;
        foreach (var (program, bitmap) in shown)
        {
            _running.Children.Add(RunningRow(program, bitmap));
        }
    }

    private Button RunningRow(V2pnRunningProgram program, Bitmap? bitmap)
    {
        var added = new TextBlock
        {
            Classes = { "srowhint" },
            Text = L.AppsAdded,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = Listed(program.Process),
        };
        var text = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Classes = { "srowtitle" },
                    Text = program.Title,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
                // The name the rule matches ("Codex" runs as ChatGPT.exe); the full path is in the tooltip.
                new TextBlock
                {
                    Classes = { "srowhint" },
                    Text = program.Process,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            },
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12, Margin = new Thickness(4, 2) };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(added, 2);
        grid.Children.Add(Icon(bitmap));
        grid.Children.Add(text);
        grid.Children.Add(added);

        var row = new Button
        {
            Content = grid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !added.IsVisible,
        };
        row.Bind(ThemeProperty, row.GetResourceObservable("V2SideRow"));
        AutomationProperties.SetName(row, string.Format(L.AppsRunningAdd, program.Title));
        ToolTip.SetTip(row, program.Path ?? program.Process);
        row.Click += async (_, _) =>
        {
            row.IsEnabled = false;
            added.IsVisible = true;
            await AddAsync(program.Process);
        };
        return row;
    }

    private async Task ChooseFileAsync()
    {
        _problem.IsVisible = false;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null)
        {
            return;
        }
        var mac = OperatingSystem.IsMacOS();
        var type = mac
            ? new FilePickerFileType(L.AppsFileFilter) { Patterns = ["*.app"], AppleUniformTypeIdentifiers = ["com.apple.application-bundle"] }
            : new FilePickerFileType(L.AppsFileFilter) { Patterns = ["*.exe"], MimeTypes = ["application/x-msdownload"] };
        var start = mac ? "/Applications" : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.AppsChooseFile,
            AllowMultiple = false,
            FileTypeFilter = [type],
            SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(start),
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path.IsNullOrEmpty())
        {
            return;
        }
        var process = ProgramOf(path, mac);
        if (process == null)
        {
            _problem.Text = mac ? L.AppsNotAProgramMac : L.AppsNotAProgram;
            _problem.IsVisible = true;
            return;
        }
        await AddAsync(process);
    }

    /// <summary>The process name of the chosen file: an .exe on Windows; a Mac app's program from its bundle.</summary>
    private static string? ProgramOf(string path, bool mac)
    {
        if (!mac)
        {
            return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? V2pnPresetRules.NormalizeProgram(path) : null;
        }
        var trimmed = path.TrimEnd('/');
        if (trimmed.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            return V2pnRunningPrograms.MacExecutable(trimmed);
        }
        // A program inside a bundle, picked by hand.
        return V2pnRunningPrograms.MacBundleOf(trimmed) != null ? V2pnPresetRules.NormalizeProgram(trimmed) : null;
    }
}
