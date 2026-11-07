namespace v2rayN.Desktop;

// The Appearance page: themes, accent, shape, layout.
public static partial class L
{
    public static string ApTitle => T("Appearance", "Оформление");
    public static string ApHint => T("Changes show up at once.", "Изменения видны сразу.");
    public static string ApGrpTheme => T("Theme", "Тема");
    public static string ApMode => T("Mode", "Режим");
    public static string ApModeSystem => T("System", "Как в Windows");
    public static string ApModeLight => T("Light", "Светлая");
    public static string ApModeDark => T("Dark", "Тёмная");
    public static string ApPalette => T("Palette", "Палитра");
    public static string ApAccent => T("Accent colour", "Цвет акцента");
    public static string ApAccentHint => T("Buttons, the Connect button and highlights.", "Кнопки, «Подключиться» и выделение.");
    public static string ApAccentCustom => T("Your own, #RRGGBB", "Свой, #RRGGBB");

    public static string ApGrpShape => T("Shape and text", "Форма и текст");
    public static string ApCorners => T("Corners", "Скругления");
    public static string ApCornersSharp => T("Sharp", "Острые");
    public static string ApCornersNormal => T("Normal", "Обычные");
    public static string ApCornersRound => T("Round", "Круглые");
    public static string ApFont => T("Font", "Шрифт");
    public static string ApFontInter => T("Inter", "Inter");
    public static string ApFontSegoe => T("Segoe UI", "Segoe UI");
    public static string ApHeadings => T("Headings", "Заголовки");
    public static string ApHeadingsSans => T("Like the text", "Как текст");
    public static string ApHeadingsSerif => T("Serif", "С засечками");
    public static string ApTextSize => T("Text size", "Размер текста");
    public static string ApTextSizeDefault => T("Standard", "Обычный");

    public static string ApGrpLayout => T("Main window", "Главное окно");
    public static string ApSidebar => T("Server list", "Список серверов");
    public static string ApSideLeft => T("Left", "Слева");
    public static string ApSideRight => T("Right", "Справа");
    public static string ApWidth => T("List width", "Ширина списка");
    public static string ApWidthNarrow => T("Narrow", "Узкий");
    public static string ApWidthNormal => T("Normal", "Обычный");
    public static string ApWidthWide => T("Wide", "Широкий");
    public static string ApDensity => T("Rows", "Строки");
    public static string ApDensityComfortable => T("Roomy", "Просторные");
    public static string ApDensityCompact => T("Compact", "Компактные");
    public static string ApMap => T("World map", "Карта мира");
    public static string ApMapHint => T("Off: a plain background, a little lighter on the processor.", "Выкл.: простой фон, чуть меньше нагрузки на процессор.");
    public static string ApStatusCard => T("Connection card", "Карточка подключения");
    public static string ApSpeedChart => T("Speed graph", "График скорости");

    public static string ApGrpMotion => T("Motion and language", "Движение и язык");
    public static string ApMotion => T("Animations", "Анимации");
    public static string ApMotionHint => T("Takes effect after V2pn restarts.", "Применится после перезапуска V2pn.");
    public static string ApMotionSystem => T("As in Windows", "Как в Windows");
    public static string ApMotionOn => T("On", "Включены");
    public static string ApMotionOff => T("Off", "Выключены");
    public static string ApLanguage => T("Language", "Язык");

    public static string BarMode => T("Mode", "Режим");
    public static string BarRoute => T("What goes through VPN", "Что идёт через VPN");

    public static string ApReset => T("Reset to default", "Вернуть как было");

    public static string KeepModeNoDisconnect => T(
        "In \"Keep system settings\" mode V2pn does not touch the Windows proxy, so there is nothing to switch off. Port {0} works while V2pn is open.",
        "В режиме «Не менять настройки системы» V2pn не трогает прокси Windows, отключать нечего. Порт {0} работает, пока V2pn открыт.");

    public static string ApPaletteName(string id) => id switch
    {
        "midnight" => T("Midnight", "Полночь"),
        "nord" => T("Nord", "Север"),
        "forest" => T("Forest", "Лес"),
        "plum" => T("Plum", "Слива"),
        "mocha" => T("Mocha", "Мокко"),
        "oled" => T("Black", "Чёрная"),
        _ => T("Graphite", "Графит"),
    };
}
