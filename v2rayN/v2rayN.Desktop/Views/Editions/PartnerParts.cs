using Avalonia.Layout;
using Avalonia.Media.Transformation;

namespace v2rayN.Desktop.Views.EditionLooks;

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

    /// <summary>True when the provider sent a term or traffic: only then is there a plan to show and to renew.</summary>
    public bool HasPlan => DaysLeft != null || Traffic != null;

    /// <summary>The provider's offer first, else its incident, else its panel's announcement.</summary>
    public string? Note => Banner?.Text ?? Incident ?? Announce;

    /// <summary>Where "Renew" leads: the banner's button, else the cabinet.</summary>
    public string? RenewUrl => Banner?.ButtonUrl ?? CabinetUrl;
}

/// <summary>Shared drawing for the partner's card and plan block.</summary>
internal static class Scene
{
    public static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));

    public static TextBlock Text(string text, double size, string? color = null, FontWeight weight = FontWeight.Normal)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (color != null)
        {
            block.Foreground = B(color);
        }
        return block;
    }

    /// <summary>
    /// A button whose fill is its own: V2Button's hover would paint the theme's grey over a partner colour, so the
    /// colour sits on an inner border and hover only dims it.
    /// </summary>
    public static Button Button(Control content, string? fill, string foreground, double radius, Thickness padding, double height = double.NaN)
    {
        var button = new Button
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(radius + 1.5),
            Foreground = B(foreground),
            Height = height,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Classes = { "scene" },
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = new Border
            {
                Background = fill == null ? Brushes.Transparent : B(fill),
                CornerRadius = new CornerRadius(radius),
                Padding = padding,
                Child = content,
            },
        };
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        button.Theme = ButtonTheme;
        return button;
    }

    // Only a presenter: V2Button's own hover, press and disabled fills would paint the theme's grey over the
    // partner's colours. The styles below do hover and press.
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

    /// <summary>Hover, press and the keyboard ring for the buttons above; added once to each root that holds them.</summary>
    public static void AddButtonStyles(StyledElement root, string focus)
    {
        root.Styles.Add(new Style(x => x.OfType<Button>().Class("scene").Class(":pointerover"))
        {
            Setters = { new Setter(Visual.OpacityProperty, 0.86) },
        });
        root.Styles.Add(new Style(x => x.OfType<Button>().Class("scene").Class(":pressed"))
        {
            Setters = { new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("scale(0.97)")) },
        });
        // Keyboard focus gets a ring; a click does not.
        root.Styles.Add(new Style(x => x.OfType<Button>().Class("scene").Class(":focus-visible"))
        {
            Setters = { new Setter(Avalonia.Controls.Primitives.TemplatedControl.BorderBrushProperty, B(focus)) },
        });
    }

    /// <summary>One of V2pn's own icons (a resource key) in <paramref name="color"/>.</summary>
    public static PathIcon Icon(string key, string color, double size = 14) => new()
    {
        Width = size,
        Height = size,
        Data = Application.Current?.FindResource(key) as Geometry,
        Foreground = B(color),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The partner's mark in a rounded square, or the first letter of its name in its colour.</summary>
    public static Control Mark(V2pnPartner partner, string accent, string onAccent, double size)
    {
        var radius = new CornerRadius(Math.Round(size * 0.28));
        if (Picture(partner, partner.Pack.Mark) is { } mark)
        {
            // A cut-out mark (a white glyph, say) vanishes on a ground of its own lightness: it gets a tile that sets it off.
            var tile = CutOutLight(mark) is { } light ? light > 0.55 ? "#18181B" : light < 0.45 ? "#F2F2F4" : null : null;
            return new Border
            {
                Width = size,
                Height = size,
                CornerRadius = radius,
                ClipToBounds = true,
                Background = tile == null ? null : B(tile),
                Padding = new Thickness(tile == null ? 0 : Math.Round(size * 0.16)),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Image { Source = mark, Stretch = tile == null ? Stretch.UniformToFill : Stretch.Uniform },
            };
        }
        var letter = Text(partner.Name[..1].ToUpperInvariant(), size * 0.45, onAccent, FontWeight.SemiBold);
        letter.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = radius,
            Background = B(accent),
            VerticalAlignment = VerticalAlignment.Center,
            Child = letter,
        };
    }

    /// <summary>
    /// For a picture whose corners are see-through (a cut-out), the mean lightness (0 to 1) of its opaque pixels; null
    /// for a picture that brings its own ground, or one that cannot be read.
    /// </summary>
    private static double? CutOutLight(Bitmap bitmap)
    {
        try
        {
            var size = bitmap.PixelSize;
            if (size.Width < 2 || size.Height < 2)
            {
                return null;
            }
            var stride = size.Width * 4;
            var pixels = new byte[stride * size.Height];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, stride);
            }
            finally
            {
                handle.Free();
            }
            // Bgra8888 and Rgba8888 both keep alpha last; the mean of the three channels does for either order.
            int At(int x, int y) => (y * stride) + (x * 4);
            if (new[] { At(0, 0), At(size.Width - 1, 0), At(0, size.Height - 1), At(size.Width - 1, size.Height - 1) }.Any(i => pixels[i + 3] > 24))
            {
                return null;
            }
            double sum = 0;
            var count = 0;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i + 3] > 128)
                {
                    sum += (pixels[i] + pixels[i + 1] + pixels[i + 2]) / (3 * 255.0);
                    count++;
                }
            }
            return count == 0 ? null : sum / count;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn partner mark", ex);
            return null;
        }
    }

    /// <summary>The partner's wide logo for a light or a dark ground, or null when its pack has none that fits.</summary>
    public static Bitmap? Logo(V2pnPartner partner, bool onLight) =>
        Picture(partner, onLight ? partner.Pack.LogoOnLight ?? partner.Pack.Logo : partner.Pack.Logo);

    public static Bitmap? Picture(V2pnPartner partner, string? asset)
    {
        try
        {
            return partner.AssetPath(asset) is { } path && File.Exists(path) ? new Bitmap(path) : null;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn partner picture", ex);
            return null;
        }
    }
}
