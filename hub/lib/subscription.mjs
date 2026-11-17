// Reading a provider's test subscription the way V2pn reads it: the panel headers (Handler/SubPanelHeaders.cs, with
// the Happ, INCY and v2RayTun ones), the "#key: value" lines a body may carry above its links, and the body itself
// (base64 or plain share links, Xray JSON, sing-box JSON, Clash YAML), counted by protocol.

// What V2pn (v2rayN's core set) can run; anything else in a subscription is skipped by the app.
export const SUPPORTED = new Set(['vless', 'vmess', 'trojan', 'shadowsocks', 'hysteria2', 'tuic', 'wireguard', 'anytls', 'naive', 'socks', 'http']);

const SCHEMES = {
  vless: 'vless', vmess: 'vmess', trojan: 'trojan', ss: 'shadowsocks', ssr: 'shadowsocksr', hysteria2: 'hysteria2', hy2: 'hysteria2',
  hysteria: 'hysteria', tuic: 'tuic', wireguard: 'wireguard', wg: 'wireguard', anytls: 'anytls', naive: 'naive',
  'naive+https': 'naive', 'naive+quic': 'naive', socks: 'socks', socks5: 'socks', socks4: 'socks', http: 'http', https: 'http',
  'hysteria2+realm': 'hysteria2', 'hysteria2+realm+http': 'hysteria2', juicity: 'juicity', mieru: 'mieru', snell: 'snell',
};

const XRAY_PROTOCOLS = { vless: 'vless', vmess: 'vmess', trojan: 'trojan', shadowsocks: 'shadowsocks', wireguard: 'wireguard', socks: 'socks', http: 'http', hysteria: 'hysteria2', hysteria2: 'hysteria2' };
const XRAY_SKIP = new Set(['freedom', 'blackhole', 'dns', 'loopback']);
const SINGBOX_PROTOCOLS = { vless: 'vless', vmess: 'vmess', trojan: 'trojan', shadowsocks: 'shadowsocks', hysteria2: 'hysteria2', hysteria: 'hysteria', tuic: 'tuic', wireguard: 'wireguard', anytls: 'anytls', naive: 'naive', socks: 'socks', http: 'http', shadowtls: 'shadowtls', ssh: 'ssh', tor: 'tor' };
const SINGBOX_SKIP = new Set(['direct', 'block', 'dns', 'selector', 'urltest']);
const CLASH_PROTOCOLS = { vless: 'vless', vmess: 'vmess', trojan: 'trojan', ss: 'shadowsocks', ssr: 'shadowsocksr', hysteria2: 'hysteria2', hysteria: 'hysteria', tuic: 'tuic', wireguard: 'wireguard', anytls: 'anytls', socks5: 'socks', http: 'http', snell: 'snell', mieru: 'mieru' };

// Every header SubPanelHeaders reads.
export const KNOWN_HEADERS = [
  'profile-title', 'content-disposition', 'subscription-userinfo', 'profile-update-interval', 'support-url', 'profile-web-page-url',
  'announce', 'v2pn-account-token', 'announce-url', 'sub-info-text', 'sub-info-color', 'sub-info-button-text', 'sub-info-button-link',
  'banner-text', 'banner-button-text', 'banner-button-url', 'banner-url', 'premium-url', 'sub-expire', 'sub-expire-button-link',
  'expiry-banner-text', 'expiry-banner-button-text', 'expiry-banner-button-url', 'expiry-banner-url', 'subscription-autoconnect',
  'subscription-autoconnect-type', 'subscriptions-sort-type', 'sort-order', 'subscription-ping-onopen-enabled', 'ping-type',
  'check-url-via-proxy', 'update-always', 'subscription-auto-update-open-enable', 'new-domain', 'new-url', 'hide-settings', 'hide-url',
];

const BODY_KEYS = new Set([
  'profile-title', 'subscription-userinfo', 'profile-update-interval', 'support-url', 'profile-web-page-url', 'announce', 'announce-url',
  'sub-info-text', 'sub-info-color', 'sub-info-button-text', 'sub-info-button-link', 'sub-expire', 'sub-expire-button-link',
  'subscription-autoconnect', 'subscription-autoconnect-type', 'subscriptions-sort-type', 'subscription-ping-onopen-enabled', 'ping-type',
  'check-url-via-proxy', 'update-always', 'subscription-auto-update-open-enable', 'hide-settings', 'hide-url',
]);

const clip = (s, n) => (s == null ? null : s.length <= n ? s : s.slice(0, n));
// eslint-disable-next-line no-control-regex
const clean = s => s.replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/g, '').trim();
const oneLine = s => (s == null ? null : s.replace(/[\r\n\t]+/g, ' ').trim() || null);

