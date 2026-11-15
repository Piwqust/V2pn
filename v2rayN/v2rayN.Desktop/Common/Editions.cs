using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Threading;

namespace v2rayN.Desktop.Common;

/// <summary>Which edition the window wears, which look each partner's edition was last left in, and how each look is tuned.</summary>
public sealed class EditionSettings
{
    /// <summary>The partner whose edition is open; null for plain V2pn.</summary>
    public string? Partner { get; set; }

    /// <summary>Partner id → the look (V2pnPartnerVariants) the person picked for it.</summary>
    public Dictionary<string, string> Variants { get; set; } = [];

    /// <summary>"partner:look" → what the person changed in that look on the Appearance page.</summary>
    public Dictionary<string, EditionLookPrefs> Looks { get; set; } = [];

    /// <summary>Partners whose look was already offered (the intro shows once per partner).</summary>
    public List<string> Introduced { get; set; } = [];
}

/// <summary>A look's own appearance choices; null keeps what the partner's look has.</summary>
public sealed class EditionLookPrefs
{
    /// <summary>"light" or "dark".</summary>
    public string? Mode { get; set; }

    /// <summary>A V2pn accent id or "#RRGGBB"; null is the partner's colour.</summary>
    public string? Accent { get; set; }

    /// <summary>square, sharp, normal or round.</summary>
    public string? Corners { get; set; }

    /// <summary>geist, geistmono, inter or segoe.</summary>
    public string? Font { get; set; }
}

