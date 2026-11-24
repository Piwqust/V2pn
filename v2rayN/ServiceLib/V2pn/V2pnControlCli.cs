using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace ServiceLib;

// V2pn: compiled into ServiceLib (for the tests) and into v2pnctl, like V2pnControlProtocol.cs.

/// <summary>What v2pnctl was asked to do.</summary>
public sealed record V2pnCtlCommandLine(string Command, JsonObject Args, string? Instance, bool Json);

/// <summary>
/// V2pn: v2pnctl's words. The arguments become one request on the channel, and V2pn's answer becomes a few short lines
/// (or the answer itself with --json). Nothing here talks to V2pn, so all of it is tested. Every word comes in English
/// and Russian (<see cref="V2pnControlText"/>); commands, options and route names are typed, so they stay as they are.
/// </summary>
public static class V2pnControlCli
{
    public const string Help = "help";
    public const string VersionCommand = "version";
    public const string Mcp = "mcp";

    /// <summary>The variable that picks the language: "ru" or "en". Without it, the system's language decides.</summary>
    public const string LanguageVariable = "V2PN_LANG";

    // The Windows LANGID of Russian, without its sublanguage (LANG_RUSSIAN).
    private const int WindowsRussian = 0x19;

    /// <summary>
    /// The language v2pnctl speaks. <paramref name="v2pnLang"/> (V2PN_LANG) wins: "ru" is Russian, any other value
    /// English. Then the system's: on Windows the user's interface language (<paramref name="windowsUiLanguage"/>, a
    /// LANGID from GetUserDefaultUILanguage), elsewhere the first of LC_ALL, LC_MESSAGES and LANG that is set, as POSIX
    /// reads them. Russian only when that is Russian and the console can show Cyrillic; English otherwise.
    /// </summary>
    public static V2pnControlText Language(string? v2pnLang, bool windows, int windowsUiLanguage, string? lcAll, string? lcMessages, string? lang,
        bool consoleShowsCyrillic = true)
    {
        if (v2pnLang?.Trim() is { Length: > 0 } chosen)
        {
            return V2pnControlText.Of(chosen);
        }
        if (!consoleShowsCyrillic)
        {
            // A console on an English code page prints Russian as question marks.
            return V2pnControlText.English;
        }
        if (windows)
        {
            return (windowsUiLanguage & 0x3FF) == WindowsRussian ? V2pnControlText.Russian : V2pnControlText.English;
        }
        var locale = new[] { lcAll, lcMessages, lang }.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        return V2pnControlText.Of(locale);
    }

    private const string UsageEn = """
        v2pnctl: control V2pn from a terminal or an AI assistant.

        Usage: v2pnctl [--instance <folder>] [--json] <command> [arguments]

        Reading:
          status                         Connected or not, the server, the mode, the route, the last check
          servers [--country XX]         The servers: name, country, last delay, favourite (no addresses)
          subs                           The subscriptions: name, last update, expiry, traffic (no links)
          check                          Check the connection again
          diag                           The diagnostics report
          open                           Show V2pn's window

        Changing (Settings > General > Control from the command line and AI > Control):
          connect                        Connect with the chosen server, mode and route
          disconnect                     Disconnect
          use <name or id>               Choose a server; a connected V2pn switches to it at once
          use --fastest [--country XX]   Test the servers and choose the quickest
          mode proxy|vpn                 Proxy mode or VPN mode
          route ru-direct|blocked-only|all
                                         Russian sites directly / only blocked sites through the server / everything
          confirm                        Keep a change made with --confirm-within

          --confirm-within <seconds>     With connect, disconnect, use, mode and route: V2pn puts the change back
                                         by itself unless "v2pnctl confirm" comes in time. For a change that may
                                         cut off your own connection.

          mcp                            Run as an MCP server on standard input and output, for AI assistants

        Options:
          --instance <folder>            The copy of V2pn to talk to (or V2PN_INSTANCE); by default the one
                                         v2pnctl came with
          --json                         Print V2pn's answer as JSON

        Language: the system's. V2PN_LANG=en answers in English, V2PN_LANG=ru in Russian. MCP and --json are
        always in English.

        Exit codes: 0 done, 1 error, 2 needs a confirmation on the screen, 3 V2pn is not running and could not
        be started, 4 not allowed by V2pn's settings.
        """;

