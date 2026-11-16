namespace ServiceLib;

/// <summary>
/// A special build that comes with a subscription: v2pn-preset.txt next to V2pn.exe holds one link per line
/// (a plain https subscription link, or a v2pn:// / happ:// / v2raytun:// / hiddify:// add link). The person
/// downloaded that build to get this subscription, so it is added at the first start without asking.
/// Each link is added once: guiConfigs/V2pnPreset.txt remembers what was added, so a subscription the person
/// later deletes does not come back, while a new link in the file (another build over the same folder) does.
/// </summary>
public static class V2pnPreset
{
    public const string FileName = "v2pn-preset.txt";
    private const string MarkerName = "V2pnPreset.txt";
    private const int MaxLinks = 10;

    /// <summary>The subscription links in the text of a preset file; anything that is not one is skipped.</summary>
    public static List<string> Parse(string? text)
    {
        var links = new List<string>();
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            if (line.IsNullOrEmpty() || line.StartsWith('#'))
            {
                continue;
            }
            var url = V2pnDeepLink.Parse(line)?.SubscriptionUrl ?? (V2pnSubs.IsHttpLink(line) ? line : null);
            if (url != null && !links.Contains(url, StringComparer.Ordinal))
            {
                links.Add(url);
            }
            if (links.Count >= MaxLinks)
            {
                break;
            }
        }
        return links;
    }

    /// <summary>The links of this build's preset not yet added.</summary>
    public static List<string> Pending(string? appDir = null, string? configDir = null)
    {
        try
        {
            var file = Path.Combine(appDir ?? Utils.GetBaseDirectory(), FileName);
            if (!File.Exists(file))
            {
                return [];
            }
            var done = Applied(configDir);
            return Parse(File.ReadAllText(file)).Where(l => !done.Contains(l)).ToList();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnPreset), ex);
            return [];
        }
    }

    public static void MarkApplied(IEnumerable<string> links, string? configDir = null)
    {
        try
        {
            var done = Applied(configDir);
            done.UnionWith(links);
            File.WriteAllLines(MarkerPath(configDir), done.Order(StringComparer.Ordinal));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(V2pnPreset), ex);
        }
    }

    private static HashSet<string> Applied(string? configDir)
    {
        var path = MarkerPath(configDir);
        return File.Exists(path)
            ? File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    private static string MarkerPath(string? configDir) =>
        configDir != null ? Path.Combine(configDir, MarkerName) : Utils.GetConfigPath(MarkerName);
}
