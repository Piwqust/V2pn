// A provider's public page, /p/<id>/ (it began as a port of the former tools/partner-page.py: its colours, checks and
// layout), made for the hub's way of coming from a provider: the page offers "Открыть в V2pn" (v2pn://add?url=…&partner=<id>)
// when its address carries #sub=<subscription>, and the downloads go through /r/<name> (plain V2pn, no marker).
// Drawn in the site's system (Inter Tight and Inter, square chrome with the fork's 45° cut, the window of
// /assets/window.css) in the provider's colours.
import fs from 'node:fs';
import { colors as C } from './colors.mjs';

const SCRIPT = fs.readFileSync(new URL('./page-script.js', import.meta.url), 'utf8');

// Ground and text of the page: V2pn's own tokens (Assets/V2pnTheme.axaml).
const LIGHT = { bg: '#ECEEF3', card: '#FFFFFF', muted_bg: '#F0F1F5', line: '#DCDEE5', ink: '#16161B', soft: '#34343F', muted: '#5E5E6B' };
const DARK = { bg: '#0E0E11', card: '#1A1A21', muted_bg: '#26262F', line: '#2A2A33', ink: '#ECECF1', soft: '#C9C9D3', muted: '#9B9BA8' };
const ON_ACCENT_INK = C.parse('#0B0B0D');
const V2MARK = '<svg class="v2" viewBox="0 0 17 18" aria-hidden="true"><path fill="#FF3D1F" d="M0 0h5.2l3.3 6.4L11.8 0H17L10.9 11.2V18H6.1v-6.8z"/></svg>';

const esc = s => String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const same = (a, b) => a[0] === b[0] && a[1] === b[1] && a[2] === b[2];
const rgba = (c, a) => C.rgba(c, a);
const onAccent = accent => (C.contrast(ON_ACCENT_INK, accent) >= C.contrast(C.WHITE, accent) ? ON_ACCENT_INK : C.WHITE);

/**
 * The page's colours in the light and dark modes and every pair of text and ground checked at 4.5:1, as
 * the former partner-page.py computed them. {modes, checks: [{what, ratio}], failures}.
 */
export function pageTheme(theme) {
  const accent = C.awayFromState(theme.brand.length ? C.parse(theme.brand[0]) : C.parse(C.FALLBACK_BRAND));
  const checks = [];
  const check = (what, text, ground) => checks.push({ what, ratio: C.contrast(text, ground) });
  const modes = {};
  for (const [mode, tokens] of [['light', LIGHT], ['dark', DARK]]) {
    const dark = mode === 'dark';
    const t = Object.fromEntries(Object.entries(tokens).map(([k, v]) => [k, C.parse(v)]));
    for (const k of ['ink', 'soft', 'muted']) for (const g of ['bg', 'card', 'muted_bg']) check(`${mode}: ${k} on ${g}`, t[k], t[g]);
    // The app's label (Appearance.OnAccent) where it reads; a mid-tone brand moves until its label does.
    const [fill, label] = C.contrast(onAccent(accent), accent) >= C.TEXT ? [accent, onAccent(accent)] : C.withLabel(accent, C.WHITE, ON_ACCENT_INK, t.card);
    check(`${mode}: label on the brand button`, label, fill);
    const link = C.readableOn(accent, t.card);
    const linkBg = C.readableOn(accent, t.bg);
    check(`${mode}: brand link on card`, link, t.card);
    check(`${mode}: brand link on ground`, linkBg, t.bg);
    const v = {
      bg: tokens.bg, card: tokens.card, mutedBg: tokens.muted_bg, line: tokens.line, ink: tokens.ink, soft: tokens.soft, muted: tokens.muted,
      accent: C.hex(fill), onAccent: C.hex(label), link: C.hex(link), linkBg: C.hex(linkBg), focus: C.hex(link),
    };
    const st = C.stage(theme, dark);
    if (st) {
      if (st.kind === 'gradient') {
        v.hero = `linear-gradient(135deg, ${st.stops.map(C.hex).join(', ')})`;
        v.scrim = st.scrim ? rgba(st.scrim[0], st.scrim[1]) : 'transparent';
        checks.push({ what: `${mode}: stage ink at its worst point`, ratio: C.minContrast(st.ink, st.seen) });
        checks.push({ what: `${mode}: stage soft at its worst point`, ratio: C.minContrast(st.soft, st.seen) });
        const ghostSeen = st.seen.map(s => C.over(st.ghost[0], s, st.ghost[1]));
        checks.push({ what: `${mode}: stage ink on a quiet button`, ratio: C.minContrast(st.ink, ghostSeen) });
      } else {
        v.hero = `url(${theme.background.art}) center / cover no-repeat, ${C.hex(st.stops[0])}`;
        const alpha = Math.round(st.scrim[1] * 255) / 255;
        v.scrim = rgba(st.scrim[0], st.scrim[1]);
        // Whatever the picture holds: its darkest and lightest pixels under the scrim.
        const worst = [C.over(st.scrim[0], C.BLACK, alpha), C.over(st.scrim[0], C.WHITE, alpha)];
        checks.push({ what: `${mode}: stage ink over any pixel`, ratio: C.minContrast(st.ink, worst) });
        const ghostSeen = worst.map(w => C.over(st.ghost[0], w, st.ghost[1]));
        checks.push({ what: `${mode}: stage ink on a quiet button`, ratio: Math.min(...ghostSeen.map(g => C.contrast(st.ink, g))) });
      }
      check(`${mode}: label on the stage's strong button`, st.onSolid, st.solid);
      Object.assign(v, { heroInk: C.hex(st.ink), heroSoft: C.hex(st.soft), ghost: rgba(st.ghost[0], st.ghost[1]), solid: C.hex(st.solid), onSolid: C.hex(st.onSolid) });
      v.heroLight = same(st.ink, C.WHITE);
    } else {
      Object.assign(v, { hero: tokens.card, scrim: 'transparent', heroInk: tokens.ink, heroSoft: tokens.muted, ghost: tokens.muted_bg, solid: C.hex(fill), onSolid: C.hex(label) });
      v.heroLight = dark;
    }
    modes[mode] = v;
  }
  const failures = checks.filter(c => c.ratio < C.TEXT);
  return { modes, checks, failures };
}

