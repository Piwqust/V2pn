using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Media.Transformation;
using v2rayN.Desktop.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views.EditionLooks;

/// <summary>A server a scene lists. Delay: 0 not checked, below 0 no answer.</summary>
public sealed record SceneServer(string IndexId, string Name, string? Code, int Delay, bool Selected, string? SubId = null, string? Sub = null, bool Favorite = false);

/// <summary>A subscription with servers, for the scene's filter chips.</summary>
public sealed record SceneSub(string Id, string Name, int Count);

/// <summary>What the partner's panel says about the person's subscription: traffic, term, its note and its links.</summary>
public sealed record SceneAccount(
    string? Traffic,
    double Fraction,
    bool ShowBar,
    string? Expiry,
    bool Warn,
    bool Bad,
    string? Announce,
    string? CabinetUrl,
    string? SupportUrl,
    string? Plan = null,
    string? Devices = null,
    string? Incident = null,
    bool IncidentSevere = false)
{
    public bool IsEmpty => Traffic == null && Expiry == null && Announce == null && CabinetUrl == null && SupportUrl == null
        && Plan == null && Devices == null && Incident == null;
}

/// <summary>
/// What a partner scene shows, read from the main window each time it refreshes: the same verdict, server,
/// mode and route the plain V2pn header shows, so a scene can never say something V2pn does not.
/// </summary>
public sealed record EditionSnapshot
{
    /// <summary>safe, unsafe, warn or checking: the pill's verdict, from V2pn's own state code.</summary>
    public string StateKind { get; init; } = "unsafe";
    public string StateLabel { get; init; } = string.Empty;
    public bool Protected { get; init; }
    public bool HasServer { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string? CountryCode { get; init; }
    public string? Subscription { get; init; }
    public int Ping { get; init; }
    public bool Vpn { get; init; }
    public string Route { get; init; } = string.Empty;
    public int NodesOk { get; init; }
    public int NodesTotal { get; init; }
    public string ConnectLabel { get; init; } = string.Empty;
    public IReadOnlyList<SceneServer> Servers { get; init; } = [];
    /// <summary>Every server V2pn has, in its list order; Selected is not set here, see SelectedId.</summary>
    public IReadOnlyList<SceneServer> Catalog { get; init; } = [];
    public IReadOnlyList<SceneSub> Subs { get; init; } = [];
    public string? SelectedId { get; init; }
    /// <summary>False before the first server: the scene shows how to add one instead of the list.</summary>
    public bool AnyServers { get; init; }
    /// <summary>Other VPN apps with servers were found; the empty state offers the move.</summary>
    public bool CanImport { get; init; }

    /// <summary>The open partner's name, and its subscriptions: the list starts with their servers.</summary>
    public string? PartnerName { get; init; }
    public IReadOnlyList<string> PartnerSubIds { get; init; } = [];

    /// <summary>The server in use comes from another provider; the look says so and offers the partner's best one.</summary>
    public bool ForeignServer { get; init; }
    public string? BestPartnerServer { get; init; }

    /// <summary>The partner subscription's traffic, term and links; null when the panel sent none.</summary>
    public SceneAccount? Account { get; init; }

    /// <summary>The node counts are the partner's own checks of its servers, not V2pn's pings.</summary>
    public bool NodesByPartner { get; init; }
}

/// <summary>What a scene's controls do: the plain window's own actions, so behaviour never forks.</summary>
public interface IEditionActions
{
    void ToggleConnect();
    void SetVpn(bool vpn);
    void OpenRoute(Control anchor);
    void Select(string indexId);
    void PingAll();
    void UpdateSubscriptions();
    /// <summary>The window's "+" menu: a link, a subscription, a QR code, by hand.</summary>
    void Add(Control anchor);
    void Paste();
    void AddSubscription();
    void Import();
    void OpenSettings();
    void OpenIp();
    void OpenUrl(string url);
}

/// <summary>Shared drawing for the partner scenes.</summary>
internal static class Scene
{
    public static readonly FontFamily Geist = new("avares://V2pn/Assets/Fonts#Geist, avares://V2pn/Assets/Fonts#Inter");
    public static readonly FontFamily Mono = new("avares://V2pn/Assets/Fonts#Geist Mono, Consolas, monospace");
    public static readonly FontFamily Inter = new("avares://V2pn/Assets/Fonts#Inter");

    /// <summary>The look being built: set by the window before it makes a scene, read by the helpers below.</summary>
    public static SceneLook Current { get; set; } = new(true, "#FF7A2F", "round", "round", "geist");

