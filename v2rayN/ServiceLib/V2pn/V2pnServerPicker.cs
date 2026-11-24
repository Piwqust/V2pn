namespace ServiceLib;

/// <summary>
/// V2pn: which server "Fastest" means, and which servers a quick recheck tests, as plain functions with tests. They lived
/// in the sidebar's view model and the main window, where nothing tested them, though "Fastest" connects to what they
/// pick.
/// </summary>
public static class V2pnServerPicker
{
    /// <summary>At most this many servers a quick recheck before "Fastest" tests.</summary>
    public const int MaxCandidates = 24;

    /// <summary>The quickest servers of the last test that a quick recheck takes, before favorites and recent ones.</summary>
    public const int QuickestCandidates = 12;

    /// <summary>How many servers "Recent" keeps.</summary>
    public const int MaxRecent = 8;

    /// <summary>
    /// The quickest server by the last results. With statistics on, a server that failed most of its tests is passed
    /// over even when its last answer was quick (<paramref name="unreliable"/>); when every answering server is so, the
    /// quickest of them still counts.
    /// </summary>
    public static T? PickFastest<T>(IEnumerable<T> items, Func<T, int> delay, Func<T, bool> unreliable) where T : class
    {
        var answered = items.Where(m => delay(m) > 0).ToList();
        return answered.Where(m => !unreliable(m)).MinBy(delay) ?? answered.MinBy(delay);
    }

    /// <summary>
    /// What a quick recheck before "Fastest" tests: the servers that were quickest last time, then favorites and recent
    /// ones, each once, at most <see cref="MaxCandidates"/>. <paramref name="distinct"/> drops entries of one connection.
    /// </summary>
    public static List<T> Candidates<T>(IEnumerable<T> all, IEnumerable<T> favorites, IReadOnlyCollection<string> recent,
        Func<T, string> id, Func<T, int> delay, Func<IEnumerable<T>, IEnumerable<T>> distinct)
    {
        var list = all.ToList();
        return distinct(list.Where(m => delay(m) > 0).OrderBy(delay).Take(QuickestCandidates)
                .Concat(favorites)
                .Concat(list.Where(m => recent.Contains(id(m)))))
            .GroupBy(id)
            .Select(g => g.First())
            .Take(MaxCandidates)
            .ToList();
    }

    /// <summary>
    /// Puts <paramref name="indexId"/> first in <paramref name="recent"/> (newest first, at most <see cref="MaxRecent"/>).
    /// False when nothing changed: no server, or it is already first.
    /// </summary>
    public static bool Remember(List<string> recent, string? indexId)
    {
        if (indexId.IsNullOrEmpty() || recent.FirstOrDefault() == indexId)
        {
            return false;
        }
        recent.Remove(indexId);
        recent.Insert(0, indexId);
        if (recent.Count > MaxRecent)
        {
            recent.RemoveRange(MaxRecent, recent.Count - MaxRecent);
        }
        return true;
    }
}
