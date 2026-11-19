using System.Collections.Specialized;
using Avalonia.Data.Converters;

namespace v2rayN.Desktop.Views;

public partial class ClashConnectionsView : ReactiveUserControl<ClashConnectionsViewModel>
{
    // The view model's "rule , a->b" as "rule · a → b".
    public static readonly IValueConverter ChainText = new FuncValueConverter<string?, string>(s =>
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return string.Empty;
        }
        var parts = s.Split(" , ", 2);
        var rule = parts[0].Trim();
        var chain = parts.Length > 1 ? parts[1].Replace("->", " → ").Trim() : string.Empty;
        return rule.Length == 0 ? chain : chain.Length == 0 ? rule : $"{rule} · {chain}";
    });

    public static readonly IValueConverter AppName = new FuncValueConverter<string?, string>(p =>
        string.IsNullOrWhiteSpace(p) ? "—" : Path.GetFileName(p.Replace('\\', '/').TrimEnd('/')) is { Length: > 0 } name ? name : p);

    public static readonly IValueConverter Upper = new FuncValueConverter<string?, string>(s => s?.ToUpperInvariant() ?? string.Empty);

    // The view model refills its list every second; the page shows a sorted copy and keeps the selection by id.
    private readonly BulkObservableCollection<ClashConnectionModel> _rows = [];
    private string _sortColumn = "Time";
    private bool _sortDescending;
    private bool _refilling;

    public ClashConnectionsView()
    {
        InitializeComponent();

        lstConnections.ItemsSource = _rows;
        lstConnections.KeyDown += LstConnections_KeyDown;
        foreach (var btn in new[] { btnSortHost, btnSortApp, btnSortType, btnSortTime })
        {
            btn.Click += SortHeader_Click;
        }
        AttachedToVisualTree += (_, _) => UpdateState();
        cardList.SizeChanged += (_, e) => cardList.Classes.Set("narrow", e.NewSize.Width < 620);

        this.WhenActivated(disposables =>
        {
            this.Bind(ViewModel, vm => vm.HostFilter, v => v.txtHostFilter.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AutoRefresh, v => v.togAutoRefresh.IsChecked).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.ConnectionCloseCmd, v => v.menuConnectionClose).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ConnectionCloseAllCmd, v => v.menuConnectionCloseAll).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ConnectionCloseAllCmd, v => v.btnConnectionCloseAll).DisposeWith(disposables);

            if (ViewModel is { } vm)
            {
                vm.ConnectionItems.CollectionChanged += Source_CollectionChanged;
                new ActionDisposable(() => vm.ConnectionItems.CollectionChanged -= Source_CollectionChanged).DisposeWith(disposables);
            }
            lstConnections.SelectionChanged += LstConnections_SelectionChanged;
            new ActionDisposable(() => lstConnections.SelectionChanged -= LstConnections_SelectionChanged).DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel!.AutoRefresh, v => v.ViewModel!.HostFilter)
                .Subscribe(_ => UpdateState())
                .DisposeWith(disposables);

            Refill();
        });
    }

    private void Source_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refill();

    private void LstConnections_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_refilling && ViewModel != null)
        {
            ViewModel.SelectedSource = lstConnections.SelectedItem as ClashConnectionModel;
        }
    }

    private void Refill()
    {
        if (ViewModel == null)
        {
            return;
        }
        var selectedId = (lstConnections.SelectedItem as ClashConnectionModel)?.Id;
        IEnumerable<ClashConnectionModel> items = ViewModel.ConnectionItems.ToList();
        items = _sortColumn switch
        {
            "Host" => Order(items, t => t.Host ?? string.Empty),
            "ProcessPath" => Order(items, t => AppName.Convert(t.ProcessPath, typeof(string), null, CultureInfo.CurrentCulture) as string ?? string.Empty),
            "Network" => Order(items, t => $"{t.Network} {t.Type}"),
            _ => _sortDescending ? items.OrderByDescending(t => t.Time) : items.OrderBy(t => t.Time),
        };
        _refilling = true;
        try
        {
            _rows.ReplaceRange(items.ToList());
            var again = selectedId == null ? null : _rows.FirstOrDefault(t => t.Id == selectedId);
            lstConnections.SelectedItem = again;
            ViewModel.SelectedSource = again!;
        }
        finally
        {
            _refilling = false;
        }
        UpdateState();
    }

    private IEnumerable<ClashConnectionModel> Order(IEnumerable<ClashConnectionModel> items, Func<ClashConnectionModel, string> key) =>
        _sortDescending
            ? items.OrderByDescending(key, StringComparer.CurrentCultureIgnoreCase)
            : items.OrderBy(key, StringComparer.CurrentCultureIgnoreCase);

    private void UpdateState()
    {
        if (ViewModel == null)
        {
            return;
        }
        var count = _rows.Count;
        txtCount.Text = L.ConnCount(count);
        var singBox = AppManager.Instance.IsRunningCore(ECoreType.sing_box);
        txtState.Text = singBox && !ViewModel.AutoRefresh ? L.ConnPaused : string.Empty;
        txtState.IsVisible = txtState.Text.Length > 0;

        panelEmpty.IsVisible = count == 0;
        var filter = ViewModel.HostFilter;
        if (count == 0 && !string.IsNullOrEmpty(filter) && singBox)
        {
            txtEmptyTitle.Text = L.ConnNoMatchTitle;
            txtEmptyHint.Text = string.Format(L.ConnNoMatchHint, filter);
        }
        else
        {
            txtEmptyTitle.Text = L.ConnEmptyTitle;
            txtEmptyHint.Text = singBox ? L.ConnEmptyHint : L.LiveNeedsSingBox;
        }
        btnConnectionCloseAll.IsEnabled = count > 0;
    }

    private void SortHeader_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string column })
        {
            return;
        }
        // A new column starts A to Z, or newest first for the time; the same column again flips it.
        _sortDescending = column == _sortColumn && !_sortDescending;
        _sortColumn = column;
        Refill();
    }

    private async void RowClose_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ClashConnectionModel item } || ViewModel == null)
        {
            return;
        }
        try
        {
            ViewModel.SelectedSource = item;
            await ViewModel.ClashConnectionClose(false);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ClashConnectionsView", ex);
        }
    }

    private async void LstConnections_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || ViewModel?.SelectedSource == null)
        {
            return;
        }
        e.Handled = true;
        try
        {
            await ViewModel.ClashConnectionClose(false);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ClashConnectionsView", ex);
        }
    }
}
