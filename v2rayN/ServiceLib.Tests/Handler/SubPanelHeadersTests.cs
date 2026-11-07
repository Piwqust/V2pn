namespace ServiceLib.Tests.Handler;

public class SubPanelHeadersTests
{
    private static string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private static Dictionary<string, string> Headers(params (string Name, string Value)[] headers) =>
        headers.ToDictionary(h => h.Name, h => h.Value, StringComparer.OrdinalIgnoreCase);

    [Test]
    public async Task DecodeText_ShouldReadPlainAndBase64Titles()
    {
        await SubPanelHeaders.DecodeText("My VPN").Should().BeEqualTo("My VPN");
        await SubPanelHeaders.DecodeText("  My VPN  ").Should().BeEqualTo("My VPN");
        await SubPanelHeaders.DecodeText("base64:" + B64("Мой VPN 🚀")).Should().BeEqualTo("Мой VPN 🚀");
        await SubPanelHeaders.DecodeText("BASE64:" + B64("Upper prefix")).Should().BeEqualTo("Upper prefix");
    }

    [Test]
    public async Task DecodeText_ShouldAcceptUrlSafeBase64WithoutPadding()
    {
        // "Привет?>" encodes to a string with + and / and padding.
        var standard = B64("Привет?>");
        var urlSafe = standard.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        await SubPanelHeaders.DecodeText("base64:" + urlSafe).Should().BeEqualTo("Привет?>");
    }

    [Test]
    public async Task DecodeText_ShouldRepairUtf8ReadAsLatin1()
    {
        // HttpClient reads raw header bytes as Latin-1; a panel that sends UTF-8 unencoded arrives like this.
        var garbled = Encoding.Latin1.GetString(Encoding.UTF8.GetBytes("Мой провайдер"));
        await SubPanelHeaders.DecodeText(garbled).Should().BeEqualTo("Мой провайдер");
        // Real Latin-1 text that is not valid UTF-8 stays as it is.
        await SubPanelHeaders.DecodeText("Café").Should().BeEqualTo("Café");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("base64:")]
    [Arguments("base64:!!!not base64!!!")]
    [Arguments("base64:A")]
    public async Task DecodeText_ShouldReturnNullForEmptyOrBrokenValues(string? value)
    {
        await SubPanelHeaders.DecodeText(value).Should().BeNull();
    }

    [Test]
    public async Task DecodeText_ShouldRejectBase64ThatIsNotUtf8()
    {
        var notUtf8 = Convert.ToBase64String([0xFF, 0xFE, 0xC0, 0x00]);
        await SubPanelHeaders.DecodeText("base64:" + notUtf8).Should().BeNull();
    }

    [Test]
    [Arguments("attachment; filename=\"provider.txt\"", "provider")]
    [Arguments("attachment; filename=provider", "provider")]
    [Arguments("attachment;filename=sub.yaml", "sub")]
    [Arguments("attachment; filename=\"semi;colon.json\"", "semi;colon")]
    [Arguments("attachment; filename=\"esc\\\"aped\"", "esc\"aped")]
    [Arguments("attachment; filename=\"dir/inner.txt\"", "inner")]
    [Arguments("inline; filename=.txt", ".txt")]
    public async Task FileName_ShouldReadPlainFileNames(string header, string expected)
    {
        await SubPanelHeaders.FileName(header).Should().BeEqualTo(expected);
    }

    [Test]
    public async Task FileName_ShouldDecodeRfc5987Cyrillic()
    {
        const string header = "attachment; filename*=UTF-8''%D0%9C%D0%BE%D0%B9%20VPN.txt";
        await SubPanelHeaders.FileName(header).Should().BeEqualTo("Мой VPN");
    }

    [Test]
    public async Task FileName_ShouldPreferExtendedOverPlain()
    {
        const string header = "attachment; filename=\"fallback.txt\"; filename*=utf-8'ru'%D0%9F%D1%80%D0%BE%D0%B2%D0%B0%D0%B9%D0%B4%D0%B5%D1%80";
        await SubPanelHeaders.FileName(header).Should().BeEqualTo("Провайдер");
    }

    [Test]
    public async Task FileName_ShouldDecodePercentEncodedOrRawUtf8PlainNames()
    {
        await SubPanelHeaders.FileName("attachment; filename=%D0%9C%D0%BE%D0%B9.txt").Should().BeEqualTo("Мой");
        var garbled = Encoding.Latin1.GetString(Encoding.UTF8.GetBytes("attachment; filename=\"Мой VPN\""));
        await SubPanelHeaders.FileName(garbled).Should().BeEqualTo("Мой VPN");
    }

