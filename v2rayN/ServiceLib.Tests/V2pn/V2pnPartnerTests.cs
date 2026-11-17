namespace ServiceLib.Tests.V2pn;

/// <summary>A root key, the partner listings and packs it publishes, as tools/partner.cs publish writes them.</summary>
internal sealed class TestPartner
{
    public readonly ECDsa Root = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public static readonly DateOnly Today = new(2026, 11, 15);
    public static readonly byte[] Logo = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    public string RootPublic => Convert.ToBase64String(Root.ExportSubjectPublicKeyInfo());

    public static byte[] Pack(string id = "configra", string? accent = "#ff7a2f", string[]? variants = null, string? first = "cabinet",
        string? api = "https://config.example.com/v2pn/api/", (string Name, byte[] Content)[]? assets = null, string name = "Configra",
        Action<JsonObject>? more = null)
    {
        var pack = new JsonObject
        {
            ["schema"] = 1,
            ["id"] = id,
            ["name"] = name,
            ["brand"] = new JsonObject { ["accent"] = accent, ["logo"] = "logo.png", ["mark"] = "logo.png" },
            // The look switcher's fields of older packs: read without error, noted, not used.
            ["edition"] = new JsonObject
            {
                ["variants"] = new JsonArray((variants ?? ["signature", "warm", "cabinet"]).Select(v => (JsonNode)v).ToArray()),
                ["default"] = first,
            },
            ["links"] = new JsonObject { ["cabinet"] = "https://config.example.com/cabinet", ["support"] = "http://insecure.example.com" },
            ["api"] = api,
            ["assets"] = new JsonArray((assets ?? [("logo.png", Logo)]).Select(a => (JsonNode)new JsonObject
            {
                ["name"] = a.Name,
                ["size"] = a.Content.Length,
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(a.Content)),
            }).ToArray()),
        };
        more?.Invoke(pack);
        return Encoding.UTF8.GetBytes(pack.ToJsonString());
    }

    /// <summary>A pack's zip: partner.json and <paramref name="files"/> (logo.png when none are given).</summary>
    public static byte[] Zip(byte[]? pack = null, params (string Name, byte[] Content)[] files) =>
        V2pnPartnerPublisher.Zip(files.Length == 0 ? [("partner.json", pack ?? Pack()), ("logo.png", Logo)] : files.Prepend(("partner.json", pack ?? Pack())));

    public static V2pnPartnerZip ZipRef(string id, byte[] zip) =>
        new(V2pnPartnerTrust.ZipName(id), zip.LongLength, Convert.ToHexStringLower(SHA256.HashData(zip)));

    /// <summary>A listing as the signed index carries it; its zip is <paramref name="zip"/>, or the default pack of <paramref name="partner"/>.</summary>
    public static V2pnPartnerListing Listing(string partner = "configra", string[]? domains = null, string[]? grants = null, string until = "2027-11-01",
        JsonObject? reviewed = null, byte[]? zip = null)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (file, hash) in reviewed ?? [])
        {
            if (V2pnReviewedFiles.All.Contains(file) && hash?.GetValue<string>() is { Length: 64 } h && h.All(Uri.IsHexDigit))
            {
                hashes[file] = h.ToLowerInvariant();
            }
        }
        return new V2pnPartnerListing(partner, "Configra", domains ?? ["config.example.com", "*.sub.example.com"],
            (grants ?? ["edition", "account"]).Where(V2pnPartnerGrants.All.Contains).Distinct().ToList(),
            DateOnly.ParseExact(until, "yyyy-MM-dd"), ZipRef(partner, zip ?? Zip(Pack(id: partner))), hashes);
    }

    public static string Sign(ECDsa key, byte[] data) =>
        Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

    /// <summary>
    /// Puts partner-index.json(.sig) signed with the root and every zip on <paramref name="site"/>, and partners.json(.sig)
    /// signed and empty, as every release publishes it for older copies.
    /// </summary>
    public void Publish(TestSite site, DateTimeOffset published, params (V2pnPartnerListing Listing, byte[] Zip)[] partners)
    {
        var index = V2pnPartnerPublisher.IndexBytes(partners.Select(p => p.Listing), published);
        site.Put(V2pnPartnerTrust.IndexFile, index);
        site.Put(V2pnPartnerTrust.IndexFile + ".sig", Encoding.UTF8.GetBytes(Sign(Root, index)));
        var legacy = V2pnPartnerPublisher.IndexBytes([], published);
        site.Put(V2pnPartnerTrust.LegacyIndexFile, legacy);
        site.Put(V2pnPartnerTrust.LegacyIndexFile + ".sig", Encoding.UTF8.GetBytes(Sign(Root, legacy)));
        foreach (var (listing, zip) in partners)
        {
            site.Put(listing.Zip.File, zip);
        }
    }

    /// <summary>
    /// The partners of a copy of V2pn: <paramref name="edition"/> is the partner of an older build's marker (whose tab
    /// opens before any choice), null for a person who came from no partner.
    /// </summary>
    public V2pnPartners Manager(TestSite site, string root, List<string?> subscriptions, string? edition = "configra") =>
        Manager(site, root, subscriptions, new V2pnEdition(edition));

    /// <summary>The partners of a copy of V2pn with the person's own record of where they came from and which tab is open.</summary>
    public V2pnPartners Manager(TestSite site, string root, List<string?> subscriptions, V2pnEdition edition) =>
        new([RootPublic], () => root, edition)
        {
            Fetch = site.FetchAsync,
            Sources = [TestSite.Source],
            SubscriptionUrls = () => Task.FromResult<IReadOnlyList<string?>>(subscriptions.ToList()),
            Today = () => Today,
        };
}

