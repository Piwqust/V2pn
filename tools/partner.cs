#:project ../v2rayN/ServiceLib/ServiceLib.csproj
// Keys, certificates and packs of V2pn partners (providers that dress V2pn in their brand).
//
//   dotnet run tools/partner.cs -- new-key <pem file>
//       A new ECDSA P-256 key; prints its public half. For the root key (~/.v2pn/partner-root-key.pem,
//       its public half goes into V2pnPartnerKeys in v2rayN/Directory.Build.targets) and for a partner's own key.
//
//   dotnet run tools/partner.cs -- cert --partner <id> --name <name> --domains <a,b> --key <partner public key>
//                                       [--grants edition,account,lock] [--review <file>]... [--days 365] [--out <folder>] [--root <pem>]
//       Writes cert.json and cert.json.sig into --out (default: the current folder), signed with the root key
//       (--root, else V2PN_PARTNER_ROOT_KEY with the PEM text, else ~/.v2pn/partner-root-key.pem), then checks
//       the signature against the keys built into the app. Grants: edition (the look), account (the API), actions
//       (buttons and server notes; every partner gets it), lock (closed settings; only after a review).
//       --review <routing.json|transport.json|core.json> (repeatable): a file read by hand; its SHA-256 goes into
//       "reviewed" and its grant (routing, transport, core) is added. V2pn uses the file only while it has this hash.
//
//   dotnet run tools/partner.cs -- review <pack folder>
//       What the reviewed files of a pack do, in plain words, for the manual review: every routing rule (sites and
//       addresses -> proxy, direct or block), every transport setting, every outbound, balancer and rule of core.json,
//       with block rules, DNS and the hosts the computer will connect to marked, and V2pn's own checks of each file.
//
//   dotnet run tools/partner.cs -- pack <folder> --key <partner pem>
//       Lists every picture in <folder> (png, webp, jpg) and the reviewed files (routing.json, transport.json, core.json)
//       in partner.json with its size and SHA-256, and signs
//       partner.json with the partner's key into partner.json.sig. Every other field (brand, edition, links, api,
//       servers, actions, lock) is signed as written; tools/partner-check.cs says what V2pn takes from it. The folder is what the provider serves at
//       the address of its v2pn-partner header.
//
//   dotnet run tools/partner.cs -- revoke [<partner>@<issued> ...] [--out <folder>] [--root <pem>]
//       Adds certificates withdrawn before their term to partners-revoked.json(.sig) in partners/ (or --out) and
//       signs the list again. tools/sign-release.ps1 and tools/publish-mirror.ps1 publish it with every release;
//       to withdraw one at once, upload it to the latest release as the command says.
//
// Signatures are DER ECDSA P-256 / SHA-256 in base64, the same as release.json.sig.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ServiceLib;

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
        string[] knownGrants = ["edition", "account", "actions", "lock", "routing", "transport", "core"];
        // Files read by hand: their hash binds the grant, so a file changed afterwards is not used.
        var reviewed = new JsonObject();
        var reviewGrants = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "--review")
            {
                continue;
            }
            var file = Path.GetFullPath(args[i + 1]);
            var fileName = Path.GetFileName(file);
            if (!V2pnReviewedFiles.All.Contains(fileName) || !File.Exists(file))
            {
                Console.Error.WriteLine($"--review: {args[i + 1]} is not an existing {string.Join(", ", V2pnReviewedFiles.All)}.");
                return 2;
            }
            var content = File.ReadAllBytes(file);
            var problem = ReviewProblem(fileName, content);
            if (problem != null)
            {
                Console.Error.WriteLine($"--review: {fileName} does not pass V2pn's checks: {problem}");
                return 2;
            }
            reviewed[fileName] = V2pnReviewedFiles.Hash(content);
            reviewGrants.Add(V2pnReviewedFiles.GrantOf(fileName));
            Console.WriteLine($"reviewed: {fileName} sha256 {reviewed[fileName]} ({content.Length} bytes) -> grant {V2pnReviewedFiles.GrantOf(fileName)}");
        }
        // actions is level 1 of the platform: every certified partner has its buttons and server notes.
        var grants = (Option("--grants") ?? "edition,account").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append("actions").Concat(reviewGrants).Distinct().OrderBy(g => Array.IndexOf(knownGrants, g)).ToArray();
        if (grants.FirstOrDefault(g => !knownGrants.Contains(g)) is { } unknownGrant)
        {
            Console.Error.WriteLine($"--grants: {unknownGrant} is not a grant; known: {string.Join(", ", knownGrants)}.");
            return 2;
        }
        if (grants.Contains("lock"))
        {
            Console.WriteLine("note: lock closes the subscription's settings for its holders; give it only after the review in docs/internal/PARTNER-PLATFORM.md.");
        }
        foreach (var grant in new[] { "routing", "transport", "core" }.Where(g => grants.Contains(g) && !reviewGrants.Contains(g)))
        {
            Console.WriteLine($"note: {grant} without --review of its file does nothing: V2pn uses only a file whose hash the certificate names.");
        }
        // V2pn up to 1.0.3 refused a certificate with a grant it did not know; later versions skip such grants.
        Console.WriteLine("note: V2pn 1.0.3 and older refuse certificates with actions, lock, routing, transport or core; the partner's look works there again after the update.");
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
        if (reviewed.Count > 0)
        {
            cert["reviewed"] = reviewed;
        }
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
            if (ext is not (".png" or ".webp" or ".jpg" or ".jpeg") && !V2pnReviewedFiles.All.Contains(Path.GetFileName(file)))
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
        Console.WriteLine($"signed {packFile} with {assets.Count} file(s)");
        foreach (var name in V2pnReviewedFiles.All.Where(n => File.Exists(Path.Combine(folder, n))))
        {
            Console.WriteLine($"  {name}: V2pn uses it only after a review; send it to V2pn's owner (dotnet run tools/partner.cs -- review {args[1]})");
        }
        return 0;
    }
    case "review":
    {
        if (args.Length < 2 || !Directory.Exists(args[1]))
        {
            return Usage();
        }
        return Review(Path.GetFullPath(args[1]));
    }
    case "revoke":
    {
        var added = args.Skip(1).TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (added.FirstOrDefault(e => !System.Text.RegularExpressions.Regex.IsMatch(e, @"^[a-z0-9][a-z0-9-]{0,31}@\d{4}-\d{2}-\d{2}$")) is { } bad)
        {
            Console.Error.WriteLine($"Not <partner>@<issued>: {bad} (the partner id and the date its certificate was issued, as in cert.json).");
            return 1;
        }
        var outDir = Path.GetFullPath(Option("--out") ?? Path.Combine(RepoRoot(), "partners"));
        Directory.CreateDirectory(outDir);
        // The list only grows: a withdrawn certificate never comes back (a new one is issued instead).
        var listFile = Path.Combine(outDir, "partners-revoked.json");
        var kept = File.Exists(listFile)
            ? (JsonNode.Parse(File.ReadAllText(listFile))?["revoked"] as JsonArray)?.Select(n => n?.GetValue<string>()).OfType<string>().ToList() ?? []
            : [];
        var entries = kept.Concat(added).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
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
        Console.WriteLine($"wrote {listFile} with {entries.Length} entr{(entries.Length == 1 ? "y" : "ies")}");
        Console.WriteLine("It goes out with the next release. To withdraw now, add it to the latest release and the mirror:");
        Console.WriteLine($"  gh release upload <latest tag> --repo Piwqust/V2pn --clobber \"{listFile}\" \"{listFile}.sig\"");
        return CheckAgainstBuiltInKeys(bytes, signature);
    }
    default:
        return Usage();
}

