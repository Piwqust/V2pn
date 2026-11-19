// The route presets and the sites the fork on the home page sends up its stem. Client-safe (no node imports):
// the fork's script and the pages both read it.

export type PresetId = 'ru' | 'blocked' | 'all';
export type Kind = 'ru' | 'blocked' | 'world' | 'local';

// The three presets of «Что идёт через VPN» (v2rayN.Desktop/Common/L.cs: RouteRussiaDirect, RouteBlockedOnly,
// RouteAll). The hints are the app's own words without its dashes.
export const presets: { id: PresetId; name: string; hint: string; recommended?: boolean }[] = [
  {
    id: 'ru',
    name: 'Российские сайты напрямую',
    hint: 'Банки, Госуслуги и сайты в .ru открываются напрямую. Остальное, и заблокированные .ru тоже, идёт через VPN.',
    recommended: true,
  },
  {
    id: 'blocked',
    name: 'Только заблокированное',
    hint: 'Через VPN идут только заблокированные сайты, YouTube, Discord, Telegram и ИИ. Остальное открывается напрямую.',
  },
  {
    id: 'all',
    name: 'Всё через VPN',
    hint: 'Весь трафик идёт через сервер, напрямую только локальная сеть. Некоторые банки и Госуслуги не пускают с иностранного IP.',
  },
];

// ru: Russian sites; blocked: blocked in Russia or named by the preset; world: foreign, not blocked; local: home network.
export const traffic: { name: string; kind: Kind }[] = [
  { name: 'gosuslugi.ru', kind: 'ru' }, { name: 'youtube.com', kind: 'blocked' }, { name: 'github.com', kind: 'world' },
  { name: 'sberbank.ru', kind: 'ru' }, { name: 'discord.com', kind: 'blocked' }, { name: 'ozon.ru', kind: 'ru' },
  { name: 'wikipedia.org', kind: 'world' }, { name: 'chatgpt.com', kind: 'blocked' }, { name: 'yandex.ru', kind: 'ru' },
  { name: '192.168.1.1', kind: 'local' }, { name: 'instagram.com', kind: 'blocked' }, { name: 'tbank.ru', kind: 'ru' },
  { name: 'stackoverflow.com', kind: 'world' }, { name: 'telegram.org', kind: 'blocked' }, { name: 'avito.ru', kind: 'ru' },
  { name: 'x.com', kind: 'blocked' }, { name: 'mos.ru', kind: 'ru' }, { name: 'python.org', kind: 'world' },
  { name: 'linkedin.com', kind: 'blocked' }, { name: 'hh.ru', kind: 'ru' }, { name: 'claude.ai', kind: 'blocked' },
  { name: 'kinopoisk.ru', kind: 'ru' },
];

/** true: the site goes direct (the left road) under this preset. */
export function direct(kind: Kind, preset: PresetId): boolean {
  if (kind === 'local') return true;
  if (preset === 'ru') return kind === 'ru';
  if (preset === 'blocked') return kind !== 'blocked';
  return false;
}
