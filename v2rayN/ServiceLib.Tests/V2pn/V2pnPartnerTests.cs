namespace ServiceLib.Tests.V2pn;

/// <summary>A root key and a partner key, and the cert.json and partner.json they sign, as tools/partner.cs writes them.</summary>
internal sealed class TestPartner
{
    public readonly ECDsa Root = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public static readonly DateOnly Today = new(2026, 11, 15);
    public static readonly byte[] Logo = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    public string RootPublic => Convert.ToBase64String(Root.ExportSubjectPublicKeyInfo());
    public string KeyPublic => Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo());

    public byte[] Cert(string partner = "configra", string[]? domains = null, string[]? grants = null, string issued = "2026-11-01", string expires = "2027-11-01") =>
        Encoding.UTF8.GetBytes(new JsonObject
        {
            ["schema"] = 1,
            ["partner"] = partner,
            ["name"] = "Configra",
            ["domains"] = new JsonArray((domains ?? ["config.example.com", "*.sub.example.com"]).Select(d => (JsonNode)d).ToArray()),
            ["key"] = KeyPublic,
            ["grants"] = new JsonArray((grants ?? ["edition", "account"]).Select(g => (JsonNode)g).ToArray()),
            ["issued"] = issued,
            ["expires"] = expires,
        }.ToJsonString());

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
            ["edition"] = new JsonObject
            {
                ["variants"] = new JsonArray((variants ?? ["signature", "warm", "cabinet", "studio", "ascii", "bogus"]).Select(v => (JsonNode)v).ToArray()),
                ["default"] = first,
                ["art"] = new JsonObject { ["lamp"] = "logo.png", ["missing"] = "nope.png" },
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

    public static string Sign(ECDsa key, byte[] data) =>
        Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

    public V2pnPartnerCert ReadCert(byte[]? cert = null) =>
        V2pnPartnerTrust.ReadCert(cert ??= Cert(), Sign(Root, cert), [RootPublic], Today, out _)!;
}

public class V2pnPartnerTests
{
    [Test]
    public async Task Cert_SignedByRoot_IsRead()
    {
        var p = new TestPartner();
        var json = p.Cert();

        var cert = V2pnPartnerTrust.ReadCert(json, TestPartner.Sign(p.Root, json), [p.RootPublic], TestPartner.Today, out var error);

        await error.Should().BeEqualTo(string.Empty);
        await cert!.Partner.Should().BeEqualTo("configra");
        await cert.Allows(V2pnPartnerGrants.Edition).Should().BeTrue();
        await cert.Domains.Count.Should().BeEqualTo(2);
    }

    [Test]
    public async Task Cert_SignedByAnotherKey_OrChanged_IsRefused()
    {
        var p = new TestPartner();
        var json = p.Cert();
        var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        await (V2pnPartnerTrust.ReadCert(json, TestPartner.Sign(stranger, json), [p.RootPublic], TestPartner.Today, out var e1) == null).Should().BeTrue();
        await e1.Contains("not signed").Should().BeTrue();

        // The partner's own key cannot vouch for itself.
        await (V2pnPartnerTrust.ReadCert(json, TestPartner.Sign(p.Key, json), [p.RootPublic], TestPartner.Today, out _) == null).Should().BeTrue();

        var signature = TestPartner.Sign(p.Root, json);
        var widened = p.Cert(domains: ["config.example.com", "*.evil.example"]);
        await (V2pnPartnerTrust.ReadCert(widened, signature, [p.RootPublic], TestPartner.Today, out _) == null).Should().BeTrue();
    }

    [Test]
    public async Task Cert_OutsideItsTerm_IsRefused()
    {
        var p = new TestPartner();
        var expired = p.Cert(issued: "2025-01-01", expires: "2026-01-01");
        var early = p.Cert(issued: "2026-12-01", expires: "2027-12-01");

        await (V2pnPartnerTrust.ReadCert(expired, TestPartner.Sign(p.Root, expired), [p.RootPublic], TestPartner.Today, out var error) == null).Should().BeTrue();
        await error.Contains("valid from").Should().BeTrue();
        await (V2pnPartnerTrust.ReadCert(early, TestPartner.Sign(p.Root, early), [p.RootPublic], TestPartner.Today, out _) == null).Should().BeTrue();
    }

    [Test]
    public async Task Cert_WithATopLevelWildcardOrBadId_IsMalformed()
    {
        var p = new TestPartner();
        foreach (var json in new[] { p.Cert(domains: ["*.com"]), p.Cert(partner: "Bad/Id") })
        {
            await (V2pnPartnerTrust.ReadCert(json, TestPartner.Sign(p.Root, json), [p.RootPublic], TestPartner.Today, out var error) == null).Should().BeTrue();
            await error.Contains("malformed").Should().BeTrue();
        }
    }

