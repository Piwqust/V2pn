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
    public static string ApAccentNearStatus => T("Close to the status colours: the Connect button may read as connected",
        "Этот цвет похож на цвета статуса: кнопку «Подключиться» легко принять за «Подключено»");

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
    public static string ApSidebar => T("Side", "Сторона");
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

    public static string ApGrpSidebar => T("Server list", "Список серверов");
    public static string ApTabs => T("Tabs", "Вкладки");
    public static string ApTabsBoth => T("Icon and name", "Значок и имя");
    public static string ApTabsIcons => T("Icons", "Значки");
    public static string ApTabsLabels => T("Names", "Имена");
    public static string ApTabsWhere => T("Tabs sit", "Где вкладки");
    public static string ApTop => T("Top", "Сверху");
    public static string ApBottom => T("Bottom", "Снизу");
    public static string ApHidden => T("Hidden", "Скрыть");
    public static string ApStartTab => T("Opens on", "Открывать на");
    public static string ApFlags => T("Flags", "Флаги");
    public static string ApSubtitles => T("Second line", "Вторая строка");
    public static string ApSubtitlesHint => T("Protocol and speed under the server's name.", "Протокол и скорость под именем сервера.");

    public static string ApGrpButtons => T("Buttons", "Кнопки");
    public static string ApButtonsWhere => T("Where", "Где");
    public static string ApButtonsLayout => T("Layout", "Вид");
    public static string ApButtonsRow => T("In a row", "В строку");
    public static string ApButtonsColumn => T("In a column", "В столбик");
    public static string ApButtonsIcons => T("Icons only", "Только значки");
    public static string ApButtonsShown => T("Show", "Показывать");

    public static string ApGrpHome => T("Home tab", "Главная");
    public static string ApHomeHint => T("What the home tab shows and in what order.", "Что показывает «Главная» и в каком порядке.");
    public static string HomeEmptyHint => T("Every section is turned off. Turn some on in Appearance (palette button above).",
        "Все разделы выключены. Включите нужные в «Оформлении» (кнопка с палитрой вверху).");
    public static string ApHomeFavorites =>T("Favorites shown", "Избранных сразу");
    public static string ApHomeRecent => T("Recent shown", "Недавних");
    public static string ApHomeQuick => T("Fastest shown", "Быстрых");
    public static string ApAll => T("All", "Все");
    public static string ApMoveUp => T("Move up", "Выше");
    public static string ApMoveDown => T("Move down", "Ниже");

    // Right-click menus of the server list (MainWindow.SidebarMenu.cs).
    public static string SbSection => T("Section “{0}”", "Раздел «{0}»");
    public static string SbHideSection => T("Hide section", "Скрыть раздел");
    public static string SbShowSection => T("Show a section", "Вернуть раздел");
    public static string SbCount => T("How many", "Сколько показывать");
    public static string SbTabs => T("Tabs", "Вкладки");
    public static string SbButtons => T("Buttons", "Кнопки");
    public static string SbCompact => T("Compact rows", "Компактные строки");
    public static string SbAllAppearance => T("All appearance settings…", "Все настройки оформления…");
    public static string SbReset => T("Reset the list", "Вернуть список как был");
    public static string SbDragHint => T("Drag buttons, tabs and section titles to move them",
        "Кнопки, вкладки и заголовки разделов можно перетаскивать");

    // The same names the home tab's headers show.
    public static string ApHomeSection(string id) => id switch
    {
        "fastest" => Fastest,
        "favorites" => TabFavorites,
        "recent" => Recent,
        "quick" => FastestList,
        "stats" => Stats,
        _ => id,
    };

    public static string ApGrpMotion =>T("Motion and language", "Движение и язык");
    public static string ApMotion => T("Animations", "Анимации");
    public static string ApMotionHint => T("Applies right away.", "Применяется сразу.");
    public static string ApMotionSystem => T("As in Windows", "Как в Windows");
    public static string ApMotionOn => T("On", "Включены");
    public static string ApMotionOff => T("Off", "Выключены");
    public static string ApLanguage => T("Language", "Язык");

    public static string BarMode => T("Mode", "Режим");
    public static string BarRoute => T("What goes through the VPN", "Что идёт через VPN");

    public static string ApReset => T("Reset to default", "Вернуть как было");

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

    public static string ApAccentName(string id) => id switch
    {
        "red" => T("Red", "Красный"),
        "orange" => T("Orange", "Оранжевый"),
        "amber" => T("Amber", "Янтарный"),
        "green" => T("Green", "Зелёный"),
        "teal" => T("Teal", "Бирюзовый"),
        "blue" => T("Blue", "Синий"),
        "violet" => T("Violet", "Фиолетовый"),
        "pink" => T("Pink", "Розовый"),
        _ => id,
    };
}
