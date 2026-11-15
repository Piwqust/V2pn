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
        public const string Backdrop = "backdrop";

        public static readonly IReadOnlyList<string> All = [Logo, LogoOnLight, Mark, Backdrop];

        public static V2pnPartnerSlots.Slot Slot(string place) => place switch
        {
            Logo or LogoOnLight => V2pnPartnerSlots.Logo,
            Mark => V2pnPartnerSlots.Mark,
            _ => V2pnPartnerSlots.Backdrop,
        };
    }

    public V2pnPackDraft(JsonObject root)
    {
        Root = root;
    }

    public JsonObject Root { get; }

    /// <summary>A new pack: V2pn's default theme in a calm blue, Neon first and the Pass second.</summary>
    public static V2pnPackDraft New()
    {
        var theme = V2pnTheme.Default(V2pnThemePalette.FallbackBrand);
        return new V2pnPackDraft(new JsonObject
        {
            ["schema"] = 1,
            ["id"] = string.Empty,
            ["name"] = string.Empty,
            ["brand"] = new JsonObject { ["accent"] = V2pnThemePalette.FallbackBrand },
            ["edition"] = new JsonObject
            {
                ["variants"] = Array([V2pnPartnerVariants.Neon, V2pnPartnerVariants.Pass]),
                ["default"] = V2pnPartnerVariants.Neon,
            },
            ["theme"] = new JsonObject
            {
                ["palette"] = new JsonObject { ["brand"] = Array(theme.Brand) },
                ["background"] = new JsonObject { ["type"] = V2pnThemeBackgroundKinds.Solid },
                ["connect"] = theme.Connect,
                ["window"] = theme.Window,
            },
        });
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

    /// <summary>The look the pack opens in (edition.default): neon or pass.</summary>
    public string DefaultLook
    {
        get => V2pnPartnerVariants.Normalize(Text(Root["edition"]?["default"])) ?? V2pnPartnerVariants.Neon;
        set
        {
            var edition = Obj(Root, "edition");
            edition["default"] = value;
            // Both looks stay offered; the one the pack opens in comes first.
            edition["variants"] = Array(V2pnPartnerVariants.All.OrderBy(v => v == value ? 0 : 1));
        }
    }

    /// <summary>Neon's Connect: a round button or a switch.</summary>
    public string Connect
    {
        get => Text(Root["theme"]?["connect"]) ?? V2pnThemeConnect.Round;
        set => Theme["connect"] = value;
    }

    /// <summary>The window Neon opens in: mini or full.</summary>
    public string Window
    {
        get => Text(Root["theme"]?["window"]) ?? V2pnThemeWindow.Mini;
        set => Theme["window"] = value;
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

    // ---------------------------------------------------------------- pictures

    /// <summary>The file in a picture place (<see cref="Pictures"/>), or null.</summary>
    public string? Picture(string place) => place switch
    {
        Pictures.Logo => Text(Root["brand"]?["logo"]),
        Pictures.LogoOnLight => Text(Root["brand"]?["logoOnLight"]),
        Pictures.Mark => Text(Root["brand"]?["mark"]),
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
    /// Takes out the fields of the six-template themes (template, font, corners, density, blocks, art, palette.tone), which
    /// V2pn no longer reads, and writes the looks by their names (an older pack's "theme" and "signature" become neon and
    /// pass). Says what it changed, one line each; the builder calls it on save.
    /// </summary>
    public IReadOnlyList<string> Tidy()
    {
        var removed = new List<string>();
        if (Root["theme"] is JsonObject theme)
        {
            foreach (var key in new[] { "template", "font", "corners", "density", "blocks", "art" })
            {
                if (theme.Remove(key))
                {
                    removed.Add($"theme.{key}");
                }
            }
            if (theme["palette"] is JsonObject palette && palette.Remove("tone"))
            {
                removed.Add("theme.palette.tone");
            }
        }
        if (Root["edition"] is JsonObject edition)
        {
            var listed = Strings(edition["variants"]);
            var looks = V2pnPartnerVariants.For(listed, Root["theme"] != null);
            if (!listed.SequenceEqual(looks))
            {
                edition["variants"] = Array(looks);
                removed.Add("edition.variants");
            }
            var first = Text(edition["default"]);
            if (first != null && first != (V2pnPartnerVariants.Normalize(first) ?? looks[0]))
            {
                edition["default"] = V2pnPartnerVariants.Normalize(first) ?? looks[0];
                removed.Add("edition.default");
            }
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
