using System.Globalization;

namespace ServiceLib.Handler;

/// <summary>What a subscription's panel says about it, read from the response headers.</summary>
public sealed record SubPanelInfo
{
    /// <summary>From profile-title, else the Content-Disposition file name; null when neither is usable.</summary>
    public string? Title { get; init; }

    public bool HasUserInfo { get; init; }
    public long Upload { get; init; }
    public long Download { get; init; }

    /// <summary>Bytes; 0 means unlimited.</summary>
    public long Total { get; init; }

    /// <summary>Unix seconds; 0 means it never expires.</summary>
    public long Expire { get; init; }

    public int UpdateIntervalHours { get; init; }
    public string? SupportUrl { get; init; }
    public string? WebPageUrl { get; init; }
    public string? Announce { get; init; }

    /// <summary>v2pn-account-token: the holder's token for the partner's live-data API; null when absent.</summary>
    public string? AccountToken { get; init; }

    /// <summary>announce-url: where the announcement leads (https or tg).</summary>
    public string? AnnounceUrl { get; init; }

    /// <summary>The info banner: Happ sub-info-text, else INCY banner-text; at most 200 characters.</summary>
    public string? InfoText { get; init; }

    /// <summary>V2pn's own tone for the info banner (<see cref="SubPanelHeaders.ToneInfo"/> and the others), from sub-info-color.</summary>
    public string InfoTone { get; init; } = SubPanelHeaders.ToneInfo;

    public string? InfoButtonText { get; init; }
    public string? InfoButtonUrl { get; init; }

    /// <summary>Happ sub-expire: true turns the expiry banner on, false turns it off; null when not sent.</summary>
    public bool? ExpiryNotice { get; init; }

    /// <summary>INCY expiry-banner-text: the provider's own words for the expiry banner.</summary>
    public string? ExpiryText { get; init; }

    public string? ExpiryButtonText { get; init; }

    /// <summary>Where "Renew" leads: INCY expiry-banner-button-url, else Happ sub-expire-button-link.</summary>
    public string? ExpiryButtonUrl { get; init; }

    /// <summary>subscription-autoconnect: the provider advises connecting when the app starts.</summary>
    public bool Autoconnect { get; init; }

    /// <summary>subscription-autoconnect-type: lastused, lowestdelay or random; null when not sent or unknown.</summary>
    public string? AutoconnectType { get; init; }

    /// <summary>The provider's order of its servers: ping, alphabet or without (Happ subscriptions-sort-type, INCY sort-order).</summary>
    public string? SortType { get; init; }

    /// <summary>subscription-ping-onopen-enabled: test this subscription's servers when the app opens.</summary>
    public bool PingOnOpen { get; init; }

    /// <summary>ping-type: proxy, proxy-head, tcp or icmp. Kept, not used: V2pn tests every server through itself.</summary>
    public string? PingType { get; init; }

    /// <summary>check-url-via-proxy: an https test address. Kept, not used, as <see cref="PingType"/>.</summary>
    public string? CheckUrl { get; init; }

    /// <summary>update-always (v2RayTun) or subscription-auto-update-open-enable (Happ): update this subscription when the app starts.</summary>
    public bool UpdateOnOpen { get; init; }

    /// <summary>new-url: the full https address the subscription moved to. HTTP header only.</summary>
    public string? NewUrl { get; init; }

    /// <summary>new-domain: the host the subscription moved to, the rest of its address kept. HTTP header only.</summary>
    public string? NewDomain { get; init; }

    /// <summary>hide-settings: stored only; honoured later for partners with the lock grant.</summary>
    public bool HideSettings { get; init; }

    /// <summary>hide-url: stored only, as <see cref="HideSettings"/>.</summary>
    public bool HideUrl { get; init; }
}

