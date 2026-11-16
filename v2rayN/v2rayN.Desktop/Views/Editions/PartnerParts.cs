using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Media.Transformation;
using v2rayN.Desktop.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views.EditionLooks;

/// <summary>A server a scene lists. Delay: 0 not checked, below 0 no answer.</summary>
/// <param name="Group">The partner's group for it (pack or API), <paramref name="Detail"/> its purposes and note in words.</param>
public sealed record SceneServer(string IndexId, string Name, string? Code, int Delay, bool Selected, string? SubId = null, string? Sub = null, bool Favorite = false,
    string? Group = null, string? Detail = null);

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
    bool IncidentSevere = false,
    string? AnnounceUrl = null,
    SubBanner? Banner = null,
    int? DaysLeft = null)
{
    public bool IsEmpty => Traffic == null && Expiry == null && Announce == null && CabinetUrl == null && SupportUrl == null
        && Plan == null && Devices == null && Incident == null && Banner == null;
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

    /// <summary>The partner's buttons (at most four), shown in the look's subscription block.</summary>
    public IReadOnlyList<V2pnPartnerAction> Actions { get; init; } = [];

    /// <summary>The button running now; the others wait greyed out.</summary>
    public string? ActionRunning { get; init; }
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
    /// <summary>One of the partner's buttons (V2pnPartnerAction.Id).</summary>
    void RunAction(string id, Control anchor);
}

/// <summary>Shared drawing for the partner scenes.</summary>
internal static class Scene
{
    public static readonly FontFamily Geist = new("avares://V2pn/Assets/Fonts#Geist, avares://V2pn/Assets/Fonts#Inter");
    public static readonly FontFamily Mono = new("avares://V2pn/Assets/Fonts#Geist Mono, Consolas, monospace");
    public static readonly FontFamily Inter = new("avares://V2pn/Assets/Fonts#Inter");

    /// <summary>The look being built: set by the window before it makes a scene, read by the helpers below.</summary>
    public static SceneLook Current { get; set; } = new(true, "#FF7A2F", "round", "round", "geist");

    /// <summary>Labels on an accent fill: near-black or white, whichever reads better (WCAG contrast, not a guess).</summary>
    public static string OnAccent(string accent) =>
        Contrast("#170B03", accent) >= Contrast("#FFFFFF", accent) ? "#170B03" : "#FFFFFF";

    /// <summary>WCAG 2 contrast ratio of two opaque colours, 1 to 21.</summary>
    public static double Contrast(string a, string b)
    {
        static double Luminance(Color c)
        {
            static double Channel(byte v)
            {
                var x = v / 255.0;
                return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
            }
            return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
        }
        var (la, lb) = (Luminance(Color.Parse(a)), Luminance(Color.Parse(b)));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>
    /// A colour used for text on <paramref name="ground"/>, made just dark (or light) enough to read: kept as it is when
    /// it reaches <paramref name="min"/> already, else its lightness moves away from the ground's step by step. So a
    /// partner's orange stays orange on dark grounds and turns burnt orange on light ones instead of failing there.
    /// </summary>
    public static string Readable(string color, string ground, double min = 4.5)
    {
        if (Contrast(color, ground) >= min)
        {
            return color;
        }
        var hsl = Color.Parse(color).ToHsl();
        var darker = Contrast("#000000", ground) >= Contrast("#FFFFFF", ground);
        for (var step = 1; step <= 40; step++)
        {
            var l = Math.Clamp(hsl.L + ((darker ? -1 : 1) * step * 0.02), 0, 1);
            var c = HslColor.ToRgb(hsl.H, hsl.S, l, 1);
            var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            if (Contrast(hex, ground) >= min)
            {
                return hex;
            }
        }
        return darker ? "#000000" : "#FFFFFF";
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
        // A long verdict ("couldn't start the connection") ends in "…" where the place is narrow, with the whole of it as a tip.
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        var row = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 7 };
        row.Children.Add(new Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        return new Border
        {
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(11, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = row,
            Tag = light,
        };
    }

    public static void SetPill(Border pill, TextBlock label, string kind, string text)
    {
        var light = pill.Tag is true;
        var color = kind switch
        {
            "safe" => light ? "#16804F" : "#3DD68C",
            "warn" => light ? "#7A5000" : "#F2B84B",
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
        if (pill.Child is Panel { Children: [Ellipse dot, ..] })
        {
            dot.Fill = new SolidColorBrush(c);
        }
        ToolTip.SetTip(pill, text);
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