    /// <summary>Near-black labels on a light accent, white on a dark one.</summary>
    public static string OnAccent(string accent)
    {
        var c = Color.Parse(accent);
        var luma = (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);
        return luma > 140 ? "#170B03" : "#FFFFFF";
    }

    /// <summary>The accent with an alpha in front, "#59FF7A2F" for "59".</summary>
    public static string Alpha(string alpha, string color) => "#" + alpha + color.TrimStart('#')[^6..];

    public static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));

    public static TextBlock Text(string text, double size, string color, FontFamily? font = null, FontWeight weight = FontWeight.Normal) => new()
    {
        Text = text,
        FontSize = size,
        FontFamily = font ?? Current.Text,
        FontWeight = weight,
        Foreground = B(color),
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// A button whose fill is its own: V2Button's hover would paint the theme's grey over a partner colour, so the
    /// colour sits on an inner border and hover only dims it.
    /// </summary>
    public static Button Button(Control content, string? fill, string foreground, double radius, Thickness padding, string? border = null, double height = double.NaN)
    {
        var button = new Button
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(Current.Radius(radius) + 1.5),
            Foreground = B(foreground),
            Height = height,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Classes = { "scene" },
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = new Border
            {
                Background = fill == null ? Brushes.Transparent : B(fill),
                BorderBrush = border == null ? null : B(border),
                BorderThickness = new Thickness(border == null ? 0 : 1),
                CornerRadius = new CornerRadius(Current.Radius(radius)),
                Padding = padding,
                Child = content,
            },
        };
        // A label sits in the middle of its button; a row or a card fills it.
        if (content is TextBlock or StackPanel)
        {
            if (content.HorizontalAlignment == HorizontalAlignment.Stretch)
            {
                content.HorizontalAlignment = HorizontalAlignment.Center;
            }
            content.VerticalAlignment = VerticalAlignment.Center;
        }
        else if (content is Grid)
        {
            content.VerticalAlignment = VerticalAlignment.Center;
        }
        button.Theme = ButtonTheme;
        return button;
    }

    // Only a presenter: V2Button's own hover, press and disabled fills would paint the theme's grey over the
    // partner's colours (a plain button then read as disabled). The scene's styles do hover and press.
    private static ControlTheme? _buttonTheme;

    private static ControlTheme ButtonTheme => _buttonTheme ??= new ControlTheme(typeof(Button))
    {
        Setters =
        {
            new Setter(Avalonia.Controls.Primitives.TemplatedControl.TemplateProperty, new Avalonia.Controls.Templates.FuncControlTemplate<Button>((b, _) => new Avalonia.Controls.Presenters.ContentPresenter
            {
                Name = "PART_ContentPresenter",
                Background = Brushes.Transparent,
                [!Avalonia.Controls.Presenters.ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty],
                [!Avalonia.Controls.Presenters.ContentPresenter.PaddingProperty] = b[!Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty],
                [!Avalonia.Controls.Presenters.ContentPresenter.CornerRadiusProperty] = b[!Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty],
                [!Avalonia.Controls.Presenters.ContentPresenter.BorderBrushProperty] = b[!Avalonia.Controls.Primitives.TemplatedControl.BorderBrushProperty],
                [!Avalonia.Controls.Presenters.ContentPresenter.BorderThicknessProperty] = b[!Avalonia.Controls.Primitives.TemplatedControl.BorderThicknessProperty],
                [!Avalonia.Controls.Presenters.ContentPresenter.HorizontalContentAlignmentProperty] = b[!ContentControl.HorizontalContentAlignmentProperty],
                [!Avalonia.Controls.Presenters.ContentPresenter.VerticalContentAlignmentProperty] = b[!ContentControl.VerticalContentAlignmentProperty],
            })),
            new Setter(InputElement.FocusableProperty, true),
        },
    };

    /// <summary>Hover and press for scene buttons; added once to each scene root.</summary>
    public static void AddButtonStyles(StyledElement root)
    {
        root.Styles.Add(new Style(x => x.OfType<Button>().Class("scene").Class(":pointerover"))
        {
            Setters = { new Setter(Visual.OpacityProperty, 0.86) },
        });
        root.Styles.Add(new Style(x => x.OfType<Button>().Class("scene").Class(":pressed"))
        {
            Setters = { new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("scale(0.97)")) },
        });
        root.Styles.Add(new Style(x => x.OfType<Button>().Class("scene").Class(":disabled"))
        {
            Setters = { new Setter(Visual.OpacityProperty, 0.4) },
        });
        // Keyboard focus gets a ring; a click does not.
        root.Styles.Add(new Style(x => x.OfType<Button>().Class("scene").Class(":focus-visible"))
        {
            Setters = { new Setter(Avalonia.Controls.Primitives.TemplatedControl.BorderBrushProperty, B(Current.Accent)) },
        });
    }

    public static Avalonia.Controls.Shapes.Path Power(string color, double size = 15) => new()
    {
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        Stroke = B(color),
        StrokeThickness = 2.2,
        StrokeLineCap = PenLineCap.Round,
        Data = StreamGeometry.Parse("M12 3 L12 11 M6.3 7.2 A8 8 0 1 0 17.7 7.2"),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>V2pn's state pill, the one part of the window every look keeps as it is.</summary>
    public static Border Pill(TextBlock label, bool light)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(11, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children = { new Ellipse { Width = 6, Height = 6 }, label },
            },
            Tag = light,
        };
    }

    public static void SetPill(Border pill, TextBlock label, string kind, string text)
    {
        var light = pill.Tag is true;
        var color = kind switch
        {
            "safe" => light ? "#16804F" : "#3DD68C",
            "warn" => light ? "#B07508" : "#F2B84B",
            "checking" => light ? "#6A6862" : "#9B9BA8",
            _ => light ? "#C8381F" : "#FF5A3D",
        };
        var c = Color.Parse(color);
        pill.Background = new SolidColorBrush(Color.FromArgb(0x22, c.R, c.G, c.B));
        label.Text = text.ToUpper(CultureInfo.CurrentUICulture);
        label.Foreground = new SolidColorBrush(c);
        label.FontFamily = Inter;
        label.FontSize = 10.5;
        label.FontWeight = FontWeight.SemiBold;
        label.LetterSpacing = 0.8;
        if (pill.Child is StackPanel { Children: [Ellipse dot, ..] })
        {
            dot.Fill = new SolidColorBrush(c);
        }
    }

    /// <summary>The globe V2pn's list shows for a server without a country.</summary>
    public static PathIcon Globe(string color, double size) => new()
    {
        Width = size,
        Height = size,
        Data = Application.Current?.FindResource("SemiIconGlobeStroke") as Geometry,
        Foreground = B(color),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false,
    };

    /// <summary>True when the picture's four corners are (almost) transparent: a cut-out, not a photo on its own ground.</summary>
    public static bool HasTransparentCorners(Bitmap bitmap)
    {
        try
        {
            var size = bitmap.PixelSize;
            if (size.Width < 2 || size.Height < 2)
            {
                return true;
            }
            var pixel = new byte[4];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixel, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                foreach (var (x, y) in new[] { (0, 0), (size.Width - 1, 0), (0, size.Height - 1), (size.Width - 1, size.Height - 1) })
                {
                    bitmap.CopyPixels(new PixelRect(x, y, 1, 1), handle.AddrOfPinnedObject(), 4, 4);
                    // Bgra8888 and Rgba8888 both keep alpha last.
                    if (pixel[3] > 24)
                    {
                        return false;
                    }
                }
            }
            finally
            {
                handle.Free();
            }
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("EditionScene", ex);
            return false;
        }
    }

    public static Image Flag(string? code, double width, double height) => new()
    {
        Source = V2pnConverters.Flag(code),
        Width = width,
        Height = height,
        Stretch = Stretch.UniformToFill,
    };

    public static Bitmap? Picture(V2pnPartner partner, string? asset)
    {
        try
        {
            return partner.AssetPath(asset) is { } path && File.Exists(path) ? new Bitmap(path) : null;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("EditionScene", ex);
            return null;
        }
    }
}