function strings(name, login, cabinet) {
  const n = esc(name);
  const href = esc(cabinet);
  const where = cabinet ? `в боте или <a href="${href}">кабинете</a> ${n}` : `в боте или кабинете ${n}`;
  const whereEn = cabinet ? `in ${n}'s bot or <a href="${href}">cabinet</a>` : `in ${n}'s bot or cabinet`;
  const ru = {
    title: `V2pn для ${n}`,
    h1: `${n} на компьютере`,
    lead: `Ваша подписка ${n} в V2pn, бесплатном VPN-клиенте для Windows и macOS. Во вкладке ${n} ваш тариф, серверы и цвета ${n}. `
      + (login ? `Войти можно по коду из бота ${n}, без ссылок.` : 'Подключается в один клик.'),
    open: 'Открыть в V2pn',
    openNote: 'Браузер спросит, открыть ли V2pn. Если V2pn ещё нет, скачайте его ниже, установите и нажмите «Открыть в V2pn» ещё раз.',
    win: 'Скачать для Windows',
    mac: 'Скачать для macOS',
    winNote: 'Windows 10 и 11 · <a href="/r/windows-arm64" data-os-link="win-alt" data-dl-link>для ARM</a>',
    winNoteArm: 'Windows на ARM · <a href="/r/windows-x64" data-os-link="win-alt" data-dl-link>для x64</a>',
    macNote: 'macOS 13.6 и новее, Apple silicon · <a href="/r/macos-x64" data-dl-link>для Intel</a>',
    afterTitle: 'Скачивание началось',
    afterText: `Установите V2pn и нажмите кнопку «Подключить в V2pn» в боте ${n} ещё раз: V2pn откроется и добавит подписку с вкладкой ${n}.`,
    afterTextSub: 'Установите V2pn и нажмите «Открыть в V2pn» на этой странице ещё раз: V2pn добавит подписку с вашей вкладкой.',
    phoneTitle: 'V2pn работает на компьютере',
    phoneText: 'На телефоне V2pn не работает. Откройте эту страницу на компьютере с Windows или macOS.',
    share: 'Отправить ссылку себе',
    copied: 'Ссылка скопирована',
    phoneFiles: 'Файлы для компьютера: <a href="/r/windows-x64">Windows</a> · <a href="/r/macos-arm64">macOS</a>',
    stepsTitle: 'Три шага',
    s1: 'Установите V2pn',
    s1win: 'Распакуйте архив в свою папку и запустите V2pn.exe. Если Windows скажет, что защитила компьютер, нажмите «Подробнее» → «Выполнить в любом случае».',
    s1mac: 'Откройте образ и перетащите V2pn в «Программы». В первый раз разрешите запуск: «Системные настройки» → «Конфиденциальность и безопасность» → «Всё равно открыть».',
    cmdWin: 'Или одной командой в PowerShell:',
    cmdMac: 'Или одной командой в Терминале:',
    s2: login ? `Войдите в ${n}` : 'Нажмите «Подключить в V2pn»',
    s2t: login
      ? 'При первом запуске нажмите «У меня подписка от провайдера». Если V2pn уже настроен: «+» → «Войти через провайдера».'
      : `Кнопка ${where} откроет V2pn и добавит вашу подписку с вкладкой ${n}.`,
    s3: login ? 'Подтвердите в боте' : 'Подключитесь',
    s3t: login
      ? `V2pn покажет код. Подтвердите его в боте ${n}, и V2pn сам добавит подписку: останется нажать «Подключиться».`
      : `Нажмите «Подключиться». Серверы ${n} во вкладке ${n} вверху окна.`,
    bot: 'Открыть бота',
    what: 'V2pn: бесплатный VPN-клиент с открытым кодом для Windows и macOS. Он ничего не знает о вас и никуда не отправляет ваши данные.',
    whatLink: 'Что такое V2pn',
    art: `Окно V2pn во вкладке ${n}: карточка тарифа, кнопка «Подключиться» и самые быстрые серверы`,
    foot: 'V2pn: открытый код, GPL-3.0. <a href="https://github.com/Piwqust/V2pn">V2pn на GitHub</a>',
    lang: 'Язык',
    report: 'Пожаловаться',
    reportText: 'Что не так? Можно не писать.',
    reportSend: 'Отправить жалобу',
    reportDone: 'Жалоба отправлена. Модератор V2pn посмотрит.',
    reportFail: 'Не получилось отправить. Попробуйте позже.',
  };
  const en = {
    title: `V2pn for ${n}`,
    h1: `${n} on your computer`,
    lead: `Your ${n} subscription in V2pn, a free VPN client for Windows and macOS. The ${n} tab shows your plan, servers and ${n}'s colours. `
      + (login ? `Sign in with a code from ${n}'s bot, no links to copy.` : 'It connects in one click.'),
    open: 'Open in V2pn',
    openNote: 'Your browser asks whether to open V2pn. No V2pn yet? Download it below, install it and press “Open in V2pn” again.',
    win: 'Download for Windows',
    mac: 'Download for macOS',
    winNote: 'Windows 10 and 11 · <a href="/r/windows-arm64" data-os-link="win-alt" data-dl-link>for ARM</a>',
    winNoteArm: 'Windows on ARM · <a href="/r/windows-x64" data-os-link="win-alt" data-dl-link>for x64</a>',
    macNote: 'macOS 13.6 or later, Apple silicon · <a href="/r/macos-x64" data-dl-link>for Intel</a>',
    afterTitle: 'The download has started',
    afterText: `Install V2pn and press “Connect in V2pn” in ${n}'s bot again: V2pn opens and adds the subscription with the ${n} tab.`,
    afterTextSub: 'Install V2pn and press “Open in V2pn” on this page again: V2pn adds the subscription with your tab.',
    phoneTitle: 'V2pn is for computers',
    phoneText: 'V2pn does not run on phones. Open this page on a Windows or macOS computer.',
    share: 'Send the link to yourself',
    copied: 'Link copied',
    phoneFiles: 'Files for a computer: <a href="/r/windows-x64">Windows</a> · <a href="/r/macos-arm64">macOS</a>',
    stepsTitle: 'Three steps',
    s1: 'Install V2pn',
    s1win: 'Unpack the archive into a folder of yours and run V2pn.exe. If Windows says it protected your PC, press “More info”, then “Run anyway”.',
    s1mac: 'Open the disk image and drag V2pn to Applications. The first time, allow it: System Settings → Privacy & Security → “Open Anyway”.',
    cmdWin: 'Or in one command in PowerShell:',
    cmdMac: 'Or in one command in Terminal:',
    s2: login ? `Sign in to ${n}` : 'Press “Connect in V2pn”',
    s2t: login
      ? 'At first start press “I have a subscription from a provider”. If V2pn is set up already: “+” → “Sign in with a provider”.'
      : `The button ${whereEn} opens V2pn and adds your subscription with the ${n} tab.`,
    s3: login ? 'Confirm in the bot' : 'Connect',
    s3t: login
      ? `V2pn shows a code. Confirm it in ${n}'s bot and V2pn adds the subscription itself: then press “Connect”.`
      : `Press “Connect”. ${n}'s servers are in the ${n} tab at the top of the window.`,
    bot: 'Open the bot',
    what: 'V2pn is a free, open-source VPN client for Windows and macOS. It knows nothing about you and sends your data nowhere.',
    whatLink: 'What V2pn is',
    art: `V2pn's window in the ${n} tab: the plan card, the Connect button and the fastest servers`,
    foot: 'V2pn is open source, GPL-3.0. <a href="https://github.com/Piwqust/V2pn">V2pn on GitHub</a>',
    lang: 'Language',
    report: 'Report',
    reportText: 'What is wrong? You may leave it empty.',
    reportSend: 'Send the report',
    reportDone: 'Report sent. A V2pn moderator will look at it.',
    reportFail: 'Could not send it. Try again later.',
  };
  return { ru, en };
}

