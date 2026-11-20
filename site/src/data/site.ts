// Every string the marketing pages show, in one place. Facts come from the app (v2rayN.Desktop/Common/L.cs for the
// words of the window, docs/V2PN-UX.md for how it behaves) and README.ru.md.
//
// Assumptions (also in README.md):
// - The version is the newest "## x.y.z" heading of CHANGELOG.md at build time.
// - Downloads are the installer (setup.exe) for Windows and the DMG for macOS, through the hub's /r/<name> (counted
//   per day, short names in hub/lib/downloads.mjs). The portable Windows zip is /r/windows-<arch>-zip.
// - The pings on the pages (38 мс and so on) are examples from the demo subscription in the screenshots, and say so.
// - Some screenshots show the app in English, its default language (`placeholder: true` below): replace them with
//   Russian captures from tools/sandbox.ps1 with the demo subscription (tools/demo/demo_sub.py).
import fs from 'node:fs';

function latestVersion(): string {
  try {
    const log = fs.readFileSync(new URL('../../../CHANGELOG.md', import.meta.url), 'utf8');
    return /^## (\d+\.\d+\.\d+)\s*$/m.exec(log)?.[1] ?? '';
  } catch {
    return '';
  }
}

export const site = {
  name: 'V2pn',
  url: 'https://v2pn.angleford.co',
  repo: 'https://github.com/Piwqust/V2pn',
  version: latestVersion(),
  description: 'Бесплатный VPN-клиент для Windows и macOS: подписка вашего провайдера, российские сайты напрямую, остальное через сервер.',
  tagline: 'Всегда две дороги',
};

export const links = {
  releases: `${site.repo}/releases`,
  changelog: `${site.repo}/blob/v2pn/CHANGELOG.md`,
  license: `${site.repo}/blob/v2pn/LICENSE`,
  issues: `${site.repo}/issues/new/choose`,
  security: `${site.repo}/blob/v2pn/SECURITY.md`,
  upstream: 'https://github.com/2dust/v2rayN',
};

export const nav = [
  { href: '/docs/', label: 'Документация' },
  { href: '/providers/', label: 'Провайдерам' },
];

export type Download = { key: string; os: 'win' | 'mac'; href: string; title: string; detail: string; file: string };
export const downloads: Download[] = [
  { key: 'windows-x64', os: 'win', href: '/r/windows-x64', title: 'Windows', detail: 'Windows 10 и 11, x64', file: 'V2pn-windows-x64-setup.exe' },
  { key: 'windows-arm64', os: 'win', href: '/r/windows-arm64', title: 'Windows на ARM', detail: 'Windows 11 на ARM64', file: 'V2pn-windows-arm64-setup.exe' },
  { key: 'macos-arm64', os: 'mac', href: '/r/macos-arm64', title: 'macOS', detail: 'Apple silicon, M1 и новее, macOS 13.6+', file: 'V2pn-macos-arm64.dmg' },
  { key: 'macos-x64', os: 'mac', href: '/r/macos-x64', title: 'Mac с Intel', detail: 'macOS 13.6 и новее', file: 'V2pn-macos-x64.dmg' },
];

export const commands = {
  win: `irm ${site.url}/r/install.ps1 | iex`,
  mac: `curl -fsSL ${site.url}/r/install.sh | bash`,
};

export { presets, traffic } from './routes';

export const shots = {
  connected: { src: 'ru/connected', alt: 'Окно V2pn на русском: слева «Главная» с быстрым подключением и недавними серверами, на карте Европы подсвечена Германия и метка «Франкфурт 38 мс», сверху «Отключиться», режим «Прокси» и маршрут «Всё через VPN», внизу пинг, трафик за сессию и график скорости', placeholder: false },
  routingRu: { src: 'ru/routing', alt: 'То же окно с открытым меню «Что идёт через VPN»: три пресета, переключатель блокировки рекламы и свои правила', placeholder: false },
  welcome: { src: 'welcome', alt: 'Первый запуск V2pn: выбор языка и кнопка «Начать»', placeholder: true },
  import: { src: 'import', alt: 'Шаг «Ваши серверы»: вставить ссылку, добавить подписку или перенести из другой программы', placeholder: true },
  ready: { src: 'ready', alt: 'Серверы готовы: выбранный сервер, режим и маршрут, кнопка «Подключиться» на том же шаге', placeholder: true },
  routing: { src: 'routing', alt: 'Меню «Что идёт через VPN» с тремя пресетами и переключателем блокировки рекламы', placeholder: true },
  partnerScene: { src: 'configra-scene-dark', alt: 'V2pn во вкладке провайдера Configra: оранжевый фон провайдера, тариф на 77 дней, кнопки «Продлить» и «Поддержка»', placeholder: false },
  partnerAccent: { src: 'configra-accent-light', alt: 'Вкладка Configra в светлой теме: карточка тарифа в боковой панели и кнопка подключения в цвете провайдера', placeholder: false },
};

// The three decisions of the main screen (docs/V2PN-UX.md, «The main screen: three decisions»).
export const decisions = [
  {
    n: '1',
    title: 'Сервер',
    text: 'Страны с пингом в списке слева и на карте. Сверху самый быстрый сервер, избранное и недавние. Серии вроде «AUTO 1/34» собраны в одну строку.',
    focus: { x: 0.016, y: 0.2, w: 0.228, h: 0.162 },
  },
  {
    n: '2',
    title: 'Прокси или VPN',
    text: '«Прокси» включает системный прокси: через сервер идут браузеры и программы, которые его слушают. VPN ведёт через сервер весь трафик компьютера, игры тоже.',
    focus: { x: 0.476, y: 0.325, w: 0.157, h: 0.06 },
  },
  {
    n: '3',
    title: 'Что идёт через VPN',
    text: 'Три пресета вместо редактора правил. Менять можно когда угодно, с подключением и без: новое правило работает сразу.',
    focus: { x: 0.535, y: 0.383, w: 0.34, h: 0.592 },
  },
];

export const checkLog = [
  { text: 'Ядро Xray запущено', ok: true },
  { text: 'Проверка соединения, HTTP 204', ok: true, value: '38 мс' },
  { text: 'Системный прокси включён', ok: true },
];

export const faq = [
  { q: 'Сколько стоит V2pn?', a: 'Нисколько. Код открыт под GPL-3.0 на GitHub. Платите вы только провайдеру за подписку.' },
  { q: 'Где взять серверы?', a: 'У VPN-провайдера: V2pn серверы не продаёт. Подойдёт ссылка на подписку или на сервер вида vless://…' },
  { q: 'Есть ли V2pn для телефона?', a: 'Нет. V2pn работает на Windows 10 и 11 (x64 и ARM) и на macOS 13.6 и новее (Apple silicon и Intel).' },
  { q: 'Почему Windows предупреждает при запуске?', a: 'У V2pn нет платной подписи разработчика. Нажмите «Подробнее», затем «Выполнить в любом случае». Установка командой обходится без этого окна.' },
  { q: 'Как перейти с v2rayN?', a: 'При первом запуске V2pn предложит забрать серверы, подписки и настройки v2rayN. Файлы v2rayN остаются на месте, вернуться можно.' },
  { q: 'Почему окно на английском?', a: 'Английский стоит по умолчанию. Русский выбирается на первом шаге или в «Оформлении»: значок палитры в заголовке окна.' },
];
