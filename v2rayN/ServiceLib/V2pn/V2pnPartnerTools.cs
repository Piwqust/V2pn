namespace ServiceLib;

/// <summary>
/// Keys and signatures of the partner platform, shared by tools/partner.cs and V2pn's theme builder, so a pack signed in
/// the app and one signed by the script are the same bytes with the same kind of signature.
/// Signatures are DER ECDSA P-256 / SHA-256 in base64, the same as release.json.sig.
/// </summary>
public static class V2pnPartnerSigning
{
    public static string Sign(ECDsa key, byte[] data) =>
        Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

    public static bool Verify(string publicKey, byte[] data, string? signature) => V2pnManifest.VerifySignature(data, signature, [publicKey]);

    /// <summary>The public half as base64 SubjectPublicKeyInfo: what a certificate names as "key".</summary>
    public static string PublicKey(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    /// <summary>A new P-256 key: the private half as PKCS#8 PEM (to keep secret) and the public half.</summary>
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

    /// <summary>A JSON document as tools/partner.cs has always written it: indented, Cyrillic as is, a newline at the end.</summary>
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

    /// <summary>One line of compact JSON: what a request's signature covers.</summary>
    public static byte[] Compact(JsonNode node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            node.WriteTo(writer);
        }
        return stream.ToArray();
    }
}

/// <summary>
/// The pack folder a provider serves: partner.json with every picture and reviewed file of the folder in "assets" (size and
/// SHA-256), and partner.json.sig. tools/partner.cs pack and the theme builder write it through here.
/// </summary>
public static class V2pnPackWriter
{
    public const string PackFile = "partner.json";
    public const string SignatureFile = "partner.json.sig";

    private static readonly string[] PictureExtensions = [".png", ".webp", ".jpg", ".jpeg"];

    public static bool IsPicture(string name) => PictureExtensions.Contains(Path.GetExtension(name).ToLowerInvariant());

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

