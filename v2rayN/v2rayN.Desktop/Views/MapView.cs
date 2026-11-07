using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>A country with servers, shown as a dot on the map.</summary>
public sealed record MapLocation(string Code, string Name, int Count, int BestDelay);

/// <summary>
/// World map that glides to the country of the active server. The pin stays fixed in the
/// middle of the view and the map pans under it; every other country with servers gets a dot.
/// </summary>
public sealed class MapView : UserControl
{
    private readonly Canvas _canvas;
    private readonly Canvas _dots = new() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly List<(MapLocation Location, MapCountry Country, Ellipse Dot)> _dotItems = [];
    private readonly Panel _root;
    private readonly Panel _pin;
    private readonly Ellipse _pinCore;
    private readonly Ellipse _pinRing;
    private readonly Ellipse _pinHalo;
    private readonly Dictionary<string, Path> _paths = new(StringComparer.OrdinalIgnoreCase);
    private string? _focusCode;
    private ProtectionState _state;
    private bool _built;

    /// <summary>Where the pin sits, as a fraction of the view (the header covers the top).</summary>
    public double FocusY { get; set; } = 0.47;

    public MapView()
    {
        ClipToBounds = true;
        var map = WorldMap.Instance;

        _canvas = new Canvas
        {
            Width = map.Width,
            Height = map.Height,
            RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        _pinHalo = new Ellipse { Width = 64, Height = 64, Opacity = 0.18, Classes = { "halo" } };
        _pinRing = new Ellipse { Width = 26, Height = 26, StrokeThickness = 3, Fill = Brushes.Transparent };
        _pinCore = new Ellipse { Width = 12, Height = 12 };
        _pin = new Panel
        {
            Width = 64,
            Height = 64,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { _pinHalo, _pinRing, _pinCore },
        };
        foreach (var e in new[] { _pinHalo, _pinRing, _pinCore })
        {
            e.HorizontalAlignment = HorizontalAlignment.Center;
            e.VerticalAlignment = VerticalAlignment.Center;
        }

        _root = new Panel { Children = { _canvas, _dots, _pin } };
        Content = _root;

        SizeChanged += (_, _) => Apply(animate: false);
        ActualThemeVariantChanged += (_, _) => Paint();
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(Build, DispatcherPriority.Background);
    }

    /// <summary>Raised with the country code when a location dot is clicked.</summary>
    public event Action<string>? LocationClicked;

    /// <summary>Replaces the location dots.</summary>
    public void SetLocations(IEnumerable<MapLocation> locations)
    {
        _dots.Children.Clear();
        _dotItems.Clear();
        foreach (var location in locations)
        {
            var country = WorldMap.Instance.Get(location.Code);
            if (country == null)
            {
                continue;
            }
            var dot = new Ellipse { Classes = { "mapdot" } };
            var ping = location.BestDelay > 0 ? $" · {L.Ms(location.BestDelay)}" : string.Empty;
            ToolTip.SetTip(dot, $"{location.Name}\n{L.ServerCount(location.Count)}{ping}");
            ToolTip.SetShowDelay(dot, 150);
            var code = location.Code;
            dot.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                LocationClicked?.Invoke(code);
            };
            _dots.Children.Add(dot);
            _dotItems.Add((location, country, dot));
        }
        PlaceDots();
    }
    public void Focus(string? countryCode, ProtectionState state)
    {
        var changed = !string.Equals(_focusCode, countryCode, StringComparison.OrdinalIgnoreCase);
        _focusCode = countryCode;
        _state = state;
        Paint();
        Apply(animate: changed);
    }