function fromBase64(text) {
  const t = text.trim().replace(/-/g, '+').replace(/_/g, '/').replace(/\s+/g, '');
  if (!/^[A-Za-z0-9+/]*={0,2}$/.test(t)) return null;
  const buf = Buffer.from(t, 'base64');
  try {
    return new TextDecoder('utf-8', { fatal: true }).decode(buf);
  } catch {
    return null;
  }
}

/** Plain text, or "base64:<b64>"; raw UTF-8 in a header (read as Latin-1) repaired. */
export function decodeText(value) {
  if (value == null || !String(value).trim()) return null;
  let text = String(value).trim();
  if (/^base64:/i.test(text)) {
    text = fromBase64(text.slice(7));
    if (text == null) return null;
  } else if (/[\u0080-ÿ]/.test(text) && !/[^\u0000-ÿ]/.test(text)) {
    try {
      text = new TextDecoder('utf-8', { fatal: true }).decode(Buffer.from(text, 'latin1'));
    } catch { /* not UTF-8 after all: keep it */ }
  }
  text = clean(text);
  return text || null;
}

function parseNumber(s) {
  const t = String(s).trim().replace(/^"|"$/g, '');
  if (!/^-?\d+(\.\d+)?([eE][+-]?\d+)?$/.test(t)) return null;
  const n = Math.floor(Number(t));
  return Number.isFinite(n) ? n : null;
}

/** subscription-userinfo: "upload=N; download=N; total=N; expire=UNIX"; null when no part is a number. */
export function parseUserInfo(value) {
  if (!value) return null;
  const out = { upload: 0, download: 0, total: 0, expire: 0 };
  let any = false;
  for (const part of String(value).split(/[;,]/)) {
    const eq = part.indexOf('=');
    if (eq <= 0) continue;
    const key = part.slice(0, eq).trim().toLowerCase();
    const n = parseNumber(part.slice(eq + 1));
    if (n == null || !(key in out)) continue;
    out[key] = Math.max(0, n);
    any = true;
  }
  // Above this the expiry is in milliseconds (as seconds it would be past the year 5000).
  if (out.expire > 100_000_000_000) out.expire = Math.floor(out.expire / 1000);
  return any ? out : null;
}

function parseLink(value, { secure = false } = {}) {
  const text = decodeText(value);
  if (!text || /\s/.test(text)) return null;
  try {
    const u = new URL(text);
    if (u.protocol === 'tg:') return text;
    if (u.protocol === 'https:' && u.hostname && !u.username && !u.password) return text;
    if (!secure && u.protocol === 'http:' && u.hostname) return text;
  } catch { /* not a link */ }
  return null;
}

