namespace ServiceLib.Tests.Handler;

// The Happ, INCY and v2RayTun headers V2pn reads (docs/partners/README.md, "Headers of other apps").
public class SubPanelCompatTests
{
    private static string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private static Dictionary<string, string> Headers(params (string Name, string Value)[] headers) =>
        headers.ToDictionary(h => h.Name, h => h.Value, StringComparer.OrdinalIgnoreCase);

    private static SubPanelInfo Parse(params (string Name, string Value)[] headers) => SubPanelHeaders.Parse(Headers(headers));

    [Test]
    public async Task AnnounceUrl_ShouldTakeHttpsAndTelegramOnly()
    {
        await Parse(("announce-url", "https://example.com/news")).AnnounceUrl.Should().BeEqualTo("https://example.com/news");
        await Parse(("Announce-URL", "tg://resolve?domain=example")).AnnounceUrl.Should().BeEqualTo("tg://resolve?domain=example");
        await Parse(("announce-url", "base64:" + B64("https://example.com/b64"))).AnnounceUrl.Should().BeEqualTo("https://example.com/b64");
        await Parse(("announce-url", "http://example.com/news")).AnnounceUrl.Should().BeNull();
        await Parse(("announce-url", "file:///C:/Windows/System32/calc.exe")).AnnounceUrl.Should().BeNull();
        await Parse(("announce-url", "javascript:alert(1)")).AnnounceUrl.Should().BeNull();
        await Parse(("announce-url", "ms-msdt:/id PCWDiagnostic")).AnnounceUrl.Should().BeNull();
        await Parse(("announce-url", "https://user:pass@example.com/")).AnnounceUrl.Should().BeNull();
        await Parse(("announce-url", "https://exa mple.com/")).AnnounceUrl.Should().BeNull();
    }

    [Test]
    public async Task HappInfoBanner_ShouldReadTextColourAndButton()
    {
        var info = Parse(
            ("sub-info-text", "base64:" + B64("Скидка 50% до пятницы")),
            ("sub-info-color", "green"),
            ("sub-info-button-text", "base64:" + B64("Купить")),
            ("sub-info-button-link", "https://t.me/example_bot"));
        await info.InfoText.Should().BeEqualTo("Скидка 50% до пятницы");
        await info.InfoTone.Should().BeEqualTo(SubPanelHeaders.ToneOk);
        await info.InfoButtonText.Should().BeEqualTo("Купить");
        await info.InfoButtonUrl.Should().BeEqualTo("https://t.me/example_bot");
    }

    [Test]
    [Arguments("red", SubPanelHeaders.ToneAttention)]
    [Arguments("RED", SubPanelHeaders.ToneAttention)]
    [Arguments("blue", SubPanelHeaders.ToneInfo)]
    [Arguments("green", SubPanelHeaders.ToneOk)]
    [Arguments("#E53E3E", SubPanelHeaders.ToneInfo)]
    [Arguments("purple", SubPanelHeaders.ToneInfo)]
    [Arguments("", SubPanelHeaders.ToneInfo)]
    public async Task InfoColour_ShouldMapToV2pnTonesOnly(string colour, string tone)
    {
        await Parse(("sub-info-text", "Hello"), ("sub-info-color", colour)).InfoTone.Should().BeEqualTo(tone);
    }

    [Test]
    public async Task InfoBanner_ShouldBeOffWithZeroOrEmptyText()
    {
        await Parse(("sub-info-text", "0")).InfoText.Should().BeNull();
        await Parse(("sub-info-text", "  ")).InfoText.Should().BeNull();
        await Parse(("sub-info-color", "red")).InfoText.Should().BeNull();
    }

