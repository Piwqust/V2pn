using System.IO.Compression;

namespace ServiceLib;

/// <summary>
/// A partner's packages of V2pn, made from the standard packages of a release after they are signed
/// (tools/partner-packages.cs, run by tools/sign-release.ps1): the same files with v2pn-partner.txt added, so the program
/// is byte for byte V2pn's, installs where V2pn installs and updates from the same signed release. The marker says the
/// person came from that partner (V2pnEdition): V2pn gets that partner's tab. V2pn-windows-x64.zip becomes
/// V2pn-&lt;id&gt;-windows-x64.zip with V2pn/v2pn-partner.txt; V2pn-macos-arm64.zip becomes V2pn-&lt;id&gt;-macos-arm64.zip with
/// V2pn.app/Contents/MacOS/v2pn-partner.txt. They are not in release.json: an installed copy updates from the standard
/// update package and keeps its marker.
/// </summary>
public static class V2pnPartnerBuilds
{
    /// <summary>The standard packages a partner's packages are made from, when the release has them.</summary>
    public static readonly IReadOnlyList<string> Sources =
        ["V2pn-windows-x64.zip", "V2pn-windows-arm64.zip", "V2pn-macos-arm64.zip", "V2pn-macos-x64.zip"];

    private static readonly Regex PartnerPackage = new(
        "^V2pn-(?!(windows|macos|linux)-)[a-z0-9][a-z0-9-]{1,31}-(windows|macos)-[a-z0-9]+\\.zip(\\.sha256)?$", RegexOptions.CultureInvariant);

    /// <summary>"V2pn-windows-x64.zip" of partner configra: "V2pn-configra-windows-x64.zip".</summary>
    public static string PackageName(string partnerId, string source) =>
        source.StartsWith("V2pn-", StringComparison.Ordinal) ? $"V2pn-{partnerId}-{source["V2pn-".Length..]}" : throw new ArgumentException($"{source} is not a V2pn package");

    /// <summary>A partner's package (or its .sha256): never listed in release.json.</summary>
    public static bool IsPartnerPackage(string? name) => name != null && PartnerPackage.IsMatch(name);

    /// <summary>The text of the marker: the id, which is all V2pnEdition reads.</summary>
    public static string MarkerText(string partnerId) => partnerId + "\n";

    /// <summary>
    /// Writes the partner's package of <paramref name="source"/> (a standard package: one top folder V2pn/ or V2pn.app/)
    /// to <paramref name="target"/>. Every entry is copied with its date and Unix mode; the marker is added. Throws when
    /// the source is not a standard package or already has a marker.
    /// </summary>
    public static void Repack(string source, string target, string partnerId)
    {
        if (!V2pnPartnerTrust.ValidId(partnerId))
        {
            throw new ArgumentException($"{partnerId} is not a partner id");
        }
        using var input = ZipFile.OpenRead(source);
        var tops = input.Entries.Select(e => e.FullName.Replace('\\', '/').Split('/')[0]).Distinct().ToList();
        if (tops.Count != 1 || tops[0] is not ("V2pn" or "V2pn.app"))
        {
            throw new InvalidDataException($"{Path.GetFileName(source)} is not a V2pn package (one top folder V2pn/ or V2pn.app/).");
        }
        var mac = tops[0] == "V2pn.app";
        var top = tops[0];
        var marker = mac ? $"{top}/Contents/MacOS/{V2pnEdition.FileName}" : $"{top}/{V2pnEdition.FileName}";
        if (input.Entries.Any(e => e.Name == V2pnEdition.FileName))
        {
            throw new InvalidDataException($"{Path.GetFileName(source)} already has {V2pnEdition.FileName}.");
        }
        if (File.Exists(target))
        {
            File.Delete(target);
        }
        using var output = ZipFile.Open(target, ZipArchiveMode.Create);
        foreach (var entry in input.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            var copy = output.CreateEntry(top + name[tops[0].Length..], CompressionLevel.Optimal);
            copy.LastWriteTime = entry.LastWriteTime;
            copy.ExternalAttributes = entry.ExternalAttributes;
            if (!name.EndsWith('/'))
            {
                using var from = entry.Open();
                using var to = copy.Open();
                from.CopyTo(to);
            }
        }
        var added = output.CreateEntry(marker, CompressionLevel.Optimal);
        added.LastWriteTime = DateTimeOffset.UtcNow;
        // A regular file, rw-r--r--, as ditto writes one (AmazTool and macOS read the mode from here).
        added.ExternalAttributes = mac ? unchecked((int)(0x81A4u << 16)) : 0;
        using (var writer = new StreamWriter(added.Open(), new UTF8Encoding(false)))
        {
            writer.Write(MarkerText(partnerId));
        }
    }

    /// <summary>"&lt;sha256&gt;  &lt;name&gt;", as tools/manifest.cs writes for the standard packages (sha256sum -c reads it).</summary>
    public static string Sha256Line(string file)
    {
        using var stream = File.OpenRead(file);
        return $"{Convert.ToHexStringLower(SHA256.HashData(stream))}  {Path.GetFileName(file)}\n";
    }
}
