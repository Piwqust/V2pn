using Avalonia.Layout;

namespace v2rayN.Desktop.Views.EditionLooks;

/// <summary>What a partner's screen of its own (theme.layout) does through the window: the same as V2pn's own controls.</summary>
public interface IEditionActions
{
    void ToggleConnect();

    void SetVpn(bool vpn);

    void OpenRoute(Control anchor);

    void Select(string indexId);

    void RunAction(V2pnPartnerAction action, Control anchor);

    void Open(string url);

    /// <summary>Back to V2pn's own layout in this tab, until the person asks for the partner's screen again.</summary>
    void ShowPlain();
}

/// <summary>One server of the open subscription.</summary>
/// <param name="Delay">The last ping in ms; 0 not measured, below 0 no answer.</param>
public sealed record SceneServer(string IndexId, string Name, string? Code, int Delay, bool Selected);

/// <summary>What V2pn's own header and sidebar show right now, for a partner's screen to show the same.</summary>
public sealed record EditionSnapshot
{
    /// <summary>The verdict's class on V2pn's pill: safe, warn, checking or unsafe.</summary>
    public string StateKind { get; init; } = "unsafe";

    public string StateLabel { get; init; } = string.Empty;

    /// <summary>The core runs (or is starting): Connect would disconnect.</summary>
    public bool On { get; init; }

    public bool HasServer { get; init; }

    /// <summary>The header's title (the country) and subtitle (the server).</summary>
    public string Title { get; init; } = string.Empty;

    public string Subtitle { get; init; } = string.Empty;

    public string? CountryCode { get; init; }

    public int Ping { get; init; }

    public bool Vpn { get; init; }

    /// <summary>VPN mode would ask for administrator rights first (V2pn's own switch shows a lock).</summary>
    public bool VpnNeedsAdmin { get; init; }

    public string? VpnTip { get; init; }

    public string Route { get; init; } = string.Empty;

    public IReadOnlyList<SceneServer> Servers { get; init; } = [];

    /// <summary>The partner's button running now, if any.</summary>
    public string? BusyAction { get; init; }

    public SceneAccount? Account { get; init; }
}

/// <summary>A partner's screen of its own: it covers the sidebar and the stage, reads a snapshot and acts through the window.</summary>
public abstract class EditionScene : UserControl
{
    protected EditionScene(V2pnPartner partner, IEditionActions actions)
    {
        Partner = partner;
        Actions = actions;
    }

    protected V2pnPartner Partner { get; }

    protected IEditionActions Actions { get; }

    public abstract void Update(EditionSnapshot s);

    /// <summary>The screen V2pn draws for <paramref name="layout"/>; null for V2pn's own layout.</summary>
    public static EditionScene? For(string layout, V2pnPartner partner, IEditionActions actions) => layout switch
    {
        V2pnThemeLayouts.Stickers => new StickerScene(partner, actions),
        _ => null,
    };

    /// <summary>"Германия · Hysteria2" as its place and the rest; a name without the dot is all place.</summary>
    protected static (string Place, string Detail) SplitName(string name)
    {
        var dot = name.IndexOf(" · ", StringComparison.Ordinal);
        return dot < 0 ? (name, string.Empty) : (name[..dot], name[(dot + 3)..]);
    }

    /// <summary>A partner button's label as its caption and, after the dot, what it does.</summary>
    protected static (string Caption, string Does) SplitLabel(string label)
    {
        var dot = label.IndexOf(" · ", StringComparison.Ordinal);
        return dot < 0 ? (label, string.Empty) : (label[..dot], label[(dot + 3)..]);
    }

    /// <summary>V2pn's own state pill: its colours come from the theme's styles (Border.state.safe and the rest).</summary>
    protected static (Border Pill, TextBlock Text) StatePill()
    {
        var text = new TextBlock();
        var pill = new Border
        {
            Classes = { "state" },
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children = { new Avalonia.Controls.Shapes.Ellipse { Classes = { "statedot" } }, text },
            },
        };
        return (pill, text);
    }

    protected static void SetPill(Border pill, TextBlock text, string kind, string label)
    {
        foreach (var c in new[] { "safe", "unsafe", "warn", "checking" })
        {
            pill.Classes.Set(c, c == kind);
        }
        text.Text = label.ToUpper(CultureInfo.CurrentUICulture);
        Avalonia.Automation.AutomationProperties.SetName(pill, label);
    }
}
