namespace ServiceLib;

/// <summary>A code to sign in with: six digits to show, the token to ask with, the bot link for the QR code and the term.</summary>
/// <param name="Url">The partner's bot (or a page on its domains) with the code; null when the partner sent none or one V2pn does not open.</param>
public sealed record V2pnLoginCode(string Code, string Token, string? Url, TimeSpan Expires);

public enum V2pnLoginState
{
    /// <summary>The person has not confirmed yet.</summary>
    Waiting,

    /// <summary>Confirmed: <see cref="V2pnLoginAnswer.Subscription"/> is the person's link.</summary>
    Done,

    /// <summary>The code ran out, was refused in the bot («Это не я»), or the partner no longer knows it.</summary>
    Expired,

    /// <summary>Something is wrong: <see cref="V2pnLoginAnswer.Problem"/> says what.</summary>
    Failed,
}

public enum V2pnLoginProblem
{
    None,

    /// <summary>The partner's API does not answer (or not with 200).</summary>
    NoAnswer,

    /// <summary>The answer is not what the contract says.</summary>
    Malformed,

    /// <summary>The subscription it handed over is not https on the partner's listed domains.</summary>
    WrongDomain,

    /// <summary>Asked again after the sign-in was over: a token is good for one answer only.</summary>
    Used,
}

public sealed record V2pnLoginAnswer(V2pnLoginState State, string? Subscription = null, string? Name = null, V2pnLoginProblem Problem = V2pnLoginProblem.None)
{
    public static readonly V2pnLoginAnswer Waiting = new(V2pnLoginState.Waiting);
    public static readonly V2pnLoginAnswer Expired = new(V2pnLoginState.Expired);

    public static V2pnLoginAnswer Fail(V2pnLoginProblem problem) => new(V2pnLoginState.Failed, Problem: problem);
}

/// <summary>
/// Sign-in with a partner by code (pack "login": true, with its API under the account grant). V2pn asks
/// <c>POST &lt;api&gt;login/start</c> with the device's name for six digits and a token, shows the digits and a QR code of
/// the partner's bot link, and asks <c>GET &lt;api&gt;login/poll?token=</c> every two seconds until the person confirms in the
/// bot: then the answer carries the person's subscription link, which V2pn adds as if it were pasted. Everything follows
/// the API's rules (https on the listing's domains, JSON up to 64 KB, a blocked site asked again through the VPN); the
/// subscription is taken only when it is https on the listing's domains, and the token goes in the address only, never to
/// another host (no redirects).
/// </summary>
public static class V2pnPartnerLogin
{
    public const int MaxBytes = 64 * 1024;
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DefaultTerm = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MinTerm = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxTerm = TimeSpan.FromMinutes(30);
    private const int MaxDevice = 60;
    private const int MaxName = 40;
    private static readonly Regex Digits = new("^[0-9]{6}$", RegexOptions.CultureInvariant);
    private static readonly Regex UrlSafe = new("^[A-Za-z0-9_.~-]{32,256}$", RegexOptions.CultureInvariant);

    /// <summary>Overridable for tests: POST of a JSON body.</summary>
    public static Func<string, string, CancellationToken, Task<V2pnHttp.Result>> Post { get; set; } = DefaultPostAsync;

    /// <summary>Overridable for tests: GET.</summary>
    public static Func<string, CancellationToken, Task<V2pnHttp.Result>> Get { get; set; } = DefaultGetAsync;

    /// <summary>True when V2pn may offer to sign in with <paramref name="partner"/>.</summary>
    public static bool Offers(V2pnPartner partner) => partner.Pack.OffersLogin && Base(partner) != null;

    /// <summary>What the partner's bot names this computer by: "Windows · DESKTOP-7Q2", at most 60 characters.</summary>
    public static string DeviceName()
    {
        var os = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsLinux() ? "Linux" : "PC";
        string machine;
        try
        {
            machine = Environment.MachineName;
        }
        catch (InvalidOperationException)
        {
            machine = string.Empty;
        }
        var name = machine.Length > 0 ? $"{os} · {machine}" : os;
        return name.Length <= MaxDevice ? name : name[..MaxDevice];
    }

