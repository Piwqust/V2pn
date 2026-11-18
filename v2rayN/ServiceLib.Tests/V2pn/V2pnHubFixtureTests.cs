namespace ServiceLib.Tests.V2pn;

/// <summary>
/// hub/ (the V2pn Hub, a Node service) publishes partner-index.json, its signature and the packs on V2pn's server, signed
/// with its own key. V2pn must take them with these very checks. The fixture is what hub/test/make-csharp-fixture.mjs
/// wrote with a throwaway key: run it again when the hub's output changes.
/// </summary>
public class V2pnHubFixtureTests
{
    private static string? _dir;

    private static string Dir()
    {
        if (_dir != null)
        {
            return _dir;
        }
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "hub", "test", "fixtures", "csharp");
            if (Directory.Exists(candidate))
            {
                return _dir = candidate;
            }
        }
        throw new DirectoryNotFoundException("hub/test/fixtures/csharp is not above the test's folder.");
    }

    private static byte[] Bytes(string name) => File.ReadAllBytes(Path.Combine(Dir(), name));

    private static string Text(string name) => File.ReadAllText(Path.Combine(Dir(), name)).Trim();

    private static string RepoRoot() => Path.GetFullPath(Path.Combine(Dir(), "..", "..", "..", ".."));

    private static V2pnPartnerIndex ReadHubIndex()
    {
        var index = V2pnPartnerTrust.ReadIndex(Bytes("partner-index.json"), Text("partner-index.json.sig"), [Text("hub-public-key.txt")], out var error);
        return index ?? throw new InvalidOperationException(error);
    }

    [Test]
    public async Task HubIndex_IsReadAsSigned_EveryPackUnpacksAndFits()
    {
        var index = ReadHubIndex();

        await string.Join("; ", index.Problems).Should().BeEqualTo(string.Empty);
        await string.Join(",", index.Partners.Select(p => p.Id)).Should().BeEqualTo("configra,kestrel");
        foreach (var listing in index.Partners)
        {
            var pack = V2pnPartnerTrust.Unpack(Bytes(listing.Zip.File), listing, out var files, out var error);
            await error.Should().BeEqualTo(string.Empty);
            var fitted = V2pnPartnerSlots.Fit(pack!, files.GetValueOrDefault, out var slotNotes);
            await string.Join("; ", slotNotes.Concat(pack!.Notes).Concat(pack.Theme?.Notes ?? [])).Should().BeEqualTo(string.Empty);
            await (fitted.Logo != null && fitted.LogoOnLight != null && fitted.Mark != null).Should().BeTrue();
        }

        // The legacy index goes out signed and empty, as tools/partner.cs writes it.
        var legacy = V2pnPartnerTrust.ReadIndex(Bytes("partners.json"), Text("partners.json.sig"), [Text("hub-public-key.txt")], out _);
        await legacy!.Partners.Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task HubPack_WithEveryTier2Field_IsTakenWhole()
    {
        var listing = ReadHubIndex().Get("kestrel")!;
        var pack = V2pnPartnerTrust.Unpack(Bytes(listing.Zip.File), listing, out var files, out _)!;
        var fitted = V2pnPartnerSlots.Fit(pack, files.GetValueOrDefault, out _);

        await listing.Name.Should().BeEqualTo("Кестрел");
        await string.Join(",", listing.Grants).Should().BeEqualTo("edition,account,actions,lock");
        await fitted.Theme!.Background.Kind.Should().BeEqualTo(V2pnThemeBackgroundKinds.Image);
        await fitted.Theme.Background.Art.Should().BeEqualTo("background.png");
        await string.Join(",", fitted.Theme.Brand).Should().BeEqualTo("#2F7BFF,#7C4DFF,#FFD600");
        await fitted.OffersLogin.Should().BeTrue();
        await fitted.Lock.Should().BeTrue();
        await fitted.CabinetUrl.Should().BeEqualTo("https://kestrel.example/cabinet");
        await string.Join(",", fitted.ActionList.Select(a => $"{a.Id}:{a.Do}")).Should()
            .BeEqualTo("button-1:connect.best,youtube:connect.recommend,button-3:open.link,button-4:info.card");
    }

    [Test]
    public async Task HubIndex_IsRefused_WithoutTheHubKey()
    {
        var index = V2pnPartnerTrust.ReadIndex(Bytes("partner-index.json"), Text("partner-index.json.sig"), V2pnInfo.PartnerKeys, out var error);

        await (index == null).Should().BeTrue();
        await error.Should().BeEqualTo("The partner index is not signed by V2pn.");
    }

    [Test]
    public async Task HubIndex_HasTheBytesOfV2pnPartnerPublisher_AndConfigrasEntryIsTheOnePartnerCsPublishes()
    {
        var hub = ReadHubIndex();
        var configra = V2pnPartnerPublisher.Build(Path.Combine(RepoRoot(), "partners", "configra"), new DateOnly(2026, 11, 17), out var error);
        await error.Should().BeEqualTo(string.Empty);

        var hubConfigra = hub.Get("configra")!;
        // Everything but the zip's bytes: Node's deflate is not .NET's, so the zip's size and hash differ.
        var same = configra!.Listing with { Zip = hubConfigra.Zip };
        var published = DateTimeOffset.Parse(System.Text.Json.Nodes.JsonNode.Parse(Bytes("partner-index.json"))!["published"]!.GetValue<string>(),
            System.Globalization.CultureInfo.InvariantCulture);
        var expected = V2pnPartnerPublisher.IndexBytes([same, hub.Get("kestrel")!], published);
        static string Lf(byte[] b) => Encoding.UTF8.GetString(b).Replace("\r\n", "\n", StringComparison.Ordinal);
        await Lf(Bytes("partner-index.json")).Should().BeEqualTo(Lf(expected));

        // And the pack holds the same files: partner.json (line ends aside) and the same pictures.
        V2pnPartnerTrust.Unpack(configra.Zip, configra.Listing, out var theirs, out _);
        V2pnPartnerTrust.Unpack(Bytes(hubConfigra.Zip.File), hubConfigra, out var ours, out _);
        await string.Join(",", ours.Keys.Order(StringComparer.Ordinal)).Should().BeEqualTo(string.Join(",", theirs.Keys.Order(StringComparer.Ordinal)));
        foreach (var (name, content) in theirs)
        {
            await (name == V2pnPartnerTrust.PackFile ? Lf(content) == Lf(ours[name]) : content.SequenceEqual(ours[name])).Should().BeTrue();
        }
    }

    [Test]
    public async Task HubColours_AreTheOnesV2pnDerives()
    {
        var failures = new List<string>();
        foreach (var node in System.Text.Json.Nodes.JsonNode.Parse(Bytes("colours.json"))!.AsArray())
        {
            var brand = node!["brand"]!.AsArray().Select(b => b!.GetValue<string>()).ToList();
            var bg = node["background"]!;
            var kind = bg["type"]!.GetValue<string>();
            var background = kind switch
            {
                V2pnThemeBackgroundKinds.Gradient => new V2pnThemeBackground(kind, bg["colors"]!.AsArray().Select(c => c!.GetValue<string>()).ToList(), null),
                V2pnThemeBackgroundKinds.Image => new V2pnThemeBackground(kind, [], bg["art"]!.GetValue<string>()),
                _ => V2pnThemeBackground.Solid,
            };
            var theme = V2pnTheme.Default(null) with { Brand = brand, Background = background };
            var dark = node["dark"]!.GetValue<bool>();
            var c = V2pnThemePalette.Derive(theme, dark);
            var context = $"{string.Join(",", brand)} {kind} {(dark ? "dark" : "light")}";
            void Same(string what, string? ours, string? theirs)
            {
                if (ours != theirs)
                {
                    failures.Add($"{context}: {what} hub {ours}, V2pn {theirs}");
                }
            }
            Same("accent", node["accent"]!.GetValue<string>(), c.Accent);
            Same("onAccent", node["onAccent"]!.GetValue<string>(), c.OnAccent);
            Same("accentText", node["accentText"]!.GetValue<string>(), c.AccentText);
            Same("ground", node["ground"]!.GetValue<string>(), c.Ground);
            Same("surface", node["surface"]!.GetValue<string>(), c.Surface);
            Same("ink", node["ink"]!.GetValue<string>(), c.Ink);
            var stage = V2pnThemePalette.Stage(theme, dark);
            var hubStage = node["stage"];
            if ((hubStage == null) != (stage == null))
            {
                failures.Add($"{context}: stage hub {hubStage != null}, V2pn {stage != null}");
            }
            else if (stage != null)
            {
                Same("stage stops", string.Join(",", hubStage!["stops"]!.AsArray().Select(s => s!.GetValue<string>())), string.Join(",", stage.Stops));
                Same("stage ink", hubStage["ink"]!.GetValue<string>(), stage.Ink);
                Same("stage soft", hubStage["soft"]!.GetValue<string>(), stage.Soft);
            }
        }
        await string.Join("; ", failures).Should().BeEqualTo(string.Empty);
    }
}