    private const string UsageRu = """
        v2pnctl: управление V2pn из терминала и ИИ-ассистентов.

        Запуск: v2pnctl [--instance <папка>] [--json] <команда> [аргументы]

        Чтение:
          status                         Подключено или нет, сервер, режим, маршрут, последняя проверка
          servers [--country XX]         Серверы: название, страна, последний пинг, избранное (без адресов)
          subs                           Подписки: название, обновление, срок, трафик (без ссылок)
          check                          Проверить подключение ещё раз
          diag                           Диагностика для поддержки
          open                           Показать окно V2pn

        Изменение (Настройки → Общие → «Управление из командной строки и ИИ» → «Управление»):
          connect                        Подключиться с выбранными сервером, режимом и маршрутом
          disconnect                     Отключиться
          use <название или id>          Выбрать сервер; подключённый V2pn сразу переключится на него
          use --fastest [--country XX]   Проверить серверы и выбрать самый быстрый
          mode proxy|vpn                 Режим прокси или режим VPN
          route ru-direct|blocked-only|all
                                         Российские сайты напрямую / только заблокированное / всё через VPN
          confirm                        Оставить изменение, сделанное с --confirm-within

          --confirm-within <секунды>     С connect, disconnect, use, mode и route: V2pn сам вернёт как было,
                                         если «v2pnctl confirm» не придёт вовремя. Для изменения, которое
                                         может оборвать ваше же подключение.

          mcp                            Работать MCP-сервером для ИИ-ассистентов на стандартных вводе и выводе

        Параметры:
          --instance <папка>             С какой копией V2pn работать (или V2PN_INSTANCE); по умолчанию с той,
                                         вместе с которой пришёл v2pnctl
          --json                         Вывести ответ V2pn как JSON

        Язык: как в системе. V2PN_LANG=en — по-английски, V2PN_LANG=ru — по-русски. MCP и --json всегда
        по-английски.

        Коды выхода: 0 — готово, 1 — ошибка, 2 — нужно подтверждение на экране, 3 — V2pn не запущен и запустить
        его не удалось, 4 — не разрешено настройками V2pn.
        """;

    /// <summary>The help text.</summary>
    public static string Usage(V2pnControlText t = default) => t.Ru ? UsageRu : UsageEn;

