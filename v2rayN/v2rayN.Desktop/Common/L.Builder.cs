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
    public static string BuilderBrand => T("Brand colours, 1 to 3. The first one is the accent and the brand area.", "Цвета бренда, от 1 до 3. Первый — акцент и цвет фирменной области.");
    public static string BuilderAddColour => T("+ Colour", "+ Цвет");
    public static string BuilderTone => T("Tone of the grounds and cards", "Тон фона и карточек");
    public static string BuilderColourHex => T("#RRGGBB", "#RRGGBB");
    public static string BuilderHue => T("Hue", "Оттенок");
    public static string BuilderSaturation => T("Saturation", "Насыщенность");
    public static string BuilderLightness => T("Lightness", "Яркость");

    public static string BuilderToneName(string tone) => tone switch
    {
        V2pnThemeTones.Warm => T("Warm", "Тёплый"),
        V2pnThemeTones.Cool => T("Cool", "Холодный"),
        _ => T("Neutral", "Нейтральный"),
    };

    // ------------------------------------------------------------ layout
    public static string BuilderTemplate => T("Main screen", "Главный экран");
    public static string BuilderConnect => T("Connect button", "Кнопка подключения");

    public static string BuilderConnectName(string connect) => connect switch
    {
        V2pnThemeConnect.Round => T("Round", "Круглая"),
        V2pnThemeConnect.Capsule => T("Capsule", "Капсула"),
        V2pnThemeConnect.Bar => T("Bar", "Полоса"),
        _ => T("Toggle", "Переключатель"),
    };

    public static string BuilderFont => T("Font", "Шрифт");
    public static string BuilderFontSample => T("Aa Бб 123", "Аа Bb 123");
    public static string BuilderShape => T("Shape", "Форма");
    public static string BuilderCorners => T("Corners", "Углы");
    public static string BuilderDensity => T("Density", "Плотность");

    public static string BuilderCornersName(string corners) => corners switch
    {
        V2pnThemeCorners.Sharp => T("Sharp", "Острые"),
        V2pnThemeCorners.Round => T("Round", "Круглые"),
        _ => T("Soft", "Мягкие"),
    };

    public static string BuilderDensityName(string density) =>
        density == V2pnThemeDensity.Compact ? T("Compact", "Плотная") : T("Normal", "Обычная");

    public static string BuilderBackground => T("Background of the brand area", "Фон фирменной области");

    public static string BuilderBackgroundName(string kind) => kind switch
    {
        V2pnThemeBackgroundKinds.Gradient => T("Gradient", "Градиент"),
        V2pnThemeBackgroundKinds.Image => T("Picture", "Картинка"),
        _ => T("Brand colour", "Цвет бренда"),
    };

    public static string BuilderStops => T("Gradient colours, 2 to 4, corner to corner", "Цвета градиента, от 2 до 4, из угла в угол");
    public static string BuilderNoArea => T("The map template has no brand area: V2pn's map is its ground.", "У шаблона «Карта» нет фирменной области: её фон — карта V2pn.");
    public static string BuilderBlocks => T("Blocks and their order", "Блоки и их порядок");
    public static string BuilderBlocksHint => T("The server list is always there. A block shows when there is something for it.",
        "Список серверов есть всегда. Блок появляется, когда для него есть данные.");
    public static string BuilderAddBlock => T("Add:", "Добавить:");
    public static string BuilderUp => T("Up", "Выше");
    public static string BuilderDown => T("Down", "Ниже");
    public static string BuilderRemove => T("Remove", "Убрать");

    public static string BuilderBlockName(string block) => block switch
    {
        V2pnThemeBlocks.Plan => T("Plan and term", "Тариф и срок"),
        V2pnThemeBlocks.Traffic => T("Traffic", "Трафик"),
        V2pnThemeBlocks.Servers => T("Server list", "Список серверов"),
        V2pnThemeBlocks.Actions => T("Your buttons", "Ваши кнопки"),
        V2pnThemeBlocks.Banner => T("Banner", "Баннер"),
        V2pnThemeBlocks.News => T("News", "Новости"),
        _ => T("Wide picture", "Широкая картинка"),
    };

    // ------------------------------------------------------------ pictures
    public static string BuilderPictures => T("Pictures", "Картинки");
    public static string BuilderPicturesHint => T("PNG, JPEG or WebP up to 1 MB, at most 8 in a pack. Transparent logos read on any ground.",
        "PNG, JPEG или WebP до 1 МБ, не больше 8 в пакете. Логотип с прозрачным фоном читается на любом фоне.");
    public static string BuilderPick => T("Choose…", "Выбрать…");
    public static string BuilderPictureOk => T("{0} · {1}×{2}, fits", "{0} · {1}×{2}, подходит");
    public static string BuilderPictureTooLarge => T("{0} is larger than 1 MB.", "{0} больше 1 МБ.");
    public static string BuilderPictureNotImage => T("{0} is not a PNG, JPEG or WebP.", "{0} — не PNG, JPEG и не WebP.");
    public static string BuilderPictureWrongSize => T("{0} is {1}×{2}; this place takes {3}.", "{0}: {1}×{2}, а сюда подходит {3}.");
    public static string BuilderPictureNoPlace => T("The “{0}” template has no place for it.", "В шаблоне «{0}» для неё нет места.");
    public static string BuilderPictureTooMany => T("A pack holds at most 8 pictures.", "В пакете не больше 8 картинок.");
    public static string BuilderPictureEmpty => T("None", "Нет");

    public static string BuilderPictureName(string place) => place switch
    {
        V2pnPackDraft.Pictures.Logo => T("Logo on dark", "Логотип на тёмном"),
        V2pnPackDraft.Pictures.LogoOnLight => T("Logo on light", "Логотип на светлом"),
        V2pnPackDraft.Pictures.Mark => T("Square mark", "Квадратный знак"),
        V2pnPackDraft.Pictures.Hero => T("Illustration", "Иллюстрация"),
        V2pnPackDraft.Pictures.Banner => T("Wide picture", "Широкая картинка"),
        _ => T("Background picture", "Картинка фона"),
    };

    public static string BuilderSlotSize(string place) => place switch
    {
        V2pnPackDraft.Pictures.Logo or V2pnPackDraft.Pictures.LogoOnLight => T("2:1 to 8:1, 32–256 px tall", "от 2:1 до 8:1, высота 32–256 px"),
        V2pnPackDraft.Pictures.Mark => T("a square of 64–512 px", "квадрат 64–512 px"),
        V2pnPackDraft.Pictures.Hero => T("200–1600 px a side, at most 2:1", "200–1600 px по стороне, не шире 2:1"),
        V2pnPackDraft.Pictures.Banner => T("2:1 to 5:1, 400–2400 px wide", "от 2:1 до 5:1, ширина 400–2400 px"),
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
        // V2pnThemeReader: what a template has no place for.
        m = System.Text.RegularExpressions.Regex.Match(note, "^theme\\.blocks: the (\\w+) template has no place for \"(\\w+)\"; left out$");
        if (m.Success)
        {
            return $"Для блока «{BuilderBlockName(m.Groups[2].Value)}» в шаблоне «{ThemeTemplate(m.Groups[1].Value)}» нет места: V2pn его пропустит, при сохранении он уйдёт из пакета";
        }
        m = System.Text.RegularExpressions.Regex.Match(note, "^theme\\.art\\.(\\w+): the (\\w+) template has (no picture slots|the slots [^;]*); ignored$");
        if (m.Success)
        {
            var name = m.Groups[1].Value == V2pnThemeArt.Banner ? BuilderPictureName(V2pnPackDraft.Pictures.Banner) : BuilderPictureName(V2pnPackDraft.Pictures.Hero);
            return $"Для картинки «{name}» в шаблоне «{ThemeTemplate(m.Groups[2].Value)}» нет места: V2pn её пропустит";
        }
        if (note.StartsWith("theme.background: the map template has no brand area", StringComparison.Ordinal))
        {
            return BuilderNoArea + " Фон пакета не используется.";
        }
        if (note.StartsWith("theme.blocks has \"art\" but theme.art.banner names no picture", StringComparison.Ordinal))
        {
            return $"Блок «{BuilderBlockName(V2pnThemeBlocks.Art)}» есть, а картинки для него нет: блок останется пустым";
        }
        // V2pnPartnerSlots.Fit: a picture that does not fit its place.
        m = System.Text.RegularExpressions.Regex.Match(note, "^(brand\\.logo|brand\\.logoOnLight|brand\\.mark|theme\\.art\\.hero|theme\\.art\\.banner|theme\\.background\\.art) (\\S+) is (\\d+)×(\\d+); ");
        if (m.Success)
        {
            var place = m.Groups[1].Value switch
            {
                "brand.logo" => V2pnPackDraft.Pictures.Logo,
                "brand.logoOnLight" => V2pnPackDraft.Pictures.LogoOnLight,
                "brand.mark" => V2pnPackDraft.Pictures.Mark,
                "theme.art.hero" => V2pnPackDraft.Pictures.Hero,
                "theme.art.banner" => V2pnPackDraft.Pictures.Banner,
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
