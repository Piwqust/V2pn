namespace v2rayN.Desktop;

// The theme builder (Views/ThemeBuilderWindow*.cs): a provider's look from nothing to a signed pack and a certificate request.
public static partial class L
{
    public static string BuilderTitle => T("Theme builder", "Конструктор оформления");
    public static string BuilderMenu => T("Theme builder…", "Конструктор оформления…");
    public static string BuilderNew => T("New", "Новая тема");
    public static string BuilderOpen => T("Open folder…", "Открыть папку…");
    public static string BuilderSave => T("Save pack…", "Сохранить пакет…");
    public static string BuilderApply => T("Show in V2pn", "Посмотреть в V2pn");
    public static string BuilderApplyAgain => T("Update in V2pn", "Обновить в V2pn");
    public static string BuilderRemovePreview => T("Remove preview", "Убрать предпросмотр");
    public static string BuilderPreviewMark => T("Preview", "Предпросмотр");
    public static string BuilderPreviewHint => T("Not signed, on this computer only, gone when V2pn closes.", "Без подписи, только на этом компьютере, пропадёт после закрытия V2pn.");
    public static string BuilderPreviewOn => T("The V2pn window wears {0} as a preview.", "Окно V2pn в оформлении {0}, это предпросмотр.");
    public static string BuilderPreviewOff => T("Preview removed.", "Предпросмотр убран.");
    public static string BuilderPreviewName => T("{0} (preview)", "{0} (предпросмотр)");

    // ------------------------------------------------------------ the provider
    public static string BuilderProvider => T("Provider", "Провайдер");
    public static string BuilderName => T("Name people see", "Имя, которое видят люди");
    public static string BuilderNameHint => T("Up to 40 characters, without “V2pn”.", "До 40 символов, без «V2pn».");
    public static string BuilderId => T("Partner id", "id партнёра");
    public static string BuilderIdHint => T("Latin lowercase letters, digits and dashes, 2–32 characters. The certificate is issued for it.",
        "Строчные латинские буквы, цифры и дефис, 2–32 символа. На него выписывается сертификат.");

    // ------------------------------------------------------------ colours
    public static string BuilderColours => T("Colours", "Цвета");
    public static string BuilderBrand => T("Brand colours, 1 to 3. The first is the accent (Connect, the glow of Neon, the Pass card); the others colour Neon's ring.",
        "Цвета бренда, от 1 до 3. Первый — акцент (кнопка, свечение «Неона», карта «Пропуска»), остальные красят кольцо «Неона».");
    public static string BuilderAddColour => T("+ Colour", "+ Цвет");
    public static string BuilderHue => T("Hue", "Оттенок");
    public static string BuilderSaturation => T("Saturation", "Насыщенность");
    public static string BuilderLightness => T("Lightness", "Яркость");

    // ------------------------------------------------------------ looks
    public static string BuilderLooks => T("Looks", "Виды");
    public static string BuilderLooksHint => T("V2pn draws two looks in your colours; the person picks one on the Appearance page. Click the one your pack opens in.",
        "V2pn рисует два вида в ваших цветах, человек выбирает на странице «Оформление». Нажмите тот, в котором пакет откроется.");
    public static string BuilderConnect => T("Neon's Connect", "Кнопка «Неона»");

    public static string BuilderConnectName(string connect) => connect switch
    {
        V2pnThemeConnect.Round => T("Round, with a ring", "Круглая с кольцом"),
        _ => T("Switch in a row", "Переключатель в строке"),
    };

    public static string BuilderWindow => T("Neon's window", "Окно «Неона»");
    public static string BuilderWindowHint => T("Mini opens Neon as a narrow window (380×640), like a phone at the edge of the screen; the person can widen it.",
        "«Мини» открывает «Неон» узким окном 380×640, как телефон у края экрана; человек может его расширить.");

    public static string BuilderWindowName(string window) =>
        window == V2pnThemeWindow.Mini ? T("Mini", "Мини") : T("As the person left it", "Как оставил человек");

    public static string BuilderBackground => T("The Pass card", "Карта «Пропуска»");
    public static string BuilderBackgroundHint => T("The card with the plan, the days left and the traffic at the bottom of the Pass look.",
        "Карта с тарифом, сроком и трафиком внизу вида «Пропуск».");

    public static string BuilderBackgroundName(string kind) => kind switch
    {
        V2pnThemeBackgroundKinds.Gradient => T("Gradient", "Градиент"),
        V2pnThemeBackgroundKinds.Image => T("Picture", "Картинка"),
        _ => T("Brand colour", "Цвет бренда"),
    };