/// <summary>A releases page: &lt;source&gt;/latest/download/&lt;file&gt;.</summary>
internal sealed class TestSite
{
    public const string Source = "https://releases.example.org/v2pn";
    public readonly Dictionary<string, byte[]> Files = new(StringComparer.Ordinal);
    public readonly List<string> Asked = [];

    public void Put(string name, byte[] body) => Files[$"{Source}/latest/download/{name}"] = body;

    public Task<V2pnHttp.Result> FetchAsync(string url, CancellationToken _)
    {
        Asked.Add(url);
        return Task.FromResult(Files.TryGetValue(url, out var body)
            ? new V2pnHttp.Result(HttpStatusCode.OK, body, null)
            : new V2pnHttp.Result(HttpStatusCode.NotFound, null, "HTTP 404"));
    }
}

public class V2pnPartnerTests
{
    private static readonly DateTimeOffset Published = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static (byte[] Json, string Signature) SignedIndex(TestPartner p, params V2pnPartnerListing[] listings)
    {
        var json = V2pnPartnerPublisher.IndexBytes(listings, Published);
        return (json, TestPartner.Sign(p.Root, json));
    }

    private static byte[] IndexWith(Action<JsonObject> change)
    {
        var root = (JsonObject)JsonNode.Parse(V2pnPartnerPublisher.IndexBytes([TestPartner.Listing()], Published))!;
        change((JsonObject)root["partners"]![0]!);
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    #region The index

    [Test]
    public async Task Index_SignedByRoot_IsRead()
    {
        var p = new TestPartner();
        var listing = TestPartner.Listing(grants: ["edition", "routing-v9", "actions", "lock", "actions"], reviewed: new JsonObject { ["routing.json"] = new string('A', 64) });
        var (json, signature) = SignedIndex(p, listing);

        var index = V2pnPartnerTrust.ReadIndex(json, signature, [p.RootPublic], out var error);

        await error.Should().BeEqualTo(string.Empty);
        var read = index!.Get("configra")!;
        await read.Name.Should().BeEqualTo("Configra");
        await read.Domains.Count.Should().BeEqualTo(2);
        // A grant this V2pn does not know is left out; the rest stands.
        await read.Grants.Should().BeEquivalentTo(new[] { "edition", "actions", "lock" });
        await read.Until.Should().BeEqualTo(new DateOnly(2027, 11, 1));
        await read.ReviewedFiles["routing.json"].Should().BeEqualTo(new string('a', 64));
        await read.Zip.Should().BeEqualTo(listing.Zip);
    }

    [Test]
    public async Task Index_SignedByAnotherKey_OrTampered_IsRefused()
    {
        var p = new TestPartner();
        var (json, signature) = SignedIndex(p, TestPartner.Listing());
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        await (V2pnPartnerTrust.ReadIndex(json, TestPartner.Sign(stranger, json), [p.RootPublic], out var e1) == null).Should().BeTrue();
        await e1.Contains("not signed").Should().BeTrue();
        await (V2pnPartnerTrust.ReadIndex(json, null, [p.RootPublic], out _) == null).Should().BeTrue();

        // One more domain, or the same JSON with a space added: not the bytes the root signed.
        var widened = IndexWith(e => ((JsonArray)e["domains"]!).Add((JsonNode)"*.evil.example"));
        await (V2pnPartnerTrust.ReadIndex(widened, signature, [p.RootPublic], out _) == null).Should().BeTrue();
        var reformatted = json.Append((byte)' ').ToArray();
        await (V2pnPartnerTrust.ReadIndex(reformatted, signature, [p.RootPublic], out _) == null).Should().BeTrue();
    }

    [Test]
    public async Task Index_EntryBreakingARule_IsLeftOut_ADuplicateRefusesTheIndex()
    {
        var p = new TestPartner();
        foreach (var bad in new Action<JsonObject>[]
                 {
                     e => e["domains"] = new JsonArray("*.com"),
                     e => e["id"] = "Bad/Id",
                     e => e["until"] = "soon",
                     e => e["name"] = "V2pn Pro",
                     e => e["pack"]!["file"] = "../partner-configra.zip",
                     e => e["pack"]!["sha256"] = "not-a-hash",
                 })
        {
            var json = IndexWith(bad);
            var index = V2pnPartnerTrust.ReadIndex(json, TestPartner.Sign(p.Root, json), [p.RootPublic], out var error);
            await error.Should().BeEqualTo(string.Empty);
            await index!.Partners.Count.Should().BeEqualTo(0);
            await index.Problems.Count.Should().BeEqualTo(1);
        }

        var twice = V2pnPartnerPublisher.IndexBytes([TestPartner.Listing(), TestPartner.Listing()], Published);
        await (V2pnPartnerTrust.ReadIndex(twice, TestPartner.Sign(p.Root, twice), [p.RootPublic], out var dupError) == null).Should().BeTrue();
        await dupError.Contains("twice").Should().BeTrue();
    }

    [Test]
    public async Task Domains_MatchExactlyOrUnderAWildcard()
    {
        var listing = TestPartner.Listing();

        await V2pnPartnerTrust.DomainAllowed(listing, "https://config.example.com/s/abc").Should().BeTrue();
        await V2pnPartnerTrust.DomainAllowed(listing, "https://a.sub.example.com/x").Should().BeTrue();
        await V2pnPartnerTrust.DomainAllowed(listing, "https://CONFIG.example.com./s").Should().BeTrue();
        await V2pnPartnerTrust.DomainAllowed(listing, "https://sub.example.com/x").Should().BeFalse();
        await V2pnPartnerTrust.DomainAllowed(listing, "https://evilconfig.example.com/").Should().BeFalse();
        await V2pnPartnerTrust.DomainAllowed(listing, "https://config.example.com.evil.net/").Should().BeFalse();
        await V2pnPartnerTrust.DomainAllowed(listing, "not a url").Should().BeFalse();

        var index = new V2pnPartnerIndex([TestPartner.Listing(partner: "kestrel", domains: ["kestrel.example"]), listing], []);
        await index.ForUrl("https://a.sub.example.com/s/1")!.Id.Should().BeEqualTo("configra");
        await index.ForUrl("https://kestrel.example/s/1")!.Id.Should().BeEqualTo("kestrel");
        await (index.ForUrl("https://other.example/s/1") == null).Should().BeTrue();
    }

    #endregion The index

    #region The pack

    [Test]
    public async Task Pack_IsRead_AndKeepsOnlyWhatItMayUse()
    {
        var pack = V2pnPartnerTrust.ReadPack(TestPartner.Pack(), TestPartner.Listing(), out var error);

        await error.Should().BeEqualTo(string.Empty);
        await pack!.Accent.Should().BeEqualTo("#FF7A2F");
        await pack.Logo.Should().BeEqualTo("logo.png");
        await pack.CabinetUrl.Should().BeEqualTo("https://config.example.com/cabinet");
        await (pack.SupportUrl == null).Should().BeTrue();
        await pack.ApiUrl.Should().BeEqualTo("https://config.example.com/v2pn/api/");
        await (pack.Theme != null).Should().BeTrue();
        // The look switcher's fields are read without error and only noted.
        await pack.Notes.Count.Should().BeEqualTo(2);
        await pack.Notes[0].Contains("edition.variants is no longer used").Should().BeTrue();
    }

    [Test]
    public async Task Pack_ForAnotherPartner_IsRefused()
    {
        await (V2pnPartnerTrust.ReadPack(TestPartner.Pack(id: "someone-else"), TestPartner.Listing(), out var error) == null).Should().BeTrue();
        await error.Contains("another partner").Should().BeTrue();
    }

    [Test]
    public async Task Pack_WithoutTheGrants_LosesItsLookAndApi()
    {
        var pack = V2pnPartnerTrust.ReadPack(TestPartner.Pack(), TestPartner.Listing(grants: []), out _)!;

        await (pack.Theme == null).Should().BeTrue();
        await (pack.ApiUrl == null).Should().BeTrue();
        await pack.Name.Should().BeEqualTo("Configra");
    }

    [Test]
    public async Task Pack_ApiOnAForeignDomain_IsDropped()
    {
        var pack = V2pnPartnerTrust.ReadPack(TestPartner.Pack(api: "https://tracker.example.net/"), TestPartner.Listing(), out _)!;

        await (pack.ApiUrl == null).Should().BeTrue();
    }

    [Test]
    public async Task Pack_WithAPathOrABadColour_IsMalformed()
    {
        foreach (var json in new[]
                 {
                     TestPartner.Pack(assets: [("../evil.png", TestPartner.Logo)]),
                     TestPartner.Pack(assets: [("sub/evil.png", TestPartner.Logo)]),
                     TestPartner.Pack(assets: [("script.svg", TestPartner.Logo)]),
                     TestPartner.Pack(assets: [("con.png", TestPartner.Logo)]),
                     TestPartner.Pack(assets: [("logo.png", TestPartner.Logo), ("LOGO.png", TestPartner.Logo)]),
                     TestPartner.Pack(accent: "orange"),
                 })
        {
            await (V2pnPartnerTrust.ReadPack(json, TestPartner.Listing(), out var error) == null).Should().BeTrue();
            await error.StartsWith("partner.json").Should().BeTrue();
        }
    }

    [Test]
    public async Task Pack_ReadsTheSquareMark_AndRefusesANameThatPassesForV2pn()
    {
        await V2pnPartnerTrust.ReadPack(TestPartner.Pack(), TestPartner.Listing(), out _)!.Mark.Should().BeEqualTo("logo.png");

        foreach (var name in new[] { "V2pn Pro", "Super v2 PN", "v2pn" })
        {
            await (V2pnPartnerTrust.ReadPack(TestPartner.Pack(name: name), TestPartner.Listing(), out _) == null).Should().BeTrue();
        }
    }

    #endregion The pack

    #region The zip

    [Test]
    public async Task Unpack_TheZipTheIndexNames_GivesThePackAndItsFiles()
    {
        var zip = TestPartner.Zip();

        var pack = V2pnPartnerTrust.Unpack(zip, TestPartner.Listing(zip: zip), out var files, out var error);

        await error.Should().BeEqualTo(string.Empty);
        await pack!.Id.Should().BeEqualTo("configra");
        await files.Keys.Order().Should().BeEquivalentTo(new[] { "logo.png", "partner.json" });
        await files["logo.png"].Should().BeEquivalentTo(TestPartner.Logo);
    }

    [Test]
    public async Task Unpack_AZipWhoseHashOrSizeIsNotTheIndexs_IsRefused()
    {
        var zip = TestPartner.Zip();
        var listing = TestPartner.Listing(zip: zip);
        var other = TestPartner.Zip(TestPartner.Pack(accent: "#123456"));

        await (V2pnPartnerTrust.Unpack(other, listing, out _, out var error) == null).Should().BeTrue();
        await error.Contains("not the one the partner index names").Should().BeTrue();
        await (V2pnPartnerTrust.Unpack(null, listing, out _, out _) == null).Should().BeTrue();
        await (V2pnPartnerTrust.Unpack(zip.Append((byte)0).ToArray(), listing, out _, out _) == null).Should().BeTrue();
    }

    [Test]
    public async Task Unpack_EntriesWithPathsOrOfOtherKinds_AreRefused()
    {
        foreach (var name in new[] { "../logo.png", "..\\logo.png", "sub/logo.png", "/logo.png", ".logo.png", "con.png", "run.exe", "logo.png.lnk", "лого.png" })
        {
            var zip = RawZip(("partner.json", TestPartner.Pack()), ("logo.png", TestPartner.Logo), (name, TestPartner.Logo));
            await (V2pnPartnerTrust.Unpack(zip, TestPartner.Listing(zip: zip), out var files, out var error) == null).Should().BeTrue();
            await error.Contains("does not take").Should().BeTrue();
            await files.Count.Should().BeEqualTo(0);
        }
        var twice = RawZip(("partner.json", TestPartner.Pack()), ("logo.png", TestPartner.Logo), ("LOGO.png", TestPartner.Logo));
        await (V2pnPartnerTrust.Unpack(twice, TestPartner.Listing(zip: twice), out _, out var twiceError) == null).Should().BeTrue();
        await twiceError.Contains("twice").Should().BeTrue();
    }

    [Test]
    public async Task Unpack_AHugeEntry_IsRefusedWithoutReadingItAll()
    {
        // Zeros compress to almost nothing: a small zip that would unpack past the limit.
        var huge = new byte[V2pnPartnerTrust.MaxAssetBytes + 1];
        var zip = RawZip(("partner.json", TestPartner.Pack()), ("logo.png", huge));

        await (zip.Length < 64 * 1024).Should().BeTrue();
        await (V2pnPartnerTrust.Unpack(zip, TestPartner.Listing(zip: zip), out _, out var error) == null).Should().BeTrue();
        await error.Contains("larger than").Should().BeTrue();
    }

    [Test]
    public async Task Unpack_AFileNotMatchingPartnerJson_OrNotListedThere_IsRefused()
    {
        var changed = TestPartner.Zip(null, ("logo.png", [9, 9, 9]));
        await (V2pnPartnerTrust.Unpack(changed, TestPartner.Listing(zip: changed), out _, out var error) == null).Should().BeTrue();
        await error.Contains("logo.png does not match").Should().BeTrue();

        var stray = TestPartner.Zip(null, ("logo.png", TestPartner.Logo), ("extra.png", TestPartner.Logo));
        await (V2pnPartnerTrust.Unpack(stray, TestPartner.Listing(zip: stray), out _, out var strayError) == null).Should().BeTrue();
        await strayError.Contains("extra.png").Should().BeTrue();

        var missing = TestPartner.Zip(null, ("other.png", TestPartner.Logo));
        await (V2pnPartnerTrust.Unpack(missing, TestPartner.Listing(zip: missing), out _, out _) == null).Should().BeTrue();

        var notZip = "not a zip"u8.ToArray();
        await (V2pnPartnerTrust.Unpack(notZip, TestPartner.Listing(zip: notZip), out _, out var notZipError) == null).Should().BeTrue();
        await notZipError.Contains("not a zip").Should().BeTrue();
    }

    /// <summary>A zip written as is, names and all, as a hostile one would be.</summary>
    private static byte[] RawZip(params (string Name, byte[] Content)[] files)
    {
        using var stream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                using var write = archive.CreateEntry(name).Open();
                write.Write(content);
            }
        }
        return stream.ToArray();
    }

    #endregion The zip

    [Test]
    public async Task Headers_TokenIsRead_ThePartnerFolderIsIgnored()
    {
        var info = SubPanelHeaders.Parse(new Dictionary<string, string>
        {
            ["V2pn-Partner"] = " https://config.example.com/v2pn ",
            ["v2pn-account-token"] = "9f2c-AB_c.1~",
        });

        await info.AccountToken.Should().BeEqualTo("9f2c-AB_c.1~");
        await (SubPanelHeaders.ParseToken("a b") == null).Should().BeTrue();
        await (SubPanelHeaders.ParseToken(new string('a', 201)) == null).Should().BeTrue();
    }

    #region V2pnPartners

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "v2pn-partners-" + Guid.NewGuid().ToString("N"));

    private static void Delete(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    private const string Sub = "https://config.example.com/s/token";

    [Test]
    public async Task Refresh_TakesThePackOfASubscriptionsPartner_AndReadsItBackFromDisk()
    {
        var p = new TestPartner();
        var site = new TestSite();
        var zip = TestPartner.Zip();
        p.Publish(site, Published, (TestPartner.Listing(zip: zip), zip));
        var root = NewRoot();
        try
        {
            var manager = p.Manager(site, root, [Sub, "https://other.example.org/s/1"]);
            await manager.RefreshAsync();

            var partner = manager.Get("configra")!;
            await partner.HasEdition.Should().BeTrue();
            await File.Exists(partner.AssetPath("logo.png")!).Should().BeTrue();
            await manager.ForSubscription(new SubItem { Id = "s1", Url = Sub })!.Id.Should().BeEqualTo("configra");
            // The same pack never dresses a subscription from a domain its listing does not name.
            await (manager.ForSubscription(new SubItem { Id = "s2", Url = "https://other.example.org/s/1" }) == null).Should().BeTrue();

            var again = p.Manager(site, root, [Sub]);
            await again.LoadAsync();
            await (again.Get("configra") != null).Should().BeTrue();
            await again.ReadFolder(Path.Combine(root, "configra"), again.Index!.Get("configra")!, out var error)!.Id.Should().BeEqualTo("configra");
            await error.Should().BeEqualTo(string.Empty);
        }
        finally
        {
            Delete(root);
        }
    }

    /// <summary>Configra and Kestrel published side by side, each with its own domains.</summary>
    private static void PublishTwo(TestPartner p, TestSite site)
    {
        var configra = TestPartner.Zip();
        var kestrel = TestPartner.Zip(TestPartner.Pack(id: "kestrel", name: "Kestrel", api: "https://kestrel.example.net/v2pn/api/", more: pack => pack["login"] = true));
        p.Publish(site, Published,
            (TestPartner.Listing(zip: configra), configra),
            (TestPartner.Listing(partner: "kestrel", domains: ["kestrel.example.net"], zip: kestrel), kestrel));
    }

    private const string KestrelSub = "https://kestrel.example.net/s/token";

    [Test]
    public async Task Marker_TakesItsPartnersPackBeforeAnySubscription_OpensItsTab_AndNoOtherPartner()
    {
        var p = new TestPartner();
        var site = new TestSite();
        PublishTwo(p, site);
        var root = NewRoot();
        try
        {
            // A Kestrel subscription in a copy marked for Configra: Kestrel is not a partner here, just a subscription.
            var manager = p.Manager(site, root, [KestrelSub], edition: "configra");
            await manager.RefreshAsync();

            await manager.Edition.CameFrom.Should().BeEquivalentTo(["configra"]);
            await manager.Edition.Tab.Should().BeEqualTo("configra");
            await manager.All.Select(x => x.Id).ToList().Should().BeEquivalentTo(["configra"]);
            await site.Asked.Any(u => u.EndsWith("partner-kestrel.zip", StringComparison.Ordinal)).Should().BeFalse();
            await (manager.ForSubscription(new SubItem { Id = "k", Url = KestrelSub }) == null).Should().BeTrue();
            await (manager.ListingFor(KestrelSub) == null).Should().BeTrue();
            await manager.ForSubscription(new SubItem { Id = "c", Url = Sub })!.Id.Should().BeEqualTo("configra");
            await manager.ListingFor(Sub)!.Id.Should().BeEqualTo("configra");
            // No subscription of its own yet, and it stays: the marker says the person came from Configra.
            await manager.Prune([KestrelSub]).Count.Should().BeEqualTo(0);
            await (manager.Get("configra") != null).Should().BeTrue();
            await manager.Edition.CameFrom.Should().BeEquivalentTo(["configra"]);

            // A copy marked for Kestrel sees Kestrel only.
            var kestrel = p.Manager(site, NewRoot(), [Sub], edition: "kestrel");
            await kestrel.RefreshAsync();
            await kestrel.All.Select(x => x.Id).ToList().Should().BeEquivalentTo(["kestrel"]);
            await (kestrel.ForSubscription(new SubItem { Id = "c", Url = Sub }) == null).Should().BeTrue();
            await kestrel.ForSubscription(new SubItem { Id = "k", Url = KestrelSub })!.Id.Should().BeEqualTo("kestrel");
        }
        finally
        {
            Delete(root);
        }
    }

    [Test]
    public async Task FromNoPartner_NothingIsAskedFor_APastedPartnersLinkMakesNoTab_AndNoSubscriptionIsAPartners()
    {
        var p = new TestPartner();
        var site = new TestSite();
        PublishTwo(p, site);
        var root = NewRoot();
        try
        {
            // Configra's and Kestrel's links pasted by hand: subscriptions like any other.
            var manager = p.Manager(site, root, [Sub, KestrelSub], edition: (string?)null);
            await manager.RefreshAsync(forceIndex: true);
            await manager.PruneAsync();

            await site.Asked.Count.Should().BeEqualTo(0);
            await manager.Edition.Any.Should().BeFalse();
            await (manager.Edition.Tab == null).Should().BeTrue();
            await manager.All.Count.Should().BeEqualTo(0);
            await (manager.Index == null).Should().BeTrue();
            await (manager.ForSubscription(new SubItem { Id = "c", Url = Sub }) == null).Should().BeTrue();
            await (manager.ForSubscription(new SubItem { Id = "k", Url = KestrelSub }) == null).Should().BeTrue();
            await (manager.ListingFor(Sub) == null).Should().BeTrue();
            await Directory.Exists(root).Should().BeFalse();
            // No tab can be opened for a partner the person did not come from.
            await manager.Edition.Open("configra").Should().BeFalse();
            await (manager.Edition.Tab == null).Should().BeTrue();
        }
        finally
        {
            Delete(root);
        }
    }

    [Test]
    public async Task FromNoPartner_RemovesThePartnerFilesAnOlderVersionKept()
    {
        var p = new TestPartner();
        var site = new TestSite();
        PublishTwo(p, site);
        var root = NewRoot();
        try
        {
            // What 1.2.1 left: an index and a kept pack.
            var before = p.Manager(site, root, [Sub]);
            await before.RefreshAsync();
            await Directory.Exists(Path.Combine(root, "configra")).Should().BeTrue();

            var plain = p.Manager(site, root, [Sub], edition: (string?)null);
            await plain.LoadAsync();
            await Directory.Exists(root).Should().BeFalse();
            await plain.All.Count.Should().BeEqualTo(0);
            plain.SetDebugSubscription("c", "configra");
            await (plain.ForSubscription(new SubItem { Id = "c", Url = Sub }) == null).Should().BeTrue();
        }
        finally
        {
            Delete(root);
        }
    }

    [Test]
    public async Task SignIn_IsRecorded_BringsThePartnersPack_OpensItsTab_AndIsKeptAcrossRestarts()
    {
        var p = new TestPartner();
        var site = new TestSite();
        PublishTwo(p, site);
        var root = NewRoot();
        try
        {
            (List<string> SignedIn, string? Tab) saved = ([], null);
            V2pnEdition Edition() => new(null, saved.SignedIn, saved.Tab, e => saved = (e.SignedIn.ToList(), e.SavedTab));
            var subs = new List<string?>();
            var manager = p.Manager(site, root, subs, Edition());

            // The person opens «Sign in with a provider»: only now is the index asked for, and no partner has a tab yet.
            var offers = await manager.LoginOffersAsync();
            await offers.Select(o => o.Id).ToList().Should().BeEquivalentTo(["kestrel"]);
            await manager.All.Count.Should().BeEqualTo(0);
            await manager.Edition.Any.Should().BeFalse();

            // Kestrel confirms the code: its subscription is added, then the sign-in is recorded and its tab opened.
            subs.Add(KestrelSub);
            await manager.Edition.RecordSignIn("kestrel").Should().BeTrue();
            await manager.Edition.Open("kestrel").Should().BeTrue();
            await manager.RefreshAsync();

            await manager.Edition.CameFrom.Should().BeEquivalentTo(["kestrel"]);
            await manager.Edition.Tab.Should().BeEqualTo("kestrel");
            await manager.All.Select(x => x.Id).ToList().Should().BeEquivalentTo(["kestrel"]);
            await manager.ForSubscription(new SubItem { Id = "k", Url = KestrelSub })!.Id.Should().BeEqualTo("kestrel");
            await saved.SignedIn.Should().BeEquivalentTo(["kestrel"]);
            await saved.Tab.Should().BeEqualTo("kestrel");

            // The next start reads it back from the settings: the same tab, the same partner.
            var again = p.Manager(site, root, subs, Edition());
            await again.LoadAsync();
            await again.Edition.Tab.Should().BeEqualTo("kestrel");
            await (again.Get("kestrel") != null).Should().BeTrue();
        }
        finally
        {
            Delete(root);
        }
    }

    [Test]
    public async Task SignIn_IsForgottenWithItsLastSubscription_TheMarkerNever()
    {
        var p = new TestPartner();
        var site = new TestSite();
        PublishTwo(p, site);
        var root = NewRoot();
        try
        {
            var subs = new List<string?> { KestrelSub, "https://elsewhere.example.org/s/1" };
            var edition = new V2pnEdition("configra", ["kestrel"], "kestrel");
            var manager = p.Manager(site, root, subs, edition);
            await manager.RefreshAsync();
            await manager.All.Select(x => x.Id).ToList().Should().BeEquivalentTo(["configra", "kestrel"]);
            var gone = new List<(string, V2pnPartnerGone)>();
            manager.Removed += (partner, why) => gone.Add((partner.Id, why));

            // A refresh never forgets: its links may be older than a sign-in adding its subscription right now.
            subs.Remove(KestrelSub);
            await manager.RefreshAsync(forceIndex: true);
            await edition.IsFrom("kestrel").Should().BeTrue();

            // The person deleted Kestrel's last subscription: the sign-in, the pack and the tab go; V2pn's tab opens.
            await manager.PruneAsync();
            await edition.CameFrom.Should().BeEquivalentTo(["configra"]);
            await (manager.Get("kestrel") == null).Should().BeTrue();
            await gone.Should().BeEquivalentTo(new[] { ("kestrel", V2pnPartnerGone.Unlinked) });
            await (edition.Tab == null).Should().BeTrue();
            await Directory.Exists(Path.Combine(root, "kestrel")).Should().BeFalse();

            // No subscription from Configra either, but the marker says the person came from it: its tab stays.
            await (manager.Get("configra") != null).Should().BeTrue();
            await edition.Open("configra").Should().BeTrue();
        }
        finally
        {
            Delete(root);
        }
    }

    [Test]
    public async Task PartnerFeatures_ApplyOnlyInThatPartnersTab_AndOnlyToItsSubscriptions()
    {
        var p = new TestPartner();
        var site = new TestSite();
        var configra = TestPartner.Zip();
        var kestrel = TestPartner.Zip(TestPartner.Pack(id: "kestrel", name: "Kestrel", more: pack => pack["lock"] = true));
        p.Publish(site, Published,
            (TestPartner.Listing(zip: configra, grants: ["edition", "account", "lock"]), configra),
            (TestPartner.Listing(partner: "kestrel", domains: ["kestrel.example.net"], grants: ["edition", "account", "lock"], zip: kestrel), kestrel));
        var root = NewRoot();
        try
        {
            var edition = new V2pnEdition("configra", ["kestrel"]);
            var manager = p.Manager(site, root, [Sub, KestrelSub], edition);
            await manager.RefreshAsync();
            var configraSub = new SubItem { Id = "c", Url = Sub };
            var kestrelSub = new SubItem { Id = "k", Url = KestrelSub };
            var changes = 0;
            manager.Changed += () => changes++;

            // Before any choice the marker's tab is open: Configra's subscription is Configra's, Kestrel's is not.
            await edition.Tab.Should().BeEqualTo("configra");
            await manager.ForSubscription(configraSub)!.Id.Should().BeEqualTo("configra");
            await (manager.ForSubscription(kestrelSub) == null).Should().BeTrue();

            // Kestrel's tab: Kestrel's subscription is its, with its lock; Configra's is just a subscription.
            await edition.Open("kestrel").Should().BeTrue();
            await (changes > 0).Should().BeTrue();
            await (manager.ForSubscription(configraSub) == null).Should().BeTrue();
            await (manager.ListingFor(Sub) == null).Should().BeTrue();
            await V2pnLockState.For(manager.ForSubscription(kestrelSub), null).Settings.Should().BeTrue();

            // V2pn's tab: no partner at all, no lock.
            await edition.Open(null).Should().BeTrue();
            await (manager.ForSubscription(configraSub) == null).Should().BeTrue();
            await (manager.ForSubscription(kestrelSub) == null).Should().BeTrue();
            await (manager.ListingFor(KestrelSub) == null).Should().BeTrue();
            await V2pnLockState.For(manager.ForSubscription(kestrelSub), null).Any.Should().BeFalse();
            // The partners stay kept: their tabs are there to go back to.
            await manager.All.Count.Should().BeEqualTo(2);
        }
        finally
        {
            Delete(root);
        }
    }

    [Test]
    public async Task APartnerLeavingTheIndex_LosesItsTabsPartner()
    {
        var p = new TestPartner();
        var site = new TestSite();
        PublishTwo(p, site);
        var root = NewRoot();
        try
        {
            var manager = p.Manager(site, root, [Sub], edition: "configra");
            await manager.RefreshAsync();
            var gone = new List<(string, V2pnPartnerGone)>();
            manager.Removed += (partner, why) => gone.Add((partner.Id, why));

            p.Publish(site, Published.AddDays(1));
            await manager.RefreshAsync(forceIndex: true);

            await gone.Should().BeEquivalentTo(new[] { ("configra", V2pnPartnerGone.Revoked) });
            await (manager.ForSubscription(new SubItem { Id = "c", Url = Sub }) == null).Should().BeTrue();
            // The marker stays: a partner listed again gets its tab back.
            await manager.Edition.IsFrom("configra").Should().BeTrue();
        }
        finally
        {
            Delete(root);
        }
    }

    [Test]
    public async Task Marker_OfAnUnknownPartner_TakesNothing()
    {
        var p = new TestPartner();
        var site = new TestSite();
        PublishTwo(p, site);
        var root = NewRoot();
        try
        {
            var manager = p.Manager(site, root, [Sub, KestrelSub], edition: "nobody");
            await manager.RefreshAsync();

            await manager.All.Count.Should().BeEqualTo(0);
            await site.Asked.Any(u => u.EndsWith(".zip", StringComparison.Ordinal)).Should().BeFalse();
            await (manager.ForSubscription(new SubItem { Id = "c", Url = Sub }) == null).Should().BeTrue();
            await (await manager.LoginOffersAsync()).Any(o => o.Id == "nobody").Should().BeFalse();
        }
        finally
        {
            Delete(root);
        }
    }

    [Test]
    public async Task Partners_ReadTheNewIndexName_NeverTheOneOlderCopiesRead()
    {
        var p = new TestPartner();
        var site = new TestSite();
        PublishTwo(p, site);
        var root = NewRoot();
        try
        {
            var manager = p.Manager(site, root, [Sub]);
            await manager.RefreshAsync();

            await V2pnPartnerTrust.IndexFile.Should().BeEqualTo("partner-index.json");
            await V2pnPartnerTrust.LegacyIndexFile.Should().BeEqualTo("partners.json");
            await site.Asked.Should().Contain($"{TestSite.Source}/latest/download/partner-index.json");
            await site.Asked.Any(u => u.EndsWith("/partners.json", StringComparison.Ordinal)).Should().BeFalse();
            await File.Exists(Path.Combine(root, "partner-index.json")).Should().BeTrue();

            // Only the empty legacy index on the release: a person who came from a partner finds none there.
            var old = new TestSite();
            p.Publish(old, Published);
            old.Files.Remove($"{TestSite.Source}/latest/download/{V2pnPartnerTrust.IndexFile}");
            old.Files.Remove($"{TestSite.Source}/latest/download/{V2pnPartnerTrust.IndexFile}.sig");
            var fresh = p.Manager(old, NewRoot(), [Sub]);
            await fresh.RefreshAsync();
            await (fresh.Index == null).Should().BeTrue();
        }
        finally
        {
            Delete(root);
        }
    }

    [Test]
    public async Task Refresh_AZipThatDoesNotMatch_KeepsTheLastGoodPack()
    {
        var p = new TestPartner();
        var site = new TestSite();
        var zip = TestPartner.Zip();
        p.Publish(site, Published, (TestPartner.Listing(zip: zip), zip));
        var root = NewRoot();
        try
        {
            var manager = p.Manager(site, root, [Sub]);
            await manager.RefreshAsync();
            await manager.Get("configra")!.Pack.Accent.Should().BeEqualTo("#FF7A2F");

            // A new pack in the index, but the release serves other bytes under its name.
            var next = TestPartner.Zip(TestPartner.Pack(accent: "#123456"));
            p.Publish(site, Published.AddDays(1), (TestPartner.Listing(zip: next), next));
            site.Put("partner-configra.zip", TestPartner.Zip(TestPartner.Pack(accent: "#654321")));
            await manager.RefreshAsync(forceIndex: true);

            await manager.Get("configra")!.Pack.Accent.Should().BeEqualTo("#FF7A2F");
            await manager.ErrorFor("configra")!.Contains("not the one").Should().BeTrue();
            await File.Exists(Path.Combine(root, "configra", "logo.png")).Should().BeTrue();

            // Once the right zip is there, the new pack replaces the old one.
            site.Put("partner-configra.zip", next);
            await manager.RefreshAsync();
            await manager.Get("configra")!.Pack.Accent.Should().BeEqualTo("#123456");
            await (manager.ErrorFor("configra") == null).Should().BeTrue();
        }
        finally
        {
            Delete(root);
        }
    }

    [Test]
    public async Task Refresh_ABadOrOlderIndex_KeepsTheLastGoodOne()
    {
        var p = new TestPartner();
        var site = new TestSite();
        var zip = TestPartner.Zip();
        p.Publish(site, Published, (TestPartner.Listing(zip: zip), zip));
        var root = NewRoot();
        try
        {
            var manager = p.Manager(site, root, [Sub]);
            await manager.RefreshAsync();

            // Tampered: the partner stays.
            site.Put(V2pnPartnerTrust.IndexFile, V2pnPartnerPublisher.IndexBytes([], Published.AddDays(1)));
            await manager.RefreshAsync(forceIndex: true);
            await (manager.Get("configra") != null).Should().BeTrue();
            await manager.IndexError!.Contains("not signed").Should().BeTrue();

            // Signed, but older than the kept one: a mirror that lags, or an old index served again.
            p.Publish(site, Published.AddDays(-1));
            await manager.RefreshAsync(forceIndex: true);
            await (manager.Get("configra") != null).Should().BeTrue();
            await manager.IndexError!.Contains("older").Should().BeTrue();
        }
        finally
        {
            Delete(root);
        }
    }

    [Test]
    public async Task Refresh_AsksForTheIndexAtStart_ThenAtMostEverySixHours()
    {
        var p = new TestPartner();
        var site = new TestSite();
        p.Publish(site, Published);
        var root = NewRoot();
        try
        {
            var now = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);
            var manager = p.Manager(site, root, [Sub]);
            manager.Now = () => now;
            int Asks() => site.Asked.Count(u => u.EndsWith("/" + V2pnPartnerTrust.IndexFile, StringComparison.Ordinal));

            await manager.RefreshAsync();
            await Asks().Should().BeEqualTo(1);
            now = now.AddHours(5);
            await manager.RefreshAsync();
            await Asks().Should().BeEqualTo(1);
            await manager.RefreshAsync(forceIndex: true);
            await Asks().Should().BeEqualTo(2);
            now = now.AddHours(6);
            await manager.RefreshAsync();
            await Asks().Should().BeEqualTo(3);
        }
        finally
        {
            Delete(root);
        }
    }

    [Test]
    public async Task APartnerThatLeavesTheIndex_IsRevoked_WithItsFiles()
    {
        var p = new TestPartner();
        var site = new TestSite();
        var zip = TestPartner.Zip();
        p.Publish(site, Published, (TestPartner.Listing(zip: zip), zip));
        var root = NewRoot();
        try
        {
            var manager = p.Manager(site, root, [Sub]);
            await manager.RefreshAsync();
            var removed = new List<(string, V2pnPartnerGone)>();
            manager.Removed += (partner, why) => removed.Add((partner.Id, why));

            p.Publish(site, Published.AddDays(1));
            await manager.RefreshAsync(forceIndex: true);

            await (manager.Get("configra") == null).Should().BeTrue();
            await Directory.Exists(Path.Combine(root, "configra")).Should().BeFalse();
            await removed.Count.Should().BeEqualTo(1);
            await removed[0].Item2.Should().BeEqualTo(V2pnPartnerGone.Revoked);
        }
        finally
        {
            Delete(root);
        }
    }

    [Test]
    public async Task APartnerPastItsTerm_Expires_ButOneWithoutASubscriptionStays()
    {
        var p = new TestPartner();
        var site = new TestSite();
        var zip = TestPartner.Zip();
        p.Publish(site, Published, (TestPartner.Listing(zip: zip, until: "2026-12-01"), zip));
        var root = NewRoot();
        try
        {
            var subs = new List<string?> { Sub };
            var manager = p.Manager(site, root, subs);
            await manager.RefreshAsync();
            V2pnPartnerGone? why = null;
            manager.Removed += (_, reason) => why = reason;

            // The subscription moved to a domain the listing does not name: the build keeps its partner.
            await manager.Prune(["https://other.example.org/s/token"]).Count.Should().BeEqualTo(0);
            await (why == null).Should().BeTrue();
            await (manager.Get("configra") != null).Should().BeTrue();
            manager.Today = () => new DateOnly(2026, 12, 2);
            await manager.Prune(subs).Count.Should().BeEqualTo(1);
            await why.Should().BeEqualTo(V2pnPartnerGone.Expired);
            // And a run-out partner is not downloaded again.
            await manager.RefreshAsync();
            await (manager.Get("configra") == null).Should().BeTrue();
        }
        finally
        {
            Delete(root);
        }
    }

    [Test]
    public async Task ANewListing_IsTakenWithoutDownloadingTheSameZipAgain()
    {
        var p = new TestPartner();
        var site = new TestSite();
        var zip = TestPartner.Zip();
        p.Publish(site, Published, (TestPartner.Listing(zip: zip, grants: ["edition"]), zip));
        var root = NewRoot();
        try
        {
            var manager = p.Manager(site, root, [Sub]);
            await manager.RefreshAsync();
            await (manager.Get("configra")!.Pack.ApiUrl == null).Should().BeTrue();

            p.Publish(site, Published.AddDays(1), (TestPartner.Listing(zip: zip, grants: ["edition", "account"]), zip));
            await manager.RefreshAsync(forceIndex: true);

            await manager.Get("configra")!.Pack.ApiUrl.Should().BeEqualTo("https://config.example.com/v2pn/api/");
            await site.Asked.Count(u => u.EndsWith(".zip", StringComparison.Ordinal)).Should().BeEqualTo(1);
        }
        finally
        {
            Delete(root);
        }
    }

    [Test]
    public async Task KeptPartner_ChangedOnDisk_IsNoLongerTrusted()
    {
        var p = new TestPartner();
        var site = new TestSite();
        var zip = TestPartner.Zip();
        p.Publish(site, Published, (TestPartner.Listing(zip: zip), zip));
        var root = NewRoot();
        try
        {
            var manager = p.Manager(site, root, [Sub]);
            await manager.RefreshAsync();
            File.WriteAllBytes(Path.Combine(root, "configra", "logo.png"), [9, 9, 9]);

            var again = p.Manager(site, root, [Sub]);
            await again.LoadAsync();
            await (again.Get("configra") == null).Should().BeTrue();
            await (again.ReadFolder(Path.Combine(root, "configra"), again.Index!.Get("configra")!, out var error) == null).Should().BeTrue();
            await error.Contains("changed").Should().BeTrue();

            // The kept index changed on disk: nothing kept is trusted.
            File.WriteAllText(Path.Combine(root, V2pnPartnerTrust.IndexFile), "{}");
            var third = p.Manager(site, root, [Sub]);
            await third.LoadAsync();
            await (third.Index == null).Should().BeTrue();
        }
        finally
        {
            Delete(root);
        }
    }

    #endregion V2pnPartners
}
