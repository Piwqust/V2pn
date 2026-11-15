using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Threading;

namespace v2rayN.Desktop.Common;

/// <summary>Which edition the window wears, and which look each partner's edition was last left in.</summary>
public sealed class EditionSettings
{
    /// <summary>The partner whose edition is open; null for plain V2pn.</summary>
    public string? Partner { get; set; }

    /// <summary>Partner id → the look (V2pnPartnerVariants) the person picked for it.</summary>
    public Dictionary<string, string> Variants { get; set; } = [];
}

[JsonSerializable(typeof(EditionSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class EditionJson : JsonSerializerContext;

/// <summary>
/// Partner editions: V2pn dressed as a verified partner (V2pnPartners). The person switches between plain
/// V2pn and each partner from the name in the title bar or with Ctrl+1…9; the connection, settings and
/// servers stay the same, only the look changes. A partner only picks among looks V2pn draws itself.
/// </summary>
public static class Editions
{
    private const string FileName = "V2pnEdition.json";
    private static EditionSettings? _settings;

    /// <summary>Raised on the UI thread after the edition, its look or the set of partners changed.</summary>
    public static event Action? Changed;

    private static EditionSettings Settings => _settings ??= Load();

    /// <summary>Partners whose certificate grants an edition, in the order the switcher lists them.</summary>
    public static IReadOnlyList<V2pnPartner> Available => V2pnPartners.Instance.All.Where(p => p.HasEdition).ToList();

    /// <summary>The partner whose edition is open; null for plain V2pn, also when that partner is no longer verified.</summary>
    public static V2pnPartner? Partner => Settings.Partner is { } id ? Available.FirstOrDefault(p => p.Id == id) : null;

    public static string Variant => Partner is { } partner ? VariantOf(partner) : string.Empty;

    /// <summary>True when the open look replaces the sidebar and the map with a scene of its own.</summary>
    public static bool IsScene => Variant is V2pnPartnerVariants.Cabinet or V2pnPartnerVariants.Studio or V2pnPartnerVariants.Ascii;

    public static string VariantOf(V2pnPartner partner) =>
        Settings.Variants.TryGetValue(partner.Id, out var chosen) && partner.Pack.Variants.Contains(chosen)
            ? chosen
            : partner.Pack.DefaultVariant;

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
        _ = V2pnPartners.Instance.LoadAsync();
        Apply();
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

    private static void Apply()
    {
        Appearance.SetEdition(Overlay(), Tokens());
        Changed?.Invoke();
    }

    /// <summary>
    /// How the edition changes the person's appearance. The signature look keeps V2pn's own; every other look takes
    /// the partner's warm ground, its accent and its type. State colours (protected green, unprotected red) are
    /// never part of a pack, so they stay V2pn's whatever the partner's accent is.
    /// </summary>
    private static Func<AppearanceSettings, AppearanceSettings>? Overlay()
    {
        if (Partner is not { } partner || Variant == V2pnPartnerVariants.Signature)
        {
            return null;
        }
        var accent = partner.Pack.Accent;
        return settings =>
        {
            settings.Palette = "warm";
            if (accent != null)
            {
                settings.Accent = accent;
            }
            settings.Font = "geist";
            settings.Headings = "geist";
            return settings;
        };
    }

    /// <summary>
    /// The warm look's own grounds: the partner's near-black, its map in warm greys with its servers in its accent.
    /// Only the warm look and the scenes' window chrome take them; the signature look stays V2pn's.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? Tokens()
    {
        if (Partner is not { } partner || Variant == V2pnPartnerVariants.Signature)
        {
            return null;
        }
        var accent = partner.Pack.Accent ?? "#FF3D1F";
        return new Dictionary<string, string>
        {
            ["V2WindowBackground"] = "#0B0B0A",
            ["V2SidebarBackground"] = "#171716",
            ["V2ContentBackground"] = "#0E0E0D",
            ["V2CardBackground"] = "#1C1B19",
            ["V2CardMutedBackground"] = "#232321",
            ["V2Border"] = "#2A2926",
            ["V2PanelBorder"] = "#22211E",
            ["V2MapOcean"] = "#0E0E0D",
            ["V2MapLand"] = "#262421",
            ["V2MapBorder"] = "#34312C",
            ["V2MapDot"] = accent,
            ["V2RowSelected"] = "#2A2420",
            ["V2RowSelectedBorder"] = "#5A3A26",
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