    [Test]
    public async Task InfoBanner_ShouldClipTextAndButton()
    {
        var info = Parse(
            ("sub-info-text", new string('a', 300)),
            ("sub-info-button-text", "A very long button label that goes on"),
            ("sub-info-button-link", "https://example.com/"));
        await info.InfoText!.Length.Should().BeEqualTo(201);
        await info.InfoText.Should().EndWith("…");
        await info.InfoButtonText!.Length.Should().BeLessThanOrEqualTo(26);
    }

    [Test]
    public async Task InfoBanner_ShouldDropButtonWithoutSafeLink()
    {
        var info = Parse(("sub-info-text", "Hi"), ("sub-info-button-text", "Go"), ("sub-info-button-link", "http://example.com/"));
        await info.InfoButtonUrl.Should().BeNull();
        await info.InfoButtonText.Should().BeNull();
    }

    [Test]
    public async Task IncyBanner_ShouldBeUsedWhenHappSentNone()
    {
        var info = Parse(
            ("banner-text", "base64:" + B64("Акция! Скидка 50%")),
            ("banner-button-text", "Подробнее"),
            ("banner-button-url", "https://example.com/promo"),
            ("banner-bg-color", "#E53E3E"));
        await info.InfoText.Should().BeEqualTo("Акция! Скидка 50%");
        await info.InfoTone.Should().BeEqualTo(SubPanelHeaders.ToneInfo);
        await info.InfoButtonUrl.Should().BeEqualTo("https://example.com/promo");
        await info.InfoButtonText.Should().BeEqualTo("Подробнее");
    }

    [Test]
    public async Task IncyBanner_ShouldTakeShortAliasThenPremiumUrlForTheButton()
    {
        await Parse(("banner-text", "Hi"), ("banner-url", "https://example.com/a"), ("premium-url", "https://example.com/p"))
            .InfoButtonUrl.Should().BeEqualTo("https://example.com/a");
        await Parse(("banner-text", "Hi"), ("premium-url", "https://example.com/p"))
            .InfoButtonUrl.Should().BeEqualTo("https://example.com/p");
        await Parse(("premium-url", "https://example.com/p")).InfoButtonUrl.Should().BeNull();
    }

    [Test]
    public async Task HappBanner_ShouldWinOverIncyBanner()
    {
        var info = Parse(("sub-info-text", "Happ"), ("banner-text", "INCY"), ("banner-button-url", "https://example.com/incy"));
        await info.InfoText.Should().BeEqualTo("Happ");
        await info.InfoButtonUrl.Should().BeNull();
    }

    [Test]
    public async Task ExpiryBanner_ShouldReadHappAndIncyFields()
    {
        var happ = Parse(("sub-expire", "1"), ("sub-expire-button-link", "https://t.me/renew_bot"));
        await happ.ExpiryNotice.Should().BeEqualTo(true);
        await happ.ExpiryButtonUrl.Should().BeEqualTo("https://t.me/renew_bot");
        await Parse(("sub-expire", "0")).ExpiryNotice.Should().BeEqualTo(false);
        await Parse(("sub-expire", "maybe")).ExpiryNotice.Should().BeNull();
        await Parse().ExpiryNotice.Should().BeNull();

        var incy = Parse(
            ("expiry-banner-text", "base64:" + B64("Продлите подписку")),
            ("expiry-banner-button-text", "Продлить"),
            ("expiry-banner-url", "https://example.com/short"),
            ("expiry-banner-button-url", "https://example.com/renew"),
            ("sub-expire-button-link", "https://example.com/happ"));
        await incy.ExpiryText.Should().BeEqualTo("Продлите подписку");
        await incy.ExpiryButtonText.Should().BeEqualTo("Продлить");
        // The full INCY name wins over its short alias, and INCY over Happ.
        await incy.ExpiryButtonUrl.Should().BeEqualTo("https://example.com/renew");
        await Parse(("expiry-banner-button-url", "http://example.com/renew")).ExpiryButtonUrl.Should().BeNull();
    }

