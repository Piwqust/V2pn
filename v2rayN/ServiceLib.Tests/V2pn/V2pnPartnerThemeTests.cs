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
              "palette": { "brand": ["#0a7cff", "#ff2d87", "#ffd600"] },
              "background": { "type": "gradient", "colors": ["#0A7CFF", "#5B2DFF", "#141A33"] },
              "connect": "Toggle",
              "window": "full"
            }
            """);

        await string.Join(",", theme.Brand).Should().BeEqualTo("#0A7CFF,#FF2D87,#FFD600");
        await theme.Background.Kind.Should().BeEqualTo(V2pnThemeBackgroundKinds.Gradient);
        await theme.Background.Colors.Count.Should().BeEqualTo(3);
        await theme.Connect.Should().BeEqualTo(V2pnThemeConnect.Toggle);
        await theme.Window.Should().BeEqualTo(V2pnThemeWindow.Full);
        await theme.Notes.Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task Theme_Missing_IsTheDefaultInTheBrandColour_NeonInTheMiniWindow()
    {
        var theme = V2pnThemeReader.Parse(null, "#2f7bff", _ => false);

        await theme.Brand[0].Should().BeEqualTo("#2F7BFF");
        await theme.Connect.Should().BeEqualTo(V2pnThemeConnect.Round);
        await theme.Window.Should().BeEqualTo(V2pnThemeWindow.Mini);
        await theme.Background.Kind.Should().BeEqualTo(V2pnThemeBackgroundKinds.Solid);
        await theme.Notes.Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task Theme_EveryWrongField_FallsBack_WithANote()
    {
        var theme = Read("""
            {
              "connect": "capsule", "window": "huge",
              "palette": { "brand": ["red", "#12345", "#112233", "#445566", "#778899", "#AABBCC"] },
              "background": { "type": "gradient", "colors": ["#000000"] },
              "script": "alert(1)"
            }
            """);

        await theme.Connect.Should().BeEqualTo(V2pnThemeConnect.Round);
        await theme.Window.Should().BeEqualTo(V2pnThemeWindow.Mini);
        await string.Join(",", theme.Brand).Should().BeEqualTo("#112233,#445566,#778899");
        await theme.Background.Kind.Should().BeEqualTo(V2pnThemeBackgroundKinds.Solid);
        foreach (var field in new[] { "connect", "window", "brand", "background", "script" })
        {
            await theme.Notes.Any(n => n.Contains(field)).Should().BeTrue();
        }
    }

    [Test]
    public async Task Theme_OfTheSixTemplates_IsReadWithoutError_ItsOldFieldsNoted()
    {
        var theme = Read("""
            {
              "template": "split", "connect": "toggle", "font": "Manrope", "corners": "round", "density": "compact",
              "palette": { "brand": ["#6D5DFC"], "tone": "cool" },
              "blocks": ["plan", "servers"], "art": { "hero": "orb.png" }
            }
            """, pictures: ["orb.png"]);

        await theme.Brand[0].Should().BeEqualTo("#6D5DFC");
        await theme.Connect.Should().BeEqualTo(V2pnThemeConnect.Toggle);
        foreach (var field in new[] { "template", "font", "corners", "density", "blocks", "art", "tone" })
        {
            await theme.Notes.Any(n => n.Contains($"theme.{field}") || n.Contains($"palette.{field}")).Should().BeTrue();
        }
    }

    [Test]
    public async Task Theme_NotAnObject_OrWithWrongShapes_StaysUsable()
    {
        foreach (var json in new[] { "\"split\"", "[]", "42", """{ "palette": "#FF0000", "background": "image", "connect": 7 }""" })
        {
            var theme = Read(json);
            await theme.Brand[0].Should().BeEqualTo("#2F7BFF");
            await theme.Connect.Should().BeEqualTo(V2pnThemeConnect.Round);
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
            var theme = V2pnTheme.Default(null) with { Brand = brand, Background = background };
            foreach (var dark in new[] { false, true })
            {
                var c = V2pnThemePalette.Derive(theme, dark);
                var context = $"{string.Join(",", brand)} {background.Kind} {(dark ? "dark" : "light")}";
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
    public async Task Variants_OlderNames_ComeToNeonOrPass()
    {
        await V2pnPartnerVariants.Normalize("signature").Should().BeEqualTo(V2pnPartnerVariants.Pass);
        await V2pnPartnerVariants.Normalize("warm").Should().BeEqualTo(V2pnPartnerVariants.Pass);
        foreach (var name in new[] { "theme", "cabinet", "studio", "ascii", " NEON " })
        {
            await V2pnPartnerVariants.Normalize(name).Should().BeEqualTo(V2pnPartnerVariants.Neon);
        }
        await (V2pnPartnerVariants.Normalize("bogus") == null).Should().BeTrue();
        await string.Join(",", V2pnPartnerVariants.For(["pass", "signature", "bogus"], hasTheme: true)).Should().BeEqualTo("pass");
        await string.Join(",", V2pnPartnerVariants.For([], hasTheme: true)).Should().BeEqualTo("neon,pass");
        await string.Join(",", V2pnPartnerVariants.For([], hasTheme: false)).Should().BeEqualTo("pass,neon");
    }

    [Test]
    public async Task Pack_OfConfigra_ListingItsOldLooks_GetsNeonAndPass()
    {
        var pack = ReadPack("configra", null, first: "cabinet")!;

        await string.Join(",", pack.Variants).Should().BeEqualTo("pass,neon");
        await pack.DefaultVariant.Should().BeEqualTo(V2pnPartnerVariants.Neon);
        await pack.Theme!.Brand[0].Should().BeEqualTo("#FF7A2F");
    }

    [Test]
    public async Task Pack_WithAThemeAndNoLooks_OpensNeon_InItsBrandColour()
    {
        var pack = ReadPack("kestrel", [], more: p =>
        {
            ((JsonObject)p["edition"]!).Remove("variants");
            p["theme"] = new JsonObject { ["connect"] = "toggle", ["window"] = "full" };
        })!;

        await string.Join(",", pack.Variants).Should().BeEqualTo("neon,pass");
        await pack.DefaultVariant.Should().BeEqualTo(V2pnPartnerVariants.Neon);
        await pack.Theme!.Brand[0].Should().BeEqualTo("#FF7A2F");
        await pack.Theme.Connect.Should().BeEqualTo(V2pnThemeConnect.Toggle);
        await pack.Theme.Window.Should().BeEqualTo(V2pnThemeWindow.Full);
    }

    [Test]
    public async Task Pack_ThemeWithoutTheEditionGrant_IsNotUsed()
    {
        var p = new TestPartner();
        var cert = p.ReadCert(p.Cert(partner: "kestrel", grants: ["account"]));
        var json = TestPartner.Pack(id: "kestrel", variants: ["neon"], more: x => x["theme"] = new JsonObject());

        var pack = V2pnPartnerTrust.ReadPack(json, TestPartner.Sign(p.Key, json), cert, out _)!;

        await pack.Variants.Count.Should().BeEqualTo(0);
        await (pack.Theme == null).Should().BeTrue();
    }

    [Test]
    public async Task Slots_ACardPictureThatDoesNotFit_IsTakenOut_TheThemeStays()
    {
        static byte[] Png(int w, int h)
        {
            var b = new byte[24];
            new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }.CopyTo(b, 0);
            BitConverter.GetBytes(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(w)).CopyTo(b, 16);
            BitConverter.GetBytes(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(h)).CopyTo(b, 20);
            return b;
        }
        var files = new Dictionary<string, byte[]> { ["sky.png"] = Png(1600, 1000), ["strip.png"] = Png(3000, 200) };
        V2pnPartnerPack Pack(string picture) => new("kestrel", "Kestrel", "#2F7BFF", null, null, ["neon"], "neon", new Dictionary<string, string>(),
            null, null, null, [], Theme: V2pnTheme.Default("#2F7BFF") with { Background = new V2pnThemeBackground(V2pnThemeBackgroundKinds.Image, [], picture) });

        var lost = V2pnPartnerSlots.Fit(Pack("strip.png"), files.GetValueOrDefault, out var problems);
        var kept = V2pnPartnerSlots.Fit(Pack("sky.png"), files.GetValueOrDefault, out _);

        await lost.Theme!.Background.Kind.Should().BeEqualTo(V2pnThemeBackgroundKinds.Solid);
        await lost.Theme.Brand[0].Should().BeEqualTo("#2F7BFF");
        await problems.Count.Should().BeEqualTo(1);
        await kept.Theme!.Background.Art.Should().BeEqualTo("sky.png");
    }

    [Test]
    public async Task Describe_SaysWhatWasChanged()
    {
        var notes = V2pnThemePalette.Describe(V2pnTheme.Default("#3DD68C"));

        await notes.Any(n => n.Contains("state colours")).Should().BeTrue();
    }

    [Test]
    public async Task Stage_OfAGradient_ReadsEverywhere_InBothModes()
    {
        var failures = new List<string>();
        var themes = new[]
        {
            V2pnTheme.Default("#6D5DFC") with { Background = new V2pnThemeBackground(V2pnThemeBackgroundKinds.Gradient, ["#3B2FD9", "#6D28D9", "#C026D3"], null) },
            V2pnTheme.Default("#FF7A2F") with { Background = new V2pnThemeBackground(V2pnThemeBackgroundKinds.Gradient, ["#FF7A2F", "#FFD9A8"], null) },
            // The worst case of the prototype: a pale green brand next to "protected".
            V2pnTheme.Default("#9BE15D") with { Background = new V2pnThemeBackground(V2pnThemeBackgroundKinds.Gradient, ["#9BE15D", "#00A37A"], null) },
            V2pnTheme.Default("#FFFFFF") with { Background = new V2pnThemeBackground(V2pnThemeBackgroundKinds.Gradient, ["#FFFFFF", "#F4F4F4"], null) },
            V2pnTheme.Default("#808080") with { Background = new V2pnThemeBackground(V2pnThemeBackgroundKinds.Gradient, ["#7A7A7A", "#8A8A8A"], null) },
        };
        foreach (var theme in themes)
        {
            foreach (var dark in new[] { false, true })
            {
                var stage = V2pnThemePalette.Stage(theme, dark)!;
                var name = $"{theme.Background.Colors[0]} {(dark ? "dark" : "light")}";
                var stops = stage.Stops.Select(V2pnColor.Parse).ToList();
                if (stage.Scrim is { } scrim)
                {
                    stops = stops.Select(s => V2pnColor.Parse("#" + scrim[3..]).Over(s, Convert.ToInt32(scrim[1..3], 16) / 255.0)).ToList();
                }
                var ink = V2pnColor.Parse(stage.Ink);
                void Check(bool ok, string what)
                {
                    if (!ok)
                    {
                        failures.Add($"{name}: {what}");
                    }
                }
                Check(V2pnThemePalette.MinContrast(ink, stops) >= V2pnThemePalette.TextContrast, "ink");
                Check(V2pnThemePalette.MinContrast(V2pnColor.Parse(stage.Soft), stops) >= V2pnThemePalette.TextContrast, "soft");
                var ghost = V2pnColor.Parse("#" + stage.Ghost[3..]);
                var ghostAlpha = Convert.ToInt32(stage.Ghost[1..3], 16) / 255.0;
                Check(V2pnThemePalette.MinContrast(ink, stops.Select(s => ghost.Over(s, ghostAlpha)).ToList()) >= V2pnThemePalette.TextContrast, "ink on a ghost chip");
                Check(V2pnColor.Contrast(V2pnColor.Parse(stage.OnSolid), V2pnColor.Parse(stage.Solid)) >= V2pnThemePalette.TextContrast, "label on the solid button");
                Check(!stage.Stops.Select(V2pnColor.Parse).Any(V2pnThemePalette.NearState), "a stop near a state colour");
                if (dark)
                {
                    var lightness = stage.Stops.Select(h => V2pnColor.Parse(h).OkLightness).ToList();
                    Check(lightness[0] <= 0.785 && lightness.Skip(1).All(l => l <= 0.725), "dark stops not toned down");
                }
            }
        }
        await string.Join("; ", failures).Should().BeEqualTo(string.Empty);
    }

    [Test]
    public async Task Stage_OfAPicture_HasAScrim_AndASolidBackgroundHasNone()
    {
        var picture = V2pnTheme.Default("#E11D48") with { Background = new V2pnThemeBackground(V2pnThemeBackgroundKinds.Image, [], "dunes.jpg") };

        foreach (var dark in new[] { false, true })
        {
            var stage = V2pnThemePalette.Stage(picture, dark)!;
            var ink = V2pnColor.Parse(stage.Ink);
            var scrim = V2pnColor.Parse("#" + stage.Scrim![3..]);
            var alpha = Convert.ToInt32(stage.Scrim[1..3], 16) / 255.0;
            await stage.Art.Should().BeEqualTo("dunes.jpg");
            foreach (var pixel in new[] { V2pnColor.Black, V2pnColor.White, V2pnColor.Parse("#808080") })
            {
                await (V2pnColor.Contrast(ink, scrim.Over(pixel, alpha)) >= V2pnThemePalette.TextContrast).Should().BeTrue();
            }
        }
        await (V2pnThemePalette.Stage(V2pnTheme.Default("#2F7BFF"), dark: true) == null).Should().BeTrue();
    }

    [Test]
    public async Task ReadableOn_MovesOnlyTheLightness()
    {
        foreach (var (hex, ground) in new[] { ("#FF7A2F", "#F0F1F5"), ("#FFD600", "#FFFFFF"), ("#3B2FD9", "#26262F"), ("#121211", "#1E1E25") })
        {
            var colour = V2pnColor.Parse(hex);
            var readable = V2pnThemePalette.ReadableOn(colour, V2pnColor.Parse(ground));
            await (V2pnColor.Contrast(readable, V2pnColor.Parse(ground)) >= V2pnThemePalette.TextContrast).Should().BeTrue();
            if (colour.Oklch.Chroma > 0.08 && readable.Oklch.Chroma > 0.04)
            {
                var diff = Math.Abs(readable.Oklch.Hue - colour.Oklch.Hue) % 360;
                await (Math.Min(diff, 360 - diff) < 8).Should().BeTrue();
            }
        }
        var blue = V2pnColor.Parse("#2F7BFF");
        await V2pnColor.FromOklch(blue.OkLightness, blue.Oklch.Chroma, blue.Oklch.Hue).Hex.Should().BeEqualTo("#2F7BFF");
    }
}