    [Test]
    public async Task Cert_WithAGrantThisV2pnDoesNotKnow_IsReadWithoutIt()
    {
        var p = new TestPartner();
        var cert = p.ReadCert(p.Cert(grants: ["edition", "routing-v9", "actions", "lock", "actions"]));

        await cert.Grants.Should().BeEquivalentTo(new[] { "edition", "actions", "lock" });
        await cert.Allows("routing-v9").Should().BeFalse();
    }

    [Test]
    public async Task Domains_MatchExactlyOrUnderAWildcard()
    {
        var cert = new TestPartner().ReadCert();

        await V2pnPartnerTrust.DomainAllowed(cert, "https://config.example.com/s/abc").Should().BeTrue();
        await V2pnPartnerTrust.DomainAllowed(cert, "https://a.sub.example.com/x").Should().BeTrue();
        await V2pnPartnerTrust.DomainAllowed(cert, "https://CONFIG.example.com./s").Should().BeTrue();
        await V2pnPartnerTrust.DomainAllowed(cert, "https://sub.example.com/x").Should().BeFalse();
        await V2pnPartnerTrust.DomainAllowed(cert, "https://evilconfig.example.com/").Should().BeFalse();
        await V2pnPartnerTrust.DomainAllowed(cert, "https://config.example.com.evil.net/").Should().BeFalse();
        await V2pnPartnerTrust.DomainAllowed(cert, "not a url").Should().BeFalse();
    }

    [Test]
    public async Task Pack_SignedByTheCertifiedKey_IsRead_AndKeepsOnlyWhatItMayUse()
    {
        var p = new TestPartner();
        var cert = p.ReadCert();
        var json = TestPartner.Pack();

        var pack = V2pnPartnerTrust.ReadPack(json, TestPartner.Sign(p.Key, json), cert, out var error);

        await error.Should().BeEqualTo(string.Empty);
        await pack!.Accent.Should().BeEqualTo("#FF7A2F");
        await pack.Variants.Count.Should().BeEqualTo(5);
        await pack.DefaultVariant.Should().BeEqualTo(V2pnPartnerVariants.Cabinet);
        await pack.Logo.Should().BeEqualTo("logo.png");
        await pack.Art.ContainsKey("lamp").Should().BeTrue();
        await pack.Art.ContainsKey("missing").Should().BeFalse();
        await pack.CabinetUrl.Should().BeEqualTo("https://config.example.com/cabinet");
        await (pack.SupportUrl == null).Should().BeTrue();
        await pack.ApiUrl.Should().BeEqualTo("https://config.example.com/v2pn/api/");
    }

    [Test]
    public async Task Pack_SignedByAnotherKey_OrForAnotherPartner_IsRefused()
    {
        var p = new TestPartner();
        var cert = p.ReadCert();
        var json = TestPartner.Pack();

        await (V2pnPartnerTrust.ReadPack(json, TestPartner.Sign(p.Root, json), cert, out var e1) == null).Should().BeTrue();
        await e1.Contains("partner's key").Should().BeTrue();

        var other = TestPartner.Pack(id: "someone-else");
        await (V2pnPartnerTrust.ReadPack(other, TestPartner.Sign(p.Key, other), cert, out var e2) == null).Should().BeTrue();
        await e2.Contains("another partner").Should().BeTrue();
    }

    [Test]
    public async Task Pack_WithoutTheGrants_LosesItsEditionAndApi()
    {
        var p = new TestPartner();
        var cert = p.ReadCert(p.Cert(grants: []));
        var json = TestPartner.Pack();

        var pack = V2pnPartnerTrust.ReadPack(json, TestPartner.Sign(p.Key, json), cert, out _)!;

        await pack.Variants.Count.Should().BeEqualTo(0);
        await (pack.ApiUrl == null).Should().BeTrue();
        await pack.Name.Should().BeEqualTo("Configra");
    }

    [Test]
    public async Task Pack_ApiOnAForeignDomain_IsDropped_AndAMissingDefaultFallsBack()
    {
        var p = new TestPartner();
        var cert = p.ReadCert();
        var json = TestPartner.Pack(api: "https://tracker.example.net/", variants: ["warm", "studio"], first: "ascii");

        var pack = V2pnPartnerTrust.ReadPack(json, TestPartner.Sign(p.Key, json), cert, out _)!;

        await (pack.ApiUrl == null).Should().BeTrue();
        await pack.DefaultVariant.Should().BeEqualTo(V2pnPartnerVariants.Warm);
    }