    public static string BuilderStops => T("Gradient colours, 2 to 4, corner to corner", "Цвета градиента, от 2 до 4, из угла в угол");
    public static string BuilderRemove => T("Remove", "Убрать");

    // ------------------------------------------------------------ pictures
    public static string BuilderPictures => T("Pictures", "Картинки");
    public static string BuilderPicturesHint => T("PNG, JPEG or WebP up to 1 MB, at most 8 in a pack. Transparent logos read on any ground.",
        "PNG, JPEG или WebP до 1 МБ, не больше 8 в пакете. Логотип с прозрачным фоном читается на любом фоне.");
    public static string BuilderPick => T("Choose…", "Выбрать…");
    public static string BuilderPictureOk => T("{0} · {1}×{2}, fits", "{0} · {1}×{2}, подходит");
    public static string BuilderPictureTooLarge => T("{0} is larger than 1 MB.", "{0} больше 1 МБ.");
    public static string BuilderPictureNotImage => T("{0} is not a PNG, JPEG or WebP.", "{0} — не PNG, JPEG и не WebP.");
    public static string BuilderPictureWrongSize => T("{0} is {1}×{2}; this place takes {3}.", "{0}: {1}×{2}, а сюда подходит {3}.");
    public static string BuilderPictureTooMany => T("A pack holds at most 8 pictures.", "В пакете не больше 8 картинок.");
    public static string BuilderPictureEmpty => T("None", "Нет");

    public static string BuilderPictureName(string place) => place switch
    {
        V2pnPackDraft.Pictures.Logo => T("Logo on dark", "Логотип на тёмном"),
        V2pnPackDraft.Pictures.LogoOnLight => T("Logo on light", "Логотип на светлом"),
        V2pnPackDraft.Pictures.Mark => T("Square mark", "Квадратный знак"),
        _ => T("Card picture", "Картинка карты"),
    };

    public static string BuilderSlotSize(string place) => place switch
    {
        V2pnPackDraft.Pictures.Logo or V2pnPackDraft.Pictures.LogoOnLight => T("2:1 to 8:1, 32–256 px tall", "от 2:1 до 8:1, высота 32–256 px"),
        V2pnPackDraft.Pictures.Mark => T("a square of 64–512 px", "квадрат 64–512 px"),
        _ => T("640–3200 px a side, at most 3:1", "640–3200 px по стороне, не шире 3:1"),
    };

    // ------------------------------------------------------------ buttons
    public static string BuilderActions => T("Your buttons, up to 4", "Ваши кнопки, до 4");
    public static string BuilderAddAction => T("+ Button", "+ Кнопка");
    public static string BuilderActionLabel => T("Label, up to 24 characters", "Подпись, до 24 символов");
    public static string BuilderActionServer => T("Server name or address", "Имя или адрес сервера");
    public static string BuilderActionGroup => T("Group name", "Название группы");
    public static string BuilderActionUrl => T("https://… on your domain, t.me/… or tg:…", "https://… на вашем домене, t.me/… или tg:…");
    public static string BuilderActionSkipped => T("V2pn does not show this button: it lacks a field, needs your API, or its link is not on your domains.",
        "V2pn не покажет эту кнопку: не хватает поля, нужен ваш API или ссылка не на ваших доменах.");
    public static string BuilderNoIcon => T("No icon", "Без значка");

    public static string BuilderActionKind(string kind) => kind switch
    {
        V2pnActionKinds.ConnectBest => T("Best server", "Лучший сервер"),
        V2pnActionKinds.ConnectRecommend => T("Your pick (API)", "Ваш выбор (API)"),
        V2pnActionKinds.ConnectServer => T("One server", "Один сервер"),
        V2pnActionKinds.ConnectGroup => T("A group", "Группа"),
        V2pnActionKinds.OpenCabinet => T("Cabinet", "Кабинет"),
        V2pnActionKinds.OpenRenew => T("Renew", "Продление"),
        V2pnActionKinds.OpenSupport => T("Support", "Поддержка"),
        V2pnActionKinds.OpenLink => T("A link", "Ссылка"),
        V2pnActionKinds.SubscriptionUpdate => T("Update subscription", "Обновить подписку"),
        V2pnActionKinds.CheckServices => T("Check services", "Проверить сервисы"),
        _ => T("Card from API", "Карточка из API"),
    };

    public static string BuilderStrategy(string strategy) => strategy switch
    {
        V2pnStrategies.Stable => T("Most stable", "Самый стабильный"),
        V2pnStrategies.Load => T("Least loaded (API)", "Наименее загруженный (API)"),
        _ => T("Lowest ping", "Наименьший пинг"),
    };

