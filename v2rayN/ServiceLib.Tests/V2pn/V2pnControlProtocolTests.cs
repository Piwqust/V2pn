namespace ServiceLib.Tests.V2pn;

/// <summary>The lines of the control channel, the address of a copy of V2pn, and v2pnctl's words.</summary>
public class V2pnControlProtocolTests
{
    [Test]
    public async Task ARequestReadsBackAsItWasWritten()
    {
        var line = new V2pnControlRequest(7, "route", new JsonObject { ["route"] = "all", ["confirmWithin"] = 20 }, "mcp").ToLine();

        var request = V2pnControlRequest.Parse(line, out var refusal);

        await refusal.Should().BeNull();
        await request!.Id.Should().BeEqualTo(7L);
        await request.Command.Should().BeEqualTo("route");
        await request.Source.Should().BeEqualTo("mcp");
        await V2pnControlJson.String(request.Args["route"]).Should().BeEqualTo("all");
        await V2pnControlJson.Long(request.Args["confirmWithin"]).Should().BeEqualTo(20L);
        await line.Should().Contain("\"v\":1");
        await line.Should().NotContain("\n");
    }

    [Test]
    public async Task ARequestCarriesItsLanguage_OnlyWhenItIsRussian()
    {
        var russian = new V2pnControlRequest(3, "status", new JsonObject(), "cli", V2pnControlText.Russian.Code).ToLine();
        var english = new V2pnControlRequest(3, "status", new JsonObject(), "cli").ToLine();

        var back = V2pnControlRequest.Parse(russian, out var r1);
        var plain = V2pnControlRequest.Parse(english, out var r2);

        await r1.Should().BeNull();
        await russian.Should().Contain("\"lang\":\"ru\"");
        await russian.Should().Contain("\"v\":1");
        await back!.Lang.Should().BeEqualTo("ru");
        await back.Text.Ru.Should().BeTrue();
        await back.Command.Should().BeEqualTo("status");
        await r2.Should().BeNull();
        await english.Should().NotContain("lang");
        await plain!.Lang.Should().BeNull();
        await plain.Text.Should().BeEqualTo(V2pnControlText.English);
    }

    [Test]
    public async Task TheLanguageKey_IsAllThatIsAdded_SoAnOlderV2pnReadsTheRequestAsBefore()
    {
        // An older V2pn looks up v, id, cmd, args and src by name and never sees the rest; the line is otherwise the same.
        var args = new JsonObject { ["route"] = "all", ["confirmWithin"] = 20 };
        var withLang = (JsonObject)JsonNode.Parse(new V2pnControlRequest(9, "route", args, "cli", "ru").ToLine())!;
        var without = new V2pnControlRequest(9, "route", args, "cli").ToLine();

        withLang.Remove("lang");

        await withLang.ToJsonString(V2pnControlProtocol.Compact).Should().BeEqualTo(without);
    }

    [Test]
    public async Task ALineWithoutLanguage_FromAnOlderV2pnctl_IsEnglish_AndOddValuesAreToo()
    {
        var old = V2pnControlRequest.Parse("""{"v":1,"id":2,"cmd":"servers","args":{"country":"DE"},"src":"cli"}""", out var e1);
        var regional = V2pnControlRequest.Parse("""{"v":1,"id":2,"cmd":"status","lang":"ru-RU"}""", out _);
        var other = V2pnControlRequest.Parse("""{"v":1,"id":2,"cmd":"status","lang":"de"}""", out _);
        var notText = V2pnControlRequest.Parse("""{"v":1,"id":2,"cmd":"status","lang":7}""", out var e2);

        await e1.Should().BeNull();
        await old!.Lang.Should().BeNull();
        await V2pnControlJson.String(old.Args["country"]).Should().BeEqualTo("DE");
        await regional!.Lang.Should().BeEqualTo("ru");
        await other!.Lang.Should().BeNull();
        await e2.Should().BeNull();
        await notText!.Lang.Should().BeNull();
    }

