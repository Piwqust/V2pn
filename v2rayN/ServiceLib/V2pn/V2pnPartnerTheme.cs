namespace ServiceLib;

/// <summary>How the Neon look draws Connect: INCY's two styles.</summary>
public static class V2pnThemeConnect
{
    /// <summary>A big round button with a ring and the timer in it.</summary>
    public const string Round = "round";

    /// <summary>One row with the flag, the server and a switch; the list takes the rest of the window.</summary>
    public const string Toggle = "toggle";

    public static readonly IReadOnlyList<string> All = [Round, Toggle];
}

/// <summary>The window the Neon look opens in the first time.</summary>
public static class V2pnThemeWindow
{
    /// <summary>The narrow window (380×640), like a phone at the edge of the screen; the person can widen it.</summary>
    public const string Mini = "mini";

    /// <summary>The window as the person last had it.</summary>
    public const string Full = "full";

    public static readonly IReadOnlyList<string> All = [Mini, Full];
}

public static class V2pnThemeBackgroundKinds
{
    public const string Solid = "solid";
    public const string Gradient = "gradient";
    public const string Image = "image";

    public static readonly IReadOnlyList<string> All = [Solid, Gradient, Image];
}

/// <summary>The picture slot of a theme.</summary>
public static class V2pnThemeArt
{
    /// <summary>background.art: the picture of the brand area (the Pass card), 640-3200 px a side, at most 3:1.</summary>
    public const string Backdrop = "backdrop";
}

/// <summary>theme.background: the brand area of the Pass card. A colour, a gradient or a picture (with a scrim V2pn sets).</summary>
/// <param name="Kind">solid, gradient or image.</param>
/// <param name="Colors">The gradient's 2 to 4 stops, "#RRGGBB"; empty otherwise.</param>
/// <param name="Art">The picture (a pack asset) for an image background.</param>
public sealed record V2pnThemeBackground(string Kind, IReadOnlyList<string> Colors, string? Art)
{
    public static readonly V2pnThemeBackground Solid = new(V2pnThemeBackgroundKinds.Solid, [], null);
}

/// <summary>
/// A provider's theme (partner.json "theme", with the edition grant). V2pn draws two looks, Neon and Pass, and lays them
/// out itself; the theme gives them the provider's colours, the brand area of the Pass card, Neon's Connect and the
/// window Neon opens in. Every field has a safe value: whatever a pack gets wrong falls back to it, and
/// <see cref="Notes"/> says what and why.
/// </summary>
/// <param name="Brand">1 to 3 brand colours: the accent first, then the colours of Neon's ring.</param>
public sealed record V2pnTheme(
    IReadOnlyList<string> Brand,
    V2pnThemeBackground Background,
    string Connect,
    string Window)
{
    /// <summary>What V2pn did not take from the pack and what it used instead, one line each (tools/partner-check.cs).</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>A theme of V2pn's defaults in <paramref name="brand"/> (or no brand colour, which V2pn fills in).</summary>
    public static V2pnTheme Default(string? brand) => new(
        V2pnColor.TryParse(brand) is { } c ? [c.Hex] : [],
        V2pnThemeBackground.Solid,
        V2pnThemeConnect.Round,
        V2pnThemeWindow.Mini);

    /// <summary>One string that changes whenever anything that is drawn changes (for rebuilding a look).</summary>
    public string Key => string.Join("|", string.Join(",", Brand), Background.Kind, string.Join(",", Background.Colors),
        Background.Art, Connect, Window);
}

/// <summary>Reads partner.json's "theme" strictly: known values only, within limits; anything else falls back.</summary>
public static class V2pnThemeReader
{
    public const int MaxBrand = 3;
    public const int MinStops = 2;
    public const int MaxStops = 4;
    private const int MaxText = 40;

    private static readonly HashSet<string> Keys = ["palette", "background", "connect", "window"];

    // Fields of the six-template themes V2pn drew before Neon and Pass: read without error, used no more.
    private static readonly HashSet<string> Retired = ["template", "font", "corners", "density", "blocks", "art"];

