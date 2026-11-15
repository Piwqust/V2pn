// Keys, certificates and packs of V2pn partners (providers that dress V2pn in their brand).
//
//   dotnet run tools/partner.cs -- new-key <pem file>
//       A new ECDSA P-256 key; prints its public half. For the root key (~/.v2pn/partner-root-key.pem,
//       its public half goes into V2pnPartnerKeys in v2rayN/Directory.Build.targets) and for a partner's own key.
//
//   dotnet run tools/partner.cs -- cert --partner <id> --name <name> --domains <a,b> --key <partner public key>
//                                       [--grants edition,account] [--days 365] [--out <folder>] [--root <pem>]
//       Writes cert.json and cert.json.sig into --out (default: the current folder), signed with the root key
//       (--root, else V2PN_PARTNER_ROOT_KEY with the PEM text, else ~/.v2pn/partner-root-key.pem), then checks
//       the signature against the keys built into the app.
//
//   dotnet run tools/partner.cs -- pack <folder> --key <partner pem>
//       Lists every picture in <folder> (png, webp, jpg) in partner.json with its size and SHA-256, and signs
//       partner.json with the partner's key into partner.json.sig. The folder is what the provider serves at
//       the address of its v2pn-partner header.
//
//   dotnet run tools/partner.cs -- revoke <partner>@<issued> [...] --out <folder> [--root <pem>]
//       Writes partners-revoked.json(.sig): certificates withdrawn before their term. Publish it with a release.
//
// Signatures are DER ECDSA P-256 / SHA-256 in base64, the same as release.json.sig.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

if (args.Length == 0)
{
    return Usage();
}

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var json = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

switch (args[0])
{
    case "new-key":
    {
        if (args.Length < 2)
        {
            return Usage();
        }
        if (File.Exists(args[1]))
        {
            Console.Error.WriteLine($"{args[1]} already exists; a key is never overwritten.");
            return 1;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
        using var fresh = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(args[1], fresh.ExportPkcs8PrivateKeyPem() + Environment.NewLine);
        Console.WriteLine(Convert.ToBase64String(fresh.ExportSubjectPublicKeyInfo()));
        return 0;
    }
    case "cert":
    {
        var partner = Option("--partner");
        var name = Option("--name");
        var domains = Option("--domains")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var key = Option("--key");
        if (partner == null || name == null || domains is not { Length: > 0 } || key == null)
        {
            return Usage();
        }
        if (!Regex.IsMatch(partner, "^[a-z0-9][a-z0-9-]{1,31}$"))
        {
            Console.Error.WriteLine("--partner: lowercase letters, digits and dashes, 2 to 32 characters.");
            return 2;
        }
        using (var check = ECDsa.Create())
        {
            check.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);
        }
        var grants = (Option("--grants") ?? "edition,account").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var days = int.Parse(Option("--days") ?? "365");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cert = new JsonObject
        {
            ["schema"] = 1,
            ["partner"] = partner,
            ["name"] = name,
            ["domains"] = new JsonArray(domains.Select(d => (JsonNode)d.ToLowerInvariant()).ToArray()),
            ["key"] = key,
            ["grants"] = new JsonArray(grants.Select(g => (JsonNode)g).ToArray()),
            ["issued"] = today.ToString("yyyy-MM-dd"),
            ["expires"] = today.AddDays(days).ToString("yyyy-MM-dd"),
        };
        var outDir = Path.GetFullPath(Option("--out") ?? ".");
        Directory.CreateDirectory(outDir);
        var bytes = Encoding.UTF8.GetBytes(cert.ToJsonString(json) + "\n");
        using var root = RootKey();
        if (root == null)
        {
            return 1;
        }
        var signature = Sign(root, bytes);
        File.WriteAllBytes(Path.Combine(outDir, "cert.json"), bytes);
        File.WriteAllText(Path.Combine(outDir, "cert.json.sig"), signature + "\n");
        Console.WriteLine($"wrote {Path.Combine(outDir, "cert.json")} (+ .sig), valid to {cert["expires"]}");
        return CheckAgainstBuiltInKeys(bytes, signature);
    }
    case "pack":
    {
        if (args.Length < 2 || Option("--key") is not { } keyFile)
        {
            return Usage();
        }
        var folder = Path.GetFullPath(args[1]);
        var packFile = Path.Combine(folder, "partner.json");
        if (JsonNode.Parse(File.ReadAllText(packFile)) is not JsonObject pack)
        {
            Console.Error.WriteLine($"{packFile} is not a JSON object.");
            return 1;
        }
        var assets = new JsonArray();
        foreach (var file in Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is not (".png" or ".webp" or ".jpg" or ".jpeg"))
            {
                continue;
            }
            var content = File.ReadAllBytes(file);
            assets.Add((JsonNode)new JsonObject
            {
                ["name"] = Path.GetFileName(file),
                ["size"] = content.LongLength,
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(content)),
            });
        }
        pack["assets"] = assets;
        var bytes = Encoding.UTF8.GetBytes(pack.ToJsonString(json) + "\n");
        using var key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(keyFile));
        File.WriteAllBytes(packFile, bytes);
        File.WriteAllText(packFile + ".sig", Sign(key, bytes) + "\n");
        Console.WriteLine($"signed {packFile} with {assets.Count} picture(s)");
        return 0;
    }
    case "revoke":
    {
        var entries = args.Skip(1).TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        var outDir = Path.GetFullPath(Option("--out") ?? ".");
        Directory.CreateDirectory(outDir);
        var list = new JsonObject { ["revoked"] = new JsonArray(entries.Select(e => (JsonNode)e).ToArray()) };
        var bytes = Encoding.UTF8.GetBytes(list.ToJsonString(json) + "\n");
        using var root = RootKey();
        if (root == null)
        {
            return 1;
        }
        var signature = Sign(root, bytes);
        File.WriteAllBytes(Path.Combine(outDir, "partners-revoked.json"), bytes);
        File.WriteAllText(Path.Combine(outDir, "partners-revoked.json.sig"), signature + "\n");
        Console.WriteLine($"wrote partners-revoked.json with {entries.Length} entr{(entries.Length == 1 ? "y" : "ies")}");
        return CheckAgainstBuiltInKeys(bytes, signature);
    }
    default:
        return Usage();
}

