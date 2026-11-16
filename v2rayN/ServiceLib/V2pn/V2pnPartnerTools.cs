namespace ServiceLib;

/// <summary>
/// Keys and signatures of the partner index (tools/partner.cs publish). Signatures are DER ECDSA P-256 / SHA-256 in
/// base64, the same as release.json.sig.
/// </summary>
public static class V2pnPartnerSigning
{
    public static string Sign(ECDsa key, byte[] data) =>
        Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

    public static bool Verify(string publicKey, byte[] data, string? signature) => V2pnManifest.VerifySignature(data, signature, [publicKey]);

    /// <summary>The public half as base64 SubjectPublicKeyInfo: what V2pnPartnerKeys in v2rayN/Directory.Build.targets holds.</summary>
    public static string PublicKey(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    /// <summary>A new P-256 key: the private half as PKCS#8 PEM (to keep secret) and the public half. For test roots.</summary>
    public static (string Pem, string PublicKey) NewKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportPkcs8PrivateKeyPem() + Environment.NewLine, PublicKey(key));
    }

    /// <summary>A private ECDSA P-256 key from PEM text (PKCS#8 or "EC PRIVATE KEY"); null with the reason otherwise.</summary>
    public static ECDsa? ReadPrivateKey(string? pem, out string error)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportFromPem(pem ?? string.Empty);
            var curve = key.ExportParameters(false).Curve.Oid;
            if (key.KeySize != 256 || !(curve?.Value == "1.2.840.10045.3.1.7" || curve?.FriendlyName is "nistP256" or "ECDSA_P256" or "secp256r1" or "prime256v1"))
            {
                error = "The key is not an ECDSA P-256 key.";
                key.Dispose();
                return null;
            }
            // A public key alone cannot sign.
            key.ExportParameters(true);
            error = string.Empty;
            return key;
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            error = "The file is not an ECDSA P-256 private key in PEM.";
            key.Dispose();
            return null;
        }
    }

    /// <summary>A JSON document as the partner tools write it: indented, Cyrillic as is, a newline at the end.</summary>
    public static byte[] Json(JsonNode node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            node.WriteTo(writer);
        }
        stream.Write("\n"u8);
        return stream.ToArray();
    }
}

/// <summary>
/// partner.json of a pack folder (partners/&lt;id&gt; in the repository) with every picture and reviewed file of the folder
/// in "assets" (size and SHA-256). tools/partner.cs pack writes it through here.
/// </summary>
public static class V2pnPackWriter
{
    public const string PackFile = V2pnPartnerTrust.PackFile;

    public static bool IsPicture(string name) => V2pnPartnerTrust.IsPictureName(name);

    /// <summary>The files of <paramref name="folder"/> that go into "assets": the pictures and the reviewed files, by name.</summary>
    public static IReadOnlyList<string> Files(string folder) =>
        Directory.EnumerateFiles(folder)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(n => IsPicture(n) || V2pnReviewedFiles.All.Contains(n))
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>"assets" for <paramref name="folder"/>: name, size and SHA-256 of each of its <see cref="Files"/>.</summary>
    public static JsonArray Assets(string folder)
    {
        var assets = new JsonArray();
        foreach (var name in Files(folder))
        {
            var content = File.ReadAllBytes(Path.Combine(folder, name));
            assets.Add((JsonNode)new JsonObject
            {
                ["name"] = name,
                ["size"] = content.LongLength,
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(content)),
            });
        }
        return assets;
    }

    /// <summary>The bytes of partner.json for <paramref name="pack"/> in <paramref name="folder"/>: its "assets" filled in from the folder.</summary>
    public static byte[] Bytes(JsonObject pack, string folder)
    {
        pack["assets"] = Assets(folder);
        return V2pnPartnerSigning.Json(pack);
    }

    /// <summary>Writes partner.json of <paramref name="pack"/> into <paramref name="folder"/>, with "assets" filled in.</summary>
    public static byte[] Write(string folder, JsonObject pack)
    {
        var bytes = Bytes(pack, folder);
        File.WriteAllBytes(Path.Combine(folder, PackFile), bytes);
        return bytes;
    }

    /// <summary>partner.json of <paramref name="folder"/> as an object; null with the reason when it is missing or is not one.</summary>
    public static JsonObject? Read(string folder, out string error)
    {
        var file = Path.Combine(folder, PackFile);
        try
        {
            if (!File.Exists(file))
            {
                error = $"There is no {PackFile} in {folder}.";
                return null;
            }
            if (new FileInfo(file).Length > V2pnPartnerTrust.MaxPackBytes)
            {
                error = $"{PackFile} is larger than 64 KB.";
                return null;
            }
            if (JsonNode.Parse(File.ReadAllBytes(file)) is JsonObject pack)
            {
                error = string.Empty;
                return pack;
            }
            error = $"{PackFile} is not a JSON object.";
            return null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = $"{PackFile}: {ex.Message}";
            return null;
        }
    }
}