const FORK_PATH = 'M0,0 L32,0 L32,40 L72,0 L104,0 L104,32 L85.255,32 L64,53.255 L64,112 L32,112 L32,72 L0,40 Z';
const ICON_POWER = '<svg class="ic" viewBox="0 0 24 24"><path d="M12 3v8M6.3 6.3a8 8 0 1 0 11.4 0"/></svg>';
const ICON_MENU = '<svg class="ic" viewBox="0 0 24 24"><path d="M4 7h16M4 12h16M4 17h16"/></svg>';

/**
 * V2pn's window in the provider's tab, as the app draws it (the site's .v2w, /assets/window.css): the plan card
 * and the map for Accent, the provider's background for Scene. Decorative: the hero says the same in words.
 */
function windowHtml({ theme, dark, name, files, dims = () => '' }) {
  const d = C.derive(theme, dark);
  const st = C.stage(theme, dark);
  const n = esc(name);
  const accent = C.hex(d.accent), onAccent = C.hex(d.onAccent), accentText = C.hex(d.accentText);
  const mark = files.mark ? `<img src="${files.mark}" alt="" width="16" height="16">`
    : `<span class="pp-letter" style="background: ${accent}; color: ${onAccent}">${esc(name.trim().charAt(0).toUpperCase())}</span>`;
  const rows = [['de', 'Германия', 'VLESS Reality', 41], ['nl', 'Нидерланды', 'VLESS Reality', 48], ['fi', 'Финляндия', 'Hysteria2', 52], ['se', 'Швеция', 'Hysteria2', 57], ['pl', 'Польша', 'Trojan', 63]]
    .map(([cc, c, p, ms], i) => `<div class="v2-row${i ? '' : ' is-on'}"><img src="/assets/v2pn/flags/${cc}.png" alt="" width="24" height="18"><span><b>${c}</b><small>${p}</small></span><span class="v2-ping">${ms} мс</span></div>`).join('');
  const plan = `<div class="v2-plan"><div class="v2-plan__head">${files.mark ? `<img src="${files.mark}" alt="" width="30" height="30">` : mark}<span><b>${n}</b><small>Ваш тариф · 77 дней</small></span></div>
    <div class="v2-bar" style="--v: 24%"><i></i></div><small>12,4 из 50 ГБ · до 31.01.2027</small><div class="v2-plan__links"><span>Продлить</span><span>Поддержка</span></div></div>`;
  let main;
  if (st) {
    const ink = C.hex(st.ink);
    const scrim = st.scrim ? C.rgba(st.scrim[0], st.scrim[1]) : null;
    const bg = st.kind === 'image'
      ? `linear-gradient(${scrim}, ${scrim}), url(${files.backdrop}) center / cover no-repeat, ${C.hex(st.stops[0])}`
      : `${scrim ? `linear-gradient(${scrim}, ${scrim}), ` : ''}linear-gradient(135deg, ${st.stops.map(C.hex).join(', ')})`;
    const logo = same(st.ink, C.WHITE) ? (files.logo || files.logoOnLight) : (files.logoOnLight || files.logo);
    main = `<div class="v2-main v2-main--scene" style="--w-scene: ${bg}">
      <div class="v2-panel-dark"><div class="v2-head" style="margin-top: 12px"><span class="v2-pill v2-pill--off">Не защищено</span><span class="v2-title">Германия</span></div>
      <span class="v2-connect v2-connect--go" style="color: ${onAccent}">${ICON_POWER}Подключиться</span></div>
      <div class="v2-scene-plan" style="color: ${ink}">${logo ? `<img src="${logo}" alt=""${dims(logo)}>` : `<span class="pp-scene-name">${n}</span>`}
        <span class="days">77<small style="color: ${C.hex(st.soft)}">осталось дней<br>до 31 января 2027</small></span>
        <div class="v2-scene-plan__btns"><span style="background: ${C.hex(st.solid)}; color: ${C.hex(st.onSolid)}">Продлить</span><span style="background: ${C.rgba(st.ghost[0], st.ghost[1])}; color: ${ink}">Поддержка</span></div></div></div>`;
  } else {
    main = `<div class="v2-main"><span class="v2-map"></span>
      <div class="v2-head"><span class="v2-pill v2-pill--off">Не защищено</span><span class="v2-title"><img class="v2-flag" src="/assets/v2pn/flags/de.png" alt="" width="34" height="25">Германия</span><span class="v2-sub">VLESS Reality</span></div>
      <span class="v2-connect v2-connect--go" style="color: ${onAccent}">${ICON_POWER}Подключиться</span>
      <div class="v2-chips"><span>Самый быстрый</span><span>Продлить</span></div></div>`;
  }
  return `<div class="v2w ui-v2pn pp-win pp-win--${dark ? 'dark' : 'light'}"${dark ? ' data-theme="dark"' : ''} style="--w-brand: ${accent}; --w-brand-text: ${accentText}" aria-hidden="true">
    <div class="v2w__bar">${ICON_MENU}<span class="v2-tabs-bar"><span class="v2-tab"><svg class="fork" viewBox="0 0 104 112"><path d="${FORK_PATH}"/></svg>V2pn</span><span class="v2-tab is-on">${mark}${n}</span></span><span class="v2w__sp"></span></div>
    <div class="v2w__body"><div class="v2-side"><div class="v2-seg"><span class="is-on">Главная</span><span>Страны</span><span>Подписки</span></div>${st ? '' : plan}<div class="v2-label"><span>Самые быстрые</span></div>${rows}</div>${main}</div></div>`;
}

