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

    /// <summary>v2pn-partner: the https folder of the provider's signed partner pack (V2pnPartner); null when absent.</summary>
    public string? PartnerUrl { get; init; }

    /// <summary>v2pn-account-token: the holder's token for the partner's live-data API; null when absent.</summary>
    public string? AccountToken { get; init; }
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
    public const string Partner = "v2pn-partner";
    public const string AccountToken = "v2pn-account-token";

    private static readonly string[] Known = [ProfileTitle, ContentDisposition, UserInfo, UpdateInterval, SupportUrl, WebPageUrl, Announce, Partner, AccountToken];

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
            PartnerUrl = ParsePartnerUrl(Get(Partner)),
            AccountToken = ParseToken(Get(AccountToken)),
        };
    }

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

    /// <summary>An https folder URL with no user info, ending in "/"; null for anything else.</summary>
    public static string? ParsePartnerUrl(string? value)
    {
        var text = value?.Trim();
        if (text.IsNullOrEmpty() || text.Any(char.IsWhiteSpace) || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || !(uri.Scheme == Uri.UriSchemeHttps || V2pnPartnerTrust.DevLoopback(uri)) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            return null;
        }
        var url = uri.GetLeftPart(UriPartial.Path);
        return url.EndsWith('/') ? url : url + "/";
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
