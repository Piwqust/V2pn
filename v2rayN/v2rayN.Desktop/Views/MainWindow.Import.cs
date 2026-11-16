using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Styling;
using v2rayN.Desktop.ViewModels;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>
/// V2pn: moving to V2pn from other clients. The first-run card offers what it finds; the menu (Tools →
/// "Move from another app") lists the same any time, plus a folder picker for portable copies the search
/// missed. v2rayN alone moves whole, with routing and settings (<see cref="V2pnImport"/>, a restart);
/// everything else, and v2rayN together with other apps, moves as subscriptions and servers
/// (<see cref="V2pnForeignImport"/>), live.
/// </summary>
public partial class MainWindow
{
    /// <summary>One app the window offers to move from.</summary>
    private sealed record ImportSource(string Name, int Subscriptions, int Servers, V2rayNInstall? V2rayN, ForeignApp? Foreign)
    {
        public bool Locked => Foreign?.Locked == true;
        public string Counts => L.ImportCounts(Subscriptions, Servers);
    }

    private List<ImportSource>? _importSources;
    private Task<List<ImportSource>>? _importSearch;
    private readonly List<Button> _importRows = [];

    private void InitImport()
    {
        btnWelcomeImport.Click += async (_, _) => await MoveFromWelcomeAsync();
        menuImportV2rayN.SubmenuOpened += (_, _) => _ = FillImportMenuAsync();
    }

    private bool _welcomeImportStarted;

    /// <summary>
    /// The search walks a few thousand folders (1-2 s of CPU), so it runs when something can show what it finds: the
    /// welcome card (no servers yet), the walkthrough, or the menu. It used to run at every start.
    /// </summary>
    private void ShowImportOnWelcomeOnce()
    {
        if (_welcomeImportStarted)
        {
            return;
        }
        _welcomeImportStarted = true;
        _ = ShowImportOnWelcomeAsync();
    }

    /// <summary>The search runs once per start, in the background; the card and the menu share it.</summary>
    private Task<List<ImportSource>> SearchImportSourcesAsync(bool again = false)
    {
        if (again || _importSearch == null || _importSearch.IsFaulted)
        {
            _importSearch = Task.Run(FindImportSources);
        }
        return _importSearch;
    }

