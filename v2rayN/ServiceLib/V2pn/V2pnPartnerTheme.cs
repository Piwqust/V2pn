namespace ServiceLib;

/// <summary>The main-screen layouts a provider theme picks from. V2pn draws each one at every window width, light and dark.</summary>
public static class V2pnThemeTemplates
{
    /// <summary>A big connect control in the middle of the brand area, the place and state under it, the rest in a side panel.</summary>
    public const string Center = "center";

    /// <summary>The upper part in the brand colour (or picture) with the place and Connect; the lower part the blocks and servers.</summary>
    public const string Half = "half";

    /// <summary>A brand column on the left (brand, state, Connect, plan); the server list and the other blocks on the right.</summary>
    public const string Split = "split";

    /// <summary>Settings-style grouped lists: Connect is the first row, the server, mode and route follow, then the blocks.</summary>
    public const string List = "list";

    /// <summary>V2pn's map edge to edge with floating panels over it.</summary>
    public const string Map = "map";

    /// <summary>One tall narrow card, made for small windows; a centred card in a large one.</summary>
    public const string Compact = "compact";

    public static readonly IReadOnlyList<string> All = [Center, Half, Split, List, Map, Compact];

    /// <summary>The blocks a template has a place for; any other block in a pack is left out there.</summary>
    public static IReadOnlyList<string> Blocks(string template) => template switch
    {
        // A settings list holds rows, not pictures; the map's panels have no room for news or a picture beside the map.
        List => [V2pnThemeBlocks.Plan, V2pnThemeBlocks.Traffic, V2pnThemeBlocks.Servers, V2pnThemeBlocks.Actions, V2pnThemeBlocks.Banner, V2pnThemeBlocks.News],
        Map => [V2pnThemeBlocks.Plan, V2pnThemeBlocks.Traffic, V2pnThemeBlocks.Servers, V2pnThemeBlocks.Actions, V2pnThemeBlocks.Banner],
        Compact => [V2pnThemeBlocks.Plan, V2pnThemeBlocks.Traffic, V2pnThemeBlocks.Servers, V2pnThemeBlocks.Actions, V2pnThemeBlocks.Banner, V2pnThemeBlocks.News],
        _ => V2pnThemeBlocks.All,
    };

    /// <summary>The named picture slots of a template (theme.art); see <see cref="V2pnPartnerSlots"/> for what fits each.</summary>
    public static IReadOnlyList<string> ArtSlots(string template) => template switch
    {
        Center or Half or Split => [V2pnThemeArt.Hero, V2pnThemeArt.Banner],
        Compact => [V2pnThemeArt.Hero],
        _ => [],
    };

    /// <summary>The connect control a template is drawn with when the theme names none (or one V2pn does not know).</summary>
    public static string DefaultConnect(string template) => template switch
    {
        Center => V2pnThemeConnect.Round,
        List => V2pnThemeConnect.Toggle,
        Split or Compact => V2pnThemeConnect.Bar,
        _ => V2pnThemeConnect.Capsule,
    };

    /// <summary>Whether a template paints part of the window with the theme's background (the map never does).</summary>
    public static bool HasArea(string template) => template != Map;
}

/// <summary>The parts a theme orders on the main screen. The server list is always there; the others show when there is something to show.</summary>
public static class V2pnThemeBlocks
{
    /// <summary>The plan, devices and term from the panel or the API.</summary>
    public const string Plan = "plan";

    /// <summary>Traffic used, with a bar.</summary>
    public const string Traffic = "traffic";

    /// <summary>The server list with search, filters, ping, update and "+".</summary>
    public const string Servers = "servers";

    /// <summary>The provider's buttons (up to four).</summary>
    public const string Actions = "actions";

    /// <summary>The subscription's banner and the provider's notice of an incident.</summary>
    public const string Banner = "banner";

    /// <summary>The panel's announcement, with its link.</summary>
    public const string News = "news";

    /// <summary>The theme's wide picture (theme.art.banner).</summary>
    public const string Art = "art";

    public static readonly IReadOnlyList<string> All = [Plan, Traffic, Servers, Actions, Banner, News, Art];

    /// <summary>The order when a theme names none.</summary>
    public static readonly IReadOnlyList<string> Default = [Actions, Banner, Plan, Traffic, News, Art, Servers];
}

