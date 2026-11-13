namespace ServiceLib;

/// <summary>One package of a release: its file name, size in bytes and SHA-256 in lower-case hex.</summary>
public sealed record V2pnManifestFile(string Name, long Size, string Sha256);

/// <summary>
/// release.json: the version of a release and the packages it consists of. It is signed
/// (release.json.sig, ECDSA P-256 over the file's bytes) with a key whose public half is built
/// into the app, so a package is trusted because of that signature and not because of where it
/// was downloaded from. A mirror, a proxy on the way or a program that drops files into the
/// update folder cannot make V2pn install something the release key did not sign.
/// </summary>
public sealed class V2pnManifest
{
    /// <summary>A manifest lists a handful of files; anything bigger is not one.</summary>
    public const int MaxBytes = 256 * 1024;

    public string Version { get; }

    public IReadOnlyList<V2pnManifestFile> Files { get; }

    private V2pnManifest(string version, IReadOnlyList<V2pnManifestFile> files)
    {
        Version = version;
        Files = files;
    }

    public V2pnManifestFile? File(string? name) => Files.FirstOrDefault(f => f.Name == name);

    /// <summary>
    /// Returns the manifest when the signature is valid for one of the keys and the content is
    /// well formed; otherwise null and the reason.
    /// </summary>
    public static V2pnManifest? ReadVerified(byte[]? json, string? signature, IEnumerable<string> publicKeys, out string error)
    {
        if (json is not { Length: > 0 and <= MaxBytes })
        {
            error = "The release manifest is missing.";
            return null;
        }
        if (!VerifySignature(json, signature, publicKeys))
        {
            error = "The release manifest is not signed with V2pn's release key.";
            return null;
        }
        var manifest = Parse(json);
        error = manifest == null ? "The release manifest is malformed." : string.Empty;
        return manifest;
    }

    public static bool VerifySignature(byte[] data, string? signature, IEnumerable<string> publicKeys)
    {
        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String((signature ?? string.Empty).Trim());
        }
        catch (FormatException)
        {
            return false;
        }
        if (signatureBytes.Length == 0)
        {
            return false;
        }
        foreach (var key in publicKeys)
        {
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);
                // DER, as written by tools/sign and by `openssl dgst -sha256 -sign`.
                if (ecdsa.VerifyData(data, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                // A key that does not parse verifies nothing; the next one may.
            }
        }
        return false;
    }

    private static V2pnManifest? Parse(byte[] json)
    {
        try
        {
            var root = JsonNode.Parse(json) as JsonObject;
            var version = root?["version"]?.GetValue<string>();
            if (version.IsNullOrEmpty() || root?["files"] is not JsonArray array)
            {
                return null;
            }
            // A version that does not parse compares as 0.0.0 and would never be offered; say so here.
            if (new SemanticVersion(version).ToStandardVersionString() != version)
            {
                return null;
            }
            var files = new List<V2pnManifestFile>();
            foreach (var node in array)
            {
                var name = node?["name"]?.GetValue<string>();
                var size = node?["size"]?.GetValue<long>() ?? -1;
                var hash = node?["sha256"]?.GetValue<string>()?.ToLowerInvariant();
                // The name becomes part of a URL and of a file path: no folders, nothing but a plain file name.
                if (name.IsNullOrEmpty() || name != Path.GetFileName(name) || name.Contains("..")
                    || size <= 0 || hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit))
                {
                    return null;
                }
                files.Add(new V2pnManifestFile(name, size, hash));
            }
            return files.Count == 0 ? null : new V2pnManifest(version, files);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
