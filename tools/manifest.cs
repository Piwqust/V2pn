// Writes and signs the manifest of a V2pn release.
//
//   dotnet run tools/manifest.cs -- <folder> <version> [--repo <repository root>] [--key <pem file>]
//
// <folder> holds the packages of one release (V2pn-windows-x64.zip, V2pn-macos-arm64.zip, ...).
// The tool writes into it:
//   release.json        version + name, size and SHA-256 of every package
//   release.json.sig    ECDSA P-256 / SHA-256 signature of release.json, DER, base64
//   <package>.sha256    for people who check a download by hand (sha256sum -c)
//
// The private key comes from --key, else the environment variable V2PN_SIGNING_KEY (the PEM
// text itself, which is how CI passes a secret), else ~/.v2pn/release-signing-key.pem.
// Before finishing, the signature is checked against the public keys built into the app
// (V2pnUpdateKeys in v2rayN/Directory.Build.targets): a release signed with a key the installed
// apps do not know would be refused by every one of them.
//
// A new key pair (for the first setup, or to retire a key):
//
//   dotnet run tools/manifest.cs -- --new-key <pem file to write>
//
// prints the public key to add to V2pnUpdateKeys. Keep the old public key there too until every
// installed copy has updated to a version that knows the new one.
//
// `openssl dgst -sha256 -verify pub.pem -signature <(base64 -d release.json.sig) release.json`
// checks the same signature without this tool.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

if (args.Length == 2 && args[0] == "--new-key")
{
    if (File.Exists(args[1]))
    {
        Console.Error.WriteLine($"{args[1]} already exists; a key is never overwritten.");
        return 1;
    }
    using var fresh = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    File.WriteAllText(args[1], fresh.ExportPkcs8PrivateKeyPem() + Environment.NewLine);
    Console.WriteLine(Convert.ToBase64String(fresh.ExportSubjectPublicKeyInfo()));
    return 0;
}

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: dotnet run tools/manifest.cs -- <folder> <version> [--repo <root>] [--key <pem file>]");
    return 2;
}

var folder = Path.GetFullPath(args[0]);
var version = args[1].TrimStart('v');
string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
var repo = Path.GetFullPath(Option("--repo") ?? Directory.GetCurrentDirectory());

if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$"))
{
    Console.Error.WriteLine($"'{version}' is not a version like 1.2.3 or 1.2.3-beta.1");
    return 2;
}

// A partner's build (V2pn-<id>-windows-x64.zip, tools/partner-packages.cs) is never listed: installed copies update
// from the standard packages, and a partner's build keeps its marker through that update.
var partnerPackage = new Regex("^V2pn-(?!(windows|macos|linux)-)[a-z0-9][a-z0-9-]{1,31}-(windows|macos)-", RegexOptions.CultureInvariant);
var packages = Directory.GetFiles(folder, "V2pn-*")
    .Where(f => !f.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) && !partnerPackage.IsMatch(Path.GetFileName(f)))
    .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
    .ToList();
if (packages.Count == 0)
{
    Console.Error.WriteLine($"No V2pn-* packages in {folder}");
    return 1;
}

var files = new JsonArray();
foreach (var package in packages)
{
    var name = Path.GetFileName(package);
    string hash;
    using (var stream = File.OpenRead(package))
    {
        hash = Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    var size = new FileInfo(package).Length;
    files.Add((JsonNode)new JsonObject { ["name"] = name, ["size"] = size, ["sha256"] = hash });
    // sha256sum format.
    File.WriteAllText(package + ".sha256", $"{hash}  {name}\n");
    Console.WriteLine($"{hash}  {name}  {size / 1024.0 / 1024.0:F1} MB");
}

var manifest = new JsonObject
{
    ["version"] = version,
    ["published"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
    ["files"] = files,
};
var json = Encoding.UTF8.GetBytes(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n");

string pem;
if (Option("--key") is { } keyFile)
{
    pem = File.ReadAllText(keyFile);
}
else if (Environment.GetEnvironmentVariable("V2PN_SIGNING_KEY") is { Length: > 0 } fromEnvironment)
{
    pem = fromEnvironment;
}
else
{
    var home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".v2pn", "release-signing-key.pem");
    if (!File.Exists(home))
    {
        Console.Error.WriteLine($"No signing key: pass --key, set V2PN_SIGNING_KEY, or put the key at {home}");
        return 1;
    }
    pem = File.ReadAllText(home);
}

using var key = ECDsa.Create();
key.ImportFromPem(pem);
var signature = key.SignData(json, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

var targets = Path.Combine(repo, "v2rayN", "Directory.Build.targets");
if (!File.Exists(targets))
{
    Console.Error.WriteLine($"{targets} not found; pass --repo <repository root>");
    return 1;
}
// --public replaces the built-in keys; only tools/update-e2e.ps1 uses it, for packages built to trust a test key.
var builtIn = (Option("--public") ?? Regex.Match(File.ReadAllText(targets), @"<V2pnUpdateKeys[^>]*>([^<]+)</V2pnUpdateKeys>").Groups[1].Value)
    .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var known = builtIn.Any(candidate =>
{
    try
    {
        using var publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String(candidate), out _);
        return publicKey.VerifyData(json, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }
    catch (Exception ex) when (ex is CryptographicException or FormatException)
    {
        return false;
    }
});
if (!known)
{
    Console.Error.WriteLine("The signing key does not match any key in V2pnUpdateKeys (v2rayN/Directory.Build.targets).");
    Console.Error.WriteLine("Installed V2pn would refuse this release. Nothing was written.");
    return 1;
}

File.WriteAllBytes(Path.Combine(folder, "release.json"), json);
File.WriteAllText(Path.Combine(folder, "release.json.sig"), Convert.ToBase64String(signature) + "\n");
Console.WriteLine($"release.json for V2pn {version}: {packages.Count} package(s), signed.");
return 0;