    private static List<ImportSource> FindImportSources()
    {
        var sources = new List<ImportSource>();
        try
        {
            if (V2pnImport.Find().FirstOrDefault(i => i.Servers > 0 || i.Subscriptions > 0) is { } v2rayN)
            {
                sources.Add(new ImportSource("v2rayN", v2rayN.Subscriptions, v2rayN.Servers, v2rayN, null));
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn import", ex);
        }
        try
        {
            sources.AddRange(V2pnForeignImport.Find().Select(a => new ImportSource(a.Name, a.Subscriptions.Count, a.Links.Count, null, a)));
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn foreign import", ex);
        }
        return sources;
    }

    #region First-run card

    // Tile hues of the subscriptions in the sidebar; an app keeps its colour from start to start.
    private static readonly string[] ImportTints = ["V2TileBlue", "V2TileGreen", "V2TilePurple", "V2TileAmber", "V2TileTeal", "V2TileRed"];
    private static readonly Geometry ImportTick = Geometry.Parse("M0,4.5 L3.2,7.5 L9,1");

    /// <summary>
    /// With other apps found, the card leads with the move: its own title, the apps as rows (a tick each
    /// when there are several), one solid button, and the ways to add a server by hand under a divider.
    /// </summary>
    private async Task ShowImportOnWelcomeAsync()
    {
        _importSources = await SearchImportSourcesAsync();
        var usable = _importSources.Where(s => !s.Locked).ToList();
        var shown = _importSources.Count > 0;
        panelWelcomeImport.IsVisible = shown;
        btnWelcomeImport.IsVisible = usable.Count > 0;
        // One solid button on the card: the move when it is offered, else pasting a link.
        btnWelcomePaste.Classes.Set("accent", usable.Count == 0);
        txtWelcomeTitle.Text = usable.Count > 0 ? L.ImportTitle : L.WelcomeTitle;
        txtWelcomeLead.Text = usable.Count > 0 ? L.ImportLead : L.WelcomeText;

        LayOutHandButtons(compact: usable.Count > 0);

        panelWelcomeImportApps.Children.Clear();
        _importRows.Clear();
        var ticks = usable.Count > 1;
        // Apps V2pn can move first; Happ, which it cannot read, last.
        var all = usable.Concat(_importSources.Where(s => s.Locked)).ToList();
        // A long list would push the buttons off the card: three rows and "N more", which opens the rest.
        var visible = all.Count > ImportRowsShown + 1 ? ImportRowsShown : all.Count;
        var hidden = new List<Control>();
        foreach (var (source, index) in all.Select((s, i) => (s, i)))
        {
            var row = ImportRow(source, ticks && !source.Locked);
            if (!source.Locked)
            {
                _importRows.Add(row);
            }
            row.IsVisible = index < visible;
            if (!row.IsVisible)
            {
                hidden.Add(row);
            }
            panelWelcomeImportApps.Children.Add(row);
        }
        if (hidden.Count > 0)
        {
            var more = new Button
            {
                Content = string.Format(L.ImportMoreApps, hidden.Count) + "  ⌄",
                Classes = { "subtle", "small" },
                Theme = this.FindResource("V2Button") as ControlTheme,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(4, 2, 0, 0),
            };
            more.Click += (_, _) =>
            {
                hidden.ForEach(r => r.IsVisible = true);
                more.IsVisible = false;
            };
            panelWelcomeImportApps.Children.Add(more);
        }
        UpdateImportButton();
    }

    private const int ImportRowsShown = 3;

    /// <summary>
    /// Under the move the ways to add by hand are secondary: one row of three short buttons. Without the
    /// move, "Paste from clipboard" is the card's main button on a row of its own, as before.
    /// </summary>
    private void LayOutHandButtons(bool compact)
    {
        var qr = btnWelcomeQr.IsVisible;
        if (compact && btnWelcomePaste.Parent is StackPanel stack)
        {
            stack.Children.Remove(btnWelcomePaste);
            gridWelcomeHand.Children.Insert(0, btnWelcomePaste);
            gridWelcomeHand.ColumnDefinitions = new ColumnDefinitions(qr ? "*,8,*,8,*" : "*,8,*");
            btnWelcomePaste.Height = double.NaN;
            Grid.SetColumn(btnWelcomePaste, 0);
            Grid.SetColumn(btnWelcomeSub, 2);
            Grid.SetColumnSpan(btnWelcomeSub, 1);
            Grid.SetColumn(btnWelcomeQr, 4);
            btnWelcomePaste.Content = L.HandPaste;
            btnWelcomeSub.Content = L.HandSubscription;
            btnWelcomeQr.Content = L.HandQr;
        }
        else if (!compact && btnWelcomePaste.Parent == gridWelcomeHand && gridWelcomeHand.Parent is StackPanel parent)
        {
            gridWelcomeHand.Children.Remove(btnWelcomePaste);
            parent.Children.Insert(0, btnWelcomePaste);
            gridWelcomeHand.ColumnDefinitions = new ColumnDefinitions("*,8,*");
            btnWelcomePaste.Height = 42;
            Grid.SetColumn(btnWelcomeSub, 0);
            Grid.SetColumnSpan(btnWelcomeSub, qr ? 1 : 3);
            Grid.SetColumn(btnWelcomeQr, 2);
            btnWelcomePaste.Content = L.WelcomePaste;
            btnWelcomeSub.Content = L.AddSubscription;
            btnWelcomeQr.Content = L.WelcomeQr;
        }
    }

    private Button ImportRow(ImportSource source, bool tick, Action? changed = null)
    {
        var hint = source.Locked ? L.ImportLockedShort
            : source.V2rayN != null && _importSources!.Count(s => !s.Locked) == 1 ? $"{source.Counts} · {L.ImportFoundV2rayN}"
            : source.Counts;
        var hash = 0;
        foreach (var c in source.Name)
        {
            hash = unchecked((hash * 31) + c);
        }
        var tile = new Border
        {
            Classes = { "subtile" },
            Background = Application.Current?.TryGetResource(ImportTints[Math.Abs(hash % ImportTints.Length)], ActualThemeVariant, out var brush) == true ? brush as IBrush : null,
            Child = new TextBlock { Text = source.Name[..1].ToUpperInvariant() },
        };
        var text = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = source.Name, Classes = { "routetitle" } },
                new TextBlock { Text = hint, Classes = { "routehint" }, TextWrapping = TextWrapping.Wrap },
            },
        };
        Grid.SetColumn(text, 1);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10, Children = { tile, text } };
        if (tick)
        {
            var check = new Border { Classes = { "importcheck" }, Child = new Avalonia.Controls.Shapes.Path { Data = ImportTick } };
            Grid.SetColumn(check, 2);
            grid.Children.Add(check);
        }
        var row = new Button
        {
            Tag = source,
            Content = grid,
            Theme = this.FindResource("V2SideRow") as ControlTheme,
            Classes = { "routeoption", "importapp" },
            IsHitTestVisible = tick,
            Focusable = tick,
        };
        AutomationProperties.SetName(row, source.Name);
        row.Classes.Set("active", tick);
        row.Classes.Set("locked", source.Locked);
        if (tick)
        {
            row.Click += (_, _) =>
            {
                row.Classes.Set("active", !row.Classes.Contains("active"));
                (changed ?? UpdateImportButton)();
            };
        }
        return row;
    }

    /// <summary>"Move from Throne" for one app; "Move from 3 apps" for the ticked ones, off when none is.</summary>
    private void UpdateImportButton()
    {
        if (_importRows.Count == 1)
        {
            btnWelcomeImport.Content = string.Format(L.ImportMoveFrom, ((ImportSource)_importRows[0].Tag!).Name);
            btnWelcomeImport.IsEnabled = true;
            return;
        }
        var picked = _importRows.Count(r => r.Classes.Contains("active"));
        btnWelcomeImport.Content = picked > 0 ? L.ImportMoveApps(picked) : L.ImportMoveSelected;
        btnWelcomeImport.IsEnabled = picked > 0;
    }

    private async Task MoveFromWelcomeAsync()
    {
        var picked = _importRows.Count == 1
            ? [(ImportSource)_importRows[0].Tag!]
            : _importRows.Where(r => r.Classes.Contains("active")).Select(r => (ImportSource)r.Tag!).ToList();
        if (picked.Count == 0)
        {
            return;
        }
        if (picked is [{ V2rayN: { } install }])
        {
            await MoveFromV2rayNAsync(install, fromWelcome: true);
            return;
        }
        btnWelcomeImport.IsEnabled = false;
        try
        {
            await MoveForeignAsync(picked);
        }
        finally
        {
            UpdateImportButton();
        }
    }

    #endregion First-run card

    #region Menu

    /// <summary>Tools → "Move from another app": each app found, then the folder picker.</summary>
    private async Task FillImportMenuAsync()
    {
        var search = SearchImportSourcesAsync();
        if (!search.IsCompleted)
        {
            SetImportMenu([new MenuItem { Header = L.ImportMenuSearching, IsEnabled = false }]);
        }
        var sources = await search;
        var items = new List<Control>();
        foreach (var source in sources)
        {
            var item = new MenuItem
            {
                Classes = { "rich" },
                IsEnabled = !source.Locked,
                Header = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = source.Name, Classes = { "mititle" } },
                        new TextBlock { Text = source.Locked ? L.ImportLockedHint : source.Counts, Classes = { "midesc" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 280 },
                    },
                },
            };
            item.Click += async (_, _) =>
            {
                if (source.V2rayN is { } install)
                {
                    await MoveFromV2rayNAsync(install, fromWelcome: false);
                }
                else
                {
                    await MoveForeignAsync([source]);
                }
            };
            items.Add(item);
        }
        if (items.Count == 0)
        {
            items.Add(new MenuItem { Header = L.ImportMenuNoneFound, IsEnabled = false });
        }
        items.Add(new Separator());
        var folder = new MenuItem { Header = L.ImportMenuFolder };
        folder.Click += async (_, _) => await MoveFromFolderAsync();
        items.Add(folder);
        SetImportMenu(items);
    }

    private void SetImportMenu(IEnumerable<Control> items)
    {
        menuImportV2rayN.Items.Clear();
        foreach (var item in items)
        {
            menuImportV2rayN.Items.Add(item);
        }
    }

    /// <summary>A portable copy the search missed: the person points at its folder.</summary>
    private async Task MoveFromFolderAsync()
    {
        try
        {
            var folder = await UI.OpenFolderDialog(L.ImportFolderTitle);
            if (folder.IsNullOrEmpty())
            {
                return;
            }
            if (V2pnImport.HasData(folder))
            {
                await MoveFromV2rayNAsync(V2pnImport.Describe(folder), fromWelcome: false);
                return;
            }
            var apps = await Task.Run(() => V2pnForeignImport.FindIn(folder));
            if (apps.Count == 0)
            {
                ShowToast(ToastKind.Warning, L.ImportFolderNothing);
                return;
            }
            await MoveForeignAsync(apps.Select(a => new ImportSource(a.Name, a.Subscriptions.Count, a.Links.Count, null, a)).ToList());
            _ = SearchImportSourcesAsync(again: true);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn import folder", ex);
        }
    }

    #endregion Menu

    #region Moving

    /// <summary>
    /// Subscriptions and servers of the picked apps, added to what V2pn has. The new subscriptions then
    /// download one by one, in view in the Subscriptions tab, the way a pasted link does.
    /// </summary>
    private async Task<bool> MoveForeignAsync(List<ImportSource> sources)
    {
        if (ViewModel == null)
        {
            return false;
        }
        var names = string.Join(", ", sources.Select(s => s.Name));
        try
        {
            var apps = await Task.Run(() => sources
                .Select(s => s.Foreign ?? V2pnForeignImport.ReadV2rayN(s.V2rayN!.DataDir))
                .Where(a => !a.Locked)
                .ToList());
            var newSubs = new List<string>();
            int servers = 0, skipped = 0;
            foreach (var app in apps)
            {
                var result = await V2pnForeignImport.ImportAsync(_config, app);
                newSubs.AddRange(result.NewSubIds);
                servers += result.ServersAdded;
                skipped += result.Skipped;
            }
            await ViewModel.RefreshAfterImportAsync();

            if (newSubs.Count + servers == 0)
            {
                ShowToast(ToastKind.Info, string.Format(L.ImportForeignNothingNew, names));
                return true;
            }
            var message = string.Format(L.ImportForeignDone, names, L.ImportCounts(newSubs.Count, servers));
            if (skipped > 0)
            {
                message += " " + string.Format(L.ImportForeignSkipped, L.ServerCount(skipped));
            }
            ShowToast(ToastKind.Success, message);

            if (newSubs.Count > 0)
            {
                // Where the new subscriptions are, so their rows and spinners are in view while they download.
                txtSearch.Text = string.Empty;
                if (_sidebar != null)
                {
                    _sidebar.OpenedSub = null;
                }
                lstTabs.SelectedIndex = (int)SidebarTab.Subscriptions;
                foreach (var id in newSubs)
                {
                    await UpdateAddedSubscriptionAsync(id);
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn foreign import", ex);
            ShowToast(ToastKind.Error, string.Format(L.ImportForeignFailed, names));
            return false;
        }
    }

    /// <summary>
    /// Closes v2rayN, notes where from and restarts: the copy is made at the next start, before V2pn
    /// reads its settings. From the menu, over servers V2pn already has, it asks first.
    /// </summary>
    private async Task MoveFromV2rayNAsync(V2rayNInstall install, bool fromWelcome)
    {
        try
        {
            var hasOwn = (ViewModel?.ProfilesViewModel.AllServersCount ?? 0) > 0;
            if ((!fromWelcome || hasOwn)
                && await UI.ShowYesNo(string.Format(L.ImportConfirm, install.DataDir), L.ImportConfirmYes) != ButtonResult.Yes)
            {
                return;
            }
            if (!await Task.Run(() => V2pnImport.Stop(install)))
            {
                ShowToast(ToastKind.Warning, L.ImportCloseFirst);
                return;
            }
            V2pnImport.Schedule(install.DataDir);
            ShowToast(ToastKind.Info, L.ImportRestarting);
            // Lets the message paint before the window goes.
            await Task.Delay(800);
            if (!ProcUtils.RebootAsAdmin(false))
            {
                ShowToast(ToastKind.Error, L.ImportRestartFailed);
                return;
            }
            await AppManager.Instance.AppExitAsync(true);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn import", ex);
        }
    }

    #endregion Moving
}
