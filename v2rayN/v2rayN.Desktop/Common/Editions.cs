using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Threading;
using v2rayN.Desktop.Views.EditionLooks;

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

    /// <summary>Partners whose Neon already opened the mini window once (theme.window "mini"); after that the window stays as the person leaves it.</summary>
    public List<string> Minified { get; set; } = [];
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

    /// <summary>Raised just before a change of look or tuning is applied, while the old look is still on screen.</summary>
    public static event Action? Changing;

    /// <summary>Raised on the UI thread when the open partner's look went away (with the partner and why).</summary>
    public static event Action<V2pnPartner, V2pnPartnerGone>? Closed;

    private static EditionSettings Settings => _settings ??= Load();

    /// <summary>Partners whose certificate grants an edition, in the order the switcher lists them.</summary>
    public static IReadOnlyList<V2pnPartner> Available => V2pnPartners.Instance.All.Where(p => p.HasEdition).ToList();

    /// <summary>The partner whose edition is open; null for plain V2pn, also when that partner is no longer verified.</summary>
    public static V2pnPartner? Partner => Settings.Partner is { } id ? Available.FirstOrDefault(p => p.Id == id) : null;

    public static string Variant => Partner is { } partner ? VariantOf(partner) : string.Empty;

    /// <summary>True when the open look (Neon) replaces the sidebar and the map with a scene of its own.</summary>
    public static bool IsScene => Variant == V2pnPartnerVariants.Neon;

    /// <summary>The open partner's theme (both looks wear it: Neon all over, the Pass on its card); null for plain V2pn.</summary>
    public static V2pnTheme? Theme => Partner is { } partner ? ThemeKit.Of(partner) : null;

    /// <summary>The colours of the open theme in the light or dark mode, with the accent the person picked.</summary>
    public static V2pnThemeColors? ThemeColors(bool dark) => Theme is { } theme ? V2pnThemePalette.Derive(theme, dark, AccentHex) : null;

    /// <summary>True when Neon is open: the Appearance page then tunes it. The Pass keeps plain V2pn's appearance.</summary>
    public static bool IsTuned => Partner != null && Variant == V2pnPartnerVariants.Neon;

    public static string VariantOf(V2pnPartner partner) =>
        Settings.Variants.TryGetValue(partner.Id, out var chosen) && V2pnPartnerVariants.Normalize(chosen) is { } look && partner.Pack.Variants.Contains(look)
            ? look
            : partner.Pack.DefaultVariant;

    /// <summary>The open look's tuning as the person left it (empty when untouched).</summary>
    public static EditionLookPrefs Prefs =>
        Partner is { } partner && Settings.Looks.TryGetValue($"{partner.Id}:{Variant}", out var prefs) ? prefs : new();

    /// <summary>
    /// The partner's own colour for its looks, before the person's choice; turned away from the state hues when it
    /// is close to them, so a green partner never paints Connect as "protected".
    /// </summary>
    public static string PartnerAccent
    {
        get
        {
            // A theme's first brand colour, turned from the state colours the same way the theme's palette does.
            if (Theme is { } theme)
            {
                var brand = V2pnColor.TryParse(theme.Brand.FirstOrDefault()) ?? V2pnColor.Parse(V2pnThemePalette.FallbackBrand);
                return V2pnThemePalette.AwayFromState(brand).Hex;
            }
            if (Partner?.Pack.Accent is not { } hex || !Color.TryParse(hex, out var colour))
            {
                return "#FF3D1F";
            }
            var safe = Appearance.AwayFromStatus(colour);
            return safe == colour ? hex : $"#{safe.R:X2}{safe.G:X2}{safe.B:X2}";
        }
    }

    // What each look is drawn with when the person has not changed it. Neon is made dark first, as INCY is; the
    // person may switch it to light. The Pass is plain V2pn and keeps the person's own settings.
    public static string DefaultMode(string look) => look == V2pnPartnerVariants.Neon ? "dark" : PersonsMode();

    public static string DefaultCorners(string look) => look == V2pnPartnerVariants.Neon ? "round" : Appearance.Current.Corners;

    public static string DefaultFont(string look) => "geist";

    /// <summary>Light or dark as plain V2pn is: the person's own choice, or the system's when they follow it.</summary>
    private static string PersonsMode()
    {
        var app = Application.Current;
        var chosen = _forced != null ? _userVariant : app?.RequestedThemeVariant;
        if (chosen == ThemeVariant.Light)
        {
            return "light";
        }
        if (chosen == ThemeVariant.Dark)
        {
            return "dark";
        }
        return app?.PlatformSettings?.GetColorValues().ThemeVariant == Avalonia.Platform.PlatformThemeVariant.Light ? "light" : "dark";
    }

    /// <summary>The open look's settings in effect: the person's tuning over the look's own.</summary>
    public static string Mode => Prefs.Mode ?? DefaultMode(Variant);

    // Corners and type are part of what a look is: free choices there multiplied the looks into combinations nobody
    // had seen, and broke them. A look keeps its own. Older tuning files may still carry a choice; it is ignored.
    public static string Corners => DefaultCorners(Variant);

    public static string Font => DefaultFont(Variant);

    /// <summary>
    /// The accents a person may pick for a partner's look: the partner's own, then V2pn's presets that cannot pass for
    /// a state colour (no green, no amber). Only these are offered, so every pick is one that was looked at.
    /// </summary>
    public static IReadOnlyList<(string? Id, Color Color)> AccentChoices()
    {
        var choices = new List<(string? Id, Color Color)> { (null, Color.Parse(PartnerAccent)) };
        choices.AddRange(Appearance.Accents
            .Where(a => !Appearance.NearHue(a.Color, Appearance.StatusHues))
            .Select(a => ((string?)a.Id, a.Color)));
        return choices;
    }

    /// <summary>The accent in effect as "#RRGGBB", for the looks' own drawing.</summary>
    public static string AccentHex
    {
        get
        {
            var accent = Prefs.Accent;
            if (accent != null && AccentChoices().FirstOrDefault(a => a.Id == accent) is { Id: not null } preset)
            {
                return $"#{preset.Color.R:X2}{preset.Color.G:X2}{preset.Color.B:X2}";
            }
            return PartnerAccent;
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
        Changing?.Invoke();
        Settings.Partner = partnerId;
        Save();
        Apply();
    }

    public static void SetVariant(string variant)
    {
        if (Partner is not { } partner || !partner.Pack.Variants.Contains(variant) || variant == Variant)
        {
            return;
        }
        Changing?.Invoke();
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
            // The theme builder's preview is opened by the person on purpose; it has no intro.
            if (V2pnPartners.IsPreview(partner.Id) || Settings.Introduced.Contains(partner.Id))
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

    /// <summary>Debug: the intro shows again for every partner.</summary>
    public static void DebugResetIntroduced()
    {
        Settings.Introduced.Clear();
        Save();
        Changed?.Invoke();
    }

    /// <summary>Debug: <paramref name="partner"/>'s look closes as it would when <paramref name="why"/> happens; its files stay.</summary>
    public static void DebugClose(V2pnPartner partner, V2pnPartnerGone why)
    {
        Settings.Partner = partner.Id;
        OnRemoved(partner, why);
    }

    // The look open before the theme builder's preview, to go back to; the preview itself is never written to the file.
    private static string? _beforePreview;

    /// <summary>Opens the theme builder's preview (V2pnPartners.SetPreview) in its theme look.</summary>
    public static void OpenPreview()
    {
        if (V2pnPartners.Instance.Get(V2pnPartners.PreviewId) is not { } preview)
        {
            return;
        }
        Changing?.Invoke();
        if (!V2pnPartners.IsPreview(Settings.Partner))
        {
            _beforePreview = Settings.Partner;
        }
        Settings.Partner = preview.Id;
        Settings.Variants[preview.Id] = preview.Pack.DefaultVariant;
        Apply();
    }

    /// <summary>
    /// True once for each partner whose Neon asks for the mini window (theme.window "mini"), the first time it opens:
    /// the window then goes mini; the next times it stays as the person left it.
    /// </summary>
    public static bool TakeMiniStart()
    {
        if (Partner is not { } partner || Variant != V2pnPartnerVariants.Neon || Theme?.Window != V2pnThemeWindow.Mini
            || Settings.Minified.Contains(partner.Id))
        {
            return false;
        }
        Settings.Minified.Add(partner.Id);
        Save();
        return true;
    }

    /// <summary>Takes the preview away and opens the look that was open before it.</summary>
    public static void ClosePreview()
    {
        if (V2pnPartners.IsPreview(Settings.Partner))
        {
            Changing?.Invoke();
            Settings.Partner = _beforePreview;
        }
        _beforePreview = null;
        Settings.Variants.Remove(V2pnPartners.PreviewId);
        foreach (var key in Settings.Looks.Keys.Where(k => k.StartsWith(V2pnPartners.PreviewId + ":", StringComparison.Ordinal)).ToList())
        {
            Settings.Looks.Remove(key);
        }
        V2pnPartners.Instance.RemovePreview();
        Apply();
    }

    /// <summary>True while the window wears the theme builder's preview.</summary>
    public static bool IsPreview => V2pnPartners.IsPreview(Partner?.Id);

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
        Changing?.Invoke();
        edit(prefs);
        Save();
        Apply();
    }

    /// <summary>Puts the open look back as the partner drew it.</summary>
    public static void ResetTuning()
    {
        if (Partner is { } partner && Settings.Looks.ContainsKey($"{partner.Id}:{Variant}"))
        {
            Changing?.Invoke();
            Settings.Looks.Remove($"{partner.Id}:{Variant}");
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
        var corners = Corners;
        var font = Font;
        if (ThemeColors(Mode == "dark") is not { } theme)
        {
            return null;
        }
        // Neon dresses the dialogs and menus too: its neutrals, its accent, its shape and its font.
        Appearance.ThemePalette = new AppearancePalette("theme", theme.NeutralHue, theme.NeutralSaturation, 0.9, 1.0, Color.Parse(theme.Ground), Color.Parse(theme.Ground));
        return settings =>
        {
            settings.Palette = "theme";
            settings.Accent = theme.Accent;
            settings.Corners = corners;
            settings.Font = font;
            settings.Headings = font;
            return settings;
        };
    }

    /// <summary>Neon's grounds for every window: the page, cards, menus and the map, from its derived colours.</summary>
    private static IReadOnlyDictionary<string, string>? ThemeTokens(bool dark)
    {
        if (ThemeColors(dark) is not { } c)
        {
            return null;
        }
        return new Dictionary<string, string>
        {
            ["V2WindowBackground"] = c.Ground,
            ["V2ContentBackground"] = c.Ground,
            ["V2SidebarBackground"] = c.Surface,
            ["V2DialogBackground"] = c.Surface,
            ["V2CardBackground"] = dark ? c.Raised : c.Surface,
            ["V2CardMutedBackground"] = c.Raised,
            ["V2FooterBackground"] = c.Raised,
            ["V2Border"] = c.Line,
            ["V2PanelBorder"] = c.Line,
            ["MenuFlyoutBackground"] = c.Surface,
            ["MenuFlyoutBorderBrush"] = c.Line,
            ["FlyoutBackground"] = c.Surface,
            ["FlyoutBorderBrush"] = c.Line,
            ["ComboBoxPopupBackground"] = c.Surface,
            ["ComboBoxPopupBorderBrush"] = c.Line,
            ["NotificationCardBackground"] = c.Surface,
            ["NotificationCardBorderBrush"] = c.Line,
            ["TextBoxDefaultBackground"] = dark ? c.Raised : c.Surface,
            ["ComboBoxSelectorBackground"] = dark ? c.Raised : c.Surface,
            ["V2Ink"] = c.Ink,
            ["V2InkSoft"] = c.Soft,
            ["V2Muted"] = c.Muted,
            ["V2MapOcean"] = c.Ground,
            ["V2MapLand"] = c.Raised,
            ["V2MapBorder"] = c.Line,
            ["V2MapDot"] = c.Accent,
        };
    }

    private static IReadOnlyDictionary<string, string>? DarkTokens() => IsTuned ? ThemeTokens(dark: true) : null;

    private static IReadOnlyDictionary<string, string>? LightTokens() => IsTuned ? ThemeTokens(dark: false) : null;

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
            // The theme builder's preview lives in memory only: the file keeps the look that was open before it.
            var kept = new EditionSettings
            {
                Partner = V2pnPartners.IsPreview(Settings.Partner) ? _beforePreview : Settings.Partner,
                Variants = Settings.Variants.Where(v => !V2pnPartners.IsPreview(v.Key)).ToDictionary(v => v.Key, v => v.Value),
                Looks = Settings.Looks.Where(l => !l.Key.StartsWith(V2pnPartners.PreviewId + ":", StringComparison.Ordinal)).ToDictionary(l => l.Key, l => l.Value),
                Introduced = Settings.Introduced.Where(id => !V2pnPartners.IsPreview(id)).ToList(),
                Minified = Settings.Minified.Where(id => !V2pnPartners.IsPreview(id)).ToList(),
            };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(kept, EditionJson.Default.EditionSettings));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(Editions), ex);
        }
    }
}
