using System.Collections.Concurrent;
using Avalonia.Data.Converters;

namespace v2rayN.Desktop.Converters;

/// <summary>
/// Value converters for the V2pn interface. Latency tiers drive style classes so colors stay theme-aware.
/// </summary>
public static class V2pnConverters
{
    // Delay: 0 = never tested, negative = failed, positive = milliseconds.
    public static readonly IValueConverter IsPingGood =
        new FuncValueConverter<int, bool>(d => d is > 0 and <= 300);

    public static readonly IValueConverter IsPingMid =
        new FuncValueConverter<int, bool>(d => d is > 300 and <= 800);

    public static readonly IValueConverter IsPingBad =
        new FuncValueConverter<int, bool>(d => d < 0 || d > 800);

    public static readonly IValueConverter PingText =
        new FuncValueConverter<int, string>(d => d switch
        {
            0 => "—",
            < 0 => L.Timeout,
            _ => L.Ms(d),
        });

    // A never-tested delay (0) shows no pill at all instead of a column of grey "—" placeholders.
    public static readonly IValueConverter NotZero =
        new FuncValueConverter<int, bool>(d => d != 0);

    public static readonly IValueConverter PingShort =
        new FuncValueConverter<int, string>(d => d switch
        {
            0 => "",
            < 0 => "×",
            _ => L.Ms(d),
        });

    /// <summary>Short label for the system proxy mode (ESysProxyType index).</summary>
    public static readonly IValueConverter ProxyModeShort =
        new FuncValueConverter<int, string>(m => (ESysProxyType)m switch
        {
            ESysProxyType.ForcedChange => L.ProxyOn,
            ESysProxyType.Unchanged => L.ProxyKeep,
            ESysProxyType.Pac => L.ProxyPac,
            _ => L.ProxyOff,
        });

    public static readonly IValueConverter IsProxyRouting =
        new FuncValueConverter<int, bool>(m => (ESysProxyType)m is ESysProxyType.ForcedChange or ESysProxyType.Pac);

    public static readonly IValueConverter OnOff =
        new FuncValueConverter<bool, string>(b => b ? L.ProxyOn : L.ProxyOff);

    /// <summary>True while a ping test is running for the row (the test writes a placeholder into DelayVal).</summary>
    public static readonly IValueConverter IsTesting =
        new FuncValueConverter<string?, bool>(v => v == ResUI.Speedtesting);

    public static readonly IValueConverter IsNotTesting =
        new FuncValueConverter<string?, bool>(v => v != ResUI.Speedtesting);

    /// <summary>Short monogram for a protocol, e.g. VLESS → VL, Hysteria2 → HY2.</summary>
    public static readonly IValueConverter ProtocolMonogram =
        new FuncValueConverter<EConfigType, string>(t => t switch
        {
            EConfigType.VMess => "VM",
            EConfigType.VLESS => "VL",
            EConfigType.Shadowsocks => "SS",
            EConfigType.Trojan => "TR",
            EConfigType.Hysteria2 => "HY2",
            EConfigType.TUIC => "TU",
            EConfigType.WireGuard => "WG",
            EConfigType.SOCKS => "S5",
            EConfigType.HTTP => "HT",
            EConfigType.Anytls => "AT",
            EConfigType.Naive => "NV",
            EConfigType.MASQUE => "MQ",
            EConfigType.Outbound => "OB",
            EConfigType.PolicyGroup => "GR",
            EConfigType.ProxyChain => "CH",
            EConfigType.Custom => "{ }",
            _ => t.ToString().Length > 3 ? t.ToString()[..2].ToUpperInvariant() : t.ToString().ToUpperInvariant(),
        });

    public static readonly IValueConverter ProtocolName =
        new FuncValueConverter<EConfigType, string>(t => t switch
        {
            EConfigType.VMess => "VMess",
            EConfigType.VLESS => "VLESS",
            EConfigType.Shadowsocks => "Shadowsocks",
            EConfigType.Trojan => "Trojan",
            EConfigType.Hysteria2 => "Hysteria2",
            EConfigType.TUIC => "TUIC",
            EConfigType.WireGuard => "WireGuard",
            EConfigType.SOCKS => "SOCKS",
            EConfigType.HTTP => "HTTP",
            _ => t.ToString(),
        });

    private static readonly ConcurrentDictionary<string, Bitmap?> _flags = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Two-letter country code → small flag bitmap (null when unknown).</summary>
    /// <summary>"host:port", or "address hidden" for a server whose partner closed its settings (V2pnLocks).</summary>
    public static readonly IValueConverter ServerAddress = new FuncValueConverter<ProfileItemModel?, string>(m =>
        m == null ? string.Empty : V2pnLocks.Instance.Hides(m.Subid) ? L.AddressHidden : $"{m.Address}:{m.Port}");

    /// <summary>A subscription's link, or "the link is hidden by …" when its partner hid it (hide-url with the lock grant).</summary>
    public static readonly IValueConverter SubLink = new FuncValueConverter<SubItem?, string>(s =>
        s == null ? string.Empty : V2pnLocks.Instance.ForSub(s.Id) is { HideUrl: true } locked ? string.Format(L.LockedUrl, locked.PartnerName) : s.Url);

    /// <summary>A subscription's link for a list, or "servers added by hand" when it has none.</summary>
    public static readonly IValueConverter SubListLink = new FuncValueConverter<SubItem?, string>(s =>
        s == null ? string.Empty : s.Url.IsNullOrEmpty() ? L.SubManual : SubLink.Convert(s, typeof(string), null, CultureInfo.CurrentUICulture) as string ?? string.Empty);

    /// <summary>A subscription's name for a list: its own, or the link's site, or "servers added by hand".</summary>
    public static readonly IValueConverter SubName = new FuncValueConverter<SubItem?, string>(s =>
        s == null ? string.Empty : s.Remarks.IsNotEmpty() ? s.Remarks : Utils.TryUri(s.Url)?.Host ?? L.SubManual);

    public static Bitmap? Flag(string? code)
    {
        if (code.IsNullOrEmpty() || code.Length != 2)
        {
            return null;
        }
        return _flags.GetOrAdd(code, c =>
        {
            var uri = new Uri($"avares://V2pn/Assets/Flags/{c.ToLowerInvariant()}.png");
            return AssetLoader.Exists(uri) ? new Bitmap(AssetLoader.Open(uri)) : null;
        });
    }

    public static readonly IValueConverter FlagImage = new FuncValueConverter<string?, Bitmap?>(Flag);

    public static readonly IValueConverter NotEmpty =
        new FuncValueConverter<string?, bool>(s => !string.IsNullOrWhiteSpace(s));

    public static readonly IValueConverter PositiveToBool =
        new FuncValueConverter<int, bool>(n => n > 0);

    public static readonly IValueConverter ZeroToBool =
        new FuncValueConverter<int, bool>(n => n <= 0);
}
