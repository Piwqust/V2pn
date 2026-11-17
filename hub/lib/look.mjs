// A provider's look as the cabinet edits it, the checks it must pass before it can be published (the rules of
// V2pn's reader: V2pnPartnerTrust, V2pnPartnerCatalog.ParseAction, V2pnPartnerSlots), and partner.json built from it
// as docs/partners/partner.schema.json describes it.
import { colors as C } from './colors.mjs';
import { HubError } from './errors.mjs';
import { COLOUR, domainAllowed, httpsUrl, oneLine, validName } from './validate.mjs';

export const ICONS = ['bolt', 'shield', 'play', 'game', 'card', 'chat', 'globe', 'star', 'refresh', 'check', 'info', 'gift'];
export const DOS = ['connect.best', 'connect.recommend', 'connect.server', 'connect.group', 'open.cabinet', 'open.renew', 'open.support',
  'open.link', 'subscription.update', 'check.services', 'info.card'];
export const PURPOSES = ['youtube', 'games', 'mobile', 'whitelist', 'streaming', 'ai', 'torrents'];
export const STRATEGIES = ['ping', 'stable', 'load'];
const NEEDS = { 'connect.best': 'strategy', 'connect.recommend': 'purpose', 'connect.server': 'server', 'connect.group': 'group', 'open.link': 'url' };
const NEEDS_API = new Set(['connect.recommend', 'info.card']);
const NEED_MAX = { strategy: 16, purpose: 16, server: 253, group: 24, url: 2048 };

// The names the pictures get in the pack, as in partners/configra: the light logo is the one for a dark ground.
export const FILE = { logo: 'logo-light.png', logoOnLight: 'logo-dark.png', mark: 'mark.png', backdrop: 'background.png' };
export const SLOT_OF = { logo: 'logo', logoOnLight: 'logo', mark: 'mark', backdrop: 'backdrop' };
export const PICTURES = Object.keys(FILE);

const SHA = /^[0-9a-f]{64}$/;

/** A look with nothing but the name: what an approved provider has before it designs its own. */
export function defaultLook(name) {
  return { name, brand: [], background: 'none', logo: null, logoOnLight: null, mark: null, backdrop: null,
    links: { cabinet: '', support: '' }, api: '', login: false, actions: [], lock: false };
}

const bad = (fields, key, message) => { fields[key] = message; };

/**
 * The look of a request body, with every value of the right type and within its limits; throws "invalid" otherwise.
 * Whether it can be published is lookProblems' question.
 */
export function parseLook(input) {
  if (!input || typeof input !== 'object' || Array.isArray(input)) throw new HubError('bad_request');
  const fields = {};
  const name = oneLine(input.name ?? '', 40);
  if (name == null) bad(fields, 'name', 'До 40 символов в одну строку.');
  const brand = [];
  if (input.brand != null && !Array.isArray(input.brand)) bad(fields, 'brand', 'Список цветов.');
  for (const h of Array.isArray(input.brand) ? input.brand.slice(0, 8) : []) {
    if (typeof h !== 'string' || !COLOUR.test(h)) { bad(fields, 'brand', 'Цвета в виде #RRGGBB.'); break; }
    const hex = h.toUpperCase();
    if (!brand.includes(hex)) brand.push(hex);
  }
  if (brand.length > 3) bad(fields, 'brand', 'Не больше трёх цветов.');
  const background = input.background ?? 'none';
  if (!['none', 'gradient', 'image'].includes(background)) bad(fields, 'background', 'Фон: нет, градиент или картинка.');
  const pics = {};
  for (const key of PICTURES) {
    const v = input[key] ?? null;
    if (v !== null && (typeof v !== 'string' || !SHA.test(v))) bad(fields, key, 'Загрузите картинку заново.');
    pics[key] = v;
  }
  const links = { cabinet: '', support: '' };
  for (const key of ['cabinet', 'support']) {
    const v = oneLine(input.links?.[key] ?? '', 2048);
    if (v == null) bad(fields, `links.${key}`, 'Ссылка до 2048 символов.');
    else links[key] = v;
  }
  const api = oneLine(input.api ?? '', 512);
  if (api == null) bad(fields, 'api', 'Адрес до 512 символов.');
  if (input.login != null && typeof input.login !== 'boolean') bad(fields, 'login', 'Да или нет.');
  if (input.lock != null && typeof input.lock !== 'boolean') bad(fields, 'lock', 'Да или нет.');
  const actions = [];
  if (input.actions != null && !Array.isArray(input.actions)) bad(fields, 'actions', 'Список кнопок.');
  const rawActions = Array.isArray(input.actions) ? input.actions : [];
  if (rawActions.length > 4) bad(fields, 'actions', 'Не больше четырёх кнопок.');
  rawActions.slice(0, 4).forEach((a, i) => {
    if (!a || typeof a !== 'object') { bad(fields, `actions.${i}`, 'Кнопка не заполнена.'); return; }
    const action = { label: oneLine(a.label ?? '', 64), icon: oneLine(a.icon ?? '', 16), do: oneLine(a.do ?? '', 32) };
    for (const [k, max] of Object.entries(NEED_MAX)) action[k] = oneLine(a[k] ?? '', max);
    if (Object.values(action).some(v => v == null)) { bad(fields, `actions.${i}`, 'Слишком длинное поле.'); return; }
    actions.push(action);
  });
  if (Object.keys(fields).length) throw new HubError('invalid', { fields });
  return { name, brand, background, ...pics, links, api, login: input.login === true, actions, lock: input.lock === true };
}

