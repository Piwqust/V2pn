// A provider's routing rules (routing.json of the pack), checked with the rules of V2pn's reader
// (ServiceLib/V2pn/V2pnPartnerRouting.cs: V2pnRouteEntries, V2pnRoutingPreset.Parse) so the hub never publishes a
// file V2pn would refuse. Providers write them as JSON or as lines of "entries → where"; the hub keeps the JSON.
import net from 'node:net';
import { HubError } from './errors.mjs';
import { dotnetJson } from './jsonfmt.mjs';

export const PROXY = 'proxy';
export const DIRECT = 'direct';
export const BLOCK = 'block';
const MAX_RULES = 50;
const MAX_ENTRIES_PER_RULE = 500;
const MAX_ENTRIES = 2000;
const MAX_NAME = 40;
const MAX_ENTRY = 200;

const LABEL = /^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$/;
const GEO_TAG = /^!?[a-z0-9][a-z0-9!@._-]{0,63}$/;
// eslint-disable-next-line no-control-regex
const CONTROL = /[\u0000-\u001f\u007f-\u009f]/;

function isDomainName(value) {
  const v = value.replace(/\.+$/, '');
  return v.length > 0 && v.length <= 253 && v.split('.').every(l => LABEL.test(l));
}

/** {address, prefix, v6} of an address or a network; null when it is neither. */
function network(text) {
  const parts = text.split('/');
  if (parts.length > 2 || parts[0].includes('%')) return null;
  let address = parts[0];
  let v6 = false;
  if (net.isIPv4(address)) {
    // .NET reads a leading zero as octal; such an entry would not mean what it says.
    if (address.split('.').some(o => o.length > 1 && o.startsWith('0'))) return null;
  } else if (net.isIPv6(address)) {
    v6 = true;
    try {
      address = new URL(`http://[${address}]/`).hostname.slice(1, -1);
    } catch {
      return null;
    }
  } else {
    return null;
  }
  if (parts.length === 1) return { address, prefix: null, v6 };
  if (!/^\d{1,3}$/.test(parts[1])) return null;
  const prefix = Number(parts[1]);
  return prefix <= (v6 ? 128 : 32) ? { address, prefix, v6 } : null;
}

/** V2pnRouteEntries.Domain: the entry as V2pn writes it, or {problem}. */
export function domainEntry(entry) {
  const text = typeof entry === 'string' ? entry.trim().toLowerCase() : '';
  if (!text || text.length > MAX_ENTRY) return { problem: `«${String(entry ?? '').slice(0, 60)}»: пусто или длиннее ${MAX_ENTRY} символов` };
  const colon = text.indexOf(':');
  const kind = colon < 0 ? 'domain' : text.slice(0, colon);
  const value = colon < 0 ? text : text.slice(colon + 1);
  if (kind === 'geosite') {
    if (GEO_TAG.test(value)) return { value: text };
  } else if (kind === 'domain' || kind === 'full') {
    if (isDomainName(value)) return { value: `${kind}:${value.replace(/\.+$/, '')}` };
  } else if (kind === 'keyword') {
    // Three letters at least: "keyword:a" would catch nearly every site.
    if (value.length >= 3 && /^[a-z0-9.-]+$/.test(value)) return { value: text };
  } else {
    return { problem: `«${text.slice(0, 60)}»: V2pn понимает domain:, full:, keyword:, geosite:` };
  }
  return { problem: `«${text.slice(0, 60)}»: неверная запись ${kind}:` };
}

/** V2pnRouteEntries.Ip: an address, a network or "geoip:tag", or {problem}. */
export function ipEntry(entry) {
  const text = typeof entry === 'string' ? entry.trim().toLowerCase() : '';
  if (text.startsWith('geoip:')) {
    if (GEO_TAG.test(text.slice(6))) return { value: text };
  } else {
    const n = network(text);
    if (n) return { value: `${n.address}${n.prefix != null ? `/${n.prefix}` : ''}` };
  }
  return { problem: `«${text.slice(0, 60)}»: не IP-адрес, не сеть и не geoip:` };
}

const looksLikeIp = text => text.startsWith('geoip:') || network(text.trim().toLowerCase()) != null;

/** V2pnRouteEntries.IsPrivate: the local network's own lists and addresses. */
export function isPrivate(entry) {
  if (entry === 'geosite:private' || entry === 'geoip:private') return true;
  const n = network(entry);
  if (!n) return false;
  if (n.v6) {
    const a = n.address;
    return a === '::1' || /^fe[89ab]/.test(a) || /^f[cd]/.test(a) || (n.prefix != null && n.prefix < 8);
  }
  const [a, b] = n.address.split('.').map(Number);
  return a === 127 || a === 10 || (a === 192 && b === 168) || (a === 169 && b === 254) || (a === 172 && b >= 16 && b <= 31)
    || (n.prefix != null && n.prefix < 8);
}

/** V2pnRouteEntries.TooBroadToBlock: why blocking this entry is too broad, or null. */
export function tooBroadToBlock(entry) {
  if (entry.startsWith('geoip:')) return 'целую страну (geoip:)';
  if (entry.startsWith('domain:') && !entry.slice(7).includes('.')) return 'целую доменную зону';
  if (entry.startsWith('keyword:') && entry.length - 8 < 5) return 'слишком короткое слово';
  const n = network(entry);
  if (n && n.prefix != null && n.prefix < (n.v6 ? 32 : 16)) return `сеть шире /${n.v6 ? 32 : 16}`;
  return isPrivate(entry) ? 'локальную сеть' : null;
}