    /// <summary>The command line, or the reason it cannot be one, in <paramref name="t"/>'s words.</summary>
    public static V2pnCtlCommandLine Parse(IReadOnlyList<string> argv, out string? error, V2pnControlText t = default)
    {
        error = null;
        string? instance = null;
        string? country = null;
        int? confirmWithin = null;
        var json = false;
        var fastest = false;
        var words = new List<string>();
        for (var i = 0; i < argv.Count; i++)
        {
            var arg = argv[i];
            string? inline = null;
            if (arg.StartsWith("--", StringComparison.Ordinal) && arg.IndexOf('=') is var eq and > 2)
            {
                inline = arg[(eq + 1)..];
                arg = arg[..eq];
            }
            string? Value()
            {
                if (inline != null)
                {
                    return inline;
                }
                return i + 1 < argv.Count ? argv[++i] : null;
            }
            switch (arg)
            {
                case "--instance":
                    instance = Value();
                    if (string.IsNullOrWhiteSpace(instance))
                    {
                        error = t.T("--instance needs a folder.", "После --instance нужна папка.");
                        return Fail();
                    }
                    break;

                case "--json":
                    json = true;
                    break;

                case "-h" or "--help" or "-?" or "/?":
                    words.Insert(0, Help);
                    break;

                case "--version":
                    words.Insert(0, VersionCommand);
                    break;

                case "--fastest":
                    fastest = true;
                    break;

                case "--country":
                    country = Value()?.Trim().ToUpperInvariant();
                    if (country is not { Length: 2 } || !country.All(c => c is >= 'A' and <= 'Z'))
                    {
                        error = t.T("--country needs a two-letter country code, such as DE.",
                            "После --country нужен двухбуквенный код страны, например DE.");
                        return Fail();
                    }
                    break;

                case "--confirm-within":
                    if (!int.TryParse(Value(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                        || seconds < V2pnControlProtocol.MinConfirmWithin || seconds > V2pnControlProtocol.MaxConfirmWithin)
                    {
                        error = t.T(
                            $"--confirm-within needs a number of seconds from {V2pnControlProtocol.MinConfirmWithin} to {V2pnControlProtocol.MaxConfirmWithin}.",
                            $"После --confirm-within нужно число секунд от {V2pnControlProtocol.MinConfirmWithin} до {V2pnControlProtocol.MaxConfirmWithin}.");
                        return Fail();
                    }
                    confirmWithin = seconds;
                    break;

                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal) && arg.Length > 2)
                    {
                        error = t.T($"Unknown option {arg}.", $"Неизвестный параметр {arg}.");
                        return Fail();
                    }
                    words.Add(argv[i]);
                    break;
            }
        }

        if (words.Count == 0)
        {
            words.Add(Help);
        }
        var command = words[0].ToLowerInvariant();
        var rest = words.Skip(1).ToList();
        var args = new JsonObject();

        if (command is Help or VersionCommand)
        {
            return new V2pnCtlCommandLine(command, args, instance, json);
        }
        if (!V2pnControlProtocol.IsKnown(command) && command != Mcp)
        {
            error = t.T($"Unknown command {words[0]}. Run \"v2pnctl help\" for the list.",
                $"Неизвестная команда {words[0]}. Список команд: v2pnctl help.");
            return Fail();
        }
        if (confirmWithin != null && !V2pnControlProtocol.RevertibleCommands.Contains(command))
        {
            error = t.T("--confirm-within goes only with connect, disconnect, use, mode and route.",
                "--confirm-within работает только с connect, disconnect, use, mode и route.");
            return Fail();
        }
        if (fastest && command != V2pnControlProtocol.Use)
        {
            error = t.T("--fastest goes only with use.", "--fastest работает только с use.");
            return Fail();
        }
        if (country != null && command != V2pnControlProtocol.Servers && !(command == V2pnControlProtocol.Use && fastest))
        {
            error = t.T("--country goes only with servers and use --fastest.", "--country работает только с servers и use --fastest.");
            return Fail();
        }

        switch (command)
        {
            case V2pnControlProtocol.Servers:
                if (rest.Count > 0)
                {
                    error = t.T("servers takes no words; filter with --country XX.",
                        "Команде servers аргументы не нужны; страну задаёт --country XX.");
                    return Fail();
                }
                if (country != null)
                {
                    args["country"] = country;
                }
                break;

            case V2pnControlProtocol.Use:
                if (fastest == (rest.Count > 0))
                {
                    error = t.T("use needs a server's name or id, or --fastest.", "После use нужно название или id сервера либо --fastest.");
                    return Fail();
                }
                if (fastest)
                {
                    args["fastest"] = true;
                    if (country != null)
                    {
                        args["country"] = country;
                    }
                }
                else
                {
                    args["server"] = string.Join(' ', rest);
                }
                break;

            case V2pnControlProtocol.Mode:
                if (rest.Count != 1 || rest[0].ToLowerInvariant() is not ("proxy" or "vpn"))
                {
                    error = t.T("mode needs proxy or vpn.", "После mode нужно proxy или vpn.");
                    return Fail();
                }
                args["mode"] = rest[0].ToLowerInvariant();
                break;

            case V2pnControlProtocol.Route:
                if (rest.Count != 1 || !V2pnControlProtocol.Routes.Any(r => r.Name == rest[0].ToLowerInvariant()))
                {
                    error = t.T("route needs ru-direct, blocked-only or all.", "После route нужно ru-direct, blocked-only или all.");
                    return Fail();
                }
                args["route"] = rest[0].ToLowerInvariant();
                break;

            default:
                if (rest.Count > 0)
                {
                    error = t.T($"{command} takes no words.", $"Команде {command} аргументы не нужны.");
                    return Fail();
                }
                break;
        }
        if (confirmWithin is { } within)
        {
            args["confirmWithin"] = within;
        }
        return new V2pnCtlCommandLine(command, args, instance, json);

        V2pnCtlCommandLine Fail() => new(Help, new JsonObject(), instance, json);
    }

