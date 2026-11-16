using Avalonia.Threading;

namespace v2rayN.Desktop.Common;

/// <summary>
/// A partner's brand on its own build of V2pn (V2pnEdition): the window always wears that partner's colour (Connect and
/// every accent), its logo beside V2pn's and its card; with a background in its theme, its stage too
/// (MainWindow.Edition.cs), whatever server is selected. Plain V2pn never wears a brand, whatever subscription is there.
/// Nobody picks it: the build is fixed at install. The layout, the state colours and the person's own appearance
/// settings stay V2pn's.
/// </summary>
public static class Editions
{
    /// <summary>Raised on the UI thread after the partner in the window or its theme changed.</summary>
    public static event Action? Changed;

    /// <summary>Raised on the UI thread when the partner in the window went away (with the partner and why).</summary>
    public static event Action<V2pnPartner, V2pnPartnerGone>? Closed;

    // Debug only (DebugWindow): "" shows the window as before the partner's pack came, the build's partner id shows it,
    // null is the build's own state.
    private static string? _forced;
    private static string? _key;

    /// <summary>This build's partner when its certificate grants an edition; empty in plain V2pn.</summary>
    public static IReadOnlyList<V2pnPartner> Available =>
        V2pnEdition.PartnerId is { } own ? V2pnPartners.Instance.All.Where(p => p.Id == own && p.HasEdition).ToList() : [];

    /// <summary>The partner whose brand the window wears now; null for plain V2pn.</summary>
    public static V2pnPartner? Partner { get; private set; }

    /// <summary>The partner's theme; null for plain V2pn.</summary>
    public static V2pnTheme? Theme => Partner is { } partner ? ThemeOf(partner) : null;

    public static V2pnTheme ThemeOf(V2pnPartner partner) => partner.Pack.Theme ?? V2pnTheme.Default(partner.Pack.Accent);

    /// <summary>The partner's background where V2pn's map would be, in the light or dark mode; null without one.</summary>
    public static V2pnPartnerStage? Stage(bool dark) =>
        Theme is { } theme && !DebugOverrides.NoStage ? V2pnThemePalette.Stage(theme, dark) : null;

    /// <summary>True while the debug window overrides the build's own state.</summary>
    public static bool IsForced => _forced != null;

    /// <summary>
    /// The partner's own colour for Connect and the accents; turned away from the state hues when it is close to them,
    /// so a green partner never paints Connect as "protected".
    /// </summary>
    public static string PartnerAccent
    {
        get
        {
            if (Partner?.Pack.Theme is { } theme)
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

    private static bool _started;
    private static DispatcherTimer? _pruneTimer;

    /// <summary>Loads the kept partner and follows its changes; the window calls it once. Plain V2pn has nothing to follow.</summary>
    public static void Start()
    {
        if (_started)
        {
            return;
        }
        _started = true;
        if (V2pnEdition.PartnerId == null)
        {
            // Plain V2pn: what an older version kept of a partner goes (V2pnPartners), nothing is asked for.
            _ = V2pnPartners.Instance.LoadAsync();
            return;
        }
        V2pnPartners.Instance.Changed += () => Dispatcher.UIThread.Post(Update);
        V2pnPartners.Instance.Removed += (partner, why) => Dispatcher.UIThread.Post(() => OnRemoved(partner, why));
        // The index of partners now (then at most every six hours: RefreshAsync keeps to it); a term that runs out while
        // V2pn is open is caught by the hourly check.
        _ = V2pnPartners.Instance.RefreshAsync();
        _pruneTimer = new DispatcherTimer(TimeSpan.FromHours(1), DispatcherPriority.Background, (_, _) => _ = V2pnPartners.Instance.RefreshAsync());
        _pruneTimer.Start();
        Update();
    }


    private static void OnRemoved(V2pnPartner partner, V2pnPartnerGone why)
    {
        var shown = Partner?.Id == partner.Id;
        Update();
        if (shown)
        {
            Closed?.Invoke(partner, why);
        }
    }

    /// <summary>Debug: shows the build's partner (<paramref name="partnerId"/>); "" shows the window without it, null the build's own state.</summary>
    public static void DebugShow(string? partnerId)
    {
        _forced = partnerId;
        Update();
    }

    /// <summary>Debug: the partner leaves the window as it would when <paramref name="why"/> happens; its files stay.</summary>
    public static void DebugClose(V2pnPartner partner, V2pnPartnerGone why)
    {
        _forced = string.Empty;
        Update();
        Closed?.Invoke(partner, why);
    }

    /// <summary>Takes the brand again from the build's partner; also after a debug override changed.</summary>
    public static void Update()
    {
        // Available is empty in plain V2pn, so nothing here can dress it; a partner's build wears its own partner only.
        var own = Available.FirstOrDefault();
        var next = _forced != null ? (own?.Id == _forced ? own : null) : own;
        // The folder and the theme too: a pack updated in place brings new pictures and colours.
        var key = next == null ? null : $"{next.Id}|{next.Folder}|{next.Pack.Accent}|{ThemeOf(next).Key}|{DebugOverrides.NoStage}";
        if (key == _key)
        {
            return;
        }
        _key = key;
        Partner = next;
        var accent = next == null ? null : PartnerAccent;
        // Only the accent: the person's palette, corners, type and light or dark stay as they set them.
        Appearance.SetEdition(accent == null ? null : settings =>
        {
            settings.Accent = accent;
            return settings;
        });
        Changed?.Invoke();
    }
}