/// <summary>listing.json of partners/&lt;id&gt;, as the owner of V2pn writes it.</summary>
/// <param name="Contact">How the owner reaches the provider; never published.</param>
public sealed record V2pnPartnerSource(string Id, string Name, IReadOnlyList<string> Domains, IReadOnlyList<string> Grants, DateOnly Until, string? Contact);

/// <summary>One partner ready to publish: its listing as the index will carry it, its zip, and what the owner should know.</summary>
public sealed record V2pnPublishedPartner(V2pnPartnerListing Listing, byte[] Zip, IReadOnlyList<string> Notes, string? Contact);

/// <summary>Everything a publication writes: partners.json (to be signed as these bytes) and one zip per partner.</summary>
public sealed record V2pnPublication(byte[] Index, IReadOnlyList<V2pnPublishedPartner> Partners, IReadOnlyList<string> Problems)
{
    public bool Ok => Problems.Count == 0;
}

/// <summary>
/// Builds the published form of partners/ (tools/partner.cs publish): each partners/&lt;id&gt; folder (listing.json,
/// partner.json and its files) is checked with the reader the app uses, packed into partner-&lt;id&gt;.zip, and listed in
/// partners.json with its domains, grants, term, reviewed hashes and the zip's size and SHA-256.
/// </summary>
public static class V2pnPartnerPublisher
{
    public const string ListingFile = "listing.json";
    public const int MaxContact = 200;

    // The same date in every zip, so a pack whose files did not change keeps its hash and nobody downloads it again.
    private static readonly DateTimeOffset ZipTime = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>listing.json of <paramref name="folder"/>; null with the reason when it breaks a rule.</summary>
    public static V2pnPartnerSource? ReadListing(string folder, out string error)
    {
        var file = Path.Combine(folder, ListingFile);
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length > 16 * 1024)
            {
                error = $"{ListingFile} is missing or larger than 16 KB.";
                return null;
            }
            if (JsonNode.Parse(File.ReadAllBytes(file)) is not JsonObject root)
            {
                error = $"{ListingFile} is not a JSON object.";
                return null;
            }
            string? Text(string key) => root[key] is JsonValue v && v.TryGetValue<string>(out var t) ? t.Trim() : null;
            List<string> List(string key) => root[key] is JsonArray a
                ? a.Select(n => n is JsonValue v && v.TryGetValue<string>(out var t) ? t.Trim() : null).OfType<string>().Where(t => t.Length > 0).ToList()
                : [];