    /// <summary>
    /// Writes partner.json of <paramref name="pack"/> into <paramref name="folder"/>, with "assets" filled in, and signs it
    /// into partner.json.sig with <paramref name="key"/>. Without a key an old partner.json.sig is removed: it would not match.
    /// </summary>
    public static (byte[] Bytes, string? Signature) Write(string folder, JsonObject pack, ECDsa? key)
    {
        var bytes = Bytes(pack, folder);
        var signature = key == null ? null : V2pnPartnerSigning.Sign(key, bytes);
        File.WriteAllBytes(Path.Combine(folder, PackFile), bytes);
        var sigFile = Path.Combine(folder, SignatureFile);
        if (signature != null)
        {
            File.WriteAllText(sigFile, signature + "\n");
        }
        else if (File.Exists(sigFile))
        {
            File.Delete(sigFile);
        }
        return (bytes, signature);
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
            if (new FileInfo(file).Length > V2pnPartnerTrust.MaxPackBytes * 4L)
            {
                error = $"{PackFile} is far larger than 64 KB.";
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

/// <summary>cert.json as the owner of V2pn issues it (tools/partner.cs cert).</summary>
public static class V2pnPartnerIssuer
{
    /// <summary>
    /// The grants of a certificate: the ones asked for, actions (every partner has its buttons and server notes) and the
    /// grant of each reviewed file, once each, in V2pn's order. Null with the unknown grant when one is not V2pn's.
    /// </summary>
    public static IReadOnlyList<string>? Grants(IEnumerable<string> asked, IEnumerable<string> reviewedFiles, out string unknown)
    {
        var all = asked.Append(V2pnPartnerGrants.Actions).Concat(reviewedFiles.Select(V2pnReviewedFiles.GrantOf)).Distinct().ToList();
        unknown = all.FirstOrDefault(g => !V2pnPartnerGrants.All.Contains(g)) ?? string.Empty;
        return unknown.Length > 0 ? null : all.OrderBy(g => V2pnPartnerGrants.All.ToList().IndexOf(g)).ToList();
    }

    /// <summary>The bytes of cert.json, ready to be signed with the root key.</summary>
    public static byte[] Cert(string partner, string name, IEnumerable<string> domains, string key, IEnumerable<string> grants,
        IReadOnlyDictionary<string, string>? reviewed, DateOnly issued, int days)
    {
        var cert = new JsonObject
        {
            ["schema"] = 1,
            ["partner"] = partner,
            ["name"] = name,
            ["domains"] = new JsonArray(domains.Select(d => (JsonNode)JsonValue.Create(d.ToLowerInvariant())).ToArray()),
            ["key"] = key,
            ["grants"] = new JsonArray(grants.Select(g => (JsonNode)JsonValue.Create(g)).ToArray()),
            ["issued"] = issued.ToString("yyyy-MM-dd"),
            ["expires"] = issued.AddDays(days).ToString("yyyy-MM-dd"),
        };
        if (reviewed is { Count: > 0 })
        {
            var files = new JsonObject();
            foreach (var (file, hash) in reviewed)
            {
                files[file] = hash;
            }
            cert["reviewed"] = files;
        }
        return V2pnPartnerSigning.Json(cert);
    }
}

/// <summary>
/// request.json: what a provider asks the owner of V2pn to certify, signed with the provider's own key. The signature proves
/// the provider holds the private half of the key the certificate will name, and that nobody changed the request on the way.
/// </summary>
public sealed record V2pnCertRequest(
    string Partner,
    string Name,
    IReadOnlyList<string> Domains,
    string Key,
    IReadOnlyList<string> Grants,
    IReadOnlyDictionary<string, string> Reviewed,
    string? Contact,
    DateOnly Created);

/// <summary>
/// Writes and checks certificate requests. The signature ("signature") covers the request without that field, as one line
/// of compact JSON in UTF-8 (<see cref="V2pnPartnerSigning.Compact"/>), so the file may be reformatted on the way.
/// </summary>
public static class V2pnCertRequests
{
    public const string Kind = "v2pn-partner-request";
    public const int MaxBytes = 16 * 1024;
    public const int MaxContact = 200;

    /// <summary>The grants a provider may ask for (actions comes with every certificate; routing, transport and core with a reviewed file).</summary>
    public static readonly IReadOnlyList<string> Askable = [V2pnPartnerGrants.Edition, V2pnPartnerGrants.Account, V2pnPartnerGrants.Lock];

    /// <summary>Why <paramref name="request"/> cannot be certified, or null when it can. Checks the same rules as a certificate.</summary>
    public static string? Problem(V2pnCertRequest request)
    {
        if (!V2pnPartnerTrust.ValidId(request.Partner))
        {
            return "id: lowercase letters, digits and dashes, 2 to 32 characters.";
        }
        if (V2pnPartnerTrust.ValidName(request.Name) != request.Name)
        {
            return "name: 1 to 40 characters, no control characters, no \"V2pn\".";
        }
        if (request.Domains.Count == 0)
        {
            return "domains: at least one.";
        }
        if (request.Domains.FirstOrDefault(d => !V2pnPartnerTrust.ValidDomain(d)) is { } domain)
        {
            return $"domains: \"{domain}\" is not a domain (example.com or *.example.com).";
        }
        if (!V2pnPartnerTrust.ValidKey(request.Key))
        {
            return "key: not an ECDSA P-256 public key.";
        }
        if (request.Grants.FirstOrDefault(g => !V2pnPartnerGrants.All.Contains(g)) is { } grant)
        {
            return $"grants: \"{grant}\" is not a grant; known: {string.Join(", ", V2pnPartnerGrants.All)}.";
        }
        foreach (var (file, hash) in request.Reviewed)
        {
            if (!V2pnReviewedFiles.All.Contains(file))
            {
                return $"reviewed: \"{file}\" is not one of {string.Join(", ", V2pnReviewedFiles.All)}.";
            }
            if (hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit))
            {
                return $"reviewed: the hash of {file} is not a SHA-256.";
            }
        }
        if (request.Contact is { } contact && (contact.Length > MaxContact || contact.Any(char.IsControl)))
        {
            return $"contact: up to {MaxContact} characters on one line.";
        }
        return null;
    }

    /// <summary>
    /// request.json for the provider whose private key is <paramref name="key"/>: its public half goes into the request and the
    /// request is signed with it. The grants get actions and the grant of each reviewed file. Throws when the request breaks a rule.
    /// </summary>
    public static byte[] Create(string partner, string name, IEnumerable<string> domains, IEnumerable<string> grants,
        IReadOnlyDictionary<string, string> reviewed, string? contact, ECDsa key, DateOnly created)
    {
        var all = V2pnPartnerIssuer.Grants(grants, reviewed.Keys, out var unknown)
            ?? throw new ArgumentException($"grants: \"{unknown}\" is not a grant.");
        var request = new V2pnCertRequest(partner, name.Trim(), domains.Select(d => d.Trim().ToLowerInvariant()).Where(d => d.Length > 0).Distinct().ToList(),
            V2pnPartnerSigning.PublicKey(key), all, reviewed, contact.IsNullOrEmpty() ? null : contact.Trim(), created);
        if (Problem(request) is { } problem)
        {
            throw new ArgumentException(problem);
        }
        var root = Body(request);
        root["signature"] = V2pnPartnerSigning.Sign(key, V2pnPartnerSigning.Compact(root));
        return V2pnPartnerSigning.Json(root);
    }

    /// <summary>The request in <paramref name="json"/> when it is well formed and signed by the key it names; null with the reason otherwise.</summary>
    public static V2pnCertRequest? Read(byte[]? json, out string error)
    {
        if (json is not { Length: > 0 and <= MaxBytes })
        {
            error = "The request is empty or larger than 16 KB.";
            return null;
        }
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["schema"]?.GetValue<int>() != 1 || root["kind"]?.GetValue<string>() != Kind)
            {
                error = "This is not a V2pn partner certificate request.";
                return null;
            }
            var signature = root["signature"]?.GetValue<string>();
            root.Remove("signature");
            var key = root["key"]?.GetValue<string>();
            if (!V2pnPartnerTrust.ValidKey(key) || !V2pnPartnerSigning.Verify(key!, V2pnPartnerSigning.Compact(root), signature))
            {
                error = "The request is not signed by the key it names: it was changed, or signed with another key.";
                return null;
            }
            var reviewed = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root["reviewed"] is JsonObject files)
            {
                foreach (var (file, value) in files)
                {
                    reviewed[file] = value?.GetValue<string>()?.ToLowerInvariant() ?? string.Empty;
                }
            }
            if (!DateOnly.TryParseExact(root["created"]?.GetValue<string>(), "yyyy-MM-dd", out var created))
            {
                error = "The request has no date.";
                return null;
            }
            var request = new V2pnCertRequest(
                root["partner"]?.GetValue<string>() ?? string.Empty,
                root["name"]?.GetValue<string>() ?? string.Empty,
                Strings(root["domains"]),
                key!,
                Strings(root["grants"]),
                reviewed,
                root["contact"]?.GetValue<string>(),
                created);
            if (Problem(request) is { } problem)
            {
                error = "The request breaks a rule: " + problem;
                return null;
            }
            error = string.Empty;
            return request;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            error = "The request is malformed.";
            return null;
        }
    }

    private static JsonObject Body(V2pnCertRequest request)
    {
        var root = new JsonObject
        {
            ["schema"] = 1,
            ["kind"] = Kind,
            ["partner"] = request.Partner,
            ["name"] = request.Name,
            ["domains"] = new JsonArray(request.Domains.Select(d => (JsonNode)JsonValue.Create(d)).ToArray()),
            ["key"] = request.Key,
            ["grants"] = new JsonArray(request.Grants.Select(g => (JsonNode)JsonValue.Create(g)).ToArray()),
        };
        if (request.Reviewed.Count > 0)
        {
            var files = new JsonObject();
            foreach (var (file, hash) in request.Reviewed.OrderBy(r => V2pnReviewedFiles.All.ToList().IndexOf(r.Key)))
            {
                files[file] = hash;
            }
            root["reviewed"] = files;
        }
        if (request.Contact != null)
        {
            root["contact"] = request.Contact;
        }
        root["created"] = request.Created.ToString("yyyy-MM-dd");
        return root;
    }

    private static List<string> Strings(JsonNode? node) =>
        node is JsonArray array ? array.Select(n => n?.GetValue<string>()).OfType<string>().ToList() : [];
}
