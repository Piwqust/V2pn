using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace AmazTool;

/// <summary>
/// V2pn: AmazTool checks the package itself, against the signed manifest V2pn keeps beside it, while it holds the file
/// open so that nobody can write to it. V2pn checked the package before starting AmazTool, but AmazTool opened it again
/// up to half a minute later and, when V2pn ran as administrator, with its rights: a program of the same user could put
/// another zip in its place in between.
/// </summary>
internal static class PackageCheck
{
    private const int MaxManifestBytes = 256 * 1024;

    /// <summary>The release keys built into this AmazTool ("|" between several).</summary>
    public static IReadOnlyList<string> Keys { get; } = (typeof(PackageCheck).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "V2pnUpdateKeys")?.Value ?? string.Empty)
        .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Null when <paramref name="zip"/> (the open package at <paramref name="zipPath"/>) is the package of
    /// <paramref name="version"/> that the manifest beside it lists and the release key signed; otherwise why not.
    /// </summary>
    public static string? Verify(Stream zip, string zipPath, string version, IReadOnlyList<string> keys)
    {
        var manifestPath = Path.ChangeExtension(zipPath, ".json");
        var signaturePath = manifestPath + ".sig";
        if (!File.Exists(manifestPath) || !File.Exists(signaturePath) || new FileInfo(manifestPath).Length > MaxManifestBytes)
        {
            return "There is no signed manifest beside the package.";
        }
        var json = File.ReadAllBytes(manifestPath);
        if (!SignedByAny(json, File.ReadAllText(signaturePath), keys))
        {
            return "The manifest beside the package is not signed with V2pn's release key.";
        }
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["version"]?.GetValue<string>() != version || root["files"] is not JsonArray files)
            {
                return $"The signed manifest is not the one of version {version}.";
            }
            zip.Position = 0;
            var size = zip.Length;
            var hash = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
            zip.Position = 0;
            var listed = files.OfType<JsonObject>().Any(f =>
                f["size"]?.GetValue<long>() == size
                && string.Equals(f["sha256"]?.GetValue<string>(), hash, StringComparison.OrdinalIgnoreCase));
            return listed ? null : "The package is not the one the signed manifest lists.";
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return "The signed manifest is malformed.";
        }
    }

    private static bool SignedByAny(byte[] data, string signature, IReadOnlyList<string> keys)
    {
        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(signature.Trim());
        }
        catch (FormatException)
        {
            return false;
        }
        foreach (var key in keys)
        {
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);
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
}