// Why V2pn would not use a reviewed file, or null when it passes its checks.
static string? ReviewProblem(string name, byte[] content)
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

// The checklist of docs/internal/PARTNER-PLATFORM.md ("Ручная проверка"), filled in from the files.
static int Review(string folder)
{
    var found = false;
    var bad = false;
    foreach (var name in V2pnReviewedFiles.All)
    {
        var path = Path.Combine(folder, name);
        if (!File.Exists(path))
        {
            continue;
        }
        found = true;
        var content = File.ReadAllBytes(path);
        Console.WriteLine();
        Console.WriteLine($"== {name}  sha256 {V2pnReviewedFiles.Hash(content)}  {content.Length} bytes  (grant: {V2pnReviewedFiles.GrantOf(name)})");
        if (content.Length > V2pnReviewedFiles.MaxBytes)
        {
            Console.WriteLine($"  PROBLEM: larger than {V2pnReviewedFiles.MaxBytes / 1024} KB; V2pn refuses the pack");
            bad = true;
            continue;
        }
        if (ReviewProblem(name, content) is { } problem)
        {
            Console.WriteLine($"  PROBLEM: V2pn would not use it: {problem}");
            bad = true;
            continue;
        }
        switch (name)
        {
            case V2pnReviewedFiles.Routing:
                ReviewRouting(V2pnRoutingPreset.Parse(content, out _)!);
                break;
            case V2pnReviewedFiles.Transport:
                ReviewTransport(V2pnTransport.Parse(content, out _)!, content);
                break;
            default:
                ReviewCore(V2pnCoreFragment.Parse(content, out _)!);
                break;
        }
        Console.WriteLine("  checks: OK, V2pn's format and filter accept it");
    }
    if (!found)
    {
        Console.WriteLine($"No routing.json, transport.json or core.json in {folder}.");
        return 1;
    }
    Console.WriteLine();
    Console.WriteLine("If it is fine, issue the certificate with the hashes above:");
    var reviews = string.Join(" ", V2pnReviewedFiles.All.Where(n => File.Exists(Path.Combine(folder, n))).Select(n => $"--review \"{Path.Combine(folder, n)}\""));
    Console.WriteLine($"  dotnet run tools/partner.cs -- cert --partner <id> --name <name> --domains <...> --key <...> --grants <...> {reviews}");
    Console.WriteLine("The right is bound to these exact files; any change needs a new review and a new certificate. Review again every year.");
    return bad ? 1 : 0;
}