    [Test]
    [Arguments("1", true)]
    [Arguments("true", true)]
    [Arguments("TRUE", true)]
    [Arguments("0", false)]
    [Arguments("false", false)]
    [Arguments("", false)]
    [Arguments("2", false)]
    public async Task Autoconnect_ShouldReadFlag(string value, bool expected)
    {
        await Parse(("subscription-autoconnect", value)).Autoconnect.Should().BeEqualTo(expected);
    }

    [Test]
    [Arguments("lastused", SubPanelHeaders.ConnectLastUsed)]
    [Arguments("LowestDelay", SubPanelHeaders.ConnectLowestDelay)]
    [Arguments("random", SubPanelHeaders.ConnectRandom)]
    [Arguments("fastest", null)]
    public async Task AutoconnectType_ShouldReadKnownTypes(string value, string? expected)
    {
        await Parse(("subscription-autoconnect-type", value)).AutoconnectType.Should().BeEqualTo(expected);
    }

    [Test]
    [Arguments("subscriptions-sort-type", "ping", SubPanelHeaders.SortPing)]
    [Arguments("subscriptions-sort-type", "alphabet", SubPanelHeaders.SortAlphabet)]
    [Arguments("subscriptions-sort-type", "without", SubPanelHeaders.SortWithout)]
    [Arguments("sort-order", "name", SubPanelHeaders.SortAlphabet)]
    [Arguments("sort-order", "none", SubPanelHeaders.SortWithout)]
    [Arguments("sort-order", "ping", SubPanelHeaders.SortPing)]
    [Arguments("subscriptions-sort-type", "random", null)]
    public async Task SortType_ShouldReadHappAndIncyNames(string header, string value, string? expected)
    {
        await Parse((header, value)).SortType.Should().BeEqualTo(expected);
    }

    [Test]
    public async Task SortType_HappShouldWinOverIncy()
    {
        await Parse(("subscriptions-sort-type", "without"), ("sort-order", "name")).SortType.Should().BeEqualTo(SubPanelHeaders.SortWithout);
    }

    [Test]
    public async Task PingDefaults_ShouldBeRead()
    {
        var info = Parse(("subscription-ping-onopen-enabled", "1"), ("ping-type", "Proxy-Head"), ("check-url-via-proxy", "https://cp.cloudflare.com/generate_204"));
        await info.PingOnOpen.Should().BeTrue();
        await info.PingType.Should().BeEqualTo("proxy-head");
        await info.CheckUrl.Should().BeEqualTo("https://cp.cloudflare.com/generate_204");
        await Parse(("ping-type", "udp")).PingType.Should().BeNull();
        await Parse(("check-url-via-proxy", "http://cp.cloudflare.com/generate_204")).CheckUrl.Should().BeNull();
    }

    [Test]
    public async Task UpdateOnOpen_ShouldReadV2rayTunAndHapp()
    {
        await Parse(("update-always", "true")).UpdateOnOpen.Should().BeTrue();
        await Parse(("subscription-auto-update-open-enable", "1")).UpdateOnOpen.Should().BeTrue();
        await Parse(("update-always", "false")).UpdateOnOpen.Should().BeFalse();
        await Parse().UpdateOnOpen.Should().BeFalse();
    }

    [Test]
    public async Task HideFlags_ShouldBeStoredOnly()
    {
        var info = Parse(("hide-settings", "1"), ("hide-url", "true"));
        await info.HideSettings.Should().BeTrue();
        await info.HideUrl.Should().BeTrue();
        await Parse(("hide-url", "0")).HideUrl.Should().BeFalse();
    }

