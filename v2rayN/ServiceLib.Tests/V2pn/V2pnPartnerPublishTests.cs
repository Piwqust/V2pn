namespace ServiceLib.Tests.V2pn;

/// <summary>
/// tools/partner.cs publish: partners/&lt;id&gt; folders in the repository become partner-index.json and one zip each, which
/// the app reads back with the checks it uses for a download, and an empty partners.json for older copies.
/// </summary>
public class V2pnPartnerPublishTests
{
    private static readonly DateOnly Today = new(2026, 11, 16);
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 30, 0, TimeSpan.Zero);

    private static readonly byte[] RoutingJson = """
        { "name": "Видео через VPN", "rules": [ { "domains": ["geosite:youtube"], "to": "proxy" } ], "default": "direct" }
        """u8.ToArray();

    // Just the header bytes the size is read from.
    private static byte[] Png(int width, int height, int length = 33)
    {
        var b = new byte[length];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        b[16] = (byte)((width >> 24) & 0xFF); b[17] = (byte)((width >> 16) & 0xFF); b[18] = (byte)((width >> 8) & 0xFF); b[19] = (byte)(width & 0xFF);
        b[20] = (byte)((height >> 24) & 0xFF); b[21] = (byte)((height >> 16) & 0xFF); b[22] = (byte)((height >> 8) & 0xFF); b[23] = (byte)(height & 0xFF);
        return b;
    }

    /// <summary>partners/ with one folder per id: listing.json, partner.json (assets filled in), a logo, a mark and <paramref name="routing"/>.</summary>
    private static string Partners(string[]? grants = null, bool routing = false, params string[] ids)
    {
        var dir = Path.Combine(Path.GetTempPath(), "v2pn-publish-test-" + Guid.NewGuid().ToString("N")[..8]);
        foreach (var id in ids.DefaultIfEmpty("kestrel"))
        {
            var folder = Path.Combine(dir, id);
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "logo.png"), Png(340, 96));
            File.WriteAllBytes(Path.Combine(folder, "mark.png"), Png(128, 128));
            if (routing)
            {
                File.WriteAllBytes(Path.Combine(folder, "routing.json"), RoutingJson);
            }
            File.WriteAllBytes(Path.Combine(folder, "listing.json"), V2pnPartnerSigning.Json(new JsonObject
            {
                ["id"] = id,
                ["name"] = "Kestrel",
                ["domains"] = new JsonArray($"{id}.example", $"*.{id}.example"),
                ["grants"] = new JsonArray((grants ?? ["account", "actions"]).Select(g => (JsonNode)g).ToArray()),
                ["until"] = "2027-11-16",
                ["contact"] = "@kestrel_admin",
            }));
            V2pnPackWriter.Write(folder, new JsonObject
            {
                ["schema"] = 1,
                ["id"] = id,
                ["name"] = "Kestrel",
                ["brand"] = new JsonObject { ["accent"] = "#FF7A2F", ["logo"] = "logo.png", ["mark"] = "mark.png" },
                ["theme"] = new JsonObject
                {
                    ["background"] = new JsonObject { ["type"] = "gradient", ["colors"] = new JsonArray("#FF7A2F", "#EDAA80") },
                },
                ["api"] = $"https://{id}.example/v2pn/api/",
            });
        }
        return dir;
    }

    private static void Delete(string dir)
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Publish_ThenRead_GivesThePartnerAsTheAppTakesIt()
    {
        var dir = Partners();
        try
        {
            var publication = V2pnPartnerPublisher.Publish(dir, Today, Now);
            await string.Join("; ", publication.Problems).Should().BeEqualTo(string.Empty);
            using var root = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var signature = V2pnPartnerSigning.Sign(root, publication.Index);

            var index = V2pnPartnerTrust.ReadIndex(publication.Index, signature, [V2pnPartnerSigning.PublicKey(root)], out var error);
            await error.Should().BeEqualTo(string.Empty);
            var listing = index!.Get("kestrel")!;
            // Every partner dresses the window; the rest is as listing.json grants it.
            await listing.Grants.Should().BeEquivalentTo(new[] { "edition", "account", "actions" });
            await listing.Domains.Should().BeEquivalentTo(new[] { "kestrel.example", "*.kestrel.example" });
            await listing.Until.Should().BeEqualTo(new DateOnly(2027, 11, 16));
            // The contact is the owner's to know; it is not published.
            await Encoding.UTF8.GetString(publication.Index).Contains("kestrel_admin").Should().BeFalse();

            var zip = publication.Partners.Single().Zip;
            var pack = V2pnPartnerTrust.Unpack(zip, listing, out var files, out error);
            await error.Should().BeEqualTo(string.Empty);
            await files.Keys.Order().Should().BeEquivalentTo(new[] { "logo.png", "mark.png", "partner.json" });
            await pack!.Theme!.Background.Kind.Should().BeEqualTo(V2pnThemeBackgroundKinds.Gradient);
            await pack.ApiUrl.Should().BeEqualTo("https://kestrel.example/v2pn/api/");
        }
        finally
        {
            Delete(dir);
        }
    }

    [Test]
    public async Task WriteOut_GivesPartnerBuildsTheIndex_AndOlderCopiesAnEmptySignedOne()
    {
        var dir = Partners(ids: ["kestrel", "configra"]);
        var outDir = Path.Combine(dir, "out");
        using var root = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = V2pnPartnerSigning.PublicKey(root);
        try
        {
            // What an earlier publication left, including a zip of a partner gone since.
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "partner-gone.zip"), "old");
            File.WriteAllText(Path.Combine(outDir, "notes.txt"), "mine");

            var publication = V2pnPartnerPublisher.Publish(dir, Today, Now);
            var written = V2pnPartnerPublisher.WriteOut(outDir, publication, root);

            await written.Should().BeEquivalentTo(["partner-index.json", "partner-index.json.sig", "partners.json", "partners.json.sig",
                "partner-configra.zip", "partner-kestrel.zip"]);
            var index = V2pnPartnerTrust.ReadIndex(File.ReadAllBytes(Path.Combine(outDir, "partner-index.json")),
                File.ReadAllText(Path.Combine(outDir, "partner-index.json.sig")).Trim(), [key], out var error);
            await error.Should().BeEqualTo(string.Empty);
            await index!.Partners.Select(l => l.Id).ToList().Should().BeEquivalentTo(["configra", "kestrel"]);

            // V2pn 1.2.1 reads partners.json: signed, as new as the index, and with no partner, so it drops the one it kept.
            var legacyBytes = File.ReadAllBytes(Path.Combine(outDir, "partners.json"));
            var legacy = V2pnPartnerTrust.ReadIndex(legacyBytes, File.ReadAllText(Path.Combine(outDir, "partners.json.sig")).Trim(), [key], out error);
            await error.Should().BeEqualTo(string.Empty);
            await legacy!.Partners.Count.Should().BeEqualTo(0);
            await JsonNode.Parse(legacyBytes)!["published"]!.GetValue<string>().Should().BeEqualTo("2026-11-16T09:30:00Z");

            await File.Exists(Path.Combine(outDir, "partner-gone.zip")).Should().BeFalse();
            await File.Exists(Path.Combine(outDir, "notes.txt")).Should().BeTrue();
        }
        finally
        {
            Delete(dir);
        }
    }

    [Test]
    public async Task Publish_TheSameFiles_GiveTheSameZip()
    {
        var dir = Partners();
        try
        {
            var first = V2pnPartnerPublisher.Publish(dir, Today, Now).Partners.Single();
            var second = V2pnPartnerPublisher.Publish(dir, Today, Now.AddDays(3)).Partners.Single();

            // Nobody downloads a pack again because it was published again.
            await second.Listing.Zip.Should().BeEqualTo(first.Listing.Zip);
        }
        finally
        {
            Delete(dir);
        }
    }

    [Test]
    public async Task Publish_PutsTheReviewedHash_OnlyWithItsGrant()
    {
        var without = Partners(routing: true);
        var with = Partners(grants: ["actions", "routing"], routing: true);
        try
        {
            var plain = V2pnPartnerPublisher.Publish(without, Today, Now).Partners.Single();
            await plain.Listing.ReviewedFiles.Count.Should().BeEqualTo(0);
            await plain.Notes.Any(n => n.Contains("routing.json is not used")).Should().BeTrue();

            var reviewed = V2pnPartnerPublisher.Publish(with, Today, Now).Partners.Single();
            await reviewed.Listing.ReviewedFiles["routing.json"].Should().BeEqualTo(V2pnReviewedFiles.Hash(RoutingJson));
            V2pnPartnerTrust.Unpack(reviewed.Zip, reviewed.Listing, out var files, out _);
            var pack = V2pnPartnerTrust.ReadPack(files["partner.json"], reviewed.Listing, out _)!;
            var features = V2pnPartnerFeatures.Read(reviewed.Listing, pack, name => files.GetValueOrDefault(name));
            await (features.Routing != null).Should().BeTrue();
        }
        finally
        {
            Delete(without);
            Delete(with);
        }
    }

    [Test]
    public async Task Publish_RefusesWhatTheOwnerDidNotLookAt()
    {
        var dir = Partners();
        try
        {
            var folder = Path.Combine(dir, "kestrel");
            // A picture changed after partner.json listed it.
            File.WriteAllBytes(Path.Combine(folder, "logo.png"), Png(400, 100));
            await V2pnPartnerPublisher.Publish(dir, Today, Now).Problems.Single().Contains("partner.cs -- pack").Should().BeTrue();
            V2pnPackWriter.Write(folder, V2pnPackWriter.Read(folder, out _)!);
            await V2pnPartnerPublisher.Publish(dir, Today, Now).Ok.Should().BeTrue();

            // A file the pack cannot carry.
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "draft");
            await V2pnPartnerPublisher.Publish(dir, Today, Now).Problems.Single().Contains("notes.txt").Should().BeTrue();
        }
        finally
        {
            Delete(dir);
        }
    }

    [Test]
    public async Task Publish_RefusesABadListing()
    {
        void Change(string dir, Action<JsonObject> change)
        {
            var file = Path.Combine(dir, "kestrel", "listing.json");
            var listing = (JsonObject)JsonNode.Parse(File.ReadAllText(file))!;
            change(listing);
            File.WriteAllBytes(file, V2pnPartnerSigning.Json(listing));
        }
        foreach (var (change, expected) in new (Action<JsonObject>, string)[]
                 {
                     (l => l["domains"] = new JsonArray("localhost"), "localhost"),
                     (l => l["domains"] = new JsonArray("*.com"), "*.com"),
                     (l => l["domains"] = new JsonArray(), "at least one"),
                     (l => l["grants"] = new JsonArray("edition", "everything"), "everything"),
                     (l => l["until"] = "2026-11-16", "not after today"),
                     (l => l["id"] = "other", "folder's name"),
                     (l => l["name"] = "V2pn Kestrel", "V2pn"),
                 })
        {
            var dir = Partners();
            try
            {
                Change(dir, change);
                var problems = V2pnPartnerPublisher.Publish(dir, Today, Now).Problems;
                await problems.Count.Should().BeEqualTo(1);
                await problems[0].Contains(expected).Should().BeTrue();
            }
            finally
            {
                Delete(dir);
            }
        }
    }

    [Test]
    public async Task Publish_RefusesADomainOfTwoPartners()
    {
        var dir = Partners(null, false, "kestrel", "osprey");
        try
        {
            var file = Path.Combine(dir, "osprey", "listing.json");
            var listing = (JsonObject)JsonNode.Parse(File.ReadAllText(file))!;
            listing["domains"] = new JsonArray("osprey.example", "kestrel.example");
            File.WriteAllBytes(file, V2pnPartnerSigning.Json(listing));

            var problems = V2pnPartnerPublisher.Publish(dir, Today, Now).Problems;

            await problems.Single().Contains("kestrel.example is listed by kestrel and osprey").Should().BeTrue();
        }
        finally
        {
            Delete(dir);
        }
    }

    [Test]
    public async Task PrivateKey_ReadFromPem_SignsWhatItsPublicHalfVerifies()
    {
        var (pem, publicKey) = V2pnPartnerSigning.NewKey();
        using var key = V2pnPartnerSigning.ReadPrivateKey(pem, out var error);

        await error.Should().BeEqualTo(string.Empty);
        await V2pnPartnerSigning.PublicKey(key!).Should().BeEqualTo(publicKey);
        await V2pnPartnerSigning.Verify(publicKey, [1, 2, 3], V2pnPartnerSigning.Sign(key!, [1, 2, 3])).Should().BeTrue();

        // A public key, another curve, or anything else is not a signing key.
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        await (V2pnPartnerSigning.ReadPrivateKey(p384.ExportPkcs8PrivateKeyPem(), out _) == null).Should().BeTrue();
        await (V2pnPartnerSigning.ReadPrivateKey(key!.ExportSubjectPublicKeyInfoPem(), out _) == null).Should().BeTrue();
        await (V2pnPartnerSigning.ReadPrivateKey("hello", out _) == null).Should().BeTrue();
    }

    [Test]
    public async Task Slot_Check_SaysWhyAPictureDoesNotFit()
    {
        await V2pnPartnerSlots.Check(V2pnPartnerSlots.Backdrop, Png(1600, 800)).Problem.Should().BeEqualTo(V2pnPartnerSlots.Problem.None);
        var small = V2pnPartnerSlots.Check(V2pnPartnerSlots.Backdrop, Png(120, 90));
        await small.Problem.Should().BeEqualTo(V2pnPartnerSlots.Problem.WrongSize);
        await small.Width.Should().BeEqualTo(120);
        await V2pnPartnerSlots.Check(V2pnPartnerSlots.Logo, Png(340, 96, V2pnPartnerTrust.MaxAssetBytes + 1)).Problem.Should().BeEqualTo(V2pnPartnerSlots.Problem.TooLarge);
        await V2pnPartnerSlots.Check(V2pnPartnerSlots.Mark, [1, 2, 3, 4]).Problem.Should().BeEqualTo(V2pnPartnerSlots.Problem.NotPicture);
    }
}
