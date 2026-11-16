using System.Text.Json;
using Avalonia.Markup.Xaml.Styling;
using System.Text.Json.Serialization;

namespace v2rayN.Desktop.Common;

/// <summary>Everything a person can change about how V2pn looks. Stored in guiConfigs/V2pnAppearance.json.</summary>
public sealed class AppearanceSettings
{
    public string Palette { get; set; } = "graphite";

    /// <summary>A preset id from <see cref="Appearance.Accents"/> or a "#RRGGBB" colour.</summary>
    public string Accent { get; set; } = "red";

    public string Corners { get; set; } = "normal";
    public string Font { get; set; } = "inter";
    public string Headings { get; set; } = "inter";
    public string SidebarSide { get; set; } = "left";
    public string SidebarWidth { get; set; } = "normal";
    public string Density { get; set; } = "comfortable";
    public bool ShowMap { get; set; } = true;
    public bool ShowSpeedChart { get; set; } = true;
    public bool ShowStatusCard { get; set; } = true;

    /// <summary>"system" follows Windows' animation setting; "on" and "off" override it. Applied live (Motion.Refresh).</summary>
    public string Motion { get; set; } = "system";

    public AppearanceSettings Clone() => (AppearanceSettings)MemberwiseClone();
}

[JsonSerializable(typeof(AppearanceSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class AppearanceJson : JsonSerializerContext;

/// <summary>A neutral tint for every surface, text and line colour of the V2pn tokens.</summary>
public sealed record AppearancePalette(string Id, double Hue, double Saturation, double DarkDepth, double LightDepth, Color Swatch, Color SwatchLight);

/// <summary>
/// Applies <see cref="AppearanceSettings"/> to the running app. Colours are not hand-written per
/// palette: every V2pn token from V2pnTheme.axaml is re-tinted by one rule (neutrals take the
/// palette's hue and depth, the accent family takes the chosen accent, status colours stay), so a
/// token added later follows every palette with no extra work. The result is a resource dictionary
/// merged after V2pnTheme, which wins the lookup; DynamicResource bindings repaint at once.
/// </summary>
public static class Appearance
{
    private const string FileName = "V2pnAppearance.json";

    public static readonly AppearancePalette[] Palettes =
    [
        new("graphite", 240, 0.10, 1.00, 1.00, Color.Parse("#1C1C23"), Color.Parse("#F4F4F7")),
        new("midnight", 222, 0.34, 0.95, 1.00, Color.Parse("#141C2E"), Color.Parse("#EEF2FA")),
        new("nord", 216, 0.20, 1.45, 1.00, Color.Parse("#2B313D"), Color.Parse("#ECEFF4")),
        new("forest", 152, 0.20, 0.95, 1.00, Color.Parse("#16211C"), Color.Parse("#EEF5F1")),
        new("plum", 282, 0.22, 0.98, 1.00, Color.Parse("#211A28"), Color.Parse("#F5F0F8")),
        new("mocha", 28, 0.18, 1.05, 1.00, Color.Parse("#241E1A"), Color.Parse("#F7F2EC")),
        new("oled", 0, 0.00, 0.30, 1.00, Color.Parse("#050505"), Color.Parse("#FFFFFF")),
    ];

    public static readonly (string Id, Color Color)[] Accents =
    [
        // V2pn Signal, the brand red.
        ("red", Color.Parse("#FF3D1F")),
        ("orange", Color.Parse("#F07B2C")),
        ("amber", Color.Parse("#E8A21E")),
        ("green", Color.Parse("#22B573")),
        ("teal", Color.Parse("#14A8A0")),
        ("blue", Color.Parse("#3B82F6")),
        ("violet", Color.Parse("#8B5CF6")),
        ("pink", Color.Parse("#E94F96")),
    ];

    // Brushes that carry the brand colour; they take the chosen accent's hue.
    // Switches that are on and selected text or list items belong to the same family: with a green accent
    // a blue switch looked like a second, unrelated accent. The focus ring stays blue on purpose.
    private static readonly HashSet<string> AccentKeys =
    [
        "V2Accent", "V2AccentHover", "V2AccentSoft", "V2AccentFill", "V2AccentFillHover", "V2AccentShadow",
        "ToggleSwitchContainerCheckedDefaultBackground", "ToggleSwitchContainerCheckedPointeroverBackground",
        "ToggleSwitchContainerCheckedPressedBackground", "ToggleSwitchContainerCheckedDisabledBackground",
        "ComboBoxItemSelectedBackground", "TextBoxSelectionBackground",
    ];

    // State and identity colours that are muted on purpose (dark map fills, the selected row, folder
    // tiles) fall under ToPalette's saturation test and would take the palette's hue: under Mocha the
    // protected country turned brown. They keep the theme's own value. Row fill and border go together.
    private static readonly HashSet<string> FixedKeys =
    [
        "V2MapActiveSafe", "V2MapActiveLocal", "V2MapActiveUnsafe", "V2RowSelected", "V2RowSelectedBorder",
        "V2Folder", "V2FolderSoft", "V2DangerFill", "V2DangerFillHover", "V2OnDanger",
    ];

    private static readonly string[] RadiusKeys =
    [
        "V2RadiusPanel", "V2RadiusPanelInner", "V2RadiusDialog", "V2RadiusFloating", "V2RadiusFloatingInner", "V2RadiusCard", "V2RadiusTile", "V2RadiusRow", "V2RadiusControl",
        "V2RadiusBar", "V2RadiusBarInner", "V2RadiusSegment", "V2RadiusIconTile",
        "TextBoxDefaultCornerRadius", "ComboBoxSelectorCornerRadius", "ComboBoxPopupBoxCornerRadius",
        "MenuFlyoutCornerRadius", "MenuItemCornerRadius", "NotificationCardCornerRadius",
    ];

    private static AppearanceSettings? _current;
    private static ResourceDictionary? _applied;
    private static Dictionary<ThemeVariant, Dictionary<string, object>>? _baseColors;
    private static Dictionary<string, CornerRadius>? _baseRadii;
    private static object? _baseFont;

    public static AppearanceSettings Current => _current ??= Load();

    /// <summary>Raised after a change was applied; the main window rearranges itself on it.</summary>
    public static event Action? Changed;

    private static Func<AppearanceSettings, AppearanceSettings>? _edition;

    /// <summary>
    /// The settings in effect: the person's own, with a partner's brand laid over them (Editions). Only
    /// <see cref="Current"/> is stored; the brand never changes what the Appearance page shows or saves.
    /// </summary>
    public static AppearanceSettings Effective => _edition?.Invoke(Current.Clone()) ?? Current;

    /// <summary>Lays a partner's brand over the person's appearance, or takes it off (null). Applied live.</summary>
    public static void SetEdition(Func<AppearanceSettings, AppearanceSettings>? edition)
    {
        _edition = edition;
        if (Application.Current is { } app)
        {
            Apply(app, Effective);
        }
        Changed?.Invoke();
    }

    public static AppearancePalette Palette => Palettes.FirstOrDefault(p => p.Id == Effective.Palette) ?? Palettes[0];

    public static Color AccentColor => Accents.FirstOrDefault(a => a.Id == Effective.Accent) is { Id: not null } preset
        ? preset.Color
        : Color.TryParse(Effective.Accent, out var custom) ? custom : Accents[0].Color;

    /// <summary>Applies the stored appearance; called once while the app starts.</summary>
    public static void Install(Application app)
    {
        Capture(app);
        Apply(app, Effective);
    }

    /// <summary>Applies and stores a new appearance. The window updates live.</summary>
    public static void Set(AppearanceSettings settings)
    {
        _current = settings;
        if (Application.Current is { } app)
        {
            Apply(app, Effective);
        }
        Save(settings);
        Changed?.Invoke();
    }

    public static void Reset() => Set(new AppearanceSettings());

    private static void Apply(Application app, AppearanceSettings settings)
    {
        var dict = new ResourceDictionary();
        var palette = Palette;
        var accent = AccentColor;
        var identity = palette.Id == "graphite" && settings.Accent == "red";

        if (!identity && _baseColors != null)
        {
            foreach (var (variant, tokens) in _baseColors)
            {
                var dark = variant == ThemeVariant.Dark;
                var themed = new ResourceDictionary();
                var baseAccent = tokens.TryGetValue("V2Accent", out var a) && a is ISolidColorBrush ab ? ab.Color : Accents[0].Color;
                foreach (var (key, value) in tokens)
                {
                    if (FixedKeys.Contains(key))
                    {
                        continue;
                    }
                    // Graphite is the stock look: only the accent family changes there.
                    Func<Color, Color> recolor = AccentKeys.Contains(key)
                        ? c => ToAccent(c, baseAccent, accent)
                        : palette.Id == "graphite" ? c => c : c => ToPalette(c, palette, dark);
                    if (Recolor(value, recolor) is { } changed)
                    {
                        themed[key] = changed;
                    }
                }
                // Labels on the accent: near-black like the brand's Signal buttons, white on a dark custom accent.
                themed["V2OnAccent"] = new SolidColorBrush(OnAccent(accent));
                dict.ThemeDictionaries[variant] = themed;
            }
        }

        var scale = settings.Corners switch
        {
            "sharp" => 0.35,
            "round" => 1.45,
            _ => 1.0,
        };
        if (_baseRadii != null && scale != 1.0)
        {
            foreach (var (key, radius) in _baseRadii)
            {
                dict[key] = new CornerRadius(
                    Math.Round(radius.TopLeft * scale), Math.Round(radius.TopRight * scale),
                    Math.Round(radius.BottomRight * scale), Math.Round(radius.BottomLeft * scale));
            }
        }

        var ui = settings.Font switch
        {
            "segoe" => new FontFamily("Segoe UI Variable Text, Segoe UI, avares://V2pn/Assets/Fonts#Inter"),
            _ => null,
        };
        if (ui != null)
        {
            dict["V2UiFont"] = ui;
        }
        if (settings.Headings == "serif")
        {
            dict["V2DisplayFont"] = new FontFamily("avares://V2pn/Assets/Fonts#Spectral");
        }
        else if (ui != null)
        {
            dict["V2DisplayFont"] = ui;
        }
        // AppFontFamily is an own entry of the app's resources, which outranks merged dictionaries.
        app.Resources["AppFontFamily"] = ui ?? _baseFont ?? app.Resources["AppFontFamily"];

        var merged = app.Resources.MergedDictionaries;
        if (_applied != null)
        {
            merged.Remove(_applied);
        }
        _applied = dict;
        merged.Add(dict);
    }

    // The stock tokens, read once, so every palette starts from the same colours.
    private static void Capture(Application app)
    {
        var theme = app.Resources.MergedDictionaries
            .Select(m => m is ResourceInclude include ? include.Loaded : m as IResourceDictionary)
            .OfType<ResourceDictionary>()
            .FirstOrDefault(d => d.ThemeDictionaries.Values.Any(t => t is IResourceDictionary r && r.ContainsKey("V2WindowBackground")));
        if (theme == null)
        {
            return;
        }
        _baseColors = [];
        foreach (var pair in theme.ThemeDictionaries)
        {
            if (pair.Value is IResourceDictionary tokens)
            {
                _baseColors[pair.Key] = tokens.Keys.OfType<string>().ToDictionary(k => k, k => tokens[k]!);
            }
        }
        _baseRadii = [];
        foreach (var key in RadiusKeys)
        {
            if (theme.TryGetResource(key, null, out var value) && value is CornerRadius radius)
            {
                _baseRadii[key] = radius;
            }
        }
        _baseFont = app.Resources["AppFontFamily"];
    }

    private static object? Recolor(object value, Func<Color, Color> map) => value switch
    {
        ISolidColorBrush solid => new SolidColorBrush(map(solid.Color), solid.Opacity),
        ILinearGradientBrush linear => new LinearGradientBrush
        {
            StartPoint = linear.StartPoint,
            EndPoint = linear.EndPoint,
            GradientStops = [.. linear.GradientStops.Select(s => new GradientStop(map(s.Color), s.Offset))],
        },
        BoxShadows shadows when shadows.Count > 0 => RecolorShadows(shadows, map),
        _ => null,
    };

    private static BoxShadows RecolorShadows(BoxShadows shadows, Func<Color, Color> map)
    {
        var list = new List<BoxShadow>();
        foreach (var s in shadows)
        {
            list.Add(s with { Color = map(s.Color) });
        }
        return new BoxShadows(list[0], [.. list.Skip(1)]);
    }

    /// <summary>
    /// Neutral colours take the palette's hue; their lightness keeps its place in the scale, pushed
    /// deeper or lighter by the palette. Coloured ones (status, tiles) and pure black or white
    /// overlays stay as they are.
    /// </summary>
    internal static Color ToPalette(Color c, AppearancePalette palette, bool dark)
    {
        var hsl = c.ToHsl();
        if (hsl.S > 0.36 || hsl.L > 0.985 || hsl.L < 0.015)
        {
            return c;
        }
        // Depth moves surfaces and lines only; secondary text (about 45% and up in the dark theme)
        // keeps its lightness, or a black palette would make hints unreadable.
        var l = hsl.L;
        if (dark && l < 0.42)
        {
            var reach = l < 0.28 ? 1.0 : (0.42 - l) / 0.14;
            l *= 1 + ((palette.DarkDepth - 1) * reach);
        }
        else if (!dark && l > 0.5)
        {
            l = 1 - ((1 - l) * palette.LightDepth);
        }
        // Text and the brightest/darkest surfaces take little tint; mid surfaces take the most.
        var s = palette.Saturation * (dark ? 1.0 : 0.75);
        if ((dark && l > 0.7) || (!dark && l < 0.3))
        {
            s *= 0.35;
        }
        return HslColor.ToRgb(palette.Hue, Math.Clamp(s, 0, 1), Math.Clamp(l, 0, 1), c.A / 255.0);
    }

    private static readonly Color Ink = Color.Parse("#0B0B0D");

    /// <summary>Ink or white, whichever reads better on the accent (WCAG contrast).</summary>
    internal static Color OnAccent(Color accent) =>
        Contrast(Ink, accent) >= Contrast(Colors.White, accent) ? Ink : Colors.White;

    private static double Contrast(Color a, Color b)
    {
        static double Lum(Color c)
        {
            static double Channel(byte v)
            {
                var x = v / 255.0;
                return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
            }
            return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
        }
        var (l1, l2) = (Lum(a), Lum(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    /// <summary>OKLCH chroma and hue (degrees) of an sRGB colour.</summary>
    internal static (double Chroma, double Hue) Oklch(Color c)
    {
        static double Linear(byte v)
        {
            var x = v / 255.0;
            return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        var (r, g, b) = (Linear(c.R), Linear(c.G), Linear(c.B));
        var l = Math.Cbrt((0.4122214708 * r) + (0.5363325363 * g) + (0.0514459929 * b));
        var m = Math.Cbrt((0.2119034982 * r) + (0.6806995451 * g) + (0.1073969566 * b));
        var s = Math.Cbrt((0.0883024619 * r) + (0.2817188376 * g) + (0.6299787005 * b));
        var a = (1.9779984951 * l) - (2.4285922050 * m) + (0.4505937099 * s);
        var bb = (0.0259040371 * l) + (0.7827717662 * m) - (0.8086757660 * s);
        var hue = Math.Atan2(bb, a) * 180 / Math.PI;
        return (Math.Sqrt((a * a) + (bb * bb)), hue < 0 ? hue + 360 : hue);
    }

    /// <summary>True when a coloured accent sits within <paramref name="degrees"/> of one of the given hues.</summary>
    /// <summary>The hues of "protected" green and "attention" amber: an accent near them reads as a state.</summary>
    internal static readonly Color[] StatusHues = [Color.Parse("#3DD68C"), Color.Parse("#F2B84B")];

    /// <summary>
    /// A partner's accent turned just far enough from the state hues that a button in it never reads as
    /// "protected" or "attention"; an accent already clear of them comes back as it is.
    /// </summary>
    internal static Color AwayFromStatus(Color accent)
    {
        if (!NearHue(accent, StatusHues))
        {
            return accent;
        }
        var hsl = accent.ToHsl();
        for (var step = 5; step <= 90; step += 5)
        {
            foreach (var sign in new[] { -1, 1 })
            {
                var turned = HslColor.ToRgb((hsl.H + (sign * step) + 360) % 360, hsl.S, hsl.L, 1);
                if (!NearHue(turned, StatusHues, 25))
                {
                    return turned;
                }
            }
        }
        return accent;
    }

    internal static bool NearHue(Color accent, IEnumerable<Color> others, double degrees = 20)
    {
        var (chroma, hue) = Oklch(accent);
        if (chroma < 0.04)
        {
            return false;
        }
        foreach (var other in others)
        {
            var diff = Math.Abs(hue - Oklch(other).Hue) % 360;
            if (Math.Min(diff, 360 - diff) <= degrees)
            {
                return true;
            }
        }
        return false;
    }

    internal static Color ToAccent(Color c, Color from, Color to)
    {
        var hsl = c.ToHsl();
        var f = from.ToHsl();
        var t = to.ToHsl();
        // Shadows and highlights inside the accent styles are black or white: leave them.
        if (hsl.S < 0.15 && (hsl.L < 0.1 || hsl.L > 0.9))
        {
            return c;
        }
        var s = f.S > 0 ? hsl.S * (t.S / f.S) : t.S;
        var l = hsl.L + (t.L - f.L);
        return HslColor.ToRgb(t.H, Math.Clamp(s, 0, 1), Math.Clamp(l, 0, 1), c.A / 255.0);
    }

    private static string FilePath => Utils.GetConfigPath(FileName);

    private static AppearanceSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), AppearanceJson.Default.AppearanceSettings) ?? new();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(Appearance), ex);
        }
        return new();
    }

    private static void Save(AppearanceSettings settings)
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, AppearanceJson.Default.AppearanceSettings));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(Appearance), ex);
        }
    }
}
