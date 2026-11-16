namespace ServiceLib;

/// <summary>
/// What a provider says about one of its servers (pack "servers", API <c>GET servers</c>): the server is the one of
/// the partner's subscription whose name (exact remark) or address matches. Every field may be missing; the API's
/// <see cref="Load"/> and <see cref="Healthy"/> never come from the pack.
/// </summary>
public sealed record V2pnServerNote(
    string? Name,
    string? Host,
    string? Group,
    string? Country,
    string? City,
    IReadOnlyList<string> Purposes,
    int? Priority,
    bool? Hidden,
    string? Note,
    int? Load = null,
    bool? Healthy = null)
{
    public bool IsHidden => Hidden == true;

    public bool HasPurpose(string purpose) => Purposes.Contains(purpose);

    /// <summary>True for the server named <paramref name="remarks"/> or at <paramref name="address"/>.</summary>
    public bool Matches(string? remarks, string? address) =>
        (Name != null && remarks != null && Name == remarks)
        || (Host != null && address.IsNotEmpty() && string.Equals(Host, address!.Trim().TrimEnd('.'), StringComparison.OrdinalIgnoreCase));

    /// <summary>This (the API's word) over <paramref name="pack"/>, field by field: what the API leaves out, the pack keeps.</summary>
    public V2pnServerNote Over(V2pnServerNote? pack) => pack == null ? this : new V2pnServerNote(
        Name ?? pack.Name,
        Host ?? pack.Host,
        Group ?? pack.Group,
        Country ?? pack.Country,
        City ?? pack.City,
        Purposes.Count > 0 ? Purposes : pack.Purposes,
        Priority ?? pack.Priority,
        Hidden ?? pack.Hidden,
        Note ?? pack.Note,
        Load ?? pack.Load,
        Healthy ?? pack.Healthy);
}

/// <summary>What a server is good for. A fixed set: a provider picks from it, V2pn names it in the person's language.</summary>
public static class V2pnPurposes
{
    public const string YouTube = "youtube";
    public const string Games = "games";
    public const string Mobile = "mobile";
    public const string Whitelist = "whitelist";
    public const string Streaming = "streaming";
    public const string Ai = "ai";
    public const string Torrents = "torrents";

    public static readonly IReadOnlyList<string> All = [YouTube, Games, Mobile, Whitelist, Streaming, Ai, Torrents];

    public static string Label(string purpose) => purpose switch
    {
        YouTube => ResUI.V2pnPurposeYouTube,
        Games => ResUI.V2pnPurposeGames,
        Mobile => ResUI.V2pnPurposeMobile,
        Whitelist => ResUI.V2pnPurposeWhitelist,
        Streaming => ResUI.V2pnPurposeStreaming,
        Ai => ResUI.V2pnPurposeAi,
        Torrents => ResUI.V2pnPurposeTorrents,
        _ => purpose,
    };
}

/// <summary>The icons an action button may wear: names of V2pn's own glyphs, never a picture from the provider.</summary>
public static class V2pnActionIcons
{
    public static readonly IReadOnlyList<string> All = ["bolt", "shield", "play", "game", "card", "chat", "globe", "star", "refresh", "check", "info", "gift"];
}

/// <summary>What an action button does. Each is code of V2pn; the pack only names one.</summary>
public static class V2pnActionKinds
{
    public const string ConnectBest = "connect.best";
    public const string ConnectRecommend = "connect.recommend";
    public const string ConnectServer = "connect.server";
    public const string ConnectGroup = "connect.group";
    public const string OpenCabinet = "open.cabinet";
    public const string OpenRenew = "open.renew";
    public const string OpenSupport = "open.support";
    public const string OpenLink = "open.link";
    public const string SubscriptionUpdate = "subscription.update";
    public const string CheckServices = "check.services";
    public const string InfoCard = "info.card";

    public static readonly IReadOnlyList<string> All =
    [
        ConnectBest, ConnectRecommend, ConnectServer, ConnectGroup, OpenCabinet, OpenRenew, OpenSupport, OpenLink,
        SubscriptionUpdate, CheckServices, InfoCard,
    ];

    public static bool Connects(string kind) => kind is ConnectBest or ConnectRecommend or ConnectServer or ConnectGroup;
}

/// <summary>How connect.best picks a server.</summary>
public static class V2pnStrategies
{
    /// <summary>The lowest delay V2pn measures now.</summary>
    public const string Ping = "ping";

    /// <summary>The most answers over V2pn's last checks, then the lowest median delay, then the smallest spread.</summary>
    public const string Stable = "stable";

    /// <summary>The lowest load the partner's API reports; by ping when it reports none.</summary>
    public const string Load = "load";

    public static readonly IReadOnlyList<string> All = [Ping, Stable, Load];
}

/// <summary>Which servers an action chooses from; empty means every server of the partner's subscriptions.</summary>
public sealed record V2pnActionScope(string? Group, string? Purpose, IReadOnlyList<string> Servers)
{
    public bool IsEmpty => Group == null && Purpose == null && Servers.Count == 0;
}

