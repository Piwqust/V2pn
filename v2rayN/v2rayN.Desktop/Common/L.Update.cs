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
    public static string UpdInstallFailed => T("Version {0} did not start after installing, so V2pn went back to the previous one.", "Версия {0} не запустилась после установки, V2pn вернулся к предыдущей.");

    public static string UpdCheckNow => T("Check for updates", "Проверить обновления");
    public static string UpdCheckAgain => T("Check again", "Проверить ещё раз");
    public static string UpdInstall => T("Download and install", "Скачать и установить");
    public static string UpdRestart => T("Restart and update", "Перезапустить и обновить");
    public static string UpdRetry => T("Try again", "Повторить");
    public static string UpdWhatsNew => T("What's new", "Что нового");

    public static string UpdAuto => T("Update V2pn automatically", "Обновлять V2pn автоматически");
    public static string UpdAutoHint => T("New versions download in the background and install the next time V2pn starts.", "Новые версии скачиваются в фоне и ставятся при следующем запуске V2pn.");
    public static string UpdBeta => T("Get beta versions", "Получать бета-версии");
    public static string UpdBetaHint => T(
        "Try new versions a few days before everyone else and help find what breaks. A beta may have bugs. Turned off, V2pn keeps the beta it has until a newer release replaces it.",
        "Пробовать новые версии на несколько дней раньше всех и помогать найти, что сломалось. В бете бывают ошибки. Если выключить, V2pn останется на своей бете, пока её не заменит более новый релиз.");
    public static string UpdReport => T("Tell V2pn when an update fails", "Сообщать V2pn о неудачных обновлениях");
    public static string UpdReportHint => T(
        "Only which version failed to replace which, the system and the step, such as \"download\" or \"start\". Nothing about you, your servers or your settings. Off unless you turn it on.",
        "Только какая версия не смогла заменить какую, система и шаг, например «скачивание» или «запуск». Ничего о вас, ваших серверах и настройках. Выключено, пока вы не включите.");
    public static string UpdComponents => T("Cores and lists", "Ядра и списки");
    public static string UpdComponentsHint => Tk("Turn on what to check, then press Update.", "Turn on what to check, then press Update selected.", "Отметьте, что проверять, и нажмите «Обновить отмеченное».");

    public static string UpdBadgeAvailable => T("Update available", "Есть обновление");
    public static string UpdBadgeReady => T("Restart to update", "Перезапустить и обновить");
    public static string UpdToastReady => T("V2pn {0} is ready. It installs the next time V2pn starts.", "V2pn {0} скачан. Он установится при следующем запуске.");
    public static string UpdToastAvailable => T("V2pn {0} is available.", "Вышел V2pn {0}.");
    public static string UpdInstalling => T("Installing V2pn {0}…", "Установка V2pn {0}…");
    public static string UpdThroughWinGet => T("Update with winget", "Обновить через winget");
    public static string UpdLocalUpToDate => T("Built from the sources; no newer release", "Сборка из исходников, новее релизов нет");
    public static string UpdBuiltFrom => T("commit {0}", "коммит {0}");
    public static string UpdBuiltFromChanged => T("commit {0} with uncommitted changes", "коммит {0} с незакоммиченными правками");

    // Developer mode.
    public static string DevMode => T("Developer mode", "Режим разработчика");
    public static string DevModeHint => T("Build V2pn from the sources on this computer and install it here. If the new build does not start, the previous version comes back.", "Собирать V2pn из исходников на этом компьютере и ставить сюда же. Если новая сборка не запустится, вернётся прежняя версия.");
    public static string DevSource => T("Source folder", "Папка с исходниками");
    public static string DevSourceNone => T(@"Not chosen: the folder with tools\release.ps1", @"Не выбрана: нужна папка, где лежит tools\release.ps1");
    public static string DevSourceBad => T(@"This folder has no V2pn sources (tools\release.ps1 is missing)", @"В этой папке нет исходников V2pn (не найден tools\release.ps1)");
    public static string DevChoose => T("Choose…", "Выбрать…");
    public static string DevBuild => T("Build and install", "Собрать и установить");
    public static string DevCancel => T("Cancel", "Отменить");
    public static string DevLog => T("Log", "Журнал");
    public static string DevIdle => T("The connection stays up while V2pn builds; it restarts for a few seconds to install.", "Пока идёт сборка, соединение работает. Для установки V2pn перезапустится на несколько секунд.");
    public static string DevBuilding => T("Building…", "Сборка…");
    public static string DevBuildingStep => T("Building: {0}", "Сборка: {0}");
    public static string DevInstalling => T("Installing, V2pn restarts…", "Установка, V2pn перезапускается…");
    public static string DevFailed => T("The build failed: {0}", "Сборка не удалась: {0}");
    public static string DevInstallFailed => T("Build {0} did not start, so V2pn went back to the previous version.", "Сборка {0} не запустилась, V2pn вернул прежнюю версию.");
    public static string DevOnLocal => T("This is a build from the sources ({0}). A newer release replaces it on its own.", "Сейчас стоит сборка из исходников ({0}). Следующий релиз заменит её сам.");
    public static string DevBackToRelease => T("Back to the release", "Вернуться на релиз");
    public static string DevBackFailed => T("Could not return to the release: {0}", "Не удалось вернуться на релиз: {0}");
}