function contentDispositionName(value) {
  if (!value) return null;
  let plain = null;
  let extended = null;
  for (const part of String(value).split(';')) {
    const eq = part.indexOf('=');
    if (eq <= 0) continue;
    const name = part.slice(0, eq).trim().toLowerCase();
    let v = part.slice(eq + 1).trim().replace(/^"|"$/g, '');
    if (name === 'filename*') {
      const m = /^([^']*)'[^']*'(.*)$/.exec(v);
      try { extended = decodeURIComponent(m ? m[2] : v); } catch { /* ignore */ }
    } else if (name === 'filename') {
      plain = v;
    }
  }
  let file = extended || plain;
  if (!file) return null;
  file = file.replace(/\\/g, '/');
  file = file.slice(file.lastIndexOf('/') + 1).replace(/\.(txt|yaml|yml|json|conf|list)$/i, '');
  return clean(file) || null;
}

/** The "#key: value" lines above the links of a link list (never of a JSON or YAML config). */
export function readBodyMeta(text) {
  const result = {};
  if (!text) return result;
  let read = 0;
  for (const raw of text.split('\n')) {
    const line = raw.trim().replace(/^﻿/, '');
    if (!line) continue;
    if (!line.startsWith('#')) {
      if (!line.includes('://')) return {};
      break;
    }
    if (++read > 64) break;
    const colon = line.indexOf(':');
    if (colon <= 1) continue;
    const key = line.slice(1, colon).trim().toLowerCase();
    const value = line.slice(colon + 1).trim();
    if (value && BODY_KEYS.has(key) && !(key in result)) result[key] = value;
  }
  return result;
}

/** What V2pn shows from the headers (the response headers win over the body's lines). */
export function readHeaders(responseHeaders, bodyMeta = {}) {
  const h = { ...bodyMeta };
  for (const name of KNOWN_HEADERS) {
    const v = responseHeaders[name];
    const value = Array.isArray(v) ? v.find(x => String(x).trim()) : v;
    if (value != null && String(value).trim()) h[name] = String(value);
  }
  const title = clip(oneLine(decodeText(h['profile-title'])), 120) || clip(oneLine(contentDispositionName(h['content-disposition'])), 120);
  const interval = parseNumber(h['profile-update-interval'] ?? '');
  const happInfo = decodeText(h['sub-info-text']);
  const info = happInfo && happInfo !== '0' ? happInfo : (decodeText(h['banner-text']) !== '0' ? decodeText(h['banner-text']) : null);
  return {
    title,
    userInfo: parseUserInfo(h['subscription-userinfo']),
    updateIntervalHours: interval && interval > 0 ? Math.min(interval, 24 * 365) : null,
    supportUrl: parseLink(h['support-url']),
    webPageUrl: parseLink(h['profile-web-page-url']),
    announce: clip(decodeText(h.announce), 2000),
    announceUrl: parseLink(h['announce-url'], { secure: true }),
    infoText: clip(info, 200),
    expiryNotice: h['sub-expire'] != null ? /^(1|true|yes|on)$/i.test(String(h['sub-expire']).trim().replace(/"/g, '')) : null,
    expiryText: clip(decodeText(h['expiry-banner-text']), 200),
    expiryButtonUrl: parseLink(h['expiry-banner-button-url'], { secure: true }) || parseLink(h['expiry-banner-url'], { secure: true })
      || parseLink(h['sub-expire-button-link'], { secure: true }),
    autoconnect: /^(1|true|yes|on)$/i.test(String(h['subscription-autoconnect'] ?? '').trim()),
    sortType: h['subscriptions-sort-type'] || h['sort-order'] ? String(h['subscriptions-sort-type'] || h['sort-order']).trim().toLowerCase() : null,
    hideSettings: /^(1|true|yes|on)$/i.test(String(h['hide-settings'] ?? '').trim()),
    hideUrl: /^(1|true|yes|on)$/i.test(String(h['hide-url'] ?? '').trim()),
    accountToken: h['v2pn-account-token'] ? true : false,
    newUrl: h['new-url'] ? true : false,
    present: Object.keys(h).filter(k => KNOWN_HEADERS.includes(k)).sort(),
  };
}

// ------------------------------------------------------------------ the body

function hostPort(host, port) {
  const h = String(host || '').trim().replace(/^\[|\]$/g, '');
  const p = Number(port);
  return h && Number.isInteger(p) && p > 0 && p < 65536 ? { host: h.toLowerCase(), port: p } : null;
}

function linkServer(line, scheme) {
  try {
    if (scheme === 'vmess') {
      const json = fromBase64(line.slice('vmess://'.length).split('#')[0]);
      if (json) {
        const o = JSON.parse(json);
        return hostPort(o.add, o.port);
      }
    }
    if (scheme === 'ss') {
      let rest = line.slice(5).split('#')[0];
      if (!rest.includes('@')) {
        const decoded = fromBase64(rest.split('?')[0]);
        if (decoded) rest = decoded;
      }
      const at = rest.lastIndexOf('@');
      if (at >= 0) {
        const u = new URL('ss://x@' + rest.slice(at + 1));
        return hostPort(u.hostname, u.port);
      }
      return null;
    }
    const u = new URL(line.replace(/^[a-z0-9+.-]+:\/\//i, 'proto://'));
    return hostPort(u.hostname, u.port || (scheme === 'https' ? 443 : scheme === 'http' ? 80 : null));
  } catch {
    return null;
  }
}

function tally(list) {
  const byProtocol = {};
  for (const s of list) byProtocol[s.protocol] = (byProtocol[s.protocol] || 0) + 1;
  return byProtocol;
}

function parseLinks(text) {
  const servers = [];
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    const m = /^([a-z0-9+.-]+):\/\//i.exec(line);
    if (!m) continue;
    const scheme = m[1].toLowerCase();
    const protocol = SCHEMES[scheme];
    if (!protocol) continue;
    // A line of https links is a list of subscriptions, not a server.
    if (scheme === 'https' || scheme === 'http') continue;
    servers.push({ protocol, ...linkServer(line, scheme) });
  }
  return servers;
}

function xrayOutbounds(config) {
  const out = [];
  for (const ob of Array.isArray(config?.outbounds) ? config.outbounds : []) {
    const proto = String(ob?.protocol || '').toLowerCase();
    if (!proto || XRAY_SKIP.has(proto)) continue;
    const vnext = ob.settings?.vnext?.[0] || ob.settings?.servers?.[0] || ob.settings?.peers?.[0];
    const endpoint = ob.settings?.address ? hostPort(ob.settings.address, ob.settings.port)
      : vnext?.endpoint ? hostPort(String(vnext.endpoint).replace(/:\d+$/, ''), String(vnext.endpoint).split(':').pop())
        : vnext ? hostPort(vnext.address, vnext.port) : null;
    out.push({ protocol: XRAY_PROTOCOLS[proto] || proto, ...endpoint });
  }
  return out;
}

function singboxOutbounds(config) {
  const out = [];
  for (const ob of [...(Array.isArray(config?.outbounds) ? config.outbounds : []), ...(Array.isArray(config?.endpoints) ? config.endpoints : [])]) {
    const type = String(ob?.type || '').toLowerCase();
    if (!type || SINGBOX_SKIP.has(type)) continue;
    const peer = Array.isArray(ob.peers) ? ob.peers[0] : null;
    out.push({ protocol: SINGBOX_PROTOCOLS[type] || type, ...(hostPort(ob.server ?? peer?.address, ob.server_port ?? peer?.port) || {}) });
  }
  return out;
}

function clashProxies(text) {
  const lines = text.split(/\r?\n/);
  const servers = [];
  let inProxies = false;
  let current = null;
  const flush = () => {
    if (current?.type) {
      const t = current.type.toLowerCase();
      servers.push({ protocol: CLASH_PROTOCOLS[t] || t, ...(hostPort(current.server, current.port) || {}) });
    }
    current = null;
  };
  const unquote = v => v.trim().replace(/^['"]|['"]$/g, '');
  for (const raw of lines) {
    if (/^\S/.test(raw)) {
      if (inProxies) flush();
      inProxies = /^proxies\s*:/.test(raw);
      continue;
    }
    if (!inProxies) continue;
    const line = raw.trim();
    if (!line || line.startsWith('#')) continue;
    const flow = /^-\s*\{(.*)\}\s*$/.exec(line);
    if (flow) {
      flush();
      current = {};
      for (const part of flow[1].split(/,(?=(?:[^"']|"[^"]*"|'[^']*')*$)/)) {
        const c = part.indexOf(':');
        if (c > 0) current[part.slice(0, c).trim()] = unquote(part.slice(c + 1));
      }
      flush();
      continue;
    }
    const item = /^-\s*(.*)$/.exec(line);
    if (item && /^\s{0,4}-/.test(raw)) {
      flush();
      current = {};
      const c = item[1].indexOf(':');
      if (c > 0) current[item[1].slice(0, c).trim()] = unquote(item[1].slice(c + 1));
      continue;
    }
    if (current) {
      const c = line.indexOf(':');
      const key = c > 0 ? line.slice(0, c).trim() : '';
      if (['type', 'server', 'port', 'name'].includes(key) && !(key in current)) current[key] = unquote(line.slice(c + 1));
    }
  }
  if (inProxies) flush();
  return servers;
}

/** The servers of a subscription body: {format, servers: [{protocol, host?, port?}], meta: {header: value}}. */
export function parseBody(buf) {
  let text = buf.toString('utf8').replace(/^﻿/, '').trim();
  if (!text) return { format: 'empty', servers: [], meta: {} };
  if (text.startsWith('{') || text.startsWith('[')) {
    try {
      const json = JSON.parse(text);
      const configs = Array.isArray(json) ? json : [json];
      const singbox = configs.some(c => Array.isArray(c?.outbounds) && c.outbounds.some(o => o && typeof o.type === 'string'));
      if (singbox) return { format: 'singbox', servers: configs.flatMap(singboxOutbounds), meta: {} };
      return { format: 'xray-json', servers: configs.flatMap(xrayOutbounds), meta: {} };
    } catch {
      return { format: 'unknown', servers: [], meta: {} };
    }
  }
  if (/^proxies\s*:/m.test(text) && !/^[a-z0-9+.-]+:\/\//im.test(text.split('\n')[0])) {
    return { format: 'clash', servers: clashProxies(text), meta: {} };
  }
  let format = 'plain';
  if (!text.startsWith('#') && !text.includes('://')) {
    const decoded = fromBase64(text);
    if (decoded && decoded.includes('://')) {
      text = decoded;
      format = 'base64';
    }
  }
  const servers = parseLinks(text);
  return { format: servers.length || text.includes('://') ? format : 'unknown', servers, meta: readBodyMeta(text) };
}

export function summarize(servers) {
  const byProtocol = tally(servers);
  const supported = servers.filter(s => SUPPORTED.has(s.protocol)).length;
  return { total: servers.length, supported, byProtocol };
}

export const PROTOCOL_NAMES = {
  vless: 'VLESS', vmess: 'VMess', trojan: 'Trojan', shadowsocks: 'Shadowsocks', shadowsocksr: 'ShadowsocksR', hysteria2: 'Hysteria2',
  hysteria: 'Hysteria', tuic: 'TUIC', wireguard: 'WireGuard', anytls: 'AnyTLS', naive: 'Naive', socks: 'SOCKS', http: 'HTTP',
};