/// <summary>A button of the partner: a label, one of V2pn's icons and one of V2pn's actions.</summary>
public sealed record V2pnPartnerAction(
    string Id,
    string Label,
    string? Icon,
    string Do,
    string? Strategy = null,
    V2pnActionScope? Scope = null,
    string? Purpose = null,
    string? Server = null,
    string? Group = null,
    string? Url = null);

/// <summary>A short text the partner's API gives for an info.card button.</summary>
public sealed record V2pnPartnerCard(string? Title, string Text);

/// <summary>
/// Reads the "servers" and "actions" of a pack and the same server notes from the partner's API. Pure and strict:
/// limits on counts and lengths, unknown values left out, an entry that cannot be used dropped without failing the pack.
/// </summary>
public static class V2pnPartnerCatalog
{
    public const int MaxServers = 300;
    public const int MaxActions = 4;
    public const int MaxGroup = 24;
    public const int MaxCity = 32;
    public const int MaxNote = 60;
    public const int MaxLabel = 24;
    public const int MaxScopeServers = 50;
    public const int MaxRecommended = 50;
    public const int MaxCardTitle = 60;
    public const int MaxCardText = 600;
    private const int MaxName = 128;
    private const int MaxHost = 253;
    private const int MaxPriority = 1000;

    private static readonly Regex ActionId = new("^[a-z0-9][a-z0-9_-]{0,31}$", RegexOptions.CultureInvariant);
    private static readonly Regex CountryCode = new("^[A-Za-z]{2}$", RegexOptions.CultureInvariant);

    /// <summary>The server notes of a pack (<paramref name="live"/> false) or of an API answer (true: load and health too).</summary>
    public static IReadOnlyList<V2pnServerNote> ParseServers(JsonNode? node, bool live)
    {
        if (node is not JsonArray array)
        {
            return [];
        }
        var notes = new List<V2pnServerNote>();
        foreach (var item in array.Take(MaxServers * 2).OfType<JsonObject>())
        {
            if (notes.Count >= MaxServers)
            {
                break;
            }
            if (ParseServer(item, live) is { } note)
            {
                notes.Add(note);
            }
        }
        return notes;
    }

