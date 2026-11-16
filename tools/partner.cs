#:project ../v2rayN/ServiceLib/ServiceLib.csproj
// The partners of V2pn (providers whose subscriptions V2pn dresses in their brand), kept in partners/<id> in this
// repository and published by the owner of V2pn with the releases.
//
//   dotnet run tools/partner.cs -- publish [--partners <folder>] [--out <folder>] [--root-key <pem>] [--previous <partners.json>] [--local]
//       Reads every partners/<id> folder (listing.json, partner.json and its files), checks each pack with the reader
//       the app uses, and writes into --out (default partners/out, not in git): partners.json, partners.json.sig
//       (signed with --root-key, default ~/.v2pn/partner-root-key.pem, over the exact bytes) and partner-<id>.zip for
//       each partner. The hash of each reviewed file (routing.json, transport.json, core.json) goes into the index when
//       listing.json grants it. Prints who, which domains, which grants, until when, and what changed since --previous
//       (default: the partners.json already in --out). Nothing is written when any partner has a problem.
//       tools/sign-release.ps1 and tools/publish-mirror.ps1 ship the result with a release. --local lets listing.json
//       name 127.0.0.1 and localhost, for tools/demo with a test root key; only development builds take those domains.
//
//   dotnet run tools/partner.cs -- pack <folder>
//       Lists every picture (png, webp, jpg) and reviewed file of <folder> in its partner.json with size and SHA-256,
//       and prints what V2pn takes of the theme (Accent, or Scene with a background) and what it leaves out.
//
//   dotnet run tools/partner.cs -- review <folder>
//       What the reviewed files of a pack do, in plain words, for the manual review: every routing rule (sites and
//       addresses -> proxy, direct or block), every transport setting, every outbound, balancer and rule of core.json,
//       with block rules, DNS and the hosts the computer will connect to marked, and V2pn's own checks of each file.
//
// The signature is DER ECDSA P-256 / SHA-256 in base64, the same as release.json.sig.

using System.Security.Cryptography;
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

