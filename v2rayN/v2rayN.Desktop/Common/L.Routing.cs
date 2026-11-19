namespace v2rayN.Desktop;

// Routing editors ("Your rules"): the list of rule sets, one set, one rule.
public static partial class L
{
    // List of rule sets.
    public static string RtListTitle => T("Your rule sets", "Свои наборы правил");
    public static string RtListHint => T("The three modes in the main window cover most needs; here you build your own sets for special cases.",
        "Обычно хватает трёх режимов главного окна, а здесь собирают свои наборы для особых случаев.");
    public static string RtSetsTab => T("Rule sets", "Наборы");
    public static string RtNewSet => T("New set", "Новый набор");
    public static string RtAddBuiltin => T("Add the built-in sets", "Добавить стандартные наборы");
    public static string RtUseSet => T("Use this set", "Включить этот набор");
    public static string RtEdit => T("Edit", "Изменить");
    public static string RtInUse => T("In use", "Используется");
    public static string RtDelete => T("Delete", "Удалить");
    public static string RtSelectAll => T("Select all", "Выделить все");
    public static string RtColName => T("Name", "Название");
    public static string RtColRules => T("Rules", "Правил");
    public static string RtColOrder => T("Order", "Порядок");
    public static string RtColLink => T("Link", "Ссылка");
    public static string RtGrpDomains => T("Matching domains", "Сопоставление доменов");
    public static string RtDomainStrategy => T("How to match domains (Xray)", "Как сопоставлять домены (Xray)");
    public static string RtDomainStrategyHint => T("AsIs checks only the name; IPIfNonMatch also checks the site's IP address.",
        "AsIs смотрит только на имя, IPIfNonMatch — ещё и на IP-адрес сайта.");
    public static string RtDomainStrategySb => T("How to resolve domains (sing-box)", "Как сопоставлять домены (sing-box)");
    public static string RtDomainStrategySbHint => T("Which IP version to prefer. Empty: the default.", "Какую версию IP предпочитать. Пусто — по умолчанию.");
    public static string RtLearnMore => T("Learn more", "Подробнее");

    // One rule set.
    public static string RtSetTitle => T("Rule set", "Набор правил");
    public static string RtSetHint => T("Rules are checked from top to bottom; the first match decides where the traffic goes.",
        "Правила проверяются сверху вниз: первое подходящее решает, куда пойдёт трафик.");
    public static string RtGrpSet => T("Set", "Набор");
    public static string RtSetName => T("Name", "Название");
    public static string RtSetNamePlaceholder => T("My sites", "Мои сайты");
    public static string RtSetOrder => T("Position in the list", "Место в списке");
    public static string RtSetOrderHint => T("Smaller numbers go higher.", "Чем меньше число, тем выше.");
    public static string RtSetDomainStrategyHint => T("For this set only. Empty: the general setting.", "Только для этого набора. Пусто — как в общих настройках.");
    public static string RtSetUrl => T("Rules link", "Ссылка на правила");
    public static string RtSetUrlHint => T("Import → From the link downloads rules from here.", "Отсюда пункт «Импорт → По ссылке» скачивает правила.");
    public static string RtSetRuleset => T("sing-box rule-set file", "Файл rule-set для sing-box");
    public static string RtSetRulesetHint => T("Your own rule-set definitions for sing-box.", "Свои описания rule-set для sing-box.");
    public static string RtExample => T("Example", "Пример");
    public static string RtRulesTab => T("Rules", "Правила");
    public static string RtAddRule => T("Add rule", "Добавить правило");
    public static string RtImport => T("Import", "Импорт");
    public static string RtFromFile => T("From a file…", "Из файла…");
    public static string RtFromClipboard => T("From the clipboard", "Из буфера обмена");
    public static string RtFromUrl => T("From the link", "По ссылке");
    public static string RtCopySelected => T("Copy selected", "Скопировать выбранные");
    public static string RtMoveTop => T("Move to the top", "В самый верх");
    public static string RtMoveUp => T("Move up", "Выше");
    public static string RtMoveDown => T("Move down", "Ниже");
    public static string RtMoveBottom => T("Move to the bottom", "В самый низ");
    public static string RtColWhere => T("Where to", "Куда");
    public static string RtColMatch => T("Sites, addresses, programs", "Сайты, адреса, программы");
    public static string RtColApplies => T("Applies to", "Где действует");
    public static string RtColPort => T("Port", "Порт");
    public static string RtColProtocol => T("Protocol", "Протокол");
    public static string RtColEntry => T("Entry", "Вход");
    public static string RtColNetwork => T("Network", "Сеть");

