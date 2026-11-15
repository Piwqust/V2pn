using DialogHostAvalonia;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>Connection state of the current server.</summary>
public enum ProtectionState
{
    Unprotected,
    Protected,
    LocalOnly,
}

/// <summary>
/// Bottom statistics card: protection state, server, ping, protocol, session traffic and a live speed graph.
/// </summary>
public partial class StatusBarView : ReactiveUserControl<StatusBarViewModel>
{
    private const int ChartPoints = 60;

    private static Config _config;
    private readonly Queue<double> _down = new(Enumerable.Repeat(0d, ChartPoints));
    private readonly Queue<double> _up = new(Enumerable.Repeat(0d, ChartPoints));
    private readonly DispatcherTimer _clock;
    private ProtectionState _state;
    private DateTime? _since;
    private bool _hasServer;
    private double _sessionBytes;
    private bool _checkFailed;
    // AvailabilityDelay as last shown: the chip's visibility depends on it and on the find offer.
    private int _checkDelay;
    private string? _findProgress;
    private bool _iconWarn;
    private int _iconIndex;
    // Below this width the card drops the speed graph and the Check label, so the state and the numbers
    // keep their whole text (a narrow window clipped "Proxy on port 20828" to "port 2").
    private const double CompactWidth = 600;
    private bool _compact;
    private bool _chartWanted = true;

    /// <summary>"Find a working server" was clicked; the window pings the neighbours and switches.</summary>
    public event Action? FindWorkingRequested;

