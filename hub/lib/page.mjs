// A provider's public page, /p/<id>/: a Node port of tools/partner-page.py (its colours, checks and layout), changed
// for the hub's way of coming from a provider: the page offers "Открыть в V2pn" (v2pn://add?url=…&partner=<id>)
// when its address carries #sub=<subscription>, and the downloads go through /r/<name> (plain V2pn, no marker).
// The site stage restyles this template; keep it in this file.
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
 * partner-page.py computes them. {modes, checks: [{what, ratio}], failures}.
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
    lead: `V2pn — простой VPN-клиент для Windows и macOS. Откройте в нём подписку ${n}, и появится вкладка ${n}: ваш тариф, серверы и цвета ${n}. `
      + (login ? `Подписка ${n} входит в него по коду из бота, без ссылок.` : `Подписка ${n} подключается в один клик.`),
    open: 'Открыть в V2pn',
    openNote: 'Если V2pn ещё нет, скачайте его ниже, установите и нажмите «Открыть в V2pn» ещё раз.',
    win: 'Скачать для Windows',
    mac: 'Скачать для macOS',
    winNote: 'Windows 10 и 11 · <a href="/r/windows-arm64" data-os-link="win-alt">для ARM</a>',
    winNoteArm: 'Windows на ARM · <a href="/r/windows-x64" data-os-link="win-alt">для x64</a>',
    macNote: 'macOS 13.6 и новее, Apple silicon · <a href="/r/macos-x64">для Intel</a>',
    phoneTitle: 'V2pn — для компьютера',
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
      : `Нажмите «Подключиться». Серверы ${n} — во вкладке ${n} вверху окна.`,
    bot: 'Открыть бота',
    foot: 'V2pn — открытый код, GPL-3.0. <a href="https://github.com/Piwqust/V2pn">V2pn на GitHub</a>',
    lang: 'Язык',
  };
  const en = {
    title: `V2pn for ${n}`,
    h1: `${n} on your computer`,
    lead: `V2pn is a simple VPN client for Windows and macOS. Open your ${n} subscription in it and a ${n} tab appears: your plan, servers and ${n}'s colours. `
      + (login ? `Your ${n} subscription gets into it by a code from the bot, no links to copy.` : `Your ${n} subscription connects in one click.`),
    open: 'Open in V2pn',
    openNote: 'No V2pn yet? Download it below, install it and press “Open in V2pn” again.',
    win: 'Download for Windows',
    mac: 'Download for macOS',
    winNote: 'Windows 10 and 11 · <a href="/r/windows-arm64" data-os-link="win-alt">for ARM</a>',
    winNoteArm: 'Windows on ARM · <a href="/r/windows-x64" data-os-link="win-alt">for x64</a>',
    macNote: 'macOS 13.6 or later, Apple silicon · <a href="/r/macos-x64">for Intel</a>',
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
    foot: 'V2pn is open source, GPL-3.0. <a href="https://github.com/Piwqust/V2pn">V2pn on GitHub</a>',
    lang: 'Language',
  };
  return { ru, en };
}

/**
 * The files of /p/<id>/: index.html, page.js and the pictures it shows. <pictures> maps logo / logoOnLight /
 * backdrop to their PNG bytes (only those the pack carries). Throws when a pair of text and ground would not read.
 */
