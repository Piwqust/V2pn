namespace ServiceLib;

/// <summary>
/// The pack V2pn's theme builder edits: partner.json as a JSON object, changed field by field in place. What the builder does
/// not edit (links, api, lock, fields of a newer V2pn) stays as it was and where it was, so a pack opened and saved unchanged is
/// written byte for byte the same.
/// </summary>
public sealed class V2pnPackDraft
{
    /// <summary>The picture places of a pack: where each sits in partner.json and which slot (V2pnPartnerSlots) it must fit.</summary>
    public static class Pictures
    {
        public const string Logo = "logo";
        public const string LogoOnLight = "logoOnLight";
        public const string Mark = "mark";
        public const string Hero = "hero";
        public const string Banner = "banner";
        public const string Backdrop = "backdrop";

        public static readonly IReadOnlyList<string> All = [Logo, LogoOnLight, Mark, Hero, Banner, Backdrop];

        public static V2pnPartnerSlots.Slot Slot(string place) => place switch
        {
            Logo or LogoOnLight => V2pnPartnerSlots.Logo,
            Mark => V2pnPartnerSlots.Mark,
            Hero => V2pnPartnerSlots.Hero,
            Banner => V2pnPartnerSlots.Banner,
            _ => V2pnPartnerSlots.Backdrop,
        };
    }

    public V2pnPackDraft(JsonObject root)
    {
        Root = root;
    }

    public JsonObject Root { get; }

    /// <summary>A new pack: V2pn's default theme in a calm blue, its theme first and V2pn with its logo second.</summary>
    public static V2pnPackDraft New()
    {
        var theme = V2pnTheme.Default(V2pnThemePalette.FallbackBrand);
        var draft = new V2pnPackDraft(new JsonObject
        {
            ["schema"] = 1,
            ["id"] = string.Empty,
            ["name"] = string.Empty,
            ["brand"] = new JsonObject { ["accent"] = V2pnThemePalette.FallbackBrand },
            ["edition"] = new JsonObject
            {
                ["variants"] = Array([V2pnPartnerVariants.Theme, V2pnPartnerVariants.Signature]),
                ["default"] = V2pnPartnerVariants.Theme,
            },
            ["theme"] = new JsonObject
            {
                ["template"] = theme.Template,
                ["connect"] = theme.Connect,
                ["palette"] = new JsonObject { ["brand"] = Array(theme.Brand), ["tone"] = theme.Tone },
                ["font"] = theme.Font,
                ["corners"] = theme.Corners,
                ["density"] = theme.Density,
                ["background"] = new JsonObject { ["type"] = V2pnThemeBackgroundKinds.Solid },
                ["blocks"] = Array(theme.Blocks),
            },
        });
        return draft;
    }

    // ---------------------------------------------------------------- the pack

    public string Id
    {
        get => Text(Root["id"]) ?? string.Empty;
        set => Root["id"] = value.Trim().ToLowerInvariant();
    }

    public string Name
    {
        get => Text(Root["name"]) ?? string.Empty;
        set => Root["name"] = value;
    }

    /// <summary>brand.accent, kept as the first brand colour of the theme (older V2pn and the signature look use it).</summary>
    public string? Accent
    {
        get => Text(Root["brand"]?["accent"]);
        set => Set(Obj(Root, "brand"), "accent", value);
    }

    // ---------------------------------------------------------------- the theme

    private JsonObject Theme => Obj(Root, "theme");

    public string Template
    {
        get => Text(Root["theme"]?["template"]) ?? V2pnThemeTemplates.Center;
        set => Theme["template"] = value;
    }

    public string Connect
    {
        get => Text(Root["theme"]?["connect"]) ?? V2pnThemeTemplates.DefaultConnect(Template);
        set => Theme["connect"] = value;
    }

    /// <summary>palette.brand: 1 to 3 colours. The first is also written as brand.accent.</summary>
    public IReadOnlyList<string> Brand
    {
        get => Strings(Root["theme"]?["palette"]?["brand"]);
        set
        {
            Obj(Theme, "palette")["brand"] = Array(value);
            if (value.Count > 0)
            {
                Accent = value[0];
            }
        }
    }

    public string Tone
    {
        get => Text(Root["theme"]?["palette"]?["tone"]) ?? V2pnThemeTones.Neutral;
        set => Obj(Theme, "palette")["tone"] = value;
    }

    public string Font
    {
        get => Text(Root["theme"]?["font"]) ?? V2pnThemeFonts.Default;
        set => Theme["font"] = value;
    }

    public string Corners
    {
        get => Text(Root["theme"]?["corners"]) ?? V2pnThemeCorners.Soft;
        set => Theme["corners"] = value;
    }

    public string Density
    {
        get => Text(Root["theme"]?["density"]) ?? V2pnThemeDensity.Normal;
        set => Theme["density"] = value;
    }

    public string BackgroundKind
    {
        get => Text(Root["theme"]?["background"]?["type"]) ?? V2pnThemeBackgroundKinds.Solid;
        set => Obj(Theme, "background")["type"] = value;
    }

    /// <summary>background.colors: the gradient's 2 to 4 stops (kept while another kind is picked, so switching back finds them).</summary>
    public IReadOnlyList<string> BackgroundColors
    {
        get => Strings(Root["theme"]?["background"]?["colors"]);
        set => Obj(Theme, "background")["colors"] = Array(value);
    }