    // ------------------------------------------------------------ preview
    public static string BuilderSizeMini => T("Mini", "Мини");
    public static string BuilderSizeMedium => T("Medium", "Среднее");
    public static string BuilderSizeWide => T("Wide", "Широкое");
    public static string BuilderLight => T("Light", "Светлый");
    public static string BuilderDark => T("Dark", "Тёмный");
    public static string BuilderSample => T("Drawn by V2pn's theme look with made-up data: six servers, a plan, a banner.",
        "Рисует настоящий вид темы V2pn на выдуманных данных: шесть серверов, тариф, баннер.");
    public static string BuilderNotes => T("What V2pn changed or left out", "Что V2pn поправил или пропустил");
    public static string BuilderNoNotes => T("Nothing: V2pn takes the theme as it is.", "Ничего: V2pn берёт тему как есть.");
    public static string BuilderNoPreview => T("No preview: {0}", "Нет предпросмотра: {0}");

    /// <summary>
    /// A note of V2pnThemePalette.Describe (written in English for partner-check) in the person's language; other notes, which
    /// name the pack's fields, stay as partner-check prints them.
    /// </summary>
    public static string BuilderNote(string note)
    {
        if (!Ru)
        {
            return note;
        }
        var mode = System.Text.RegularExpressions.Regex.Match(note, "^(light|dark): (.*)$");
        var prefix = mode.Success ? (mode.Groups[1].Value == "light" ? "Светлый вариант: " : "Тёмный вариант: ") : string.Empty;
        var rest = mode.Success ? mode.Groups[2].Value : note;
        var m = System.Text.RegularExpressions.Regex.Match(rest, "^where the brand colour is text, it is (#[0-9A-F]{6})$");
        if (m.Success)
        {
            return $"{prefix}где цвет бренда — текст, он {m.Groups[1].Value}, чтобы читался";
        }
        m = System.Text.RegularExpressions.Regex.Match(rest, "^buttons use (#[0-9A-F]{6}) instead of (#[0-9A-F]{6}), so");
        if (m.Success)
        {
            return $"{prefix}кнопки {m.Groups[1].Value} вместо {m.Groups[2].Value}, чтобы выделялись на фоне и подпись на них читалась";
        }
        m = System.Text.RegularExpressions.Regex.Match(rest, "^(#[0-9A-F]{6}) is close to one of V2pn's state colours .*; V2pn uses (#[0-9A-F]{6}) instead$");
        if (m.Success)
        {
            return $"{m.Groups[1].Value} похож на цвет состояния V2pn (защищено, внимание, не защищено): V2pn берёт {m.Groups[2].Value}";
        }
        // V2pnThemeReader: fields of the six-template themes.
        m = System.Text.RegularExpressions.Regex.Match(note, "^theme\\.([\\w.]+) is no longer used");
        if (m.Success)
        {
            return $"theme.{m.Groups[1].Value} больше не используется: V2pn сам раскладывает свои два вида; при сохранении поле уйдёт из пакета";
        }
        // V2pnPartnerSlots.Fit: a picture that does not fit its place.
        m = System.Text.RegularExpressions.Regex.Match(note, "^(brand\\.logo|brand\\.logoOnLight|brand\\.mark|theme\\.background\\.art) (\\S+) is (\\d+)×(\\d+); ");
        if (m.Success)
        {
            var place = m.Groups[1].Value switch
            {
                "brand.logo" => V2pnPackDraft.Pictures.Logo,
                "brand.logoOnLight" => V2pnPackDraft.Pictures.LogoOnLight,
                "brand.mark" => V2pnPackDraft.Pictures.Mark,
                _ => V2pnPackDraft.Pictures.Backdrop,
            };
            return $"{m.Groups[1].Value}: {m.Groups[2].Value} — {m.Groups[3].Value}×{m.Groups[4].Value}, а сюда подходит {BuilderSlotSize(place)}; V2pn её не покажет";
        }
        return note;
    }

