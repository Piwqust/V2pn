namespace ServiceLib.Tests.V2pn;

public class V2pnPartnerThemeTests
{
    private static readonly byte[] Picture = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private static V2pnTheme Read(string json, string? accent = "#2F7BFF", params string[] pictures) =>
        V2pnThemeReader.Parse(JsonNode.Parse(json), accent, pictures.Contains);

    private static V2pnPartnerPack? ReadPack(string partner, string[]? variants, Action<JsonObject>? more = null, string? first = null)
    {
        var p = new TestPartner();
        var cert = p.ReadCert(p.Cert(partner: partner));
        var json = TestPartner.Pack(id: partner, variants: variants, first: first, more: more);
        return V2pnPartnerTrust.ReadPack(json, TestPartner.Sign(p.Key, json), cert, out _);
    }

    [Test]
    public async Task Theme_WithEveryField_IsRead()
    {
        var theme = Read("""
            {
              "template": "Split", "connect": "toggle",
              "palette": { "brand": ["#0a7cff", "#ff2d87", "#ffd600"], "tone": "cool" },
              "font": "golos-text", "corners": "round", "density": "compact",
              "background": { "type": "gradient", "colors": ["#0A7CFF", "#5B2DFF", "#141A33"] },
              "blocks": ["plan", "actions", "servers", "art"],
              "art": { "hero": "hero.png", "banner": "wide.png" }
            }
            """, pictures: ["hero.png", "wide.png"]);

        await theme.Template.Should().BeEqualTo(V2pnThemeTemplates.Split);
        await theme.Connect.Should().BeEqualTo(V2pnThemeConnect.Toggle);
        await string.Join(",", theme.Brand).Should().BeEqualTo("#0A7CFF,#FF2D87,#FFD600");
        await theme.Tone.Should().BeEqualTo(V2pnThemeTones.Cool);
        await theme.Font.Should().BeEqualTo("Golos Text");
        await theme.Corners.Should().BeEqualTo(V2pnThemeCorners.Round);
        await theme.Density.Should().BeEqualTo(V2pnThemeDensity.Compact);
        await theme.Background.Kind.Should().BeEqualTo(V2pnThemeBackgroundKinds.Gradient);
        await theme.Background.Colors.Count.Should().BeEqualTo(3);
        await string.Join(",", theme.Blocks).Should().BeEqualTo("plan,actions,servers,art");
        await theme.Picture(V2pnThemeArt.Hero).Should().BeEqualTo("hero.png");
        await theme.Notes.Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task Theme_Missing_IsVsDefaultInTheBrandColour()
    {
        var theme = V2pnThemeReader.Parse(null, "#2f7bff", _ => false);

        await theme.Template.Should().BeEqualTo(V2pnThemeTemplates.Center);
        await theme.Connect.Should().BeEqualTo(V2pnThemeConnect.Round);
        await theme.Brand[0].Should().BeEqualTo("#2F7BFF");
        await theme.Font.Should().BeEqualTo(V2pnThemeFonts.Default);
        await theme.Blocks[^1].Should().BeEqualTo(V2pnThemeBlocks.Servers);
        await theme.Notes.Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task Theme_EveryWrongField_FallsBack_WithANote()
    {
        var theme = Read("""
            {
              "template": "grid", "connect": "slider",
              "palette": { "brand": ["red", "#12345", "#112233", "#445566", "#778899", "#AABBCC"], "tone": "hot" },
              "font": "Comic Sans", "corners": 7, "density": "airy",
              "background": { "type": "gradient", "colors": ["#000000"] },
              "blocks": ["weather", "plan", "plan", 42],
              "art": { "lamp": "a.png", "hero": "missing.png" },
              "script": "alert(1)"
            }
            """, pictures: ["a.png"]);

        await theme.Template.Should().BeEqualTo(V2pnThemeTemplates.Center);
        await theme.Connect.Should().BeEqualTo(V2pnThemeConnect.Round);
        await string.Join(",", theme.Brand).Should().BeEqualTo("#112233,#445566,#778899");
        await theme.Tone.Should().BeEqualTo(V2pnThemeTones.Neutral);
        await theme.Font.Should().BeEqualTo("Geist");
        await theme.Corners.Should().BeEqualTo(V2pnThemeCorners.Soft);
        await theme.Density.Should().BeEqualTo(V2pnThemeDensity.Normal);
        await theme.Background.Kind.Should().BeEqualTo(V2pnThemeBackgroundKinds.Solid);
        await string.Join(",", theme.Blocks).Should().BeEqualTo("plan,servers");
        await theme.Art.Count.Should().BeEqualTo(0);
        foreach (var field in new[] { "template", "connect", "brand", "tone", "font", "corners", "density", "background", "blocks", "lamp", "hero", "script" })
        {
            await theme.Notes.Any(n => n.Contains(field)).Should().BeTrue();
        }
    }

    [Test]
    public async Task Theme_NotAnObject_OrWithWrongShapes_StaysUsable()
    {
        foreach (var json in new[] { "\"split\"", "[]", "42", """{ "palette": "#FF0000", "blocks": "servers", "art": [], "background": "image" }""" })
        {
            var theme = Read(json);
            await theme.Template.Should().BeEqualTo(V2pnThemeTemplates.Center);
            await theme.Brand[0].Should().BeEqualTo("#2F7BFF");
            await theme.Blocks.Contains(V2pnThemeBlocks.Servers).Should().BeTrue();
            await (theme.Notes.Count > 0).Should().BeTrue();
        }
    }

    [Test]
    public async Task Theme_ImageBackground_NeedsAPictureOfThePack()
    {
        var kept = Read("""{ "background": { "type": "image", "art": "sky.jpg" } }""", pictures: ["sky.jpg"]);
        var lost = Read("""{ "background": { "type": "image", "art": "https://example.com/sky.jpg" } }""", pictures: ["sky.jpg"]);

        await kept.Background.Art.Should().BeEqualTo("sky.jpg");
        await lost.Background.Kind.Should().BeEqualTo(V2pnThemeBackgroundKinds.Solid);
        await lost.Notes.Count.Should().BeEqualTo(1);
    }

    [Test]
    public async Task Blocks_PerTemplate_OnlyWhereTheTemplateHasAPlace()
    {
        var all = """["art", "news", "plan", "traffic", "actions", "banner", "servers"]""";
        var list = Read($$"""{ "template": "list", "blocks": {{all}} }""");
        var map = Read($$"""{ "template": "map", "blocks": {{all}}, "background": { "type": "gradient", "colors": ["#101010", "#202020"] } }""");
        var center = Read($$"""{ "template": "center", "blocks": {{all}} }""");

        await string.Join(",", list.Blocks).Should().BeEqualTo("news,plan,traffic,actions,banner,servers");
        await string.Join(",", map.Blocks).Should().BeEqualTo("plan,traffic,actions,banner,servers");
        await map.Background.Kind.Should().BeEqualTo(V2pnThemeBackgroundKinds.Solid);
        await string.Join(",", center.Blocks).Should().BeEqualTo("art,news,plan,traffic,actions,banner,servers");
        foreach (var template in V2pnThemeTemplates.All)
        {
            var blocks = V2pnThemeTemplates.Blocks(template);
            await blocks.Contains(V2pnThemeBlocks.Servers).Should().BeTrue();
            await blocks.All(V2pnThemeBlocks.All.Contains).Should().BeTrue();
            await V2pnThemeConnect.All.Contains(V2pnThemeTemplates.DefaultConnect(template)).Should().BeTrue();
        }
    }

    [Test]
    public async Task Blocks_WithoutServers_GetTheListLast()
    {
        var theme = Read("""{ "blocks": ["plan", "news"] }""");

        await string.Join(",", theme.Blocks).Should().BeEqualTo("plan,news,servers");
        await theme.Notes.Any(n => n.Contains("servers")).Should().BeTrue();
    }

    [Test]
    public async Task WithTemplate_KeepsWhatTheNewTemplateHolds()
    {
        var theme = Read("""{ "template": "half", "blocks": ["art", "servers", "news"], "art": { "hero": "h.png", "banner": "b.png" } }""", pictures: ["h.png", "b.png"]);

        var compact = V2pnThemeReader.WithTemplate(theme, V2pnThemeTemplates.Compact);
        var map = V2pnThemeReader.WithTemplate(theme, V2pnThemeTemplates.Map);

        await string.Join(",", compact.Blocks).Should().BeEqualTo("servers,news");
        await string.Join(",", compact.Art.Keys).Should().BeEqualTo("hero");
        await string.Join(",", map.Blocks).Should().BeEqualTo("servers");
        await map.Art.Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task Fonts_ResolveByNameLoosely_UnknownIsGeist()
    {
        await V2pnThemeFonts.Resolve("golos text").Family.Should().BeEqualTo("Golos Text");
        await V2pnThemeFonts.Resolve("GolosText").Family.Should().BeEqualTo("Golos Text");
        await V2pnThemeFonts.Resolve("ibm-plex-sans").Family.Should().BeEqualTo("IBM Plex Sans");
        await V2pnThemeFonts.Resolve("EXO 2").Folder.Should().BeEqualTo("Exo2");
        await V2pnThemeFonts.Resolve("jetbrains_mono").Mono.Should().BeTrue();
        await V2pnThemeFonts.Resolve("Comic Sans").Family.Should().BeEqualTo("Geist");
        await V2pnThemeFonts.Resolve(null).Family.Should().BeEqualTo("Geist");
        await V2pnThemeFonts.Resolve("").Family.Should().BeEqualTo("Geist");
        await (V2pnThemeFonts.All.Count is >= 10 and <= 15).Should().BeTrue();
        await V2pnThemeFonts.All.Select(f => f.Family).Distinct().Count().Should().BeEqualTo(V2pnThemeFonts.All.Count);
    }

    [Test]
    public async Task Fonts_EveryBundledFontIsInTheApp()
    {
        var assets = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "v2rayN.Desktop", "Assets", "Fonts"));
        if (!Directory.Exists(assets))
        {
            return;
        }
        foreach (var font in V2pnThemeFonts.All)
        {
            var folder = font.Folder == null ? assets : Path.Combine(assets, "Themes", font.Folder);
            var prefix = font.Family.Replace(" ", string.Empty);
            await Directory.GetFiles(folder, prefix + "-Regular.ttf").Length.Should().BeEqualTo(1);
            await (File.Exists(Path.Combine(folder, "OFL.txt")) || File.Exists(Path.Combine(folder, prefix + "-OFL.txt"))).Should().BeTrue();
        }
    }

    [Test]
    public async Task Palette_RandomBrands_AlwaysPassAA_InLightAndDark()
    {
        var random = new Random(20261115);
        string Hex() => $"#{random.Next(0x1000000):X6}";
        foreach (var i in Enumerable.Range(0, 400))
        {
            var brand = Enumerable.Range(0, 1 + (i % 3)).Select(_ => Hex()).ToList();
            var background = (i % 3) switch
            {
                0 => V2pnThemeBackground.Solid,
                1 => new V2pnThemeBackground(V2pnThemeBackgroundKinds.Gradient, Enumerable.Range(0, 2 + (i % 3)).Select(_ => Hex()).ToList(), null),
                _ => new V2pnThemeBackground(V2pnThemeBackgroundKinds.Image, [], "sky.jpg"),
            };
            var theme = V2pnTheme.Default(null) with { Brand = brand, Tone = V2pnThemeTones.All[i % 3], Background = background };
            foreach (var dark in new[] { false, true })
            {
                var c = V2pnThemePalette.Derive(theme, dark);
                var context = $"{string.Join(",", brand)} {background.Kind} {theme.Tone} {(dark ? "dark" : "light")}";
                await AssertPalette(c, context);
            }
        }
    }

    [Test]
    public async Task Palette_EdgeColours_PassAA()
    {
        foreach (var hex in new[] { "#000000", "#FFFFFF", "#808080", "#FFFF00", "#00FF00", "#FF0000", "#3DD68C", "#F2B84B", "#FF5A3D", "#777777", "#7F7FFF", "#FF3D1F" })
        {
            foreach (var dark in new[] { false, true })
            {
                var theme = V2pnTheme.Default(hex) with
                {
                    Background = new V2pnThemeBackground(V2pnThemeBackgroundKinds.Gradient, [hex, "#FFFFFF", "#000000"], null),
                };
                await AssertPalette(V2pnThemePalette.Derive(theme, dark), hex);
                await AssertPalette(V2pnThemePalette.Derive(V2pnTheme.Default(hex), dark), hex);
            }
        }
    }

    private static async Task AssertPalette(V2pnThemeColors c, string context)
    {
        static V2pnColor C(string hex) => V2pnColor.Parse(hex.Length == 9 ? "#" + hex[3..] : hex);
        static double Alpha(string hex) => Convert.ToInt32(hex[1..3], 16) / 255.0;
        static double Ratio(string a, string b) => V2pnColor.Contrast(C(a), C(b));
        const double Text = V2pnThemePalette.TextContrast;

        foreach (var ground in new[] { c.Ground, c.Surface, c.Raised })
        {
            foreach (var text in new[] { c.Ink, c.Soft, c.Muted, c.AccentText, c.Good, c.Warn, c.Bad })
            {
                if (Ratio(text, ground) < Text)
                {
                    throw new InvalidOperationException($"{context}: {text} on {ground} is {Ratio(text, ground):F2}");
                }
            }
        }
        void Check(bool ok, string what)
        {
            if (!ok)
            {
                throw new InvalidOperationException($"{context}: {what} ({c})");
            }
        }
        Check(Ratio(c.OnAccent, c.Accent) >= Text, $"label {c.OnAccent} on accent {c.Accent}");
        Check(Ratio(c.Accent, c.Ground) >= V2pnThemePalette.UiContrast, "accent on ground");
        Check(Ratio(c.Accent, c.Surface) >= V2pnThemePalette.UiContrast, "accent on surface");
        Check(Ratio(c.Secondary, c.Surface) >= V2pnThemePalette.UiContrast, "secondary on surface");
        Check(!V2pnThemePalette.NearState(C(c.Accent)), "accent near a state colour");

        // The brand area reads everywhere: every stop, halfway between them, under a picture whatever it holds.
        var area = c.Area;
        Check(Ratio(area.OnAction, area.Action) >= Text, "label of the area's action");
        if (area.Scrim is { } scrim)
        {
            foreach (var pixel in new[] { V2pnColor.Black, V2pnColor.White, V2pnColor.Parse("#808080") })
            {
                var seen = C(scrim).Over(pixel, Alpha(scrim));
                Check(V2pnColor.Contrast(C(area.Ink), seen) >= Text, $"area ink over {pixel}");
                Check(V2pnColor.Contrast(C(area.Soft), seen) >= Text, $"area soft over {pixel}");
            }
        }
        else
        {
            var stops = area.Colors.Select(C).ToList();
            Check(V2pnThemePalette.MinContrast(C(area.Ink), stops) >= Text, "area ink");
            Check(V2pnThemePalette.MinContrast(C(area.Soft), stops) >= Text, "area soft");
            Check(!stops.Any(V2pnThemePalette.NearState), "area near a state colour");
        }
        // A panel on the area reads over anything under it.
        foreach (var pixel in new[] { V2pnColor.Black, V2pnColor.White })
        {
            var seen = C(area.Glass).Over(pixel, Alpha(area.Glass));
            foreach (var text in new[] { c.Ink, c.Soft, c.Muted })
            {
                Check(V2pnColor.Contrast(C(text), seen) >= Text, $"{text} on glass over {pixel}");
            }
        }
        await Task.CompletedTask;
    }

    [Test]
    public async Task Palette_TurnsBrandsAwayFromStateColours_AndKeepsOthers()
    {
        foreach (var hex in new[] { "#3DD68C", "#22B573", "#F2B84B", "#E8A21E", "#FF5A3D" })
        {
            var turned = V2pnThemePalette.AwayFromState(V2pnColor.Parse(hex));
            await V2pnThemePalette.NearState(turned).Should().BeFalse();
        }
        foreach (var hex in new[] { "#2F7BFF", "#FF7A2F", "#8B5CF6", "#E94F96", "#14A8A0", "#D7263D", "#7A7A7A" })
        {
            await V2pnThemePalette.AwayFromState(V2pnColor.Parse(hex)).Hex.Should().BeEqualTo(hex);
        }
    }

    [Test]
    public async Task Palette_PersonsAccent_ReplacesTheFirstBrandColour()
    {
        var theme = V2pnTheme.Default("#2F7BFF");

        var own = V2pnThemePalette.Derive(theme, dark: true);
        var picked = V2pnThemePalette.Derive(theme, dark: true, accent: "#E94F96");

        await own.Accent.Should().BeEqualTo("#2F7BFF");
        await picked.Accent.Should().BeEqualTo("#E94F96");
    }

    [Test]
    public async Task Variants_ConfigrasLooksAreItsOwn()
    {
        foreach (var variant in V2pnPartnerVariants.ConfigraOnly)
        {
            await V2pnPartnerVariants.Allowed("configra", variant).Should().BeTrue();
            await V2pnPartnerVariants.Allowed("kestrel", variant).Should().BeFalse();
        }
        await V2pnPartnerVariants.Allowed("kestrel", V2pnPartnerVariants.Signature).Should().BeTrue();
        await V2pnPartnerVariants.Allowed("kestrel", V2pnPartnerVariants.Theme).Should().BeTrue();
        await V2pnPartnerVariants.Allowed("kestrel", "bogus").Should().BeFalse();
    }

    [Test]
    public async Task Pack_OfConfigra_KeepsItsFiveLooks()
    {
        var pack = ReadPack("configra", null, first: "cabinet")!;

        await string.Join(",", pack.Variants).Should().BeEqualTo("signature,warm,cabinet,studio,ascii");
        await pack.DefaultVariant.Should().BeEqualTo(V2pnPartnerVariants.Cabinet);
        await (pack.Theme == null).Should().BeTrue();
    }

    [Test]
    public async Task Pack_OfAnotherPartner_ListingConfigrasLooks_GetsItsThemeOrSignature()
    {
        var themed = ReadPack("kestrel", ["cabinet", "signature", "ascii"], more: p => p["theme"] = new JsonObject { ["template"] = "half" }, first: "cabinet")!;
        var plain = ReadPack("kestrel", ["cabinet", "studio"], first: "studio")!;

        await string.Join(",", themed.Variants).Should().BeEqualTo("theme,signature");
        await themed.DefaultVariant.Should().BeEqualTo(V2pnPartnerVariants.Theme);
        await themed.Theme!.Template.Should().BeEqualTo(V2pnThemeTemplates.Half);
        // No theme in the pack: the signature look, and nothing of Configra's.
        await string.Join(",", plain.Variants).Should().BeEqualTo("signature");
        await (plain.Theme == null).Should().BeTrue();
    }

    [Test]
    public async Task Pack_WithAThemeAndNoLooks_OpensTheTheme_InItsBrandColour()
    {
        var pack = ReadPack("kestrel", [], more: p =>
        {
            ((JsonObject)p["edition"]!).Remove("variants");
            p["theme"] = new JsonObject { ["font"] = "Manrope", ["art"] = new JsonObject { ["hero"] = "logo.png" } };
        })!;

        await string.Join(",", pack.Variants).Should().BeEqualTo("theme");
        await pack.Theme!.Brand[0].Should().BeEqualTo("#FF7A2F");
        await pack.Theme.Font.Should().BeEqualTo("Manrope");
        await pack.Theme.Picture(V2pnThemeArt.Hero).Should().BeEqualTo("logo.png");
    }

    [Test]
    public async Task Pack_ThemeWithoutTheEditionGrant_IsNotUsed()
    {
        var p = new TestPartner();
        var cert = p.ReadCert(p.Cert(partner: "kestrel", grants: ["account"]));
        var json = TestPartner.Pack(id: "kestrel", variants: ["theme"], more: x => x["theme"] = new JsonObject());

        var pack = V2pnPartnerTrust.ReadPack(json, TestPartner.Sign(p.Key, json), cert, out _)!;

        await pack.Variants.Count.Should().BeEqualTo(0);
        await (pack.Theme == null).Should().BeTrue();
    }

    [Test]
    public async Task Slots_ThemePicturesThatDoNotFit_AreTakenOut_TheThemeStays()
    {
        static byte[] Png(int w, int h)
        {
            var b = new byte[24];
            new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }.CopyTo(b, 0);
            BitConverter.GetBytes(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(w)).CopyTo(b, 16);
            BitConverter.GetBytes(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(h)).CopyTo(b, 20);
            return b;
        }
        var files = new Dictionary<string, byte[]>
        {
            ["hero.png"] = Png(800, 600),
            ["wide.png"] = Png(1200, 400),
            ["tiny.png"] = Png(100, 100),
            ["sky.png"] = Png(1600, 1000),
            ["strip.png"] = Png(3000, 200),
        };
        var theme = V2pnTheme.Default("#2F7BFF") with
        {
            Art = new Dictionary<string, string> { ["hero"] = "tiny.png", ["banner"] = "wide.png" },
            Background = new V2pnThemeBackground(V2pnThemeBackgroundKinds.Image, [], "strip.png"),
        };
        var pack = new V2pnPartnerPack("kestrel", "Kestrel", "#2F7BFF", null, null, ["theme"], "theme", new Dictionary<string, string>(),
            null, null, null, [], Theme: theme);

        var fitted = V2pnPartnerSlots.Fit(pack, files.GetValueOrDefault, out var problems);

        await fitted.Theme!.Art.ContainsKey("hero").Should().BeFalse();
        await fitted.Theme.Picture("banner").Should().BeEqualTo("wide.png");
        await fitted.Theme.Background.Kind.Should().BeEqualTo(V2pnThemeBackgroundKinds.Solid);
        await problems.Count.Should().BeEqualTo(2);
        await V2pnPartnerSlots.Hero.Fits(800, 600).Should().BeTrue();
        await V2pnPartnerSlots.Banner.Fits(1200, 400).Should().BeTrue();
        await V2pnPartnerSlots.Banner.Fits(800, 600).Should().BeFalse();
        await V2pnPartnerSlots.Backdrop.Fits(1600, 1000).Should().BeTrue();
    }

    [Test]
    public async Task Describe_SaysWhatWasChanged()
    {
        var notes = V2pnThemePalette.Describe(V2pnTheme.Default("#3DD68C"));

        await notes.Any(n => n.Contains("state colours")).Should().BeTrue();
    }
}