/// <summary>One square per server: lit when it answered the last ping, dim when it did not.</summary>
public sealed class NodeMeter : Control
{
    private int _ok;
    private int _total;
    private readonly double _cell;
    private readonly IBrush _on;
    private readonly IBrush _off;
    private const int Max = 16;

    public NodeMeter(double cell, string on, string off)
    {
        _cell = cell;
        _on = Scene.B(on);
        _off = Scene.B(off);
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Left;
    }

    public void Set(int ok, int total)
    {
        if (ok == _ok && total == _total)
        {
            return;
        }
        (_ok, _total) = (ok, total);
        InvalidateMeasure();
        InvalidateVisual();
    }

    private int Squares => Math.Min(Max, Math.Max(_total, 1));

    protected override Size MeasureOverride(Size availableSize) => new(Squares * (_cell + _cell * 0.4) - _cell * 0.4, _cell);

    public override void Render(DrawingContext context)
    {
        // Past Max squares each one stands for several servers, lit in proportion.
        var lit = _total <= 0 ? 0 : (int)Math.Round(_ok * (double)Squares / _total);
        for (var i = 0; i < Squares; i++)
        {
            var x = i * (_cell * 1.4);
            context.DrawRectangle(i < lit ? _on : _off, null, new Rect(x, 0, _cell, _cell), _cell * 0.18, _cell * 0.18);
        }
    }
}