export function buildPage({ id, name, look, theme, pictures, origin, home = '/' }) {
  const { modes, failures } = pageTheme(theme);
  if (failures.length) throw new Error(`text would not read: ${failures.map(f => `${f.what} ${f.ratio.toFixed(2)}`).join('; ')}`);
  const logo = pictures.logo ? 'logo-light.png' : null;
  const logoLight = pictures.logoOnLight ? 'logo-dark.png' : null;
  const login = look.login === true && Boolean(look.api);
  const support = look.links?.support || '';
  const bot = /^(https:\/\/t\.me\/|tg:)/.test(support) ? support : null;
  const cabinet = /^https:\/\//.test(look.links?.cabinet || '') ? look.links.cabinet : '';
  const { ru, en } = strings(name, login, cabinet);

  const logoFor = lightInk => (lightInk ? (logo || logoLight) : (logoLight || logo));
  const heroLogo = { light: logoFor(modes.light.heroLight), dark: logoFor(modes.dark.heroLight) };
  const barLogo = { light: logoLight || logo, dark: logo || logoLight };
  const pic = (cls, files, alt) => {
    if (!files.light && !files.dark) return `<span class="${cls} name">${esc(name)}</span>`;
    if (files.light === files.dark) return `<img class="${cls}" src="${files.light}" alt="${alt}">`;
    return `<picture><source srcset="${files.dark}" media="(prefers-color-scheme: dark)"><img class="${cls}" src="${files.light}" alt="${alt}"></picture>`;
  };
  const keys = ['bg', 'card', 'mutedBg', 'line', 'ink', 'soft', 'muted', 'accent', 'onAccent', 'link', 'linkBg', 'focus', 'hero', 'scrim', 'heroInk', 'heroSoft', 'ghost', 'solid', 'onSolid'];
  const tokens = v => keys.map(k => `--${k}: ${v[k]}`).join('; ');
  const commands = { win: `irm ${origin}/r/install.ps1 | iex`, mac: `curl -fsSL ${origin}/r/install.sh | bash` };
  const botHtml = bot ? `<a class="btn quiet small" href="${esc(bot)}" data-t="bot">${ru.bot}</a>` : '';
  const data = JSON.stringify({ id, name, text: { ru, en } }).replace(/</g, '\\u003c');

  const html = `<!doctype html>
<html lang="ru">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${ru.title}</title>
<meta name="description" content="V2pn — простой VPN-клиент для Windows и macOS.">
<style>
@font-face { font-family: Geist; src: url(/assets/fonts/Geist-Regular.woff2) format("woff2"); font-weight: 400; font-display: swap; }
@font-face { font-family: Geist; src: url(/assets/fonts/Geist-SemiBold.woff2) format("woff2"); font-weight: 600; font-display: swap; }
:root { color-scheme: light dark; ${tokens(modes.light)}; }
@media (prefers-color-scheme: dark) { :root { ${tokens(modes.dark)}; } }
* { box-sizing: border-box; }
html { -webkit-text-size-adjust: 100%; }
body { margin: 0; background: var(--bg); color: var(--ink); font: 16px/1.55 Geist, "Segoe UI", system-ui, -apple-system, sans-serif;
  -webkit-font-smoothing: antialiased; }
a { color: var(--linkBg); text-underline-offset: 3px; }
:focus-visible { outline: 2px solid var(--focus); outline-offset: 2px; border-radius: 6px; }
[hidden] { display: none !important; }
.wrap { max-width: 1040px; margin: 0 auto; padding: 0 24px; }
.bar { display: flex; align-items: center; gap: 10px; height: 64px; }
.lockup { display: flex; align-items: center; gap: 10px; min-width: 0; font-weight: 600; color: inherit; text-decoration: none; }
.v2 { width: 17px; height: 18px; flex: none; }
.x { color: var(--muted); font-weight: 400; }
.barlogo { height: 20px; width: auto; display: block; max-width: 160px; object-fit: contain; object-position: left center; }
.name { font-weight: 600; }
.sp { flex: 1; }
.switch { display: inline-flex; padding: 3px; border-radius: 10px; background: var(--mutedBg); flex: none; }
.switch button { font: inherit; font-size: 13px; font-weight: 600; border: 0; background: none; color: var(--muted); height: 30px; min-width: 40px;
  padding: 0 10px; border-radius: 8px; cursor: pointer; }
.switch button[aria-pressed=true] { background: var(--card); color: var(--ink); box-shadow: 0 1px 2px rgba(0,0,0,.12); }
.hero { position: relative; overflow: hidden; border-radius: 24px; background: var(--hero); color: var(--heroInk); margin-top: 8px;
  box-shadow: 0 1px 2px rgba(0,0,0,.06), 0 18px 40px -22px rgba(0,0,0,.35); }
.hero::before { content: ""; position: absolute; inset: 0; background: var(--scrim); pointer-events: none; }
.hero-in { position: relative; padding: 56px 56px 48px; display: grid; gap: 22px; max-width: 720px; }
.herologo { height: 44px; width: auto; max-width: 320px; display: block; object-fit: contain; object-position: left center; }
.hero .name { font-size: 28px; }
h1 { margin: 0; font-size: 46px; line-height: 1.08; letter-spacing: -0.03em; font-weight: 600; text-wrap: balance; }
.lead { margin: 0; font-size: 18.5px; color: var(--heroSoft); max-width: 36em; text-wrap: pretty; }
.open { display: grid; gap: 8px; justify-items: start; }
.dl { display: flex; flex-wrap: wrap; gap: 14px 18px; margin-top: 6px; }
.dl .opt { display: grid; gap: 8px; justify-items: start; }
.btn { display: inline-flex; align-items: center; justify-content: center; gap: 10px; height: 52px; padding: 0 24px; border-radius: 999px;
  font: inherit; font-weight: 600; font-size: 16px; text-decoration: none; border: 0; cursor: pointer; white-space: nowrap;
  transition: transform 140ms cubic-bezier(.23,1,.32,1), filter 150ms ease; }
.btn:active { transform: scale(.97); }
.btn:hover { filter: brightness(1.06); }
.btn svg { width: 18px; height: 18px; flex: none; }
.hero .btn { background: var(--ghost); color: var(--heroInk); }
.hero .btn.primary { background: var(--solid); color: var(--onSolid); box-shadow: 0 1px 2px rgba(0,0,0,.18), 0 10px 22px -12px rgba(0,0,0,.5); }
.btn.small { height: 40px; padding: 0 16px; font-size: 14.5px; }
.note { font-size: 13.5px; color: var(--heroSoft); padding-left: 4px; }
.hero a:not(.btn) { color: var(--heroInk); }
.phone { display: none; gap: 12px; justify-items: start; }
.phone h2 { margin: 0; font-size: 20px; letter-spacing: -0.01em; }
.phone p { margin: 0; color: var(--heroSoft); }
body.is-phone .phone { display: grid; }
body.is-phone .dl, body.is-phone .open { display: none; }
.steps { margin: 44px 0 0; }
.steps h2 { font-size: 24px; letter-spacing: -0.02em; margin: 0 0 16px; }
ol { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 14px; }
li { background: var(--card); border-radius: 18px; padding: 22px; display: grid; gap: 10px; align-content: start;
  box-shadow: 0 0 0 1px var(--line); }
.num { width: 32px; height: 32px; border-radius: 50%; display: grid; place-items: center; background: var(--accent); color: var(--onAccent);
  font-weight: 600; font-size: 15px; font-variant-numeric: tabular-nums; }
li h3 { margin: 0; font-size: 17px; font-weight: 600; }
li p { margin: 0; color: var(--soft); font-size: 15px; }
li p b { color: var(--ink); font-weight: 600; }
li a { color: var(--link); }
li .btn.quiet { background: var(--accent); color: var(--onAccent); justify-self: start; margin-top: 4px; }
li .cmd { font-size: 13.5px; color: var(--muted); }
li code { display: block; margin-top: 4px; padding: 8px 10px; border-radius: 10px; background: var(--mutedBg); color: var(--ink);
  font: 12.5px/1.45 ui-monospace, "Cascadia Mono", Consolas, Menlo, monospace; overflow-wrap: anywhere; user-select: all; }
body.os-win [data-os=mac], body.os-mac [data-os=win] { display: none; }
.osl { display: none; }
body.os-any .osl, body.os-phone .osl { display: inline; }
footer { margin: 48px 0 40px; color: var(--muted); font-size: 13.5px; }
footer a { color: var(--muted); }
@media (max-width: 860px) {
  ol { grid-template-columns: 1fr; }
  .hero-in { padding: 40px 32px 36px; }
  h1 { font-size: 36px; }
}
@media (max-width: 560px) {
  .wrap { padding: 0 16px; }
  .bar { height: 56px; }
  .hero { border-radius: 20px; }
  .hero-in { padding: 28px 20px 26px; gap: 18px; }
  .herologo { height: 34px; max-width: 100%; }
  h1 { font-size: 30px; }
  .lead { font-size: 16.5px; }
  .dl, .dl .opt { width: 100%; }
  .btn { width: 100%; }
  .btn.small { width: auto; }
  li { padding: 18px; }
}
@media (prefers-reduced-motion: reduce) { .btn { transition: none; } .btn:active { transform: none; } }
</style>
</head>
<body class="os-any">
<div class="wrap">
  <header class="bar">
    <a class="lockup" href="${esc(home)}">${V2MARK}<span>V2pn</span><span class="x">×</span>${pic('barlogo', barLogo, esc(name))}</a>
    <div class="sp"></div>
    <div class="switch" role="group" aria-label="Язык">
      <button type="button" data-lang="ru" aria-pressed="true">RU</button><button type="button" data-lang="en" aria-pressed="false">EN</button>
    </div>
  </header>
  <main>
    <section class="hero">
      <div class="hero-in">
        ${pic('herologo', heroLogo, esc(name))}
        <h1 data-t="h1">${ru.h1}</h1>
        <p class="lead" data-t="lead">${ru.lead}</p>
        <div class="open" id="open-block" hidden>
          <a class="btn primary" id="open" href="#"><span data-t="open">${ru.open}</span></a>
          <span class="note" data-t="openNote">${ru.openNote}</span>
        </div>
        <div class="dl">
          <div class="opt" data-dl="win">
            <a class="btn primary" id="win" href="/r/windows-x64"><svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M3 5.5 10.4 4.5v7H3zm8.4-1.1L21 3v8.5h-9.6zM3 12.5h7.4v7L3 18.5zm8.4 0H21V21l-9.6-1.4z"/></svg><span data-t="win">${ru.win}</span></a>
            <span class="note" id="win-note" data-t="winNote">${ru.winNote}</span>
          </div>
          <div class="opt" data-dl="mac">
            <a class="btn" id="mac" href="/r/macos-arm64"><svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M16.4 12.6c0-2.4 2-3.6 2.1-3.7a4.6 4.6 0 0 0-3.6-1.9c-1.5-.2-3 .9-3.7.9-.8 0-2-.9-3.2-.9a4.8 4.8 0 0 0-4 2.5c-1.7 3-.4 7.4 1.2 9.8.8 1.2 1.8 2.5 3 2.4 1.2 0 1.7-.8 3.1-.8s1.9.8 3.1.8c1.3 0 2.1-1.2 2.9-2.4a10 10 0 0 0 1.3-2.7 4.2 4.2 0 0 1-2.2-4zM14 5.5A4.3 4.3 0 0 0 15 2.4a4.4 4.4 0 0 0-2.9 1.5 4.1 4.1 0 0 0-1 3 3.6 3.6 0 0 0 2.9-1.4z"/></svg><span data-t="mac">${ru.mac}</span></a>
            <span class="note" data-t="macNote">${ru.macNote}</span>
          </div>
        </div>
        <div class="phone">
          <h2 data-t="phoneTitle">${ru.phoneTitle}</h2>
          <p data-t="phoneText">${ru.phoneText}</p>
          <button type="button" class="btn primary" id="share" data-t="share">${ru.share}</button>
          <span class="note" data-t="phoneFiles">${ru.phoneFiles}</span>
        </div>
      </div>
    </section>
    <section class="steps">
      <h2 data-t="stepsTitle">${ru.stepsTitle}</h2>
      <ol>
        <li><span class="num">1</span><h3 data-t="s1">${ru.s1}</h3><p data-os="win"><b class="osl">Windows. </b><span data-t="s1win">${ru.s1win}</span></p><p class="cmd" data-os="win"><span data-t="cmdWin">${ru.cmdWin}</span><code>${esc(commands.win)}</code></p><p data-os="mac"><b class="osl">macOS. </b><span data-t="s1mac">${ru.s1mac}</span></p><p class="cmd" data-os="mac"><span data-t="cmdMac">${ru.cmdMac}</span><code>${esc(commands.mac)}</code></p></li>
        <li><span class="num">2</span><h3 data-t="s2">${ru.s2}</h3><p data-t="s2t">${ru.s2t}</p></li>
        <li><span class="num">3</span><h3 data-t="s3">${ru.s3}</h3><p data-t="s3t">${ru.s3t}</p>${botHtml}</li>
      </ol>
    </section>
  </main>
  <footer data-t="foot">${ru.foot}</footer>
</div>
<script type="application/json" id="page-data">${data}</script>
<script src="page.js"></script>
</body>
</html>
`;
  const files = [
    { name: 'index.html', content: Buffer.from(html, 'utf8') },
    { name: 'page.js', content: Buffer.from(SCRIPT, 'utf8') },
  ];
  if (pictures.logo) files.push({ name: 'logo-light.png', content: pictures.logo });
  if (pictures.logoOnLight) files.push({ name: 'logo-dark.png', content: pictures.logoOnLight });
  if (theme.background.type === 'image' && pictures.backdrop) files.push({ name: 'background.png', content: pictures.backdrop });
  return files;
}
