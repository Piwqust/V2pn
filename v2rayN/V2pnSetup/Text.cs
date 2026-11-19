using System.Globalization;

namespace V2pnSetup
{
    /// <summary>Every word the installer shows, in Russian for Russian, Ukrainian, Belarusian and Kazakh Windows, else in English.</summary>
    internal static class Text
    {
        /// <summary>Settable for --lang ru|en (pictures for review).</summary>
        public static bool Ru { get; set; } = IsRussian(CultureInfo.CurrentUICulture);

        private static bool IsRussian(CultureInfo culture)
        {
            var name = culture.TwoLetterISOLanguageName;
            return name == "ru" || name == "uk" || name == "be" || name == "kk";
        }

        private static string T(string ru, string en) => Ru ? ru : en;

        public static string WindowTitle => T("Установка V2pn", "V2pn setup");
        public static string UninstallTitle => T("Удаление V2pn", "Remove V2pn");
        public static string MapCaption => T("Xray, sing-box и mihomo внутри", "Xray, sing-box and mihomo inside");
        public static string Footer => T("GPL-3.0 · на основе v2rayN", "GPL-3.0 · based on v2rayN");

        // Welcome
        public static string EyebrowInstall => T("УСТАНОВКА", "INSTALL");
        public static string EyebrowUpdate => T("ОБНОВЛЕНИЕ", "UPDATE");
        public static string EyebrowReinstall => T("ПЕРЕУСТАНОВКА", "REINSTALL");
        public static string Tagline => T("Простой VPN-клиент для Windows. Серверы и подписки из v2rayN работают как есть.",
            "A simple VPN client for Windows. Servers and subscriptions from v2rayN work as they are.");
        public static string UpdateTagline(string from) => T($"Сейчас установлена версия {from}. Серверы, подписки и настройки останутся на месте.",
            $"Version {from} is installed now. Servers, subscriptions and settings stay as they are.");
        public static string ReinstallTagline => T("Эта версия уже установлена. Файлы программы будут записаны заново, серверы и настройки останутся.",
            "This version is already installed. The program's files are written again; servers and settings stay.");
        public static string Folder => T("Папка", "Folder");
        public static string Change => T("Изменить", "Change");
        public static string DesktopShortcut => T("Ярлык на рабочем столе", "Desktop shortcut");
        public static string ImportV2rayN => T("Перенести серверы и настройки из v2rayN", "Move servers and settings over from v2rayN");
        public static string ImportV2rayNHint => T("Если он есть на этом компьютере", "If it is on this computer");
        public static string Install => T("Установить", "Install");
        public static string Update => T("Обновить", "Update");
        public static string Reinstall => T("Переустановить", "Reinstall");
        public static string NoAdmin => T("Без прав администратора, только для вас", "No administrator rights, for you only");
        public static string PickFolder => T("Куда установить V2pn", "Where to install V2pn");

        // Checks
        public static string NoAccess => T("В эту папку нельзя записывать без прав администратора. Выберите папку в своём профиле, например в «Документах».",
            "This folder cannot be written to without administrator rights. Choose a folder in your profile, for example in Documents.");
        public static string NoSpace(string need) => T($"На диске не хватает места: нужно около {need}.", $"Not enough disk space: about {need} is needed.");
        public static string WrongArch => T("Этот установщик для Windows на ARM. Для этого компьютера нужен V2pn-windows-x64-setup.exe.",
            "This installer is for Windows on ARM. This computer needs V2pn-windows-x64-setup.exe.");
        public static string Broken => T("Файл установщика повреждён. Скачайте его заново.", "The installer file is damaged. Download it again.");

        // Running
        public static string RunningTitle => T("Закройте V2pn", "Quit V2pn");
        public static string RunningBody => T("V2pn сейчас запущен. Нажмите на его значок в трее и выберите «Выход»: системный прокси вернётся как был, и установка продолжится сама.",
            "V2pn is running. Click its tray icon and choose Exit: the system proxy is put back, and setup goes on by itself.");
        public static string RunningBodyUninstall => T("V2pn сейчас запущен. Нажмите на его значок в трее и выберите «Выход»: системный прокси вернётся как был, и удаление продолжится само.",
            "V2pn is running. Click its tray icon and choose Exit: the system proxy is put back, and removal goes on by itself.");
        public static string Waiting => T("Жду, пока V2pn закроется…", "Waiting for V2pn to quit…");
        public static string Cancel => T("Отмена", "Cancel");

        // Newer
        public static string NewerTitle => T("Уже стоит версия новее", "A newer version is installed");
        public static string NewerBody(string installed, string own) => T($"Установлен V2pn {installed}, а этот установщик ставит {own}. Обычно это не нужно: V2pn обновляется сам.",
            $"V2pn {installed} is installed, and this installer has {own}. That is rarely needed: V2pn updates itself.");
        public static string InstallOlder => T("Всё равно поставить", "Install it anyway");
        public static string Close => T("Закрыть", "Close");

        // Progress
        public static string Installing => T("Устанавливаю V2pn", "Installing V2pn");
        public static string Updating => T("Обновляю V2pn", "Updating V2pn");
        public static string StepPrepare => T("Готовлю файлы…", "Preparing files…");
        public static string StepFiles => T("Копирую файлы…", "Copying files…");
        public static string StepFinish => T("Ярлыки и последние штрихи…", "Shortcuts and final touches…");
        public static string ProgressHint => T("Это займёт меньше минуты.", "This takes less than a minute.");

        // Done
        public static string DoneTitle => T("V2pn установлен", "V2pn is installed");
        public static string DoneUpdatedTitle => T("V2pn обновлён", "V2pn is updated");
        public static string DoneBody => T("Он есть в меню «Пуск». Дальше V2pn обновляется сам.", "You will find it in the Start menu. From now on V2pn updates itself.");
        public static string Open => T("Открыть V2pn", "Open V2pn");

        // Error
        public static string ErrorTitle => T("Не получилось установить", "Setup did not finish");
        public static string ErrorUninstallTitle => T("Не получилось удалить", "Removal did not finish");
        public static string ErrorBody => T("Ничего не сломалось: прежние файлы на месте.", "Nothing is broken: the previous files are in place.");
        public static string Retry => T("Повторить", "Try again");
        public static string ShowLog => T("Открыть журнал", "Open the log");
        public static string EngineFailed(int code) => T($"Установщик завершился с кодом {code}.", $"The installer stopped with code {code}.");

        // Uninstall
        public static string UninstallEyebrow => T("УДАЛЕНИЕ", "REMOVE");
        public static string UninstallQuestion => T("Удалить V2pn?", "Remove V2pn?");
        public static string UninstallBody => T("Программа, ярлыки, автозапуск и ссылки v2pn:// будут удалены.", "The program, its shortcuts, the start at sign-in and v2pn:// links are removed.");
        public static string RemoveData => T("Удалить и серверы с настройками", "Remove servers and settings too");
        public static string RemoveDataHint => T("Иначе они останутся для следующей установки", "Otherwise they stay for the next install");
        public static string Remove => T("Удалить", "Remove");
        public static string Removing => T("Удаляю V2pn", "Removing V2pn");
        public static string RemovedTitle => T("V2pn удалён", "V2pn is removed");
        public static string RemovedKept => T("Серверы и настройки сохранены: при следующей установке они вернутся.", "Servers and settings are kept: the next install brings them back.");
        public static string RemovedAll => T("С этого компьютера убрано всё, что ставил V2pn.", "Everything V2pn put on this computer is gone.");
        public static string NotInstalled => T("V2pn не найден на этом компьютере.", "V2pn is not installed on this computer.");
    }
}