ECDsa? RootKey()
{
    var pem = Option("--root") is { } file ? File.ReadAllText(file)
        : Environment.GetEnvironmentVariable("V2PN_PARTNER_ROOT_KEY") is { Length: > 0 } text ? text
        : File.Exists(Path.Combine(home, ".v2pn", "partner-root-key.pem")) ? File.ReadAllText(Path.Combine(home, ".v2pn", "partner-root-key.pem"))
        : null;
    if (pem == null)
    {
        Console.Error.WriteLine("No root key: pass --root, set V2PN_PARTNER_ROOT_KEY or create ~/.v2pn/partner-root-key.pem with new-key.");
        return null;
    }
    var key = ECDsa.Create();
    key.ImportFromPem(pem);
    return key;
}

static string Sign(ECDsa key, byte[] data) =>
    Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

int CheckAgainstBuiltInKeys(byte[] data, string signature)
{
    var targets = Path.Combine(Directory.GetCurrentDirectory(), "v2rayN", "Directory.Build.targets");
    if (!File.Exists(targets))
    {
        Console.WriteLine("(run from the repository root to check the signature against the keys built into the app)");
        return 0;
    }
    var match = Regex.Match(File.ReadAllText(targets), "<V2pnPartnerKeys[^>]*>([^<]*)</V2pnPartnerKeys>");
    var keys = match.Success ? match.Groups[1].Value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
    foreach (var pub in keys)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(pub), out _);
            if (ecdsa.VerifyData(data, Convert.FromBase64String(signature), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            {
                Console.WriteLine("checked: the app's built-in partner keys accept this signature");
                return 0;
            }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
        }
    }
    Console.Error.WriteLine("WARNING: no key in V2pnPartnerKeys accepts this signature; installed apps will refuse it.");
    return 3;
}

static int Usage()
{
    Console.Error.WriteLine("""
        usage:
          dotnet run tools/partner.cs -- new-key <pem file>
          dotnet run tools/partner.cs -- cert --partner <id> --name <name> --domains <a,b> --key <public key> [--grants edition,account] [--days 365] [--out <folder>] [--root <pem>]
          dotnet run tools/partner.cs -- pack <folder> --key <partner pem>
          dotnet run tools/partner.cs -- revoke <partner>@<issued> [...] --out <folder> [--root <pem>]
        """);
    return 2;
}
