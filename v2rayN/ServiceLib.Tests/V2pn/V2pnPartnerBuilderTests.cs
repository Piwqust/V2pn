namespace ServiceLib.Tests.V2pn;

/// <summary>
/// The theme builder and the certificate request: requests signed with the provider's key, certificates issued from them,
/// packs written with their assets, the same bytes and signatures as tools/partner.cs, and a preview that never stays.
/// </summary>
public class V2pnPartnerBuilderTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);
    private static readonly byte[] RoutingJson = """
        { "schema": 1, "name": "Видео через VPN", "rules": [ { "domains": ["geosite:youtube"], "to": "proxy" } ], "default": "direct" }
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

    private static string TempFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "v2pn-builder-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Delete(string dir)
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }
    }

    private static byte[] Request(ECDsa key, string name = "Kestrel", Dictionary<string, string>? reviewed = null) =>
        V2pnCertRequests.Create("kestrel", name, ["kestrel.example", "*.kestrel.example"], ["edition", "account"],
            reviewed ?? new Dictionary<string, string>(), "@kestrel_admin", key, Today);

    #region Requests

    [Test]
    public async Task Request_SignedWithTheProvidersKey_IsRead()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var reviewed = new Dictionary<string, string> { ["routing.json"] = V2pnReviewedFiles.Hash(RoutingJson) };

        var request = V2pnCertRequests.Read(Request(key, reviewed: reviewed), out var error);

        await error.Should().BeEqualTo(string.Empty);
        await request!.Partner.Should().BeEqualTo("kestrel");
        await request.Name.Should().BeEqualTo("Kestrel");
        await request.Domains.Should().BeEquivalentTo(new[] { "kestrel.example", "*.kestrel.example" });
        await request.Key.Should().BeEqualTo(V2pnPartnerSigning.PublicKey(key));
        // Every partner gets actions; a reviewed file brings its grant.
        await request.Grants.Should().BeEquivalentTo(new[] { "edition", "account", "actions", "routing" });
        await request.Reviewed["routing.json"].Should().BeEqualTo(V2pnReviewedFiles.Hash(RoutingJson));
        await request.Contact.Should().BeEqualTo("@kestrel_admin");
        await request.Created.Should().BeEqualTo(Today);
    }

    [Test]
    public async Task Request_Reformatted_StillReads()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var compact = Encoding.UTF8.GetBytes(JsonNode.Parse(Request(key))!.ToJsonString());

        await (V2pnCertRequests.Read(compact, out _) != null).Should().BeTrue();
    }

    [Test]
    public async Task Request_ChangedAfterSigning_IsRefused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var root = (JsonObject)JsonNode.Parse(Request(key))!;
        root["domains"] = new JsonArray(JsonValue.Create("evil.example"));

        var request = V2pnCertRequests.Read(Encoding.UTF8.GetBytes(root.ToJsonString()), out var error);

        await (request == null).Should().BeTrue();
        await error.Contains("not signed by the key").Should().BeTrue();
    }

    [Test]
    public async Task Request_SignedWithAnotherKey_IsRefused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        // Someone puts their own public key into a provider's request: the signature is not theirs.
        var root = (JsonObject)JsonNode.Parse(Request(key))!;
        root["key"] = V2pnPartnerSigning.PublicKey(other);

        await (V2pnCertRequests.Read(Encoding.UTF8.GetBytes(root.ToJsonString()), out _) == null).Should().BeTrue();

        // And a request signed with a key other than the one it names.
        root = (JsonObject)JsonNode.Parse(Request(key))!;
        root.Remove("signature");
        root["key"] = V2pnPartnerSigning.PublicKey(other);
        root["signature"] = V2pnPartnerSigning.Sign(key, V2pnPartnerSigning.Compact(root));
        await (V2pnCertRequests.Read(Encoding.UTF8.GetBytes(root.ToJsonString()), out _) == null).Should().BeTrue();
    }

    [Test]
    public async Task Request_BreakingTheRules_IsNotWritten()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var none = new Dictionary<string, string>();

        await Throws(() => V2pnCertRequests.Create("Kestrel!", "Kestrel", ["kestrel.example"], ["edition"], none, null, key, Today)).Should().BeTrue();
        await Throws(() => V2pnCertRequests.Create("kestrel", "Kestrel V2pn", ["kestrel.example"], ["edition"], none, null, key, Today)).Should().BeTrue();
        await Throws(() => V2pnCertRequests.Create("kestrel", "Kestrel", ["*.com"], ["edition"], none, null, key, Today)).Should().BeTrue();
        await Throws(() => V2pnCertRequests.Create("kestrel", "Kestrel", ["kestrel.example"], ["everything"], none, null, key, Today)).Should().BeTrue();
        await Throws(() => V2pnCertRequests.Create("kestrel", "Kestrel", ["kestrel.example"], ["edition"],
            new Dictionary<string, string> { ["evil.json"] = new string('a', 64) }, null, key, Today)).Should().BeTrue();
    }

    private static bool Throws(Action act)
    {
        try
        {
            act();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    [Test]
    public async Task Cert_FromRequest_IsTakenByV2pn()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var root = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var hash = V2pnReviewedFiles.Hash(RoutingJson);
        var request = V2pnCertRequests.Read(Request(key, reviewed: new() { ["routing.json"] = hash }), out _)!;

        // What partner.cs cert --from writes.
        var grants = V2pnPartnerIssuer.Grants(request.Grants, request.Reviewed.Keys, out _)!;
        var json = V2pnPartnerIssuer.Cert(request.Partner, request.Name, request.Domains, request.Key, grants, request.Reviewed, Today, 365);
        var cert = V2pnPartnerTrust.ReadCert(json, V2pnPartnerSigning.Sign(root, json), [V2pnPartnerSigning.PublicKey(root)], Today, out var error);

        await error.Should().BeEqualTo(string.Empty);
        await cert!.Partner.Should().BeEqualTo("kestrel");
        await cert.Key.Should().BeEqualTo(V2pnPartnerSigning.PublicKey(key));
        await cert.Domains.Should().BeEquivalentTo(new[] { "kestrel.example", "*.kestrel.example" });
        await cert.Grants.Should().BeEquivalentTo(new[] { "edition", "account", "actions", "routing" });
        await cert.ReviewedFiles["routing.json"].Should().BeEqualTo(hash);
        await cert.Expires.Should().BeEqualTo(Today.AddDays(365));

        // The provider's key from the request signs packs the certificate accepts.
        var pack = TestPartner.Pack(id: "kestrel", name: "Kestrel", api: null);
        await (V2pnPartnerTrust.ReadPack(pack, V2pnPartnerSigning.Sign(key, pack), cert, out _) != null).Should().BeTrue();
    }

    [Test]
    public async Task Cert_IsTheSameAsPartnerCsWroteIt()
    {
        // tools/partner.cs before the builder: a JsonObject written with these options and a newline.
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var old = new JsonObject
        {
            ["schema"] = 1,
            ["partner"] = "kestrel",
            ["name"] = "Кестрел",
            ["domains"] = new JsonArray(JsonValue.Create("kestrel.example")),
            ["key"] = "KEY",
            ["grants"] = new JsonArray(JsonValue.Create("edition"), JsonValue.Create("actions")),
            ["issued"] = "2026-11-15",
            ["expires"] = "2027-11-15",
        };
        var expected = Encoding.UTF8.GetBytes(old.ToJsonString(options) + "\n");

        var bytes = V2pnPartnerIssuer.Cert("kestrel", "Кестрел", ["Kestrel.example"], "KEY", ["edition", "actions"], null, Today, 365);

        await bytes.Should().BeEquivalentTo(expected);
    }

    #endregion Requests

    #region Packs

    [Test]
    public async Task Pack_Written_ListsEveryPictureAndReviewedFile_WithSizeAndHash()
    {
        var folder = TempFolder();
        try
        {
            var logo = Png(340, 96);
            var hero = Png(800, 600, 50);
            File.WriteAllBytes(Path.Combine(folder, "logo.png"), logo);
            File.WriteAllBytes(Path.Combine(folder, "hero.png"), hero);
            File.WriteAllBytes(Path.Combine(folder, "routing.json"), RoutingJson);
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "not part of the pack");
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var draft = V2pnPackDraft.New();
            draft.Id = "kestrel";
            draft.Name = "Kestrel";
            draft.SetPicture(V2pnPackDraft.Pictures.Logo, "logo.png");
            draft.SetPicture(V2pnPackDraft.Pictures.Hero, "hero.png");

            var (bytes, signature) = V2pnPackWriter.Write(folder, draft.Root, key);

            var assets = (JsonNode.Parse(File.ReadAllBytes(Path.Combine(folder, "partner.json")))!["assets"] as JsonArray)!;
            await assets.Select(a => a!["name"]!.GetValue<string>()).Should().BeEquivalentTo(new[] { "hero.png", "logo.png", "routing.json" });
            await assets[1]!["size"]!.GetValue<long>().Should().BeEqualTo(logo.LongLength);
            await assets[1]!["sha256"]!.GetValue<string>().Should().BeEqualTo(Convert.ToHexStringLower(SHA256.HashData(logo)));
            await assets[2]!["sha256"]!.GetValue<string>().Should().BeEqualTo(V2pnReviewedFiles.Hash(RoutingJson));
            await File.ReadAllText(Path.Combine(folder, "partner.json.sig")).Trim().Should().BeEqualTo(signature!);

            // A certificate for the key takes the pack with its theme and pictures.
            var p = new TestPartner();
            var cert = p.ReadCert(V2pnPartnerIssuer.Cert("kestrel", "Kestrel", ["kestrel.example"], V2pnPartnerSigning.PublicKey(key), ["edition", "actions"], null, Today, 365));
            var pack = V2pnPartnerTrust.ReadPack(bytes, signature, cert, out var error);
            await error.Should().BeEqualTo(string.Empty);
            await pack!.Theme!.Picture(V2pnThemeArt.Hero).Should().BeEqualTo("hero.png");
            await pack.Logo.Should().BeEqualTo("logo.png");
        }
        finally
        {
            Delete(folder);
        }
    }

    [Test]
    public async Task Pack_OpenedAndSavedUnchanged_IsTheSameBytes()
    {
        var folder = TempFolder();
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "orb.png"), Png(800, 800));
            var draft = V2pnPackDraft.New();
            draft.Id = "ember";
            draft.Name = "Эмбер";
            draft.Template = V2pnThemeTemplates.Half;
            draft.Brand = ["#E11D48", "#FB7185"];
            draft.SetPicture(V2pnPackDraft.Pictures.Hero, "orb.png");
            draft.Root["links"] = new JsonObject { ["support"] = "https://t.me/ember" };
            draft.SetActions([new JsonObject { ["id"] = "fast", ["label"] = "Самый быстрый", ["icon"] = "bolt", ["do"] = "connect.best" }]);
            var (first, _) = V2pnPackWriter.Write(folder, draft.Root, null);

            // Open: the builder reads the folder and edits nothing.
            var opened = new V2pnPackDraft(V2pnPackWriter.Read(folder, out _)!);
            await opened.Name.Should().BeEqualTo("Эмбер");
            await opened.Picture(V2pnPackDraft.Pictures.Hero).Should().BeEqualTo("orb.png");
            await opened.FitTemplate().Count.Should().BeEqualTo(0);
            var (second, _) = V2pnPackWriter.Write(folder, opened.Root, null);

            await second.Should().BeEquivalentTo(first);
            // A field the builder does not edit stays where it was.
            await Encoding.UTF8.GetString(second).Contains("\"support\": \"https://t.me/ember\"").Should().BeTrue();
        }
        finally
        {
            Delete(folder);
        }
    }

    [Test]
    public async Task Pack_SignedInTheApp_IsWhatPartnerCsSigns()
    {
        var folder = TempFolder();
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "mark.png"), Png(128, 128));
            var draft = V2pnPackDraft.New();
            draft.Id = "kestrel";
            draft.Name = "Кестрел";
            draft.SetPicture(V2pnPackDraft.Pictures.Mark, "mark.png");
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var (inApp, signature) = V2pnPackWriter.Write(folder, draft.Root, key);

            // tools/partner.cs pack before the builder: every picture in "assets", the object written with these options.
            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            var pack = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "partner.json")))!;
            var content = File.ReadAllBytes(Path.Combine(folder, "mark.png"));
            pack["assets"] = new JsonArray(new JsonObject
            {
                ["name"] = "mark.png",
                ["size"] = content.LongLength,
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(content)),
            });
            var script = Encoding.UTF8.GetBytes(pack.ToJsonString(options) + "\n");

            await inApp.Should().BeEquivalentTo(script);
            // ECDSA signatures differ each time; both verify against the provider's key.
            var publicKey = V2pnPartnerSigning.PublicKey(key);
            await V2pnPartnerSigning.Verify(publicKey, script, signature).Should().BeTrue();
            await V2pnPartnerSigning.Verify(publicKey, inApp, V2pnPartnerSigning.Sign(key, script)).Should().BeTrue();

            // And signing it again (partner.cs pack on the builder's folder) leaves partner.json as it was.
            var (again, _) = V2pnPackWriter.Write(folder, V2pnPackWriter.Read(folder, out _)!, key);
            await again.Should().BeEquivalentTo(inApp);
        }
        finally
        {
            Delete(folder);
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
    public async Task Draft_FitTemplate_TakesOutWhatTheTemplateHasNoPlaceFor()
    {
        var draft = V2pnPackDraft.New();
        draft.Blocks = ["plan", "news", "art", "servers"];
        draft.SetPicture(V2pnPackDraft.Pictures.Hero, "orb.png");
        draft.BackgroundKind = V2pnThemeBackgroundKinds.Gradient;
        draft.BackgroundColors = ["#111111", "#222222"];
        draft.Template = V2pnThemeTemplates.Map;

        var removed = draft.FitTemplate();

        await draft.Blocks.Should().BeEquivalentTo(new[] { "plan", "servers" });
        await (draft.Picture(V2pnPackDraft.Pictures.Hero) == null).Should().BeTrue();
        await draft.BackgroundKind.Should().BeEqualTo(V2pnThemeBackgroundKinds.Solid);
        await removed.Count.Should().BeEqualTo(4);
    }

    [Test]
    public async Task Slot_Check_SaysWhyAPictureDoesNotFit()
    {
        await V2pnPartnerSlots.Check(V2pnPartnerSlots.Hero, Png(800, 600)).Problem.Should().BeEqualTo(V2pnPartnerSlots.Problem.None);
        var small = V2pnPartnerSlots.Check(V2pnPartnerSlots.Hero, Png(120, 90));
        await small.Problem.Should().BeEqualTo(V2pnPartnerSlots.Problem.WrongSize);
        await small.Width.Should().BeEqualTo(120);
        await V2pnPartnerSlots.Check(V2pnPartnerSlots.Logo, Png(340, 96, V2pnPartnerTrust.MaxAssetBytes + 1)).Problem.Should().BeEqualTo(V2pnPartnerSlots.Problem.TooLarge);
        await V2pnPartnerSlots.Check(V2pnPartnerSlots.Mark, [1, 2, 3, 4]).Problem.Should().BeEqualTo(V2pnPartnerSlots.Problem.NotPicture);
    }

    #endregion Packs

    #region Preview

    [Test]
    public async Task Preview_IsWornButNeverKept()
    {
        var root = TempFolder();
        var folder = TempFolder();
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "logo.png"), Png(340, 96));
            var draft = V2pnPackDraft.New();
            draft.Id = "kestrel";
            draft.Name = "Kestrel";
            draft.Template = V2pnThemeTemplates.Split;
            draft.SetPicture(V2pnPackDraft.Pictures.Logo, "logo.png");
            V2pnPackWriter.Write(folder, draft.Root, null);
            var manager = new V2pnPartners([], () => root) { Today = () => Today };

            var preview = manager.SetPreview(folder, out var error);

            await error.Should().BeEqualTo(string.Empty);
            await preview!.Id.Should().BeEqualTo(V2pnPartners.PreviewId);
            await preview.HasEdition.Should().BeTrue();
            await preview.Pack.Theme!.Template.Should().BeEqualTo(V2pnThemeTemplates.Split);
            await preview.Pack.Logo.Should().BeEqualTo("logo.png");
            await manager.Get(V2pnPartners.PreviewId).Should().BeEqualTo(preview);
            // Drawn from a copy in the temp folder, never from the partners' folder, and no certificate id can be it.
            await preview.Folder.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase).Should().BeTrue();
            await Directory.EnumerateFileSystemEntries(root).Any().Should().BeFalse();
            await V2pnPartnerTrust.ValidId(V2pnPartners.PreviewId).Should().BeFalse();

            // No subscription points to it, yet the hourly check leaves it alone.
            manager.Prune([]);
            await (manager.Get(V2pnPartners.PreviewId) != null).Should().BeTrue();

            // Another V2pn start finds nothing of it.
            var restarted = new V2pnPartners([], () => root) { Today = () => Today };
            await restarted.LoadAsync();
            await restarted.All.Count.Should().BeEqualTo(0);

            manager.RemovePreview();
            await (manager.Get(V2pnPartners.PreviewId) == null).Should().BeTrue();
            await Directory.Exists(preview.Folder).Should().BeFalse();
        }
        finally
        {
            Delete(root);
            Delete(folder);
        }
    }

    [Test]
    public async Task Preview_OfAPackStillBeingTyped_DrawsWithoutAName()
    {
        var folder = TempFolder();
        try
        {
            V2pnPackWriter.Write(folder, V2pnPackDraft.New().Root, null);

            var partner = V2pnPartners.ReadUnsigned(folder, V2pnPartners.PreviewId, Today, out _, out var error);

            await error.Should().BeEqualTo(string.Empty);
            await partner!.Name.Should().BeEqualTo("Provider");
            await partner.Pack.Variants.Should().BeEquivalentTo(new[] { "theme", "signature" });
        }
        finally
        {
            Delete(folder);
        }
    }

    #endregion Preview
}