    /// <summary>A new code; null with the reason when the partner does not answer or answers something else.</summary>
    public static async Task<(V2pnLoginCode? Code, V2pnLoginProblem Problem)> StartAsync(V2pnPartner partner, CancellationToken cancellationToken = default)
    {
        if (!partner.Pack.OffersLogin || Base(partner) is not { } api)
        {
            return (null, V2pnLoginProblem.Malformed);
        }
        var body = new JsonObject { ["device"] = DeviceName(), ["app"] = $"{Global.AppName} {V2pnInfo.Version}" }.ToJsonString();
        var result = await Post(api + "login/start", body, cancellationToken);
        if (!result.Ok)
        {
            return (null, V2pnLoginProblem.NoAnswer);
        }
        return ParseStart(result.Body!, partner.Listing) is { } code ? (code, V2pnLoginProblem.None) : (null, V2pnLoginProblem.Malformed);
    }

    /// <summary>One look at whether the person confirmed. A token the partner does not know (404) is an expired one.</summary>
    public static async Task<V2pnLoginAnswer> PollAsync(V2pnPartner partner, string token, CancellationToken cancellationToken = default)
    {
        if (Base(partner) is not { } api)
        {
            return V2pnLoginAnswer.Fail(V2pnLoginProblem.Malformed);
        }
        var result = await Get(api + "login/poll?token=" + Uri.EscapeDataString(token), cancellationToken);
        if (result.NotFound)
        {
            return V2pnLoginAnswer.Expired;
        }
        return result.Ok ? ParsePoll(result.Body!, partner.Listing) : V2pnLoginAnswer.Fail(V2pnLoginProblem.NoAnswer);
    }

    /// <summary>The answer of login/start; null when the code is not six digits or the token not 32 to 256 url-safe characters.</summary>
    public static V2pnLoginCode? ParseStart(byte[] json, V2pnPartnerListing listing)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || Text(root["code"]) is not { } code || !Digits.IsMatch(code)
                || Text(root["token"]) is not { } token || !UrlSafe.IsMatch(token))
            {
                return null;
            }
            var term = root["expires"] is JsonValue value && value.TryGetValue<int>(out var seconds) ? TimeSpan.FromSeconds(seconds) : DefaultTerm;
            term = term < MinTerm ? MinTerm : term > MaxTerm ? MaxTerm : term;
            return new V2pnLoginCode(code, token, BotLink(listing, Text(root["url"])), term);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The answer of login/poll. A state V2pn does not know yet counts as waiting, so a partner can add its own.</summary>
    public static V2pnLoginAnswer ParsePoll(byte[] json, V2pnPartnerListing listing)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
            {
                return V2pnLoginAnswer.Fail(V2pnLoginProblem.Malformed);
            }
            switch (Text(root["state"]))
            {
                case "expired":
                    return V2pnLoginAnswer.Expired;
                case "done":
                    var subscription = Text(root["subscription"])?.Trim();
                    if (subscription.IsNullOrEmpty())
                    {
                        return V2pnLoginAnswer.Fail(V2pnLoginProblem.Malformed);
                    }
                    if (!SubscriptionAllowed(listing, subscription))
                    {
                        return V2pnLoginAnswer.Fail(V2pnLoginProblem.WrongDomain);
                    }
                    return new V2pnLoginAnswer(V2pnLoginState.Done, subscription, Plain(Text(root["name"]), MaxName));
                case null:
                    return V2pnLoginAnswer.Fail(V2pnLoginProblem.Malformed);
                default:
                    return V2pnLoginAnswer.Waiting;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return V2pnLoginAnswer.Fail(V2pnLoginProblem.Malformed);
        }
    }

    /// <summary>The link for the QR code and «Открыть бота»: https on t.me or on the listing's domains, else null.</summary>
    public static string? BotLink(V2pnPartnerListing listing, string? url)
    {
        var uri = Utils.TryUri(url?.Trim() ?? string.Empty);
        if (uri == null || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 || uri.OriginalString.Length > 512)
        {
            return null;
        }
        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        return host == "t.me" || V2pnPartnerTrust.DomainAllowed(listing, uri.ToString()) ? uri.ToString() : null;
    }

    /// <summary>
    /// A link V2pn takes as the partner's subscription: https (plain http from this computer in a development build, for
    /// tools/demo), no user name or password in it, and its host on the listing's domains, so the partner can hand over
    /// only its own subscriptions.
    /// </summary>
    public static bool SubscriptionAllowed(V2pnPartnerListing listing, string? url)
    {
        if (url.IsNullOrEmpty() || url.Length > 2048 || url.Any(char.IsWhiteSpace) || url.Any(char.IsControl))
        {
            return false;
        }
        var uri = Utils.TryUri(url);
        return uri != null && (uri.Scheme == Uri.UriSchemeHttps || V2pnPartnerTrust.DevLoopback(uri)) && uri.UserInfo.Length == 0
               && V2pnPartnerTrust.DomainAllowed(listing, url);
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

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string? Plain(string? text, int max)
    {
        if (text == null)
        {
            return null;
        }
        text = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return text.Length == 0 ? null : text.Length <= max ? text : text[..(max - 1)] + "…";
    }

    // A site that did not answer directly is asked through the VPN, and the next questions of the sign-in go that way first.
    private static volatile bool _proxyFirst;

    private static Task<V2pnHttp.Result> DefaultPostAsync(string url, string json, CancellationToken cancellationToken) =>
        TwoWaysAsync(proxy => V2pnHttp.PostJsonAsync(url, json, proxy, MaxBytes, cancellationToken), cancellationToken);

    private static Task<V2pnHttp.Result> DefaultGetAsync(string url, CancellationToken cancellationToken) =>
        TwoWaysAsync(proxy => V2pnHttp.GetAsync(url, proxy, MaxBytes, cancellationToken, redirects: false), cancellationToken);

    private static async Task<V2pnHttp.Result> TwoWaysAsync(Func<IWebProxy?, Task<V2pnHttp.Result>> send, CancellationToken cancellationToken)
    {
        var first = _proxyFirst ? await V2pnHttp.LocalProxyAsync(cancellationToken) : null;
        var result = await send(first);
        if (result.Status != null)
        {
            return result;
        }
        // The other way: direct after the VPN, the VPN after direct (when a core listens).
        var other = first == null ? await V2pnHttp.LocalProxyAsync(cancellationToken) : null;
        if (first == null && other == null)
        {
            return result;
        }
        var again = await send(other);
        if (again.Status != null)
        {
            _proxyFirst = other != null;
            return again;
        }
        return result;
    }
}

