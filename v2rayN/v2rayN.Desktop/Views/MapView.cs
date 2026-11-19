using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Converters;

namespace v2rayN.Desktop.Views;

/// <summary>A country with servers, shown as a dot on the map.</summary>
public sealed record MapLocation(string Code, string Name, int Count, int BestDelay);

/// <summary>
/// World map that flies to the active server. The map pans and zooms under a pin that stands in the server's
/// city (or the person's own city while V2pn is off and that city is known); every other country with servers
/// gets a dot in the colour of its best ping. The map repeats to the left and right, so it has no edge.
/// </summary>
public sealed class MapView : UserControl
{
    private enum PinMode
    {
        None,
        // Connected or connecting: the beacon in the verdict's colour, the country highlighted.
        Active,
        // Off, with the person's own location known: a red beacon there, "your address is visible".
        Home,
        // Off, own location unknown: a quiet ring on the chosen server, so nobody reads "I am in the Netherlands".
        Idle,
    }

    private sealed class DotItem
    {
        public required MapLocation Location { get; init; }
        public required Point Anchor { get; init; }
        public required Panel Hit { get; init; }
        public required Ellipse Dot { get; init; }
        public required Border Cluster { get; init; }
        public required TextBlock Count { get; init; }
        public string? TipKey { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public int Rank { get; init; }
    }

    // A dot is 9 px; the transparent 28 px panel around it takes the pointer.
    private const double HitSize = 28;
    // Dots closer than this on screen merge into one with a number, so their click areas never overlap.
    private const double ClusterRadius = 18;
    private const double PinSize = 120;

    private readonly WorldMap _map = WorldMap.Instance;
    private readonly Canvas _world;
    private readonly Path[] _grids = new Path[3];
    private readonly Path[] _outlines = new Path[3];
    private readonly Panel _countries;
    private readonly Canvas _dots = new() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly Panel _root;
    private readonly Panel _pin;
    private readonly Ellipse _halo;
    private readonly Ellipse _ripple;
    private readonly Border _disc;
    private readonly Border _chip;
    private readonly Border _chipFlagFrame;
    private readonly Image _chipFlag;
    private readonly TextBlock _chipTitle;
    private readonly TextBlock _chipSub;
    private readonly Dictionary<string, List<Path>> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Geometry> _geometry = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DotItem> _dotItems = [];
    private bool _built;
    private Cursor? _handCursor;

    private string? _code;
    private ProtectionState _state;
    private bool _connected;
    private MapPlace? _place;
    private MapPlace? _home;
    private int _delay;
    private PinMode _mode;
    private Point? _point;
    private string? _highlight;
    // Set by the first Focus with a place: the state at launch is shown in place, with no flight and no ripple.
    private bool _focusedOnce;
    private bool _landPending;

    public const double DefaultFocusY = 0.47;
    private double _focusY = DefaultFocusY;

    /// <summary>Where the pin sits, as a fraction of the view (the header covers the top).</summary>
    public double FocusY
    {
        get => _focusY;
        set
        {
            if (Math.Abs(value - _focusY) < 0.002)
            {
                return;
            }
            _focusY = value;
            Apply(animate: false);
        }
    }

    public MapView()
    {
        ClipToBounds = true;

        _world = new Canvas
        {
            Width = _map.Width,
            Height = _map.Height,
            RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        _halo = new Ellipse { Width = 84, Height = 84 };
        _ripple = new Ellipse { Width = PinSize, Height = PinSize, StrokeThickness = 1.5, Fill = Brushes.Transparent, Classes = { "mapripple" } };
        _disc = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8) };
        _pin = new Panel
        {
            Width = PinSize,
            Height = PinSize,
            IsVisible = false,
            Children = { _halo, _ripple, _disc },
        };
        foreach (var c in new Control[] { _halo, _ripple, _disc })
        {
            c.HorizontalAlignment = HorizontalAlignment.Center;
            c.VerticalAlignment = VerticalAlignment.Center;
        }