    public StatusBarView()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;
        btnCheck.Click += (_, _) => _ = CheckConnectionAsync();
        Avalonia.Automation.AutomationProperties.SetName(btnCheck, L.CheckConnection);
        btnIpInfo.Click += (_, _) => _ = MainWindow.ShowRoomyDialog(new IpInfoView());
        Avalonia.Automation.AutomationProperties.SetName(btnIpInfo, L.IpButtonTip);
        btnFindWorking.Click += (_, _) =>
        {
            if (_findProgress == null)
            {
                FindWorkingRequested?.Invoke();
            }
        };
        chartHost.SizeChanged += (_, _) => DrawChart();
        // The card's width follows the window, never its own content, so this cannot flip back and forth.
        SizeChanged += (_, e) => SetCompact(e.NewSize.Width < CompactWidth);

        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateClock());

        this.WhenActivated(disposables =>
        {
            this.WhenAnyValue(v => v.ViewModel.AvailabilityDelay)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(ShowCheck)
                .DisposeWith(disposables);

            ViewModel.SetClipboardDataInteraction.RegisterHandler(async interaction =>
            {
                await AvaUtils.SetClipboardData(this, interaction.Input);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.PasswordInputInteraction.RegisterHandler(async interaction =>
            {
                interaction.SetOutput(await PasswordInputAsync());
            }).DisposeWith(disposables);

            ViewModel.DispatcherRefreshIconInteraction.RegisterHandler(interaction =>
            {
                Dispatcher.UIThread.Post(RefreshIcon, DispatcherPriority.Default);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            AppEvents.DispatcherStatisticsRequested
                .AsObservable()
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(AddSample)
                .DisposeWith(disposables);

            _clock.Start();
            new ActionDisposable(_clock.Stop).DisposeWith(disposables);
        });

        SetState(ProtectionState.Unprotected);
        SetServer(null, 0);
        ShowCheck(0);
        DrawChart();

        // This view is created before the first DispatcherRefreshIconInteraction can reach it.
        RefreshIcon();
    }

    /// <summary>The live speed graph can be turned off on the Appearance page; the stats then use the full width.</summary>
    public void ShowSpeedChart(bool show)
    {
        _chartWanted = show;
        ApplyChart();
    }

    private void SetCompact(bool compact)
    {
        if (compact == _compact)
        {
            return;
        }
        _compact = compact;
        ApplyChart();
        RefreshFindWorking();
    }

    private void ApplyChart()
    {
        var show = _chartWanted && !_compact;
        panelSpeed.IsVisible = show;
        if (panelSpeed.Parent is Grid grid && grid.ColumnDefinitions.Count == 3)
        {
            grid.ColumnDefinitions[1].Width = show ? new GridLength(28) : new GridLength(0);
            grid.ColumnDefinitions[2].Width = show ? new GridLength(1.15, GridUnitType.Star) : new GridLength(0);
        }
    }

    /// <summary>Pauses the clock while the window is minimized or in the tray; nobody reads it there.</summary>
    public void SetSeen(bool seen)
    {
        if (seen)
        {
            UpdateClock();
            _clock.Start();
        }
        else
        {
            _clock.Stop();
        }
    }

    /// <summary>
    /// The text follows the proxy mode; <paramref name="warn"/> paints it amber when the last connection
    /// check failed, so the card agrees with the pill, the tint and the map pin.
    /// </summary>
    public void SetState(ProtectionState state, bool warn = false, bool iconWarn = false)
    {
        _iconWarn = iconWarn;
        if (state != _state || _since == null)
        {
            _since = state == ProtectionState.Unprotected ? null : DateTime.Now;
            if (state == ProtectionState.Unprotected)
            {
                _sessionBytes = 0;
            }
        }
        _state = state;

        var (text, brush) = state switch
        {
            ProtectionState.Protected => (L.Protected, warn ? "V2PingMid" : "V2Safe"),
            ProtectionState.LocalOnly => (L.LocalOnly, "V2PingMid"),
            _ => (L.Unprotected, "V2Unsafe"),
        };
        txtState.Text = text;
        ToolTip.SetTip(txtState, state == ProtectionState.LocalOnly
            ? string.Format(L.LocalOnlyTip, AppManager.Instance.GetLocalPort(EInboundProtocol.socks))
            : null);
        SetBrush(txtState, TextBlock.ForegroundProperty, brush);
        SetBrush(iconState, PathIcon.ForegroundProperty, brush);
        UpdateClock();
        RefreshIcon();
    }

    public void SetServer(ProfileItem? item, int delay)
    {
        // Nothing to check without a server; the check result of a previous server would mislead.
        _hasServer = item != null;
        btnCheck.IsVisible = _hasServer;
        _checkDelay = ViewModel?.AvailabilityDelay ?? 0;
        RefreshFindWorking();
        // By default the card answers what a person asks: which address sites see now, and whose server
        // this is. The server's own address, port and transport are for the advanced view.
        var advanced = _config.UiItem.V2pnSettingsAdvanced;
        // The address sites see is known only when an IP lookup service is set in Settings (off by default,
        // so nothing extra is asked of a third party); without it the cell names the server itself.
        var ip = ViewModel?.AvailabilityIp;
        var exitKnown = !advanced && item != null && _state != ProtectionState.Unprotected
            && ViewModel?.AvailabilityDelay > 0 && ip.IsNotEmpty();
        lblStatServer.Text = exitKnown ? L.ExitAddress : L.ServerIp;
        lblStatProtocol.Text = advanced ? L.Protocol : L.ServerSource;
        if (item == null)
        {
            txtStatServer.Text = "—";
            txtStatProtocol.Text = "—";
        }
        else if (!advanced)
        {
            txtStatServer.Text = exitKnown ? ip : ServerNames.TrimLeadingSymbols(CountryResolver.ServerName(item));
            txtStatProtocol.Text = SourceName(item);
        }
        else
        {
            // V2pn: a partner that closed its settings keeps the address out of sight.
            txtStatServer.Text = V2pnLocks.Instance.Hides(item.Subid) ? L.AddressHidden : item.Port > 0 ? $"{item.Address}:{item.Port}" : item.Address;
            var transport = string.Join(" · ", new[] { item.GetNetwork(), item.StreamSecurity }.Where(t => t.IsNotEmpty()));
            txtStatProtocol.Text = transport.IsNotEmpty() ? $"{item.ConfigType} · {transport}" : item.ConfigType.ToString();
        }
        // A long host name is trimmed in the middle; hovering shows it whole.
        ToolTip.SetTip(txtStatServer, item == null ? null : txtStatServer.Text);

        txtStatPing.Text = delay switch
        {
            0 => "—",
            < 0 => L.Timeout,
            _ => L.Ms(delay),
        };
        var tier = delay switch
        {
            <= 0 => "V2PingNone",
            <= 300 => "V2PingGood",
            <= 800 => "V2PingMid",
            _ => "V2PingBad",
        };
        SetBrush(barPing, Border.BackgroundProperty, tier);
        // Lower is better: a full bar means a fast server, like a quality meter.
        barPing.Width = delay > 0 ? Math.Clamp(48 * (1 - (delay / 1000.0)), 6, 48) : 0;
        // An empty track next to "—" reads as a loader that never finishes.
        trackPing.IsVisible = delay > 0;
    }

    // Subscription names by id. Looked up once each: the card is refreshed every second and a half.
    private readonly Dictionary<string, string> _sourceNames = [];

    private string SourceName(ProfileItem item)
    {
        if (item.Subid.IsNullOrEmpty())
        {
            return L.OwnServer;
        }
        if (_sourceNames.TryGetValue(item.Subid, out var name))
        {
            return name;
        }
        _sourceNames[item.Subid] = "…";
        _ = LoadSourceNameAsync(item.Subid);
        return "…";
    }

    private async Task LoadSourceNameAsync(string subId)
    {
        try
        {
            var sub = await AppManager.Instance.GetSubItem(subId);
            _sourceNames[subId] = sub == null ? L.OwnServer : ViewModels.SideSubRow.NameOf(sub);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(StatusBarView), ex);
            _sourceNames.Remove(subId);
        }
    }

    private void UpdateClock()
    {
        txtStateTime.Text = _since is { } since ? "·  " + Fmt.Duration(DateTime.Now - since) : string.Empty;
    }

    private void AddSample(ServerSpeedItem update)
    {
        // The cores' statistics arrive in kilobytes per one-second sample (v2rayN divides by 1024 and its
        // HumanFy starts at KB). Read as bytes, a 3 MB/s download showed as 3 KB/s and a session of
        // 200 MB as 200 KB.
        const double bytesPerUnit = 1024;
        var down = (update.ProxyDown + update.DirectDown) * bytesPerUnit;
        var up = (update.ProxyUp + update.DirectUp) * bytesPerUnit;
        _down.Dequeue();
        _down.Enqueue(down);
        _up.Dequeue();
        _up.Enqueue(up);
        if (_state != ProtectionState.Unprotected)
        {
            _sessionBytes += down + up;
        }
        txtSpeedDown.Text = Fmt.Speed(down);
        txtSpeedUp.Text = Fmt.Speed(up);
        txtStatTotal.Text = Fmt.Bytes(_sessionBytes);
        DrawChart();
    }

    private async Task CheckConnectionAsync()
    {
        if (ViewModel == null || ViewModel.AvailabilityDelay == -2)
        {
            return;
        }
        await ViewModel.TestServerAvailability();
    }

    // AvailabilityDelay: 0 not run, -2 running, -1 failed, otherwise the delay in ms.
    private void ShowCheck(int delay)
    {
        var running = delay == -2;
        spinCheck.IsVisible = running;
        iconCheckIdle.IsVisible = !running;
        // The label stays: a shorter "Checking…" made the right-aligned button jump. The spinner and the
        // pill already say a check is running, and the -2 guard in CheckConnectionAsync stops a second run.
        txtCheckAction.Text = L.CheckConnection;

        var was = chipCheck.IsVisible;
        var wasOk = chipCheck.Classes.Contains("ok");
        _checkDelay = delay;
        RefreshFindWorking();
        chipCheck.Classes.Set("ok", delay > 0);
        chipCheck.Classes.Set("fail", delay == -1);
        iconCheck.Data = this.FindResource(delay > 0 ? "SemiIconTickCircle" : "SemiIconAlertCircle") as Geometry;
        txtCheck.Text = delay > 0 ? string.Format(L.CheckWorks, delay) : L.CheckFailed;
        var ip = ViewModel?.AvailabilityIp;
        ToolTip.SetTip(chipCheck, ip.IsNotEmpty() ? $"IP {ip}" : null);
        // The answer to a click: it settles out of the button once, not on every refresh.
        if (chipCheck.IsVisible && (!was || wasOk != (delay > 0)))
        {
            _ = Motion.Enter(chipCheck, dx: 6, dy: 0, ms: 180, scale: 0.97);
        }
    }

    /// <summary>
    /// Connected and the server didn't answer the check: offer another one. Not when the core itself
    /// didn't run (another server won't help; the pill points at the log), and not while disconnected.
    /// </summary>
    public void SetCheckFailure(bool serverFailed)
    {
        _checkFailed = serverFailed;
        RefreshFindWorking();
    }

    /// <summary>Progress of the search ("Checking 3 of 12…"), or null when it is over.</summary>
    public void SetFindProgress(string? progress)
    {
        _findProgress = progress;
        RefreshFindWorking();
    }

    private void RefreshFindWorking()
    {
        var finding = _findProgress != null;
        btnFindWorking.IsVisible = finding || (_hasServer && _checkFailed);
        // Both labels don't fit the header at the minimum width in Russian; Check keeps its icon, tooltip and name.
        // A compact card keeps only the icon too.
        txtCheckAction.IsVisible = !btnFindWorking.IsVisible && !_compact;
        txtIpInfo.IsVisible = txtCheckAction.IsVisible;
        spinFind.IsVisible = finding;
        txtFindWorking.Text = _findProgress ?? L.FindWorkingServer;
        // While the offer is up, the pill above already says the server is not answering; a "No response"
        // chip beside the button said it a third time. Without the offer (the core itself failed) it stays.
        chipCheck.IsVisible = _hasServer && _checkDelay is -1 or > 0 && !btnFindWorking.IsVisible;
    }

    private void DrawChart()
    {
        var w = chartHost.Bounds.Width;
        var h = chartHost.Bounds.Height;
        var peak = Math.Max(_down.Max(), _up.Max());
        var idle = peak <= 0;
        txtChartIdle.IsVisible = idle;
        txtChartMax.IsVisible = !idle;
        if (txtSpeedDown.Text.IsNullOrEmpty())
        {
            txtSpeedDown.Text = Fmt.Speed(0);
            txtSpeedUp.Text = Fmt.Speed(0);
            txtStatTotal.Text = Fmt.Bytes(0);
        }
        if (w <= 0 || h <= 0 || idle)
        {
            pathDown.Data = pathDownFill.Data = pathUp.Data = null;
            return;
        }

        var max = Math.Max(peak, 1024) * 1.15;
        txtChartMax.Text = Fmt.Speed(max);
        pathDown.Data = BuildLine(_down, max, w, h, closed: false);
        pathDownFill.Data = BuildLine(_down, max, w, h, closed: true);
        pathUp.Data = BuildLine(_up, max, w, h, closed: false);
    }

    private static StreamGeometry BuildLine(IEnumerable<double> values, double max, double w, double h, bool closed)
    {
        var points = values.ToArray();
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        var step = w / (points.Length - 1);
        Point P(int i) => new(i * step, h - 1 - (points[i] / max * (h - 2)));

        ctx.BeginFigure(closed ? new Point(0, h) : P(0), closed);
        if (closed)
        {
            ctx.LineTo(P(0));
        }
        for (var i = 1; i < points.Length; i++)
        {
            ctx.LineTo(P(i));
        }
        if (closed)
        {
            ctx.LineTo(new Point(w, h));
        }
        ctx.EndFigure(closed);
        return geometry;
    }

    // The tray and taskbar follow the window's verdict (SetState), not the system-proxy type: in VPN mode
    // the system proxy is off while the connection is on. Re-set only when the icon actually changes.
    private void RefreshIcon()
    {
        var index = AvaUtils.AppIconIndex(_state, _iconWarn);
        if (index == _iconIndex)
        {
            return;
        }
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            _iconIndex = index;
            desktop.MainWindow.Icon = AvaUtils.GetAppIcon(index);
            var icons = TrayIcon.GetIcons(Application.Current);
            if (icons?.Count > 0)
            {
                icons[0].Icon = desktop.MainWindow.Icon;
                TrayIcon.SetIcons(Application.Current, icons);
            }
        }
    }

    private async Task<string?> PasswordInputAsync()
    {
        var dialog = new SudoPasswordInputView();
        var obj = await DialogHost.Show(dialog);

        var password = obj?.ToString();
        if (password.IsNullOrEmpty())
        {
            if (ViewModel != null)
            {
                ViewModel.EnableTun = false;
            }
            return password;
        }

        AppManager.Instance.LinuxSudoPwd = password;
        return password;
    }

    // A resource binding rather than a looked-up brush, so the colour follows a theme switch.
    private static void SetBrush(Control control, AvaloniaProperty property, string key) =>
        control.Bind(property, control.GetResourceObservable(key));
}

internal sealed class ActionDisposable(Action action) : IDisposable
{
    public void Dispose() => action();
}