/// <summary>
/// One sign-in, from its code until it ends: asks every <see cref="V2pnPartnerLogin.Interval"/>, stops at the code's term
/// (counted from when V2pn got it), on "expired" or a 404, on "done", or when the view closes. A token is good for one
/// answer: once the sign-in is over, nothing is asked again, and asking anyway is an error.
/// </summary>
public sealed class V2pnLoginSession
{
    /// <summary>How many questions in a row may go unanswered before the sign-in gives up (about ten seconds).</summary>
    public const int MaxFailures = 5;

    private readonly Func<DateTime> _now;
    private int _failures;
    private bool _over;

    public V2pnLoginSession(V2pnPartner partner, V2pnLoginCode code, Func<DateTime>? now = null)
    {
        _now = now ?? (() => DateTime.UtcNow);
        Partner = partner;
        Code = code;
        ExpiresAt = _now() + code.Expires;
    }

    public V2pnPartner Partner { get; }
    public V2pnLoginCode Code { get; }
    public DateTime ExpiresAt { get; }

    public TimeSpan Left => ExpiresAt - _now() is var left && left > TimeSpan.Zero ? left : TimeSpan.Zero;

    /// <summary>True while the partner has not answered the last questions (the view says so, and keeps waiting).</summary>
    public bool Unanswered => _failures > 0;

    public async Task<V2pnLoginAnswer> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        if (_over)
        {
            return V2pnLoginAnswer.Fail(V2pnLoginProblem.Used);
        }
        if (Left <= TimeSpan.Zero)
        {
            _over = true;
            return V2pnLoginAnswer.Expired;
        }
        var answer = await V2pnPartnerLogin.PollAsync(Partner, Code.Token, cancellationToken);
        if (answer is { State: V2pnLoginState.Failed, Problem: V2pnLoginProblem.NoAnswer } && ++_failures < MaxFailures)
        {
            return V2pnLoginAnswer.Waiting;
        }
        if (answer.State != V2pnLoginState.Failed)
        {
            _failures = 0;
        }
        _over = answer.State != V2pnLoginState.Waiting;
        return answer;
    }

    /// <summary>Asks until the sign-in ends and returns how; <paramref name="waiting"/> hears every "not yet".</summary>
    public async Task<V2pnLoginAnswer> RunAsync(Action? waiting = null, CancellationToken cancellationToken = default, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        while (true)
        {
            await delay(V2pnPartnerLogin.Interval, cancellationToken);
            var answer = await PollOnceAsync(cancellationToken);
            if (answer.State != V2pnLoginState.Waiting)
            {
                return answer;
            }
            waiting?.Invoke();
        }
    }
}