switch (args[0])
{
    case "publish":
        return Publish();
    case "pack":
    {
        if (args.Length < 2 || !Directory.Exists(args[1]))
        {
            return Usage();
        }
        var folder = Path.GetFullPath(args[1]);
        if (V2pnPackWriter.Read(folder, out var packError) is not { } pack)
        {
            Console.Error.WriteLine(packError);
            return 1;
        }
        V2pnPackWriter.Write(folder, pack);
        var assets = (JsonArray)pack["assets"]!;
        Console.WriteLine($"wrote {Path.Combine(folder, V2pnPackWriter.PackFile)} with {assets.Count} file(s)");
        var names = assets.Select(a => a?["name"]?.GetValue<string>()).OfType<string>().Where(n => !V2pnReviewedFiles.All.Contains(n)).ToHashSet();
        var theme = V2pnThemeReader.Parse(pack["theme"], pack["brand"]?["accent"]?.GetValue<string>(), names.Contains);
        var look = theme.Background.Kind == V2pnThemeBackgroundKinds.Solid ? "Accent (no background)" : $"Scene on its {theme.Background.Kind} background";
        Console.WriteLine($"  look: {look}; brand {string.Join(" ", theme.Brand.DefaultIfEmpty("(none)"))}");
        foreach (var note in theme.Notes.Concat(V2pnThemePalette.Describe(theme)))
        {
            Console.WriteLine($"  theme: {note}");
        }
        foreach (var name in V2pnReviewedFiles.All.Where(n => File.Exists(Path.Combine(folder, n))))
        {
            Console.WriteLine($"  {name}: V2pn uses it only once its grant is in listing.json (read it first: dotnet run tools/partner.cs -- review {args[1]})");
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
    default:
        return Usage();
}

int Publish()
{
    var repo = RepoRoot();
    var partnersDir = Path.GetFullPath(Option("--partners") ?? Path.Combine(repo, "partners"));
    var outDir = Path.GetFullPath(Option("--out") ?? Path.Combine(partnersDir, "out"));
    if (!Directory.Exists(partnersDir))
    {
        Console.Error.WriteLine($"{partnersDir} does not exist.");
        return 2;
    }
    var keyFile = Option("--root-key") ?? Path.Combine(home, ".v2pn", "partner-root-key.pem");
    if (!File.Exists(keyFile))
    {
        Console.Error.WriteLine($"No root key at {keyFile}; pass --root-key <pem>.");
        return 2;
    }
    using var root = V2pnPartnerSigning.ReadPrivateKey(File.ReadAllText(keyFile), out var keyError);
    if (root == null)
    {
        Console.Error.WriteLine($"{keyFile}: {keyError}");
        return 2;
    }

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var publication = V2pnPartnerPublisher.Publish(partnersDir, today, DateTimeOffset.UtcNow, args.Contains("--local"));
    if (!publication.Ok)
    {
        foreach (var problem in publication.Problems)
        {
            Console.Error.WriteLine($"PROBLEM {problem}");
        }
        Console.Error.WriteLine("Nothing was written.");
        return 1;
    }

    // What was out before, to say what changes: --previous, or what the last publish left in --out.
    var previousFile = Option("--previous") ?? Path.Combine(outDir, V2pnPartnerTrust.IndexFile);
    var hadPrevious = File.Exists(previousFile);
    var previous = hadPrevious ? ReadUnsigned(File.ReadAllBytes(previousFile)) : null;

    Directory.CreateDirectory(outDir);
    foreach (var old in Directory.EnumerateFiles(outDir).Where(f => Regex.IsMatch(Path.GetFileName(f), @"^(partner-[a-z0-9-]+\.zip|partners\.json(\.sig)?)$")))
    {
        File.Delete(old);
    }
    var signature = V2pnPartnerSigning.Sign(root, publication.Index);
    File.WriteAllBytes(Path.Combine(outDir, V2pnPartnerTrust.IndexFile), publication.Index);
    File.WriteAllText(Path.Combine(outDir, V2pnPartnerTrust.IndexFile + ".sig"), signature + "\n");
    foreach (var partner in publication.Partners)
    {
        File.WriteAllBytes(Path.Combine(outDir, partner.Listing.Zip.File), partner.Zip);
    }

    // Read back the way the app reads it: the signature over these bytes, every zip against the index.
    var check = V2pnPartnerTrust.ReadIndex(File.ReadAllBytes(Path.Combine(outDir, V2pnPartnerTrust.IndexFile)),
        File.ReadAllText(Path.Combine(outDir, V2pnPartnerTrust.IndexFile + ".sig")).Trim(), [V2pnPartnerSigning.PublicKey(root)], out var readError);
    if (check == null || check.Problems.Count > 0 || check.Partners.Count != publication.Partners.Count)
    {
        Console.Error.WriteLine($"The written index does not read back: {readError}{string.Join("; ", check?.Problems ?? [])}");
        return 1;
    }
    foreach (var listing in check.Partners)
    {
        if (V2pnPartnerTrust.Unpack(File.ReadAllBytes(Path.Combine(outDir, listing.Zip.File)), listing, out _, out var unpackError) == null)
        {
            Console.Error.WriteLine($"{listing.Zip.File} does not read back: {unpackError}");
            return 1;
        }
    }

    Console.WriteLine($"Published {publication.Partners.Count} partner(s) into {outDir}:");
    foreach (var partner in publication.Partners)
    {
        var l = partner.Listing;
        var days = l.Until.DayNumber - today.DayNumber;
        Console.WriteLine();
        Console.WriteLine($"  {l.Name} ({l.Id})");
        Console.WriteLine($"    domains   {string.Join(", ", l.Domains)}");
        Console.WriteLine($"    grants    {string.Join(", ", l.Grants)}");
        Console.WriteLine($"    until     {l.Until:yyyy-MM-dd} ({days} days){(days < 30 ? "  <- runs out soon" : "")}");
        Console.WriteLine($"    pack      {l.Zip.File}, {l.Zip.Size / 1024.0:0.#} KB, sha256 {l.Zip.Sha256[..12]}");
        foreach (var (file, hash) in l.ReviewedFiles)
        {
            Console.WriteLine($"    reviewed  {file} sha256 {hash[..12]} -> grant {V2pnReviewedFiles.GrantOf(file)}");
        }
        if (partner.Contact != null)
        {
            Console.WriteLine($"    contact   {partner.Contact} (not published)");
        }
        foreach (var note in partner.Notes)
        {
            Console.WriteLine($"    note      {note}");
        }
    }
    Console.WriteLine();
    if (previous == null)
    {
        Console.WriteLine(hadPrevious ? $"Changes: {previousFile} does not read, nothing to compare with." : "Changes: no earlier index to compare with.");
    }
    else
    {
        var changes = Changes(previous, publication.Partners.Select(p => p.Listing).ToList());
        Console.WriteLine(changes.Count == 0 ? "Changes since the last index: none." : "Changes since the last index:");
        foreach (var change in changes)
        {
            Console.WriteLine($"  {change}");
        }
    }
    Console.WriteLine();
    Console.WriteLine($"Signed with the key in {keyFile}, public half {V2pnPartnerSigning.PublicKey(root)}");
    Console.WriteLine("It goes out with the next release (tools/sign-release.ps1). To ship it now, add it to the latest release and the mirror:");
    Console.WriteLine($"  gh release upload <latest tag> --repo Piwqust/V2pn --clobber \"{outDir}\"/*");
    return CheckAgainstBuiltInKeys(publication.Index, signature);
}

// An earlier partners.json as written, without its signature: only to say what changed.
static V2pnPartnerIndex? ReadUnsigned(byte[] json)
{
    try
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return V2pnPartnerTrust.ReadIndex(json, V2pnPartnerSigning.Sign(key, json), [V2pnPartnerSigning.PublicKey(key)], out _);
    }
    catch (CryptographicException)
    {
        return null;
    }
}

static List<string> Changes(V2pnPartnerIndex before, IReadOnlyList<V2pnPartnerListing> after)
{
    var changes = new List<string>();
    foreach (var gone in before.Partners.Where(b => after.All(a => a.Id != b.Id)))
    {
        changes.Add($"{gone.Id}: removed (installed copies drop its look: \"revoked\")");
    }
    foreach (var now in after)
    {
        if (before.Get(now.Id) is not { } was)
        {
            changes.Add($"{now.Id}: new");
            continue;
        }
        void Diff(string what, string a, string b)
        {
            if (a != b)
            {
                changes.Add($"{now.Id}: {what} {a} -> {b}");
            }
        }
        Diff("name", was.Name, now.Name);
        Diff("domains", string.Join(",", was.Domains), string.Join(",", now.Domains));
        Diff("grants", string.Join(",", was.Grants), string.Join(",", now.Grants));
        Diff("until", was.Until.ToString("yyyy-MM-dd"), now.Until.ToString("yyyy-MM-dd"));
        Diff("reviewed", string.Join(",", was.ReviewedFiles.Select(r => $"{r.Key}:{r.Value[..12]}")), string.Join(",", now.ReviewedFiles.Select(r => $"{r.Key}:{r.Value[..12]}")));
        if (was.Zip.Sha256 != now.Zip.Sha256)
        {
            changes.Add($"{now.Id}: pack changed ({was.Zip.Sha256[..12]} -> {now.Zip.Sha256[..12]}); installed copies download it again");
        }
    }
    return changes;
}

// The checklist of the manual review, filled in from the files.
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
        if (V2pnPartnerPublisher.ReviewProblem(name, content) is { } problem)
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
    Console.WriteLine("If it is fine, add each file's grant (routing, transport, core) to the grants of listing.json and publish:");
    Console.WriteLine("  dotnet run tools/partner.cs -- publish");
    Console.WriteLine("The index then names these exact hashes; any change to a file needs a new review and a new publish. Review again every year.");
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
        Console.WriteLine($"  rule {i + 1}: go to {rule.To} (after the person's own rules, before the mode's catch-all)");
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
    Console.WriteLine("  To decide by hand: only what the provider described (the person's own rules always come before these).");
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

// Whether the keys built into the app (V2pnPartnerKeys in v2rayN/Directory.Build.targets) take the signature.
int CheckAgainstBuiltInKeys(byte[] data, string signature)
{
    var targets = Path.Combine(RepoRoot(), "v2rayN", "Directory.Build.targets");
    if (!File.Exists(targets))
    {
        Console.WriteLine("(run inside the repository to check the signature against the keys built into the app)");
        return 0;
    }
    var match = Regex.Match(File.ReadAllText(targets), "<V2pnPartnerKeys[^>]*>([^<]*)</V2pnPartnerKeys>");
    var keys = match.Success ? match.Groups[1].Value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
    if (keys.Any(key => V2pnPartnerSigning.Verify(key, data, signature)))
    {
        Console.WriteLine("checked: the app's built-in partner keys accept this signature");
        return 0;
    }
    Console.Error.WriteLine("WARNING: no key in V2pnPartnerKeys accepts this signature; installed apps will refuse this index (fine for a test key).");
    return 3;
}

static int Usage()
{
    Console.Error.WriteLine("""
        usage:
          dotnet run tools/partner.cs -- publish [--partners <folder>] [--out <folder>] [--root-key <pem>] [--previous <partners.json>] [--local]
          dotnet run tools/partner.cs -- pack <folder>
          dotnet run tools/partner.cs -- review <folder>
        """);
    return 2;
}
