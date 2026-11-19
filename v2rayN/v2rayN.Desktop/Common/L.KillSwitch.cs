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