/**
 * The files of /p/<id>/: index.html, page.js and the pictures it shows. <pictures> maps logo / logoOnLight / mark /
 * backdrop to their PNG bytes (only those the pack carries). Throws when a pair of text and ground would not read.
 * The page takes the site's fonts, its window styles (/assets/window.css), V2pn's flags and map from the site.
 */
export function buildPage({ id, name, look, theme, pictures, origin, home = '/' }) {
  const { modes, failures } = pageTheme(theme);
  if (failures.length) throw new Error(`text would not read: ${failures.map(f => `${f.what} ${f.ratio.toFixed(2)}`).join('; ')}`);
  const logo = pictures.logo ? 'logo-light.png' : null;
  const logoLight = pictures.logoOnLight ? 'logo-dark.png' : null;
  const files = { logo, logoOnLight: logoLight, mark: pictures.mark ? 'mark.png' : null, backdrop: theme.background.type === 'image' && pictures.backdrop ? 'background.png' : null };
  const login = look.login === true && Boolean(look.api);
  const support = look.links?.support || '';
  const bot = /^(https:\/\/t\.me\/|tg:)/.test(support) ? support : null;
  const cabinet = /^https:\/\//.test(look.links?.cabinet || '') ? look.links.cabinet : '';
  const { ru, en } = strings(name, login, cabinet);

  const logoFor = lightInk => (lightInk ? (logo || logoLight) : (logoLight || logo));
  const heroLogo = { light: logoFor(modes.light.heroLight), dark: logoFor(modes.dark.heroLight) };
  const barLogo = { light: logoLight || logo, dark: logo || logoLight };
  // The pictures' own sizes on their <img>, so the page does not shift while they load.
  const bytesOf = { 'logo-light.png': pictures.logo, 'logo-dark.png': pictures.logoOnLight };
  const dims = file => {
    const b = bytesOf[file];
    return b && b.length > 24 && b.readUInt32BE(12) === 0x49484452 ? ` width="${b.readUInt32BE(16)}" height="${b.readUInt32BE(20)}"` : '';
  };
  const pic = (cls, set, alt) => {
    if (!set.light && !set.dark) return `<span class="${cls} name">${esc(name)}</span>`;
    if (set.light === set.dark) return `<img class="${cls}" src="${set.light}" alt="${alt}"${dims(set.light)}>`;
    return `<picture><source srcset="${set.dark}" media="(prefers-color-scheme: dark)"><img class="${cls}" src="${set.light}" alt="${alt}"${dims(set.light)}></picture>`;
  };
  const keys = ['bg', 'card', 'mutedBg', 'line', 'ink', 'soft', 'muted', 'accent', 'onAccent', 'link', 'linkBg', 'focus', 'hero', 'scrim', 'heroInk', 'heroSoft', 'ghost', 'solid', 'onSolid'];
  const tokens = v => keys.map(k => `--${k}: ${v[k]}`).join('; ');
  const commands = { win: `irm ${origin}/r/install.ps1 | iex`, mac: `curl -fsSL ${origin}/r/install.sh | bash` };
  const botHtml = bot ? `<a class="pp-btn pp-btn--quiet pp-btn--small" href="${esc(bot)}" data-t="bot">${ru.bot}</a>` : '';
  const data = JSON.stringify({ id, name, text: { ru, en } }).replace(/</g, '\\u003c');
  const winLight = windowHtml({ theme, dark: false, name, files, dims });
  const winDark = windowHtml({ theme, dark: true, name, files, dims });

  const html = `<!doctype html>
<html lang="ru">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<title>${ru.title}</title>
<meta name="description" content="${esc(name)} в V2pn, бесплатном VPN-клиенте для Windows и macOS: подписка со своей вкладкой в один клик.">
<meta name="robots" content="noindex">
<link rel="canonical" href="${esc(origin)}/p/${esc(id)}/">
<meta property="og:type" content="website">
<meta property="og:site_name" content="V2pn">
<meta property="og:title" content="${ru.title}">
<meta property="og:description" content="${esc(name)} в V2pn, бесплатном VPN-клиенте для Windows и macOS: подписка со своей вкладкой в один клик.">
<meta property="og:url" content="${esc(origin)}/p/${esc(id)}/">
<meta property="og:image" content="${esc(origin)}/share.png">
<meta name="twitter:card" content="summary_large_image">
<meta name="theme-color" content="${modes.light.bg}" media="(prefers-color-scheme: light)">
<meta name="theme-color" content="${modes.dark.bg}" media="(prefers-color-scheme: dark)">
<link rel="icon" href="/favicon.svg" type="image/svg+xml">
<link rel="preload" href="/assets/fonts/inter-tight-cyrillic.woff2" as="font" type="font/woff2" crossorigin>
<link rel="stylesheet" href="/assets/window.css">
<style>
@font-face { font-family: "Inter Tight"; src: url(/assets/fonts/inter-tight-cyrillic.woff2) format("woff2"); font-weight: 200 800; font-display: swap; unicode-range: U+0301, U+0400-045F, U+0490-0491, U+04B0-04B1, U+2116; }
@font-face { font-family: "Inter Tight"; src: url(/assets/fonts/inter-tight-latin.woff2) format("woff2"); font-weight: 200 800; font-display: swap; unicode-range: U+0000-00FF, U+0131, U+0152-0153, U+2000-206F, U+20AC, U+2122, U+2212; }
@font-face { font-family: Inter; src: url(/assets/fonts/inter-cyrillic.woff2) format("woff2"); font-weight: 400 600; font-display: swap; unicode-range: U+0301, U+0400-045F, U+0490-0491, U+04B0-04B1, U+2116; }
@font-face { font-family: Inter; src: url(/assets/fonts/inter-latin.woff2) format("woff2"); font-weight: 400 600; font-display: swap; unicode-range: U+0000-00FF, U+0131, U+0152-0153, U+2000-206F, U+20AC, U+2122, U+2212; }
@font-face { font-family: "JetBrains Mono"; src: url(/assets/fonts/jetbrains-mono-latin.woff2) format("woff2"); font-weight: 400 500; font-display: swap; }
:root { color-scheme: light; ${tokens(modes.light)};
  --font-text: Inter, "Segoe UI", system-ui, -apple-system, sans-serif; --font-display: "Inter Tight", Inter, system-ui, sans-serif; --font-mono: "JetBrains Mono", ui-monospace, Consolas, Menlo, monospace;
  --ease: cubic-bezier(.2, 0, 0, 1); --cut: 10px; --margin: clamp(16px, 3vw + 4px, 56px); }
@media (prefers-color-scheme: dark) { :root { color-scheme: dark; ${tokens(modes.dark)}; } }
* { box-sizing: border-box; }
html { -webkit-text-size-adjust: 100%; }
body { margin: 0; background: var(--bg); color: var(--ink); font: 400 17px/1.55 var(--font-text); -webkit-font-smoothing: antialiased; }
img, svg { display: block; max-width: 100%; }
a { color: var(--linkBg); text-underline-offset: 3px; text-decoration-thickness: 1.5px; }
:focus-visible { outline: 2px solid var(--focus); outline-offset: 3px; }
.pp-hero :focus-visible { outline-color: var(--heroInk); }
[hidden] { display: none !important; }
.pp-skip { position: fixed; left: 16px; top: 10px; z-index: 20; padding: 10px 16px; background: #FF3D1F; color: #0B0B0D; font-weight: 600; text-decoration: none; translate: 0 -200%; }
.pp-skip:focus-visible { translate: none; }
#pp-main:focus { outline: none; }
::selection { background: var(--accent); color: var(--onAccent); }
.pp-wrap { width: min(100% - 2 * var(--margin), 1440px); margin-inline: auto; }
.pp-bar { position: sticky; top: 0; z-index: 10; background: color-mix(in srgb, var(--bg) 90%, transparent); backdrop-filter: blur(12px); border-bottom: 1px solid var(--line); }
.pp-bar__in { display: flex; align-items: center; gap: 14px; height: 68px; }
.pp-lockup { display: flex; align-items: center; gap: 10px; min-width: 0; font: 700 21px/1 var(--font-display); letter-spacing: -0.04em; color: inherit; text-decoration: none; }
.pp-lockup { padding-block: 8px; }
.pp-lockup .fork { width: 19px; height: 20px; flex: none; fill: #FF3D1F; transition: translate 350ms var(--ease); }
@media (hover: hover) { .pp-lockup:hover .fork { translate: 0 -3px; } }
.pp-x { color: var(--muted); font-weight: 300; }
.pp-barlogo { height: 22px; width: auto; max-width: 160px; object-fit: contain; object-position: left center; }
.name { font-weight: 600; }
.pp-sp { flex: 1; }
.pp-switch { display: inline-flex; border: 1px solid var(--line); flex: none; }
.pp-switch button { font: 600 13px/1 var(--font-text); border: 0; background: none; color: var(--muted); height: 40px; min-width: 48px; padding: 0 12px; cursor: pointer; transition: color 200ms var(--ease), background-color 200ms var(--ease); }
.pp-switch button + button { border-left: 1px solid var(--line); }
.pp-switch button[aria-pressed=true] { background: var(--ink); color: var(--bg); }
@media (hover: hover) { .pp-switch button:hover { color: var(--ink); box-shadow: inset 0 -3px 0 #FF3D1F; } }
.pp-hero { position: relative; overflow: hidden; background: var(--hero); color: var(--heroInk); border-bottom: 1px solid var(--line); }
.pp-hero::before { content: ""; position: absolute; inset: 0; background: var(--scrim); pointer-events: none; }
.pp-hero__in { position: relative; display: grid; grid-template-columns: minmax(0, 5fr) minmax(0, 7fr); gap: 56px; align-items: center; padding-block: 72px; min-height: min(720px, calc(100svh - 68px)); }
.pp-text { display: grid; gap: 22px; align-content: center; }
.pp-herologo { height: 44px; width: auto; max-width: 320px; object-fit: contain; object-position: left center; }
.pp-hero .name { font-size: 28px; }
h1 { margin: 0; font: 600 clamp(40px, 3.6vw + 16px, 76px)/0.98 var(--font-display); letter-spacing: -0.045em; margin-left: -0.03em; text-wrap: balance; }
.pp-lead { margin: 0; font-size: 19px; line-height: 1.45; color: var(--heroSoft); max-width: 32em; text-wrap: pretty; }
.pp-open, .pp-dl .pp-opt { display: grid; gap: 8px; justify-items: start; }
.pp-dl { display: flex; flex-wrap: wrap; gap: 18px 22px; }
/* The fork's 45° cut on the strong button; it opens on hover. */
.pp-btn { --c: var(--cut); position: relative; display: inline-flex; align-items: center; justify-content: center; gap: 10px; min-height: 54px; padding: 0 24px; font: 600 16px/1 var(--font-text);
  text-decoration: none; border: 0; cursor: pointer; white-space: nowrap; background: transparent; color: var(--heroInk); box-shadow: inset 0 0 0 1.5px currentColor;
  transition: clip-path 400ms var(--ease), translate 150ms var(--ease), background-color 200ms var(--ease), color 200ms var(--ease); }
.pp-btn svg { width: 18px; height: 18px; flex: none; }
.pp-btn--primary { background: var(--solid); color: var(--onSolid); box-shadow: none; clip-path: polygon(0 0, calc(100% - var(--c)) 0, 100% var(--c), 100% 100%, 0 100%); }
@media (hover: hover) { .pp-btn--primary:hover { --c: calc(var(--cut) * 2); } .pp-btn:not(.pp-btn--primary):hover { background: var(--ghost); } }
.pp-btn:focus-visible { --c: calc(var(--cut) * 2); }
.pp-btn:active { translate: 0 1px; }
.pp-btn--small { min-height: 44px; padding: 0 18px; font-size: 15px; }
.pp-note { font-size: 14px; line-height: 1.45; color: var(--heroSoft); max-width: 34em; }
.pp-hero a:not(.pp-btn) { color: var(--heroInk); display: inline-block; padding-block: 4px; transition: text-decoration-thickness 200ms var(--ease); }
@media (hover: hover) { .pp-hero a:not(.pp-btn):hover { text-decoration-thickness: 3px; } }
.pp-after { display: grid; grid-template-columns: 22px minmax(0, 1fr); gap: 12px; padding: 16px 18px; background: var(--ghost); color: var(--heroInk); max-width: 34em; box-shadow: inset 3px 0 0 var(--heroInk); }
.pp-after svg { width: 22px; height: 22px; fill: none; stroke: currentColor; stroke-width: 2; stroke-linecap: square; }
.pp-after b { display: block; font-weight: 600; }
.pp-after p { margin: 2px 0 0; font-size: 15px; line-height: 1.5; }
.pp-phone { display: none; gap: 12px; justify-items: start; }
.pp-phone h2 { margin: 0; font: 600 22px/1.2 var(--font-display); letter-spacing: -0.02em; }
.pp-phone p { margin: 0; color: var(--heroSoft); }
body.is-phone .pp-phone { display: grid; }
body.is-phone .pp-dl, body.is-phone .pp-open, body.is-phone .pp-after { display: none !important; }
.pp-art { position: relative; min-width: 0; }
.pp-art .v2w { width: 100%; aspect-ratio: 16 / 11; box-shadow: 0 1px 2px rgba(0,0,0,.08), 0 50px 90px -40px rgba(0,0,0,.55); }
.pp-art .v2-tab img { width: 16px; height: 16px; }
.pp-win--dark { display: none !important; }
@media (prefers-color-scheme: dark) { .pp-win--light { display: none !important; } .pp-win--dark { display: grid !important; } }
.pp-letter { width: 16px; height: 16px; border-radius: 4px; display: inline-grid; place-items: center; font-size: 10px; font-weight: 700; }
.v2-plan__head .pp-letter { width: 30px; height: 30px; border-radius: 8px; font-size: 15px; }
.pp-scene-name { font: 600 24px/1.1 var(--font-display); }
.v2-panel-dark .v2-pill--off { color: #ff7a62; background: rgba(255, 90, 61, .16); }
.pp-steps { padding: 104px 0 0; }
.pp-steps h2 { margin: 0 0 40px; font: 600 clamp(30px, 2vw + 18px, 48px)/1.05 var(--font-display); letter-spacing: -0.035em; }
.pp-steps ol { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 32px; }
.pp-steps li { display: grid; gap: 12px; align-content: start; padding-top: 20px; border-top: 2px solid var(--ink); }
.pp-num { font: 200 56px/0.85 var(--font-display); letter-spacing: -0.05em; color: var(--link); font-variant-numeric: tabular-nums; }
.pp-steps h3 { margin: 0; font: 600 21px/1.25 var(--font-display); letter-spacing: -0.015em; }
.pp-steps p { margin: 0; color: var(--soft); font-size: 16px; line-height: 1.55; }
.pp-steps p b { color: var(--ink); font-weight: 600; }
.pp-steps li a { color: var(--link); }
.pp-steps .pp-btn--quiet { background: var(--accent); color: var(--onAccent); box-shadow: none; justify-self: start; margin-top: 6px;
  clip-path: polygon(0 0, calc(100% - var(--c)) 0, 100% var(--c), 100% 100%, 0 100%); }
.pp-cmd { font-size: 14px; color: var(--muted); }
.pp-cmd code { display: block; margin-top: 6px; padding: 11px 13px; background: var(--mutedBg); color: var(--ink); border: 1px solid var(--line);
  font: 13px/1.45 var(--font-mono); overflow-wrap: anywhere; user-select: all; }
body.os-win [data-os=mac], body.os-mac [data-os=win] { display: none; }
.pp-osl { display: none; }
body.os-any .pp-osl, body.os-phone .pp-osl { display: inline; }
.pp-what { margin: 88px 0 0; padding: 24px 0 0; border-top: 1px solid var(--line); display: flex; flex-wrap: wrap; gap: 10px 24px; align-items: baseline; justify-content: space-between; }
.pp-what p { margin: 0; color: var(--soft); max-width: 62ch; }
.pp-what a { color: var(--ink); font-weight: 600; display: inline-block; padding-block: 6px; }
@media (hover: hover) { .pp-what a:hover { text-decoration-thickness: 3px; text-decoration-color: #FF3D1F; } }
.pp-links { display: inline-flex; gap: 22px; align-items: baseline; }
.pp-report-open { border: 0; background: none; padding: 6px 0; font: inherit; color: var(--muted); text-decoration: underline; text-underline-offset: 3px; cursor: pointer; transition: color 200ms var(--ease); }
@media (hover: hover) { .pp-report-open:hover { color: var(--ink); text-decoration-thickness: 2px; } }
.pp-report { margin: 16px 0 0; display: grid; gap: 8px; max-width: 34em; color: var(--soft); }
.pp-report textarea { font: inherit; color: var(--ink); background: var(--card); border: 1px solid var(--line); padding: 10px 12px; resize: vertical; }
.pp-report__row { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; }
.pp-report .pp-btn--quiet { background: var(--accent); color: var(--onAccent); box-shadow: none; }
.pp-report__state { font-size: 14px; color: var(--muted); }
footer { padding: 32px 0 48px; color: var(--muted); font-size: 14px; }
footer a { color: var(--muted); display: inline-block; padding-block: 4px; }
@media (hover: hover) { footer a:hover { color: var(--ink); text-decoration-thickness: 2px; } }
@media (max-width: 1023px) {
  .pp-hero__in { grid-template-columns: minmax(0, 1fr); padding-block: 48px; min-height: 0; }
  .pp-steps ol { grid-template-columns: minmax(0, 1fr); gap: 28px; }
}
@media (max-width: 639px) {
  .pp-bar__in { height: 58px; }
  .pp-lockup { font-size: 18px; }
  .pp-hero__in { padding-block: 32px 40px; gap: 32px; }
  .pp-herologo { height: 34px; max-width: 100%; }
  .pp-lead { font-size: 17px; }
  .pp-dl, .pp-dl .pp-opt, .pp-open { width: 100%; }
  .pp-btn { width: 100%; }
  .pp-btn--small { width: auto; }
  .pp-steps { padding-top: 64px; }
  .pp-art .v2w { aspect-ratio: 4 / 5; }
}
@media (prefers-reduced-motion: reduce) { .pp-btn { transition: none; } .pp-btn:active { translate: none; } }
</style>
</head>
<body class="os-any">
<a class="pp-skip" href="#pp-main">К содержанию</a>
<header class="pp-bar">
  <div class="pp-wrap pp-bar__in">
    <a class="pp-lockup" href="${esc(home)}"><svg class="fork" viewBox="0 0 104 112" aria-hidden="true"><path d="${FORK_PATH}"/></svg><span>v2pn</span><span class="pp-x" aria-hidden="true">×</span>${pic('pp-barlogo', barLogo, esc(name))}</a>
    <div class="pp-sp"></div>
    <div class="pp-switch" role="group" aria-label="Язык">
      <button type="button" data-lang="ru" aria-pressed="true">RU</button><button type="button" data-lang="en" aria-pressed="false">EN</button>
    </div>
  </div>
</header>
<main id="pp-main" tabindex="-1">
  <section class="pp-hero" aria-labelledby="pp-h1">
    <div class="pp-wrap pp-hero__in">
      <div class="pp-text">
        ${pic('pp-herologo', heroLogo, esc(name))}
        <h1 id="pp-h1" data-t="h1">${ru.h1}</h1>
        <p class="pp-lead" data-t="lead">${ru.lead}</p>
        <div class="pp-open" id="open-block" hidden>
          <a class="pp-btn pp-btn--primary" id="open" href="#"><svg viewBox="0 0 104 112" aria-hidden="true"><path fill="currentColor" d="${FORK_PATH}"/></svg><span data-t="open">${ru.open}</span></a>
          <span class="pp-note" data-t="openNote">${ru.openNote}</span>
        </div>
        <div class="pp-dl">
          <div class="pp-opt" data-dl="win">
            <a class="pp-btn pp-btn--primary" id="win" href="/r/windows-x64" data-dl-link><svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M3 5.5 10.4 4.5v7H3zm8.4-1.1L21 3v8.5h-9.6zM3 12.5h7.4v7L3 18.5zm8.4 0H21V21l-9.6-1.4z"/></svg><span data-t="win">${ru.win}</span></a>
            <span class="pp-note" id="win-note" data-t="winNote">${ru.winNote}</span>
          </div>
          <div class="pp-opt" data-dl="mac">
            <a class="pp-btn" id="mac" href="/r/macos-arm64" data-dl-link><svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M16.4 12.6c0-2.4 2-3.6 2.1-3.7a4.6 4.6 0 0 0-3.6-1.9c-1.5-.2-3 .9-3.7.9-.8 0-2-.9-3.2-.9a4.8 4.8 0 0 0-4 2.5c-1.7 3-.4 7.4 1.2 9.8.8 1.2 1.8 2.5 3 2.4 1.2 0 1.7-.8 3.1-.8s1.9.8 3.1.8c1.3 0 2.1-1.2 2.9-2.4a10 10 0 0 0 1.3-2.7 4.2 4.2 0 0 1-2.2-4zM14 5.5A4.3 4.3 0 0 0 15 2.4a4.4 4.4 0 0 0-2.9 1.5 4.1 4.1 0 0 0-1 3 3.6 3.6 0 0 0 2.9-1.4z"/></svg><span data-t="mac">${ru.mac}</span></a>
            <span class="pp-note" data-t="macNote">${ru.macNote}</span>
          </div>
        </div>
        <div class="pp-after" id="after" role="status" hidden>
          <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 4v11M7 10l5 5 5-5M5 20h14"/></svg>
          <div><b data-t="afterTitle">${ru.afterTitle}</b><p id="after-text" data-t="afterText">${ru.afterText}</p></div>
        </div>
        <div class="pp-phone">
          <h2 data-t="phoneTitle">${ru.phoneTitle}</h2>
          <p data-t="phoneText">${ru.phoneText}</p>
          <button type="button" class="pp-btn pp-btn--primary" id="share" data-t="share">${ru.share}</button>
          <span class="pp-note" data-t="phoneFiles">${ru.phoneFiles}</span>
        </div>
      </div>
      <div class="pp-art" role="img" aria-label="${ru.art}" data-t-label="art">${winLight}${winDark}</div>
    </div>
  </section>
  <div class="pp-wrap">
  <section class="pp-steps" aria-labelledby="pp-steps-h">
    <h2 id="pp-steps-h" data-t="stepsTitle">${ru.stepsTitle}</h2>
    <ol>
      <li><span class="pp-num" aria-hidden="true">1</span><h3 data-t="s1">${ru.s1}</h3><p data-os="win"><b class="pp-osl">Windows. </b><span data-t="s1win">${ru.s1win}</span></p><p class="pp-cmd" data-os="win"><span data-t="cmdWin">${ru.cmdWin}</span><code>${esc(commands.win)}</code></p><p data-os="mac"><b class="pp-osl">macOS. </b><span data-t="s1mac">${ru.s1mac}</span></p><p class="pp-cmd" data-os="mac"><span data-t="cmdMac">${ru.cmdMac}</span><code>${esc(commands.mac)}</code></p></li>
      <li><span class="pp-num" aria-hidden="true">2</span><h3 data-t="s2">${ru.s2}</h3><p data-t="s2t">${ru.s2t}</p></li>
      <li><span class="pp-num" aria-hidden="true">3</span><h3 data-t="s3">${ru.s3}</h3><p data-t="s3t">${ru.s3t}</p>${botHtml}</li>
    </ol>
  </section>
  <div class="pp-what"><p data-t="what">${ru.what}</p><span class="pp-links"><a href="${esc(home)}" data-t="whatLink">${ru.whatLink}</a><button type="button" class="pp-report-open" id="report-open" data-t="report">${ru.report}</button></span></div>
  <form class="pp-report" id="report" hidden>
    <label for="report-text" data-t="reportText">${ru.reportText}</label>
    <textarea id="report-text" maxlength="1000" rows="3"></textarea>
    <div class="pp-report__row"><button type="submit" class="pp-btn pp-btn--small pp-btn--quiet" id="report-send" data-t="reportSend">${ru.reportSend}</button><span class="pp-report__state" id="report-state" role="status"></span></div>
  </form>
  </div>
</main>
<footer class="pp-wrap" data-t="foot">${ru.foot}</footer>
<script type="application/json" id="page-data">${data}</script>
<script src="page.js"></script>
</body>
</html>
`;
  const out = [
    { name: 'index.html', content: Buffer.from(html, 'utf8') },
    { name: 'page.js', content: Buffer.from(SCRIPT, 'utf8') },
  ];
  if (pictures.logo) out.push({ name: 'logo-light.png', content: pictures.logo });
  if (pictures.logoOnLight) out.push({ name: 'logo-dark.png', content: pictures.logoOnLight });
  if (pictures.mark) out.push({ name: 'mark.png', content: pictures.mark });
  if (theme.background.type === 'image' && pictures.backdrop) out.push({ name: 'background.png', content: pictures.backdrop });
  return out;
}

