// The partner look builder: a provider fills in its look and sees V2pn's window wear it (Accent and Scene, light and
// dark, three window sizes), with the colours V2pn derives (v2pn-colors.js) and the picture slots V2pn checks; then it
// downloads partner.json and listing.json for the owner of V2pn. Nothing leaves the browser.
(() => {
  'use strict';
  const C = window.V2pnColors;

  // ------------------------------------------------------------------ texts
  const L = {
    ru: {
      docTitle: 'Оформление партнёра V2pn', forProviders: 'для провайдеров', language: 'Язык', title: 'Собрать оформление',
      lead: 'Заполните поля, и справа появится окно V2pn в вашем оформлении. Когда всё готово, скачайте файлы и отправьте их владельцу V2pn.',
      secProvider: 'Провайдер', name: 'Название', nameHint: 'Так его увидят люди. До 40 знаков, без «V2pn».',
      id: 'Код', idHint: 'Латиница, цифры и дефис, 2–32 знака. Имя папки у владельца.',
      domains: 'Домены подписок', domainsHint: 'По одному в строке. example.com — сам этот адрес, *.example.com — всё под ним.',
      contact: 'Как с вами связаться', contactHint: 'Только для владельца V2pn, людям не показывается.',
      secPictures: 'Картинки', choose: 'Выбрать',
      logoDark: 'Логотип для тёмного фона', logoDarkHint: 'Светлый широкий логотип: от 2:1 до 8:1, высотой 32–256 px, PNG с прозрачностью. Подойдёт 340×96.',
      logoLight: 'Логотип для светлого фона', logoLightHint: 'Тот же логотип в тёмном цвете, тех же размеров.',
      mark: 'Квадратный знак', markHint: 'Для карточки подписки и списка: 64–512 px, на прозрачном фоне. Подойдёт 128×128.',
      secColours: 'Цвета', coloursHint: '1–3 цвета бренда. Первый — главный: кнопка «Подключиться», ссылки, карточка. Цвета состояния («Защищено», «Не защищено») у V2pn свои.',
      addColour: 'Добавить цвет', colourMain: 'главный', colourMore: 'для градиента', remove: 'Убрать',
      secBackground: 'Фон «Сцены»', bgNone: 'Без фона: только «Акцент», с картой мира', bgGradient: 'Градиент из цветов бренда', bgImage: 'Картинка',
      backdrop: 'Картинка фона', backdropHint: '640–3200 px по стороне, не вытянутее 3:1, до 1 МБ. V2pn положит поверх неё затемнение, чтобы текст читался.',
      gradientOne: 'Градиенту нужны два цвета: второй V2pn взял светлее первого. Добавьте свой второй цвет, чтобы выбрать его самим.',
      gradientMany: 'Градиент под 135° через цвета бренда по порядку.',
      secButtons: 'Кнопки', buttonsHint: 'До четырёх кнопок под панелью подключения. Нужна отдельная договорённость с владельцем V2pn (право actions).',
      addAction: 'Добавить кнопку', label: 'Надпись', icon: 'Значок', doWhat: 'Что делает', noIcon: 'без значка',
      strategy: 'Как выбирать', purpose: 'Для чего', server: 'Сервер (имя или адрес)', group: 'Группа', url: 'Ссылка',
      secLinks: 'Ссылки и вход', cabinet: 'Кабинет', support: 'Поддержка', login: 'Вход по коду из бота',
      loginHint: 'Человек подтверждает вход в вашем боте, и V2pn сам добавляет подписку. Нужен API: login/start и login/poll (раздел 9 руководства).',
      api: 'Адрес API', secExport: 'Файлы', dlPartner: 'Скачать partner.json', dlListing: 'Скачать listing.json',
      sendTitle: 'Что прислать владельцу V2pn', preview: 'Предпросмотр', lookAccent: 'Акцент', lookScene: 'Сцена', light: 'Светлый', dark: 'Тёмный',
      mini: 'Мини', mid: 'Среднее', wide: 'Широкое', withPlan: 'С тарифом',
      // what V2pn does
      do: { 'connect.best': 'Лучший сервер', 'connect.recommend': 'Сервер по совету API', 'connect.server': 'Определённый сервер', 'connect.group': 'Лучший в группе',
        'open.cabinet': 'Открыть кабинет', 'open.renew': 'Продлить', 'open.support': 'Поддержка', 'open.link': 'Открыть ссылку',
        'subscription.update': 'Обновить подписку', 'check.services': 'Проверить сервисы', 'info.card': 'Карточка из API' },
      strategies: { ping: 'самый быстрый сейчас', stable: 'самый стабильный', load: 'наименее загруженный' },
      purposes: { youtube: 'YouTube', games: 'игры', mobile: 'мобильный интернет', whitelist: 'белые списки', streaming: 'стриминг', ai: 'нейросети', torrents: 'торренты' },
      // problems
      pName: 'Нужно название.', pNameLong: 'Название длиннее 40 знаков.', pNameV2pn: 'В названии не должно быть «V2pn».',
      pId: 'Код: латиница в нижнем регистре, цифры и дефис, 2–32 знака, с буквы или цифры.',
      pDomains: 'Нужен хотя бы один домен подписок.', pDomain: d => `Домен «${d}» не подходит: нужен вид example.com или *.example.com, хотя бы имя и зона.`,
      pApi: 'Для входа по коду нужен адрес API.', pApiHttps: 'Адрес API должен начинаться с https://.', pApiDomain: 'Адрес API не на ваших доменах подписок: V2pn к нему не обратится.',
      pLink: f => `${f}: ссылка должна начинаться с https://.`, pColour: 'Нужен хотя бы один цвет бренда #RRGGBB.',
      pAction: (n, why) => `Кнопка ${n}: ${why}`, aLabel: 'нужна надпись до 24 знаков', aField: 'не заполнено поле', aApi: 'нужен адрес API', aUrl: 'ссылка: https на ваших доменах или t.me, либо tg:',
      pBackdrop: 'Картинка фона не выбрана или не подходит: «Сцены» не будет.',
      // notes
      nLookAccent: 'Только «Акцент»: без фона V2pn оставляет карту мира.', nLookGradient: s => `«Сцена» на градиенте ${s}.`, nLookImage: f => `«Сцена» на картинке ${f}.`,
      nSceneOff: 'У оформления нет фона, поэтому «Сцены» нет: выберите градиент или картинку.',
      nInk: (mode, ink, ratio) => `${mode}: текст на «Сцене» ${ink}, в худшей точке ${ratio}:1.`, white: 'белый', darkInk: 'почти чёрный',
      nScrim: mode => `${mode}: V2pn кладёт на фон затемнение, иначе текст не читается.`,
      nMoved: (from, to, state) => `${from} похож на цвет «${state}» V2pn, поэтому V2pn возьмёт ${to}.`,
      states: { protected: 'Защищено', attention: 'Внимание', unprotected: 'Не защищено' },
      nButton: (mode, from, to) => `${mode}: кнопки темы ${to} вместо ${from}, чтобы выделяться на фоне и чтобы подпись читалась.`,
      nText: (mode, to) => `${mode}: где цвет бренда становится текстом, он ${to}.`,
      modeLight: 'светлая тема', modeDark: 'тёмная тема', hColours: 'Цвета (как скажет partner-check)', hPictures: 'Картинки', hLook: 'Вид',
      picOk: (f, w, h) => `${f}: ${w}×${h}, подходит.`, picWrong: (f, w, h, d) => `${f}: ${w}×${h}, а слот принимает ${d}. V2pn её не покажет.`,
      picLarge: f => `${f}: больше 1 МБ. V2pn её не возьмёт.`, picBad: f => `${f}: не PNG, JPEG или WebP, чей размер V2pn может прочесть.`,
      picNone: role => `${role}: не выбран, V2pn покажет название.`, roleLogo: 'Логотип для тёмного фона', roleLogoLight: 'Логотип для светлого фона', roleMark: 'Знак',
      // the list for the owner
      sFiles: 'Файлы <code>partner.json</code> и <code>listing.json</code> отсюда.',
      sPic: (role, name) => `${role} под именем <code>${name}</code>`, sPicDl: 'скачать с этим именем',
      sContact: 'Как с вами связаться, если в поле выше пусто.', sGrants: g => `Какие права нужны: ${g}. Решает владелец.`,
      gAccount: '<code>account</code> (API: тариф, проверки, вход по коду)', gActions: '<code>actions</code> (ваши кнопки)',
      sLogin: 'Для входа по коду: бот, который подтверждает код (раздел 9 руководства), и API на домене подписок.',
      sUntil: 'Срок партнёрства (<code>until</code> в listing.json) ставит владелец.', sNoExport: 'Сначала исправьте то, что отмечено выше.',
      // the window
      w: { connect: 'Подключиться', disconnect: 'Отключиться', connecting: 'Подключение…', unsafe: 'Не защищено', safe: 'Защищено', wait: 'Подключение…',
        proxy: 'Прокси', route: 'Российские сайты напрямую', search: 'Поиск', tabs: ['Избранное', 'Страны', 'Подписки'], servers: 'Серверы', serversOf: n => `Серверы ${n}`,
        server: 'Сервер', ping: 'Пинг', traffic: 'Трафик', renew: 'Продлить', support: 'Поддержка', daysLeft: 'дня осталось', days: 'дн.', ms: 'мс',
        planName: 'Личный доступ', until: 'до 9 декабря', of: 'из', gb: 'ГБ', devices: '2 из 5 устройств', noPlan: 'Тариф не указан', bytes: '0 Б', mb: '12,4 МБ',
        list: [['de', 'Германия 2', 'Франкфурт · VLESS Reality', 38], ['nl', 'Нидерланды', 'Амстердам · Hysteria2', 42], ['fi', 'Финляндия', 'Хельсинки · XHTTP', 51],
          ['pl', 'Польша', 'Варшава · XHTTP', 124], ['lv', 'Латвия', 'Рига · Hysteria2', null]] }
    },
    en: {
      docTitle: 'V2pn partner look', forProviders: 'for providers', language: 'Language', title: 'Build your look',
      lead: 'Fill in the fields and V2pn\'s window on the right wears your look. When it is ready, download the files and send them to the owner of V2pn.',
      secProvider: 'Provider', name: 'Name', nameHint: 'What people see. Up to 40 characters, without "V2pn".',
      id: 'Id', idHint: 'Lowercase letters, digits and hyphens, 2–32 characters. The owner\'s folder name.',
      domains: 'Subscription domains', domainsHint: 'One per line. example.com is that host, *.example.com everything under it.',
      contact: 'How to reach you', contactHint: 'For the owner of V2pn only; never shown to people.',
      secPictures: 'Pictures', choose: 'Choose',
      logoDark: 'Logo for a dark ground', logoDarkHint: 'A light wide logo: 2:1 to 8:1, 32–256 px tall, PNG with transparency. 340×96 works.',
      logoLight: 'Logo for a light ground', logoLightHint: 'The same logo in a dark colour, the same sizes.',
      mark: 'Square mark', markHint: 'For the subscription card and the list: 64–512 px on a transparent ground. 128×128 works.',
      secColours: 'Colours', coloursHint: '1–3 brand colours. The first is the main one: Connect, links, the card. The state colours ("Protected", "Not protected") stay V2pn\'s.',
      addColour: 'Add a colour', colourMain: 'main', colourMore: 'for the gradient', remove: 'Remove',
      secBackground: 'Scene background', bgNone: 'None: Accent only, with the world map', bgGradient: 'A gradient of the brand colours', bgImage: 'A picture',
      backdrop: 'Background picture', backdropHint: '640–3200 px a side, at most 3:1, up to 1 MB. V2pn lays a scrim over it so text reads.',
      gradientOne: 'A gradient needs two colours: V2pn took a lighter second one. Add your own second colour to choose it.',
      gradientMany: 'A 135° gradient through the brand colours in order.',
      secButtons: 'Buttons', buttonsHint: 'Up to four buttons under the connect panel. They need a separate grant from the owner of V2pn (actions).',
      addAction: 'Add a button', label: 'Label', icon: 'Icon', doWhat: 'What it does', noIcon: 'no icon',
      strategy: 'How to pick', purpose: 'For', server: 'Server (name or address)', group: 'Group', url: 'Link',
      secLinks: 'Links and sign-in', cabinet: 'Cabinet', support: 'Support', login: 'Sign-in by a code from the bot',
      loginHint: 'The person confirms in your bot and V2pn adds the subscription itself. Needs an API: login/start and login/poll (section 9 of the guide).',
      api: 'API address', secExport: 'Files', dlPartner: 'Download partner.json', dlListing: 'Download listing.json',
      sendTitle: 'What to send the owner of V2pn', preview: 'Preview', lookAccent: 'Accent', lookScene: 'Scene', light: 'Light', dark: 'Dark',
      mini: 'Mini', mid: 'Medium', wide: 'Wide', withPlan: 'With a plan',
      do: { 'connect.best': 'Best server', 'connect.recommend': 'Server your API picks', 'connect.server': 'A given server', 'connect.group': 'Best of a group',
        'open.cabinet': 'Open the cabinet', 'open.renew': 'Renew', 'open.support': 'Support', 'open.link': 'Open a link',
        'subscription.update': 'Update the subscription', 'check.services': 'Check services', 'info.card': 'A card from the API' },
      strategies: { ping: 'fastest now', stable: 'most stable', load: 'least loaded' },
      purposes: { youtube: 'YouTube', games: 'games', mobile: 'mobile internet', whitelist: 'whitelists', streaming: 'streaming', ai: 'AI', torrents: 'torrents' },
      pName: 'A name is needed.', pNameLong: 'The name is longer than 40 characters.', pNameV2pn: 'The name may not contain "V2pn".',
      pId: 'Id: lowercase letters, digits and hyphens, 2–32 characters, starting with a letter or digit.',
      pDomains: 'At least one subscription domain is needed.', pDomain: d => `"${d}" does not fit: example.com or *.example.com, at least a name and a zone.`,
      pApi: 'Sign-in by code needs an API address.', pApiHttps: 'The API address must start with https://.', pApiDomain: 'The API address is not on your subscription domains: V2pn will not ask it.',
      pLink: f => `${f}: the link must start with https://.`, pColour: 'At least one #RRGGBB brand colour is needed.',
      pAction: (n, why) => `Button ${n}: ${why}`, aLabel: 'a label of up to 24 characters is needed', aField: 'a field is empty', aApi: 'needs an API address', aUrl: 'link: https on your domains or t.me, or tg:',
      pBackdrop: 'No background picture, or it does not fit: there will be no Scene.',
      nLookAccent: 'Accent only: without a background V2pn keeps the world map.', nLookGradient: s => `Scene on a gradient ${s}.`, nLookImage: f => `Scene on the picture ${f}.`,
      nSceneOff: 'The look has no background, so there is no Scene: pick a gradient or a picture.',
      nInk: (mode, ink, ratio) => `${mode}: text on the Scene is ${ink}, ${ratio}:1 at its worst point.`, white: 'white', darkInk: 'near-black',
      nScrim: mode => `${mode}: V2pn lays a scrim over the background, or text would not read.`,
      nMoved: (from, to, state) => `${from} could pass for V2pn's "${state}" colour, so V2pn uses ${to}.`,
      states: { protected: 'Protected', attention: 'Attention', unprotected: 'Not protected' },
      nButton: (mode, from, to) => `${mode}: theme buttons use ${to} instead of ${from}, so they stand out from the ground and their label reads.`,
      nText: (mode, to) => `${mode}: where the brand colour is text, it is ${to}.`,
      modeLight: 'light', modeDark: 'dark', hColours: 'Colours (as partner-check says)', hPictures: 'Pictures', hLook: 'Look',
      picOk: (f, w, h) => `${f}: ${w}×${h}, fits.`, picWrong: (f, w, h, d) => `${f}: ${w}×${h}; the slot takes ${d}. V2pn will not show it.`,
      picLarge: f => `${f}: larger than 1 MB. V2pn will not take it.`, picBad: f => `${f}: not a PNG, JPEG or WebP whose size V2pn can read.`,
      picNone: role => `${role}: none; V2pn shows the name.`, roleLogo: 'Logo for a dark ground', roleLogoLight: 'Logo for a light ground', roleMark: 'Mark',
      sFiles: 'The <code>partner.json</code> and <code>listing.json</code> from here.',
      sPic: (role, name) => `${role} named <code>${name}</code>`, sPicDl: 'download with this name',
      sContact: 'How to reach you, if the field above is empty.', sGrants: g => `The grants you need: ${g}. The owner decides.`,
      gAccount: '<code>account</code> (the API: plan, checks, sign-in by code)', gActions: '<code>actions</code> (your buttons)',
      sLogin: 'For sign-in by code: a bot that confirms the code (section 9 of the guide) and the API on a subscription domain.',
      sUntil: 'The owner sets the term (<code>until</code> in listing.json).', sNoExport: 'First fix what is marked above.',
      w: { connect: 'Connect', disconnect: 'Disconnect', connecting: 'Connecting…', unsafe: 'Not protected', safe: 'Protected', wait: 'Connecting…',
        proxy: 'Proxy', route: 'Russian sites direct', search: 'Search', tabs: ['Favorites', 'Countries', 'Subscriptions'], servers: 'Servers', serversOf: n => `${n} servers`,
        server: 'Server', ping: 'Ping', traffic: 'Traffic', renew: 'Renew', support: 'Support', daysLeft: 'days left', days: 'days', ms: 'ms',
        planName: 'Personal', until: 'until 9 December', of: 'of', gb: 'GB', devices: '2 of 5 devices', noPlan: 'No plan given', bytes: '0 B', mb: '12.4 MB',
        list: [['de', 'Germany 2', 'Frankfurt · VLESS Reality', 38], ['nl', 'Netherlands', 'Amsterdam · Hysteria2', 42], ['fi', 'Finland', 'Helsinki · XHTTP', 51],
          ['pl', 'Poland', 'Warsaw · XHTTP', 124], ['lv', 'Latvia', 'Riga · Hysteria2', null]] }
    }
  };
  let lang = new URLSearchParams(location.search).get('lang') === 'en' ? 'en' : 'ru';
  const t = () => L[lang];

  // ------------------------------------------------------------------ state
  const ICONS = ['bolt', 'shield', 'play', 'game', 'card', 'chat', 'globe', 'star', 'refresh', 'check', 'info', 'gift'];
  const DOS = ['connect.best', 'connect.recommend', 'connect.server', 'connect.group', 'open.cabinet', 'open.renew', 'open.support', 'open.link',
    'subscription.update', 'check.services', 'info.card'];
  const PURPOSES = ['youtube', 'games', 'mobile', 'whitelist', 'streaming', 'ai', 'torrents'];
  const NEEDS = { 'connect.best': 'strategy', 'connect.recommend': 'purpose', 'connect.server': 'server', 'connect.group': 'group', 'open.link': 'url' };
  const NEEDS_API = new Set(['connect.recommend', 'info.card']);
  // The names the pictures get in the pack, as in partners/configra: the light logo is the one for a dark ground.
  const FILE = { logo: 'logo-light', logoOnLight: 'logo-dark', mark: 'mark', backdrop: 'background' };
  const SLOT_OF = { logo: 'logo', logoOnLight: 'logo', mark: 'mark', backdrop: 'backdrop' };

  const S = {
    name: '', id: '', idTouched: false, domains: '', contact: '', brand: ['#FF7A2F'], bg: 'none',
    pics: { logo: null, logoOnLight: null, mark: null, backdrop: null },
    actions: [], cabinet: '', support: '', login: false, api: '',
    look: 'accent', dark: true, size: 'mini', plan: true, conn: 'off', sel: 0
  };
  const $ = id => document.getElementById(id);
  const esc = s => String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
  const slug = s => s.toLowerCase().normalize('NFKD').replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 32);

  // ------------------------------------------------------------------ the pack as V2pn will read it
  const brandColours = () => {
    const out = [];
    for (const h of S.brand) { const c = C.parse(h); if (c && !out.includes(C.hex(c)) && out.length < 3) out.push(C.hex(c)); }
    return out;
  };
  const fits = key => S.pics[key] && S.pics[key].check.problem === 'none';
  const fileName = key => S.pics[key] ? `${FILE[key]}.${S.pics[key].ext}` : null;
  function gradientStops() {
    const b = brandColours();
    if (b.length >= 2) return b;
    if (!b.length) return [];
    // One colour: a second one from it, lighter and a little turned, so the gradient still reads as the brand.
    const c = C.parse(b[0]), [ch, h] = C.oklch(c);
    return [b[0], C.hex(C.fromOklch(Math.min(C.okL(c) + 0.14, 0.93), ch * 0.8, h + 16))];
  }
  function theme() {
    const brand = brandColours();
    let background = { type: 'solid', colors: [], art: null };
    if (S.bg === 'gradient' && gradientStops().length >= 2) background = { type: 'gradient', colors: gradientStops(), art: null };
    if (S.bg === 'image' && fits('backdrop')) background = { type: 'image', colors: [], art: fileName('backdrop') };
    return { brand, background };
  }
  const domainsList = () => S.domains.split(/[\s,]+/).map(d => d.trim().toLowerCase()).filter(Boolean);
  const DOMAIN = /^(\*\.)?([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z][a-z0-9-]{0,62}$/;
  const onDomains = host => domainsList().some(d => d.startsWith('*.') ? host.endsWith(d.slice(1)) : host === d);
  const hostOf = u => { try { return new URL(u).hostname.toLowerCase(); } catch (e) { return null; } };
  function actionProblem(a) {
    const s = t();
    if (!a.label.trim() || a.label.length > 24) return s.aLabel;
    const need = NEEDS[a.do];
    if (need && !String(a[need] || '').trim()) return s.aField;
    if (NEEDS_API.has(a.do) && !/^https:\/\//.test(S.api)) return s.aApi;
    if (a.do === 'open.link') {
      const u = a.url.trim(), h = hostOf(u);
      if (!(u.startsWith('tg:') || (u.startsWith('https://') && h && (h === 't.me' || onDomains(h))))) return s.aUrl;
    }
    return null;
  }

  function problems() {
    const s = t(), out = [];
    const name = S.name.trim();
    if (!name) out.push(s.pName);
    else if (name.length > 40) out.push(s.pNameLong);
    if (/[Vv]\s*2\s*[Pp]\s*[Nn]/.test(name)) out.push(s.pNameV2pn);
    if (!/^[a-z0-9][a-z0-9-]{1,31}$/.test(S.id)) out.push(s.pId);
    const ds = domainsList();
    if (!ds.length) out.push(s.pDomains);
    for (const d of ds) if (!DOMAIN.test(d)) out.push(s.pDomain(d));
    if (!brandColours().length) out.push(s.pColour);
    if (S.login && !S.api.trim()) out.push(s.pApi);
    if (S.api.trim() && !/^https:\/\//.test(S.api.trim())) out.push(s.pApiHttps);
    else if (S.api.trim() && !onDomains(hostOf(S.api.trim()) || '')) out.push(s.pApiDomain);
    for (const [f, v] of [[s.cabinet, S.cabinet], [s.support, S.support]]) if (v.trim() && !/^https:\/\//.test(v.trim())) out.push(s.pLink(f));
    S.actions.forEach((a, i) => { const p = actionProblem(a); if (p) out.push(s.pAction(i + 1, p)); });
    if (S.bg === 'image' && !fits('backdrop')) out.push(s.pBackdrop);
    return out;
  }

  // ------------------------------------------------------------------ pictures
  async function readPicture(key, file) {
    const bytes = new Uint8Array(await file.arrayBuffer());
    const check = C.checkSlot(SLOT_OF[key], bytes);
    const ext = bytes[0] === 0x89 ? 'png' : bytes[0] === 0xFF ? 'jpg' : bytes[0] === 0x52 ? 'webp' : 'png';
    // A data: URL, not a blob: one, so the page works under a strict Content-Security-Policy (img-src 'self' data:).
    const url = await new Promise((done, fail) => {
      const r = new FileReader();
      r.onload = () => done(r.result);
      r.onerror = () => fail(r.error);
      r.readAsDataURL(new Blob([bytes], { type: file.type || 'image/' + ext }));
    });
    S.pics[key] = { file, bytes, check, ext, url };
    if (key === 'mark' && check.problem === 'none') S.pics[key].tile = await markTile(S.pics[key].url);
  }
  // PartnerParts.Mark: a cut-out mark (see-through corners) gets a tile that sets it off: dark under a light glyph,
  // light under a dark one; a mark that brings its own ground fills its square.
  async function markTile(url) {
    try {
      const img = new Image();
      img.src = url;
      await img.decode();
      const cv = document.createElement('canvas');
      cv.width = img.naturalWidth; cv.height = img.naturalHeight;
      const g = cv.getContext('2d');
      g.drawImage(img, 0, 0);
      const d = g.getImageData(0, 0, cv.width, cv.height).data, w = cv.width, h = cv.height;
      const at = (x, y) => (y * w + x) * 4;
      if ([at(0, 0), at(w - 1, 0), at(0, h - 1), at(w - 1, h - 1)].some(i => d[i + 3] > 24)) return null;
      let sum = 0, count = 0;
      for (let i = 0; i < d.length; i += 4) if (d[i + 3] > 128) { sum += (d[i] + d[i + 1] + d[i + 2]) / (3 * 255); count++; }
      if (!count) return null;
      const light = sum / count;
      return light > 0.55 ? '#18181B' : light < 0.45 ? '#F2F2F4' : null;
    } catch (e) { return null; }
  }
  function picNote(key) {
    const s = t(), p = S.pics[key];
    if (!p) return { cls: '', text: '' };
    const f = `${p.file.name} → ${fileName(key)}`;
    const [w, h] = [p.check.width, p.check.height];
    if (p.check.problem === 'none') return { cls: 'ok', text: s.picOk(f, w, h) };
    if (p.check.problem === 'tooLarge') return { cls: 'bad', text: s.picLarge(f) };
    if (p.check.problem === 'notPicture') return { cls: 'bad', text: s.picBad(f) };
    return { cls: 'bad', text: s.picWrong(f, w, h, C.SLOTS[SLOT_OF[key]].desc[lang === 'ru' ? 0 : 1]) };
  }
  function drawPictures() {
    document.querySelectorAll('.pic').forEach(el => {
      const key = el.dataset.key, p = S.pics[key], body = el.querySelector('.pic-body'), note = el.querySelector('.pic-note');
      if (p) body.innerHTML = `<img src="${p.url}" alt="">`;
      const n = picNote(key);
      note.className = 'pic-note ' + n.cls;
      note.textContent = n.text;
    });
    $('backdrop-pic').hidden = S.bg !== 'image';
  }

  // ------------------------------------------------------------------ colours and buttons in the form
  const X = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18"/></svg>';
  function drawColours() {
    const s = t();
    $('colours').innerHTML = S.brand.map((h, i) => {
      const ok = C.parse(h);
      return `<div class="colour"><input type="color" value="${ok ? h.toLowerCase() : '#000000'}" data-i="${i}" aria-label="${esc(s.secColours)} ${i + 1}">
        <input value="${esc(h)}" data-i="${i}" maxlength="7" spellcheck="false" class="${ok ? '' : 'bad'}" aria-label="#RRGGBB ${i + 1}">
        <span class="role">${i === 0 ? s.colourMain : s.colourMore}</span>
        ${S.brand.length > 1 ? `<button type="button" class="x" data-rm="${i}" aria-label="${esc(s.remove)}">${X}</button>` : ''}</div>`;
    }).join('');
    $('add-colour').hidden = S.brand.length >= 3;
  }
  function drawActions() {
    const s = t();
    const opt = (v, label, cur) => `<option value="${v}"${v === cur ? ' selected' : ''}>${esc(label)}</option>`;
    $('actions').innerHTML = S.actions.map((a, i) => {
      const need = NEEDS[a.do];
      let extra = '';
      if (need === 'strategy') extra = `<label class="field wide"><span>${s.strategy}</span><select data-a="${i}" data-f="strategy">${Object.entries(s.strategies).map(([k, v]) => opt(k, v, a.strategy)).join('')}</select></label>`;
      if (need === 'purpose') extra = `<label class="field wide"><span>${s.purpose}</span><select data-a="${i}" data-f="purpose">${PURPOSES.map(k => opt(k, s.purposes[k], a.purpose)).join('')}</select></label>`;
      if (need === 'server' || need === 'group' || need === 'url') {
        extra = `<label class="field wide"><span>${s[need]}</span><input data-a="${i}" data-f="${need}" value="${esc(a[need] || '')}" maxlength="${need === 'group' ? 24 : 253}" spellcheck="false"></label>`;
      }
      return `<div class="action"><div class="grid">
        <label class="field wide"><span>${s.label} <span class="count">${a.label.length}/24</span></span><input data-a="${i}" data-f="label" value="${esc(a.label)}" maxlength="24"></label>
        <label class="field"><span>${s.icon}</span><select data-a="${i}" data-f="icon">${opt('', s.noIcon, a.icon)}${ICONS.map(k => opt(k, k, a.icon)).join('')}</select></label>
        <label class="field"><span>${s.doWhat}</span><select data-a="${i}" data-f="do">${DOS.map(k => opt(k, s.do[k], a.do)).join('')}</select></label>
        ${extra}</div><button type="button" class="x" data-rm-a="${i}" aria-label="${esc(s.remove)}">${X}</button></div>`;
    }).join('');
    $('add-action').hidden = S.actions.length >= 4;
  }

  // ------------------------------------------------------------------ the window (the prototype's Accent and Scene)
  const T = {
    dark: { winBg: '#0D0D10', sidebar: '#1C1C23', card: '#1E1E25', cardMuted: '#26262F', border: '#2A2A33', ink: '#ECECF1', inkSoft: '#C9C9D3', muted: '#9B9BA8', faint: '#8A8A96',
      safe: '#3DD68C', unsafe: '#FF5A3D', warn: '#F2B84B', mapOcean: '#0E0E11', mapDot: '#2E2E38', neutral: '#33343F',
      rowSel: '#1F2A3D', rowSelBorder: '#2C3F5E', glass: 'rgba(34,34,42,.86)', segOn: '#3A3B46', track: 'rgba(255,255,255,.1)', focus: '#7AB2FF',
      cardShadow: 'inset 0 1px 0 rgba(255,255,255,.06),0 1px 2px rgba(0,0,0,.3),0 6px 16px -4px rgba(0,0,0,.35),0 20px 40px -14px rgba(0,0,0,.45)',
      raisedShadow: 'inset 0 1px 0 rgba(255,255,255,.08),0 2px 4px rgba(0,0,0,.3),0 12px 28px -8px rgba(0,0,0,.45),0 32px 64px -20px rgba(0,0,0,.55)',
      segShadow: 'inset 0 1px 0 rgba(255,255,255,.06),0 1px 2px rgba(0,0,0,.3)' },
    light: { winBg: '#E3E4E9', sidebar: '#FFFFFF', card: '#FFFFFF', cardMuted: '#F0F1F5', border: 'rgba(22,22,27,.1)', ink: '#16161B', inkSoft: '#34343F', muted: '#666673', faint: '#777783',
      safe: '#046B39', unsafe: '#AE250D', warn: '#834F00', mapOcean: '#E6E9F0', mapDot: '#C3C9D6', neutral: '#E4E6EC',
      rowSel: '#E5EDFB', rowSelBorder: '#B7CCF2', glass: 'rgba(255,255,255,.88)', segOn: '#FFFFFF', track: 'rgba(22,22,27,.09)', focus: '#2F6FE0',
      cardShadow: '0 0 0 1px rgba(22,22,27,.05),0 1px 2px rgba(22,22,27,.06),0 6px 16px -6px rgba(22,22,27,.1)',
      raisedShadow: '0 0 0 1px rgba(22,22,27,.06),0 2px 4px rgba(22,22,27,.06),0 14px 32px -10px rgba(22,22,27,.18)',
      segShadow: '0 0 0 1px rgba(22,22,27,.06),0 1px 2px rgba(22,22,27,.1)' }
  };
  const SIZES = { mini: [380, 640], mid: [820, 700], wide: [1280, 800] };
  const FL = {
    nl: '<rect width="3" height="1" fill="#AE1C28"/><rect y="1" width="3" height="1" fill="#fff"/><rect y="2" width="3" height="1" fill="#21468B"/>',
    de: '<rect width="3" height="1" fill="#000"/><rect y="1" width="3" height="1" fill="#DD0000"/><rect y="2" width="3" height="1" fill="#FFCE00"/>',
    fi: '<rect width="3" height="3" fill="#fff"/><rect x=".8" width=".5" height="3" fill="#003580"/><rect y="1.25" width="3" height=".5" fill="#003580"/>',
    pl: '<rect width="3" height="1.5" fill="#fff"/><rect y="1.5" width="3" height="1.5" fill="#DC143C"/>',
    lv: '<rect width="3" height="3" fill="#9E3039"/><rect y="1.2" width="3" height=".6" fill="#fff"/>'
  };
  const flag = c => `<svg class="flag" viewBox="0 0 3 3" preserveAspectRatio="none" aria-hidden="true">${FL[c]}</svg>`;
  const I = {
    power: '<path d="M12 2v10"/><path d="M18.4 6.6a9 9 0 1 1-12.77.04"/>', bolt: '<path d="M13 2 3 14h9l-1 8 10-12h-9l1-8z"/>',
    shield: '<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>', search: '<circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/>',
    menu: '<path d="M4 6h16M4 12h16M4 18h16"/>', min: '<path d="M5 12h14"/>', max: '<rect x="5" y="5" width="14" height="14" rx="1"/>', x: '<path d="M6 6l12 12M18 6 6 18"/>',
    chev: '<path d="m6 9 6 6 6-6"/>', globe: '<circle cx="12" cy="12" r="10"/><path d="M2 12h20M12 2a15 15 0 0 1 0 20M12 2a15 15 0 0 0 0 20"/>',
    star: '<path d="m12 2 3.1 6.3 6.9 1-5 4.9 1.2 6.8-6.2-3.2-6.2 3.2L7 14.2 2 9.3l6.9-1z"/>',
    folder: '<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>',
    route: '<circle cx="6" cy="19" r="2"/><circle cx="18" cy="5" r="2"/><path d="M8 19h8.5a3.5 3.5 0 0 0 0-7h-9a3.5 3.5 0 0 1 0-7H16"/>',
    plus: '<path d="M12 5v14M5 12h14"/>', play: '<path d="M7 4v16l13-8z"/>', card: '<rect x="2" y="5" width="20" height="14" rx="2"/><path d="M2 10h20"/>',
    chat: '<path d="M21 12a8 8 0 0 1-11.6 7.1L4 20l1-4.6A8 8 0 1 1 21 12z"/>', check: '<path d="m5 12 5 5 9-10"/>',
    game: '<rect x="2" y="6" width="20" height="12" rx="4"/><path d="M6 12h4M8 10v4M15 11h.01M18 13h.01"/>',
    refresh: '<path d="M21 12a9 9 0 1 1-3-6.7L21 8"/><path d="M21 3v5h-5"/>', info: '<circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/>',
    gift: '<rect x="3" y="8" width="18" height="4" rx="1"/><path d="M12 8v13M19 12v7a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2v-7M7.5 8a2.5 2.5 0 0 1 0-5C10 3 12 8 12 8s2-5 4.5-5a2.5 2.5 0 0 1 0 5"/>',
    palette: '<circle cx="13.5" cy="6.5" r="1"/><circle cx="17.5" cy="10.5" r="1"/><circle cx="8.5" cy="7.5" r="1"/><circle cx="6.5" cy="12.5" r="1"/><path d="M12 2a10 10 0 0 0 0 20c1 0 1.5-.8 1.5-1.6 0-.5-.2-.8-.5-1.2-.3-.3-.5-.7-.5-1.2 0-.9.7-1.6 1.6-1.6H16a6 6 0 0 0 6-6c0-4.6-4.5-8.4-10-8.4z"/>'
  };
  const ic = (n, st = '') => I[n] ? `<svg class="i" viewBox="0 0 24 24" style="${st}" aria-hidden="true">${I[n]}</svg>` : '';
  const V2MARK = '<svg class="v2mark" viewBox="0 0 24 24" aria-hidden="true"><path d="M4 3l8 10M20 3l-8 10M12 13v8" stroke="#FF3D1F" stroke-width="4" stroke-linecap="round" fill="none"/></svg>';

  // The partner as the window draws it.
  function partner() {
    const w = t().w, name = S.name.trim() || 'Kestrel';
    // PartnerParts.Logo: on a light ground the logo for it, else the main one; on a dark ground only the main one.
    const logoFor = darkGround => {
      const order = darkGround ? ['logo'] : ['logoOnLight', 'logo'];
      const key = order.find(fits);
      return key ? `<img class="plogo" src="${S.pics[key].url}" alt="${esc(name)}">` : `<span class="pname">${esc(name)}</span>`;
    };
    const m = S.pics.mark;
    const mark = fits('mark')
      ? (m.tile ? `<span class="pmark" style="background:${m.tile};padding:4px"><img src="${m.url}" alt=""></span>` : `<span class="pmark fill"><img src="${m.url}" alt=""></span>`)
      : `<span class="pmark letter">${esc(name.slice(0, 1).toUpperCase())}</span>`;
    const actions = S.actions.filter(a => !actionProblem(a)).slice(0, 4).map(a => [a.icon, a.label]);
    const plan = S.plan ? { name: w.planName, days: 23, until: w.until, used: 41.2, total: 100, devices: w.devices } : null;
    return { name, logo: logoFor, mark, plan, actions, servers: w.list };
  }

  function colours(ctx) {
    const base = ctx.dark ? T.dark : T.light, th = theme();
    const { accent, onAccent } = C.partnerAccent(th);
    const v = { ...base };
    v.accent = C.hex(accent); v.onAccent = C.hex(onAccent); v.accentGlow = C.rgba(accent, 0.55);
    // Links on the card: the brand moved in lightness until it reads there (MainWindow.Edition.BuildLook).
    v.brandText = C.hex(C.readableOn(accent, C.parse(base.cardMuted)));
    v.fillBar = v.accent;
    v.safeSoft = C.rgba(C.parse(base.safe), ctx.dark ? 0.15 : 0.12); v.unsafeSoft = C.rgba(C.parse(base.unsafe), ctx.dark ? 0.15 : 0.1);
    v.warnSoft = C.rgba(C.parse(base.warn), ctx.dark ? 0.15 : 0.12);
    return { v, stage: C.stage(th, ctx.dark), theme: th };
  }
  const cssVars = v => Object.entries(v).map(([k, x]) => `--${k}:${x}`).join(';');

  const st = ctx => { const w = t().w; return ctx.conn === 'on' ? ['ok', w.safe] : ctx.conn === 'wait' ? ['wait', w.wait] : ['bad', w.unsafe]; };
  const pingCls = p => p == null ? 'n' : p < 80 ? 'g' : p < 150 ? 'm' : 'b';
  const pingTxt = p => p == null ? '—' : `${p} ${t().w.ms}`;
  const wc = () => `<button class="ib" tabindex="-1" aria-hidden="true">${ic('min')}</button><button class="ib" tabindex="-1" aria-hidden="true">${ic('max')}</button><button class="ib" tabindex="-1" aria-hidden="true">${ic('x')}</button>`;
  const tbV2 = (ctx, p) => `<div class="tb"><span class="ib">${ic('menu')}</span><div class="lock">${V2MARK}<span>V2pn</span><span class="x">×</span><span style="--lh:15px;display:block;min-width:0;overflow:hidden;max-width:160px">${p.logo(ctx.dark)}</span></div><div class="sp"></div><span class="ib">${ic('palette')}</span>${wc()}</div>`;
  const connectBtn = ctx => { const w = t().w, on = ctx.conn === 'on'; return `<button class="btnC press ${on ? 'off' : ''}" data-act="conn">${ic('power', 'width:18px;height:18px')}<span>${on ? w.disconnect : ctx.conn === 'wait' ? w.connecting : w.connect}</span></button>`; };
  const modeSeg = (style = '') => `<div class="seg2" style="${style}"><button aria-pressed="true" tabindex="-1">${ic('globe')}${t().w.proxy}</button><button aria-pressed="false" tabindex="-1">${ic('shield')}VPN</button></div>`;
  const routeBtn = () => `<span class="route">${ic('route')}<span class="ell">${t().w.route}</span>${ic('chev', 'width:14px;height:14px')}</span>`;
  const serverRows = (ctx, p) => p.servers.map((s, i) => `<button class="row press" data-act="sel" data-i="${i}" aria-current="${ctx.sel === i}">${flag(s[0])}
    <span style="flex:1;min-width:0;display:grid;gap:1px"><span class="t ell">${s[1]}</span><span class="s ell">${s[2]}</span></span><span class="ping ${pingCls(s[3])}">${pingTxt(s[3])}</span></button>`).join('');
  const cur = (ctx, p) => p.servers[ctx.sel] || p.servers[0];
  const planLine = p => p.plan ? `${p.plan.name} · <span class="num">${p.plan.days}</span> ${t().w.days}` : t().w.noPlan;
  const wash = (ctx, v) => { const c = C.parse(ctx.conn === 'on' ? v.safe : ctx.conn === 'wait' ? v.warn : v.unsafe); return `<div class="wash" style="background:linear-gradient(${C.rgba(c, ctx.dark ? 0.2 : 0.12)},${C.rgba(c, 0)})"></div>`; };
  const chips = (p, cls, style = '') => p.actions.map(a => `<button class="chipA press ${cls}" style="${style}">${ic(a[0], 'width:14px;height:14px')}${esc(a[1])}</button>`).join('');

  function subCard(ctx, p) {
    const w = t().w, pl = p.plan;
    return `<div style="border-radius:12px;padding:12px;background:var(--cardMuted);display:grid;grid-template-columns:minmax(0,1fr);gap:10px">
      <div style="display:flex;gap:10px;align-items:center">${p.mark}<div style="min-width:0;flex:1"><div class="ell" style="font-weight:600;font-size:13.5px">${esc(p.name)}</div><div class="ell" style="font-size:12px;color:var(--muted)">${planLine(p)}${pl ? ' · ' + pl.devices : ''}</div></div></div>
      ${pl ? `<div style="display:grid;gap:5px"><div class="bar"><i style="width:${pl.used / pl.total * 100}%"></i></div><div class="num" style="font-size:11.5px;color:var(--muted)">${String(pl.used).replace('.', lang === 'ru' ? ',' : '.')} ${w.of} ${pl.total} ${w.gb} · ${pl.until}</div></div>` : ''}
      <div style="display:flex;gap:14px">${pl ? `<button class="lnk">${w.renew}</button>` : ''}<button class="lnk">${w.support}</button></div></div>`;
  }
  function sideList(ctx, p, withCard) {
    const w = t().w;
    return `<div style="display:flex;gap:8px"><div style="flex:1;display:flex;align-items:center;gap:8px;height:38px;padding:0 12px;border-radius:10px;border:1px solid var(--border);color:var(--muted);font-size:13px">${ic('search')}${w.search}</div>
      <span style="width:38px;height:38px;border-radius:10px;background:var(--neutral);display:grid;place-items:center">${ic('plus')}</span></div>
      <div class="seg2">${w.tabs.map((n, i) => `<button class="tabb" tabindex="-1" aria-pressed="${i === 2}">${ic(['star', 'globe', 'folder'][i], 'width:16px;height:16px')}${n}</button>`).join('')}</div>
      <div class="scroll" style="flex:1;margin:0 -4px;padding:0 4px">${withCard ? subCard(ctx, p) : ''}
      <div class="gh"><span class="ell">${p.name.length > 14 ? w.servers : esc(w.serversOf(p.name))}</span><span class="num">${p.servers.length}</span></div>${serverRows(ctx, p)}</div>`;
  }
  function statusCard(ctx, p) {
    const w = t().w, s = cur(ctx, p), [c, txt] = st(ctx);
    return `<div class="card" style="padding:12px 16px;display:grid;grid-template-columns:auto 1fr 1fr 1fr;gap:6px 22px;align-items:center">
      <span class="pill ${c}" style="grid-row:span 2">${txt}</span><span class="lbl">${w.server}</span><span class="lbl">${w.ping}</span><span class="lbl">${w.traffic}</span>
      <span class="ell" style="font-weight:600;font-size:13px">${s[1]}</span><span class="num" style="font-weight:600;font-size:13px">${ctx.conn === 'on' ? pingTxt(s[3]) : '—'}</span><span class="num" style="font-weight:600;font-size:13px">${ctx.conn === 'on' ? w.mb : w.bytes}</span></div>`;
  }
  function connectPanel(ctx, p, width) {
    const s = cur(ctx, p), [c, txt] = st(ctx);
    return `<div class="glass" style="width:${width};max-width:100%;padding:18px;display:grid;grid-template-columns:minmax(0,1fr);gap:14px;justify-items:center">
      <span class="pill ${c}">${txt}</span>
      <div style="text-align:center;display:grid;gap:2px;max-width:100%"><div class="ell" style="font-size:22px;font-weight:600;letter-spacing:-.01em">${s[1]}</div><div class="ell" style="font-size:12.5px;color:var(--muted)">${s[2]}</div></div>
      ${connectBtn(ctx)}
      <div style="display:flex;flex-wrap:wrap;gap:8px 10px;width:100%;align-items:center;justify-content:center"><div style="flex:1 1 180px;min-width:0">${modeSeg()}</div>${routeBtn()}</div></div>`;
  }

  function vAccent(ctx) {
    const p = partner(), { v } = colours(ctx), [W, H] = SIZES[ctx.size];
    let body;
    if (ctx.size === 'mini') {
      body = `<div class="scroll stack" style="flex:1;padding:0 10px 10px;gap:10px">
        <div class="card" style="position:relative;overflow:hidden;padding:0">${wash(ctx, v)}<div style="position:relative;padding:14px;display:grid;gap:12px;justify-items:center">
          <span class="pill ${st(ctx)[0]}">${st(ctx)[1]}</span><div class="ell" style="font-size:20px;font-weight:600;max-width:100%">${cur(ctx, p)[1]}</div>${connectBtn(ctx)}${modeSeg('width:100%')}${routeBtn()}</div></div>
        ${p.actions.length ? `<div style="display:flex;gap:6px;overflow:auto;scrollbar-width:none">${chips(p, '', 'background:var(--card);box-shadow:var(--cardShadow)')}</div>` : ''}
        <div class="card" style="padding:10px;display:grid;gap:10px">${subCard(ctx, p)}<div class="gh" style="padding-top:2px"><span>${t().w.servers}</span><span class="num">${p.servers.length}</span></div>${serverRows(ctx, p)}</div></div>`;
    } else {
      const sw = ctx.size === 'wide' ? 310 : 280;
      body = `<div style="flex:1;min-height:0;display:flex;gap:10px;padding:0 10px 10px">
        <div class="card" style="width:${sw}px;flex:none;padding:12px;display:flex;flex-direction:column;gap:10px;min-height:0;background:var(--sidebar)">${sideList(ctx, p, true)}</div>
        <div style="flex:1;min-width:0;position:relative;border-radius:14px;overflow:hidden">
          <div class="map"></div>${wash(ctx, v)}
          <div style="position:absolute;inset:16px 16px auto;display:grid;justify-items:center;gap:10px">${connectPanel(ctx, p, ctx.size === 'wide' ? '500px' : 'min(440px,100%)')}
            <div style="display:flex;gap:6px;flex-wrap:wrap;justify-content:center">${chips(p, 'glass', 'border-radius:999px')}</div></div>
          <div style="position:absolute;left:12px;right:12px;bottom:12px">${statusCard(ctx, p)}</div></div></div>`;
    }
    return `<div class="win" style="width:${W}px;height:${H}px;${cssVars(v)}">${tbV2(ctx, p)}${body}</div>`;
  }

  // The stage: the partner's gradient or picture with its scrim, and the ink, ghost and solid V2pn derives for it.
  function areaStyle(sg) {
    if (sg.kind === 'image') return `background:url(${S.pics.backdrop.url}) center/cover no-repeat,${C.hex(sg.stops[0])};`;
    return `background:linear-gradient(135deg,${sg.stops.map(C.hex).join(',')});`;
  }
  const areaScrim = sg => sg.scrim ? `<div class="scrim" style="background:${C.rgba(sg.scrim[0], sg.scrim[1])}"></div>` : '';
  function brandBlock(p, sg, big, row = false) {
    const w = t().w, pl = p.plan, ink = C.hex(sg.ink), soft = C.hex(sg.soft), ghost = C.rgba(sg.ghost[0], sg.ghost[1]);
    const solid = C.hex(sg.solid), onSolid = C.hex(sg.onSolid);
    const layout = row ? 'display:flex;gap:18px;align-items:end;flex-wrap:wrap' : `display:grid;gap:${big ? 14 : 10}px`;
    return `<div style="${layout};color:${ink};min-width:0;position:relative">
      ${pl ? `<div style="font-size:12px;color:${soft}">${pl.name} · ${pl.devices}</div>
      <div style="display:flex;align-items:baseline;gap:10px"><span class="num" style="font-size:${big ? 64 : 40}px;font-weight:600;letter-spacing:-.03em;line-height:.9">${pl.days}</span><span style="font-size:${big ? 15 : 13}px">${w.daysLeft}<br><span style="color:${soft}">${pl.until}</span></span></div>
      <div style="display:grid;gap:5px;min-width:160px;max-width:${big ? 300 : 9999}px"><div class="bar" style="background:${ghost}"><i style="width:${pl.used / pl.total * 100}%;background:${ink}"></i></div><div class="num" style="font-size:11.5px;color:${soft}">${String(pl.used).replace('.', lang === 'ru' ? ',' : '.')} ${w.of} ${pl.total} ${w.gb}</div></div>`
      : `<div style="font-size:${big ? 28 : 20}px;font-weight:600;max-width:300px" class="ell">${esc(p.name)}</div>`}
      <div style="display:flex;gap:8px;flex-wrap:wrap">${pl ? `<button class="chipA press" style="background:${solid};color:${onSolid}">${ic('card', 'width:14px;height:14px')}${w.renew}</button>` : ''}<button class="chipA press" style="background:${ghost};color:${ink}">${ic('chat', 'width:14px;height:14px')}${w.support}</button></div></div>`;
  }
  function vScene(ctx) {
    const p = partner(), { v, stage: sg } = colours(ctx), [W, H] = SIZES[ctx.size];
    const inkLight = sg.ink[0] === 255;
    const logo = lh => `<div style="--lh:${lh}px;color:${C.hex(sg.ink)};position:relative;max-width:100%">${p.logo(inkLight)}</div>`;
    let body;
    if (ctx.size === 'mini') {
      body = `<div class="scroll stack" style="flex:1;padding:0 10px 10px;gap:10px">
        <!-- In the mini window the stage has no room for the plan: it stays on the card right under it. -->
        <div class="area" style="border-radius:14px;${areaStyle(sg)}">${areaScrim(sg)}
          <div style="position:relative;padding:18px 16px 76px;display:grid;gap:14px">${logo(22)}</div></div>
        <div style="margin-top:-64px;padding:0 8px;position:relative">${connectPanel(ctx, p, '100%')}</div>
        ${p.actions.length ? `<div style="display:flex;gap:6px;overflow:auto;scrollbar-width:none">${chips(p, '', 'background:var(--card);box-shadow:var(--cardShadow)')}</div>` : ''}
        <div class="card" style="padding:10px;display:grid;gap:10px">${subCard(ctx, p)}<div>${serverRows(ctx, p)}</div></div></div>`;
    } else {
      const sw = ctx.size === 'wide' ? 300 : 260, wide = ctx.size === 'wide';
      body = `<div style="flex:1;min-height:0;display:flex;gap:10px;padding:0 10px 10px">
        <div class="card" style="width:${sw}px;flex:none;padding:12px;display:flex;flex-direction:column;gap:10px;min-height:0;background:var(--sidebar)">${sideList(ctx, p, false)}</div>
        <div class="area" style="flex:1;min-width:0;border-radius:14px;${areaStyle(sg)}">${areaScrim(sg)}
          <div style="position:relative;height:100%;padding:${wide ? '28px' : '20px'};display:grid;${wide ? 'grid-template-columns:1fr 440px;gap:28px;align-items:center' : 'grid-template-rows:auto 1fr auto;gap:16px'}">
            ${wide ? `<div style="display:grid;gap:34px;align-content:center;min-width:0">${logo(34)}${brandBlock(p, sg, true)}</div>
              <div style="display:grid;gap:10px">${connectPanel(ctx, p, '100%')}<div style="display:flex;gap:6px;flex-wrap:wrap">${chips(p, 'glass')}</div></div>`
            : `${logo(24)}<div style="display:grid;align-content:center;justify-items:center;gap:10px">${connectPanel(ctx, p, 'min(420px,100%)')}<div style="display:flex;gap:6px;flex-wrap:wrap;justify-content:center">${chips(p, 'glass')}</div></div>
              ${brandBlock(p, sg, false, true)}`}
          </div></div></div>`;
    }
    return `<div class="win" style="width:${W}px;height:${H}px;${cssVars(v)}">${tbV2(ctx, p)}${body}</div>`;
  }

  // ------------------------------------------------------------------ notes, as partner-check would say them
  function notes() {
    const s = t(), th = theme(), out = [];
    out.push(`<h3>${s.hLook}</h3>`);
    const bg = th.background;
    if (bg.type === 'gradient') out.push(['ok', s.nLookGradient(bg.colors.join(' '))]);
    else if (bg.type === 'image') out.push(['ok', s.nLookImage(bg.art)]);
    else out.push(['', s.nLookAccent]);
    if (S.bg === 'none' && S.look === 'scene') out.push(['warn', s.nSceneOff]);
    for (const dark of [false, true]) {
      const sg = C.stage(th, dark);
      if (!sg) continue;
      const mode = dark ? s.modeDark : s.modeLight, Mode = mode[0].toUpperCase() + mode.slice(1);
      if (sg.seen) out.push(['', s.nInk(Mode, sg.ink[0] === 255 ? s.white : s.darkInk, C.minContrast(sg.ink, sg.seen).toFixed(1))]);
      if (sg.scrim) out.push(['warn', s.nScrim(Mode)]);
    }
    const desc = C.describe(th);
    out.push(`<h3>${s.hColours}</h3>`);
    if (!desc.length) out.push(['ok', lang === 'ru' ? 'V2pn берёт цвета как есть.' : 'V2pn takes the colours as they are.']);
    for (const n of desc) {
      const mode = n.mode === 'dark' ? s.modeDark : s.modeLight, Mode = mode[0].toUpperCase() + mode.slice(1);
      if (n.kind === 'moved') out.push(['warn', s.nMoved(n.from, n.to, s.states[n.state] || n.state)]);
      if (n.kind === 'button') out.push(['', s.nButton(Mode, n.from, n.to)]);
      if (n.kind === 'text') out.push(['', s.nText(Mode, n.to)]);
    }
    out.push(`<h3>${s.hPictures}</h3>`);
    for (const [key, role] of [['logo', s.roleLogo], ['logoOnLight', s.roleLogoLight], ['mark', s.roleMark]]) {
      if (!S.pics[key]) { out.push(['', s.picNone(role)]); continue; }
      const n = picNote(key);
      out.push([n.cls === 'ok' ? 'ok' : 'bad', `${role}. ${n.text}`]);
    }
    if (S.bg === 'image') {
      const n = S.pics.backdrop ? picNote('backdrop') : { cls: 'bad', text: s.pBackdrop };
      out.push([n.cls === 'ok' ? 'ok' : 'bad', `${s.backdrop}. ${n.text}`]);
    }
    return out.map(n => typeof n === 'string' ? n : `<div class="n ${n[0]}"><span>${esc(n[1])}</span></div>`).join('');
  }

  // ------------------------------------------------------------------ files for the owner
  async function sha256(bytes) {
    if (!crypto.subtle) throw new Error('SubtleCrypto needs https or a local page');
    const d = await crypto.subtle.digest('SHA-256', bytes);
    return [...new Uint8Array(d)].map(b => b.toString(16).padStart(2, '0')).join('');
  }
  async function partnerJson() {
    const th = theme(), brand = {};
    if (th.brand[0]) brand.accent = th.brand[0];
    if (fits('logo')) brand.logo = fileName('logo');
    if (fits('logoOnLight')) brand.logoOnLight = fileName('logoOnLight');
    if (fits('mark')) brand.mark = fileName('mark');
    const pack = { schema: 1, id: S.id, name: S.name.trim(), brand, theme: { palette: { brand: th.brand } } };
    if (th.background.type === 'gradient') pack.theme.background = { type: 'gradient', colors: th.background.colors };
    if (th.background.type === 'image') pack.theme.background = { type: 'image', art: th.background.art };
    if (S.api.trim()) pack.api = S.api.trim();
    if (S.login) pack.login = true;
    const links = {};
    if (S.cabinet.trim()) links.cabinet = S.cabinet.trim();
    if (S.support.trim()) links.support = S.support.trim();
    if (Object.keys(links).length) pack.links = links;
    if (S.actions.length) {
      pack.actions = S.actions.map((a, i) => {
        const o = { id: slug(a.label) || `button-${i + 1}`, label: a.label.trim() };
        if (a.icon) o.icon = a.icon;
        o.do = a.do;
        const need = NEEDS[a.do];
        if (need) o[need] = String(a[need]).trim();
        return o;
      });
      // Two buttons with one label still need two ids.
      const seen = new Set();
      pack.actions.forEach((a, i) => { if (seen.has(a.id)) a.id = `${a.id}-${i + 1}`.slice(0, 32); seen.add(a.id); });
    }
    const assets = [];
    for (const key of ['logo', 'logoOnLight', 'mark', 'backdrop']) {
      if (!fits(key) || (key === 'backdrop' && th.background.type !== 'image')) continue;
      assets.push({ name: fileName(key), size: S.pics[key].bytes.length, sha256: await sha256(S.pics[key].bytes) });
    }
    pack.assets = assets.sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
    return pack;
  }
  function listingJson() {
    const grants = ['edition'];
    if (S.api.trim()) grants.push('account');
    if (S.actions.length) grants.push('actions');
    const o = { id: S.id, name: S.name.trim(), domains: domainsList(), grants };
    if (S.contact.trim()) o.contact = S.contact.trim();
    return o;
  }
  function download(name, text) {
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([text], { type: 'application/json' }));
    a.download = name;
    document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(a.href), 4000);
  }
  function drawSend() {
    const s = t(), items = [s.sFiles];
    for (const [key, role] of [['logo', s.roleLogo], ['logoOnLight', s.roleLogoLight], ['mark', s.roleMark], ['backdrop', s.backdrop]]) {
      if (!fits(key) || (key === 'backdrop' && S.bg !== 'image')) continue;
      items.push(`${s.sPic(role, fileName(key))} (<a href="${S.pics[key].url}" download="${fileName(key)}">${s.sPicDl}</a>)`);
    }
    if (!S.contact.trim()) items.push(s.sContact);
    const g = [];
    if (S.api.trim()) g.push(s.gAccount);
    if (S.actions.length) g.push(s.gActions);
    if (g.length) items.push(s.sGrants(g.join(lang === 'ru' ? ' и ' : ' and ')));
    if (S.login) items.push(s.sLogin);
    items.push(s.sUntil);
    $('send').innerHTML = items.map(i => `<li>${i}</li>`).join('');
  }

  // ------------------------------------------------------------------ drawing
  let queued = false;
  function render() {
    if (queued) return;
    queued = true;
    requestAnimationFrame(() => {
      queued = false;
      const scene = S.bg !== 'none' && (S.bg !== 'image' || fits('backdrop'));
      document.querySelector('[data-k="look"] [data-v="scene"]').disabled = !scene;
      const look = scene ? S.look : 'accent';
      document.querySelectorAll('.pv-bar .seg').forEach(g => g.querySelectorAll('button').forEach(b => {
        const k = g.dataset.k, val = k === 'dark' ? (S.dark ? '1' : '0') : k === 'look' ? look : S[k];
        b.setAttribute('aria-pressed', String(b.dataset.v === val));
      }));
      const ctx = { dark: S.dark, size: S.size, conn: S.conn, sel: S.sel };
      $('pv-frame').innerHTML = look === 'scene' ? vScene(ctx) : vAccent(ctx);
      $('pv-notes').innerHTML = notes();
      const ps = problems();
      $('problems').innerHTML = ps.map(p => `<div class="problem">${esc(p)}</div>`).join('');
      $('dl-partner').disabled = $('dl-listing').disabled = ps.length > 0;
      const gh = $('gradient-hint');
      gh.hidden = S.bg !== 'gradient';
      gh.textContent = brandColours().length >= 2 ? t().gradientMany : t().gradientOne;
      drawPictures();
      drawSend();
      fit();
    });
  }
  function fit() {
    const [w, h] = SIZES[S.size], box = $('pv-stage').getBoundingClientRect();
    const k = Math.min(1, (box.width - 32) / w, (box.height - 32) / h);
    $('pv-frame').style.transform = `translate(-50%, -50%) scale(${Math.max(0.1, k)})`;
  }

  function applyLang() {
    const s = t();
    document.documentElement.lang = lang;
    document.title = s.docTitle;
    document.querySelectorAll('[data-l]').forEach(el => { if (typeof s[el.dataset.l] === 'string') el.textContent = s[el.dataset.l]; });
    document.querySelectorAll('[data-l-label]').forEach(el => el.setAttribute('aria-label', s[el.dataset.lLabel]));
    document.querySelectorAll('.top [data-lang]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.lang === lang)));
    const brand = document.querySelector('.brand');
    brand.href = lang === 'ru' ? brand.dataset.hrefRu : brand.dataset.hrefEn;
    drawColours();
    drawActions();
    render();
  }

  // ------------------------------------------------------------------ wiring
  const form = $('form');
  const bind = (id, key, after) => $(id).addEventListener('input', e => { S[key] = e.target.value; if (after) after(); render(); });
  bind('f-name', 'name', () => { if (!S.idTouched) { S.id = slug(S.name); $('f-id').value = S.id; } });
  $('f-id').addEventListener('input', e => { S.idTouched = true; S.id = e.target.value.trim(); render(); });
  bind('f-domains', 'domains');
  bind('f-contact', 'contact');
  bind('f-cabinet', 'cabinet');
  bind('f-support', 'support');
  bind('f-api', 'api');
  $('f-login').addEventListener('change', e => { S.login = e.target.checked; render(); });
  form.addEventListener('submit', e => e.preventDefault());
  document.querySelectorAll('input[name=bg]').forEach(r => r.addEventListener('change', e => {
    S.bg = e.target.value;
    if (S.bg !== 'none') S.look = 'scene';
    render();
  }));
  document.querySelectorAll('.pic input[type=file]').forEach(inp => inp.addEventListener('change', async e => {
    const key = e.target.closest('.pic').dataset.key, file = e.target.files[0];
    if (!file) return;
    await readPicture(key, file);
    render();
  }));
  $('colours').addEventListener('input', e => {
    const i = Number(e.target.dataset.i);
    if (Number.isNaN(i)) return;
    let val = e.target.value.trim();
    if (e.target.type === 'color') val = val.toUpperCase();
    else if (val && !val.startsWith('#')) val = '#' + val;
    S.brand[i] = val;
    const row = e.target.closest('.colour'), text = row.querySelector('input:not([type])'), pick = row.querySelector('input[type=color]');
    if (e.target.type === 'color') text.value = val; else if (C.parse(val)) pick.value = val.toLowerCase();
    text.classList.toggle('bad', !C.parse(val));
    render();
  });
  $('colours').addEventListener('click', e => {
    const b = e.target.closest('[data-rm]');
    if (!b) return;
    S.brand.splice(Number(b.dataset.rm), 1);
    drawColours(); render();
  });
  $('add-colour').addEventListener('click', () => {
    const c = C.parse(brandColours()[0] || '#FF7A2F'), [ch, h] = C.oklch(c);
    S.brand.push(C.hex(C.fromOklch(Math.min(C.okL(c) + 0.1, 0.9), ch, h + 24)));
    drawColours(); render();
  });
  $('add-action').addEventListener('click', () => {
    const s = t();
    S.actions.push({ label: S.actions.length ? '' : s.do['connect.best'], icon: S.actions.length ? '' : 'bolt', do: 'connect.best', strategy: 'ping', purpose: 'youtube', server: '', group: '', url: '' });
    drawActions(); render();
  });
  $('actions').addEventListener('input', e => {
    const i = Number(e.target.dataset.a), f = e.target.dataset.f;
    if (Number.isNaN(i) || !f) return;
    S.actions[i][f] = e.target.value;
    if (f === 'do') drawActions();
    if (f === 'label') { const c = e.target.closest('.field').querySelector('.count'); if (c) c.textContent = `${e.target.value.length}/24`; }
    render();
  });
  $('actions').addEventListener('click', e => {
    const b = e.target.closest('[data-rm-a]');
    if (!b) return;
    S.actions.splice(Number(b.dataset.rmA), 1);
    drawActions(); render();
  });
  document.querySelectorAll('.pv-bar .seg').forEach(g => g.addEventListener('click', e => {
    const b = e.target.closest('button');
    if (!b || b.disabled) return;
    const k = g.dataset.k;
    if (k === 'dark') S.dark = b.dataset.v === '1'; else S[k] = b.dataset.v;
    render();
  }));
  $('pv-plan').addEventListener('change', e => { S.plan = e.target.checked; render(); });
  let connTimer;
  $('pv-frame').addEventListener('click', e => {
    const b = e.target.closest('[data-act]');
    if (!b) return;
    if (b.dataset.act === 'conn') {
      clearTimeout(connTimer);
      if (S.conn === 'on') S.conn = 'off';
      else { S.conn = 'wait'; connTimer = setTimeout(() => { S.conn = 'on'; render(); }, 700); }
    } else if (b.dataset.act === 'sel') S.sel = Number(b.dataset.i);
    render();
  });
  $('dl-partner').addEventListener('click', async () => {
    try { download('partner.json', JSON.stringify(await partnerJson(), null, 2) + '\n'); } catch (err) { $('problems').innerHTML = `<div class="problem">${esc(err.message)}</div>`; }
  });
  $('dl-listing').addEventListener('click', () => download('listing.json', JSON.stringify(listingJson(), null, 2) + '\n'));
  document.querySelectorAll('.top [data-lang]').forEach(b => b.addEventListener('click', () => {
    lang = b.dataset.lang;
    const url = new URL(location.href);
    if (lang === 'en') url.searchParams.set('lang', 'en'); else url.searchParams.delete('lang');
    history.replaceState(null, '', url);
    applyLang();
  }));
  window.addEventListener('resize', fit);

  // A page opened with ?demo=1 starts filled in, for a first look and for screenshots.
  const q = new URLSearchParams(location.search);
  if (q.get('demo')) {
    Object.assign(S, { name: 'Kestrel', id: 'kestrel', domains: 'kestrel.example\n*.kestrel.example', contact: '@kestrel_admin', brand: ['#2F7BFF', '#7C4DFF'],
      bg: 'gradient', login: true, api: 'https://kestrel.example/v2pn/api/', cabinet: 'https://kestrel.example/cabinet', support: 'https://t.me/kestrel_support',
      actions: [{ label: lang === 'ru' ? 'Самый быстрый' : 'Fastest', icon: 'bolt', do: 'connect.best', strategy: 'ping' },
        { label: 'YouTube', icon: 'play', do: 'connect.recommend', purpose: 'youtube' }, { label: lang === 'ru' ? 'Продлить' : 'Renew', icon: 'card', do: 'open.renew' }] });
    for (const [id, v] of [['f-name', S.name], ['f-id', S.id], ['f-domains', S.domains], ['f-contact', S.contact], ['f-api', S.api], ['f-cabinet', S.cabinet], ['f-support', S.support]]) $(id).value = v;
    $('f-login').checked = true;
    document.querySelector(`input[name=bg][value=${S.bg}]`).checked = true;
    S.idTouched = true;
  }
  // ?pack=<a partners/<id>/ folder> opens a partner that exists, to look at it or change it (served beside the repo).
  async function openPack(base) {
    base = base.endsWith('/') ? base : base + '/';
    const json = async name => { const r = await fetch(base + name); if (!r.ok) throw new Error(name); return r.json(); };
    const pack = await json('partner.json');
    const listing = await json('listing.json').catch(() => ({}));
    const th = pack.theme || {}, bg = th.background || {};
    Object.assign(S, { name: pack.name || '', id: pack.id || '', domains: (listing.domains || []).join('\n'), contact: listing.contact || '',
      brand: ((th.palette && th.palette.brand) || [pack.brand && pack.brand.accent].filter(Boolean)).slice(0, 3),
      bg: bg.type === 'gradient' ? 'gradient' : bg.type === 'image' ? 'image' : 'none', login: pack.login === true, api: pack.api || '',
      cabinet: (pack.links && pack.links.cabinet) || '', support: (pack.links && pack.links.support) || '',
      actions: (pack.actions || []).slice(0, 4).map(a => ({ strategy: 'ping', purpose: 'youtube', server: '', group: '', url: '', icon: '', ...a })), idTouched: true });
    if (!S.brand.length) S.brand = ['#FF7A2F'];
    const files = { logo: pack.brand && pack.brand.logo, logoOnLight: pack.brand && pack.brand.logoOnLight, mark: pack.brand && pack.brand.mark, backdrop: bg.art };
    for (const [key, name] of Object.entries(files)) {
      if (!name) continue;
      const r = await fetch(base + name);
      if (r.ok) await readPicture(key, new File([await r.blob()], name));
    }
    for (const [id, v] of [['f-name', S.name], ['f-id', S.id], ['f-domains', S.domains], ['f-contact', S.contact], ['f-api', S.api], ['f-cabinet', S.cabinet], ['f-support', S.support]]) $(id).value = v;
    $('f-login').checked = S.login;
    document.querySelector(`input[name=bg][value=${S.bg}]`).checked = true;
    if (S.bg !== 'none' && !q.get('look')) S.look = 'scene';
    applyLang();
  }
  if (q.get('pack')) openPack(q.get('pack')).catch(err => { $('problems').innerHTML = `<div class="problem">${esc(String(err.message || err))}</div>`; });

  for (const k of ['look', 'size']) if (q.get(k)) S[k] = q.get(k);
  if (q.get('dark')) S.dark = q.get('dark') === '1';
  if (!q.get('dark') && window.matchMedia) S.dark = window.matchMedia('(prefers-color-scheme: dark)').matches;
  if (q.get('look') === 'scene' && S.bg === 'none') S.bg = 'gradient';
  document.querySelector(`input[name=bg][value=${S.bg}]`).checked = true;
  applyLang();
})();