[JsonSerializable(typeof(EditionSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class EditionJson : JsonSerializerContext;

/// <summary>
/// Partner editions: V2pn dressed as a verified partner (V2pnPartners). The person switches between plain
/// V2pn and each partner from the name in the title bar or with Ctrl+1…9; the connection, settings and
/// servers stay the same, only the look changes. A partner only picks among looks V2pn draws itself, and the
/// person tunes each look (light or dark, accent, corners, type) on the Appearance page without touching the
/// appearance of plain V2pn.
/// </summary>
public static class Editions
{
    private const string FileName = "V2pnEdition.json";
    private static EditionSettings? _settings;

    /// <summary>Raised on the UI thread after the edition, its look, its tuning or the set of partners changed.</summary>
    public static event Action? Changed;

    /// <summary>Raised on the UI thread when the open partner's look went away (with the partner and why).</summary>
    public static event Action<V2pnPartner, V2pnPartnerGone>? Closed;

    private static EditionSettings Settings => _settings ??= Load();

    /// <summary>Partners whose certificate grants an edition, in the order the switcher lists them.</summary>
    public static IReadOnlyList<V2pnPartner> Available => V2pnPartners.Instance.All.Where(p => p.HasEdition).ToList();

    /// <summary>The partner whose edition is open; null for plain V2pn, also when that partner is no longer verified.</summary>
    public static V2pnPartner? Partner => Settings.Partner is { } id ? Available.FirstOrDefault(p => p.Id == id) : null;

    public static string Variant => Partner is { } partner ? VariantOf(partner) : string.Empty;

    /// <summary>True when the open look replaces the sidebar and the map with a scene of its own.</summary>
    public static bool IsScene => Variant is V2pnPartnerVariants.Cabinet or V2pnPartnerVariants.Studio or V2pnPartnerVariants.Ascii;

    /// <summary>True when a look other than the signature one is open: the Appearance page then tunes that look.</summary>
    public static bool IsTuned => Partner != null && Variant != V2pnPartnerVariants.Signature;

    public static string VariantOf(V2pnPartner partner) =>
        Settings.Variants.TryGetValue(partner.Id, out var chosen) && partner.Pack.Variants.Contains(chosen)
            ? chosen
            : partner.Pack.DefaultVariant;

    /// <summary>The open look's tuning as the person left it (empty when untouched).</summary>
    public static EditionLookPrefs Prefs =>
        Partner is { } partner && Settings.Looks.TryGetValue($"{partner.Id}:{Variant}", out var prefs) ? prefs : new();

    /// <summary>The partner's own colour for its looks, before the person's choice.</summary>
    public static string PartnerAccent => Partner?.Pack.Accent ?? "#FF3D1F";

    // What each look is drawn with when the person has not changed it.
    public static string DefaultMode(string look) => look == V2pnPartnerVariants.Studio ? "light" : "dark";

    public static string DefaultCorners(string look) => look switch
    {
        V2pnPartnerVariants.Cabinet or V2pnPartnerVariants.Studio => "round",
        V2pnPartnerVariants.Ascii => "square",
        _ => Appearance.Current.Corners,
    };

    public static string DefaultFont(string look) => look == V2pnPartnerVariants.Ascii ? "geistmono" : "geist";

    /// <summary>The open look's settings in effect: the person's tuning over the look's own.</summary>
    public static string Mode => Prefs.Mode ?? DefaultMode(Variant);

    public static string Corners => Prefs.Corners ?? DefaultCorners(Variant);

    public static string Font => Prefs.Font ?? DefaultFont(Variant);

    /// <summary>The accent in effect as "#RRGGBB", for the looks' own drawing.</summary>
    public static string AccentHex
    {
        get
        {
            var accent = Prefs.Accent;
            if (accent == null)
            {
                return PartnerAccent;
            }
            if (Appearance.Accents.FirstOrDefault(a => a.Id == accent) is { Id: not null } preset)
            {
                return $"#{preset.Color.R:X2}{preset.Color.G:X2}{preset.Color.B:X2}";
            }
            return Color.TryParse(accent, out _) ? accent : PartnerAccent;
        }
    }

    private static bool _started;

    /// <summary>Loads the kept partners and follows their changes; the window calls it once.</summary>
    public static void Start()
    {
        if (_started)
        {
            return;
        }
        _started = true;
        V2pnPartners.Instance.Changed += () => Dispatcher.UIThread.Post(Apply);
        V2pnPartners.Instance.Removed += (partner, why) => Dispatcher.UIThread.Post(() => OnRemoved(partner, why));
        // Partners of subscriptions deleted while V2pn was closed go now; certificates that run out while it is
        // open are caught by the hourly check.
        _ = V2pnPartners.Instance.PruneAsync();
        _pruneTimer = new DispatcherTimer(TimeSpan.FromHours(1), DispatcherPriority.Background, (_, _) => _ = V2pnPartners.Instance.PruneAsync());
        _pruneTimer.Start();
        Apply();
    }

    private static DispatcherTimer? _pruneTimer;

    private static void OnRemoved(V2pnPartner partner, V2pnPartnerGone why)
    {
        if (Settings.Partner != partner.Id)
        {
            return;
        }
        // Plain V2pn opens; the person's tuning of the partner's looks stays, should the partner come back.
        Settings.Partner = null;
        Save();
        Apply();
        Closed?.Invoke(partner, why);
    }

    /// <summary>Opens a partner's edition, or plain V2pn for null.</summary>
    public static void Select(string? partnerId)
    {
        if (Settings.Partner == partnerId)
        {
            return;
        }
        Settings.Partner = partnerId;
        Save();
        Apply();
    }

    public static void SetVariant(string variant)
    {
        if (Partner is not { } partner || !partner.Pack.Variants.Contains(variant))
        {
            return;
        }
        Settings.Variants[partner.Id] = variant;
        Save();
        Apply();
    }

    /// <summary>
    /// A partner whose look is available but was never offered: its intro is due. A partner the person already
    /// opened counts as offered.
    /// </summary>
    public static V2pnPartner? DueIntro()
    {
        foreach (var partner in Available)
        {
            if (Settings.Introduced.Contains(partner.Id))
            {
                continue;
            }
            if (Settings.Partner == partner.Id || Settings.Variants.ContainsKey(partner.Id))
            {
                MarkIntroduced(partner.Id);
                continue;
            }
            return partner;
        }
        return null;
    }

    public static void MarkIntroduced(string partnerId)
    {
        if (!Settings.Introduced.Contains(partnerId))
        {
            Settings.Introduced.Add(partnerId);
            Save();
        }
    }

    /// <summary>Changes the open look's tuning (the Appearance page in an edition); applied at once.</summary>
    public static void Tune(Action<EditionLookPrefs> edit)
    {
        if (Partner is not { } partner)
        {
            return;
        }
        var key = $"{partner.Id}:{Variant}";
        if (!Settings.Looks.TryGetValue(key, out var prefs))
        {
            prefs = new EditionLookPrefs();
            Settings.Looks[key] = prefs;
        }
        edit(prefs);
        Save();
        Apply();
    }

    /// <summary>Puts the open look back as the partner drew it.</summary>
    public static void ResetTuning()
    {
        if (Partner is { } partner && Settings.Looks.Remove($"{partner.Id}:{Variant}"))
        {
            Save();
            Apply();
        }
    }

    private static void Apply()
    {
        Appearance.SetEdition(Overlay(), DarkTokens(), LightTokens());
        ApplyThemeVariant();
        Changed?.Invoke();
    }

    // The person's own light/dark choice, put back when the edition is left.
    private static ThemeVariant? _userVariant;
    private static ThemeVariant? _forced;

    /// <summary>
    /// A partner look is light or dark as the look is drawn (or as the person tuned it), whatever plain V2pn uses,
    /// and so is every dialog, menu and window while it is open. The signature look keeps the person's mode.
    /// </summary>
    private static void ApplyThemeVariant()
    {
        if (Application.Current is not { } app)
        {
            return;
        }
        ThemeVariant? forced = IsTuned ? (Mode == "light" ? ThemeVariant.Light : ThemeVariant.Dark) : null;
        if (forced != null)
        {
            if (_forced == null)
            {
                _userVariant = app.RequestedThemeVariant;
            }
            _forced = forced;
            app.RequestedThemeVariant = forced;
        }
        else if (_forced != null)
        {
            // A mode picked for plain V2pn meanwhile is the person's newer choice; it stays.
            if (app.RequestedThemeVariant == _forced)
            {
                app.RequestedThemeVariant = _userVariant;
            }
            _forced = null;
        }
    }

    /// <summary>
    /// How the edition changes the appearance, and with it every dialog, menu and settings window. The signature
    /// look keeps V2pn's own; the others take the look's ground and the tuning on top. State colours (protected
    /// green, unprotected red) are never part of a look, so they stay V2pn's.
    /// </summary>
    private static Func<AppearanceSettings, AppearanceSettings>? Overlay()
    {
        if (!IsTuned)
        {
            return null;
        }
        var look = Variant;
        var accent = Prefs.Accent ?? PartnerAccent;
        var corners = Corners;
        var font = Font;
        return settings =>
        {
            settings.Palette = look switch
            {
                V2pnPartnerVariants.Studio => "studio",
                V2pnPartnerVariants.Ascii => "ink",
                _ => "warm",
            };
            settings.Accent = accent;
            settings.Corners = corners;
            settings.Font = font;
            settings.Headings = font is "geist" or "geistmono" ? font : "inter";
            return settings;
        };
    }

    /// <summary>
    /// The looks' dark grounds: the window, the dialogs and menus, the map. The ASCII look is a terminal, flat black
    /// with hairlines; the others share the partner's warm near-black.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? DarkTokens()
    {
        if (!IsTuned)
        {
            return null;
        }
        var ascii = Variant == V2pnPartnerVariants.Ascii;
        var (window, ground, dialog, card, muted, line, menu, menuLine) = ascii
            ? ("#0C0C0B", "#121211", "#121211", "#121211", "#1C1B19", "#2E2D29", "#121211", "#3A3934")
            : ("#0B0B0A", "#0E0E0D", "#171716", "#1C1B19", "#232321", "#2A2926", "#1C1B19", "#34312C");
        return new Dictionary<string, string>
        {
            ["V2WindowBackground"] = window,
            ["V2SidebarBackground"] = dialog,
            ["V2ContentBackground"] = ground,
            ["V2DialogBackground"] = dialog,
            ["V2CardBackground"] = card,
            ["V2CardMutedBackground"] = muted,
            ["V2Border"] = line,
            ["V2PanelBorder"] = line,
            ["MenuFlyoutBackground"] = menu,
            ["MenuFlyoutBorderBrush"] = menuLine,
            ["FlyoutBackground"] = menu,
            ["FlyoutBorderBrush"] = menuLine,
            ["ComboBoxPopupBackground"] = menu,
            ["ComboBoxPopupBorderBrush"] = menuLine,
            ["NotificationCardBackground"] = menu,
            ["NotificationCardBorderBrush"] = menuLine,
            ["V2MapOcean"] = ground,
            ["V2MapLand"] = ascii ? "#2A2926" : "#262421",
            ["V2MapBorder"] = menuLine,
            ["V2MapDot"] = AccentHex,
            ["V2RowSelected"] = ascii ? "#22201C" : "#2A2420",
            ["V2RowSelectedBorder"] = "#5A3A26",
        };
    }

    /// <summary>The looks' light grounds: studio grey with white cards for most, plain paper for the ASCII look.</summary>
    private static IReadOnlyDictionary<string, string>? LightTokens()
    {
        if (!IsTuned)
        {
            return null;
        }
        var ascii = Variant == V2pnPartnerVariants.Ascii;
        var (window, sidebar, card, muted, menu) = ascii
            ? ("#ECEAE4", "#F3F2EE", "#F7F6F2", "#E7E5DF", "#F7F6F2")
            : ("#E2E2E0", "#F4F3F0", "#FFFFFF", "#ECEBE8", "#FFFFFF");
        return new Dictionary<string, string>
        {
            ["V2WindowBackground"] = window,
            ["V2ContentBackground"] = window,
            ["V2SidebarBackground"] = sidebar,
            ["V2DialogBackground"] = sidebar,
            ["V2CardBackground"] = card,
            ["V2CardMutedBackground"] = muted,
            ["V2FooterBackground"] = muted,
            ["V2Primary"] = "#121211",
            ["V2PrimaryHover"] = "#2E2D2B",
            ["V2OnPrimary"] = "#F3F2EE",
            ["MenuFlyoutBackground"] = menu,
            ["FlyoutBackground"] = menu,
            ["ComboBoxPopupBackground"] = menu,
            ["NotificationCardBackground"] = menu,
            ["TextBoxDefaultBackground"] = card,
            ["ComboBoxSelectorBackground"] = card,
        };
    }

    private static string FilePath => Utils.GetConfigPath(FileName);

    private static EditionSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), EditionJson.Default.EditionSettings) ?? new();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(Editions), ex);
        }
        return new();
    }

    private static void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Settings, EditionJson.Default.EditionSettings));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(Editions), ex);
        }
    }
}
