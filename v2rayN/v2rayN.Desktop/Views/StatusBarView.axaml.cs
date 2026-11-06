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
    private const double ChartWidth = 300;
    private const double ChartHeight = 58;

    private static Config _config;
    private readonly Queue<double> _down = new(Enumerable.Repeat(0d, ChartPoints));
    private readonly Queue<double> _up = new(Enumerable.Repeat(0d, ChartPoints));
    private readonly DispatcherTimer _clock;
    private ProtectionState _state;
    private DateTime? _since;
    private double _sessionBytes;

    public StatusBarView()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;
        txtRunningInfoDisplay.Tapped += (_, _) => ViewModel?.TestServerAvailability();

        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateClock());

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.RunningInfoDisplay, v => v.txtRunningInfoDisplay.Text).DisposeWith(disposables);

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
        DrawChart();

        // This view is created before the first DispatcherRefreshIconInteraction can reach it.
        RefreshIcon();
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
        SetBrush(txtState, TextBlock.ForegroundProperty, brush);
        SetBrush(iconState, PathIcon.ForegroundProperty, brush);
        UpdateClock();
    }

    public void SetServer(ProfileItem? item, int delay)
    {
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
            _ => $"{delay} ms",
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

    private void DrawChart()
    {
        var max = Math.Max(Math.Max(_down.Max(), _up.Max()), 1024);
        txtChartMax.Text = Fmt.Speed(max);
        pathDown.Data = BuildLine(_down, max, closed: false);
        pathDownFill.Data = BuildLine(_down, max, closed: true);
        pathUp.Data = BuildLine(_up, max, closed: false);
        if (txtSpeedDown.Text.IsNullOrEmpty())
        {
            txtSpeedDown.Text = Fmt.Speed(0);
            txtSpeedUp.Text = Fmt.Speed(0);
            txtStatTotal.Text = Fmt.Bytes(0);
        }
    }

    private static StreamGeometry BuildLine(IEnumerable<double> values, double max, bool closed)
    {
        var points = values.ToArray();
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        var step = ChartWidth / (points.Length - 1);
        Point P(int i) => new(i * step, ChartHeight - (points[i] / max * (ChartHeight - 4)) - 1);

        ctx.BeginFigure(closed ? new Point(0, ChartHeight) : P(0), closed);
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
            ctx.LineTo(new Point(ChartWidth, ChartHeight));
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
