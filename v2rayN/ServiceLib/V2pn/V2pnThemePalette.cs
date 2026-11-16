namespace ServiceLib;

/// <summary>
/// An opaque sRGB colour and the maths provider themes need: WCAG contrast, HSL, OKLCH hue, mixing. Pure, with no
/// UI types, so the app, the tests and the provider tools derive exactly the same colours.
/// </summary>
public readonly record struct V2pnColor(byte R, byte G, byte B)
{
    private static readonly Regex HexColour = new("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);

    public static readonly V2pnColor White = new(255, 255, 255);
    public static readonly V2pnColor Black = new(0, 0, 0);

    /// <summary>A "#RRGGBB" colour; null for anything else (no names, no alpha, no short form).</summary>
    public static V2pnColor? TryParse(string? hex)
    {
        if (hex == null || !HexColour.IsMatch(hex))
        {
            return null;
        }
        var v = Convert.ToInt32(hex[1..], 16);
        return new V2pnColor((byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
    }

    public static V2pnColor Parse(string hex) => TryParse(hex) ?? throw new FormatException($"Not a #RRGGBB colour: {hex}");

    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>"#AARRGGBB" with <paramref name="alpha"/> (0 to 1) in front.</summary>
    public string WithAlpha(double alpha) => $"#{(byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255):X2}{R:X2}{G:X2}{B:X2}";

    public override string ToString() => Hex;

    private static double Linear(byte v)
    {
        var x = v / 255.0;
        return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
    }

    /// <summary>WCAG 2 relative luminance, 0 to 1.</summary>
    public double Luminance => (0.2126 * Linear(R)) + (0.7152 * Linear(G)) + (0.0722 * Linear(B));

    /// <summary>WCAG 2 contrast ratio, 1 to 21.</summary>
    public static double Contrast(V2pnColor a, V2pnColor b)
    {
        var (la, lb) = (a.Luminance, b.Luminance);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public (double H, double S, double L) Hsl
    {
        get
        {
            var (r, g, b) = (R / 255.0, G / 255.0, B / 255.0);
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var l = (max + min) / 2;
            if (max - min < 1e-9)
            {
                return (0, 0, l);
            }
            var d = max - min;
            var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            double h;
            if (max == r)
            {
                h = ((g - b) / d) + (g < b ? 6 : 0);
            }
            else if (max == g)
            {
                h = ((b - r) / d) + 2;
            }
            else
            {
                h = ((r - g) / d) + 4;
            }
            return (h * 60, s, l);
        }
    }

    public static V2pnColor FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 360;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        if (s < 1e-9)
        {
            var grey = (byte)Math.Round(l * 255);
            return new V2pnColor(grey, grey, grey);
        }
        var q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
        var p = (2 * l) - q;
        static double Channel(double p, double q, double t)
        {
            t = t < 0 ? t + 1 : t > 1 ? t - 1 : t;
            return t < 1.0 / 6 ? p + ((q - p) * 6 * t)
                : t < 0.5 ? q
                : t < 2.0 / 3 ? p + ((q - p) * ((2.0 / 3) - t) * 6)
                : p;
        }
        static byte Byte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return new V2pnColor(Byte(Channel(p, q, h + (1.0 / 3))), Byte(Channel(p, q, h)), Byte(Channel(p, q, h - (1.0 / 3))));
    }

    /// <summary>OKLCH chroma and hue in degrees: hue as the eye sees it, for telling a brand colour from a state colour.</summary>
    public (double Chroma, double Hue) Oklch
    {
        get
        {
            var (_, a, bb) = OkLab();
            var hue = Math.Atan2(bb, a) * 180 / Math.PI;
            return (Math.Sqrt((a * a) + (bb * bb)), hue < 0 ? hue + 360 : hue);
        }
    }

    /// <summary>OKLCH lightness, 0 to 1: lightness as the eye sees it, the same step for every hue.</summary>
    public double OkLightness => OkLab().L;

    private (double L, double A, double B) OkLab()
    {
        var (r, g, b) = (Linear(R), Linear(G), Linear(B));
        var l = Math.Cbrt((0.4122214708 * r) + (0.5363325363 * g) + (0.0514459929 * b));
        var m = Math.Cbrt((0.2119034982 * r) + (0.6806995451 * g) + (0.1073969566 * b));
        var s = Math.Cbrt((0.0883024619 * r) + (0.2817188376 * g) + (0.6299787005 * b));
        return ((0.2104542553 * l) + (0.7936177850 * m) - (0.0040720468 * s),
            (1.9779984951 * l) - (2.4285922050 * m) + (0.4505937099 * s),
            (0.0259040371 * l) + (0.7827717662 * m) - (0.8086757660 * s));
    }

    /// <summary>
    /// The colour of OKLCH <paramref name="lightness"/>, <paramref name="chroma"/> and <paramref name="hue"/> (degrees);
    /// outside sRGB the chroma is lowered until it fits, so the lightness and the hue stay as asked.
    /// </summary>
    public static V2pnColor FromOklch(double lightness, double chroma, double hue)
    {
        var l = Math.Clamp(lightness, 0, 1);
        var h = hue * Math.PI / 180;
        for (var c = Math.Max(0, chroma); ; c *= 0.96)
        {
            var (a, b) = (c * Math.Cos(h), c * Math.Sin(h));
            var l3 = Math.Pow(l + (0.3963377774 * a) + (0.2158037573 * b), 3);
            var m3 = Math.Pow(l - (0.1055613458 * a) - (0.0638541728 * b), 3);
            var s3 = Math.Pow(l - (0.0894841775 * a) - (1.2914855480 * b), 3);
            var red = (4.0767416621 * l3) - (3.3077115913 * m3) + (0.2309699292 * s3);
            var green = (-1.2684380046 * l3) + (2.6097574011 * m3) - (0.3413193965 * s3);
            var blue = (-0.0041960863 * l3) - (0.7034186147 * m3) + (1.7076147010 * s3);
            const double Slack = 0.0005;
            if (c < 0.0005 || (red is >= -Slack and <= 1 + Slack && green is >= -Slack and <= 1 + Slack && blue is >= -Slack and <= 1 + Slack))
            {
                return new V2pnColor(Encode(red), Encode(green), Encode(blue));
            }
        }

        static byte Encode(double linear)
        {
            var x = Math.Clamp(linear, 0, 1);
            var v = x <= 0.0031308 ? x * 12.92 : (1.055 * Math.Pow(x, 1 / 2.4)) - 0.055;
            return (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        }
    }

    /// <summary>This colour with its OKLCH lightness set to <paramref name="lightness"/>, chroma and hue kept where sRGB allows.</summary>
    public V2pnColor WithOkLightness(double lightness)
    {
        var (chroma, hue) = Oklch;
        return FromOklch(lightness, chroma, hue);
    }

    /// <summary>This colour moved <paramref name="t"/> (0 to 1) of the way to <paramref name="other"/>, channel by channel.</summary>
    public V2pnColor Mix(V2pnColor other, double t)
    {
        static byte Lerp(byte a, byte b, double t) => (byte)Math.Round(a + ((b - a) * Math.Clamp(t, 0, 1)));
        return new V2pnColor(Lerp(R, other.R, t), Lerp(G, other.G, t), Lerp(B, other.B, t));
    }

    /// <summary>
    /// What the eye sees where this colour lies with <paramref name="alpha"/> over <paramref name="below"/> (the way the
    /// renderer blends: in sRGB values).
    /// </summary>
    public V2pnColor Over(V2pnColor below, double alpha) => below.Mix(this, alpha);

    public V2pnColor WithLightness(double l)
    {
        var (h, s, _) = Hsl;
        return FromHsl(h, s, l);
    }
}

/// <summary>
/// Where the theme's background shows: the area each template paints with the provider's colour, gradient or picture,
/// and the colours that read on it in every spot (WCAG AA against the lightest and darkest point of it).
/// </summary>
/// <param name="Kind">solid, gradient or image (V2pnThemeBackgroundKinds).</param>
/// <param name="Colors">The fill: one colour, or the gradient's 2 to 4 stops; under a picture the colour shown while it loads.</param>
/// <param name="Scrim">For a picture: "#AARRGGBB" laid over it so the text reads whatever the picture holds.</param>
/// <param name="Ink">Text on the area (4.5:1 everywhere on it).</param>
/// <param name="Soft">Secondary text on the area (4.5:1 too).</param>
/// <param name="Action">The main control's fill on the area (Connect).</param>
/// <param name="OnAction">The label on <paramref name="Action"/>.</param>
/// <param name="Glass">"#AARRGGBB": a panel on the area; text on it uses the theme's Ink, Soft and Muted.</param>
public sealed record V2pnThemeArea(string Kind, IReadOnlyList<string> Colors, string? Scrim, string Ink, string Soft, string Action, string OnAction, string Glass);

/// <summary>
/// Every colour of a provider theme in one mode (light or dark), derived from its 1 to 3 brand colours.
/// The provider never sets these: V2pn derives them and guarantees WCAG AA (4.5:1 for text, 3:1 for controls), turns a
/// brand colour away from the state hues (protected green, attention amber, unprotected red), and darkens or lightens
/// the brand where it becomes text or a ground for text.
/// </summary>
public sealed record V2pnThemeColors(
    bool Dark,
    string Ground,
    string Surface,
    string Raised,
    string Line,
    string Ink,
    string Soft,
    string Muted,
    string Accent,
    string OnAccent,
    string AccentText,
    string Secondary,
    V2pnThemeArea Area,
    string Good,
    string Warn,
    string Bad,
    double NeutralHue,
    double NeutralSaturation);

/// <summary>
/// The stage of V2pn's window in a partner's background (V2pnThemePalette.Stage): where V2pn's map would be, and what
/// reads on it. State colours are never part of it.
/// </summary>
/// <param name="Kind">gradient or image (V2pnThemeBackgroundKinds).</param>
/// <param name="Stops">The gradient's stops, "#RRGGBB", drawn at 135°; for a picture the colour shown while it loads.</param>
/// <param name="Art">The picture (a pack asset) for an image stage.</param>
/// <param name="Scrim">"#AARRGGBB" over the whole stage so its text reads; null when none is needed.</param>
/// <param name="Ink">Text on the stage, 4.5:1 at its worst point.</param>
/// <param name="Soft">Secondary text, 4.5:1 too.</param>
/// <param name="Ghost">"#AARRGGBB": the fill of a quiet chip or button with <paramref name="Ink"/> on it.</param>
/// <param name="Solid">The fill of the one strong button ("Renew").</param>
/// <param name="OnSolid">The label on <paramref name="Solid"/>.</param>
public sealed record V2pnPartnerStage(string Kind, IReadOnlyList<string> Stops, string? Art, string? Scrim, string Ink, string Soft, string Ghost, string Solid, string OnSolid);

public static class V2pnThemePalette
{
    public const double TextContrast = 4.5;
    public const double UiContrast = 3.0;

    /// <summary>The brand colour of a theme that names none (and whose pack has no accent): a calm blue, not V2pn's red.</summary>
    public const string FallbackBrand = "#3B6CF6";

    // V2pn's state colours: what a brand colour must never pass for.
    private static readonly (V2pnColor Colour, double Near, double Clear)[] StateHues =
    [
        (V2pnColor.Parse("#3DD68C"), 20, 25), // protected
        (V2pnColor.Parse("#F2B84B"), 20, 25), // attention
        // Unprotected red sits next to every orange and crimson; only the red itself is taken.
        (V2pnColor.Parse("#FF5A3D"), 8, 12),
    ];

    private static readonly (string Good, string Warn, string Bad) DarkStates = ("#3DD68C", "#F2B84B", "#FF5A3D");
    private static readonly (string Good, string Warn, string Bad) LightStates = ("#16804F", "#8A5A00", "#C8381F");

    /// <summary>True when a coloured <paramref name="colour"/> could pass for one of V2pn's state colours.</summary>
    public static bool NearState(V2pnColor colour) => StateHues.Any(s => HueWithin(colour, s.Colour, s.Near));

    /// <summary>
    /// The brand colour turned just far enough from the state hues that a button in it never reads as "protected",
    /// "attention" or "unprotected"; a colour already clear of them (or a grey) comes back as it is.
    /// </summary>
    public static V2pnColor AwayFromState(V2pnColor colour)
    {
        if (!NearState(colour))
        {
            return colour;
        }
        var (h, s, l) = colour.Hsl;
        for (var step = 3; step <= 120; step += 3)
        {
            foreach (var sign in new[] { -1, 1 })
            {
                var turned = V2pnColor.FromHsl(h + (sign * step), s, l);
                if (!StateHues.Any(x => HueWithin(turned, x.Colour, x.Clear)))
                {
                    return turned;
                }
            }
        }
        return colour;
    }

    private static bool HueWithin(V2pnColor colour, V2pnColor state, double degrees)
    {
        var (chroma, hue) = colour.Oklch;
        if (chroma < 0.04)
        {
            return false;
        }
        var diff = Math.Abs(hue - state.Oklch.Hue) % 360;
        return Math.Min(diff, 360 - diff) <= degrees;
    }

    /// <summary>
    /// <paramref name="colour"/> with its lightness moved, step by step, until it reaches <paramref name="min"/> against
    /// every one of <paramref name="grounds"/>; kept as it is when it already does. Darker on light grounds, lighter on dark.
    /// </summary>
    public static V2pnColor Readable(V2pnColor colour, IReadOnlyList<V2pnColor> grounds, double min = TextContrast)
    {
        if (grounds.All(g => V2pnColor.Contrast(colour, g) >= min))
        {
            return colour;
        }
        var darker = grounds.Average(g => g.Luminance) > 0.18;
        var (h, s, l) = colour.Hsl;
        for (var step = 1; step <= 100; step++)
        {
            var candidate = V2pnColor.FromHsl(h, s, l + ((darker ? -1 : 1) * step * 0.01));
            if (grounds.All(g => V2pnColor.Contrast(candidate, g) >= min))
            {
                return candidate;
            }
        }
        return darker ? V2pnColor.Black : V2pnColor.White;
    }

    public static V2pnColor Readable(V2pnColor colour, V2pnColor ground, double min = TextContrast) => Readable(colour, [ground], min);

    /// <summary>
    /// <paramref name="colour"/> as text on <paramref name="ground"/>: only its OKLCH lightness moves, in steps of 0.01,
    /// until it reaches <paramref name="min"/>; the hue and the chroma stay, so a brand's links still look like the brand.
    /// </summary>
    public static V2pnColor ReadableOn(V2pnColor colour, V2pnColor ground, double min = TextContrast)
    {
        if (V2pnColor.Contrast(colour, ground) >= min)
        {
            return colour;
        }
        var darker = V2pnColor.Contrast(V2pnColor.Black, ground) >= V2pnColor.Contrast(V2pnColor.White, ground);
        var (chroma, hue) = colour.Oklch;
        var start = colour.OkLightness;
        for (var step = 1; step <= 100; step++)
        {
            var candidate = V2pnColor.FromOklch(start + ((darker ? -1 : 1) * step * 0.01), chroma, hue);
            if (V2pnColor.Contrast(candidate, ground) >= min)
            {
                return candidate;
            }
        }
        return darker ? V2pnColor.Black : V2pnColor.White;
    }

    private static readonly V2pnColor StageDarkInk = V2pnColor.Parse("#111114");

    /// <summary>
    /// The stage of a partner whose theme has a background (V2pn's "Scene"), in the light or dark mode; null for a solid
    /// background, which keeps V2pn's map. A gradient runs at 135° through the background's colours (or the first two
    /// brand colours), turned from the state colours; in the dark mode each stop is toned down (OKLCH lightness × 0.78,
    /// at most 0.78 for the first and 0.72 for the rest) so a big area does not glare. The text is white or near-black,
    /// whichever reads better at the worst point; where neither reaches 4.5:1 a scrim as light as possible is laid over.
    /// A picture always gets the scrim that makes the text read over any pixel.
    /// </summary>
    public static V2pnPartnerStage? Stage(V2pnTheme theme, bool dark)
    {
        switch (theme.Background.Kind)
        {
            case V2pnThemeBackgroundKinds.Image when theme.Background.Art != null:
            {
                var ink = dark ? V2pnColor.White : StageDarkInk;
                var scrim = dark ? V2pnColor.Black : V2pnColor.White;
                var alpha = ScrimAlpha(scrim, [ink]);
                return StageOf(V2pnThemeBackgroundKinds.Image, [scrim.Hex], theme.Background.Art, scrim.WithAlpha(alpha), ink);
            }
            case V2pnThemeBackgroundKinds.Gradient:
            {
                var source = theme.Background.Colors.Count >= 2 ? theme.Background.Colors : theme.Brand.Take(2).ToList();
                var stops = source.Select(V2pnColor.TryParse).OfType<V2pnColor>().Select(AwayFromState).ToList();
                if (stops.Count == 0)
                {
                    return null;
                }
                if (stops.Count == 1)
                {
                    stops.Add(stops[0]);
                }
                if (dark)
                {
                    stops = stops.Select((c, i) =>
                    {
                        var (chroma, hue) = c.Oklch;
                        return V2pnColor.FromOklch(Math.Min(c.OkLightness * 0.78, i == 0 ? 0.78 : 0.72), i == 0 ? chroma : chroma * 0.95, hue);
                    }).ToList();
                }
                var white = MinContrast(V2pnColor.White, stops);
                var black = MinContrast(StageDarkInk, stops);
                var ink = white >= black ? V2pnColor.White : StageDarkInk;
                string? scrim = null;
                var seen = stops;
                if (Math.Max(white, black) < TextContrast)
                {
                    var veil = Opposite(ink);
                    for (var step = 1; step <= 255; step++)
                    {
                        var alpha = step / 255.0;
                        var under = stops.Select(s => veil.Over(s, alpha)).ToList();
                        if (MinContrast(ink, under) >= TextContrast)
                        {
                            (scrim, seen) = (veil.WithAlpha(alpha), under);
                            break;
                        }
                    }
                }
                return StageOf(V2pnThemeBackgroundKinds.Gradient, stops.Select(c => c.Hex).ToList(), null, scrim, ink, seen);
            }
            default:
                return null;
        }
    }

    private static V2pnColor Opposite(V2pnColor ink) => ink == V2pnColor.White ? V2pnColor.Black : V2pnColor.White;

    /// <param name="seen">The ground the text really lies on (the stops under the scrim); null for a picture, whose pixels are unknown.</param>
    private static V2pnPartnerStage StageOf(string kind, IReadOnlyList<string> stops, string? art, string? scrim, V2pnColor ink, IReadOnlyList<V2pnColor>? seen = null)
    {
        var light = ink == V2pnColor.White;
        // A chip in the ink's own colour, as a glass on the brand, while the ink still reads on it everywhere; else a
        // veil of the opposite colour, which only adds contrast.
        var chip = light ? 0.16 : 0.08;
        var ghost = seen != null && MinContrast(ink, seen.Select(s => ink.Over(s, chip)).ToList()) >= TextContrast
            ? ink.WithAlpha(chip)
            : Opposite(ink).WithAlpha(light ? 0.22 : 0.3);
        var soft = seen == null ? ink : Softer(ink, seen);
        return new V2pnPartnerStage(kind, stops, art, scrim, ink.Hex, soft.Hex, ghost, ink.Hex, light ? StageDarkInk.Hex : V2pnColor.White.Hex);
    }

    /// <summary>
    /// The colours of <paramref name="theme"/> in the light or dark mode. <paramref name="accent"/> replaces the first
    /// brand colour when the person picked another accent on the Appearance page.
    /// </summary>
    public static V2pnThemeColors Derive(V2pnTheme theme, bool dark, string? accent = null)
    {
        var brands = theme.Brand.Select(V2pnColor.TryParse).OfType<V2pnColor>().ToList();
        if (V2pnColor.TryParse(accent) is { } picked)
        {
            if (brands.Count == 0)
            {
                brands.Add(picked);
            }
            else
            {
                brands[0] = picked;
            }
        }
        if (brands.Count == 0)
        {
            brands.Add(V2pnColor.Parse(FallbackBrand));
        }
        brands = brands.Select(AwayFromState).ToList();
        var brand = brands[0];

        // The neutrals: the brand's own hue, barely tinted (none for a grey brand).
        var (brandHue, brandSat, _) = brand.Hsl;
        var hue = brandHue;
        var sat = (brandSat < 0.08 ? 0.0 : 0.07) * (dark ? 1.0 : 0.85);
        V2pnColor N(double l, double satFactor = 1) => V2pnColor.FromHsl(hue, sat * satFactor, l);

        V2pnColor ground, surface, raised, line, ink, soft, muted;
        if (dark)
        {
            ground = N(0.075);
            surface = N(0.115);
            raised = N(0.16);
            line = N(0.215, 0.8);
            ink = N(0.95, 0.35);
            soft = N(0.80, 0.4);
            muted = N(0.66, 0.4);
        }
        else
        {
            ground = N(0.95);
            surface = N(0.995);
            raised = N(0.915);
            line = N(0.855, 0.8);
            ink = N(0.10, 0.5);
            soft = N(0.30, 0.5);
            muted = N(0.42, 0.5);
        }
        V2pnColor[] neutrals = [ground, surface, raised];
        ink = Readable(ink, neutrals);
        soft = Readable(soft, neutrals);
        muted = Readable(muted, neutrals);

        // The accent fills buttons: 3:1 against the grounds it sits on, a label of 4.5:1 on it.
        // Moving the lightness can carry a colour's hue toward a state colour as the eye sees it; it turns away again.
        var fill = brand;
        var onFill = ink;
        for (var round = 0; round < 6; round++)
        {
            var moved = Readable(fill, [ground, surface], UiContrast);
            (moved, onFill) = WithLabel(moved, dark ? ink : surface, dark ? ground : ink, ground);
            var away = AwayFromState(moved);
            fill = away;
            if (away == moved)
            {
                break;
            }
        }
        var accentText = Readable(brand, neutrals);
        var secondary = Readable(brands.Count > 1 ? brands[1] : brand, [surface, raised], UiContrast);

        var area = Area(theme, dark, brands, ground, ink, soft, muted, surface, fill);
        var states = dark ? DarkStates : LightStates;
        string State(string hex) => Readable(V2pnColor.Parse(hex), neutrals).Hex;

        return new V2pnThemeColors(
            dark,
            ground.Hex,
            surface.Hex,
            raised.Hex,
            line.Hex,
            ink.Hex,
            soft.Hex,
            muted.Hex,
            fill.Hex,
            onFill.Hex,
            accentText.Hex,
            secondary.Hex,
            area,
            State(states.Good),
            State(states.Warn),
            State(states.Bad),
            hue,
            sat);
    }

    /// <summary>
    /// A fill and the label that reads on it: whichever of <paramref name="light"/> and <paramref name="darkInk"/> reads
    /// better. When neither reaches 4.5:1 (a mid-tone), the fill moves away from <paramref name="ground"/> (darker on a
    /// light ground, lighter on a dark one) until its label does, so it keeps standing out from the ground too.
    /// </summary>
    private static (V2pnColor Fill, V2pnColor Label) WithLabel(V2pnColor fill, V2pnColor light, V2pnColor darkInk, V2pnColor ground)
    {
        var better = V2pnColor.Contrast(light, fill) >= V2pnColor.Contrast(darkInk, fill) ? light : darkInk;
        if (V2pnColor.Contrast(better, fill) >= TextContrast)
        {
            return (fill, better);
        }
        var darken = ground.Luminance > 0.18;
        var label = darken ? light : darkInk;
        var (h, s, l) = fill.Hsl;
        for (var step = 1; step <= 100; step++)
        {
            var moved = V2pnColor.FromHsl(h, s, l + ((darken ? -1 : 1) * step * 0.01));
            if (V2pnColor.Contrast(label, moved) >= TextContrast)
            {
                return (moved, label);
            }
        }
        return (darken ? V2pnColor.Black : V2pnColor.White, label);
    }

    /// <summary>The brand area: a brand colour, a gradient or a picture under a scrim, and what reads on it.</summary>
    private static V2pnThemeArea Area(V2pnTheme theme, bool dark, IReadOnlyList<V2pnColor> brands, V2pnColor ground,
        V2pnColor ink, V2pnColor soft, V2pnColor muted, V2pnColor surface, V2pnColor accent)
    {
        V2pnColor white = V2pnColor.White;
        var deep = V2pnColor.FromHsl(ground.Hsl.H, Math.Min(0.25, ground.Hsl.S + 0.05), 0.07);
        var glass = GlassOver(surface, [ink, soft, muted]);

        if (theme.Background.Kind == V2pnThemeBackgroundKinds.Image)
        {
            // Whatever the picture holds, the text reads: the scrim is as strong as the darkest and the lightest pixel need.
            var scrimColour = dark ? ground : surface;
            var alpha = ScrimAlpha(scrimColour, [ink, soft]);
            var action = WithLabel(accent, dark ? ink : surface, dark ? ground : ink, scrimColour);
            return new V2pnThemeArea(V2pnThemeBackgroundKinds.Image, [scrimColour.Hex], scrimColour.WithAlpha(alpha), ink.Hex, soft.Hex,
                action.Fill.Hex, action.Label.Hex, glass);
        }

        var stops = theme.Background.Kind == V2pnThemeBackgroundKinds.Gradient && theme.Background.Colors.Count >= 2
            ? theme.Background.Colors.Select(V2pnColor.Parse).Select(AwayFromState).ToList()
            : [brands[0]];
        if (dark)
        {
            // A big area in the full brand colour glares in the dark: its deep version, same hue.
            stops = stops.Select(c => c.Hsl.L > 0.42 ? AwayFromState(c.WithLightness(0.42)) : c).ToList();
        }

        // White or near-black text, whichever reads better on the whole area; the area moves until it reads everywhere.
        var candidates = new[] { white, deep };
        var areaInk = candidates.OrderByDescending(c => MinContrast(c, stops)).First();
        var lighten = areaInk == deep;
        for (var step = 0; step < 100 && MinContrast(areaInk, stops) < TextContrast; step++)
        {
            stops = stops.Select(c => AwayFromState(c.WithLightness(c.Hsl.L + (lighten ? 0.01 : -0.01)))).ToList();
        }
        var areaSoft = Softer(areaInk, stops);
        var onAction = Readable(brands[0], areaInk);
        return new V2pnThemeArea(
            stops.Count > 1 ? V2pnThemeBackgroundKinds.Gradient : V2pnThemeBackgroundKinds.Solid,
            stops.Select(c => c.Hex).ToList(),
            null,
            areaInk.Hex,
            areaSoft.Hex,
            areaInk.Hex,
            onAction.Hex,
            glass);
    }

    /// <summary>The lowest contrast of <paramref name="text"/> anywhere on the stops and halfway between neighbours.</summary>
    public static double MinContrast(V2pnColor text, IReadOnlyList<V2pnColor> stops)
    {
        var min = double.MaxValue;
        for (var i = 0; i < stops.Count; i++)
        {
            min = Math.Min(min, V2pnColor.Contrast(text, stops[i]));
            if (i + 1 < stops.Count)
            {
                for (var t = 0.25; t < 1; t += 0.25)
                {
                    min = Math.Min(min, V2pnColor.Contrast(text, stops[i].Mix(stops[i + 1], t)));
                }
            }
        }
        return min;
    }

    /// <summary>Secondary text: <paramref name="ink"/> faded toward the area as far as it still reads at 4.5:1 everywhere.</summary>
    private static V2pnColor Softer(V2pnColor ink, IReadOnlyList<V2pnColor> stops)
    {
        var middle = stops[stops.Count / 2];
        for (var t = 0.4; t > 0; t -= 0.02)
        {
            var candidate = ink.Mix(middle, t);
            if (MinContrast(candidate, stops) >= TextContrast + 0.2)
            {
                return candidate;
            }
        }
        return ink;
    }

    /// <summary>
    /// The least opacity of <paramref name="scrim"/> over any picture (black or white pixels being the worst) at which
    /// each of <paramref name="texts"/> reads at 4.5:1.
    /// </summary>
    public static double ScrimAlpha(V2pnColor scrim, IReadOnlyList<V2pnColor> texts)
    {
        // In steps of the alpha byte the scrim is written with ("#AARRGGBB"), so the checked alpha is the drawn one.
        for (var step = 77; step < 255; step++)
        {
            var alpha = step / 255.0;
            if (texts.All(t => V2pnColor.Contrast(t, scrim.Over(V2pnColor.Black, alpha)) >= TextContrast
                    && V2pnColor.Contrast(t, scrim.Over(V2pnColor.White, alpha)) >= TextContrast))
            {
                return alpha;
            }
        }
        return 1;
    }

    /// <summary>A panel of <paramref name="surface"/> that reads over anything: "#AARRGGBB" with the least opacity that does.</summary>
    private static string GlassOver(V2pnColor surface, IReadOnlyList<V2pnColor> texts) =>
        surface.WithAlpha(Math.Max(0.86, ScrimAlpha(surface, texts)));

    /// <summary>
    /// What V2pn changed in the provider's colours and why, one line each, for tools/partner-check.cs: a brand colour turned
    /// away from a state colour, a brand made darker or lighter to read.
    /// </summary>
    public static IReadOnlyList<string> Describe(V2pnTheme theme)
    {
        var notes = new List<string>();
        foreach (var hex in theme.Brand.Concat(theme.Background.Colors))
        {
            if (V2pnColor.TryParse(hex) is { } c && AwayFromState(c) is var turned && turned != c)
            {
                notes.Add($"{hex} is close to one of V2pn's state colours (protected, attention, unprotected); V2pn uses {turned.Hex} instead");
            }
        }
        foreach (var dark in new[] { false, true })
        {
            var colours = Derive(theme, dark);
            var brand = AwayFromState(V2pnColor.TryParse(theme.Brand.FirstOrDefault()) ?? V2pnColor.Parse(FallbackBrand));
            var mode = dark ? "dark" : "light";
            if (colours.Accent != brand.Hex)
            {
                notes.Add($"{mode}: buttons use {colours.Accent} instead of {brand.Hex}, so they stand out from the ground and their label reads");
            }
            if (colours.AccentText != brand.Hex)
            {
                notes.Add($"{mode}: where the brand colour is text, it is {colours.AccentText}");
            }
        }
        return notes;
    }
}
