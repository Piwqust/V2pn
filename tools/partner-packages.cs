#:project ../v2rayN/ServiceLib/ServiceLib.csproj
// The packages of each partner's own build of V2pn, made from a release's signed standard packages:
//
//   dotnet run tools/partner-packages.cs -- <release folder> [--partners <folder>]
//
// For every partners/<id> with a listing.json (every published partner has the edition grant: publish adds it) and
// every standard package in <release folder> (V2pn-windows-x64.zip, V2pn-windows-arm64.zip, V2pn-macos-arm64.zip,
// V2pn-macos-x64.zip), writes V2pn-<id>-<os>-<arch>.zip and its .sha256 beside it: the same files plus
// v2pn-partner.txt holding the id (on macOS inside V2pn-<id>.app/Contents/MacOS; the app is renamed so it sits beside
// V2pn.app). The program is V2pn's own, so a partner's build updates from the same signed release.json, which does not
// list these packages; the updater keeps the marker. The .dmg and the update packages have no partner form.
// tools/sign-release.ps1 runs this after release.json is signed. Exit code 1 when a package could not be made.

using ServiceLib;

if (args.Length == 0 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("usage: dotnet run tools/partner-packages.cs -- <release folder> [--partners <folder>]");
    return 2;
}
var release = Path.GetFullPath(args[0]);
var i = Array.IndexOf(args, "--partners");
var partnersDir = Path.GetFullPath(i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(RepoRoot(), "partners"));

var ids = new List<string>();
foreach (var folder in Directory.EnumerateDirectories(partnersDir).Where(d => File.Exists(Path.Combine(d, V2pnPartnerPublisher.ListingFile))).Order(StringComparer.Ordinal))
{
    if (V2pnPartnerPublisher.ReadListing(folder, out var error) is not { } listing)
    {
        Console.Error.WriteLine($"PROBLEM {Path.GetFileName(folder)}: {error}");
        return 1;
    }
    ids.Add(listing.Id);
}
var sources = V2pnPartnerBuilds.Sources.Where(s => File.Exists(Path.Combine(release, s))).ToList();
if (ids.Count == 0 || sources.Count == 0)
{
    Console.WriteLine(ids.Count == 0 ? $"No partners in {partnersDir}: no partner packages." : $"No standard packages in {release}: no partner packages.");
    return 0;
}
foreach (var missing in V2pnPartnerBuilds.Sources.Except(sources))
{
    Console.WriteLine($"note: {missing} is not in {release}; its partner packages are not made");
}

foreach (var id in ids)
{
    Console.WriteLine($"{id}:");
    foreach (var source in sources)
    {
        var name = V2pnPartnerBuilds.PackageName(id, source);
        var target = Path.Combine(release, name);
        try
        {
            V2pnPartnerBuilds.Repack(Path.Combine(release, source), target, id);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"PROBLEM {name}: {ex.Message}");
            return 1;
        }
        var line = V2pnPartnerBuilds.Sha256Line(target);
        File.WriteAllText(target + ".sha256", line);
        Console.WriteLine($"  {name}  {new FileInfo(target).Length / 1024.0 / 1024.0:F1} MB  sha256 {line[..12]}");
    }
}
return 0;

// The repository this script lives in: partners/ is kept there.
static string RepoRoot()
{
    var dir = new DirectoryInfo(Environment.CurrentDirectory);
    while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")) && !File.Exists(Path.Combine(dir.FullName, ".git")))
    {
        dir = dir.Parent;
    }
    return dir?.FullName ?? Environment.CurrentDirectory;
}