const hostOf = u => { try { return new URL(u).hostname.toLowerCase(); } catch { return null; } };

function actionProblem(a, look, domains) {
  if (!a.label || a.label.length > 24) return 'Подпись от 1 до 24 символов.';
  if (!DOS.includes(a.do)) return 'Выберите, что делает кнопка.';
  if (a.icon && !ICONS.includes(a.icon)) return 'Такого значка нет.';
  const need = NEEDS[a.do];
  if (need && !a[need]) return 'Заполните поле этой кнопки.';
  if (a.do === 'connect.best' && !STRATEGIES.includes(a.strategy)) return 'Способ выбора: ping, stable или load.';
  if (a.do === 'connect.recommend' && !PURPOSES.includes(a.purpose)) return 'Выберите назначение из списка.';
  if (NEEDS_API.has(a.do) && !httpsUrl(look.api)) return 'Эта кнопка работает только с API.';
  if (a.do === 'open.link') {
    const h = hostOf(a.url);
    const ok = a.url.startsWith('tg:') ? !/\s/.test(a.url) : Boolean(httpsUrl(a.url) && h && (h === 't.me' || domainAllowed(domains, a.url)));
    if (!ok) return 'Ссылка: tg: или https на t.me или на ваших доменах.';
  }
  return null;
}

/**
 * What keeps <look> from being published for a provider of <tier> with <domains>; <pictures> maps a sha256 to its
 * upload {slot, width, height, size} (only the provider's own). An empty object when it can go out.
 */
export function lookProblems(look, { tier, domains, pictures }) {
  const p = {};
  if (validName(look.name) !== look.name) p.name = 'Название от 1 до 40 символов, без «V2pn».';
  if (!look.brand.length) p.brand = 'Нужен хотя бы один цвет.';
  for (const key of PICTURES) {
    const sha = look[key];
    if (!sha) continue;
    const pic = pictures.get(sha);
    if (!pic) { p[key] = 'Картинки нет среди загруженных. Загрузите её заново.'; continue; }
    const slot = SLOT_OF[key];
    if (!C.SLOTS[slot].fits(pic.width, pic.height) || pic.size > C.MAX_ASSET) p[key] = `Не подходит: нужен ${C.SLOTS[slot].desc[0]}.`;
  }
  if (look.background === 'image' && !look.backdrop) p.backdrop = 'Для фона-картинки загрузите картинку.';
  for (const key of ['cabinet', 'support']) {
    if (look.links[key] && !httpsUrl(look.links[key])) p[`links.${key}`] = 'Ссылка должна начинаться с https://.';
  }
  const tier2 = tier >= 2;
  if (look.api) {
    if (!tier2) p.api = 'API — на ступени 2.';
    else if (!httpsUrl(look.api)) p.api = 'Адрес API должен начинаться с https://.';
    else if (!domainAllowed(domains, look.api)) p.api = 'Адрес API должен быть на одном из ваших доменов.';
  }
  if (look.login && !tier2) p.login = 'Вход по коду — на ступени 2.';
  else if (look.login && !look.api) p.login = 'Для входа по коду нужен адрес API.';
  if (look.lock && !tier2) p.lock = 'Скрытые адреса — на ступени 2.';
  if (look.actions.length && !tier2) p.actions = 'Свои кнопки — на ступени 2.';
  else look.actions.forEach((a, i) => { const why = actionProblem(a, look, domains); if (why) p[`actions.${i}`] = why; });
  return p;
}

