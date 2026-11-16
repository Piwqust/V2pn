namespace ServiceLib;

/// <summary>
/// Which V2pn this is, fixed at install. Plain V2pn (no marker) knows nothing of partners: it asks for no partner index,
/// downloads no pack, and a partner's subscription is just a subscription there. A partner's own build has
/// v2pn-partner.txt beside the program (on macOS in V2pn.app/Contents/MacOS, where v2pn-preset.txt goes too) holding
/// that partner's id; it always wears that partner's look and takes that partner's features for its subscriptions,
/// and ignores every other partner. Read once at start; every partner feature asks <see cref="PartnerId"/>
/// (V2pnPartners, Editions), so plain V2pn has no path to one.
/// The file comes only with the partner's package (tools/partner-packages.ps1); an update package never carries it
/// and the updater keeps it (AmazTool's Installer), so a build stays what it was installed as.
/// </summary>
public static class V2pnEdition
{
    public const string FileName = "v2pn-partner.txt";
    private const int MaxBytes = 1024;

    private static readonly Lazy<string?> _partnerId = new(() => Read(Utils.GetBaseDirectory()));

    /// <summary>The partner whose build this is; null for plain V2pn.</summary>
    public static string? PartnerId => _partnerId.Value;

    /// <summary>True in a partner's build.</summary>
    public static bool IsPartner => PartnerId != null;

    /// <summary>
    /// The name this build keeps apart from the other on the same computer: "V2pn" for plain V2pn, "V2pn-&lt;id&gt;" for a
    /// partner's build (its data folder where data does not live beside the program, its single-instance lock, its
    /// sign-in entry on macOS and Linux).
    /// </summary>
    public static string InstallName => NameFor(PartnerId);

    /// <summary>
    /// The Windows sign-in entry (scheduled task or Run value). A partner's starts with "V2pn-", plain V2pn's with
    /// "V2pnAutoRun": neither name begins with the other, so neither build ever removes the other's entry.
    /// </summary>
    public static string AutoRunName => AutoRunNameFor(PartnerId);

    public static string NameFor(string? partnerId) => partnerId == null ? Global.AppName : $"{Global.AppName}-{partnerId}";

    public static string AutoRunNameFor(string? partnerId) => partnerId == null ? Global.AutoRunName : $"{Global.AppName}-{partnerId}-AutoRun";

    /// <summary>
    /// The partner id in the text of a marker: its first line that is not empty or a # comment, which must be a partner
    /// id (2 to 32 lowercase letters, digits and dashes). Anything else is not a partner build: null.
    /// </summary>
    public static string? Parse(string? text)
    {
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿').Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            return V2pnPartnerTrust.ValidId(line) ? line : null;
        }
        return null;
    }

    /// <summary>
    /// The partner id of the marker in <paramref name="appDir"/>; null when there is none or it does not read (larger
    /// than 1 KB, no id in it). Logs nothing: the log folder itself depends on <see cref="InstallName"/>.
    /// </summary>
    public static string? Read(string appDir)
    {
        try
        {
            var file = Path.Combine(appDir, FileName);
            return File.Exists(file) && new FileInfo(file).Length <= MaxBytes ? Parse(File.ReadAllText(file)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