function parseRule(node, i) {
  const at = `Правило ${i + 1}`;
  if (!node || typeof node !== 'object' || Array.isArray(node)) return { problem: `${at}: не объект.` };
  const to = typeof node.to === 'string' ? node.to.trim() : '';
  if (![PROXY, DIRECT, BLOCK].includes(to)) return { problem: `${at}: куда — proxy, direct или block.` };
  const lists = { domains: [], ips: [] };
  for (const key of ['domains', 'ips']) {
    if (node[key] == null) continue;
    if (!Array.isArray(node[key]) || node[key].length > MAX_ENTRIES_PER_RULE) return { problem: `${at}: ${key} — список до ${MAX_ENTRIES_PER_RULE} записей.` };
    for (const raw of node[key]) {
      const r = key === 'domains' ? domainEntry(raw) : ipEntry(raw);
      if (r.problem) return { problem: `${at}: ${r.problem}.` };
      if (to === BLOCK) {
        const broad = tooBroadToBlock(r.value);
        if (broad) return { problem: `${at}: «${r.value}» блокирует ${broad}. Блокировать можно только узкие списки.` };
      }
      if (!lists[key].includes(r.value)) lists[key].push(r.value);
    }
  }
  if (!lists.domains.length && !lists.ips.length) return { problem: `${at}: нет ни сайтов, ни адресов.` };
  const rule = {};
  if (lists.domains.length) rule.domains = lists.domains;
  if (lists.ips.length) rule.ips = lists.ips;
  rule.to = to;
  return { rule };
}

/**
 * Lines of "entries → where": entries split by commas or spaces, "→", "->" or "=>", where is proxy, direct or block;
 * "default → proxy|direct" sets what the rest does. Lines starting with "#" are comments. Consecutive lines with the
 * same target become one rule.
 */
export function routingFromText(text) {
  const rules = [];
  let fallback;
  const lines = String(text).replace(/\r\n/g, '\n').split('\n');
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].trim();
    if (!line || line.startsWith('#')) continue;
    const m = /^(.*?)\s*(?:→|->|=>)\s*([a-z]+)$/i.exec(line);
    if (!m) throw new HubError('invalid', { fields: { 'rights.routing': `Строка ${i + 1}: нужно «сайты → proxy, direct или block».` } });
    const to = m[2].toLowerCase();
    const entries = m[1].split(/[\s,]+/).filter(Boolean);
    if (entries.length === 1 && entries[0].toLowerCase() === 'default') {
      fallback = to;
      continue;
    }
    const last = rules[rules.length - 1];
    const rule = last && last.to === to ? last : { domains: [], ips: [], to };
    if (rule !== last) rules.push(rule);
    for (const e of entries) (looksLikeIp(e) ? rule.ips : rule.domains).push(e);
  }
  return { ...(fallback ? { default: fallback } : {}), rules };
}

/** The rules as lines, the way routingFromText reads them back. */
export function routingToText(preset) {
  const lines = preset.rules.map(r => `${[...(r.domains || []), ...(r.ips || [])].join(', ')} → ${r.to}`);
  if (preset.default !== PROXY) lines.push(`default → ${preset.default}`);
  return lines.join('\n');
}

/**
 * The preset in the one form the hub keeps and publishes ({schema, name?, default, rules}); throws "invalid" with
 * the first problem under rights.routing. <input> is that object or the text form.
 */
export function parseRouting(input) {
  const fail = message => { throw new HubError('invalid', { fields: { 'rights.routing': message } }); };
  const root = typeof input === 'string' ? routingFromText(input) : input;
  if (!root || typeof root !== 'object' || Array.isArray(root)) fail('Правила: объект или строки «сайты → куда».');
  if (root.schema != null && root.schema !== 1) fail('schema должна быть 1.');
  let name = null;
  if (root.name != null && root.name !== '') {
    if (typeof root.name !== 'string' || root.name.trim().length > MAX_NAME || CONTROL.test(root.name)) fail(`Название правил: до ${MAX_NAME} символов.`);
    name = root.name.trim() || null;
  }
  const fallback = root.default == null ? PROXY : root.default;
  if (fallback !== PROXY && fallback !== DIRECT) fail('Остальное — proxy или direct (block отрезал бы всё остальное).');
  if (!Array.isArray(root.rules) || root.rules.length === 0 || root.rules.length > MAX_RULES) fail(`Нужно от 1 до ${MAX_RULES} правил.`);
  const rules = [];
  let total = 0;
  root.rules.forEach((node, i) => {
    const r = parseRule(node, i);
    if (r.problem) fail(r.problem);
    total += (r.rule.domains?.length || 0) + (r.rule.ips?.length || 0);
    if (total > MAX_ENTRIES) fail(`Не больше ${MAX_ENTRIES} сайтов и адресов во всех правилах.`);
    rules.push(r.rule);
  });
  const preset = { schema: 1 };
  if (name) preset.name = name;
  preset.default = fallback;
  preset.rules = rules;
  return preset;
}

/** routing.json's bytes: what goes into the pack and whose SHA-256 the index names. */
export function routingBytes(preset) {
  return dotnetJson(preset);
}