static string Goes(string to) => to switch
{
    "proxy" => "PROXY (through the provider's server)",
    "direct" => "DIRECT (past the VPN, the person's own address)",
    "block" => "BLOCK (never opens)",
    _ => $"-> {to}",
};

static void Entries(IEnumerable<string> entries, string to)
{
    foreach (var entry in entries)
    {
        var marks = new List<string>();
        if (V2pnRouteEntries.IsGeo(entry))
        {
            marks.Add("a whole list from the geo files: look it up");
        }
        if (V2pnRouteEntries.AffectsDns(entry))
        {
            marks.Add("!! DNS: name lookups of the person go this way");
        }
        if (to == "block")
        {
            marks.Add("!! BLOCKED");
        }
        Console.WriteLine($"      {entry}{(marks.Count > 0 ? "   [" + string.Join("; ", marks) + "]" : "")}");
    }
}

static void ReviewRouting(V2pnRoutingPreset preset)
{
    Console.WriteLine($"  A mode of \"What goes through the VPN\" the person may pick: \"{preset.Name ?? "(no name)"}\".");
    Console.WriteLine("  V2pn adds first: the local network (geoip:private, geosite:private) DIRECT.");
    for (var i = 0; i < preset.Rules.Count; i++)
    {
        var rule = preset.Rules[i];
        Console.WriteLine($"  rule {i + 1}: {rule.Domains.Count + rule.Ips.Count} entr{(rule.Domains.Count + rule.Ips.Count == 1 ? "y goes" : "ies go")} {Goes(rule.To)}");
        Entries(rule.Domains.Concat(rule.Ips), rule.To);
    }
    Console.WriteLine($"  everything else goes {Goes(preset.Default)}");
    if (preset.UsesGeoFiles)
    {
        Console.WriteLine("  note: geo lists come from the runetfreedom geo files (as V2pn's Russian modes); a tag missing there stops the core.");
    }
    Console.WriteLine("  To decide by hand:");
    Console.WriteLine("    - banks, Gosuslugi and mail are not sent to the provider's servers while the person expects them direct;");
    Console.WriteLine("    - DNS does not go to the provider's server unencrypted (entries marked DNS);");
    Console.WriteLine("    - nothing blocked would open without the VPN (entries marked BLOCKED).");
}

static void ReviewTransport(V2pnTransport transport, byte[] content)
{
    Console.WriteLine("  Used only for the servers of this provider's subscription, instead of the person's own fragment setting.");
    if (transport.Fragment is { } f)
    {
        Console.WriteLine($"  fragment: packets {f.Packets}, pieces of {f.Length} bytes, {f.Interval} ms apart");
        Console.WriteLine("    Xray: on TLS and REALITY servers dialed directly (REALITY gets packets 1-3); sing-box: its TLS fragment on (sizes are its own)");
    }
    foreach (var n in transport.Noise)
    {
        Console.WriteLine($"  noise: {n.Rand} random bytes, {n.Delay} ms before the first UDP packet (Xray, mKCP and Hysteria servers only; sing-box has none)");
    }
    var ignored = (JsonNode.Parse(content) as JsonObject)?.Select(p => p.Key).Where(k => k is not ("schema" or "fragment" or "noise")).ToList() ?? [];
    if (ignored.Count > 0)
    {
        Console.WriteLine($"  ignored by V2pn: {string.Join(", ", ignored)} (mux is not taken: Xray's and sing-box's are different protocols)");
    }
    Console.WriteLine("  To decide by hand: it works with the provider's own servers and touches no other connection.");
}