    [Test]
    public async Task Pack_WithAPathOrABadColour_IsMalformed()
    {
        var p = new TestPartner();
        var cert = p.ReadCert();
        foreach (var json in new[]
                 {
                     TestPartner.Pack(assets: [("../evil.png", TestPartner.Logo)]),
                     TestPartner.Pack(assets: [("script.svg", TestPartner.Logo)]),
                     TestPartner.Pack(accent: "orange"),
                 })
        {
            await (V2pnPartnerTrust.ReadPack(json, TestPartner.Sign(p.Key, json), cert, out var error) == null).Should().BeTrue();
            await error.Contains("malformed").Should().BeTrue();
        }
    }

    [Test]
    public async Task Pack_ReadsTheSquareMark_AndRefusesANameThatPassesForV2pn()
    {
        var p = new TestPartner();
        var cert = p.ReadCert();
        var json = TestPartner.Pack();
        await V2pnPartnerTrust.ReadPack(json, TestPartner.Sign(p.Key, json), cert, out _)!.Mark.Should().BeEqualTo("logo.png");

        foreach (var name in new[] { "V2pn Pro", "Super v2 PN", "v2pn" })
        {
            var fake = TestPartner.Pack(name: name);
            await (V2pnPartnerTrust.ReadPack(fake, TestPartner.Sign(p.Key, fake), cert, out _) == null).Should().BeTrue();
        }
    }

    [Test]
    public async Task Revocations_AreReadOnlyWhenSignedByRoot()
    {
        var p = new TestPartner();
        var json = Encoding.UTF8.GetBytes("""{"revoked":["configra@2026-11-01"]}""");

        var revoked = V2pnPartnerTrust.ReadRevoked(json, TestPartner.Sign(p.Root, json), [p.RootPublic])!;

        await revoked.Contains(V2pnPartnerTrust.RevocationKey(p.ReadCert())).Should().BeTrue();
        await (V2pnPartnerTrust.ReadRevoked(json, TestPartner.Sign(p.Key, json), [p.RootPublic]) == null).Should().BeTrue();
    }

    [Test]
    public async Task Headers_PartnerFolderAndToken_AreRead()
    {
        var info = SubPanelHeaders.Parse(new Dictionary<string, string>
        {
            ["V2pn-Partner"] = " https://config.example.com/v2pn ",
            ["v2pn-account-token"] = "9f2c-AB_c.1~",
        });

        await info.PartnerUrl.Should().BeEqualTo("https://config.example.com/v2pn/");
        await info.AccountToken.Should().BeEqualTo("9f2c-AB_c.1~");
        await (SubPanelHeaders.ParsePartnerUrl("http://config.example.com/v2pn/") == null).Should().BeTrue();
        await (SubPanelHeaders.ParsePartnerUrl("https://user:pw@config.example.com/v2pn/") == null).Should().BeTrue();
        await (SubPanelHeaders.ParsePartnerUrl("https://config.example.com/v2pn/?x=1") == null).Should().BeTrue();
        await (SubPanelHeaders.ParseToken("a b") == null).Should().BeTrue();
        await (SubPanelHeaders.ParseToken(new string('a', 201)) == null).Should().BeTrue();
    }

    private sealed class Site
    {
        public readonly Dictionary<string, byte[]> Files = new(StringComparer.Ordinal);

        public Task<V2pnHttp.Result> FetchAsync(string url, CancellationToken _) =>
            Task.FromResult(Files.TryGetValue(url, out var body)
                ? new V2pnHttp.Result(HttpStatusCode.OK, body, null)
                : new V2pnHttp.Result(HttpStatusCode.NotFound, null, "HTTP 404"));
    }

    private static (TestPartner Partner, Site Site, V2pnPartners Manager, string Root) Setup()
    {
        var p = new TestPartner();
        var site = new Site();
        const string folder = "https://config.example.com/v2pn/";
        var cert = p.Cert();
        var pack = TestPartner.Pack();
        site.Files[folder + "cert.json"] = cert;
        site.Files[folder + "cert.json.sig"] = Encoding.UTF8.GetBytes(TestPartner.Sign(p.Root, cert));
        site.Files[folder + "partner.json"] = pack;
        site.Files[folder + "partner.json.sig"] = Encoding.UTF8.GetBytes(TestPartner.Sign(p.Key, pack));
        site.Files[folder + "logo.png"] = TestPartner.Logo;
        var root = Path.Combine(Path.GetTempPath(), "v2pn-partners-" + Guid.NewGuid().ToString("N"));
        var manager = new V2pnPartners([p.RootPublic], () => root) { Fetch = site.FetchAsync, Today = () => TestPartner.Today };
        return (p, site, manager, root);
    }

