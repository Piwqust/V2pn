namespace v2rayN.Desktop;

// V2pn keeping itself up to date.
public static partial class L
{
    public static string UpdAppTitle => T("V2pn {0}", "V2pn {0}");
    public static string UpdIdle => T("Not checked yet", "Ещё не проверяли");
    public static string UpdChecking => T("Checking for a new version…", "Поиск новой версии…");
    public static string UpdUpToDate => T("You have the latest version", "У вас последняя версия");
    public static string UpdAvailable => T("Version {0} is available", "Доступна версия {0}");
    public static string UpdDownloading => T("Downloading version {0}…", "Загрузка версии {0}…");
    public static string UpdReady => T("Version {0} is downloaded and ready to install", "Версия {0} скачана и готова к установке");
    public static string UpdFailed => T("Could not check for updates. Try again later.", "Не удалось проверить обновления. Попробуйте позже.");
    public static string UpdDownloadFailed => T("Could not download version {0}. Nothing was installed; try again later.", "Не удалось скачать версию {0}. Ничего не установлено, попробуйте позже.");
    public static string UpdBasedOn => T("Based on v2rayN {0}", "На основе v2rayN {0}");

    public static string UpdCheckNow => T("Check for updates", "Проверить обновления");
    public static string UpdCheckAgain => T("Check again", "Проверить ещё раз");
    public static string UpdInstall => T("Download and install", "Скачать и установить");
    public static string UpdRestart => T("Restart and update", "Перезапустить и обновить");
    public static string UpdRetry => T("Try again", "Повторить");
    public static string UpdWhatsNew => T("What's new", "Что нового");

    public static string UpdAuto => T("Update V2pn automatically", "Обновлять V2pn автоматически");
    public static string UpdAutoHint => T("New versions download in the background and install the next time V2pn starts.", "Новые версии скачиваются в фоне и ставятся при следующем запуске V2pn.");
    public static string UpdComponents => T("Cores and lists", "Ядра и списки");
    public static string UpdComponentsHint => Tk("Turn on what to check, then press Update.", "Turn on what to check, then press Update selected.", "Отметьте, что проверять, и нажмите «Обновить отмеченное».");

    public static string UpdBadgeAvailable => T("Update available", "Есть обновление");
    public static string UpdBadgeReady => T("Restart to update", "Перезапустить и обновить");
    public static string UpdToastReady => T("V2pn {0} is ready. It installs the next time V2pn starts.", "V2pn {0} скачан. Он установится при следующем запуске.");
    public static string UpdToastAvailable => T("V2pn {0} is available.", "Вышел V2pn {0}.");
    public static string UpdInstalling => T("Installing V2pn {0}…", "Установка V2pn {0}…");
}
