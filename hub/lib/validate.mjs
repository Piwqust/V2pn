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

export const TELEGRAM_HOSTS = ['t.me', 'telegram.me'];

/**
 * The rule of an open.link button, V2pnPartnerCatalog.ActionLink's: https on t.me or on the listing's <domains>
 * (domainAllowed). Cabinet and support links have a wider one (linkAllowed).
 */
export function partnerLink(url, domains) {
  const link = httpsUrl(url);
  if (!link) return false;
  return new URL(link).hostname.replace(/\.$/, '').toLowerCase() === 't.me' || domainAllowed(domains, link);
}

// Public suffixes of more than one label: names under which anyone gets a name of their own, a country's second level
// or a hosting service's. A partner's site (partnerSite) is never one of these nor above one. A short list kept by
// hand; V2pn keeps the same one (copy it as it is: lowercase names, one per string, any order).
export const PUBLIC_SUFFIXES = Object.freeze([
  // Russia and its neighbours
  'com.ru', 'net.ru', 'org.ru', 'pp.ru', 'msk.ru', 'spb.ru', 'msk.su', 'spb.su',
  'com.ua', 'net.ua', 'org.ua', 'in.ua', 'pp.ua', 'kiev.ua', 'com.by', 'net.by', 'org.by', 'com.kz', 'net.kz', 'org.kz',
  'com.ge', 'com.az', 'com.kg', 'com.tj', 'com.uz', 'co.uz', 'com.am',
  // Elsewhere
  'co.uk', 'org.uk', 'me.uk', 'ltd.uk', 'plc.uk', 'ac.uk', 'gov.uk', 'com.br', 'net.br', 'org.br', 'com.tr', 'net.tr',
  'org.tr', 'co.jp', 'ne.jp', 'or.jp', 'com.au', 'net.au', 'org.au', 'co.kr', 'or.kr', 'co.in', 'net.in', 'org.in',
  'com.cn', 'net.cn', 'org.cn', 'com.hk', 'com.tw', 'com.sg', 'co.nz', 'co.za', 'com.mx', 'com.ar', 'co.id', 'com.my',
  'com.vn', 'co.il', 'com.pl', 'co.th', 'com.ph', 'com.eg', 'com.sa', 'ae.org', 'eu.org', 'us.com', 'uk.com', 'eu.com',
  // Hosting and dynamic DNS: every customer gets a name under the service's
  'github.io', 'gitlab.io', 'pages.dev', 'workers.dev', 'r2.dev', 'vercel.app', 'netlify.app', 'herokuapp.com',
  'onrender.com', 'fly.dev', 'deno.dev', 'web.app', 'firebaseapp.com', 'appspot.com', 'azurewebsites.net',
  'cloudfront.net', 'amazonaws.com', 's3.amazonaws.com', 'trycloudflare.com', 'ngrok.io', 'ngrok-free.app',
  'duckdns.org', 'ddns.net', 'no-ip.org', 'hopto.org', 'sslip.io', 'nip.io', 'glitch.me', 'replit.app', 'blogspot.com',
]);
const SUFFIXES = new Set(PUBLIC_SUFFIXES);

/**
 * The site of a partner's domain: the domain (without "*."), then its parent, its grandparent and so on, as long as
 * the name has at least two labels and is not in PUBLIC_SUFFIXES; the last name reached. Null when the domain itself
 * is a public suffix or a single label.
 *   sub.nordline.com → nordline.com · a.b.nordline.co.uk → nordline.co.uk · nordline.workers.dev → nordline.workers.dev
 */
export function partnerSite(domain) {
  const labels = String(domain).replace(/^\*\./, '').toLowerCase().split('.');
  let site = null;
  for (let i = 0; labels.length - i >= 2; i++) {
    const name = labels.slice(i).join('.');
    if (SUFFIXES.has(name)) break;
    site = name;
  }
  return site;
}

/**
 * THE RULE OF A PARTNER'S CABINET AND SUPPORT LINKS; V2pn applies the same one, keep the two alike. A link passes
 * when it is https without a user name or password and its host (lowercase, without a trailing dot) is
 *   1. t.me or telegram.me, or
 *   2. one of the partner's <domains> (without "*.") or a name under it, or
 *   3. the site of one of them (partnerSite: a parent domain, never a public suffix) or a name under that site.
 * For sub.nordline.com: sub.nordline.com, my.sub.nordline.com, nordline.com and lk.nordline.com pass; com,
 * nordline.com.evil.com and evil.com do not; for sub.nordline.co.uk, nordline.co.uk passes and co.uk and x.co.uk do not.
 * V2pn itself would open any https link there: the rule keeps a provider from sending people to someone else's site
 * under its name. A button's open.link keeps V2pn's narrower rule (partnerLink).
 */
export function linkAllowed(url, domains) {
  const link = httpsUrl(url);
  if (!link) return false;
  const host = new URL(link).hostname.replace(/\.$/, '').toLowerCase();
  if (TELEGRAM_HOSTS.includes(host)) return true;
  const under = (name, base) => name === base || name.endsWith(`.${base}`);
  return domains.some(d => {
    const bare = d.replace(/^\*\./, '');
    const site = partnerSite(bare);
    return under(host, bare) || (site !== null && under(host, site));
  });
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
