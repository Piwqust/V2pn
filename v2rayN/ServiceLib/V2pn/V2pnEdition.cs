namespace ServiceLib;

/// <summary>
/// The partners a person came from, and which tab of the main window is open. V2pn is one program for everyone; a
/// partner gets a tab in its title bar beside V2pn's only when the person came from it:
/// <list type="bullet">
/// <item>the person signed in by code with that partner (V2pnPartnerLogin), or followed its link
/// (v2pn://add?url=…&amp;partner=&lt;id&gt; from its page or bot, V2pnPartners.ComeFromLinkAsync) to a subscription on its
/// domains: recorded in V2pn's settings (UIItem.V2pnCameFrom). This record goes when the last subscription from that
/// partner is deleted (V2pnPartners.PruneAsync), and with it the tab.</item>
/// <item>an older partner's build said so: V2pn 1.2 had partner packages with v2pn-partner.txt beside the program (on
/// macOS in V2pn.app/Contents/MacOS). Nothing makes such a file any more; one found at start is moved into the settings
/// (UIItem.V2pnMarker) and deleted (<see cref="RetireMarkerFileAsync"/>), so whoever installed one keeps the tab. That
/// record never goes away by itself.</item>
/// </list>
/// A pasted subscription link of a partner makes nobody "come from" it: it is just a subscription. A person who came
/// from nobody gets no tab, and V2pn asks for no partner index and no pack for them (V2pnPartners).
/// <para>
/// The open tab is V2pn's (null) or one partner's. Every partner feature asks <see cref="IsOpen"/>: a partner's look,
/// card, buttons, lock, routes, API and connection settings apply only in its own tab and only to its subscriptions;
/// the V2pn tab is plain V2pn. The tab chosen last is kept in UIItem.V2pnTab ("" for V2pn's); before any choice a
/// person with a marker gets its partner's tab.
/// </para>
/// </summary>
public sealed class V2pnEdition
{
    public const string FileName = "v2pn-partner.txt";
    private const int MaxBytes = 1024;

    private static readonly Lazy<V2pnEdition> _instance = new(FromConfig);

    /// <summary>The program's own: the person's record, marker and tab from the config.</summary>
    public static V2pnEdition Instance => _instance.Value;

    private readonly object _lock = new();
    private readonly Action<V2pnEdition>? _save;
    private readonly List<string> _signedIn;
    // Debug only (V2pnPartners.AddDebugPartner): counted as came-from until V2pn closes, never saved.
    private readonly List<string> _debug = [];
    // Null: never chosen; "": V2pn's tab; an id: that partner's.
    private string? _tab;

    /// <param name="marker">The partner of an older build's v2pn-partner.txt, as moved into the settings; null without one.</param>
    /// <param name="signedIn">The partners the person signed in with or followed the link of, as saved.</param>
    /// <param name="tab">The tab as saved: null never chosen, "" V2pn's, an id a partner's.</param>
    /// <param name="save">Writes <see cref="SignedIn"/> and <see cref="SavedTab"/> where they are kept.</param>
    public V2pnEdition(string? marker, IEnumerable<string>? signedIn = null, string? tab = null, Action<V2pnEdition>? save = null)
    {
        Marker = marker != null && V2pnPartnerTrust.ValidId(marker) ? marker : null;
        _signedIn = (signedIn ?? []).Where(V2pnPartnerTrust.ValidId).Distinct(StringComparer.Ordinal).ToList();
        _tab = tab == null || tab.Length == 0 || V2pnPartnerTrust.ValidId(tab) ? tab : null;
        _save = save;
    }

    /// <summary>Raised after the partners came from or the open tab changed. May come from any thread.</summary>
    public event Action? Changed;

    /// <summary>The partner of the v2pn-partner.txt an older partner's build carried (UIItem.V2pnMarker); null for none.</summary>
    public string? Marker { get; }

    /// <summary>
    /// The partners the person signed in with by code or followed the link of, oldest first (the marker's partner may be
    /// among them).
    /// </summary>
    public IReadOnlyList<string> SignedIn
    {
        get
        {
            lock (_lock)
            {
                return _signedIn.ToList();
            }
        }
    }

    /// <summary>Every partner the person came from: the marker's first, then the sign-ins in their order. The tabs' order.</summary>
    public IReadOnlyList<string> CameFrom
    {
        get
        {
            lock (_lock)
            {
                var all = new List<string>();
                if (Marker != null)
                {
                    all.Add(Marker);
                }
                foreach (var id in _signedIn.Concat(_debug))
                {
                    if (!all.Contains(id))
                    {
                        all.Add(id);
                    }
                }
                return all;
            }
        }
    }

    /// <summary>True when the person came from at least one partner: only then does V2pn ask for the partner index.</summary>
    public bool Any => CameFrom.Count > 0;

    public bool IsFrom(string? partnerId) => partnerId != null && CameFrom.Contains(partnerId);

    /// <summary>The tab as kept: null never chosen, "" V2pn's, an id a partner's.</summary>
    public string? SavedTab
    {
        get
        {
            lock (_lock)
            {
                return _tab;
            }
        }
    }