    [Test]
    public async Task NewUrlAndDomain_ShouldBeValidated()
    {
        await Parse(("new-url", "https://new.example.com/sub/abc?x=1")).NewUrl.Should().BeEqualTo("https://new.example.com/sub/abc?x=1");
        await Parse(("new-url", "http://new.example.com/sub")).NewUrl.Should().BeNull();
        await Parse(("new-url", "https://u:p@new.example.com/sub")).NewUrl.Should().BeNull();
        await Parse(("new-url", "base64:" + B64("https://new.example.com/"))).NewUrl.Should().BeNull();
        await Parse(("new-domain", "New.Example.com")).NewDomain.Should().BeEqualTo("new.example.com");
        await Parse(("new-domain", "https://new.example.com")).NewDomain.Should().BeNull();
        await Parse(("new-domain", "new.example.com:8443")).NewDomain.Should().BeNull();
        await Parse(("new-domain", "new.example.com/path")).NewDomain.Should().BeNull();
        await Parse(("new-domain", "localhost")).NewDomain.Should().BeNull();
    }

    [Test]
    public async Task ReadBody_ShouldReadLeadingCommentsOfPlainList()
    {
        var body = "#profile-title: base64:" + B64("Мой VPN") + "\r\n#announce: Hello\n#sub-info-text: Body banner\n#unknown-key: x\n"
                   + "vless://id@192.0.2.1:443#A\n#sub-info-color: red\n";
        var meta = SubPanelHeaders.ReadBody(body);
        await meta["profile-title"].Should().BeEqualTo("base64:" + B64("Мой VPN"));
        await meta["announce"].Should().BeEqualTo("Hello");
        await meta["sub-info-text"].Should().BeEqualTo("Body banner");
        await meta.ContainsKey("unknown-key").Should().BeFalse();
        // Only above the links.
        await meta.ContainsKey("sub-info-color").Should().BeFalse();
    }

    [Test]
    public async Task ReadBody_ShouldReadBase64List()
    {
        var body = B64("#subscription-autoconnect: 1\n#subscription-autoconnect-type: lowestdelay\nvless://id@192.0.2.1:443#A\n");
        var info = SubPanelHeaders.Parse(SubPanelHeaders.WithBody(null, body));
        await info.Autoconnect.Should().BeTrue();
        await info.AutoconnectType.Should().BeEqualTo(SubPanelHeaders.ConnectLowestDelay);
    }

    [Test]
    public async Task ReadBody_ShouldIgnoreConfigsAndHttpOnlyKeys()
    {
        await SubPanelHeaders.ReadBody("#announce: yaml comment\nport: 7890\nproxies:\n").Count.Should().BeEqualTo(0);
        await SubPanelHeaders.ReadBody("{\"outbounds\": []}").Count.Should().BeEqualTo(0);
        var meta = SubPanelHeaders.ReadBody("#new-url: https://evil.example/\n#new-domain: evil.example\n#v2pn-partner: https://evil.example/v2pn/\n#banner-text: INCY header only\nvless://id@192.0.2.1:443#A");
        await meta.Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task WithBody_HeadersShouldWinOverBody()
    {
        var body = "#announce: from body\n#announce-url: https://example.com/body\n#sub-info-text: body banner\nvless://id@192.0.2.1:443#A";
        var info = SubPanelHeaders.Parse(SubPanelHeaders.WithBody(Headers(("Announce", "from header")), body));
        await info.Announce.Should().BeEqualTo("from header");
        await info.AnnounceUrl.Should().BeEqualTo("https://example.com/body");
        await info.InfoText.Should().BeEqualTo("body banner");
    }

    [Test]
    public async Task Collect_ShouldKeepTheNewHeaders()
    {
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("x") };
        response.Headers.TryAddWithoutValidation("Sub-Info-Text", "Hi");
        response.Headers.TryAddWithoutValidation("new-domain", "new.example.com");
        response.Headers.TryAddWithoutValidation("x-unrelated", "1");
        var headers = SubPanelHeaders.Collect(response);
        await headers.ContainsKey("sub-info-text").Should().BeTrue();
        await headers.ContainsKey("new-domain").Should().BeTrue();
        await headers.ContainsKey("x-unrelated").Should().BeFalse();
    }
}
