using System.Security.Cryptography;

namespace ServiceLib;

/// <summary>
/// V2pn: how much a subscription may weigh, and what of it may reach the log. v2rayN read a subscription whole whatever
/// its size, and wrote the whole body to the log when it did not parse: the links, with their UUIDs and passwords, in
/// a file people send to support.
/// </summary>
public static class V2pnLimits
{
    /// <summary>The largest subscription V2pn reads: tens of thousands of links fit, a runaway answer does not.</summary>
    public const int MaxSubscriptionBytes = 32 * 1024 * 1024;

    /// <summary>
    /// <paramref name="content"/> as text, at most <paramref name="maxBytes"/> long; null when it is longer. The charset of
    /// the answer counts, UTF-8 when it names none.
    /// </summary>
    public static async Task<string?> ReadStringAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maxBytes)
        {
            return null;
        }
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var body = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (body.Length + read > maxBytes)
            {
                return null;
            }
            body.Write(buffer, 0, read);
        }
        var encoding = Encoding.UTF8;
        if (content.Headers.ContentType?.CharSet is { Length: > 0 } charset)
        {
            try
            {
                encoding = Encoding.GetEncoding(charset.Trim('"'));
            }
            catch (ArgumentException)
            {
                // An unknown charset: UTF-8, as for none.
            }
        }
        using var reader = new StreamReader(new MemoryStream(body.GetBuffer(), 0, (int)body.Length), encoding, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>A URL as the log may keep it: the scheme, host and port, never the path, query or user, where tokens live.</summary>
    public static string Url(string? url)
    {
        if (Utils.TryUri(url ?? string.Empty) is not { } uri || !uri.IsAbsoluteUri)
        {
            return "(not a URL)";
        }
        var hidden = uri.PathAndQuery is "/" or "" && uri.Fragment.Length == 0 ? "" : "/…";
        return $"{uri.Scheme}://{uri.Authority}{hidden}";
    }

    /// <summary>A body as the log may keep it: its length and the start of its SHA-256, enough to tell two answers apart.</summary>
    public static string Body(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "empty";
        }
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];
        return $"{text.Length} characters, sha256 {hash}…";
    }
}
