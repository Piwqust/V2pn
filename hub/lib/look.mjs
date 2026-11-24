// A partner's look as the provider edits it, its buttons, the checks they must pass before they go out (the rules of
// V2pn's reader: V2pnPartnerTrust, V2pnPartnerCatalog.ParseAction, V2pnPartnerSlots, V2pnPartnerTheme), and
// partner.json built from them as docs/partners/partner.schema.json describes it.
import { colors as C } from './colors.mjs';
import { HubError } from './errors.mjs';
import { COLOUR, httpsUrl, linkAllowed, oneLine, partnerLink, partnerSite, validName } from './validate.mjs';

// The rule of cabinet and support links lives in validate.mjs, with the list V2pn mirrors.
export { linkAllowed };

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

export const LAYOUTS = ['v2pn', 'stickers'];
// The words the site's form uses for the background, next to the pack's own.
const BACKGROUNDS = { none: 'none', map: 'none', gradient: 'gradient', image: 'image', picture: 'image' };

/** A look with nothing but the name. */
export function defaultLook(name) {
  return { name, brand: [], background: 'none', layout: 'v2pn', logo: null, logoOnLight: null, mark: null, backdrop: null,
    links: { cabinet: '', support: '' } };
}

const bad = (fields, key, message) => { fields[key] = message; };

/** "t.me/x" as the provider types it: an https link without the scheme gets it. */
function withScheme(text) {
  return text && !/^[a-z][a-z0-9+.-]*:/i.test(text) ? `https://${text}` : text;
}

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
  const background = BACKGROUNDS[input.background ?? 'none'];
  if (!background) bad(fields, 'background', 'Фон: карта мира, градиент или картинка.');
  const layout = input.layout ?? 'v2pn';
  if (!LAYOUTS.includes(layout)) bad(fields, 'layout', 'Раскладка: обычная V2pn или наклейки.');
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
    else links[key] = withScheme(v);
  }
  if (Object.keys(fields).length) throw new HubError('invalid', { fields });
  return { name, brand, background, layout, ...pics, links };
}

/** The buttons of a request body, each field within its limits; throws "invalid" under <prefix> otherwise. */
export function parseActions(input, prefix = 'actions') {
  const fields = {};
  if (!Array.isArray(input)) throw new HubError('invalid', { fields: { [prefix]: 'Список кнопок.' } });
  if (input.length > 4) bad(fields, prefix, 'Не больше четырёх кнопок.');
  if (!input.length) bad(fields, prefix, 'Добавьте хотя бы одну кнопку.');
  const actions = [];
  input.slice(0, 4).forEach((a, i) => {
    if (!a || typeof a !== 'object') { bad(fields, `${prefix}.${i}`, 'Кнопка не заполнена.'); return; }
    const action = { label: oneLine(a.label ?? '', 64), icon: oneLine(a.icon ?? '', 16), do: oneLine(a.do ?? '', 32) };
    for (const [k, max] of Object.entries(NEED_MAX)) action[k] = oneLine(a[k] ?? '', max);
    if (Object.values(action).some(v => v == null)) { bad(fields, `${prefix}.${i}`, 'Слишком длинное поле.'); return; }
    // Only the field its kind needs is kept, so the same button always has the same content (and hash).
    const need = NEEDS[action.do];
    for (const k of Object.keys(NEED_MAX)) if (k !== need) action[k] = '';
    actions.push(action);
  });
  if (Object.keys(fields).length) throw new HubError('invalid', { fields });
  return actions;
}

/** Why a button would not pass V2pnPartnerCatalog.ParseAction for a partner with <api> on <domains>; null when it does. */
export function actionProblem(a, api, domains) {
  if (!a.label || a.label.length > 24) return 'Подпись от 1 до 24 символов.';
  if (!DOS.includes(a.do)) return 'Выберите, что делает кнопка.';
  if (a.icon && !ICONS.includes(a.icon)) return 'Такого значка нет.';
  const need = NEEDS[a.do];
  if (need && !a[need]) return 'Заполните поле этой кнопки.';
  if (a.do === 'connect.best' && !STRATEGIES.includes(a.strategy)) return 'Способ выбора: ping, stable или load.';
  if (a.do === 'connect.recommend' && !PURPOSES.includes(a.purpose)) return 'Выберите назначение из списка.';
  if (NEEDS_API.has(a.do) && !httpsUrl(api || '')) return 'Эта кнопка работает только с API (особое право «API»).';
  if (a.do === 'open.link') {
    const ok = a.url.startsWith('tg:') ? !/\s/.test(a.url) : partnerLink(a.url, domains);
    if (!ok) return 'Ссылка: tg: или https на t.me или на домене вашей подписки.';
  }
  return null;
}

export const needsApi = a => NEEDS_API.has(a.do);

/** Where a cabinet or support link of a partner on <domains> may lead, in the words of the refusal. */
function linkPlaces(domains) {
  const sites = [...new Set(domains.map(d => partnerSite(d) ?? d.replace(/^\*\./, '')))];
  return sites.length === 1 ? `${sites[0]} или его поддомен` : `${sites.join(', ')} или их поддомены`;
}

/**
 * What keeps <look> from being published; <pictures> maps a sha256 to its upload {slot, width, height, size} (only
 * the account's own), <domains> are the partner's (the links must be on them; not checked when unknown yet). An empty
 * object when it can go out.
 */
export function lookProblems(look, { pictures, domains }) {
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
    const link = look.links[key];
    if (!link) continue;
    if (!httpsUrl(link)) p[`links.${key}`] = 'Ссылка должна начинаться с https://.';
    else if (domains && !linkAllowed(link, domains)) p[`links.${key}`] = `Ссылка должна вести на ${linkPlaces(domains)}, либо на t.me.`;
  }
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
 * partner.json of <look> for partner <id> (an object; jsonfmt writes it). <look> carries the approved rights too
 * (api, login, actions, lock). <assets> are [{key, content}] of the pictures that go into the pack.
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
  // V2pn's own layout is what a pack without one gets; leaving it out keeps such packs as they were.
  if (look.layout && look.layout !== 'v2pn') pack.theme.layout = look.layout;
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