        _chipFlag = new Image { Width = 20, Height = 15, Stretch = Stretch.UniformToFill };
        _chipFlagFrame = new Border { CornerRadius = new CornerRadius(2.5), ClipToBounds = true, Child = _chipFlag, VerticalAlignment = VerticalAlignment.Center };
        _chipTitle = new TextBlock { Classes = { "mapchiptitle" }, VerticalAlignment = VerticalAlignment.Center };
        _chipSub = new TextBlock { Classes = { "mapchipsub" }, VerticalAlignment = VerticalAlignment.Center };
        _chip = new Border
        {
            Classes = { "mapchip" },
            IsVisible = false,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { _chipFlagFrame, _chipTitle, _chipSub },
            },
        };
        _overlay.Children.Add(_pin);
        _overlay.Children.Add(_chip);

        // The countries are a few thousand vector paths, three times over for the copies left and right.
        // Anything animating above them (a spinner in the header, a toast, the status card) makes the renderer
        // redraw what lies beneath, and redrawing the paths cost two thirds of a core while a connection check
        // spun. Cached as a bitmap, they are drawn again only when the map itself moves or changes colour. The
        // layer is screen-aligned (the zoom sits on the canvas inside it), so the bitmap stays sharp at any zoom.
        // Transparent until Build has added the paths, so the land arrives at once instead of in pieces.
        _countries = new Panel
        {
            ClipToBounds = true,
            Children = { _world },
            CacheMode = new BitmapCache(),
            Opacity = 0,
        };
        _root = new Panel { Children = { _countries, _dots, _overlay } };
        Content = _root;

