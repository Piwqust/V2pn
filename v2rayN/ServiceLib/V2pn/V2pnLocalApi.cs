using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;

namespace ServiceLib;

/// <summary>
/// V2pn: the secret of the cores' control API (sing-box's and mihomo's Clash API). v2rayN ran it on a fixed loopback
/// port with no secret, so any program on the computer, under any Windows user, could switch the core to "direct" with
/// one request (all traffic then goes past the server, and the kill switch lets it out because it leaves from the
/// core) or read the hosts every program visits. The secret is new on every start of V2pn and never written anywhere
/// but the core's config in V2pn's own folder.
/// </summary>
public static class V2pnLocalApi
{
    /// <summary>The secret for this run of V2pn.</summary>
    public static string Secret { get; } = NewSecret();

    /// <summary>The value of the Authorization header the API expects.</summary>
    public static AuthenticationHeaderValue Authorization => new("Bearer", Secret);

    /// <summary>A client for the control API, with the secret set.</summary>
    public static HttpClient Client()
    {
        var client = HttpClientHelper.Instance.GetHttpClient(false);
        client.DefaultRequestHeaders.Authorization = Authorization;
        return client;
    }

    /// <summary>Sets the secret on a websocket to the control API (sing-box's /traffic).</summary>
    public static void Authorize(ClientWebSocket socket)
        => socket.Options.SetRequestHeader("Authorization", $"Bearer {Secret}");

    private static string NewSecret()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
}
