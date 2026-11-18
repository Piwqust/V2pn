namespace v2rayN.Desktop;

// The first-start walkthrough.
public static partial class L
{
    public static string ObSkip => T("Skip", "Пропустить");
    public static string ObNext => T("Next", "Далее");
    public static string ObLater => T("Add later", "Добавлю позже");
    public static string ObStepOf => T("Step {0} of {1}", "Шаг {0} из {1}");

    public static string ObWelcomeTitle => T("Welcome to V2pn", "Добро пожаловать в V2pn");
    public static string ObWelcomeLead => T("A minute to set up: your servers, what goes through the VPN, and how to connect.", "Настройка займёт минуту: ваши серверы, что пускать через VPN и как подключаться.");
    public static string ObLanguage => T("Language", "Язык");
    public static string ObLanguageRestart => T("V2pn restarts in the new language…", "V2pn перезапускается на новом языке…");
    public static string ObStart => T("Get started", "Начать");

    public static string ObServersTitle => T("Your servers", "Ваши серверы");
    public static string ObServersLeadFound => T("Found on this computer. Move them in one click; the apps themselves stay as they are.", "Нашлись на этом компьютере. Перенесите их в один клик, сами приложения останутся как были.");
    public static string ObServersLead => T("Add what your provider gave you: a subscription link (https://…) or a server link (vless://, vmess://, trojan://…).", "Добавьте то, что дал провайдер: ссылку на подписку (https://…) или на сервер (vless://, vmess://, trojan://…).");
    public static string ObLooking => T("Looking for other VPN apps on this computer…", "Ищем другие VPN-приложения на компьютере…");
    public static string ObMoving => T("Moving…", "Переносим…");
    public static string ObMoved => T("Moved", "Перенесено");
    public static string ObPasteHint => T("Copy the link first", "Сначала скопируйте ссылку");
    public static string ObSubHint => T("Type or paste a URL", "Ввести или вставить адрес");
    public static string ObQrHint => T("From the screen", "С экрана");
    public static string ObDownloading => T("Downloading servers…", "Загрузка серверов…");
    public static string ObServersReady => T("{0} ready", "Готово: {0}");
    public static string ObServerSummary => T("Server · {0}", "Сервер · {0}");
    public static string ObModeSummary => T("Mode · {0}", "Режим · {0}");
    public static string ObRouteSummary => T("Route · {0}", "Маршрут · {0}");
    public static string ObEditConnection => T("Change mode and route", "Изменить режим и маршрут");
    public static string ObNoServersImported => T("The subscription provided no usable servers. Check the URL or enter another one.",
        "Из подписки не удалось получить пригодные серверы. Проверьте адрес или введите другой.");

    public static string ObRouteTitle => T("What goes through the VPN", "Что пускать через VPN");
    public static string ObRouteLead => T("You can change it any time under the Connect button.", "Это можно поменять в любой момент под кнопкой «Подключиться».");

    public static string ObModeTitle => T("How to connect", "Как подключаться");
    public static string ObModeLead => T("Also a switch under the Connect button.", "Это тоже переключатель под кнопкой «Подключиться».");
    public static string ObModeProxyHint => T("Browsers and apps that follow the system proxy. No administrator rights.", "Браузеры и программы, которые используют системный прокси. Права администратора не нужны.");
    public static string ObModeVpnHint => T("All traffic of the computer, games and apps without proxy support included.", "Весь трафик компьютера, включая игры и программы без поддержки прокси.");
    public static string ObModeVpnAdmin => T("Needs administrator rights: V2pn asks when you connect.", "Нужны права администратора: V2pn спросит при подключении.");

    public static string ObDoneTitle => T("All set", "Всё готово");
    public static string ObDoneLead => T("Connect now, or pick another server on the map first.", "Подключитесь сейчас или сначала выберите другой сервер на карте.");
    public static string ObDoneNoServersTitle => T("Almost there", "Почти готово");
    public static string ObDoneNoServersLead => T("Add a server to connect: paste the link from your provider on the main screen.", "Чтобы подключиться, добавьте сервер: вставьте ссылку от провайдера на главном экране.");
    public static string ObSumServers => T("Servers", "Серверы");
    public static string ObSumServer => T("Connects to", "Подключение к");
    public static string ObSumRoute => T("Through the VPN", "Через VPN");
    public static string ObSumMode => T("Mode", "Режим");
    public static string ObChange => T("Change", "Изменить");
    public static string ObNone => T("none yet", "пока нет");
    public static string ObConnect => T("Connect", "Подключиться");
    public static string ObFinish => T("Open V2pn", "Открыть V2pn");
    public static string ObNotNow => T("Not now", "Не сейчас");
}