    /// <summary>The server notes in an API answer <c>{ "servers": [...] }</c>; null when it is not one.</summary>
    public static IReadOnlyList<V2pnServerNote>? ParseServersAnswer(byte[] json)
    {
        try
        {
            return JsonNode.Parse(json) is JsonObject root && root["servers"] is JsonArray servers ? ParseServers(servers, true) : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The servers an API answer <c>{ "servers": ["name or host", ...] }</c> recommends, in its order; null when it is not one.</summary>
    public static IReadOnlyList<string>? ParseRecommendAnswer(byte[] json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["servers"] is not JsonArray servers)
            {
                return null;
            }
            return servers.Take(MaxRecommended * 2)
                .Select(n => Plain(n, MaxHost))
                .Where(s => s != null)
                .Select(s => s!)
                .Distinct(StringComparer.Ordinal)
                .Take(MaxRecommended)
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>An info card <c>{ "title", "text" }</c>: plain text, lines kept, cut to their limits; null without text.</summary>
    public static V2pnPartnerCard? ParseCardAnswer(byte[] json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
            {
                return null;
            }
            var text = Lines(root["text"], MaxCardText);
            return text == null ? null : new V2pnPartnerCard(Plain(root["title"], MaxCardTitle, cut: true), text);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// The buttons of a pack: the first <see cref="MaxActions"/> usable ones. An action V2pn does not know, one that
    /// needs the API without it, one with a link off the listing's domains or with a malformed field is left out.
    /// </summary>
    public static IReadOnlyList<V2pnPartnerAction> ParseActions(JsonNode? node, V2pnPartnerListing listing, bool hasApi)
    {
        if (node is not JsonArray array)
        {
            return [];
        }
        var actions = new List<V2pnPartnerAction>();
        foreach (var item in array.Take(32).OfType<JsonObject>())
        {
            if (actions.Count >= MaxActions)
            {
                break;
            }
            if (ParseAction(item, listing, hasApi) is { } action && actions.All(a => a.Id != action.Id))
            {
                actions.Add(action);
            }
        }
        return actions;
    }

    /// <summary>
    /// A link an open.link button may open: https on a domain of the listing or on t.me, or a tg: link. Null otherwise.
    /// </summary>
    public static string? ActionLink(V2pnPartnerListing listing, string? url)
    {
        var uri = Utils.TryUri(url?.Trim() ?? string.Empty);
        if (uri == null || uri.UserInfo.Length > 0)
        {
            return null;
        }
        if (uri.Scheme == "tg")
        {
            // As written: Uri would add a slash Telegram does not expect ("tg://resolve/?domain=").
            return url!.Trim();
        }
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }
        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        return host == "t.me" || V2pnPartnerTrust.DomainAllowed(listing, uri.ToString()) ? uri.ToString() : null;
    }

    private static V2pnServerNote? ParseServer(JsonObject item, bool live)
    {
        var name = Plain(item["name"], MaxName);
        var host = Plain(item["host"], MaxHost)?.TrimEnd('.').ToLowerInvariant();
        if (name == null && host == null)
        {
            return null;
        }
        var country = Plain(item["country"], 2);
        var purposes = Strings(item["purpose"]).Select(p => p.ToLowerInvariant()).Where(V2pnPurposes.All.Contains).Distinct().ToList();
        return new V2pnServerNote(
            name,
            host,
            Plain(item["group"], MaxGroup),
            country != null && CountryCode.IsMatch(country) ? country.ToUpperInvariant() : null,
            Plain(item["city"], MaxCity),
            purposes,
            Int(item["priority"]) is int priority && Math.Abs(priority) <= MaxPriority ? priority : null,
            Bool(item["hidden"]),
            Plain(item["note"], MaxNote),
            live && Int(item["load"]) is int load and >= 0 and <= 100 ? load : null,
            live ? Bool(item["healthy"]) : null);
    }

    private static V2pnPartnerAction? ParseAction(JsonObject item, V2pnPartnerListing listing, bool hasApi)
    {
        var id = Plain(item["id"], 32);
        var label = Plain(item["label"], MaxLabel);
        var kind = Plain(item["do"], 32);
        if (id == null || !ActionId.IsMatch(id) || label == null || kind == null || !V2pnActionKinds.All.Contains(kind))
        {
            return null;
        }
        var icon = Plain(item["icon"], 16);
        if (icon != null && !V2pnActionIcons.All.Contains(icon))
        {
            icon = null;
        }
        var action = new V2pnPartnerAction(id, label, icon, kind);
        switch (kind)
        {
            case V2pnActionKinds.ConnectBest:
                var strategy = Plain(item["strategy"], 16) ?? V2pnStrategies.Ping;
                if (!V2pnStrategies.All.Contains(strategy) || Scope(item["scope"], out var scope) is false)
                {
                    return null;
                }
                return action with { Strategy = strategy, Scope = scope };

            case V2pnActionKinds.ConnectRecommend:
                // The provider's own choice comes from its API; without one there is nothing to ask.
                var purpose = Plain(item["purpose"], 16);
                return hasApi && purpose != null && V2pnPurposes.All.Contains(purpose) ? action with { Purpose = purpose } : null;

            case V2pnActionKinds.ConnectServer:
                return Plain(item["server"], MaxHost) is { } server ? action with { Server = server } : null;

            case V2pnActionKinds.ConnectGroup:
                return Plain(item["group"], MaxGroup) is { } group ? action with { Group = group } : null;

            case V2pnActionKinds.OpenLink:
                return ActionLink(listing, Plain(item["url"], 2048)) is { } url ? action with { Url = url } : null;

            case V2pnActionKinds.InfoCard:
                return hasApi ? action : null;

            default:
                return action;
        }
    }

    /// <summary>False for a scope that is there but malformed (then the action is dropped); null scope for none.</summary>
    private static bool Scope(JsonNode? node, out V2pnActionScope? scope)
    {
        scope = null;
        if (node == null)
        {
            return true;
        }
        if (node is not JsonObject obj)
        {
            return false;
        }
        var group = Plain(obj["group"], MaxGroup);
        var purpose = Plain(obj["purpose"], 16);
        if (purpose != null && !V2pnPurposes.All.Contains(purpose))
        {
            return false;
        }
        var servers = Strings(obj["servers"]).Select(s => s.Trim()).Where(s => s.Length is > 0 and <= MaxHost).Distinct().Take(MaxScopeServers).ToList();
        scope = new V2pnActionScope(group, purpose, servers);
        if (scope.IsEmpty)
        {
            scope = null;
        }
        return true;
    }

    // One line of plain text: no control characters; longer than max is no value at all (cut: shortened instead).
    private static string? Plain(JsonNode? node, int max, bool cut = false)
    {
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            return null;
        }
        text = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (text.Length == 0)
        {
            return null;
        }
        if (text.Length > max)
        {
            return cut ? text[..(max - 1)] + "…" : null;
        }
        return text;
    }

    // Plain text that keeps its line breaks (at most a few in a row), cut to max.
    private static string? Lines(JsonNode? node, int max)
    {
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            return null;
        }
        var clean = new string(text.Replace("\r\n", "\n").Where(c => c == '\n' || !char.IsControl(c)).ToArray());
        clean = Regex.Replace(clean, "\n{3,}", "\n\n").Trim();
        if (clean.Length == 0)
        {
            return null;
        }
        return clean.Length <= max ? clean : clean[..(max - 1)] + "…";
    }

    private static IEnumerable<string> Strings(JsonNode? node) => node switch
    {
        JsonArray array => array.Take(64).Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).Where(s => s.IsNotEmpty()).Select(s => s!),
        JsonValue value when value.TryGetValue<string>(out var single) && single.IsNotEmpty() => [single],
        _ => [],
    };

    private static int? Int(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var n) ? n : null;

    private static bool? Bool(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var b) ? b : null;
}
