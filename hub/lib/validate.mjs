// The rules V2pn applies to what the hub publishes (V2pnPartnerTrust, V2pnPartnerPublisher.ReadListing), and the
// limits the hub puts on what people send it.
import { domainToASCII } from 'node:url';

export const PARTNER_ID = /^[a-z0-9][a-z0-9-]{1,31}$/;

// Ids that would read as V2pn's own or collide with the site's paths.
export const RESERVED_IDS = new Set([
  'v2pn', 'v2rayn', 'admin', 'api', 'www', 'p', 'r', 'releases', 'partners', 'providers', 'assets', 'static', 'site', 'hub', 'root',
  'null', 'undefined', 'test', 'example', 'official', 'support', 'help', 'login', 'cabinet', 'download', 'downloads', 'install',
  'update', 'updates', 'mail', 'docs', 'guide', 'builder', 'status', 'security',
]);

// eslint-disable-next-line no-control-regex
const CONTROL = /[\u0000-\u001f\u007f-\u009f]/;

export function validId(id) {
  return typeof id === 'string' && PARTNER_ID.test(id);
}

/** V2pnPartnerTrust.Name: trimmed, 1 to 40 characters, no control characters, never passing for V2pn. */
export function validName(name) {
  if (typeof name !== 'string') return null;
  const text = name.trim();
  if (text.length === 0 || text.length > 40 || CONTROL.test(text)) return null;
  if (text.replace(/ /g, '').toLowerCase().includes('v2pn')) return null;
  return text;
}

const LABEL = /^(?!-)[a-z0-9-]{1,63}(?<!-)$/;

/** A subscription domain a listing may name: "example.com" or "*.example.com", at least a name and a zone. */
export function validDomain(domain) {
  if (typeof domain !== 'string' || domain.length > 253) return false;
  const host = domain.startsWith('*.') ? domain.slice(2) : domain;
  if (!host.includes('.') || host.includes('*')) return false;
  const labels = host.split('.');
  if (!labels.every(l => LABEL.test(l))) return false;
  // A zone is never all digits: that would be an IPv4 address.
  return !/^\d+$/.test(labels[labels.length - 1]);
}

/** A domain as V2pn compares it: trimmed, lowercase, IDN in ASCII, no trailing dot; null when it is not one. */
export function normalizeDomain(text) {
  if (typeof text !== 'string') return null;
  let d = text.trim().replace(/\.$/, '').toLowerCase();
  if (!d) return null;
  const wild = d.startsWith('*.');
  const ascii = domainToASCII(wild ? d.slice(2) : d);
  if (!ascii) return null;
  d = (wild ? '*.' : '') + ascii;
  return validDomain(d) ? d : null;
}

/** V2pnPartnerTrust.DomainAllowed: the host of <url> is one of the domains or under a "*." one. */
export function domainAllowed(domains, url) {
  let host;
  try {
    host = new URL(url).hostname.replace(/\.$/, '').toLowerCase();
  } catch {
    return false;
  }
  if (!host) return false;
  return domains.some(d => (d.startsWith('*.') ? host.endsWith('.' + d.slice(2)) : host === d));
}

/** Two domain lists that could claim the same subscription (the same name, or one under the other's "*."). */
export function domainsOverlap(a, b) {
  const covers = (wild, d) => wild.startsWith('*.') && (d === wild || (d.startsWith('*.') ? d.slice(2) : d).endsWith('.' + wild.slice(2)));
  for (const x of a) {
    for (const y of b) {
      if (x === y || covers(x, y) || covers(y, x)) return `${x} / ${y}`;
    }
  }
  return null;
}

/** An https link without a user name or password, at most <max> characters; null otherwise. */
export function httpsUrl(text, max = 2048) {
  if (typeof text !== 'string') return null;
  const t = text.trim();
  if (!t || t.length > max || /\s/.test(t)) return null;
  try {
    const u = new URL(t);
    if (u.protocol !== 'https:' || !u.hostname || u.username || u.password) return null;
    return t;
  } catch {
    return null;
  }
}

/** One line of plain text, trimmed, at most <max> characters; '' for empty; null when it breaks the rule. */
export function oneLine(text, max) {
  if (text == null) return '';
  if (typeof text !== 'string') return null;
  const t = text.trim();
  if (t.length > max || CONTROL.test(t)) return null;
  return t;
}

/** Text that may have line breaks (no other controls), at most <max> characters. */
export function multiLine(text, max) {
  if (text == null) return '';
  if (typeof text !== 'string') return null;
  const t = text.replace(/\r\n/g, '\n').trim();
  // eslint-disable-next-line no-control-regex
  if (t.length > max || /[\u0000-\u0009\u000b-\u001f\u007f]/.test(t)) return null;
  return t;
}

export const COLOUR = /^#[0-9A-Fa-f]{6}$/;