static string Host(JsonObject outbound)
{
    var server = outbound["settings"]?["vnext"]?[0] ?? outbound["settings"]?["servers"]?[0] ?? outbound["settings"];
    var address = server?["address"]?.ToString() ?? outbound["server"]?.ToString() ?? outbound["settings"]?["peers"]?[0]?["endpoint"]?.ToString();
    var port = server?["port"]?.ToString() ?? outbound["server_port"]?.ToString();
    return address == null ? "(no server)" : port == null ? address : $"{address}:{port}";
}

static void ReviewCore(V2pnCoreFragment core)
{
    Console.WriteLine($"  core: {core.Core}; added to the configs of this provider's servers only, tags prefixed partner-<id>-");
    var hosts = new List<string>();
    foreach (var outbound in core.Outbounds)
    {
        var tag = outbound["tag"]!.ToString();
        var kind = (outbound["protocol"] ?? outbound["type"])!.ToString();
        if (kind == "urltest")
        {
            Console.WriteLine($"  outbound {tag}: picks the fastest of {string.Join(", ", outbound["outbounds"]!.AsArray().Select(n => n!.ToString()))}, testing {outbound["url"]?.ToString() ?? "sing-box's default URL"} every {outbound["interval"]?.ToString() ?? "3m"}");
            continue;
        }
        var via = (outbound["streamSettings"]?["sockopt"]?["dialerProxy"] ?? outbound["detour"])?.ToString();
        var security = (outbound["streamSettings"]?["security"] ?? (outbound["tls"]?["enabled"]?.ToString() == "true" ? "tls" : null))?.ToString() ?? "no TLS";
        var host = Host(outbound);
        hosts.Add(host);
        Console.WriteLine($"  outbound {tag}: {kind} to {host}, {security}{(via != null ? ", dialed through " + via : ", dialed directly")}");
    }
    if (core.Dialer != null)
    {
        Console.WriteLine($"  dialer: every server of the subscription is reached through {core.Dialer}");
    }
    foreach (var b in core.Balancers)
    {
        Console.WriteLine($"  balancer {b.Tag}: {b.Strategy} over {string.Join(", ", b.Selector.Select(x => x == "proxy" ? "proxy (the server the person connected to)" : x))}{(b.Strategy is "leastPing" or "leastLoad" ? $", probing {core.ProbeUrl} every {core.ProbeInterval}" : "")}");
    }
    for (var i = 0; i < core.Rules.Count; i++)
    {
        var rule = core.Rules[i];
        Console.WriteLine($"  rule {i + 1}: go to {rule.To} (before the person's own routing)");
        Entries(rule.Domains.Concat(rule.Ips), rule.To);
    }
    if (core.AllowTop.Count > 0)
    {
        Console.WriteLine($"  allowTop: a whole-config subscription of the provider keeps {string.Join(", ", core.AllowTop)} (files in them are still taken out)");
    }
    if (hosts.Count > 0)
    {
        Console.WriteLine($"  the computer connects to: {string.Join(", ", hosts.Distinct())}");
    }
    Console.WriteLine("  To decide by hand: only what the request describes; rules do not take sites the person sends direct to the provider.");
}

// The repository this script lives in: partners/ is kept there.
string RepoRoot()
{
    var dir = new DirectoryInfo(Environment.CurrentDirectory);
    while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")) && !File.Exists(Path.Combine(dir.FullName, ".git")))
    {
        dir = dir.Parent;
    }
    return dir?.FullName ?? Environment.CurrentDirectory;
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
          dotnet run tools/partner.cs -- cert --partner <id> --name <name> --domains <a,b> --key <public key> [--grants edition,account,lock] [--review <file>]... [--days 365] [--out <folder>] [--root <pem>]
          dotnet run tools/partner.cs -- pack <folder> --key <partner pem>
          dotnet run tools/partner.cs -- review <pack folder>
          dotnet run tools/partner.cs -- revoke [<partner>@<issued> ...] [--out <folder>] [--root <pem>]
        """);
    return 2;
}
