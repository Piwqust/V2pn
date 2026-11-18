namespace ServiceLib.Tests.V2pn;

/// <summary>Several releases pages at once: the V2pn Hub's and the GitHub release's, &lt;source&gt;/latest/download/&lt;file&gt;.</summary>
internal sealed class TestSites
{
    public const string Hub = "https://hub.example.org/partners";
    public const string GitHub = "https://github.example.org/v2pn/releases";
    public readonly Dictionary<string, byte[]> Files = new(StringComparer.Ordinal);
    public readonly ConcurrentQueue<string> Asked = new();

    public void Put(string source, string name, byte[] body) => Files[$"{source}/latest/download/{name}"] = body;

    public void Clear(string source)
    {
        foreach (var key in Files.Keys.Where(k => k.StartsWith(source + "/", StringComparison.Ordinal)).ToList())
        {
            Files.Remove(key);
        }
    }

    /// <summary>partner-index.json(.sig) signed with <paramref name="key"/> and every zip, on <paramref name="source"/>.</summary>
    public void Publish(string source, ECDsa key, DateTimeOffset published, params (V2pnPartnerListing Listing, byte[] Zip)[] partners)
    {
        var index = V2pnPartnerPublisher.IndexBytes(partners.Select(p => p.Listing), published);
        Put(source, V2pnPartnerTrust.IndexFile, index);
        Put(source, V2pnPartnerTrust.IndexFile + ".sig", Encoding.UTF8.GetBytes(TestPartner.Sign(key, index)));
        foreach (var (listing, zip) in partners)
        {
            Put(source, listing.Zip.File, zip);
        }
    }

    public Task<V2pnHttp.Result> FetchAsync(string url, CancellationToken _)
    {
        Asked.Enqueue(url);
        return Task.FromResult(Files.TryGetValue(url, out var body)
            ? new V2pnHttp.Result(HttpStatusCode.OK, body, null)
            : new V2pnHttp.Result(HttpStatusCode.NotFound, null, "HTTP 404"));
    }
}