    /// <summary>
    /// The theme of <paramref name="node"/>; null node gives V2pn's default theme. <paramref name="accent"/> (brand.accent)
    /// is the brand colour when the theme names none. <paramref name="isPicture"/> tells a picture asset of the pack.
    /// </summary>
    public static V2pnTheme Parse(JsonNode? node, string? accent, Func<string, bool> isPicture)
    {
        var notes = new List<string>();
        if (node is not JsonObject root)
        {
            if (node != null)
            {
                notes.Add("theme is not an object; V2pn's default theme is used");
            }
            return V2pnTheme.Default(accent) with { Notes = notes };
        }
        foreach (var (key, _) in root)
        {
            if (Retired.Contains(key))
            {
                notes.Add($"theme.{key} is no longer used: V2pn lays out its two looks (neon, pass) itself; ignored");
            }
            else if (!Keys.Contains(key))
            {
                notes.Add($"theme.{Clip(key)} is not a field V2pn knows; ignored");
            }
        }

        var palette = root["palette"] as JsonObject;
        if (root["palette"] != null && palette == null)
        {
            notes.Add("theme.palette is not an object; the brand colour of the pack is used");
        }
        if (palette?["tone"] != null)
        {
            notes.Add("theme.palette.tone is no longer used: V2pn picks the neutrals from the brand colour; ignored");
        }
        var brand = Brand(palette?["brand"], notes);
        if (brand.Count == 0 && V2pnColor.TryParse(accent) is { } fallback)
        {
            brand.Add(fallback.Hex);
        }
        var background = Background(root["background"], isPicture, notes);
        var connect = Pick(root["connect"], V2pnThemeConnect.All, V2pnThemeConnect.Round, "theme.connect", notes);
        var window = Pick(root["window"], V2pnThemeWindow.All, V2pnThemeWindow.Mini, "theme.window", notes);

        return new V2pnTheme(brand, background, connect, window) { Notes = notes };
    }

    private static string Pick(JsonNode? node, IReadOnlyList<string> allowed, string fallback, string field, List<string> notes)
    {
        if (node == null)
        {
            return fallback;
        }
        var value = Text(node)?.Trim().ToLowerInvariant();
        if (value != null && allowed.Contains(value))
        {
            return value;
        }
        notes.Add($"{field} \"{Clip(value ?? node.ToJsonString())}\" is not one of {string.Join(", ", allowed)}; \"{fallback}\" is used");
        return fallback;
    }

    private static List<string> Brand(JsonNode? node, List<string> notes)
    {
        var colours = new List<string>();
        if (node == null)
        {
            return colours;
        }
        // One colour as a plain string is read as a list of one.
        var items = node is JsonArray array ? array.ToList() : [node];
        foreach (var item in items)
        {
            var hex = Text(item);
            if (V2pnColor.TryParse(hex) is not { } colour)
            {
                notes.Add($"theme.palette.brand: \"{Clip(hex ?? item?.ToJsonString() ?? "null")}\" is not a #RRGGBB colour; left out");
                continue;
            }
            if (colours.Count == MaxBrand)
            {
                notes.Add($"theme.palette.brand: at most {MaxBrand} colours; {colour.Hex} and the rest are left out");
                break;
            }
            if (!colours.Contains(colour.Hex))
            {
                colours.Add(colour.Hex);
            }
        }
        return colours;
    }

    private static V2pnThemeBackground Background(JsonNode? node, Func<string, bool> isPicture, List<string> notes)
    {
        if (node == null)
        {
            return V2pnThemeBackground.Solid;
        }
        if (node is not JsonObject background)
        {
            notes.Add("theme.background is not an object; a solid brand colour is used");
            return V2pnThemeBackground.Solid;
        }
        var kind = Pick(background["type"], V2pnThemeBackgroundKinds.All, V2pnThemeBackgroundKinds.Solid, "theme.background.type", notes);
        switch (kind)
        {
            case V2pnThemeBackgroundKinds.Gradient:
            {
                var stops = new List<string>();
                var bad = false;
                if (background["colors"] is JsonArray array)
                {
                    foreach (var item in array)
                    {
                        if (V2pnColor.TryParse(Text(item)) is { } c)
                        {
                            stops.Add(c.Hex);
                        }
                        else
                        {
                            bad = true;
                        }
                    }
                }
                if (bad || stops.Count is < MinStops or > MaxStops)
                {
                    notes.Add($"theme.background.colors needs {MinStops} to {MaxStops} #RRGGBB colours; a solid brand colour is used");
                    return V2pnThemeBackground.Solid;
                }
                return new V2pnThemeBackground(kind, stops, null);
            }
            case V2pnThemeBackgroundKinds.Image:
            {
                var art = Text(background["art"]);
                if (art == null || !isPicture(art))
                {
                    notes.Add($"theme.background.art \"{Clip(art ?? "")}\" is not a picture of the pack's assets; a solid brand colour is used");
                    return V2pnThemeBackground.Solid;
                }
                return new V2pnThemeBackground(kind, [], art);
            }
            default:
                return V2pnThemeBackground.Solid;
        }
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && text.Length <= 256 ? text : null;

    private static string Clip(string text) => text.Length <= MaxText ? text : text[..MaxText] + "…";
}