/// <summary>
/// V2pn: reads the metadata that VPN panels (Marzban, Remnawave, 3x-ui, Hiddify, Marzneshin) send in the
/// response headers of a subscription. Everything here is lenient: a header that cannot be read is
/// treated as missing, never as an error.
/// </summary>
public static class SubPanelHeaders
{
    public const string ProfileTitle = "profile-title";
    public const string ContentDisposition = "content-disposition";
    public const string UserInfo = "subscription-userinfo";
    public const string UpdateInterval = "profile-update-interval";
    public const string SupportUrl = "support-url";
    public const string WebPageUrl = "profile-web-page-url";
    public const string Announce = "announce";
    /// <summary>The header that once named a partner's own pack folder. Ignored: a subscription's partner is the one whose domains hold its link.</summary>
    public const string Partner = "v2pn-partner";
    public const string AccountToken = "v2pn-account-token";

    // What Happ, INCY and v2RayTun providers already send (docs/partners/README.md, "Headers of other apps").
    public const string AnnounceUrl = "announce-url";
    public const string SubInfoText = "sub-info-text";
    public const string SubInfoColor = "sub-info-color";
    public const string SubInfoButtonText = "sub-info-button-text";
    public const string SubInfoButtonLink = "sub-info-button-link";
    public const string BannerText = "banner-text";
    public const string BannerButtonText = "banner-button-text";
    public const string BannerButtonUrl = "banner-button-url";
    public const string BannerUrl = "banner-url";
    public const string PremiumUrl = "premium-url";
    public const string SubExpire = "sub-expire";
    public const string SubExpireButtonLink = "sub-expire-button-link";
    public const string ExpiryBannerText = "expiry-banner-text";
    public const string ExpiryBannerButtonText = "expiry-banner-button-text";
    public const string ExpiryBannerButtonUrl = "expiry-banner-button-url";
    public const string ExpiryBannerUrl = "expiry-banner-url";
    public const string Autoconnect = "subscription-autoconnect";
    public const string AutoconnectType = "subscription-autoconnect-type";
    public const string SortType = "subscriptions-sort-type";
    public const string SortOrder = "sort-order";
    public const string PingOnOpen = "subscription-ping-onopen-enabled";
    public const string PingType = "ping-type";
    public const string CheckUrl = "check-url-via-proxy";
    public const string UpdateAlways = "update-always";
    public const string UpdateOnOpen = "subscription-auto-update-open-enable";
    public const string NewDomain = "new-domain";
    public const string NewUrl = "new-url";
    public const string HideSettings = "hide-settings";
    public const string HideUrl = "hide-url";

    public const string ToneInfo = "info";
    public const string ToneAttention = "attention";
    public const string ToneOk = "ok";

    public const string ConnectLastUsed = "lastused";
    public const string ConnectLowestDelay = "lowestdelay";
    public const string ConnectRandom = "random";

    public const string SortPing = "ping";
    public const string SortAlphabet = "alphabet";
    public const string SortWithout = "without";

    // Keys a subscription body may carry as "#key: value" lines above its links. Moving the subscription, the INCY
    // banners and V2pn's own partner headers come only in HTTP headers, as INCY has it.
    private static readonly string[] BodyKeys =
    [
        ProfileTitle, UserInfo, UpdateInterval, SupportUrl, WebPageUrl, Announce, AnnounceUrl,
        SubInfoText, SubInfoColor, SubInfoButtonText, SubInfoButtonLink, SubExpire, SubExpireButtonLink,
        Autoconnect, AutoconnectType, SortType, PingOnOpen, PingType, CheckUrl, UpdateAlways, UpdateOnOpen,
        HideSettings, HideUrl,
    ];

    private static readonly string[] Known =
    [
        ProfileTitle, ContentDisposition, UserInfo, UpdateInterval, SupportUrl, WebPageUrl, Announce, AccountToken,
        AnnounceUrl, SubInfoText, SubInfoColor, SubInfoButtonText, SubInfoButtonLink, BannerText, BannerButtonText,
        BannerButtonUrl, BannerUrl, PremiumUrl, SubExpire, SubExpireButtonLink, ExpiryBannerText, ExpiryBannerButtonText,
        ExpiryBannerButtonUrl, ExpiryBannerUrl, Autoconnect, AutoconnectType, SortType, SortOrder, PingOnOpen, PingType,
        CheckUrl, UpdateAlways, UpdateOnOpen, NewDomain, NewUrl, HideSettings, HideUrl,
    ];

    // Happ's limits: the banner text and its button.
    private const int MaxBanner = 200;
    private const int MaxButton = 25;

