namespace v2rayN.Desktop;

// The kill switch: its settings page and what the main window says while it holds the internet closed.
public static partial class L
{
    public static string SetKillSwitch => T("Kill switch", "Kill switch");
    public static string SetKillSwitchHint => T(
        "If the connection drops, the internet stays closed until V2pn reconnects or you press Disconnect, so nothing goes out past the VPN. While it is off, a connection that cannot be brought back switches V2pn off and the internet goes on directly.",
        "Если соединение оборвётся, интернет останется закрытым, пока V2pn не переподключится или вы не нажмёте «Отключиться». Так ничего не уйдёт мимо VPN. Пока переключатель выключен, V2pn при обрыве отключается сам, и интернет идёт напрямую.");
    public static string GrpKillSwitch => T("Kill switch", "Kill switch");
    public static string KillSwitchEnable => T("Turn on the kill switch", "Включить kill switch");
    public static string KillSwitchEnableHint => T(
        "In VPN mode Windows lets only V2pn and the tunnel out. In proxy mode the system proxy stays on V2pn, so programs that use it get no connection instead of a direct one.",
        "В режиме VPN Windows выпускает наружу только V2pn и туннель. В режиме прокси системный прокси остаётся на V2pn: программы, которые им пользуются, не получат соединения вместо прямого.");
    public static string KillSwitchLan => T("Allow the local network", "Разрешить локальную сеть");
    public static string KillSwitchLanHint => T(
        "Printers, a NAS and the router's page stay reachable while the internet is closed. VPN mode only.",
        "Принтеры, сетевое хранилище и страница роутера остаются доступны, пока интернет закрыт. Только в режиме VPN.");
    public static string KillSwitchAdminNote => T(
        "VPN mode needs V2pn started as administrator; the kill switch uses the same rights.",
        "Режиму VPN нужны права администратора; kill switch пользуется ими же.");

    public static string GrpAppKillSwitch => T("Programs only through V2pn", "Программы только через V2pn");
    public static string AppKillSwitch => T("Never let these programs out past V2pn", "Не выпускать эти программы мимо V2pn");
    public static string AppKillSwitchHint => T(
        "They reach the internet only through V2pn's proxy or its tunnel, and are cut off otherwise: with V2pn disconnected, closed or not started. The real address never shows, for example to Claude.",
        "Они выходят в интернет только через прокси или туннель V2pn, а в остальное время отрезаны: когда V2pn отключён, закрыт или ещё не запущен. Настоящий адрес не засветится, например перед Claude.");
    public static string AppKillSwitchList => T("Programs, one per line", "Программы, по одной в строке");
    public static string AppKillSwitchListHint => T(
        "By file name, such as claude.exe: every copy and every version of it counts. Saving asks Windows for administrator rights once.",
        "По имени файла, например claude.exe: считаются все копии и все версии. При сохранении Windows один раз спросит права администратора.");
    public static string AppKillSwitchClaude => T("Add Claude", "Добавить Claude");
    public static string AppKillSwitchPick => T("Choose a program…", "Выбрать программу…");
    public static string AppKillSwitchCodeNote => T(
        "Claude Code ignores the system proxy: in proxy mode it works only with HTTPS_PROXY set to V2pn's port; VPN mode needs nothing.",
        "Claude Code не смотрит на системный прокси: в режиме прокси он заработает, только если задать HTTPS_PROXY на порт V2pn; в режиме VPN ничего не нужно.");
    public static string AppKillSwitchNow(string names) => T($"Closed now: {names}", $"Сейчас закрыты: {names}");
    public static string AppKillSwitchNone => T("Nothing is closed now.", "Сейчас ничего не закрыто.");
    public static string AppKillSwitchFailed => T(
        "The programs could not be closed off: {0}",
        "Не удалось закрыть программы: {0}");

    public static string StateKillSwitch => T("Internet blocked", "Интернет заблокирован");
    public static string KillSwitchHoldTip => T(
        "The connection dropped, and the kill switch keeps the internet closed so nothing goes out past the VPN. Reconnect, choose another server, or press Disconnect to open the internet.",
        "Соединение оборвалось, и kill switch держит интернет закрытым, чтобы ничего не ушло мимо VPN. Переподключитесь, выберите другой сервер или нажмите «Отключиться», чтобы открыть интернет.");
    public static string KillSwitchToast => T(
        "The connection dropped. The kill switch keeps the internet closed until you reconnect or disconnect.",
        "Соединение оборвалось. Kill switch держит интернет закрытым, пока вы не переподключитесь или не отключитесь.");
    public static string KillSwitchFirewallFailed => T(
        "The kill switch could not close the internet in VPN mode: {0}",
        "Kill switch не смог закрыть интернет в режиме VPN: {0}");
}