public static class V2pnThemeConnect
{
    public const string Round = "round";
    public const string Capsule = "capsule";
    public const string Bar = "bar";
    public const string Toggle = "toggle";

    public static readonly IReadOnlyList<string> All = [Round, Capsule, Bar, Toggle];
}

public static class V2pnThemeTones
{
    public const string Warm = "warm";
    public const string Neutral = "neutral";
    public const string Cool = "cool";

    public static readonly IReadOnlyList<string> All = [Warm, Neutral, Cool];
}

public static class V2pnThemeCorners
{
    public const string Sharp = "sharp";
    public const string Soft = "soft";
    public const string Round = "round";

    public static readonly IReadOnlyList<string> All = [Sharp, Soft, Round];
}

public static class V2pnThemeDensity
{
    public const string Normal = "normal";
    public const string Compact = "compact";

    public static readonly IReadOnlyList<string> All = [Normal, Compact];
}

public static class V2pnThemeBackgroundKinds
{
    public const string Solid = "solid";
    public const string Gradient = "gradient";
    public const string Image = "image";

    public static readonly IReadOnlyList<string> All = [Solid, Gradient, Image];
}

/// <summary>The picture slots of a theme (theme.art and background.art).</summary>
public static class V2pnThemeArt
{
    /// <summary>An illustration in the brand area: 200-1600 px a side, at most 2:1.</summary>
    public const string Hero = "hero";

    /// <summary>A wide picture for the "art" block: 2:1 to 5:1, 400-2400 px wide.</summary>
    public const string Banner = "banner";

    /// <summary>background.art: the picture under the brand area, 640-3200 px a side, at most 3:1.</summary>
    public const string Backdrop = "backdrop";
}

/// <summary>
/// The type a theme may pick: open fonts with Latin and Cyrillic that come with V2pn (Assets/Fonts). A provider names
/// one; it never sends a font file. A name V2pn does not know is drawn in Geist.
/// </summary>
public static class V2pnThemeFonts
{
    /// <param name="Family">The family name, as a provider writes it and as the font file names itself.</param>
    /// <param name="Folder">Its folder under Assets/Fonts/Themes; null for the fonts V2pn itself uses (Assets/Fonts).</param>
    /// <param name="Mono">Monospaced.</param>
    public sealed record Font(string Family, string? Folder, bool Mono = false);

    public const string Default = "Geist";

    public static readonly IReadOnlyList<Font> All =
    [
        new("Geist", null),
        new("Inter", null),
        new("Manrope", "Manrope"),
        new("Onest", "Onest"),
        new("Golos Text", "GolosText"),
        new("Montserrat", "Montserrat"),
        new("Rubik", "Rubik"),
        new("IBM Plex Sans", "IBMPlexSans"),
        new("PT Sans", "PTSans"),
        new("Raleway", "Raleway"),
        new("Exo 2", "Exo2"),
        new("Comfortaa", "Comfortaa"),
        new("Unbounded", "Unbounded"),
        new("Spectral", null),
        new("JetBrains Mono", "JetBrainsMono", Mono: true),
    ];

    /// <summary>
    /// The bundled font a provider means, matched without regard to case, spaces, hyphens and underscores
    /// ("golos-text", "GolosText"); null when V2pn has no such font.
    /// </summary>
    public static Font? Find(string? name)
    {
        var key = Key(name);
        return key.Length == 0 ? null : All.FirstOrDefault(f => Key(f.Family) == key);
    }

    /// <summary>The family to draw <paramref name="name"/> in: the bundled one, or Geist.</summary>
    public static Font Resolve(string? name) => Find(name) ?? All[0];