    /// <summary>The answer as a person reads it. A failure is its message alone; the caller prints it to the error output.</summary>
    public static string Format(string command, V2pnControlAnswer answer, DateTime nowUtc, V2pnControlText t = default)
    {
        if (!answer.Ok)
        {
            return answer.ErrorMessage ?? answer.ErrorCode ?? t.T("Failed.", "Не получилось.");
        }
        var data = answer.Data;
        switch (command)
        {
            case V2pnControlProtocol.Status:
                return data is JsonObject status ? FormatStatus(status, nowUtc, t) : string.Empty;

            case V2pnControlProtocol.Servers:
                return data is JsonArray servers ? FormatServers(servers, t) : string.Empty;

            case V2pnControlProtocol.Subs:
                return data is JsonArray subs ? FormatSubs(subs, nowUtc, t) : string.Empty;

            case V2pnControlProtocol.Diag:
                return V2pnControlJson.String(data?["text"]) ?? string.Empty;
        }
        var sb = new StringBuilder();
        if (V2pnControlJson.String(data?["message"]) is { Length: > 0 } message)
        {
            sb.AppendLine(message);
        }
        if (data?["status"] is JsonObject after)
        {
            sb.AppendLine(FormatStatus(after, nowUtc, t));
        }
        return sb.ToString().TrimEnd();
    }

    // The Russian words are the window's (its status pill and its toasts).
    private static string Verdict(string? verdict, V2pnControlText t) => verdict switch
    {
        "kill-switch" => t.T("Off: the connection fell, and the kill switch keeps the internet closed",
            "Отключено: соединение оборвалось, и kill switch держит интернет закрытым"),
        "core-stopped" => t.T("Off: the core kept stopping, and V2pn gave up restarting it",
            "Отключено: ядро раз за разом останавливалось, и V2pn перестал его перезапускать"),
        "core-failed" => t.T("Off: the core did not start", "Отключено: ядро не запустилось"),
        "off" => t.T("Off", "Отключено"),
        "reconnecting" => t.T("Reconnecting", "Переподключение"),
        "connecting" => t.T("Connecting", "Подключение"),
        "checking" => t.T("Checking the connection", "Проверка соединения"),
        "local-only" => t.T("On, the local proxy port only", "Подключено, только локальный порт"),
        "vpn-connected" => t.T("Connected in VPN mode", "Подключено в режиме VPN"),
        "proxy-connected" => t.T("Connected in proxy mode", "Подключено в режиме прокси"),
        "check-problem" => t.T("On, but the check of the connection did not pass", "Подключено, но проверка соединения не прошла"),
        _ => verdict ?? t.T("Unknown", "Неизвестно"),
    };

    // The check's state is V2pn's word for it (AvailabilityState); English shows it as it is.
    private static string CheckState(string? state, long? httpStatus, V2pnControlText t) => !t.Ru ? state ?? string.Empty : state switch
    {
        "Available" => "работает",
        "Checking" => "идёт",
        "Unchecked" => "не проверено",
        "Unsupported" => "недоступна для этой конфигурации",
        "UnexpectedStatus" => httpStatus is { } http ? $"проверочный сайт вернул HTTP {http}" : "проверочный сайт ответил неожиданно",
        "Redirected" => "проверочный сайт перенаправил запрос",
        "TimedOut" => "истекло время проверки",
        "Unreachable" => "проверочный сайт недоступен",
        "NoResponse" => "сервер не отвечает",
        _ => state ?? string.Empty,
    };