    private void Build()
    {
        if (_built)
        {
            return;
        }
        _built = true;
        foreach (var (code, country) in WorldMap.Instance.Countries)
        {
            if (country.Path.IsNullOrEmpty())
            {
                continue;
            }
            try
            {
                var path = new Path
                {
                    Data = StreamGeometry.Parse(country.Path),
                    StrokeThickness = 0.7,
                    StrokeJoin = PenLineJoin.Round,
                    IsHitTestVisible = false,
                };
                _paths[code] = path;
                _canvas.Children.Add(path);
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"MapView {code}", ex);
            }
        }
        Paint();
        Apply(animate: false);
    }

    private void Paint()
    {
        _root.Background = Brush("V2MapOcean");
        var land = Brush("V2MapLand");
        var border = Brush("V2MapBorder");
        var active = Brush(_state switch
        {
            ProtectionState.Protected => "V2MapActiveSafe",
            ProtectionState.LocalOnly => "V2MapActiveLocal",
            _ => "V2MapActiveUnsafe",
        });
        foreach (var (code, path) in _paths)
        {
            path.Fill = string.Equals(code, _focusCode, StringComparison.OrdinalIgnoreCase) ? active : land;
            path.Stroke = border;
        }

        var accent = Brush(_state switch
        {
            ProtectionState.Protected => "V2Safe",
            ProtectionState.LocalOnly => "V2PingMid",
            _ => "V2Unsafe",
        });
        _pinRing.Stroke = accent;
        var replay = !Equals(_pinHalo.Fill, accent) || _pulsedCode != _focusCode;
        _pinHalo.Fill = accent;
        // A white core vanishes on the light map, so there the pin is a solid dot.
        _pinCore.Fill = IsDark() ? Brush("V2PinCore") : accent;
        _pin.IsVisible = Anchor() != null;
        // Hidden with the pin, so a pulse never runs for a pin nobody sees.
        _pinHalo.IsVisible = _pin.IsVisible;
        if (replay && _pin.IsVisible)
        {
            Pulse();
        }
        PlaceDots();
    }

    private string? _pulsedCode;

    // A few waves when the pin lands somewhere new or the protection state changes its colour.
    private void Pulse()
    {
        _pulsedCode = _focusCode;
        _pinHalo.Classes.Remove("pulse");
        // Re-adding the class in a later dispatcher pass starts the animation over.
        Dispatcher.UIThread.Post(() => _pinHalo.Classes.Add("pulse"), DispatcherPriority.Background);
    }

    // "EU" servers have no single country: aim at central Europe without highlighting anything.
    private MapCountry? Anchor() =>
        string.Equals(_focusCode, "EU", StringComparison.OrdinalIgnoreCase)
            ? WorldMap.Instance.Get("DE")
            : WorldMap.Instance.Get(_focusCode);

    private void Apply(bool animate)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var map = WorldMap.Instance;
        var country = Anchor();
        double scale, x, y;
        if (country != null)
        {
            // Frame roughly a 1000-px slice of the map around the country, like a regional view.
            scale = Math.Clamp(w / 1000.0, w / map.Width, 6);
            x = country.X;
            y = country.Y;
        }
        else
        {
            scale = Math.Max(w / map.Width, h / map.Height) * 1.15;
            x = map.Width * 0.53;
            y = map.Height * 0.38;
        }

        var cx = w / 2;
        var cy = h * FocusY;
        var tx = cx - (scale * x);
        var ty = cy - (scale * y);

        AnimateTo(scale, tx, ty, animate);

        _pin.Margin = new Thickness(cx - (_pin.Width / 2), cy - (_pin.Height / 2), 0, 0);
    }

    private (double S, double X, double Y) _from;
    private (double S, double X, double Y) _to;
    private (double S, double X, double Y) _now = (1, 0, 0);
    private TimeSpan? _animStart;
    private bool _animating;
    private static readonly TimeSpan AnimDuration = TimeSpan.FromMilliseconds(700);

    // Matrix animation keeps the math explicit: screen = scale * map + translation.
    // Driven by the compositor's frame clock (RequestAnimationFrame), so it moves once per frame.
    private void AnimateTo(double scale, double tx, double ty, bool animate)
    {
        _to = (scale, tx, ty);
        var top = TopLevel.GetTopLevel(this);
        if (!animate || !Common.Motion.Enabled || top == null)
        {
            _animating = false;
            SetMatrix(_to);
            return;
        }
        _from = _now;
        _animStart = null;
        if (!_animating)
        {
            _animating = true;
            top.RequestAnimationFrame(OnFrame);
        }
    }

    private void OnFrame(TimeSpan time)
    {
        if (!_animating)
        {
            return;
        }
        _animStart ??= time;
        var t = Math.Clamp((time - _animStart.Value) / AnimDuration, 0, 1);
        // The map is already on screen and moves, so ease in and out.
        var k = t < 0.5 ? 4 * t * t * t : 1 - (Math.Pow((-2 * t) + 2, 3) / 2);
        SetMatrix((Lerp(_from.S, _to.S, k), Lerp(_from.X, _to.X, k), Lerp(_from.Y, _to.Y, k)));
        if (t >= 1)
        {
            _animating = false;
            return;
        }
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
    }

    private static double Lerp(double a, double b, double k) => a + ((b - a) * k);

    private void SetMatrix((double S, double X, double Y) v)
    {
        _now = v;
        _canvas.RenderTransform = new MatrixTransform(new Matrix(v.S, 0, 0, v.S, v.X, v.Y));
        PlaceDots();
    }

    // Dots keep their screen size, so they follow the map transform by position only.
    private const double DotSize = 9;

    private void PlaceDots()
    {
        foreach (var (location, country, dot) in _dotItems)
        {
            var focused = string.Equals(location.Code, _focusCode, StringComparison.OrdinalIgnoreCase)
                          || (string.Equals(_focusCode, "EU", StringComparison.OrdinalIgnoreCase) && location.Code == "DE");
            dot.IsVisible = !focused;
            Canvas.SetLeft(dot, (country.X * _now.S) + _now.X - (DotSize / 2));
            Canvas.SetTop(dot, (country.Y * _now.S) + _now.Y - (DotSize / 2));
        }
    }

    private bool IsDark() =>
        ActualThemeVariant == ThemeVariant.Dark || ActualThemeVariant.InheritVariant == ThemeVariant.Dark;

    // Colours come from the theme dictionaries, so the lookup needs the variant this view renders in.
    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush
            ? brush
            : Brushes.Gray;
}