    private static string Key(string? name) =>
        new((name ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}

/// <summary>theme.background: a colour, a gradient or a picture (with a scrim V2pn sets).</summary>
/// <param name="Kind">solid, gradient or image.</param>
/// <param name="Colors">The gradient's 2 to 4 stops, "#RRGGBB"; empty otherwise.</param>
/// <param name="Art">The picture (a pack asset) for an image background.</param>
public sealed record V2pnThemeBackground(string Kind, IReadOnlyList<string> Colors, string? Art)
{
    public static readonly V2pnThemeBackground Solid = new(V2pnThemeBackgroundKinds.Solid, [], null);
}

/// <summary>
/// A provider's theme (partner.json "theme", with the edition grant): which of V2pn's parts it is assembled from. Every
/// field has a safe value: whatever a pack gets wrong falls back to it, and <see cref="Notes"/> says what and why.
/// </summary>
public sealed record V2pnTheme(
    string Template,
    string Connect,
    IReadOnlyList<string> Brand,
    string Tone,
    string Font,
    string Corners,
    string Density,
    V2pnThemeBackground Background,
    IReadOnlyList<string> Blocks,
    IReadOnlyDictionary<string, string> Art)
{
    /// <summary>What V2pn did not take from the pack and what it used instead, one line each (tools/partner-check.cs).</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>A theme of V2pn's defaults in <paramref name="brand"/> (or V2pn's fallback colour).</summary>
    public static V2pnTheme Default(string? brand) => new(
        V2pnThemeTemplates.Center,
        V2pnThemeTemplates.DefaultConnect(V2pnThemeTemplates.Center),
        V2pnColor.TryParse(brand) is { } c ? [c.Hex] : [],
        V2pnThemeTones.Neutral,
        V2pnThemeFonts.Default,
        V2pnThemeCorners.Soft,
        V2pnThemeDensity.Normal,
        V2pnThemeBackground.Solid,
        V2pnThemeBlocks.Default.Where(V2pnThemeTemplates.Blocks(V2pnThemeTemplates.Center).Contains).ToList(),
        new Dictionary<string, string>());

    /// <summary>One string that changes whenever anything that is drawn changes (for rebuilding a look).</summary>
    public string Key => string.Join("|", Template, Connect, string.Join(",", Brand), Tone, Font, Corners, Density,
        Background.Kind, string.Join(",", Background.Colors), Background.Art, string.Join(",", Blocks),
        string.Join(",", Art.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => a.Key + "=" + a.Value)));

    /// <summary>The picture in a slot, when the theme has one there.</summary>
    public string? Picture(string slot) => Art.GetValueOrDefault(slot);
}

/// <summary>Reads partner.json's "theme" strictly: known values only, within limits; anything else falls back.</summary>
public static class V2pnThemeReader
{
    public const int MaxBrand = 3;
    public const int MinStops = 2;
    public const int MaxStops = 4;
    private const int MaxText = 40;

    private static readonly HashSet<string> Keys = ["template", "connect", "palette", "font", "corners", "density", "background", "blocks", "art"];

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
            if (!Keys.Contains(key))
            {
                notes.Add($"theme.{Clip(key)} is not a field V2pn knows; ignored");
            }
        }

        var template = Pick(root["template"], V2pnThemeTemplates.All, V2pnThemeTemplates.Center, "theme.template", notes);
        var connect = Pick(root["connect"], V2pnThemeConnect.All, V2pnThemeTemplates.DefaultConnect(template), "theme.connect", notes);

        var palette = root["palette"] as JsonObject;
        if (root["palette"] != null && palette == null)
        {
            notes.Add("theme.palette is not an object; the brand colour of the pack is used");
        }
        var brand = Brand(palette?["brand"], notes);
        if (brand.Count == 0 && V2pnColor.TryParse(accent) is { } fallback)
        {
            brand.Add(fallback.Hex);
        }
        var tone = Pick(palette?["tone"], V2pnThemeTones.All, V2pnThemeTones.Neutral, "theme.palette.tone", notes);

        var fontName = Text(root["font"]);
        var font = V2pnThemeFonts.Find(fontName);
        if (font == null && root["font"] != null)
        {
            notes.Add($"theme.font \"{Clip(fontName ?? root["font"]!.ToJsonString())}\" is not one of V2pn's fonts; {V2pnThemeFonts.Default} is used");
        }
        var corners = Pick(root["corners"], V2pnThemeCorners.All, V2pnThemeCorners.Soft, "theme.corners", notes);
        var density = Pick(root["density"], V2pnThemeDensity.All, V2pnThemeDensity.Normal, "theme.density", notes);
        var background = Background(root["background"], template, isPicture, notes);
        var blocks = Blocks(root["blocks"], template, notes);
        var art = Art(root["art"], template, isPicture, notes);
        if (blocks.Contains(V2pnThemeBlocks.Art) && !art.ContainsKey(V2pnThemeArt.Banner) && root["blocks"] != null)
        {
            notes.Add("theme.blocks has \"art\" but theme.art.banner names no picture; the block stays empty");
        }