        SizeChanged += (_, _) => Apply(animate: false);
        // A map taken out of the window (a partner look rebuilt) has no theme to paint in.
        ActualThemeVariantChanged += (_, _) =>
        {
            if (ActualThemeVariant != null)
            {
                Paint();
            }
        };
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
            if (_map.Anchor(location.Code) is not { } anchor)
            {
                continue;
            }
            var tier = Tier(location.BestDelay);
            var dot = new Ellipse { Classes = { "mapdot", tier } };
            var count = new TextBlock();
            var cluster = new Border { Classes = { "mapcluster", tier }, Child = count, IsVisible = false };
            var hit = new Panel
            {
                Width = HitSize,
                Height = HitSize,
                Background = Brushes.Transparent,
                Cursor = _handCursor ??= new Cursor(StandardCursorType.Hand),
                Classes = { "mapdothit" },
                Children = { dot, cluster },
            };
            ToolTip.SetShowDelay(hit, 150);
            var item = new DotItem
            {
                Location = location,
                Anchor = anchor,
                Hit = hit,
                Dot = dot,
                Cluster = cluster,
                Count = count,
                Rank = Rank(location.BestDelay),
            };
            // On release, like a button, and only for the primary button. A merged dot opens its fastest country.
            hit.Tapped += (_, e) =>
            {
                e.Handled = true;
                LocationClicked?.Invoke(item.Location.Code);
            };
            _dots.Children.Add(hit);
            _dotItems.Add(item);
        }
        // The fastest first, so a merged dot is named after the one worth picking.
        _dotItems.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.Location.BestDelay.CompareTo(b.Location.BestDelay));
        PlaceDots();
    }

    /// <summary>
    /// Points the map at the active server. <paramref name="place"/> is its city when known (from its name or a
    /// lookup of the exit address); <paramref name="home"/> is the person's own location, used while V2pn is off.
    /// <paramref name="state"/> colours the pin; <paramref name="connected"/> says whether V2pn is on at all, since a
    /// failed check while connected is red too.
    /// </summary>
    public void Focus(string? countryCode, ProtectionState state, bool connected, MapPlace? place = null, MapPlace? home = null, int delay = 0)
    {
        var oldPoint = _point;
        var oldLook = (_mode, _mode == PinMode.Active ? _state : ProtectionState.Unprotected);
        _code = countryCode?.ToUpperInvariant();
        _state = state;
        _connected = connected;
        _place = place;
        _home = home;
        _delay = delay;
        Resolve();
        var moved = oldPoint != _point;
        var relooked = oldLook != (_mode, _mode == PinMode.Active ? _state : ProtectionState.Unprotected);
        Paint();
        Apply(animate: moved && _focusedOnce);
        if (_focusedOnce && (moved || relooked) && _mode is PinMode.Active or PinMode.Home)
        {
            // A flight lands first; a change in place (the state's colour) shows its waves at once.
            if (_animating)
            {
                _landPending = true;
            }
            else
            {
                Ripple();
            }
        }
        if (_point != null)
        {
            _focusedOnce = true;
        }
    }

    /// <summary>Takes the current colours again, after the Appearance page changed them.</summary>
    public void Repaint() => Paint();

    private bool IsEurope => string.Equals(_code, "EU", StringComparison.OrdinalIgnoreCase);

    private void Resolve()
    {
        // "EU" servers have no single country: the pin stands over Europe and nothing is highlighted.
        Point? server = _place != null ? _map.Project(_place.Lon, _place.Lat)
            : IsEurope ? _map.Project(10, 50)
            : _map.Anchor(_code);
        if (_connected && server != null)
        {
            _mode = PinMode.Active;
            _point = server;
            _highlight = IsEurope ? null : _map.Get(_code)?.Code;
        }
        else if (!_connected && _home != null)
        {
            _mode = PinMode.Home;
            _point = _map.Project(_home.Lon, _home.Lat);
            _highlight = null;
        }
        else if (server != null)
        {
            _mode = PinMode.Idle;
            _point = server;
            _highlight = null;
        }
        else
        {
            _mode = PinMode.None;
            _point = null;
            _highlight = null;
        }
    }

    private void Build()
    {
        if (_built)
        {
            return;
        }
        _built = true;
        foreach (var (code, country) in _map.Countries)
        {
            if (country.Path.IsNullOrEmpty())
            {
                continue;
            }
            try
            {
                _geometry[code] = StreamGeometry.Parse(country.Path);
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"MapView {code}", ex);
            }
        }
        var grid = Graticule();
        // Three copies side by side, sharing the parsed outlines: the middle one and one on each side, so a
        // country near the date line (Japan, Alaska, New Zealand) has land beyond the edge, not empty ocean.
        for (var i = 0; i < 3; i++)
        {
            var copy = new Canvas { Width = _map.Width, Height = _map.Height };
            Canvas.SetLeft(copy, (i - 1) * _map.Width);
            _grids[i] = new Path { Data = grid, IsHitTestVisible = false };
            copy.Children.Add(_grids[i]);
            foreach (var (code, geometry) in _geometry)
            {
                var path = new Path
                {
                    Data = geometry,
                    StrokeThickness = 0.7,
                    StrokeJoin = PenLineJoin.Round,
                    IsHitTestVisible = false,
                };
                if (!_paths.TryGetValue(code, out var list))
                {
                    _paths[code] = list = new List<Path>(3);
                }
                list.Add(path);
                copy.Children.Add(path);
            }
            _outlines[i] = new Path { StrokeJoin = PenLineJoin.Round, Opacity = 0.75, IsHitTestVisible = false };
            copy.Children.Add(_outlines[i]);
            _world.Children.Add(copy);
        }
        Paint();
        Apply(animate: false);
        // Opacity on the cached layer is composited, so the fade does not draw the paths again.
        _countries.Opacity = 1;
        if (Common.Motion.Enabled)
        {
            _ = Common.Motion.Enter(_countries, dy: 0, ms: 200);
        }
    }

    // Meridians every 15° and parallels from 45° S to 75° N: a faint grid on the ocean that gives the map depth.
    private StreamGeometry Graticule()
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        for (var lon = -180; lon < 180; lon += 15)
        {
            var x = _map.Project(lon, 0).X;
            ctx.BeginFigure(new Point(x, 0), false);
            ctx.LineTo(new Point(x, _map.Height));
            ctx.EndFigure(false);
        }
        for (var lat = -45; lat <= 75; lat += 15)
        {
            var y = _map.Project(0, lat).Y;
            ctx.BeginFigure(new Point(0, y), false);
            ctx.LineTo(new Point(_map.Width, y));
            ctx.EndFigure(false);
        }
        return geometry;
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
        foreach (var (code, paths) in _paths)
        {
            var fill = string.Equals(code, _highlight, StringComparison.OrdinalIgnoreCase) ? active : land;
            foreach (var path in paths)
            {
                path.Fill = fill;
                path.Stroke = border;
            }
        }

        var accent = Brush(_mode switch
        {
            PinMode.Home => "V2Unsafe",
            PinMode.Idle => "V2Faint",
            _ => _state switch
            {
                ProtectionState.Protected => "V2Safe",
                ProtectionState.LocalOnly => "V2PingMid",
                _ => "V2Unsafe",
            },
        });
        var outline = _highlight != null && _geometry.TryGetValue(_highlight, out var g) ? g : null;
        var grid = Brush("V2MapGrid");
        for (var i = 0; i < 3; i++)
        {
            if (_outlines[i] != null)
            {
                _outlines[i].Data = outline;
                _outlines[i].Stroke = accent;
            }
            if (_grids[i] != null)
            {
                _grids[i].Stroke = grid;
            }
        }

        var beacon = _mode is PinMode.Active or PinMode.Home;
        var color = (accent as ISolidColorBrush)?.Color ?? Colors.Gray;
        _halo.Fill = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(102, color.R, color.G, color.B), 0),
                new GradientStop(Color.FromArgb(38, color.R, color.G, color.B), 0.35),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
            },
        };
        _halo.IsVisible = beacon;
        _ripple.Stroke = accent;
        _ripple.IsVisible = beacon;
        _disc.Background = beacon ? accent : Brush("V2MapOcean");
        _disc.BorderBrush = beacon ? Brush("V2PinCore") : accent;
        _disc.BorderThickness = new Thickness(beacon ? 2.5 : 2);
        _disc.BoxShadow = beacon && Resource("V2PinShadow") is BoxShadows shadow ? shadow : default;
        _pin.IsVisible = _mode != PinMode.None;
        PaintChip();
        PlaceDots();
        PlacePin();
    }

    private void PaintChip()
    {
        string? title = null, sub = null, flag = null;
        var alert = false;
        if (_mode == PinMode.Active)
        {
            title = _place?.Name ?? (IsEurope ? L.Europe : null);
            sub = _delay > 0 ? L.Ms(_delay) : null;
            flag = _code;
        }
        else if (_mode == PinMode.Home && _home != null)
        {
            title = _home.Name;
            sub = L.MapYourAddress;
            flag = _home.Code;
            alert = true;
        }
        _chip.IsVisible = title.IsNotEmpty();
        if (!_chip.IsVisible)
        {
            return;
        }
        _chipTitle.Text = title;
        _chipSub.Text = sub;
        _chipSub.IsVisible = sub.IsNotEmpty();
        _chipSub.Classes.Set("alert", alert);
        var image = V2pnConverters.Flag(flag);
        _chipFlag.Source = image;
        _chipFlagFrame.IsVisible = image != null;
        _chip.Background = Brush("V2CardBackground");
        _chip.BoxShadow = Resource("V2CardShadow") is BoxShadows shadow ? shadow : default;
        // Measured now, so the first placement already knows whether the chip fits right of the pin.
        _chip.Measure(Size.Infinity);
    }

    // Two waves when the pin lands somewhere new or the protection state changes its colour. Every frame of
    // them redraws the map under the pin, so they run once and stop; nothing on the map moves at rest.
    private void Ripple()
    {
        _ripple.Classes.Remove("play");
        if (!Common.Motion.Enabled || !_pin.IsVisible)
        {
            return;
        }
        // Re-adding the class in a later dispatcher pass starts the animation over.
        Dispatcher.UIThread.Post(() => _ripple.Classes.Add("play"), DispatcherPriority.Background);
    }

    private double Slice(double w, double h)
    {
        // Roughly how many pixels of the 2400-px map fill the view's width: framed to the country's size, so
        // Luxembourg is not a speck and Kazakhstan fits whole.
        if (_mode != PinMode.Active || _highlight == null || _map.Get(_highlight)?.Bounds is not { Length: 4 } b)
        {
            return IsEurope ? 1300 : 1100;
        }
        var bw = b[2] - b[0];
        var bh = b[3] - b[1];
        // Russia's largest landmass crosses the date line, so its bounds span the whole map.
        if (bw > 1200)
        {
            return 1500;
        }
        return Math.Clamp(Math.Max(bw * 2.4, bh * 2.4 * w / h), 560, 1500);
    }

    private void Apply(bool animate)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }
        (double S, double X, double Y) target;
        if (_point is { } p)
        {
            target = (Math.Max(w / Slice(w, h), h / _map.Height), p.X, p.Y);
        }
        else
        {
            target = (Math.Max(w / _map.Width, h / _map.Height) * 1.15, _map.Width * 0.53, _map.Height * 0.38);
        }
        AnimateTo(target, animate, w);
    }

    private (double S, double X, double Y) _from;
    private (double S, double X, double Y) _to;
    // The view as a zoom and the map point in the middle of it: a flight is a straight line in that space.
    private (double S, double X, double Y) _now = (1, 0, 0);
    private double _tx;
    private double _ty;
    private double _dip;
    private TimeSpan _duration;
    private TimeSpan? _animStart;
    private bool _animating;

    // Driven by the compositor's frame clock (RequestAnimationFrame), so the map moves once per frame.
    private void AnimateTo((double S, double X, double Y) target, bool animate, double w)
    {
        var top = TopLevel.GetTopLevel(this);
        if (!animate || !Common.Motion.Enabled || top == null)
        {
            _animating = false;
            if (_landPending)
            {
                _landPending = false;
                Ripple();
            }
            SetMatrix(Wrap(target));
            return;
        }
        // The short way round: from the US to Japan over the Pacific, not back across Eurasia.
        var dx = target.X - _now.X;
        dx -= _map.Width * Math.Round(dx / _map.Width);
        _to = (target.S, _now.X + dx, target.Y);
        _from = _now;
        // A long flight rises (zooms out) in the middle and takes a little longer; a hop next door barely does.
        var distance = Math.Sqrt((dx * dx) + Math.Pow(_to.Y - _from.Y, 2)) * Math.Min(_from.S, _to.S);
        _dip = Math.Min(0.62, distance / (w * 2));
        _duration = TimeSpan.FromMilliseconds(650 + Math.Min(650, distance * 0.32));
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
        var t = Math.Clamp((time - _animStart.Value) / _duration, 0, 1);
        // The map is already on screen and moves, so ease in and out (the same curve as the segment thumbs).
        var k = Common.Motion.InOut.Ease(t);
        var scale = Math.Exp(Lerp(Math.Log(_from.S), Math.Log(_to.S), k)) * (1 - (_dip * Math.Sin(Math.PI * t)));
        SetMatrix((scale, Lerp(_from.X, _to.X, k), Lerp(_from.Y, _to.Y, k)));
        if (t >= 1)
        {
            _animating = false;
            SetMatrix(Wrap(_to));
            if (_landPending)
            {
                _landPending = false;
                Ripple();
            }
            return;
        }
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
    }

    private static double Lerp(double a, double b, double k) => a + ((b - a) * k);

    private (double S, double X, double Y) Wrap((double S, double X, double Y) v) =>
        (v.S, ((v.X % _map.Width) + _map.Width) % _map.Width, v.Y);

    private double _strokeScale;

    private void SetMatrix((double S, double X, double Y) v)
    {
        _now = v;
        var w = Bounds.Width;
        var h = Bounds.Height;
        _tx = (w / 2) - (v.S * v.X);
        var ty = (h * FocusY) - (v.S * v.Y);
        // Never past the top or bottom of the map: the pin moves off its usual height instead.
        var height = _map.Height * v.S;
        _ty = height > h ? Math.Clamp(ty, h - height, 0) : (h - height) / 2;
        _world.RenderTransform = new MatrixTransform(new Matrix(v.S, 0, 0, v.S, _tx, _ty));
        // The grid and the highlight keep a constant on-screen width. Set only on a real change of zoom: each set
        // redraws the cached layer.
        if (Math.Abs(v.S - _strokeScale) > _strokeScale * 0.03)
        {
            _strokeScale = v.S;
            for (var i = 0; i < 3; i++)
            {
                if (_grids[i] != null)
                {
                    _grids[i].StrokeThickness = 1 / v.S;
                    _outlines[i].StrokeThickness = 1.4 / v.S;
                }
            }
        }
        PlaceDots();
        PlacePin();
    }

    // The screen x of a map x on whichever copy of the map lies nearest the middle of the view.
    private double ScreenX(double x)
    {
        var w = Bounds.Width;
        var period = _map.Width * _now.S;
        var sx = (x * _now.S) + _tx;
        return period > 0 ? sx + (period * Math.Round(((w / 2) - sx) / period)) : sx;
    }

    private void PlacePin()
    {
        if (_point is not { } p || _mode == PinMode.None)
        {
            _pin.IsVisible = false;
            _chip.IsVisible = false;
            return;
        }
        var x = ScreenX(p.X);
        var y = (p.Y * _now.S) + _ty;
        Canvas.SetLeft(_pin, x - (PinSize / 2));
        Canvas.SetTop(_pin, y - (PinSize / 2));
        if (_chip.IsVisible)
        {
            var size = _chip.DesiredSize;
            var w = Bounds.Width;
            var left = x + 20;
            if (left + size.Width > w - 8)
            {
                left = Math.Max(8, x - 20 - size.Width);
            }
            Canvas.SetLeft(_chip, left);
            Canvas.SetTop(_chip, y - (size.Height / 2));
        }
    }

    // Dots keep their screen size, so they follow the map by position only. Each dot is centred in its hit
    // area, which is what gets placed. Dots that would overlap merge into the fastest of them, with a count.
    private void PlaceDots()
    {
        var leaders = new List<(DotItem Leader, List<DotItem> Members)>();
        foreach (var item in _dotItems)
        {
            var focused = _mode is PinMode.Active or PinMode.Idle
                          && string.Equals(item.Location.Code, _code, StringComparison.OrdinalIgnoreCase);
            if (focused)
            {
                item.Hit.IsVisible = false;
                continue;
            }
            item.X = ScreenX(item.Anchor.X);
            item.Y = (item.Anchor.Y * _now.S) + _ty;
            var group = leaders.FindIndex(l => Math.Sqrt(Math.Pow(l.Leader.X - item.X, 2) + Math.Pow(l.Leader.Y - item.Y, 2)) < ClusterRadius);
            if (group >= 0)
            {
                leaders[group].Members.Add(item);
                item.Hit.IsVisible = false;
                continue;
            }
            leaders.Add((item, [item]));
        }
        foreach (var (leader, members) in leaders)
        {
            leader.Hit.IsVisible = true;
            Canvas.SetLeft(leader.Hit, leader.X - (HitSize / 2));
            Canvas.SetTop(leader.Hit, leader.Y - (HitSize / 2));
            var merged = members.Count > 1;
            leader.Dot.IsVisible = !merged;
            leader.Cluster.IsVisible = merged;
            var key = string.Join(",", members.Select(m => m.Location.Code));
            if (key == leader.TipKey)
            {
                continue;
            }
            leader.TipKey = key;
            leader.Count.Text = members.Count.ToString(CultureInfo.InvariantCulture);
            ToolTip.SetTip(leader.Hit, string.Join("\n", members.Select(m =>
            {
                var ping = m.Location.BestDelay > 0 ? $" · {L.Ms(m.Location.BestDelay)}" : string.Empty;
                return $"{m.Location.Name} · {L.ServerCount(m.Location.Count)}{ping}";
            })));
        }
    }

    // The same tiers as the ping pills in the list: 0 = never tested, negative = failed.
    private static string Tier(int delay) => delay switch
    {
        0 => "none",
        > 0 and <= 300 => "good",
        > 300 and <= 800 => "mid",
        _ => "bad",
    };

    private static int Rank(int delay) => Tier(delay) switch
    {
        "good" => 0,
        "mid" => 1,
        "bad" => 2,
        _ => 3,
    };

    // Colours come from the theme dictionaries, so the lookup needs the variant this view renders in.
    private object? Resource(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) ? value : null;

    private IBrush Brush(string key) => Resource(key) as IBrush ?? Brushes.Gray;
}