    [Test]
    public async Task FileName_ShouldReadIso88591ExtendedValues()
    {
        await SubPanelHeaders.FileName("attachment; filename*=iso-8859-1''caf%E9").Should().BeEqualTo("café");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("attachment")]
    [Arguments("attachment; filename=")]
    [Arguments("attachment; filename=\"\"")]
    [Arguments(";;;=;=")]
    [Arguments("attachment; filename*=no-quotes-here")]
    [Arguments("attachment; filename*=UTF-8''%FF%FE")]
    public async Task FileName_ShouldReturnNullWithoutAUsableName(string? header)
    {
        await SubPanelHeaders.FileName(header).Should().BeNull();
    }

    [Test]
    public async Task Parse_ShouldPreferProfileTitleOverContentDisposition()
    {
        var info = SubPanelHeaders.Parse(Headers(
            ("Profile-Title", "base64:" + B64("Панель")),
            ("Content-Disposition", "attachment; filename=\"user123\"")));
        await info.Title.Should().BeEqualTo("Панель");
    }

    [Test]
    public async Task Parse_ShouldFallBackToContentDisposition()
    {
        var info = SubPanelHeaders.Parse(Headers(
            ("profile-title", "base64:@@@"),
            ("content-disposition", "attachment; filename*=UTF-8''%D0%9F%D0%B0%D0%BD%D0%B5%D0%BB%D1%8C")));
        await info.Title.Should().BeEqualTo("Панель");
    }

    [Test]
    public async Task Parse_ShouldLeaveTitleEmptyWhenThePanelGivesNone()
    {
        var info = SubPanelHeaders.Parse(Headers(("subscription-userinfo", "upload=1")));
        await info.Title.Should().BeNull();
        await SubPanelHeaders.Parse(null).Title.Should().BeNull();
        await SubPanelHeaders.Parse(Headers()).HasUserInfo.Should().BeFalse();
    }

    [Test]
    public async Task Parse_ShouldMakeTitlesOneLine()
    {
        var info = SubPanelHeaders.Parse(Headers(("profile-title", "base64:" + B64("Line one\r\nline two\tend"))));
        await info.Title.Should().BeEqualTo("Line one line twoend");
    }

    [Test]
    public async Task Parse_ShouldReadEverythingAPanelSends()
    {
        var info = SubPanelHeaders.Parse(Headers(
            ("profile-title", "My VPN"),
            ("subscription-userinfo", "upload=1024; download=2048; total=107374182400; expire=1765497600"),
            ("profile-update-interval", "12"),
            ("support-url", "https://t.me/my_vpn_support"),
            ("profile-web-page-url", "https://panel.example.com/sub/abc"),
            ("announce", "base64:" + B64("Работы 12.12\nс 02:00 до 04:00"))));

        await info.Title.Should().BeEqualTo("My VPN");
        await info.HasUserInfo.Should().BeTrue();
        await info.Upload.Should().BeEqualTo(1024L);
        await info.Download.Should().BeEqualTo(2048L);
        await info.Total.Should().BeEqualTo(107374182400L);
        await info.Expire.Should().BeEqualTo(1765497600L);
        await info.UpdateIntervalHours.Should().BeEqualTo(12);
        await info.SupportUrl.Should().BeEqualTo("https://t.me/my_vpn_support");
        await info.WebPageUrl.Should().BeEqualTo("https://panel.example.com/sub/abc");
        await info.Announce.Should().BeEqualTo("Работы 12.12\nс 02:00 до 04:00");
    }

    [Test]
    public async Task TryParseUserInfo_ShouldAcceptAnyOrderAndMissingParts()
    {
        await SubPanelHeaders.TryParseUserInfo("expire=1700000000;total=10; download=3", out var up, out var down, out var total, out var expire).Should().BeTrue();
        await up.Should().BeEqualTo(0L);
        await down.Should().BeEqualTo(3L);
        await total.Should().BeEqualTo(10L);
        await expire.Should().BeEqualTo(1700000000L);

        await SubPanelHeaders.TryParseUserInfo("download=5", out _, out down, out total, out expire).Should().BeTrue();
        await down.Should().BeEqualTo(5L);
        await total.Should().BeEqualTo(0L);
        await expire.Should().BeEqualTo(0L);
    }

