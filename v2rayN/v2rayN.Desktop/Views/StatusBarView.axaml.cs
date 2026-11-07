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

    public StatusBarView()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;
        btnCheck.Click += (_, _) => _ = CheckConnectionAsync();
        chartHost.SizeChanged += (_, _) => DrawChart();

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

    public void SetState(ProtectionState state)
    {
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
            ProtectionState.Protected => (L.Protected, "V2Safe"),
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
    }

    public void SetServer(ProfileItem? item, int delay)
    {
        // Nothing to check without a server; the check result of a previous server would mislead.
        _hasServer = item != null;
        btnCheck.IsVisible = _hasServer;
        chipCheck.IsVisible = _hasServer && ViewModel?.AvailabilityDelay is -1 or > 0;
        if (item == null)
        {
            txtStatServer.Text = "—";
            txtStatProtocol.Text = "—";
        }
        else
        {
            txtStatServer.Text = item.Port > 0 ? $"{item.Address}:{item.Port}" : item.Address;
            var transport = string.Join(" · ", new[] { item.GetNetwork(), item.StreamSecurity }.Where(t => t.IsNotEmpty()));
            txtStatProtocol.Text = transport.IsNotEmpty() ? $"{item.ConfigType} · {transport}" : item.ConfigType.ToString();
        }

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
    }

    private void UpdateClock()
    {
        txtStateTime.Text = _since is { } since ? "·  " + Fmt.Duration(DateTime.Now - since) : string.Empty;
    }

    private void AddSample(ServerSpeedItem update)
    {
        double down = update.ProxyDown + update.DirectDown;
        double up = update.ProxyUp + update.DirectUp;
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
        txtCheckAction.Text = running ? L.Checking : L.CheckConnection;

        chipCheck.IsVisible = _hasServer && delay is -1 or > 0;
        chipCheck.Classes.Set("ok", delay > 0);
        chipCheck.Classes.Set("fail", delay == -1);
        iconCheck.Data = this.FindResource(delay > 0 ? "SemiIconTickCircle" : "SemiIconAlertCircle") as Geometry;
        txtCheck.Text = delay > 0 ? string.Format(L.CheckWorks, delay) : L.CheckFailed;
        var ip = ViewModel?.AvailabilityIp;
        ToolTip.SetTip(chipCheck, ip.IsNotEmpty() ? $"IP {ip}" : null);
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

    private void RefreshIcon()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            desktop.MainWindow.Icon = AvaUtils.GetAppIcon(_config.SystemProxyItem.SysProxyType);
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