        return new V2pnTheme(template, connect, brand, tone, (font ?? V2pnThemeFonts.Resolve(null)).Family, corners, density, background, blocks, art)
        {
            Notes = notes,
        };
    }

    /// <summary>
    /// <paramref name="theme"/> on another template: its blocks and pictures kept where that template has a place for
    /// them, in the same order (the server list always there).
    /// </summary>
    public static V2pnTheme WithTemplate(V2pnTheme theme, string template)
    {
        if (!V2pnThemeTemplates.All.Contains(template))
        {
            return theme;
        }
        var supported = V2pnThemeTemplates.Blocks(template);
        var blocks = theme.Blocks.Where(supported.Contains).ToList();
        if (!blocks.Contains(V2pnThemeBlocks.Servers))
        {
            blocks.Add(V2pnThemeBlocks.Servers);
        }
        var slots = V2pnThemeTemplates.ArtSlots(template);
        return theme with
        {
            Template = template,
            Blocks = blocks,
            Art = theme.Art.Where(a => slots.Contains(a.Key)).ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal),
            Background = V2pnThemeTemplates.HasArea(template) ? theme.Background : V2pnThemeBackground.Solid,
        };
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

    private static V2pnThemeBackground Background(JsonNode? node, string template, Func<string, bool> isPicture, List<string> notes)
    {
        if (node == null)
        {
            return V2pnThemeBackground.Solid;
        }
        if (!V2pnThemeTemplates.HasArea(template))
        {
            notes.Add($"theme.background: the {template} template has no brand area (V2pn's map is its ground); ignored");
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

    private static List<string> Blocks(JsonNode? node, string template, List<string> notes)
    {
        var supported = V2pnThemeTemplates.Blocks(template);
        List<string> blocks;
        if (node == null)
        {
            blocks = V2pnThemeBlocks.Default.Where(supported.Contains).ToList();
        }
        else if (node is not JsonArray array)
        {
            notes.Add("theme.blocks is not a list; V2pn's order is used");
            blocks = V2pnThemeBlocks.Default.Where(supported.Contains).ToList();
        }
        else
        {
            blocks = [];
            foreach (var item in array.Take(16))
            {
                var name = Text(item)?.Trim().ToLowerInvariant();
                if (name == null || !V2pnThemeBlocks.All.Contains(name))
                {
                    notes.Add($"theme.blocks: \"{Clip(name ?? item?.ToJsonString() ?? "null")}\" is not a block V2pn knows; left out");
                }
                else if (!supported.Contains(name))
                {
                    notes.Add($"theme.blocks: the {template} template has no place for \"{name}\"; left out");
                }
                else if (!blocks.Contains(name))
                {
                    blocks.Add(name);
                }
            }
        }
        if (!blocks.Contains(V2pnThemeBlocks.Servers))
        {
            // The person always reaches their servers.
            if (node != null)
            {
                notes.Add("theme.blocks has no \"servers\"; V2pn always shows the server list and puts it last");
            }
            blocks.Add(V2pnThemeBlocks.Servers);
        }
        return blocks;
    }

    private static Dictionary<string, string> Art(JsonNode? node, string template, Func<string, bool> isPicture, List<string> notes)
    {
        var art = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node == null)
        {
            return art;
        }
        if (node is not JsonObject slots)
        {
            notes.Add("theme.art is not an object; no pictures are used");
            return art;
        }
        var allowed = V2pnThemeTemplates.ArtSlots(template);
        foreach (var (slot, value) in slots)
        {
            var file = Text(value);
            if (!allowed.Contains(slot))
            {
                notes.Add(allowed.Count == 0
                    ? $"theme.art.{Clip(slot)}: the {template} template has no picture slots; ignored"
                    : $"theme.art.{Clip(slot)}: the {template} template has the slots {string.Join(", ", allowed)}; ignored");
            }
            else if (file == null || !isPicture(file))
            {
                notes.Add($"theme.art.{slot} \"{Clip(file ?? "")}\" is not a picture of the pack's assets; ignored");
            }
            else
            {
                art[slot] = file;
            }
        }
        return art;
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && text.Length <= 256 ? text : null;

    private static string Clip(string text) => text.Length <= MaxText ? text : text[..MaxText] + "…";
}