    // ------------------------------------------------------------ saving and signing
    public static string BuilderSaveFolder => T("Folder for the pack", "Папка для пакета");
    public static string BuilderOpenFolder => T("Folder with partner.json", "Папка с partner.json");
    public static string BuilderFillFirst => T("Fill in the name and the id first.", "Сначала заполните имя и id.");
    public static string BuilderSaved => T("Saved to {0}.", "Сохранено в {0}.");
    public static string BuilderLeftOut => T("Left out, the template has no place for: {0}.", "Убрано, в шаблоне для этого нет места: {0}.");
    public static string BuilderStray => T("The folder also holds {0}: they go into the pack too. Move them out if they are not part of it.",
        "В папке есть ещё {0}: они тоже попадут в пакет. Уберите их, если они не из пакета.");
    public static string BuilderOpened => T("Opened {0}.", "Открыта папка {0}.");
    public static string BuilderNewIn => T("A new theme; it is saved to {0}.", "Новая тема, сохранится в {0}.");
    public static string BuilderOpenFailed => T("Could not open it: {0}", "Не удалось открыть: {0}");
    public static string BuilderSaveFailed => T("Could not save: {0}", "Не удалось сохранить: {0}");
    public static string BuilderSignTitle => T("Sign the pack", "Подпись пакета");
    public static string BuilderSignHint => T("V2pn takes partner.json only with partner.json.sig made by your key. Sign after every change:",
        "V2pn принимает partner.json только с подписью partner.json.sig вашим ключом. Подписывайте после каждого изменения:");
    public static string BuilderCopy => T("Copy", "Копировать");
    public static string BuilderCopied => T("Copied.", "Скопировано.");
    public static string BuilderSignNow => T("Sign with my key", "Подписать моим ключом");
    public static string BuilderSigned => T("Signed: partner.json.sig is next to partner.json in {0}.", "Подписано: partner.json.sig лежит рядом с partner.json в {0}.");
    public static string BuilderSaveFirst => T("Save the pack first.", "Сначала сохраните пакет.");
    public static string BuilderPublish => T("Then put cert.json and cert.json.sig into the folder and serve it over https at the address of the v2pn-partner header.",
        "Потом положите в папку cert.json и cert.json.sig и отдавайте её по https по адресу из заголовка v2pn-partner.");

    // ------------------------------------------------------------ the request
    public static string BuilderRequest => T("Certificate request", "Заявка на сертификат");
    public static string BuilderRequestHint => T("V2pn's owner issues the certificate from this file. It is signed with your key, which proves the key is yours.",
        "По этому файлу владелец V2pn выпишет сертификат. Заявка подписана вашим ключом: так видно, что ключ ваш.");
    public static string BuilderDomains => T("Domains of the subscription and the pack, comma-separated", "Домены подписки и пакета через запятую");
    public static string BuilderGrants => T("What you ask for", "Что вы просите");
    public static string BuilderContact => T("How to reach you (Telegram, e-mail)", "Как с вами связаться (Telegram, почта)");
    public static string BuilderReviewFiles => T("Files to review, from the pack folder: {0}", "Файлы на проверку из папки пакета: {0}");
    public static string BuilderKey => T("Your key", "Ваш ключ");
    public static string BuilderNoKey => T("No key yet", "Ключа пока нет");
    public static string BuilderNewKey => T("Create a key…", "Создать ключ…");
    public static string BuilderPickKey => T("Choose key file…", "Выбрать файл ключа…");
    public static string BuilderKeyWarning => T("Keep the key file safe and to yourself: whoever has it signs packs in your name. V2pn keeps no copy; a lost key means a new certificate.",
        "Храните файл ключа надёжно и никому не отдавайте: кто им владеет, подписывает пакеты от вашего имени. V2pn копию не хранит; потеряли ключ — нужен новый сертификат.");
    public static string BuilderKeyCreated => T("Key saved to {0}.", "Ключ сохранён в {0}.");
    public static string BuilderKeyExists => T("{0} already exists; a key is never overwritten.", "{0} уже есть; ключ никогда не перезаписывается.");
    public static string BuilderKeyBad => T("Not a key: {0}", "Это не ключ: {0}");
    public static string BuilderSaveRequest => T("Save request…", "Сохранить заявку…");
    public static string BuilderRequestSaved => T("Request saved to {0}. Send it to V2pn's owner, with the files to review if there are any.",
        "Заявка сохранена в {0}. Отправьте её владельцу V2pn вместе с файлами на проверку, если они есть.");
    public static string BuilderRequestFailed => T("Request not saved: {0}", "Заявка не сохранена: {0}");
    public static string BuilderInTerminal => T("The same in a terminal:", "То же в терминале:");

    public static string BuilderGrantName(string grant) => grant switch
    {
        V2pnPartnerGrants.Edition => T("Your look (edition)", "Ваше оформление (edition)"),
        V2pnPartnerGrants.Account => T("Plan and server state from your API (account)", "Тариф и серверы из вашего API (account)"),
        _ => T("Closed settings, after a review (lock)", "Закрытые настройки, после проверки (lock)"),
    };
}