    // A body's metadata sits above its links; anything further down is not read.
    private const int MaxBodyMetaLines = 64;

    // A token is an opaque value the partner gives each holder; anything longer or stranger is not one.
    private const int MaxToken = 200;

    // Panels name the download after the format; the extension is not part of the provider's name.
    private static readonly string[] FileExtensions = [".txt", ".yaml", ".yml", ".json", ".conf", ".list"];

    private const int MaxTitle = 120;
    private const int MaxAnnounce = 2000;

    // Expiry above this is in milliseconds: as seconds it would be past the year 5000.
    private const long MillisecondsThreshold = 100_000_000_000;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>The headers this class reads, from both the response and its content, keyed case-insensitively.</summary>
    public static Dictionary<string, string> Collect(HttpResponseMessage response)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers.NonValidated.Concat(response.Content.Headers.NonValidated))
        {
            if (!Known.Contains(header.Key, StringComparer.OrdinalIgnoreCase) || result.ContainsKey(header.Key))
            {
                continue;
            }
            var value = header.Value.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            if (value != null)
            {
                result[header.Key] = value;
            }
        }
        return result;
    }

    public static SubPanelInfo Parse(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is not { Count: > 0 })
        {
            return new SubPanelInfo();
        }
        string? Get(string name)
        {
            foreach (var (key, value) in headers)
            {
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }
            return null;
        }

        var title = Clip(OneLine(DecodeText(Get(ProfileTitle))), MaxTitle)
                    ?? Clip(OneLine(FileName(Get(ContentDisposition))), MaxTitle);
        var hasUserInfo = TryParseUserInfo(Get(UserInfo), out var upload, out var download, out var total, out var expire);
        // Happ's info banner first; INCY's banner when Happ's is absent. Each keeps its own button.
        var happInfo = BannerTextOf(Get(SubInfoText));
        var incyInfo = happInfo == null ? BannerTextOf(Get(BannerText)) : null;
        var infoButtonUrl = happInfo != null
            ? ParseSecureLink(Get(SubInfoButtonLink))
            : incyInfo != null
                ? ParseSecureLink(Get(BannerButtonUrl)) ?? ParseSecureLink(Get(BannerUrl)) ?? ParseSecureLink(Get(PremiumUrl))
                : null;
        return new SubPanelInfo
        {
            Title = title,
            HasUserInfo = hasUserInfo,
            Upload = upload,
            Download = download,
            Total = total,
            Expire = expire,
            UpdateIntervalHours = ParseIntervalHours(Get(UpdateInterval)),
            SupportUrl = ParseLink(Get(SupportUrl)),
            WebPageUrl = ParseLink(Get(WebPageUrl)),
            Announce = Clip(DecodeText(Get(Announce)), MaxAnnounce),
            AccountToken = ParseToken(Get(AccountToken)),
            AnnounceUrl = ParseSecureLink(Get(AnnounceUrl)),
            InfoText = happInfo ?? incyInfo,
            InfoTone = happInfo != null ? ParseTone(Get(SubInfoColor)) : ToneInfo,
            InfoButtonText = infoButtonUrl == null ? null : ButtonText(happInfo != null ? Get(SubInfoButtonText) : Get(BannerButtonText)),
            InfoButtonUrl = infoButtonUrl,
            ExpiryNotice = ParseFlag(Get(SubExpire)),
            ExpiryText = BannerTextOf(Get(ExpiryBannerText)),
            ExpiryButtonText = ButtonText(Get(ExpiryBannerButtonText)),
            ExpiryButtonUrl = ParseSecureLink(Get(ExpiryBannerButtonUrl))
                              ?? ParseSecureLink(Get(ExpiryBannerUrl))
                              ?? ParseSecureLink(Get(SubExpireButtonLink)),
            Autoconnect = ParseFlag(Get(Autoconnect)) == true,
            AutoconnectType = ParseAutoconnectType(Get(AutoconnectType)),
            SortType = ParseSortType(Get(SortType)) ?? ParseSortType(Get(SortOrder)),
            PingOnOpen = ParseFlag(Get(PingOnOpen)) == true,
            PingType = ParsePingType(Get(PingType)),
            CheckUrl = ParseCheckUrl(Get(CheckUrl)),
            UpdateOnOpen = ParseFlag(Get(UpdateAlways)) == true || ParseFlag(Get(UpdateOnOpen)) == true,
            NewUrl = ParseNewUrl(Get(NewUrl)),
            NewDomain = ParseNewDomain(Get(NewDomain)),
            HideSettings = ParseFlag(Get(HideSettings)) == true,
            HideUrl = ParseFlag(Get(HideUrl)) == true,
        };
    }

    /// <summary>
    /// The "#key: value" lines at the top of a subscription body (Happ and INCY "body metadata"), for the keys a body
    /// may carry. Only a list of links, plain or base64, has them: a YAML or JSON config's comments are its own.
    /// </summary>
    public static Dictionary<string, string> ReadBody(string? body)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(body))
        {
            return result;
        }
        var text = body.Trim().TrimStart('\uFEFF');
        if (!text.StartsWith('#') && Utils.IsBase64String(text))
        {
            text = Utils.Base64Decode(text);
        }
        var read = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0)
            {
                continue;
            }
            if (!line.StartsWith('#'))
            {
                // The first line that is not a comment must be a link; otherwise this is a config, not a list.
                if (!line.Contains("://", StringComparison.Ordinal))
                {
                    result.Clear();
                }
                break;
            }
            if (++read > MaxBodyMetaLines)
            {
                break;
            }
            var colon = line.IndexOf(':');
            if (colon <= 1)
            {
                continue;
            }
            var key = line[1..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (value.Length > 0 && BodyKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                result.TryAdd(key.ToLowerInvariant(), value);
            }
        }
        return result;
    }

    /// <summary>The response headers with the body's metadata under them: a header always wins over the same key in the body.</summary>
    public static Dictionary<string, string> WithBody(IReadOnlyDictionary<string, string>? headers, string? body)
    {
        var result = ReadBody(body);
        foreach (var (key, value) in headers ?? new Dictionary<string, string>())
        {
            result[key] = value;
        }
        return result;
    }

    /// <summary>"1" or "true" is on, "0" or "false" off; anything else is not an answer.</summary>
    public static bool? ParseFlag(string? value) =>
        value?.Trim().Trim('"').ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => null,
        };

    /// <summary>
    /// An https or Telegram link for the newer fields (announce-url, banner buttons). Unlike <see cref="ParseLink"/>,
    /// plain http is refused: these links come from any subscription and open in the browser.
    /// </summary>
    public static string? ParseSecureLink(string? value)
    {
        var text = DecodeText(value);
        if (text == null || text.Any(char.IsWhiteSpace) || !Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return null;
        }
        return (uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0 && uri.UserInfo.Length == 0) || uri.Scheme == "tg" ? text : null;
    }

    /// <summary>V2pn's tone for Happ's sub-info-color: red, blue or green; anything else (INCY's hex colours too) is the info tone.</summary>
    public static string ParseTone(string? value) =>
        value?.Trim().Trim('"').ToLowerInvariant() switch
        {
            "red" => ToneAttention,
            "green" => ToneOk,
            _ => ToneInfo,
        };

    public static string? ParseAutoconnectType(string? value) =>
        value?.Trim().Trim('"').ToLowerInvariant() switch
        {
            "lastused" => ConnectLastUsed,
            "lowestdelay" => ConnectLowestDelay,
            "random" => ConnectRandom,
            _ => null,
        };

    /// <summary>Happ's ping, alphabet and without; INCY's ping, name and none.</summary>
    public static string? ParseSortType(string? value) =>
        value?.Trim().Trim('"').ToLowerInvariant() switch
        {
            "ping" => SortPing,
            "alphabet" or "name" => SortAlphabet,
            "without" or "none" => SortWithout,
            _ => null,
        };

    public static string? ParsePingType(string? value) =>
        value?.Trim().Trim('"').ToLowerInvariant() switch
        {
            "proxy" or "proxy-head" or "tcp" or "icmp" => value.Trim().Trim('"').ToLowerInvariant(),
            _ => null,
        };

    /// <summary>An https address for the check through the proxy; nothing else.</summary>
    public static string? ParseCheckUrl(string? value) => HttpsUrl(value?.Trim().Trim('"'));

    /// <summary>new-url: an https address with a host and no user info, as plain text (no base64, as INCY has it).</summary>
    public static string? ParseNewUrl(string? value) => HttpsUrl(value?.Trim().Trim('"'));

    /// <summary>new-domain: a bare host name, without scheme, port or path; lower case.</summary>
    public static string? ParseNewDomain(string? value)
    {
        var text = value?.Trim().Trim('"').TrimEnd('.').ToLowerInvariant();
        return text.IsNotEmpty() && text.Length <= 253 && text.Contains('.') && Uri.CheckHostName(text) == UriHostNameType.Dns
            ? text
            : null;
    }

    private static string? HttpsUrl(string? text) =>
        text.IsNotEmpty() && text.Length <= 2048 && !text.Any(char.IsWhiteSpace) && Uri.TryCreate(text, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0 && uri.UserInfo.Length == 0
            ? text
            : null;

    // Happ turns its info banner off with "0".
    private static string? BannerTextOf(string? value)
    {
        var text = DecodeText(value);
        return text is null or "0" ? null : Clip(text, MaxBanner);
    }

    private static string? ButtonText(string? value) => Clip(OneLine(DecodeText(value)), MaxButton);

    /// <summary>
    /// Plain text or "base64:&lt;b64&gt;", as panels send titles and announcements. Also repairs UTF-8
    /// sent raw in a header, which HttpClient reads as Latin-1.
    /// </summary>
    public static string? DecodeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var text = value.Trim();
        if (text.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
        {
            text = FromBase64(text["base64:".Length..]);
            if (text == null)
            {
                return null;
            }
        }
        else
        {
            text = RepairUtf8(text);
        }
        text = Clean(text);
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// The file name of a Content-Disposition value: filename* (RFC 5987, e.g. UTF-8''%D0%9C…) wins over
    /// filename, as RFC 6266 asks. Known format extensions are dropped.
    /// </summary>
    public static string? FileName(string? contentDisposition)
    {
        if (string.IsNullOrWhiteSpace(contentDisposition))
        {
            return null;
        }
        string? plain = null;
        string? extended = null;
        foreach (var part in SplitParameters(contentDisposition))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            var name = part[..eq].Trim().ToLowerInvariant();
            var value = part[(eq + 1)..].Trim();
            if (name == "filename*")
            {
                extended ??= DecodeExtendedValue(Unquote(value));
            }
            else if (name == "filename")
            {
                plain ??= DecodePlainFileName(Unquote(value));
            }
        }

        var fileName = extended.IsNotEmpty() ? extended : plain;
        if (fileName.IsNullOrEmpty())
        {
            return null;
        }
        // A path in the name would only be a server-side detail.
        fileName = fileName.Replace('\\', '/');
        fileName = fileName[(fileName.LastIndexOf('/') + 1)..];
        foreach (var ext in FileExtensions)
        {
            if (fileName.Length > ext.Length && fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                fileName = fileName[..^ext.Length];
                break;
            }
        }
        fileName = Clean(fileName);
        return fileName.Length == 0 ? null : fileName;
    }

    /// <summary>
    /// "upload=N; download=N; total=N; expire=UNIX" in any order, any part missing. True when at least one
    /// part was a number. total=0 is unlimited and expire=0 is never, as the panels mean them.
    /// </summary>
    public static bool TryParseUserInfo(string? value, out long upload, out long download, out long total, out long expire)
    {
        upload = download = total = expire = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        var any = false;
        foreach (var part in value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0 || !TryParseNumber(part[(eq + 1)..], out var number))
            {
                continue;
            }
            switch (part[..eq].Trim().ToLowerInvariant())
            {
                case "upload":
                    upload = number;
                    any = true;
                    break;

                case "download":
                    download = number;
                    any = true;
                    break;

                case "total":
                    total = number;
                    any = true;
                    break;

                case "expire":
                    expire = number > MillisecondsThreshold ? number / 1000 : number;
                    any = true;
                    break;
            }
        }
        return any;
    }

    /// <summary>Hours; 0 when missing or unreadable. Capped at a year.</summary>
    public static int ParseIntervalHours(string? value)
    {
        if (!TryParseNumber(value, out var hours) || hours <= 0)
        {
            return 0;
        }
        return (int)Math.Min(hours, 24 * 366);
    }

    /// <summary>An http(s) or Telegram link the user can open; null for anything else.</summary>
    public static string? ParseLink(string? value)
    {
        var text = DecodeText(value);
        if (text == null || text.Any(char.IsWhiteSpace) || !Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return null;
        }
        return uri.Scheme is "http" or "https" or "tg" ? text : null;
    }

    public static string? ParseToken(string? value)
    {
        var text = value?.Trim();
        return text is { Length: > 0 and <= MaxToken } && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~')
            ? text
            : null;
    }

    private static bool TryParseNumber(string? text, out long number)
    {
        number = 0;
        text = text?.Trim().Trim('"');
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            number = Math.Max(number, 0);
            return true;
        }
        // Some panels print byte counts as floats (1.5E10, 1024.0).
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d))
        {
            number = d <= 0 ? 0 : d >= long.MaxValue ? long.MaxValue : (long)d;
            return true;
        }
        return false;
    }

    private static string? FromBase64(string b64)
    {
        var s = new string(b64.Where(c => !char.IsWhiteSpace(c)).ToArray()).Replace('-', '+').Replace('_', '/');
        s = s.TrimEnd('=');
        if (s.Length % 4 == 1)
        {
            return null;
        }
        s = s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '=');
        var buffer = new byte[s.Length];
        if (!Convert.TryFromBase64String(s, buffer, out var written))
        {
            return null;
        }
        try
        {
            return StrictUtf8.GetString(buffer, 0, written);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static string RepairUtf8(string text)
    {
        if (!text.Any(c => c >= 0x80) || text.Any(c => c > 0xFF))
        {
            return text;
        }
        try
        {
            return StrictUtf8.GetString(Encoding.Latin1.GetBytes(text));
        }
        catch (DecoderFallbackException)
        {
            return text;
        }
    }

    private static string? DecodeExtendedValue(string value)
    {
        // charset'language'percent-encoded
        var first = value.IndexOf('\'');
        var second = first < 0 ? -1 : value.IndexOf('\'', first + 1);
        if (second < 0)
        {
            return null;
        }
        var charset = value[..first].Trim();
        var encoded = value[(second + 1)..];
        Encoding encoding = charset.Equals("iso-8859-1", StringComparison.OrdinalIgnoreCase) ? Encoding.Latin1 : StrictUtf8;
        try
        {
            return encoding.GetString(PercentDecode(encoded));
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static string DecodePlainFileName(string value)
    {
        // Some panels percent-encode the plain filename instead of using filename*.
        if (value.Contains('%'))
        {
            try
            {
                return StrictUtf8.GetString(PercentDecode(value));
            }
            catch (DecoderFallbackException)
            {
            }
        }
        return RepairUtf8(value);
    }

    private static byte[] PercentDecode(string value)
    {
        var bytes = new List<byte>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '%' && i + 2 < value.Length
                && byte.TryParse(value.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                bytes.Add(b);
                i += 2;
            }
            else if (c < 0x80)
            {
                bytes.Add((byte)c);
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
            }
        }
        return bytes.ToArray();
    }

    private static IEnumerable<string> SplitParameters(string value)
    {
        var start = 0;
        var quoted = false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && quoted)
            {
                i++;
            }
            else if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ';' && !quoted)
            {
                yield return value[start..i];
                start = i + 1;
            }
        }
        yield return value[start..];
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            var sb = new StringBuilder(value.Length);
            for (var i = 1; i < value.Length - 1; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length - 1)
                {
                    i++;
                }
                sb.Append(value[i]);
            }
            return sb.ToString();
        }
        return value.Trim('"');
    }

    // Keeps line breaks (announcements have them) but no other control characters.
    private static string Clean(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            if (c == '\n' || !char.IsControl(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Trim();
    }

    private static string? OneLine(string? text)
    {
        if (text == null)
        {
            return null;
        }
        var line = string.Join(' ', text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return line.Length == 0 ? null : line;
    }

    private static string? Clip(string? text, int max) =>
        text == null ? null : text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
