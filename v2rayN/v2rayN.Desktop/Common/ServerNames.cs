using System.Text;
using System.Text.RegularExpressions;

namespace v2rayN.Desktop.Common;

/// <summary>A numbered set of servers a provider publishes under one name: "AUTO · 1/34", "AUTO · 2/34"…</summary>
public sealed record ServerSeries(string Key, string Name, string? Code, IReadOnlyList<ProfileItemModel> Members);

/// <summary>
/// Reads the naming habits of subscription providers so the list can show what a person looks for.
/// Two habits matter in practice. Pools: dozens of servers that differ only by a counter, which are one
/// choice ("the fast one"), not thirty-four. And "Country - variant" names, which are too long for a row
/// and repeat the country the row already shows as a flag.
/// </summary>
public static partial class ServerNames
{
    /// <summary>Fewer members than this is a pair of servers, not a pool.</summary>
    public const int MinSeries = 3;

    [GeneratedRegex(@"^(?<base>.+?)\s*[·•|]\s*(?<n>\d{1,3})\s*/\s*(?<m>\d{1,3})\s*$")]
    private static partial Regex CounterRegex();

    [GeneratedRegex(@"^(?<place>[^\-–—|]{2,40}?)\s+[\-–—|]\s+(?<detail>.{2,})$")]
    private static partial Regex PlaceDetailRegex();

    /// <summary>"AUTO — Обход БС · 15/31" → base "AUTO — Обход БС", 15 of 31.</summary>
    public static bool TryParseCounter(string? remarks, out string baseName, out int index, out int total)
    {
        baseName = string.Empty;
        index = total = 0;
        var match = remarks.IsNullOrEmpty() ? null : CounterRegex().Match(remarks);
        if (match is not { Success: true })
        {
            return false;
        }
        baseName = match.Groups["base"].Value.Trim();
        index = int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        total = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        return baseName.Length > 0 && index >= 1 && total >= index;
    }

    /// <summary>
    /// Pulls the pools out of a list. A pool is one subscription's servers with the same name and the same
    /// total in the counter; the rest come back in their original order.
    /// </summary>
    public static (List<ServerSeries> Series, List<ProfileItemModel> Others) SplitSeries(IReadOnlyList<ProfileItemModel> items)
    {
        var groups = new Dictionary<string, (string Name, List<ProfileItemModel> Members)>();
        var keys = new Dictionary<string, string>();
        foreach (var item in items)
        {
            if (!TryParseCounter(item.Remarks, out var baseName, out _, out var total))
            {
                continue;
            }
            var key = $"series:{item.Subid}:{total}:{baseName}";
            keys[item.IndexId] = key;
            if (!groups.TryGetValue(key, out var group))
            {
                groups[key] = group = (baseName, []);
            }
            group.Members.Add(item);
        }

        var series = groups
            .Where(g => g.Value.Members.Count >= MinSeries)
            .Select(g => new ServerSeries(g.Key, SeriesTitle(g.Value.Name), CountryResolver.Resolve(g.Value.Name), g.Value.Members))
            .ToList();
        var inSeries = series.Select(s => s.Key).ToHashSet();
        var rest = items.Where(m => !keys.TryGetValue(m.IndexId, out var key) || !inSeries.Contains(key)).ToList();
        return (series, rest);
    }

    /// <summary>The pool's name without the flag and without a leading emoji: the row has its own icon.</summary>
    public static string SeriesTitle(string baseName) => TrimLeadingSymbols(CountryResolver.StripFlag(baseName));

    /// <summary>"🆘 Резервный доступ" → "Резервный доступ": the row's own icon stands where the emoji was.</summary>
    public static string TrimLeadingSymbols(string name)
    {
        var start = 0;
        foreach (var rune in name.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune) || rune.Value is '(' or '[' or '«' or '"')
            {
                break;
            }
            start += rune.Utf16SequenceLength;
        }
        var trimmed = name[start..].Trim();
        return trimmed.Length > 0 ? trimmed : name;
    }

    /// <summary>
    /// "Germany (2) - Vless TCP 1/GBIT" → place "Germany (2)", detail "Vless TCP 1/GBIT", when the first part
    /// names the country the server resolves to. Anything else stays whole (detail is null).
    /// </summary>
    public static (string Place, string? Detail) SplitPlace(string name, string? code)
    {
        if (code.IsNullOrEmpty())
        {
            return (name, null);
        }
        var match = PlaceDetailRegex().Match(name);
        if (!match.Success)
        {
            return (name, null);
        }
        var place = match.Groups["place"].Value.Trim();
        return string.Equals(CountryResolver.Resolve(place), code, StringComparison.OrdinalIgnoreCase)
            ? (place, match.Groups["detail"].Value.Trim())
            : (name, null);
    }

    /// <summary>
    /// One row per connection. The same provider added through two links lists every server twice; the copy
    /// in use, then the starred one, then the one with a ping result is the one that stays.
    /// </summary>
    public static List<ProfileItemModel> DistinctConnections(IEnumerable<ProfileItemModel> items) =>
        items
            .GroupBy(m => (m.ConfigType, Address: m.Address?.Trim().ToLowerInvariant(), m.Port, m.Network, m.StreamSecurity, Name: CountryResolver.StripFlag(m.Remarks)))
            .Select(g => g.Count() == 1 || g.Key.Address.IsNullOrEmpty()
                ? g.ToList()
                : [g.OrderByDescending(m => m.IsActive).ThenByDescending(m => m.IsFavorite).ThenBy(m => m.Delay > 0 ? m.Delay : int.MaxValue).First()])
            .SelectMany(g => g)
            .ToList();
}