/// <summary>A field of small dots, the ground of a partner's website look.</summary>
public sealed class DotField : Control
{
    private readonly IBrush _dot;
    private readonly double _step;

    public DotField(string color, double step)
    {
        _dot = Scene.B(color);
        _step = step;
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext context)
    {
        for (var y = _step / 2; y < Bounds.Height; y += _step)
        {
            for (var x = _step / 2; x < Bounds.Width; x += _step)
            {
                context.DrawEllipse(_dot, null, new Point(x, y), 0.9, 0.9);
            }
        }
    }
}

/// <summary>
/// Europe (or wherever the server is) typed in characters from V2pn's own country outlines: denser land is a
/// heavier character, the server is @, the partner's other countries are *. Built once per centre country.
/// </summary>
internal static class AsciiMap
{
    private const int Columns = 70;
    private const int Rows = 30;
    private const string Ramp = " .:+#";
    private static readonly Dictionary<string, string[]> Cache = [];

    public static string[] Lines(string centre)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(centre, out var cached))
            {
                return cached;
            }
            var lines = Build(centre);
            Cache[centre] = lines;
            return lines;
        }
    }

    private static string[] Build(string centre)
    {
        var map = WorldMap.Instance;
        var middle = map.Get(centre) ?? map.Get("DE");
        var grid = new char[Rows][];
        for (var r = 0; r < Rows; r++)
        {
            grid[r] = Enumerable.Repeat(' ', Columns).ToArray();
        }
        if (middle == null || map.Width <= 0)
        {
            return grid.Select(r => new string(r)).ToArray();
        }

        // A character is about twice as tall as it is wide.
        var width = map.Width * 0.15;
        var height = width * Rows * 2.0 / Columns;
        var left = middle.X - width * 0.5;
        var top = middle.Y - height * 0.5;
        var view = new Rect(left, top, width, height);

        var shapes = new List<Geometry>();
        foreach (var country in map.Countries.Values)
        {
            if (country.Bounds.Length == 4 && !view.Intersects(new Rect(country.Bounds[0], country.Bounds[1], country.Bounds[2] - country.Bounds[0], country.Bounds[3] - country.Bounds[1])))
            {
                continue;
            }
            try
            {
                shapes.Add(StreamGeometry.Parse(country.Path));
            }
            catch (Exception)
            {
                // A country whose outline does not parse is left as sea.
            }
        }

        for (var r = 0; r < Rows; r++)
        {
            for (var c = 0; c < Columns; c++)
            {
                var hits = 0;
                foreach (var (dx, dy) in new[] { (0.25, 0.25), (0.75, 0.25), (0.25, 0.75), (0.75, 0.75) })
                {
                    var p = new Point(left + (c + dx) * width / Columns, top + (r + dy) * height / Rows);
                    if (shapes.Any(s => s.Bounds.Contains(p) && s.FillContains(p)))
                    {
                        hits++;
                    }
                }
                grid[r][c] = Ramp[hits];
            }
        }
        return grid.Select(r => new string(r)).ToArray();
    }

    /// <summary>Where a country's centre falls on the grid of <paramref name="centre"/>; null when off it.</summary>
    public static (int Row, int Column)? Place(string centre, string? code)
    {
        var map = WorldMap.Instance;
        var middle = map.Get(centre) ?? map.Get("DE");
        var country = map.Get(code);
        if (middle == null || country == null || map.Width <= 0)
        {
            return null;
        }
        var width = map.Width * 0.15;
        var height = width * Rows * 2.0 / Columns;
        var c = (int)((country.X - (middle.X - width * 0.5)) / width * Columns);
        var r = (int)((country.Y - (middle.Y - height * 0.5)) / height * Rows);
        return c is >= 0 and < Columns && r is >= 0 and < Rows ? (r, c) : null;
    }
}