/**
 * The page of a partner a moderator took down: it says so and offers nothing, in V2pn's own colours (the partner's
 * may be what got it taken down). No script.
 */
export function buildDownPage({ name }) {
  const n = esc(name);
  const html = `<!doctype html>
<html lang="ru">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<title>${n} снят модератором V2pn</title>
<meta name="robots" content="noindex">
<link rel="icon" href="/favicon.svg" type="image/svg+xml">
<style>
@font-face { font-family: "Inter Tight"; src: url(/assets/fonts/inter-tight-cyrillic.woff2) format("woff2"); font-weight: 200 800; font-display: swap; }
@font-face { font-family: Inter; src: url(/assets/fonts/inter-cyrillic.woff2) format("woff2"); font-weight: 400 600; font-display: swap; unicode-range: U+0400-045F; }
@font-face { font-family: Inter; src: url(/assets/fonts/inter-latin.woff2) format("woff2"); font-weight: 400 600; font-display: swap; unicode-range: U+0000-00FF; }
:root { color-scheme: light dark; --bg: ${LIGHT.bg}; --card: ${LIGHT.card}; --ink: ${LIGHT.ink}; --soft: ${LIGHT.soft}; --line: ${LIGHT.line}; }
@media (prefers-color-scheme: dark) { :root { --bg: ${DARK.bg}; --card: ${DARK.card}; --ink: ${DARK.ink}; --soft: ${DARK.soft}; --line: ${DARK.line}; } }
body { margin: 0; min-height: 100svh; display: grid; place-items: center; background: var(--bg); color: var(--ink);
  font: 400 16px/1.55 Inter, "Segoe UI", system-ui, -apple-system, sans-serif; }
main { width: min(100% - 32px, 34em); padding: 32px; background: var(--card); box-shadow: 0 0 0 1px var(--line), inset 3px 0 0 #FF3D1F; }
h1 { margin: 0 0 12px; font: 600 28px/1.1 "Inter Tight", Inter, system-ui, sans-serif; letter-spacing: -0.03em; }
p { margin: 0 0 12px; color: var(--soft); }
a { color: var(--ink); }
</style>
</head>
<body>
<main>
<h1>${n} снят модератором V2pn</h1>
<p>По этой ссылке ничего не будет добавлено. Если вы пользуетесь ${n}, обратитесь к провайдеру.</p>
<p lang="en">${n} was taken down by a V2pn moderator. Nothing will be added from this link.</p>
<p><a href="/">V2pn</a></p>
</main>
</body>
</html>
`;
  return [{ name: 'index.html', content: Buffer.from(html, 'utf8') }];
}