    public static string FormatStatus(JsonObject status, DateTime nowUtc, V2pnControlText t = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Verdict(V2pnControlJson.String(status["verdict"]), t));
        var mode = V2pnControlJson.String(status["mode"]) == "vpn" ? "VPN" : t.T("proxy", "прокси");
        if (status["server"] is JsonObject server)
        {
            var country = V2pnControlJson.String(server["country"]);
            sb.AppendLine($"{t.T("Server", "Сервер")}: {V2pnControlJson.String(server["name"])}{(country != null ? $" ({country})" : string.Empty)}");
        }
        else
        {
            sb.AppendLine(t.T("Server: none chosen", "Сервер: не выбран"));
        }
        sb.AppendLine($"{t.T("Mode", "Режим")}: {mode}");
        sb.AppendLine($"{t.T("Route", "Маршрут")}: {RouteName(V2pnControlJson.String(status["route"]), t)}");
        if (status["check"] is JsonObject check)
        {
            var line = $"{t.T("Check", "Проверка")}: {CheckState(V2pnControlJson.String(check["state"]), V2pnControlJson.Long(check["httpStatus"]), t)}";
            if (V2pnControlJson.Long(check["ms"]) is { } ms)
            {
                line += $", {ms} {t.T("ms", "мс")}";
            }
            if (V2pnControlJson.String(check["at"]) is { } at
                && DateTime.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when))
            {
                line += $", {Ago(nowUtc - when, t)}";
            }
            sb.AppendLine(line);
        }
        if (status["pendingConfirm"] is JsonObject pending)
        {
            var left = V2pnControlJson.Long(pending["secondsLeft"]);
            sb.AppendLine(t.T($"Waiting for \"v2pnctl confirm\": {left} s left, then V2pn puts the change back",
                $"Ждёт подтверждения (v2pnctl confirm): осталось {left} сек, потом V2pn вернёт как было"));
        }
        return sb.ToString().TrimEnd();
    }

    private static string RouteName(string? route, V2pnControlText t) => route switch
    {
        "ru-direct" => t.T("Russian sites directly (ru-direct)", "Российские сайты напрямую (ru-direct)"),
        "blocked-only" => t.T("only blocked sites through the server (blocked-only)", "Только заблокированное (blocked-only)"),
        "all" => t.T("everything through the server (all)", "Всё через VPN (all)"),
        "custom" => t.T("a rule set of your own", "Свои правила"),
        _ => t.T("none", "не выбран"),
    };

    private static string Ago(TimeSpan span, V2pnControlText t) =>
        span.TotalSeconds < 5 ? t.T("just now", "только что")
        : span.TotalMinutes < 1 ? t.T($"{(int)span.TotalSeconds} s ago", $"{(int)span.TotalSeconds} сек назад")
        : span.TotalHours < 1 ? t.T($"{(int)span.TotalMinutes} min ago", $"{(int)span.TotalMinutes} мин назад")
        : span.TotalDays < 2 ? t.T($"{(int)span.TotalHours} h ago", $"{(int)span.TotalHours} ч назад")
        : t.T($"{(int)span.TotalDays} days ago", $"{Days((int)span.TotalDays)} назад");

    private static string Days(int n) => $"{n} {V2pnControlText.Plural(n, "день", "дня", "дней")}";

    public static string FormatServers(JsonArray servers, V2pnControlText t = default)
    {
        if (servers.Count == 0)
        {
            return t.T("No servers.", "Серверов нет.");
        }
        var rows = new List<string[]>
        {
            t.Ru ? [" ", "СТРАНА", "ПИНГ", "ИЗБР.", "НАЗВАНИЕ", "ID"] : [" ", "COUNTRY", "DELAY", "FAV", "NAME", "ID"],
        };
        foreach (var node in servers.OfType<JsonObject>())
        {
            var delay = V2pnControlJson.Long(node["delayMs"]) is { } ms ? $"{ms} {t.T("ms", "мс")}"
                : V2pnControlJson.String(node["test"]) == "failed" ? t.T("failed", "нет ответа") : "-";
            rows.Add(
            [
                V2pnControlJson.Bool(node["active"]) == true ? ">" : " ",
                V2pnControlJson.String(node["country"]) ?? "-",
                delay,
                V2pnControlJson.Bool(node["favorite"]) == true ? "*" : "",
                V2pnControlJson.String(node["name"]) ?? string.Empty,
                V2pnControlJson.String(node["id"]) ?? string.Empty,
            ]);
        }
        return Table(rows);
    }

    // How the last update ended (EV2pnSubUpdateResult); the Russian words are the subscriptions list's.
    private static string SubResult(string result, V2pnControlText t) => !t.Ru ? result : result switch
    {
        "Ok" => "успешно",
        "Unreachable" => "сайт недоступен",
        "Empty" => "серверов не прислали",
        "Invalid" => "это не подписка",
        "Failed" => "не обновилась",
        "DeviceLimit" => "лимит устройств",
        "DeviceNotAccepted" => "устройство не принято",
        _ => result,
    };

    public static string FormatSubs(JsonArray subs, DateTime nowUtc, V2pnControlText t = default)
    {
        if (subs.Count == 0)
        {
            return t.T("No subscriptions.", "Подписок нет.");
        }
        var rows = new List<string[]>
        {
            t.Ru ? ["НАЗВАНИЕ", "СЕРВЕРОВ", "ОБНОВЛЕНА", "СРОК", "ТРАФИК"] : ["NAME", "SERVERS", "UPDATED", "EXPIRES", "TRAFFIC"],
        };
        foreach (var node in subs.OfType<JsonObject>())
        {
            var updated = V2pnControlJson.String(node["updated"]) is { } at && DateTime.TryParse(at, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when)
                ? Ago(nowUtc - when, t) : t.T("never", "не обновлялась");
            if (V2pnControlJson.String(node["lastResult"]) is { } result && result != "None")
            {
                updated += $" ({SubResult(result, t)})";
            }
            var expires = "-";
            if (V2pnControlJson.String(node["expires"]) is { } exp && DateTime.TryParse(exp, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var end))
            {
                var days = (int)(end - nowUtc).TotalDays;
                expires = end < nowUtc ? t.T("expired", "истекла") : t.T($"in {days} days", $"через {Days(days)}");
            }
            var total = V2pnControlJson.Long(node["trafficTotalBytes"]) ?? 0;
            var used = V2pnControlJson.Long(node["trafficUsedBytes"]) ?? 0;
            var traffic = total > 0 ? t.T($"{Gb(used, t)} of {Gb(total, t)} GB", $"{Gb(used, t)} из {Gb(total, t)} ГБ")
                : used > 0 ? t.T($"{Gb(used, t)} GB used", $"израсходовано {Gb(used, t)} ГБ")
                : "-";
            rows.Add([V2pnControlJson.String(node["name"]) ?? string.Empty, (V2pnControlJson.Long(node["servers"]) ?? 0).ToString(CultureInfo.InvariantCulture), updated, expires, traffic]);
        }
        return Table(rows);
    }

    // "1.5" in English, "1,5" in Russian, as the window writes them.
    private static string Gb(long bytes, V2pnControlText t)
    {
        var text = (bytes / 1073741824.0).ToString("0.#", CultureInfo.InvariantCulture);
        return t.Ru ? text.Replace('.', ',') : text;
    }

    private static string Table(List<string[]> rows)
    {
        var widths = new int[rows[0].Length];
        foreach (var row in rows)
        {
            for (var i = 0; i < row.Length; i++)
            {
                widths[i] = Math.Max(widths[i], row[i].Length);
            }
        }
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join("  ", row.Select((cell, i) => i == row.Length - 1 ? cell : cell.PadRight(widths[i]))).TrimEnd());
        }
        return sb.ToString().TrimEnd();
    }
}