    /// <summary>
    /// The partner whose tab is open; null for V2pn's. Before any choice, the marker's partner. A partner the person
    /// no longer comes from is never open.
    /// </summary>
    public string? Tab
    {
        get
        {
            var tab = SavedTab ?? Marker;
            return tab is { Length: > 0 } && IsFrom(tab) ? tab : null;
        }
    }

    /// <summary>True when <paramref name="partnerId"/>'s tab is open: the one question every partner feature asks.</summary>
    public bool IsOpen(string? partnerId) => partnerId != null && Tab == partnerId;

    /// <summary>Opens a partner's tab (<paramref name="partnerId"/>), or V2pn's (null); a partner the person did not come from is refused.</summary>
    public bool Open(string? partnerId)
    {
        if (partnerId != null && !IsFrom(partnerId))
        {
            return false;
        }
        lock (_lock)
        {
            var next = partnerId ?? string.Empty;
            if (_tab == next)
            {
                return true;
            }
            _tab = next;
        }
        Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// The person signed in by code with <paramref name="partnerId"/>, or followed its link to a subscription on its domains:
    /// from now on they came from it. False when they already had.
    /// </summary>
    public bool RecordSignIn(string partnerId)
    {
        if (!V2pnPartnerTrust.ValidId(partnerId))
        {
            return false;
        }
        lock (_lock)
        {
            if (_signedIn.Contains(partnerId))
            {
                return false;
            }
            _signedIn.Add(partnerId);
        }
        Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Drops the record of a sign-in (its last subscription was deleted). The marker's partner stays: the build came
    /// from it. A tab that was open on a partner the person no longer comes from goes back to V2pn's. False when there
    /// was no record.
    /// </summary>
    public bool ForgetSignIn(string partnerId)
    {
        lock (_lock)
        {
            if (!_signedIn.Remove(partnerId))
            {
                return false;
            }
            if (_tab == partnerId && partnerId != Marker && !_debug.Contains(partnerId))
            {
                _tab = string.Empty;
            }
        }
        Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Debug only: counts <paramref name="partnerId"/> as came-from until V2pn closes (the debug window's test partner).</summary>
    public void AddDebug(string partnerId)
    {
        lock (_lock)
        {
            if (_debug.Contains(partnerId))
            {
                return;
            }
            _debug.Add(partnerId);
        }
        Changed?.Invoke();
    }

    public void RemoveDebug(string partnerId)
    {
        bool removed;
        lock (_lock)
        {
            removed = _debug.Remove(partnerId);
        }
        if (removed)
        {
            Changed?.Invoke();
        }
    }

    private void Save()
    {
        try
        {
            _save?.Invoke(this);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnEdition), ex);
        }
    }

    private static V2pnEdition FromConfig()
    {
        var config = ConfigTextHandler.Config;
        var ui = config?.UiItem;
        var appDir = Utils.GetBaseDirectory();
        // An older partner's build: its marker moves into the settings, then the file goes once they are saved.
        if (config != null && Read(appDir) is { } fileMarker)
        {
            config.UiItem.V2pnMarker = fileMarker;
            _ = RetireMarkerFileAsync(appDir, async () => await ConfigTextHandler.SaveConfig() == 0);
        }
        return new V2pnEdition(ui?.V2pnMarker, ui?.V2pnCameFrom, ui?.V2pnTab, edition =>
        {
            if (ConfigTextHandler.Config is not { } config)
            {
                return;
            }
            config.UiItem.V2pnCameFrom = edition.SignedIn.ToList();
            config.UiItem.V2pnTab = edition.SavedTab;
            _ = ConfigTextHandler.SaveConfig();
        });
    }

    /// <summary>
    /// Deletes the v2pn-partner.txt in <paramref name="appDir"/> once <paramref name="saved"/> says its partner is kept in
    /// the settings, so a failed save leaves it to be read again at the next start. Logged; never throws. False when the
    /// file stays.
    /// </summary>
    public static async Task<bool> RetireMarkerFileAsync(string appDir, Func<Task<bool>> saved)
    {
        var file = Path.Combine(appDir, FileName);
        try
        {
            if (!await saved())
            {
                Logging.SaveLog($"{nameof(V2pnEdition)}: the settings were not saved; {FileName} stays for the next start");
                return false;
            }
            File.Delete(file);
            Logging.SaveLog($"{nameof(V2pnEdition)}: {FileName} moved into the settings and deleted");
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnEdition), ex);
            return false;
        }
    }

    /// <summary>
    /// The partner id in the text of a marker: its first line that is not empty or a # comment, which must be a partner
    /// id (2 to 32 lowercase letters, digits and dashes). Anything else marks no partner: null.
    /// </summary>
    public static string? Parse(string? text)
    {
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿').Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            return V2pnPartnerTrust.ValidId(line) ? line : null;
        }
        return null;
    }

    /// <summary>The partner id of the marker in <paramref name="appDir"/>; null when there is none or it does not read (larger than 1 KB, no id in it).</summary>
    public static string? Read(string appDir)
    {
        try
        {
            var file = Path.Combine(appDir, FileName);
            return File.Exists(file) && new FileInfo(file).Length <= MaxBytes ? Parse(File.ReadAllText(file)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
