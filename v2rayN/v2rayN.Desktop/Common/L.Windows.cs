namespace v2rayN.Desktop;

// Small windows, editors, the tray and the main menu: words that still came from v2rayN's resources.
public static partial class L
{
    public static string Ok => T("OK", "ОК");
    public static string DnsSettings => T("DNS settings", "Настройки DNS");
    public static string DnCustomOn => T("Your own DNS is on, so the settings on this tab don't apply.",
        "Включён свой DNS, поэтому настройки на этой вкладке не действуют.");
    public static string EdFormat => T("Format", "Отформатировать");
    public static string EdCopy => T("Copy", "Копировать");
    public static string EdPaste => T("Paste", "Вставить");
    public static string EdSelectAll => T("Select all", "Выделить всё");
    public static string ApFontSize => T("Font size", "Размер шрифта");
    public static string SudoPassword => T("Administrator password (sudo)", "Пароль администратора (sudo)");
    public static string SudoPasswordHint => T("V2pn checks it right away and doesn't keep it: after a restart you'll enter it again. If something goes wrong after the check, restart V2pn.",
        "V2pn сразу проверит пароль и не сохранит его: после перезапуска его нужно будет ввести снова. Если после проверки что-то пошло не так, перезапустите V2pn.");
    public static string SudoWrongPassword => T("Wrong password. Try again.", "Неверный пароль. Попробуйте ещё раз.");
    public static string SeBadLink => T("This doesn't look like a link.", "Это не похоже на ссылку.");
    public static string TrayShowHide => T("Show or hide the window", "Показать или скрыть окно");
    public static string RegionRussia => T("Russia", "Россия");
    public static string RegionIran => T("Iran", "Иран");
    public static string RegionChina => T("China", "Китай");
}