    // One rule.
    public static string RtRuleTitle => T("Rule", "Правило");
    public static string RtRuleHint => T("Choose what to do and list the sites, addresses or programs it applies to.",
        "Выберите, что делать, и перечислите сайты, адреса или программы, к которым это относится.");
    public static string RtGrpAction => T("What to do", "Что делать");
    public static string RtAction => T("Send the traffic", "Куда направить трафик");
    public static string RtActionHint => T("Through the VPN, directly past it, or nowhere.", "Через VPN, напрямую в обход него или никуда.");
    public static string RtThroughVpn => T("Through VPN", "Через VPN");
    public static string RtDirect => T("Directly", "Напрямую");
    public static string RtBlock => T("Block", "Блокировать");
    public static string RtEnabled => T("Rule is on", "Правило включено");
    public static string RtEnabledHint => T("Turn off to keep the rule without using it.", "Выключите, чтобы правило сохранилось, но не работало.");
    public static string RtOutbound => T("A specific server or tag", "Конкретный сервер или тег");
    public static string RtOutboundHint => T("Used instead of the choice above: a server's name or an outbound tag.", "Вместо выбора выше: имя сервера или тег выхода.");
    public static string RtChooseServer => T("Choose a server", "Выбрать сервер");
    public static string RtGrpMatch => T("What it applies to", "К чему относится");
    public static string RtSites => T("Sites", "Сайты");
    public static string RtSitesHint => T("Domains or geosite lists, one per line.", "Домены или списки geosite, по одному на строку.");
    public static string RtAddresses => T("IP addresses", "IP-адреса");
    public static string RtAddressesHint => T("Addresses, subnets or geoip lists, one per line.", "Адреса, подсети или списки geoip, по одному на строку.");
    public static string RtPrograms => T("Programs", "Программы");
    public static string RtProgramsHint => T("Program file names, one per line.", "Имена файлов программ, по одному на строку.");
    public static string RtMatchNote => T("A line that starts with # is ignored.", "Строка с # в начале не учитывается.");
    public static string RtName => T("Name", "Название");
    public static string RtNameHint => T("Helps to find the rule in the list.", "Чтобы легко найти правило в списке.");
    public static string RtNamePlaceholder => T("Video", "Видео");
    public static string RtPort => T("Ports", "Порты");
    public static string RtPortHint => T("Only connections to these ports.", "Только соединения на эти порты.");
    public static string RtNetwork => T("Connection type", "Тип соединений");
    public static string RtNetworkHint => T("TCP, UDP or both. Empty: any.", "TCP, UDP или оба. Пусто — любые.");
    public static string RtProtocol => T("Protocol", "Протокол");
    public static string RtProtocolHint => T("Recognised from the traffic itself.", "Определяется по самому трафику.");
    public static string RtEntry => T("Where the traffic came in", "Откуда пришёл трафик");
    public static string RtEntryHint => T("tun is the VPN mode; socks, socks2 and socks3 are the proxy ports.", "tun — режим VPN; socks, socks2 и socks3 — порты прокси.");
    public static string RtRuleType => T("Applies to", "Где действует");
    public static string RtRuleTypeHint => T("Routing, DNS or both (ALL).", "Маршрутизация (Routing), DNS или оба (ALL).");
    public static string RtAutoSort => T("Sort the lists on save", "Сортировать списки при сохранении");

    // Programs pinned to one server or group (V2pnPins), above the rule sets.
    public static string PinGroup => T("Programs on their own server", "Программы на своём сервере");
    public static string PinNote => T("Checked before any rule set, whichever server is selected.",
        "Проверяются раньше любого набора правил, какой бы сервер ни был выбран.");
    public static string PinEmpty => T("For example, every Claude on this computer through one group of servers, while you switch the main server as you like.",
        "Например, все Claude на компьютере через одну группу серверов, а основной сервер переключайте как угодно.");
    public static string PinAdd => T("Add programs", "Добавить программы");
    public static string PinAddClaude => T("Add Claude", "Добавить Claude");
    public static string PinAddClaudeTip => T("The Claude app, Claude Code and the Claude sites.", "Приложение Claude, Claude Code и сайты Claude.");
    public static string PinOn => T("On", "Включено");
    public static string PinNoServer => T("No server chosen", "Сервер не выбран");
    public static string PinServerGone => T("“{0}” is gone. These programs follow the ordinary rules for now.",
        "«{0}» не найден. Пока эти программы идут по обычным правилам.");
    public static string PinGroupWord => T("group", "группа");
    public static string PinDeleteAsk => T("Delete “{0}”? Its programs will follow the ordinary rules.",
        "Удалить «{0}»? Эти программы пойдут по обычным правилам.");

