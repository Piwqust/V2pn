namespace ServiceLib.Models.Configs;

/// <summary>
/// V2pn: its own settings in the UI section of guiNConfig.json. Kept apart from v2rayN's UIItem (ConfigItems.cs), so
/// a merge from v2rayN never meets them; the JSON is the same as when they lived there.
/// </summary>
public partial class UIItem
{
    // V2pn: how Connect protects the computer. The live state stays in SystemProxyItem/TunModeItem;
    // these remember the choice while disconnected.
    public bool V2pnPreferTun { get; set; }

    // Null until first used: then taken from the mode v2rayN had (On, PAC or Keep).
    public ESysProxyType? V2pnProxyMethod { get; set; }

    public bool V2pnBlockAds { get; set; }

    // V2pn: the partner routing presets ("v2pn-partner-<id>") already offered once with a note.
    public List<string>? V2pnPartnerRoutesOffered { get; set; }

    // Settings window shows the expert rows and pages.
    public bool V2pnSettingsAdvanced { get; set; }

    public List<string>? V2pnRecent { get; set; }

    // Server statistics in the sidebar: off until the user turns them on.
    public bool V2pnStats { get; set; }

    // Sidebar list order (SideSort in the desktop app): of countries, null by name; of servers, null by ping.
    public int? V2pnSort { get; set; }

    public int? V2pnServerSort { get; set; }

    // Unix seconds of the last finished ping run, so "fastest" can tell an old result from a fresh one.
    public long V2pnLastPing { get; set; }

    // Null means on: new versions download in the background and install at the next start.
    public bool? V2pnAutoUpdate { get; set; }

    /// <summary>V2pn: also take beta versions (the beta channel, V2pnUpdateSettings.Manifests); off unless the person turns it on.</summary>
    public bool V2pnBeta { get; set; }

    /// <summary>V2pn: sites the person sends one way whatever the preset says (V2pnPresetRules).</summary>
    public List<V2pnSiteRule>? V2pnSiteRules { get; set; }

    /// <summary>V2pn: programs that always go directly while VPN mode is on (V2pnPresetRules).</summary>
    public List<V2pnAppRule>? V2pnDirectApps { get; set; }

    /// <summary>V2pn: switch to a working server of the same country by itself (V2pnAutoSwitch); null means on.</summary>
    public bool? V2pnAutoSwitch { get; set; }

    /// <summary>V2pn: tell V2pn's server when an update fails (V2pnUpdateReport); off unless the person turns it on.</summary>
    public bool V2pnReportUpdateFailures { get; set; }

    /// <summary>V2pn: the failures already told, "version|step".</summary>
    public List<string>? V2pnReportedFailures { get; set; }

    /// <summary>V2pn: what v2pnctl and AI assistants may do over the control channel (V2pnControlHandler); off by default.</summary>
    public EV2pnControlLevel V2pnControl { get; set; }

    // Developer mode (Windows): the Updates window builds V2pn from this source folder and installs it.
    public bool V2pnDevMode { get; set; }

    public string? V2pnDevSource { get; set; }

    // The close-to-tray question was answered once (Windows); until then X hides, as it always did.
    public bool V2pnCloseAsked { get; set; }

    // The sign-in entry carries Global.AutostartArg. Until it does, a launch without the argument may
    // still be the sign-in one, so "start in the tray" keeps applying to every launch.
    public bool V2pnAutostartArg { get; set; }

    // The one-time notice that the proxy is open to the local network without a password was shown.
    public bool V2pnLanWarned { get; set; }

    // The first-start walkthrough was finished or skipped; people who had servers before it existed count as done.
    public bool V2pnOnboarded { get; set; }

    // The walkthrough step to come back to after a restart it caused (a language switch, a move from v2rayN).
    public int V2pnOnboardingStep { get; set; }

    // The partners the person came from by a sign-in by code or a provider's link; each gets a tab in the title bar
    // (V2pnEdition).
    public List<string>? V2pnCameFrom { get; set; }

    // The title bar's tab chosen last: null never chosen, "" V2pn's, a partner id that partner's (V2pnEdition).
    public string? V2pnTab { get; set; }

    // The partner of the v2pn-partner.txt a partner's build of V2pn 1.2 carried, moved here at the first start of 1.3:
    // the person came from it for good, as the file said (V2pnEdition.Marker). Nothing writes a new one.
    public string? V2pnMarker { get; set; }
}
