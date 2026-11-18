using Avalonia.Threading;

namespace v2rayN.Desktop.Common;

/// <summary>
/// The tabs in the title bar: V2pn's, and one for each partner the person came from (V2pnEdition: its link,
/// a sign-in by code, or an older partner build's marker) whose pack passed V2pn's checks. With none there are no tabs at all.
/// V2pn's tab is plain V2pn. A partner's tab puts the whole window into that partner's space: its colour (Connect and
/// every accent), its card and plan, with a background in its theme its stage too (MainWindow.Edition.cs), its buttons,
/// its features for its subscriptions, and the list shows its servers. The layout, the state colours and the person's
/// own appearance settings stay V2pn's. The tab chosen last is kept across restarts.
/// </summary>
public static class Editions
{
    /// <summary>Raised on the UI thread after the tabs, the open tab or its partner's theme changed.</summary>
    public static event Action? Changed;

    /// <summary>Raised on the UI thread when the partner whose tab was open went away (with the partner and why); V2pn's tab is open then.</summary>
    public static event Action<V2pnPartner, V2pnPartnerGone>? Closed;

    private static string? _key;

    /// <summary>The partners' tabs in their order (the marker's first, then the sign-ins); empty when the person came from no partner.</summary>
    public static IReadOnlyList<V2pnPartner> Tabs { get; private set; } = [];

    /// <summary>The partner whose tab is open; null in V2pn's tab.</summary>
    public static V2pnPartner? Partner { get; private set; }

    /// <summary>The partner whose look the window wears: the open tab's, when V2pn's listing grants it an edition.</summary>
    public static V2pnPartner? Look => Partner is { HasEdition: true } partner ? partner : null;

    /// <summary>The look's theme; null for V2pn's own.</summary>
    public static V2pnTheme? Theme => Look is { } partner ? ThemeOf(partner) : null;

    public static V2pnTheme ThemeOf(V2pnPartner partner) => partner.Pack.Theme ?? V2pnTheme.Default(partner.Pack.Accent);

    /// <summary>The partner's background where V2pn's map would be, in the light or dark mode; null without one.</summary>
    public static V2pnPartnerStage? Stage(bool dark) =>
        Theme is { } theme && !DebugOverrides.NoStage ? V2pnThemePalette.Stage(theme, dark) : null;

    /// <summary>
    /// The partner's own colour for Connect and the accents; turned away from the state hues when it is close to them,
    /// so a green partner never paints Connect as "protected".
    /// </summary>
    public static string PartnerAccent => AccentOf(Partner);

    public static string AccentOf(V2pnPartner? partner)
    {
        if (partner?.Pack.Theme is { } theme)
        {
            var brand = V2pnColor.TryParse(theme.Brand.FirstOrDefault()) ?? V2pnColor.Parse(V2pnThemePalette.FallbackBrand);
            return V2pnThemePalette.AwayFromState(brand).Hex;
        }
        if (partner?.Pack.Accent is not { } hex || !Color.TryParse(hex, out var colour))
        {
            return "#FF3D1F";
        }
        var safe = Appearance.AwayFromStatus(colour);
        return safe == colour ? hex : $"#{safe.R:X2}{safe.G:X2}{safe.B:X2}";
    }

    private static bool _started;
    private static DispatcherTimer? _pruneTimer;

    /// <summary>Loads the kept partners and follows their changes; the window calls it once.</summary>
    public static void Start()
    {
        if (_started)
        {
            return;
        }
        _started = true;
        V2pnPartners.Instance.Changed += () => Dispatcher.UIThread.Post(Update);
        V2pnPartners.Instance.Removed += (partner, why) => Dispatcher.UIThread.Post(() => OnRemoved(partner, why));
        // A sign-in whose last subscription went while V2pn was closed is forgotten now; then the index (at most every
        // six hours: RefreshAsync keeps to it). For a person who came from no partner neither asks for anything.
        _ = Task.Run(async () =>
        {
            await V2pnPartners.Instance.PruneAsync();
            await V2pnPartners.Instance.RefreshAsync();
        });
        // A term that runs out while V2pn is open is caught by the hourly check.
        _pruneTimer = new DispatcherTimer(TimeSpan.FromHours(1), DispatcherPriority.Background, (_, _) => _ = V2pnPartners.Instance.RefreshAsync());
        _pruneTimer.Start();
        Update();
    }

    /// <summary>Opens a partner's tab, or V2pn's (null). At once: it is a frequent action.</summary>
    public static void Open(string? partnerId)
    {
        if (partnerId != null && Tabs.All(p => p.Id != partnerId))
        {
            return;
        }
        V2pnEdition.Instance.Open(partnerId);
        // The edition's change reaches V2pnPartners' listeners through a post; the window follows without waiting.
        Update();
    }

    /// <summary>The next tab (<paramref name="step"/> 1) or the one before (-1), round: Ctrl+Tab and Ctrl+Shift+Tab.</summary>
    public static void Cycle(int step)
    {
        if (Tabs.Count == 0)
        {
            return;
        }
        var ids = new List<string?> { null };
        ids.AddRange(Tabs.Select(p => p.Id));
        var at = ids.IndexOf(Partner?.Id);
        Open(ids[(at + step + ids.Count) % ids.Count]);
    }

    private static void OnRemoved(V2pnPartner partner, V2pnPartnerGone why)
    {
        var shown = Partner?.Id == partner.Id;
        if (shown && why != V2pnPartnerGone.Unlinked)
        {
            // Out of the index or run out: V2pn's tab, kept so; the partner's tab comes back if it is listed again.
            V2pnEdition.Instance.Open(null);
        }
        Update();
        if (shown)
        {
            Closed?.Invoke(partner, why);
        }
    }

    /// <summary>Debug: the partner leaves as it would when <paramref name="why"/> happens (its tab closes with the note); its files stay.</summary>
    public static void DebugClose(V2pnPartner partner, V2pnPartnerGone why)
    {
        var shown = Partner?.Id == partner.Id;
        V2pnEdition.Instance.Open(null);
        Update();
        if (shown)
        {
            Closed?.Invoke(partner, why);
        }
    }

    /// <summary>Takes the tabs and the open one again; also after a debug override changed.</summary>
    public static void Update()
    {
        var edition = V2pnEdition.Instance;
        var tabs = edition.CameFrom.Select(V2pnPartners.Instance.Get).OfType<V2pnPartner>().ToList();
        var open = edition.Tab;
        var next = tabs.FirstOrDefault(p => p.Id == open);
        var look = next is { HasEdition: true } ? next : null;
        // The folder and the theme too: a pack updated in place brings new pictures and colours.
        var key = string.Join(";", tabs.Select(p => $"{p.Id}|{p.Folder}|{p.Pack.Name}|{p.Pack.Mark}|{p.Pack.Accent}|{ThemeOf(p).Key}"))
                  + $"#{next?.Id}|{look != null}|{DebugOverrides.NoStage}";
        if (key == _key)
        {
            return;
        }
        _key = key;
        Tabs = tabs;
        Partner = next;
        var accent = look == null ? null : AccentOf(look);
        // Only the accent: the person's palette, corners, type and light or dark stay as they set them.
        Appearance.SetEdition(accent == null ? null : settings =>
        {
            settings.Accent = accent;
            return settings;
        });
        Changed?.Invoke();
    }
}
