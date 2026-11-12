namespace ServiceLib;

/// <summary>
/// V2pn: a pasted subscription link becomes a named subscription that downloads right away, and an
/// update that brought nothing is told apart from one that worked.
/// </summary>
public static class V2pnSubs
{
    /// <summary>Minutes between automatic updates of a new subscription: twice a day, what panels usually ask for.</summary>
    public const int NewSubUpdateMinutes = 720;

    /// <summary>The links when every non-empty line of <paramref name="text"/> is an http(s) link; otherwise null.</summary>
    public static List<string>? SubscriptionLinks(string? text)
    {
        if (text.IsNullOrEmpty())
        {
            return null;
        }
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0 || !lines.All(IsHttpLink))
        {
            return null;
        }
        return lines.Distinct(StringComparer.Ordinal).ToList();
    }

    public static bool IsHttpLink(string? line)
    {
        if (line.IsNullOrEmpty() || line.Any(char.IsWhiteSpace))
        {
            return false;
        }
        if (!line.StartsWith(Global.HttpsProtocol, StringComparison.OrdinalIgnoreCase)
            && !line.StartsWith(Global.HttpProtocol, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return Utils.TryUri(line)?.Host.IsNotEmpty() == true;
    }

    /// <summary>
    /// Saves a subscription for <paramref name="url"/>, named by the link's remarks or its host (both replaceable
    /// by the panel's name later), or returns the one that already has this link. Null when it cannot be saved.
    /// </summary>
    public static async Task<(SubItem Item, bool Existed)?> AddSubscriptionAsync(Config config, string url)
    {
        url = url.Trim();
        var existing = (await AppManager.Instance.SubItems() ?? []).FirstOrDefault(s => s.Url?.Trim() == url);
        if (existing != null)
        {
            return (existing, true);
        }
        var uri = Utils.TryUri(url);
        if (uri == null)
        {
            return null;
        }
        var item = new SubItem
        {
            Id = string.Empty,
            Url = url,
            AutoUpdateInterval = NewSubUpdateMinutes,
            Remarks = Utils.ParseQueryString(uri.Query)["remarks"] ?? SubInfoManager.FallbackName(url) ?? string.Empty,
        };
        if (await ConfigHandler.AddSubItem(config, item) != 0 || item.Id.IsNullOrEmpty())
        {
            return null;
        }
        return (item, false);
    }

    /// <summary>
    /// UpdateTime of the subscriptions an update of <paramref name="subId"/> (empty: all) will try: enabled ones
    /// with a link. The update writes UpdateTime only when it imported servers.
    /// </summary>
    public static async Task<Dictionary<string, long>> SnapshotAsync(string? subId)
    {
        var subs = await AppManager.Instance.SubItems() ?? [];
        return subs
            .Where(s => s.Id.IsNotEmpty() && s.Url.IsNotEmpty() && s.Enabled)
            .Where(s => subId.IsNullOrEmpty() || s.Id == subId)
            .GroupBy(s => s.Id)
            .ToDictionary(g => g.Key, g => g.First().UpdateTime);
    }

    /// <summary>The subscriptions of <paramref name="before"/> whose UpdateTime did not change: their update failed.</summary>
    public static async Task<List<SubItem>> NotUpdatedAsync(IReadOnlyDictionary<string, long> before)
    {
        if (before.Count == 0)
        {
            return [];
        }
        var subs = await AppManager.Instance.SubItems() ?? [];
        return subs.Where(s => before.TryGetValue(s.Id, out var time) && s.UpdateTime == time).ToList();
    }
}