    [Test]
    public async Task ARefusal_IsInTheRequestsLanguage()
    {
        V2pnControlRequest.Parse("""{"v":2,"id":3,"cmd":"status","lang":"ru"}""", out var version);
        V2pnControlRequest.Parse("""{"v":1,"id":3,"lang":"ru"}""", out var noCommand);

        await version!.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrVersion);
        await version.ErrorMessage!.Should().Contain("в версии 2");
        await noCommand!.ErrorMessage.Should().BeEqualTo("В запросе нет команды.");
    }

    [Test]
    public async Task ARequestOfAnotherVersionIsRefusedWithAReason()
    {
        V2pnControlRequest.Parse("""{"v":2,"id":3,"cmd":"status"}""", out var refusal);

        await refusal!.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrVersion);
        await refusal.Id.Should().BeEqualTo(3L);
        await refusal.ErrorMessage!.Should().Contain("version 2");
    }

    [Test]
    public async Task NotJsonOrNoCommandIsABadRequest()
    {
        V2pnControlRequest.Parse("status please", out var notJson);
        V2pnControlRequest.Parse("""{"v":1,"id":1}""", out var noCommand);
        V2pnControlRequest.Parse("""{"v":1,"id":1,"cmd":"use","args":[1]}""", out var badArgs);

        await notJson!.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrBadRequest);
        await noCommand!.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrBadRequest);
        await badArgs!.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrBadRequest);
    }

    [Test]
    public async Task AnUnknownSourceCountsAsTheTerminal()
    {
        var request = V2pnControlRequest.Parse("""{"v":1,"id":1,"cmd":"status","src":"evil"}""", out _);

        await request!.Source.Should().BeEqualTo(V2pnControlProtocol.SourceCli);
    }

    [Test]
    public async Task AnAnswerReadsBack_AndAnotherVersionIsAFailure()
    {
        var ok = V2pnControlAnswer.Parse(V2pnControlAnswer.Success(4, new JsonObject { ["message"] = "hi" }).ToLine(), 4);
        var failed = V2pnControlAnswer.Parse(V2pnControlAnswer.Failure(4, "disabled", "off").ToLine(), 4);
        var future = V2pnControlAnswer.Parse("""{"v":9,"id":4,"ok":true,"data":{}}""", 4);
        var garbage = V2pnControlAnswer.Parse("<html>", 4);

        await ok.Ok.Should().BeTrue();
        await V2pnControlJson.String(ok.Data?["message"]).Should().BeEqualTo("hi");
        await failed.ErrorCode.Should().BeEqualTo("disabled");
        await failed.ErrorMessage.Should().BeEqualTo("off");
        await future.ErrorCode.Should().BeEqualTo(V2pnControlProtocol.ErrVersion);
        await garbage.Ok.Should().BeFalse();
    }

    [Test]
    public async Task ExitCodesSayWhatHappened()
    {
        static int Code(string? error) => V2pnControlProtocol.ExitCode(error == null ? V2pnControlAnswer.Success(1, null) : V2pnControlAnswer.Failure(1, error, ""));

        await Code(null).Should().BeEqualTo(0);
        await Code(V2pnControlProtocol.ErrFailed).Should().BeEqualTo(1);
        await Code(V2pnControlProtocol.ErrNeedsConfirmation).Should().BeEqualTo(2);
        await Code(V2pnControlProtocol.ErrNotRunning).Should().BeEqualTo(3);
        await Code(V2pnControlProtocol.ErrDisabled).Should().BeEqualTo(4);
        await Code(V2pnControlProtocol.ErrForbidden).Should().BeEqualTo(4);
    }

    [Test]
    public async Task OneCopyHasOneAddress_AnotherCopyAnother()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2pn-ctl-key");
        var copy = Path.Combine(root, "a");

        await V2pnControlAddress.Key(copy + Path.DirectorySeparatorChar).Should().BeEqualTo(V2pnControlAddress.Key(copy));
        await V2pnControlAddress.Key(copy).Should().NotBeEqualTo(V2pnControlAddress.Key(Path.Combine(root, "b")));
        if (OperatingSystem.IsWindows())
        {
            await V2pnControlAddress.Key(copy.ToUpperInvariant()).Should().BeEqualTo(V2pnControlAddress.Key(copy));
            await V2pnControlEndpoint.For(copy).PipeName!.Should().StartWith("V2pn.control.");
        }
        else
        {
            // A Mac allows 104 bytes for a socket's path.
            await (Encoding.UTF8.GetByteCount(V2pnControlEndpoint.For(copy).SocketPath!) < 104).Should().BeTrue();
        }
    }

    [Test]
    public async Task TheCopyIsFoundFromItsFolderOrItsProgram()
    {
        var folder = Path.Combine(Path.GetTempPath(), "v2pn-ctl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var exe = Path.Combine(folder, "V2pn.exe");
                await File.WriteAllTextAsync(exe, "");
                await V2pnControlAddress.ResolveFolder(exe, "C:\\elsewhere").Should().BeEqualTo(folder);
                await V2pnControlAddress.ResolveFolder(folder, "C:\\elsewhere").Should().BeEqualTo(folder);
                // Without a choice: the copy v2pnctl sits in.
                await V2pnControlAddress.ResolveFolder(null, folder).Should().BeEqualTo(folder);
                await V2pnControlAddress.ResolveFolder(null, Path.Combine(folder, "none")).Should()
                    .BeEqualTo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "V2pn"));
            }
            else
            {
                var macos = Path.Combine(folder, "V2pn.app", "Contents", "MacOS");
                Directory.CreateDirectory(macos);
                await V2pnControlAddress.ResolveFolder(folder, "/x").Should().BeEqualTo(macos);
                await V2pnControlAddress.ResolveFolder(Path.Combine(folder, "V2pn.app"), "/x").Should().BeEqualTo(macos);
            }
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static V2pnCtlCommandLine Parse(string text, out string? error) =>
        V2pnControlCli.Parse(text.Split(' ', StringSplitOptions.RemoveEmptyEntries), out error);

    [Test]
    public async Task TheCommandLineBecomesOneRequest()
    {
        var route = Parse("--instance C:\\sandbox --json route all --confirm-within 20", out var e1);
        var use = Parse("use Frankfurt 2", out var e2);
        var fastest = Parse("use --fastest --country de", out var e3);
        var servers = Parse("servers --country=nl", out var e4);
        var mode = Parse("mode VPN", out var e5);

        await e1.Should().BeNull();
        await route.Instance.Should().BeEqualTo("C:\\sandbox");
        await route.Json.Should().BeTrue();
        await route.Command.Should().BeEqualTo("route");
        await V2pnControlJson.String(route.Args["route"]).Should().BeEqualTo("all");
        await V2pnControlJson.Long(route.Args["confirmWithin"]).Should().BeEqualTo(20L);
        await e2.Should().BeNull();
        await V2pnControlJson.String(use.Args["server"]).Should().BeEqualTo("Frankfurt 2");
        await e3.Should().BeNull();
        await V2pnControlJson.Bool(fastest.Args["fastest"]).Should().BeEqualTo(true);
        await V2pnControlJson.String(fastest.Args["country"]).Should().BeEqualTo("DE");
        await e4.Should().BeNull();
        await V2pnControlJson.String(servers.Args["country"]).Should().BeEqualTo("NL");
        await e5.Should().BeNull();
        await V2pnControlJson.String(mode.Args["mode"]).Should().BeEqualTo("vpn");
    }

    [Test]
    public async Task AWrongCommandLineSaysWhy()
    {
        foreach (var text in new[]
                 {
                     "route everywhere", "mode tunnel", "use", "use x --fastest", "status --confirm-within 30",
                     "disconnect --confirm-within 2", "disconnect --confirm-within soon", "servers --country Germany",
                     "connect now", "teleport", "status --verbose", "--instance",
                 })
        {
            Parse(text, out var error);
            await error.Should().NotBeNull();
        }
    }

    [Test]
    public async Task NoCommandOrHelpShowsTheUsage()
    {
        var none = Parse("", out var e1);
        var help = Parse("status --help", out var e2);

        await e1.Should().BeNull();
        await none.Command.Should().BeEqualTo(V2pnControlCli.Help);
        await e2.Should().BeNull();
        await help.Command.Should().BeEqualTo(V2pnControlCli.Help);
        await V2pnControlCli.Usage().Should().Contain("--confirm-within");
    }

    [Test]
    public async Task TheStatusReadsAsAFewLines()
    {
        var status = new JsonObject
        {
            ["verdict"] = "vpn-connected",
            ["mode"] = "vpn",
            ["server"] = new JsonObject { ["name"] = "Frankfurt 1", ["country"] = "DE" },
            ["route"] = "ru-direct",
            ["check"] = new JsonObject { ["state"] = "Available", ["ms"] = 120, ["at"] = "2026-11-24T10:00:00Z" },
            ["pendingConfirm"] = new JsonObject { ["secondsLeft"] = 42 },
        };

        var text = V2pnControlCli.Format("status", V2pnControlAnswer.Success(1, status), new DateTime(2026, 11, 24, 10, 0, 30, DateTimeKind.Utc));

        await text.Should().Contain("Connected in VPN mode");
        await text.Should().Contain("Server: Frankfurt 1 (DE)");
        await text.Should().Contain("Russian sites directly");
        await text.Should().Contain("Check: Available, 120 ms, 30 s ago");
        await text.Should().Contain("42 s left");
    }

    private const int WindowsEnglish = 0x0409;
    private const int WindowsRussian = 0x0419;

    [Test]
    public async Task TheLanguage_V2pnLangFirst_ThenWindowsInterfaceLanguage()
    {
        static bool Ru(string? v2pnLang, int ui, bool console = true) =>
            V2pnControlCli.Language(v2pnLang, windows: true, ui, lcAll: null, lcMessages: null, lang: "ru_RU.UTF-8", console).Ru;

        await Ru(null, WindowsRussian).Should().BeTrue();
        // Russian of another country (Moldova): the same primary language.
        await Ru(null, 0x0819).Should().BeTrue();
        await Ru(null, WindowsEnglish).Should().BeFalse();
        // Ukrainian is not Russian; LANG means nothing on Windows.
        await Ru(null, 0x0422).Should().BeFalse();
        await Ru("ru", WindowsEnglish).Should().BeTrue();
        await Ru("RU", WindowsEnglish).Should().BeTrue();
        await Ru("en", WindowsRussian).Should().BeFalse();
        await Ru("de", WindowsRussian).Should().BeFalse();
        await Ru("  ", WindowsRussian).Should().BeTrue();
        // A console on an English code page would print question marks, unless Russian was asked for by name.
        await Ru(null, WindowsRussian, console: false).Should().BeFalse();
        await Ru("ru", WindowsRussian, console: false).Should().BeTrue();
    }

    [Test]
    public async Task TheLanguage_OnAMacOrLinux_IsTheFirstLocaleVariableThatIsSet()
    {
        static bool Ru(string? lcAll, string? lcMessages, string? lang) =>
            V2pnControlCli.Language(null, windows: false, WindowsRussian, lcAll, lcMessages, lang).Ru;

        await Ru(null, null, "ru_RU.UTF-8").Should().BeTrue();
        await Ru(null, "ru_RU.UTF-8", "en_US.UTF-8").Should().BeTrue();
        await Ru("en_US.UTF-8", "ru_RU.UTF-8", "ru_RU.UTF-8").Should().BeFalse();
        await Ru("", "", "ru_RU.UTF-8").Should().BeTrue();
        await Ru(null, null, "C").Should().BeFalse();
        await Ru(null, null, null).Should().BeFalse();
        await V2pnControlCli.Language("ru", false, 0, "en_US.UTF-8", null, null).Ru.Should().BeTrue();
    }

    [Test]
    public async Task RussianPlurals()
    {
        static string Days(int n) => $"{n} {V2pnControlText.Plural(n, "день", "дня", "дней")}";

        await Days(1).Should().BeEqualTo("1 день");
        await Days(3).Should().BeEqualTo("3 дня");
        await Days(5).Should().BeEqualTo("5 дней");
        await Days(11).Should().BeEqualTo("11 дней");
        await Days(12).Should().BeEqualTo("12 дней");
        await Days(21).Should().BeEqualTo("21 день");
        await Days(24).Should().BeEqualTo("24 дня");
        await Days(111).Should().BeEqualTo("111 дней");
    }

    private static readonly V2pnControlText Russian = V2pnControlText.Russian;

    [Test]
    public async Task ARussianCommandLine_SaysWhyInRussian()
    {
        V2pnControlCli.Parse("route everywhere".Split(' '), out var ru, Russian);
        V2pnControlCli.Parse("route everywhere".Split(' '), out var en);

        await ru.Should().BeEqualTo("После route нужно ru-direct, blocked-only или all.");
        await en.Should().BeEqualTo("route needs ru-direct, blocked-only or all.");
        foreach (var text in new[]
                 {
                     "mode tunnel", "use", "use x --fastest", "status --confirm-within 30", "disconnect --confirm-within 2",
                     "servers --country Germany", "servers Germany", "connect now", "teleport", "status --verbose", "--instance",
                 })
        {
            V2pnControlCli.Parse(text.Split(' ', StringSplitOptions.RemoveEmptyEntries), out var error, Russian);
            await error.Should().NotBeNull();
            // Every reason has Russian words, not only the command's name.
            await error!.Any(c => c is >= 'а' and <= 'я').Should().BeTrue();
        }
    }

    [Test]
    public async Task TheRussianUsage_NamesTheWindowsSetting_AndLinesUp()
    {
        var usage = V2pnControlCli.Usage(Russian);

        await usage.Should().Contain("«Управление из командной строки и ИИ»");
        await usage.Should().Contain("route ru-direct|blocked-only|all");
        await usage.Should().Contain("--confirm-within <секунды>");
        await usage.Should().Contain("V2PN_LANG=en");
        await V2pnControlCli.Usage().Should().Contain("V2PN_LANG=en");
        // A command and its words: the words start in column 33 in both languages, and so do the lines that go on below.
        var columns = new List<int>();
        foreach (var text in new[] { usage, V2pnControlCli.Usage() })
        {
            foreach (var line in text.Split('\n').Select(l => l.TrimEnd('\r')))
            {
                if (System.Text.RegularExpressions.Regex.Match(line, @"^  \S.*?\s{2,}(\S)") is { Success: true } m)
                {
                    columns.Add(m.Groups[1].Index);
                }
                else if (line.StartsWith(new string(' ', 4), StringComparison.Ordinal))
                {
                    columns.Add(line.Length - line.TrimStart().Length);
                }
            }
        }
        // Twenty lines with words in each language.
        await columns.Count.Should().BeEqualTo(40);
        await columns.All(c => c == 33).Should().BeTrue();
    }

    [Test]
    public async Task TheRussianStatus_UsesTheWindowsWords()
    {
        var status = new JsonObject
        {
            ["verdict"] = "vpn-connected",
            ["mode"] = "vpn",
            ["server"] = new JsonObject { ["name"] = "Франкфурт 1", ["country"] = "DE" },
            ["route"] = "ru-direct",
            ["check"] = new JsonObject { ["state"] = "Available", ["ms"] = 120, ["at"] = "2026-11-24T10:00:00Z" },
            ["pendingConfirm"] = new JsonObject { ["secondsLeft"] = 42 },
        };
        var off = new JsonObject
        {
            ["verdict"] = "off",
            ["mode"] = "proxy",
            ["route"] = "none",
            ["check"] = new JsonObject { ["state"] = "TimedOut", ["at"] = "2026-11-21T10:00:00Z" },
        };
        var now = new DateTime(2026, 11, 24, 10, 0, 30, DateTimeKind.Utc);

        var text = V2pnControlCli.Format("status", V2pnControlAnswer.Success(1, status), now, Russian);
        var offText = V2pnControlCli.FormatStatus(off, now, Russian);

        await text.Split('\n')[0].TrimEnd().Should().BeEqualTo("Подключено в режиме VPN");
        await text.Should().Contain("Сервер: Франкфурт 1 (DE)");
        await text.Should().Contain("Режим: VPN");
        await text.Should().Contain("Маршрут: Российские сайты напрямую (ru-direct)");
        await text.Should().Contain("Проверка: работает, 120 мс, 30 сек назад");
        await text.Should().Contain("осталось 42 сек");
        await offText.Should().Contain("Отключено");
        await offText.Should().Contain("Сервер: не выбран");
        await offText.Should().Contain("Режим: прокси");
        await offText.Should().Contain("Маршрут: не выбран");
        await offText.Should().Contain("Проверка: истекло время проверки, 3 дня назад");
    }

    [Test]
    public async Task TheRussianServers_AreATableWithTheWindowsColumns()
    {
        var servers = new JsonArray(
            new JsonObject { ["id"] = "a1", ["name"] = "Франкфурт", ["country"] = "DE", ["delayMs"] = 85, ["favorite"] = true, ["active"] = true },
            new JsonObject { ["id"] = "b22", ["name"] = "Amsterdam", ["test"] = "failed" });

        var lines = V2pnControlCli.FormatServers(servers, Russian).Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        await lines.Count.Should().BeEqualTo(3);
        await lines[0].Should().Contain("СТРАНА");
        await lines[0].Should().Contain("ПИНГ");
        await lines[0].Should().Contain("НАЗВАНИЕ");
        await lines[1].Should().Contain("85 мс");
        await lines[2].Should().Contain("нет ответа");
        // The columns line up under their headers.
        await lines[1].IndexOf("Франкфурт", StringComparison.Ordinal).Should().BeEqualTo(lines[0].IndexOf("НАЗВАНИЕ", StringComparison.Ordinal));
        await lines[2].IndexOf("Amsterdam", StringComparison.Ordinal).Should().BeEqualTo(lines[0].IndexOf("НАЗВАНИЕ", StringComparison.Ordinal));
        await V2pnControlCli.FormatServers(new JsonArray(), Russian).Should().BeEqualTo("Серверов нет.");
    }

    [Test]
    public async Task TheRussianSubscriptions_TellTimeTrafficAndExpiry()
    {
        var now = new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc);
        var subs = new JsonArray(
            new JsonObject
            {
                ["name"] = "Мой провайдер", ["servers"] = 40, ["updated"] = "2026-11-24T09:55:00Z", ["lastResult"] = "Ok",
                ["expires"] = "2026-12-24T10:00:00Z", ["trafficUsedBytes"] = 1610612736L, ["trafficTotalBytes"] = 53687091200L,
            },
            new JsonObject { ["name"] = "Запасная", ["servers"] = 0, ["lastResult"] = "Unreachable", ["expires"] = "2026-11-01T00:00:00Z", ["trafficUsedBytes"] = 2147483648L });

        var lines = V2pnControlCli.FormatSubs(subs, now, Russian).Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        await lines[0].Should().Contain("НАЗВАНИЕ");
        await lines[0].Should().Contain("ТРАФИК");
        await lines[1].Should().Contain("5 мин назад (успешно)");
        await lines[1].Should().Contain("через 30 дней");
        await lines[1].Should().Contain("1,5 из 50 ГБ");
        await lines[2].Should().Contain("не обновлялась (сайт недоступен)");
        await lines[2].Should().Contain("истекла");
        await lines[2].Should().Contain("израсходовано 2 ГБ");
        await V2pnControlCli.FormatSubs(new JsonArray(), now, Russian).Should().BeEqualTo("Подписок нет.");
        // English as before.
        await V2pnControlCli.FormatSubs(subs, now).Should().Contain("1.5 of 50 GB");
    }

    [Test]
    public async Task AFailure_IsPrintedAsV2pnSaidIt()
    {
        var failed = V2pnControlAnswer.Failure(1, V2pnControlProtocol.ErrNoServer, "Сервер не выбран.");

        await V2pnControlCli.Format("connect", failed, DateTime.UtcNow, Russian).Should().BeEqualTo("Сервер не выбран.");
        await V2pnControlCli.Format("connect", new V2pnControlAnswer(1, false, null, null, null), DateTime.UtcNow, Russian).Should().BeEqualTo("Не получилось.");
    }
}