    /// <summary>The blocks in order; V2pn's default order when the pack names none.</summary>
    public IReadOnlyList<string> Blocks
    {
        get => Root["theme"]?["blocks"] is JsonArray ? Strings(Root["theme"]!["blocks"]) : V2pnThemeBlocks.Default.Where(V2pnThemeTemplates.Blocks(Template).Contains).ToList();
        set => Theme["blocks"] = Array(value);
    }

    // ---------------------------------------------------------------- pictures

    /// <summary>The file in a picture place (<see cref="Pictures"/>), or null.</summary>
    public string? Picture(string place) => place switch
    {
        Pictures.Logo => Text(Root["brand"]?["logo"]),
        Pictures.LogoOnLight => Text(Root["brand"]?["logoOnLight"]),
        Pictures.Mark => Text(Root["brand"]?["mark"]),
        Pictures.Hero => Text(Root["theme"]?["art"]?["hero"]),
        Pictures.Banner => Text(Root["theme"]?["art"]?["banner"]),
        _ => Text(Root["theme"]?["background"]?["art"]),
    };

    /// <summary>Puts <paramref name="file"/> (a file name of the pack folder) into a place, or empties it with null.</summary>
    public void SetPicture(string place, string? file)
    {
        switch (place)
        {
            case Pictures.Logo:
            case Pictures.LogoOnLight:
            case Pictures.Mark:
                Set(Obj(Root, "brand"), place, file);
                break;
            case Pictures.Hero:
            case Pictures.Banner:
                var art = Obj(Theme, "art");
                Set(art, place, file);
                if (art.Count == 0)
                {
                    Theme.Remove("art");
                }
                break;
            default:
                Set(Obj(Theme, "background"), "art", file);
                break;
        }
    }

    /// <summary>Every picture file the pack names, once each.</summary>
    public IReadOnlyList<string> PictureFiles => Pictures.All.Select(Picture).OfType<string>().Distinct().ToList();

    // ---------------------------------------------------------------- buttons and servers

    /// <summary>"actions": the buttons as written (up to four count).</summary>
    public IReadOnlyList<JsonObject> Actions => (Root["actions"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];

    public void SetActions(IEnumerable<JsonObject> actions)
    {
        var list = actions.Select(a => (JsonNode)(a.Parent == null ? a : (JsonObject)a.DeepClone())).ToArray();
        if (list.Length == 0)
        {
            Root.Remove("actions");
            return;
        }
        Root["actions"] = new JsonArray(list);
    }

    /// <summary>"servers": the notes on the subscription's servers as written.</summary>
    public IReadOnlyList<JsonObject> Servers => (Root["servers"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];

    public void SetServers(IEnumerable<JsonObject> servers)
    {
        var list = servers.Select(a => (JsonNode)(a.Parent == null ? a : (JsonObject)a.DeepClone())).ToArray();
        if (list.Length == 0)
        {
            Root.Remove("servers");
            return;
        }
        Root["servers"] = new JsonArray(list);
    }

    // ---------------------------------------------------------------- saving

    /// <summary>
    /// Takes out what the template has no place for (blocks, pictures, a background on the map), as V2pn would leave it out
    /// anyway, and says what, one line each. The builder keeps everything while the person tries templates and calls this on save.
    /// </summary>
    public IReadOnlyList<string> FitTemplate()
    {
        var removed = new List<string>();
        var template = Template;
        if (Root["theme"]?["blocks"] is JsonArray)
        {
            var supported = V2pnThemeTemplates.Blocks(template);
            var blocks = Blocks.ToList();
            foreach (var block in blocks.Where(b => !supported.Contains(b)).ToList())
            {
                removed.Add($"theme.blocks: {block}");
                blocks.Remove(block);
            }
            if (removed.Count > 0)
            {
                Blocks = blocks;
            }
        }
        var slots = V2pnThemeTemplates.ArtSlots(template);
        foreach (var place in new[] { Pictures.Hero, Pictures.Banner })
        {
            if (Picture(place) != null && !slots.Contains(place))
            {
                removed.Add($"theme.art.{place}");
                SetPicture(place, null);
            }
        }
        if (!V2pnThemeTemplates.HasArea(template) && Root["theme"]?["background"] is JsonObject background && Text(background["type"]) != V2pnThemeBackgroundKinds.Solid)
        {
            removed.Add("theme.background");
            Theme["background"] = new JsonObject { ["type"] = V2pnThemeBackgroundKinds.Solid };
        }
        return removed;
    }

    /// <summary>Why the pack cannot be saved as a pack V2pn takes, one line each; empty when it can.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!V2pnPartnerTrust.ValidId(Id))
        {
            problems.Add("id");
        }
        if (V2pnPartnerTrust.ValidName(Name) == null)
        {
            problems.Add("name");
        }
        return problems;
    }

    // ---------------------------------------------------------------- helpers

    private static JsonObject Obj(JsonObject parent, string key)
    {
        if (parent[key] is JsonObject child)
        {
            return child;
        }
        child = new JsonObject();
        parent[key] = child;
        return child;
    }

    private static void Set(JsonObject parent, string key, string? value)
    {
        if (value == null)
        {
            parent.Remove(key);
        }
        else
        {
            parent[key] = value;
        }
    }

    private static JsonArray Array(IEnumerable<string> values) => new(values.Select(v => (JsonNode)JsonValue.Create(v)).ToArray());

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static List<string> Strings(JsonNode? node) =>
        node is JsonArray array ? array.Select(Text).OfType<string>().ToList() : Text(node) is { } one ? [one] : [];
}