            var id = Text("id");
            var name = Text("name");
            var domains = List("domains").Select(d => d.TrimEnd('.').ToLowerInvariant()).Distinct().ToList();
            var grants = List("grants").Distinct().ToList();
            var contact = Text("contact");
            if (!V2pnPartnerTrust.ValidId(id) || id != Path.GetFileName(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)))
            {
                error = $"{ListingFile}: id must be the folder's name, 2 to 32 lowercase letters, digits and dashes.";
                return null;
            }
            if (V2pnPartnerTrust.ValidName(name) != name)
            {
                error = $"{ListingFile}: name needs 1 to 40 characters, no control characters, no \"V2pn\".";
                return null;
            }
            // A published partner is never served from this computer, whatever build the tool runs in.
            if (domains.Count == 0)
            {
                error = $"{ListingFile}: domains needs at least one subscription domain.";
                return null;
            }
            if (domains.FirstOrDefault(d => d is "127.0.0.1" or "localhost" || !V2pnPartnerTrust.ValidDomain(d)) is { } badDomain)
            {
                error = $"{ListingFile}: \"{badDomain}\" is not a domain (example.com or *.example.com).";
                return null;
            }
            if (grants.FirstOrDefault(g => !V2pnPartnerGrants.All.Contains(g)) is { } badGrant)
            {
                error = $"{ListingFile}: \"{badGrant}\" is not a grant; known: {string.Join(", ", V2pnPartnerGrants.All)}.";
                return null;
            }
            if (!DateOnly.TryParseExact(Text("until"), "yyyy-MM-dd", out var until))
            {
                error = $"{ListingFile}: until is not a yyyy-MM-dd date.";
                return null;
            }
            if (contact != null && (contact.Length > MaxContact || contact.Any(char.IsControl)))
            {
                error = $"{ListingFile}: contact takes up to {MaxContact} characters on one line.";
                return null;
            }
            error = string.Empty;
            return new V2pnPartnerSource(id!, name!, domains, grants, until, contact);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = $"{ListingFile}: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// Everything under <paramref name="partnersDir"/> (each folder with a listing.json is a partner), checked and packed.
    /// Any problem stops the whole publication: an index is never half right.
    /// </summary>
    public static V2pnPublication Publish(string partnersDir, DateOnly today, DateTimeOffset published)
    {
        var partners = new List<V2pnPublishedPartner>();
        var problems = new List<string>();
        foreach (var folder in Directory.EnumerateDirectories(partnersDir).Where(d => File.Exists(Path.Combine(d, ListingFile))).Order(StringComparer.Ordinal))
        {
            if (Build(folder, today, out var error) is { } partner)
            {
                partners.Add(partner);
            }
            else
            {
                problems.Add($"{Path.GetFileName(folder)}: {error}");
            }
        }
        foreach (var domain in partners.SelectMany(p => p.Listing.Domains.Select(d => (d, p.Listing.Id))).GroupBy(x => x.d).Where(g => g.Count() > 1))
        {
            problems.Add($"{domain.Key} is listed by {string.Join(" and ", domain.Select(x => x.Id))}: a subscription would belong to both.");
        }
        return new V2pnPublication(IndexBytes(partners.Select(p => p.Listing), published), partners, problems);
    }

    /// <summary>One partners/&lt;id&gt; folder checked and packed; null with the reason when the app would refuse any of it.</summary>
    public static V2pnPublishedPartner? Build(string folder, DateOnly today, out string error)
    {
        var source = ReadListing(folder, out error);
        if (source == null)
        {
            return null;
        }
        if (source.Until <= today)
        {
            error = $"{ListingFile}: until {source.Until:yyyy-MM-dd} is not after today.";
            return null;
        }
        if (V2pnPackWriter.Read(folder, out error) is not { } packJson)
        {
            return null;
        }
        var stray = Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>()
            .FirstOrDefault(n => n != ListingFile && n != V2pnPartnerTrust.PackFile && (!V2pnPartnerTrust.SafeFileName(n)
                || !(V2pnPackWriter.IsPicture(n) || V2pnReviewedFiles.All.Contains(n))));
        if (stray != null)
        {
            error = $"{stray} is not a picture (png, webp, jpg) or a reviewed file with a plain name; take it out of the folder.";
            return null;
        }
        // The pack must list exactly the folder's files: what the owner looked at is what goes out.
        var expected = V2pnPackWriter.Assets(folder).ToJsonString();
        if ((packJson["assets"] ?? new JsonArray()).ToJsonString() != expected)
        {
            error = $"partner.json's assets are not the folder's files; run: dotnet run tools/partner.cs -- pack {folder}";
            return null;
        }

        var notes = new List<string>();
        // Every partner dresses the window; the rest is granted one by one.
        var grants = source.Grants.Append(V2pnPartnerGrants.Edition).Distinct().OrderBy(g => V2pnPartnerGrants.All.ToList().IndexOf(g)).ToList();
        var reviewed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in V2pnReviewedFiles.All)
        {
            var grant = V2pnReviewedFiles.GrantOf(file);
            var path = Path.Combine(folder, file);
            if (!File.Exists(path))
            {
                if (grants.Contains(grant))
                {
                    notes.Add($"the {grant} grant does nothing: there is no {file}");
                }
                continue;
            }
            if (!grants.Contains(grant))
            {
                notes.Add($"{file} is not used: {ListingFile} has no {grant} grant (add it once you have read the file: partner.cs review)");
                continue;
            }
            var content = File.ReadAllBytes(path);
            if (ReviewProblem(file, content) is { } problem)
            {
                error = $"{file} does not pass V2pn's checks: {problem}";
                return null;
            }
            reviewed[file] = V2pnReviewedFiles.Hash(content);
        }

        var files = new List<(string Name, byte[] Content)> { (V2pnPartnerTrust.PackFile, File.ReadAllBytes(Path.Combine(folder, V2pnPartnerTrust.PackFile))) };
        files.AddRange(V2pnPackWriter.Files(folder).Select(n => (n, File.ReadAllBytes(Path.Combine(folder, n)))));
        var zip = Zip(files);
        var listing = new V2pnPartnerListing(source.Id, source.Name, source.Domains, grants, source.Until,
            new V2pnPartnerZip(V2pnPartnerTrust.ZipName(source.Id), zip.LongLength, Convert.ToHexStringLower(SHA256.HashData(zip))), reviewed);

        // The app's own reader, on the very bytes that go out.
        var pack = V2pnPartnerTrust.Unpack(zip, listing, out var unpacked, out error);
        if (pack == null)
        {
            return null;
        }
        V2pnPartnerSlots.Fit(pack, name => unpacked.GetValueOrDefault(name), out var slotNotes);
        notes.AddRange(slotNotes.Select(n => $"picture not shown: {n}"));
        notes.AddRange(pack.Notes);
        notes.AddRange((pack.Theme?.Notes ?? []).Select(n => $"theme: {n}"));
        var features = V2pnPartnerFeatures.Read(listing, pack, name => unpacked.GetValueOrDefault(name));
        notes.AddRange(features.Files.Where(f => f.State != V2pnReviewState.Ready).Select(f => $"{f.Name} is not used: {f.Problem}"));
        error = string.Empty;
        return new V2pnPublishedPartner(listing, zip, notes, source.Contact);
    }

    /// <summary>partners.json for <paramref name="listings"/>, by id; the bytes the root key signs.</summary>
    public static byte[] IndexBytes(IEnumerable<V2pnPartnerListing> listings, DateTimeOffset published)
    {
        var partners = new JsonArray();
        foreach (var listing in listings.OrderBy(l => l.Id, StringComparer.Ordinal))
        {
            var item = new JsonObject
            {
                ["id"] = listing.Id,
                ["name"] = listing.Name,
                ["domains"] = new JsonArray(listing.Domains.Select(d => (JsonNode)JsonValue.Create(d)).ToArray()),
                ["grants"] = new JsonArray(listing.Grants.Select(g => (JsonNode)JsonValue.Create(g)).ToArray()),
            };
            if (listing.ReviewedFiles.Count > 0)
            {
                var reviewed = new JsonObject();
                foreach (var file in V2pnReviewedFiles.All.Where(listing.ReviewedFiles.ContainsKey))
                {
                    reviewed[file] = listing.ReviewedFiles[file];
                }
                item["reviewed"] = reviewed;
            }
            item["until"] = listing.Until.ToString("yyyy-MM-dd");
            item["pack"] = new JsonObject
            {
                ["file"] = listing.Zip.File,
                ["size"] = listing.Zip.Size,
                ["sha256"] = listing.Zip.Sha256,
            };
            partners.Add((JsonNode)item);
        }
        return V2pnPartnerSigning.Json(new JsonObject
        {
            ["schema"] = 1,
            ["published"] = published.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
            ["partners"] = partners,
        });
    }

    /// <summary>A flat zip of <paramref name="files"/>, by name, with one fixed date: the same files always give the same bytes.</summary>
    public static byte[] Zip(IEnumerable<(string Name, byte[] Content)> files)
    {
        using var stream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files.OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
                entry.LastWriteTime = ZipTime;
                using var write = entry.Open();
                write.Write(content);
            }
        }
        return stream.ToArray();
    }

    /// <summary>Why V2pn would not use a reviewed file, or null when it passes its checks.</summary>
    public static string? ReviewProblem(string name, byte[] content)
    {
        string problem;
        var ok = name switch
        {
            V2pnReviewedFiles.Routing => V2pnRoutingPreset.Parse(content, out problem) != null,
            V2pnReviewedFiles.Transport => V2pnTransport.Parse(content, out problem) != null,
            _ => V2pnCoreFragment.Parse(content, out problem) != null,
        };
        return ok ? null : problem;
    }
}
