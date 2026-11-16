using System.Text.Json.Nodes;

namespace ServiceLib;

/// <summary>
/// What a partner's API says about the person's subscription: its plan, how many of its servers work by the
/// partner's own checks, the devices on it, the term and a short note. Every field may be missing.
/// </summary>
public sealed record V2pnPartnerAccount(
    string? Plan,
    string? Status,
    int? Healthy,
    int? Total,
    int? Devices,
    int? DeviceLimit,
    DateOnly? Until,
    string? Note);

/// <summary>An outage or works the partner announces for everyone.</summary>
public sealed record V2pnPartnerIncident(string Title, string? Text, bool Severe);

/// <summary>
/// A partner's optional API (pack field "api", only with the "account" grant and on the listing's domains):
/// <c>GET account</c> with the subscription's v2pn-account-token as a bearer token, <c>GET status</c> without one, and
/// for the partner's buttons <c>GET servers</c>, <c>GET recommend?purpose=</c> and <c>GET card?id=</c> with the token
/// when the subscription has one.
/// Answers are JSON up to 64 KB and read field by field, so a partner can add fields without breaking older V2pn;
/// nothing in them runs, and nothing V2pn knows about the device or its traffic is sent.
/// </summary>
public static class V2pnPartnerApi
{
    public const int MaxBytes = 64 * 1024;
    private const int MaxText = 300;
    private const int MaxShort = 60;
    private const int MaxIncidents = 3;

    /// <summary>Overridable for tests: GET with an optional bearer token.</summary>
    public static Func<string, string?, CancellationToken, Task<V2pnHttp.Result>> Fetch { get; set; } = DefaultFetchAsync;

    /// <summary>The account behind <paramref name="token"/>; null when the partner has no API or the call fails.</summary>
    public static async Task<V2pnPartnerAccount?> GetAccountAsync(V2pnPartner partner, string? token, CancellationToken cancellationToken = default)
    {
        if (Base(partner) is not { } api || SubPanelHeaders.ParseToken(token) is not { } bearer)
        {
            return null;
        }
        var result = await Fetch(api + "account", bearer, cancellationToken);
        return result.Body is { } body ? ParseAccount(body) : null;
    }

    /// <summary>The partner's current incidents; null when it has no API or the call fails, empty when all is well.</summary>
    public static async Task<IReadOnlyList<V2pnPartnerIncident>?> GetStatusAsync(V2pnPartner partner, CancellationToken cancellationToken = default)
    {
        if (Base(partner) is not { } api)
        {
            return null;
        }
        var result = await Fetch(api + "status", null, cancellationToken);
        return result.Body is { } body ? ParseStatus(body) : null;
    }

    /// <summary>The partner's live notes about its servers (load, health, groups); null when it has no API or the call fails.</summary>
    public static async Task<IReadOnlyList<V2pnServerNote>?> GetServersAsync(V2pnPartner partner, string? token, CancellationToken cancellationToken = default)
    {
        if (Base(partner) is not { } api)
        {
            return null;
        }
        var result = await Fetch(api + "servers", SubPanelHeaders.ParseToken(token), cancellationToken);
        return result.Ok ? V2pnPartnerCatalog.ParseServersAnswer(result.Body!) : null;
    }

    /// <summary>The servers the partner recommends now for <paramref name="purpose"/>, best first; null when it has no API or the call fails.</summary>
    public static async Task<IReadOnlyList<string>?> GetRecommendAsync(V2pnPartner partner, string? token, string purpose, CancellationToken cancellationToken = default)
    {
        if (Base(partner) is not { } api || !V2pnPurposes.All.Contains(purpose))
        {
            return null;
        }
        var result = await Fetch(api + "recommend?purpose=" + Uri.EscapeDataString(purpose), SubPanelHeaders.ParseToken(token), cancellationToken);
        return result.Ok ? V2pnPartnerCatalog.ParseRecommendAnswer(result.Body!) : null;
    }

    /// <summary>The text of an info.card button; null when the partner has no API or the call fails.</summary>
    public static async Task<V2pnPartnerCard?> GetCardAsync(V2pnPartner partner, string? token, string actionId, CancellationToken cancellationToken = default)
    {
        if (Base(partner) is not { } api)
        {
            return null;
        }
        var result = await Fetch(api + "card?id=" + Uri.EscapeDataString(actionId), SubPanelHeaders.ParseToken(token), cancellationToken);
        return result.Ok ? V2pnPartnerCatalog.ParseCardAnswer(result.Body!) : null;
    }

    private static string? Base(V2pnPartner partner)
    {
        var api = partner.Pack.ApiUrl;
        if (api.IsNullOrEmpty() || !partner.Listing.Allows(V2pnPartnerGrants.Account) || !V2pnPartnerTrust.DomainAllowed(partner.Listing, api))
        {
            return null;
        }
        return api.EndsWith('/') ? api : api + "/";
    }

    public static V2pnPartnerAccount? ParseAccount(byte[] json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
            {
                return null;
            }
            var servers = root["servers"] as JsonObject;
            var devices = root["devices"] as JsonObject;
            int? healthy = Count(servers?["healthy"]);
            int? total = Count(servers?["total"]);
            if (healthy > total)
            {
                healthy = total;
            }
            return new V2pnPartnerAccount(
                Text(root["plan"], MaxShort),
                Text(root["status"], 24),
                total > 0 ? healthy : null,
                total > 0 ? total : null,
                Count(devices?["count"]),
                Count(devices?["limit"]),
                DateOnly.TryParse(Text(root["until"], 32) ?? string.Empty, System.Globalization.CultureInfo.InvariantCulture, out var until) ? until : null,
                Text(root["note"], MaxText));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public static IReadOnlyList<V2pnPartnerIncident>? ParseStatus(byte[] json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["incidents"] is not JsonArray items)
            {
                return null;
            }
            var incidents = new List<V2pnPartnerIncident>();
            // The first three with a title; an item without one says nothing to show.
            foreach (var item in items.OfType<JsonObject>().Take(64))
            {
                if (incidents.Count < MaxIncidents && Text(item["title"], 80) is { } title)
                {
                    incidents.Add(new V2pnPartnerIncident(title, Text(item["text"], MaxText), Text(item["severity"], 16) == "major"));
                }
            }
            return incidents;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    // One line of plain text: no control characters, no markup worth anything, cut to a length.
    private static string? Text(JsonNode? node, int max)
    {
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            return null;
        }
        text = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return text.Length == 0 ? null : text.Length <= max ? text : text[..(max - 1)] + "…";
    }

    private static int? Count(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var n) && n is >= 0 and <= 1_000_000 ? n : null;

    private static async Task<V2pnHttp.Result> DefaultFetchAsync(string url, string? bearer, CancellationToken cancellationToken)
    {
        var result = await V2pnHttp.GetAsync(url, null, MaxBytes, cancellationToken, bearer);
        // A partner site blocked where the person is: the same question through the VPN.
        if (result.Status == null && await V2pnHttp.LocalProxyAsync(cancellationToken) is { } proxy)
        {
            result = await V2pnHttp.GetAsync(url, proxy, MaxBytes, cancellationToken, bearer);
        }
        return result;
    }
}
