namespace v2rayN.Desktop;

// Control from the command line and AI assistants (v2pnctl, its MCP server): the setting and the notes the window shows.
public static partial class L
{
    public static string GrpControl => T("Command line and AI", "Командная строка и ИИ");
    public static string SetControl => T("Control from the command line and AI", "Управление из командной строки и ИИ");
    public static string SetControlHint => T(
        "Lets the v2pnctl command and AI assistants (over MCP) read V2pn's state, or also connect, disconnect and switch the server, mode and route. V2pn shows every change they make. Links, addresses and keys are never given out.",
        "Команда v2pnctl и ИИ-ассистенты (через MCP) смогут читать состояние V2pn или ещё и подключать, отключать, менять сервер, режим и маршрут. О каждом их изменении V2pn скажет в окне. Ссылки, адреса и ключи не выдаются никогда.");
    public static string ControlOff => T("Off", "Выключено");
    public static string ControlReadOnly => T("Read only", "Только чтение");
    public static string ControlFull => T("Control", "Управление");

    public static string CtlByTerminal => T("by a command from the terminal", "командой из терминала");
    public static string CtlByAgent => T("by an AI assistant", "ИИ-ассистентом");

    // {0}: CtlByTerminal or CtlByAgent.
    public static string CtlConnected => T("Connected {0}", "Подключено {0}");
    public static string CtlDisconnected => T("Disconnected {0}", "Отключено {0}");

    // {0}: the server's name; {1}: who.
    public static string CtlServerChanged => T("Server {0} chosen {1}", "Сервер {0} выбран {1}");
    public static string CtlModeVpn => T("VPN mode chosen {0}", "Режим VPN выбран {0}");
    public static string CtlModeProxy => T("Proxy mode chosen {0}", "Режим прокси выбран {0}");

    // {0}: the route's name; {1}: who.
    public static string CtlRouteChanged => T("Route «{0}» chosen {1}", "Маршрут «{0}» выбран {1}");
    public static string CtlReverted => T(
        "A change from the command line was not confirmed in time: V2pn put back the connection it had before.",
        "Изменение из командной строки не подтвердили вовремя: V2pn вернул подключение, какое было до него.");
}