    [Test]
    public async Task TryParseUserInfo_ShouldKeepZeroTotalAndZeroExpire()
    {
        // total=0 is unlimited, expire=0 never expires: both stay 0 for the UI to read that way.
        await SubPanelHeaders.TryParseUserInfo("upload=0; download=1073741824; total=0; expire=0", out _, out var down, out var total, out var expire).Should().BeTrue();
        await down.Should().BeEqualTo(1073741824L);
        await total.Should().BeEqualTo(0L);
        await expire.Should().BeEqualTo(0L);
    }

    [Test]
    public async Task TryParseUserInfo_ShouldReadFloatsCaseAndSpacing()
    {
        await SubPanelHeaders.TryParseUserInfo(" Upload = 1.5E3 ;DOWNLOAD=2048.0; total=\"4096\"", out var up, out var down, out var total, out _).Should().BeTrue();
        await up.Should().BeEqualTo(1500L);
        await down.Should().BeEqualTo(2048L);
        await total.Should().BeEqualTo(4096L);
    }

    [Test]
    public async Task TryParseUserInfo_ShouldTurnMillisecondExpiryIntoSeconds()
    {
        await SubPanelHeaders.TryParseUserInfo("expire=1765497600000", out _, out _, out _, out var expire).Should().BeTrue();
        await expire.Should().BeEqualTo(1765497600L);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("garbage")]
    [Arguments("upload=; download=abc; total=NaN")]
    [Arguments("foo=1; bar=2")]
    [Arguments("=5;==;;")]
    public async Task TryParseUserInfo_ShouldReportNothingForGarbage(string? value)
    {
        await SubPanelHeaders.TryParseUserInfo(value, out var up, out var down, out var total, out var expire).Should().BeFalse();
        await (up + down + total + expire).Should().BeEqualTo(0L);
    }

    [Test]
    public async Task TryParseUserInfo_ShouldClampNegativeAndHugeValues()
    {
        await SubPanelHeaders.TryParseUserInfo("upload=-5; total=1e300", out var up, out _, out var total, out _).Should().BeTrue();
        await up.Should().BeEqualTo(0L);
        await total.Should().BeEqualTo(long.MaxValue);
    }

    [Test]
    [Arguments("12", 12)]
    [Arguments(" 24 ", 24)]
    [Arguments("1.5", 1)]
    [Arguments("0", 0)]
    [Arguments("-3", 0)]
    [Arguments("abc", 0)]
    [Arguments("", 0)]
    [Arguments("99999999", 8784)]
    public async Task ParseIntervalHours_ShouldBeLenient(string value, int expected)
    {
        await SubPanelHeaders.ParseIntervalHours(value).Should().BeEqualTo(expected);
    }

    [Test]
    [Arguments("https://t.me/support", "https://t.me/support")]
    [Arguments("http://example.com/", "http://example.com/")]
    [Arguments("tg://resolve?domain=support", "tg://resolve?domain=support")]
    [Arguments("javascript:alert(1)", null)]
    [Arguments("file:///C:/Windows/system32/calc.exe", null)]
    [Arguments("not a url", null)]
    [Arguments("", null)]
    public async Task ParseLink_ShouldOnlyAllowWebAndTelegramLinks(string value, string? expected)
    {
        await SubPanelHeaders.ParseLink(value).Should().BeEqualTo(expected);
    }

    [Test]
    public async Task ParseLink_ShouldAcceptBase64Links()
    {
        await SubPanelHeaders.ParseLink("base64:" + B64("https://example.com/help")).Should().BeEqualTo("https://example.com/help");
    }

    [Test]
    public async Task Parse_ShouldNotThrowOnGarbage()
    {
        string[] junk = ["", " ", "base64:", "base64:%%%", "=;=;=", "\u0000\u0001", new string('x', 100_000), "attachment; filename*=", "\"", "upload=1e999999", "😀", "%", "%G", "%E0%A4"];
        foreach (var value in junk)
        {
            var info = SubPanelHeaders.Parse(Headers(
                ("profile-title", value),
                ("content-disposition", value),
                ("subscription-userinfo", value),
                ("profile-update-interval", value),
                ("support-url", value),
                ("profile-web-page-url", value),
                ("announce", value)));
            await info.Should().NotBeNull();
        }
        await SubPanelHeaders.Parse(Headers(("profile-title", new string('a', 500)))).Title!.Length.Should().BeLessThanOrEqualTo(121);
    }