    [Test]
    public async Task Refresh_KeepsAVerifiedPartner_AndReadsItBackFromDisk()
    {
        var (p, _, manager, root) = Setup();
        try
        {
            var partner = await manager.RefreshAsync("https://config.example.com/s/token", "https://config.example.com/v2pn/");

            await partner!.Id.Should().BeEqualTo("configra");
            await partner.HasEdition.Should().BeTrue();
            await File.Exists(partner.AssetPath("logo.png")!).Should().BeTrue();
            await (manager.ForSource("https://config.example.com/v2pn/", "https://config.example.com/s/token") != null).Should().BeTrue();
            // The same pack never dresses a subscription from a domain its certificate does not name.
            await (manager.ForSource("https://config.example.com/v2pn/", "https://other.example.org/s/token") == null).Should().BeTrue();

            var again = new V2pnPartners([p.RootPublic], () => root) { Today = () => TestPartner.Today };
            await (again.ReadFolder(Path.Combine(root, "configra"), out var error) != null).Should().BeTrue();
            await error.Should().BeEqualTo(string.Empty);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task Refresh_RefusesAChangedPicture_AndAForeignSubscription()
    {
        var (_, site, manager, root) = Setup();
        try
        {
            await (await manager.RefreshAsync("https://elsewhere.example.org/s/token", "https://config.example.com/v2pn/") == null).Should().BeTrue();
            await manager.ErrorFor("https://config.example.com/v2pn/")!.Contains("domain").Should().BeTrue();

            site.Files["https://config.example.com/v2pn/logo.png"] = [1, 2, 3];
            await (await manager.RefreshAsync("https://config.example.com/s/token", "https://config.example.com/v2pn/") == null).Should().BeTrue();
            await manager.ErrorFor("https://config.example.com/v2pn/")!.Contains("logo.png").Should().BeTrue();
            await Directory.Exists(Path.Combine(root, "configra")).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Test]
    public async Task Prune_DropsAPartnerNoSubscriptionNames_WithItsFiles()
    {
        var (_, _, manager, root) = Setup();
        try
        {
            const string folder = "https://config.example.com/v2pn/";
            const string sub = "https://config.example.com/s/token";
            await manager.RefreshAsync(sub, folder);
            var removed = new List<(string, V2pnPartnerGone)>();
            manager.Removed += (partner, why) => removed.Add((partner.Id, why));

            // Still named by its subscription: kept.
            await manager.Prune([(folder, sub), (null, "https://other.example.org/s/1")]).Count.Should().BeEqualTo(0);
            // The subscription moved to a domain the certificate does not name: as good as gone.
            await manager.Prune([(folder, "https://other.example.org/s/token")]).Count.Should().BeEqualTo(1);

            await (manager.Get("configra") == null).Should().BeTrue();
            await Directory.Exists(Path.Combine(root, "configra")).Should().BeFalse();
            await removed.Count.Should().BeEqualTo(1);
            await removed[0].Item2.Should().BeEqualTo(V2pnPartnerGone.Unlinked);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Test]
    public async Task Prune_DropsAPartnerWhoseCertificateRanOutWhileOpen()
    {
        var (_, _, manager, root) = Setup();
        try
        {
            const string folder = "https://config.example.com/v2pn/";
            const string sub = "https://config.example.com/s/token";
            var partner = await manager.RefreshAsync(sub, folder);
            V2pnPartnerGone? why = null;
            manager.Removed += (_, reason) => why = reason;

            manager.Today = () => partner!.Cert.Expires.AddDays(1);
            await manager.Prune([(folder, sub)]).Count.Should().BeEqualTo(1);
            await why.Should().BeEqualTo(V2pnPartnerGone.Expired);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Test]
    public async Task KeptPartner_ChangedOnDisk_IsNoLongerTrusted()
    {
        var (p, _, manager, root) = Setup();
        try
        {
            await manager.RefreshAsync("https://config.example.com/s/token", "https://config.example.com/v2pn/");
            File.WriteAllBytes(Path.Combine(root, "configra", "logo.png"), [9, 9, 9]);

            var again = new V2pnPartners([p.RootPublic], () => root) { Today = () => TestPartner.Today };
            await (again.ReadFolder(Path.Combine(root, "configra"), out var error) == null).Should().BeTrue();
            await error.Contains("changed").Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