/** The theme V2pn reads from the pack: brand colours and the background. */
export function themeOf(look) {
  const brand = look.brand.filter(h => COLOUR.test(h)).slice(0, 3);
  let background = { type: 'solid', colors: [], art: null };
  if (look.background === 'gradient') {
    const stops = gradientStops(brand);
    if (stops.length >= 2) background = { type: 'gradient', colors: stops, art: null };
  }
  if (look.background === 'image' && look.backdrop) background = { type: 'image', colors: [], art: FILE.backdrop };
  return { brand, background };
}

/** The gradient of the brand colours; one colour gets a second one, lighter and a little turned. */
export function gradientStops(brand) {
  if (brand.length >= 2) return brand;
  if (!brand.length) return [];
  const c = C.parse(brand[0]);
  const [ch, h] = C.oklch(c);
  return [brand[0], C.hex(C.fromOklch(Math.min(C.okL(c) + 0.14, 0.93), ch * 0.8, h + 16))];
}

const slug = s => s.toLowerCase().normalize('NFKD').replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 32);

/**
 * partner.json of <look> for provider <id> (an object; jsonfmt writes it). <assets> are [{key, content}] of the
 * pictures that go into the pack, with their pack names; every picture is a PNG.
 */
export function packObject(id, look, assets) {
  const theme = themeOf(look);
  const has = key => assets.some(a => a.key === key);
  const brand = {};
  if (theme.brand[0]) brand.accent = theme.brand[0];
  if (has('logo')) brand.logo = FILE.logo;
  if (has('logoOnLight')) brand.logoOnLight = FILE.logoOnLight;
  if (has('mark')) brand.mark = FILE.mark;
  const pack = { schema: 1, id, name: look.name, brand, theme: { palette: { brand: theme.brand } } };
  if (theme.background.type === 'gradient') pack.theme.background = { type: 'gradient', colors: theme.background.colors };
  if (theme.background.type === 'image') pack.theme.background = { type: 'image', art: theme.background.art };
  if (look.api) pack.api = look.api;
  if (look.login) pack.login = true;
  const links = {};
  if (look.links.cabinet) links.cabinet = look.links.cabinet;
  if (look.links.support) links.support = look.links.support;
  if (Object.keys(links).length) pack.links = links;
  if (look.actions.length) {
    const seen = new Set();
    pack.actions = look.actions.map((a, i) => {
      const o = { id: slug(a.label) || `button-${i + 1}`, label: a.label };
      if (a.icon) o.icon = a.icon;
      o.do = a.do;
      const need = NEEDS[a.do];
      if (need) o[need] = a[need];
      // Two buttons with one label still need two ids.
      if (seen.has(o.id)) o.id = `${o.id}-${i + 1}`.slice(0, 32);
      seen.add(o.id);
      return o;
    });
  }
  if (look.lock) pack.lock = true;
  return pack;
}

/** The pack names and slots of the pictures <look> puts into its pack. */
export function packPictures(look) {
  const theme = themeOf(look);
  return PICTURES.filter(key => look[key] && (key !== 'backdrop' || theme.background.type === 'image'))
    .map(key => ({ key, sha: look[key], name: FILE[key] }));
}

/** The grants of a provider's listing: edition always; the rest at tier 2, as far as its published look uses them. */
export function grantsFor(tier, look) {
  const grants = ['edition'];
  if (tier >= 2 && look?.api) grants.push('account');
  if (tier >= 2 && look?.actions?.length) grants.push('actions');
  if (tier >= 2 && look?.lock) grants.push('lock');
  return grants;
}