    [Test]
    public async Task ApplyToSubscription_ShouldNameAnEmptyOrPlaceholderSubscription()
    {
        foreach (var remarks in new[] { "", "import_sub", "panel.example.com" })
        {
            var item = new SubItem { Id = "1", Url = "https://panel.example.com/sub/abc", Remarks = remarks };
            var info = new SubInfoItem { Id = "1" };
            await SubInfoManager.ApplyToSubscription(item, info, new SubPanelInfo { Title = "Мой VPN" }).Should().BeTrue();
            await item.Remarks.Should().BeEqualTo("Мой VPN");
            await info.PanelName.Should().BeEqualTo("Мой VPN");
        }
    }

    [Test]
    public async Task ApplyToSubscription_ShouldFallBackToTheHost()
    {
        var item = new SubItem { Id = "1", Url = "https://panel.example.com/sub/abc", Remarks = "import_sub" };
        var info = new SubInfoItem { Id = "1" };
        SubInfoManager.ApplyToSubscription(item, info, new SubPanelInfo());
        await item.Remarks.Should().BeEqualTo("panel.example.com");
    }

    [Test]
    public async Task ApplyToSubscription_ShouldFollowThePanelUntilTheUserRenames()
    {
        var item = new SubItem { Id = "1", Url = "https://panel.example.com/sub/abc", Remarks = "" };
        var info = new SubInfoItem { Id = "1" };
        SubInfoManager.ApplyToSubscription(item, info, new SubPanelInfo { Title = "Old name" });
        SubInfoManager.ApplyToSubscription(item, info, new SubPanelInfo { Title = "New name" });
        await item.Remarks.Should().BeEqualTo("New name");

        item.Remarks = "Mine";
        await SubInfoManager.ApplyToSubscription(item, info, new SubPanelInfo { Title = "Newer name" }).Should().BeFalse();
        await item.Remarks.Should().BeEqualTo("Mine");
        SubInfoManager.ApplyToSubscription(item, info, new SubPanelInfo { Title = "Newest name" });
        await item.Remarks.Should().BeEqualTo("Mine");
    }

    [Test]
    public async Task ApplyToSubscription_ShouldUseThePanelIntervalOnlyWhenTheUserSetNone()
    {
        var item = new SubItem { Id = "1", Url = "https://p.example/s", Remarks = "Mine", AutoUpdateInterval = 0 };
        var info = new SubInfoItem { Id = "1" };
        SubInfoManager.ApplyToSubscription(item, info, new SubPanelInfo { UpdateIntervalHours = 12 });
        await item.AutoUpdateInterval.Should().BeEqualTo(720);

        // Still the panel's value, so it follows the panel.
        SubInfoManager.ApplyToSubscription(item, info, new SubPanelInfo { UpdateIntervalHours = 6 });
        await item.AutoUpdateInterval.Should().BeEqualTo(360);

        // The user's own value stays.
        item.AutoUpdateInterval = 45;
        SubInfoManager.ApplyToSubscription(item, info, new SubPanelInfo { UpdateIntervalHours = 24 });
        await item.AutoUpdateInterval.Should().BeEqualTo(45);

        // No interval from the panel: nothing changes.
        var other = new SubItem { Id = "2", Url = "https://p.example/s", Remarks = "Mine", AutoUpdateInterval = 0 };
        await SubInfoManager.ApplyToSubscription(other, new SubInfoItem { Id = "2" }, new SubPanelInfo()).Should().BeFalse();
        await other.AutoUpdateInterval.Should().BeEqualTo(0);
    }

    [Test]
    public async Task Collect_ShouldReadResponseAndContentHeaders()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("body") };
        response.Headers.TryAddWithoutValidation("Profile-Title", "base64:" + B64("Тест"));
        response.Headers.TryAddWithoutValidation("subscription-userinfo", "upload=1; download=2; total=3; expire=4");
        response.Headers.TryAddWithoutValidation("X-Unrelated", "ignored");
        response.Content.Headers.TryAddWithoutValidation("Content-Disposition", "attachment; filename=\"x.txt\"");

        var headers = SubPanelHeaders.Collect(response);
        await headers.Should().HaveCount(3);
        await headers["profile-title"].Should().BeEqualTo("base64:" + B64("Тест"));
        await headers["CONTENT-DISPOSITION"].Should().BeEqualTo("attachment; filename=\"x.txt\"");
        await headers.Should().NotContainKey("X-Unrelated");
    }
}