    // One pin.
    public static string PinTitle => T("Programs on their own server", "Программы на своём сервере");
    public static string PinHint => T("These programs and sites always go through the server or group chosen here, whichever server is on and whichever rule set is active.",
        "Эти программы и сайты всегда идут через выбранный здесь сервер или группу, какой бы сервер ни был включён и какой бы набор правил ни работал.");
    public static string PinGrpWhere => T("Where to", "Куда");
    public static string PinServer => T("Server or group", "Сервер или группа");
    public static string PinServerHint => T("A group of several servers keeps working when one of them fails.",
        "Группа из нескольких серверов продолжает работать, когда один из них отказывает.");
    public static string PinChoose => T("Choose…", "Выбрать…");
    public static string PinNewGroup => T("New group…", "Новая группа…");
    public static string PinChosenServer => T("Server “{0}”", "Сервер «{0}»");
    public static string PinChosenGroup => T("Group “{0}”", "Группа «{0}»");
    public static string PinNamePlaceholder => T("Claude", "Claude");
    public static string PinProgramsHint => T("Program file names, one per line. Capital and small letters are the same.",
        "Имена файлов программ, по одному на строку. Большие и маленькие буквы не различаются.");
    public static string PinSitesHint => T("Optional. These sites go through the same server from any program, the browser included.",
        "Необязательно. Эти сайты идут через тот же сервер из любой программы, в том числе из браузера.");
    public static string PinNeedServer => T("Choose a server or a group.", "Выберите сервер или группу.");
    public static string PinNeedRules => T("List at least one program or site.", "Укажите хотя бы одну программу или сайт.");

    // The rule-sets window as a list beside the chosen set (no window on top of another).
    public static string RtMatchNav => T("How sites are matched", "Как узнаются сайты");
    public static string RtMatchHint => T("For every rule set. Change only if a rule doesn't catch a site.",
        "Для всех наборов. Меняйте, только если правило не срабатывает на нужном сайте.");
    public static string RtRulesNote => T("checked top to bottom, the first match wins", "проверяются сверху вниз, срабатывает первое подходящее");
    public static string RtRuleCount => T("Rules: {0}", "Правил: {0}");
    public static string RtLinkNote => T("rules link: {0}", "ссылка на правила: {0}");
    public static string RtNoRules => T("No rules yet", "Правил пока нет");
    public static string RtNoRulesHint => T("Without rules everything goes through the VPN.", "Без правил весь трафик идёт через VPN.");
    public static string RtAddFirstRule => T("Add the first rule", "Добавить первое правило");
    public static string RtNewRule => T("New rule", "Новое правило");
    public static string RtDuplicate => T("Duplicate", "Дублировать");
    public static string RtCopyName => T("{0} (copy)", "{0} (копия)");
    public static string RtCopyRules => T("Copy all rules", "Скопировать все правила");
    public static string RtCopyRule => T("Copy", "Скопировать");
    public static string RtCopied => T("Copied", "Скопировано");
    public static string RtTurnOff => T("Turn off", "Выключить");
    public static string RtTurnOn => T("Turn on", "Включить");
    public static string RtRuleOff => T("Off", "Выключено");
    public static string RtDeleteSetAsk => T("Delete the set “{0}”?", "Удалить набор «{0}»?");
    public static string RtDeleteRuleAsk => T("Delete the rule “{0}”?", "Удалить правило «{0}»?");
    public static string RtActiveCantDelete => T("The set in use can't be deleted: turn on another one first.",
        "Набор, который сейчас работает, удалить нельзя: сначала включите другой.");
    public static string RtSetDetails => T("More about this set", "Ещё о наборе");
    public static string RtAnything => T("Everything else", "Всё остальное");
    public static string RtPortsShort => T("ports {0}", "порты {0}");
    public static string RtMoreActions => T("More", "Ещё");
    public static string RtRuleMenu => T("More for the rule", "Ещё для правила");
    public static string RtPinEditHint => T("Opens the editor of programs and sites.", "Откроет редактор программ и сайтов.");
}