/// <summary>
/// The V2pn Hub (hub/) signs the partner index on V2pn's server with its own key; the owner still signs with the root key
/// on his computer. A hub-signed index never grants what is reviewed by hand; V2pn asks the hub and the GitHub release
/// and goes by the hub's index, the root-signed one adding only the partners the hub does not list. A provider's link
/// (v2pn://add?…&amp;partner=&lt;id&gt;) makes the person come from it only when the subscription is on its domains.
/// </summary>
public class V2pnPartnerHubTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);
    private const string Sub = "https://a.sub.example.com/s/abc";

    private readonly TestPartner _owner = new();
    private readonly ECDsa _hub = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private string HubPublic => Convert.ToBase64String(_hub.ExportSubjectPublicKeyInfo());

    private static (V2pnPartnerListing Listing, byte[] Zip) Partner(string id = "configra", string accent = "#ff7a2f", string[]? grants = null,
        string[]? domains = null, string until = "2027-11-01")
    {
        var zip = TestPartner.Zip(TestPartner.Pack(id: id, accent: accent));
        return (TestPartner.Listing(id, domains, grants, until, zip: zip), zip);
    }

    private V2pnPartners Manager(TestSites sites, string root, V2pnEdition edition, params string[] sources) =>
        new([_owner.RootPublic], () => root, edition, [HubPublic])
        {
            Fetch = sites.FetchAsync,
            Sources = sources.Length > 0 ? sources : [TestSites.Hub, TestSites.GitHub],
            SubscriptionUrls = () => Task.FromResult<IReadOnlyList<string?>>([Sub]),
            Today = () => TestPartner.Today,
        };

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "v2pn-hub-" + Guid.NewGuid().ToString("N"));

    private static void Delete(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    private (byte[] Json, string Sig) Signed(ECDsa key, params V2pnPartnerListing[] listings)
    {
        var json = V2pnPartnerPublisher.IndexBytes(listings, T);
        return (json, TestPartner.Sign(key, json));
    }

    private static readonly string RoutingHash = new('a', 64);

    #region Keys

    [Test]
    public async Task HubSignedIndex_IsTaken_WithItsPlainGrants()
    {
        var listing = TestPartner.Listing(grants: ["edition", "account", "actions", "lock"]);
        var (json, sig) = Signed(_hub, listing);

        var index = V2pnPartnerTrust.ReadIndex(json, sig, [_owner.RootPublic], [HubPublic], out var error);

        await error.Should().BeEqualTo(string.Empty);
        await index!.Signer.Should().BeEqualTo(V2pnPartnerSigner.Hub);
        await index.Published.Should().BeEqualTo(T);
        await string.Join(",", index.Get("configra")!.Grants).Should().BeEqualTo("edition,account,actions,lock");
        await index.Get("configra")!.Signer.Should().BeEqualTo(V2pnPartnerSigner.Hub);
        await index.Problems.Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task HubSignedIndex_WithReviewedGrants_DropsThem_AndKeepsTheRest()
    {
        var listing = TestPartner.Listing(grants: ["edition", "routing", "transport", "core"]) with
        {
            Reviewed = new Dictionary<string, string> { ["routing.json"] = RoutingHash },
        };
        var (json, sig) = Signed(_hub, listing);

        var index = V2pnPartnerTrust.ReadIndex(json, sig, [_owner.RootPublic], [HubPublic], out _)!;
        var taken = index.Get("configra")!;

        await string.Join(",", taken.Grants).Should().BeEqualTo("edition");
        await taken.ReviewedFiles.Count.Should().BeEqualTo(0);
        await index.Problems.Single().Contains("routing").Should().BeTrue();
        await index.Problems.Single().Contains("only the root key").Should().BeTrue();
    }

    [Test]
    public async Task RootSignedIndex_StillGrantsEverything_AndAKeyInBothListsIsARootKey()
    {
        var listing = TestPartner.Listing(grants: ["edition", "routing"]) with
        {
            Reviewed = new Dictionary<string, string> { ["routing.json"] = RoutingHash },
        };
        var (json, sig) = Signed(_owner.Root, listing);

        var index = V2pnPartnerTrust.ReadIndex(json, sig, [_owner.RootPublic], [HubPublic], out _)!;
        await index.Signer.Should().BeEqualTo(V2pnPartnerSigner.Root);
        await string.Join(",", index.Get("configra")!.Grants).Should().BeEqualTo("edition,routing");
        await index.Get("configra")!.ReviewedFiles["routing.json"].Should().BeEqualTo(RoutingHash);

        // The old overload takes root keys only: the hub's signature is refused there.
        var (hubJson, hubSig) = Signed(_hub, listing);
        await (V2pnPartnerTrust.ReadIndex(hubJson, hubSig, [_owner.RootPublic], out _) == null).Should().BeTrue();
        // Neither key: refused.
        await (V2pnPartnerTrust.ReadIndex(json, sig, [], [HubPublic], out var error) == null).Should().BeTrue();
        await error.Should().BeEqualTo("The partner index is not signed by V2pn.");
        // One key listed as both: it counts as the root's.
        await V2pnPartnerTrust.ReadIndex(json, sig, [_owner.RootPublic], [_owner.RootPublic], out _)!.Signer.Should().BeEqualTo(V2pnPartnerSigner.Root);
    }

    #endregion

    #region Merge

    [Test]
    public async Task Merge_TheHubsEntryStands_TheRootAddsThePartnersTheHubDoesNotList_ButNotOnItsDomains()
    {
        var hub = new V2pnPartnerIndex([Partner("configra", "#111111").Listing with { Signer = V2pnPartnerSigner.Hub }], [])
        { Signer = V2pnPartnerSigner.Hub, Published = T };
        var root = new V2pnPartnerIndex(
        [
            Partner("configra", "#222222", ["edition", "routing"]).Listing,
            Partner("kestrel", domains: ["kestrel.example.net"]).Listing,
            Partner("osprey", domains: ["*.example.com"]).Listing,
        ], []) { Published = T.AddDays(5) };

        var merged = V2pnPartnerIndex.Merge(hub, root)!;

        await string.Join(",", merged.Partners.Select(p => $"{p.Id}:{p.Signer}")).Should().BeEqualTo("configra:Hub,kestrel:Root");
        await merged.Get("configra")!.Zip.Sha256.Should().BeEqualTo(hub.Partners[0].Zip.Sha256);
        await merged.Problems.Single().StartsWith("osprey:").Should().BeTrue();
        await merged.Published.Should().BeEqualTo(T.AddDays(5));
        await V2pnPartnerIndex.Merge(null, root).Should().BeEqualTo(root);
        await V2pnPartnerIndex.Merge(hub, null).Should().BeEqualTo(hub);
    }

    [Test]
    public async Task DomainsOverlap_CountsWildcardsBothWays()
    {
        await V2pnPartnerTrust.DomainsOverlap(["a.example.com"], ["*.example.com"]).Should().BeTrue();
        await V2pnPartnerTrust.DomainsOverlap(["*.example.com"], ["*.x.example.com"]).Should().BeTrue();
        await V2pnPartnerTrust.DomainsOverlap(["example.com"], ["example.com"]).Should().BeTrue();
        await V2pnPartnerTrust.DomainsOverlap(["example.com"], ["*.example.com"]).Should().BeFalse();
        await V2pnPartnerTrust.DomainsOverlap(["notexample.com"], ["*.example.com"]).Should().BeFalse();
    }

    #endregion

    #region Sources

    [Test]
    public async Task Refresh_AsksBothSources_TheHubsPartnerFromTheHub_TheRestFromTheRelease_AndKeepsBoth()
    {
        var sites = new TestSites();
        var hubConfigra = Partner("configra", "#111111");
        sites.Publish(TestSites.Hub, _hub, T, hubConfigra);
        sites.Publish(TestSites.GitHub, _owner.Root, T.AddDays(-3), Partner("configra", "#222222"), Partner("kestrel", domains: ["kestrel.example.net"]));
        var root = NewRoot();
        try
        {
            var manager = Manager(sites, root, new V2pnEdition(null, ["configra", "kestrel"]));
            await manager.RefreshAsync();

            await manager.IndexError.Should().BeNull();
            await manager.Get("configra")!.Listing.Signer.Should().BeEqualTo(V2pnPartnerSigner.Hub);
            await manager.Get("configra")!.Listing.Zip.Sha256.Should().BeEqualTo(hubConfigra.Listing.Zip.Sha256);
            await manager.Get("kestrel")!.Listing.Signer.Should().BeEqualTo(V2pnPartnerSigner.Root);
            // Each pack from the source of the index that named it.
            await sites.Asked.Contains($"{TestSites.Hub}/latest/download/partner-configra.zip").Should().BeTrue();
            await sites.Asked.Contains($"{TestSites.GitHub}/latest/download/partner-configra.zip").Should().BeFalse();
            await sites.Asked.Contains($"{TestSites.GitHub}/latest/download/partner-kestrel.zip").Should().BeTrue();
            await File.Exists(Path.Combine(root, V2pnPartnerTrust.IndexFile)).Should().BeTrue();
            await File.Exists(Path.Combine(root, V2pnPartners.HubIndexFile)).Should().BeTrue();

            // The next start, offline: both kept indexes, merged the same way.
            var again = Manager(new TestSites(), root, new V2pnEdition(null, ["configra", "kestrel"]));
            await again.LoadAsync();
            await string.Join(",", again.Index!.Partners.Select(p => $"{p.Id}:{p.Signer}")).Should().BeEqualTo("configra:Hub,kestrel:Root");
            await again.Get("configra")!.Listing.Zip.Sha256.Should().BeEqualTo(hubConfigra.Listing.Zip.Sha256);
        }
        finally
        {
            Delete(root);
        }
    }

    [Test]
    public async Task Refresh_WithTheHubDown_TheReleasesRootSignedCopyIsTheFallback()
    {
        var sites = new TestSites();
        sites.Publish(TestSites.GitHub, _owner.Root, T, Partner());
        var root = NewRoot();
        try
        {
            var manager = Manager(sites, root, new V2pnEdition(null, ["configra"]));
            await manager.RefreshAsync();

            await manager.IndexError.Should().BeNull();
            await manager.Get("configra")!.Listing.Signer.Should().BeEqualTo(V2pnPartnerSigner.Root);
        }
        finally
        {
            Delete(root);
        }
    }

    [Test]
    public async Task Refresh_ALaggingCopyOfEitherSigner_IsSkipped_AndANewerRootIndexNeverShadowsTheHubs()
    {
        var sites = new TestSites();
        var hubConfigra = Partner("configra", "#111111");
        sites.Publish(TestSites.Hub, _hub, T, hubConfigra);
        sites.Publish(TestSites.GitHub, _owner.Root, T.AddDays(-3), Partner("kestrel", domains: ["kestrel.example.net"]));
        var root = NewRoot();
        try
        {
            var manager = Manager(sites, root, new V2pnEdition(null, ["configra", "kestrel"]));
            await manager.RefreshAsync();

            // The hub serves an older index (a stale cache): skipped, its partner stays as the newer one had it.
            sites.Clear(TestSites.Hub);
            sites.Publish(TestSites.Hub, _hub, T.AddDays(-1), Partner("configra", "#333333"));
            // The release gets a newer root-signed index that also lists configra: the hub's entry still stands.
            sites.Clear(TestSites.GitHub);
            sites.Publish(TestSites.GitHub, _owner.Root, T.AddDays(10), Partner("configra", "#444444"), Partner("kestrel", "#555555", domains: ["kestrel.example.net"]));
            await manager.RefreshAsync(forceIndex: true);

            await manager.Get("configra")!.Listing.Zip.Sha256.Should().BeEqualTo(hubConfigra.Listing.Zip.Sha256);
            await manager.Get("configra")!.Listing.Signer.Should().BeEqualTo(V2pnPartnerSigner.Hub);
            await manager.Get("kestrel")!.Listing.Zip.Sha256.Should().BeEqualTo(Partner("kestrel", "#555555", domains: ["kestrel.example.net"]).Listing.Zip.Sha256);
            await manager.IndexError.Should().BeNull();

            // The hub takes configra out: the root-signed entry of the same id comes in its place (the owner listed it too).
            sites.Clear(TestSites.Hub);
            sites.Publish(TestSites.Hub, _hub, T.AddDays(11));
            await manager.RefreshAsync(forceIndex: true);
            await manager.Index!.Get("configra")!.Signer.Should().BeEqualTo(V2pnPartnerSigner.Root);
        }
        finally
        {
            Delete(root);
        }
    }

    #endregion

    #region A provider's link

    [Test]
    public async Task Link_OnThePartnersDomains_MakesThePersonComeFromIt_AskingForTheIndexItDidNotHave()
    {
        var sites = new TestSites();
        sites.Publish(TestSites.Hub, _hub, T, Partner());
        var root = NewRoot();
        try
        {
            var edition = new V2pnEdition(null);
            var manager = Manager(sites, root, edition);
            await manager.LoadAsync();
            await (manager.Index == null).Should().BeTrue();

            var listing = await manager.ComeFromLinkAsync("configra", Sub);

            await listing!.Id.Should().BeEqualTo("configra");
            await string.Join(",", edition.CameFrom).Should().BeEqualTo("configra");
            await string.Join(",", edition.SignedIn).Should().BeEqualTo("configra");
            // The tab is the caller's to open; the pack comes with the refresh.
            await manager.RefreshAsync();
            await (manager.Get("configra") != null).Should().BeTrue();
        }
        finally
        {
            Delete(root);
        }
    }

    [Test]
    public async Task Link_OffItsDomains_ForAnUnknownPartner_OrOneOutOfTerm_IsJustASubscription()
    {
        var sites = new TestSites();
        sites.Publish(TestSites.Hub, _hub, T, Partner(), Partner("gone", domains: ["gone.example.net"], until: "2026-01-01"));
        var root = NewRoot();
        try
        {
            var edition = new V2pnEdition(null);
            var manager = Manager(sites, root, edition);

            await (await manager.ComeFromLinkAsync("configra", "https://evil.example.net/s/abc") == null).Should().BeTrue();
            await (await manager.ComeFromLinkAsync("kestrel", Sub) == null).Should().BeTrue();
            await (await manager.ComeFromLinkAsync("gone", "https://gone.example.net/s") == null).Should().BeTrue();
            await (await manager.ComeFromLinkAsync("Not An Id", Sub) == null).Should().BeTrue();
            await (await manager.ComeFromLinkAsync("configra", "javascript:alert(1)") == null).Should().BeTrue();

            await edition.Any.Should().BeFalse();
            // The index was asked for once: a link that does not match does not ask again within the minute.
            await sites.Asked.Count(u => u.EndsWith("/" + V2pnPartnerTrust.IndexFile, StringComparison.Ordinal)).Should().BeEqualTo(2);
        }
        finally
        {
            Delete(root);
        }
    }

    [Test]
    public async Task ListingForLink_AsksNothing_AndAnswersOnlyFromTheIndexAtHand()
    {
        var sites = new TestSites();
        sites.Publish(TestSites.Hub, _hub, T, Partner());
        var root = NewRoot();
        try
        {
            var manager = Manager(sites, root, new V2pnEdition(null, ["configra"]));
            await (manager.ListingForLink("configra", Sub) == null).Should().BeTrue();
            await manager.RefreshAsync();
            var asked = sites.Asked.Count;

            await manager.ListingForLink("configra", Sub)!.Id.Should().BeEqualTo("configra");
            await (manager.ListingForLink("configra", "https://evil.example.net/") == null).Should().BeTrue();
            await (manager.ListingForLink(null, Sub) == null).Should().BeTrue();
            await sites.Asked.Count.Should().BeEqualTo(asked);
        }
        finally
        {
            Delete(root);
        }
    }

    [Test]
    public async Task DeepLink_CarriesThePartner_AndAProvidersPageIsALinkToo()
    {
        var link = V2pnDeepLink.Parse("v2pn://add?url=https%3A%2F%2Fa.sub.example.com%2Fs%2Fabc&partner=configra&name=Configra")!;
        await link.Partner.Should().BeEqualTo("configra");
        await link.SubscriptionUrl.Should().BeEqualTo(Sub);
        await (V2pnDeepLink.Parse("v2pn://add?url=https%3A%2F%2Fa.example.com%2Fs&partner=..%2Fx")!.Partner == null).Should().BeTrue();
        await (V2pnDeepLink.Parse("happ://add/https://a.example.com/s")!.Partner == null).Should().BeTrue();

        var origins = V2pnDeepLink.PageOrigins;
        try
        {
            await string.Join(",", origins).Should().BeEqualTo("https://v2pn.angleford.co");
            V2pnDeepLink.PageOrigins = ["https://hub.example.org"];

            var page = V2pnDeepLink.Parse("https://hub.example.org/p/configra/#sub=https%3A%2F%2Fa.sub.example.com%2Fs%2Fabc")!;
            await page.Scheme.Should().BeEqualTo("page");
            await page.Partner.Should().BeEqualTo("configra");
            await page.SubscriptionUrl.Should().BeEqualTo(Sub);
            await V2pnDeepLink.Parse("https://hub.example.org/p/configra#sub=" + Sub)!.SubscriptionUrl.Should().BeEqualTo(Sub);

            foreach (var text in new[]
                     {
                         "https://elsewhere.example.org/p/configra/#sub=" + Sub,
                         "https://hub.example.org/p/configra/",
                         "https://hub.example.org/p/configra/x/#sub=" + Sub,
                         "https://hub.example.org/p/configra/?a=1#sub=" + Sub,
                         "https://hub.example.org/p/configra/#sub=http://a.sub.example.com/s",
                         "https://hub.example.org/p/configra/#sub=https://user:pw@a.sub.example.com/s",
                         "http://hub.example.org/p/configra/#sub=" + Sub,
                     })
            {
                await (V2pnDeepLink.Parse(text) == null).Should().BeTrue();
            }
        }
        finally
        {
            V2pnDeepLink.PageOrigins = origins;
        }
    }

    #endregion

    #region The marker of an older partner build

    [Test]
    public async Task Marker_IsDeletedOnlyOnceTheSettingsKeepIt()
    {
        var dir = NewRoot();
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, V2pnEdition.FileName);
            await File.WriteAllTextAsync(file, "configra\n");
            await V2pnEdition.Read(dir).Should().BeEqualTo("configra");

            await (await V2pnEdition.RetireMarkerFileAsync(dir, () => Task.FromResult(false))).Should().BeFalse();
            await File.Exists(file).Should().BeTrue();

            await (await V2pnEdition.RetireMarkerFileAsync(dir, () => Task.FromResult(true))).Should().BeTrue();
            await File.Exists(file).Should().BeFalse();

            // What the settings keep after the move: the person still comes from it, with its tab, and it is never forgotten.
            var edition = new V2pnEdition("configra");
            await string.Join(",", edition.CameFrom).Should().BeEqualTo("configra");
            await edition.Tab.Should().BeEqualTo("configra");
            await edition.ForgetSignIn("configra").Should().BeFalse();
            await edition.IsFrom("configra").Should().BeTrue();
        }
        finally
        {
            Delete(dir);
        }
    }

    #endregion
}
