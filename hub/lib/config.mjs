// The hub's settings, from the environment (documented in hub/README.md; the root partners' in
// deploy/v2pn-hub.env.example).
import path from 'node:path';
import { parseRootList } from './roots.mjs';

// The User-Agent V2pn sends for a subscription (V2pnSubs.SubscriptionUserAgent: v2rayN's agent, by which panels pick
// the format, then V2pn's own name). Remnawave and Marzban answer it with share links.
export const DEFAULT_CHECK_UA = 'v2rayN/7.25.6 V2pn/1.3.0';

// V2pn's root key for partner indexes: the plain key of V2pnPartnerKeys in v2rayN/Directory.Build.targets, with which
// the owner signs partner-index.json on his computer.
export const ROOT_PARTNER_KEY = 'MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE4LVjtb8RY7TUlzR32OpH1Tn5FPKp5B+m2Zb72/cQzjOSuBvYnBWdHnZ7PnPoFhmNKSeCj5shwtmJD1JIVtCGCg==';

function int(value, fallback, min, max) {
  const n = Number.parseInt(value ?? '', 10);
  return Number.isInteger(n) && n >= min && n <= max ? n : fallback;
}

export function loadConfig(env = process.env) {
  const data = path.resolve(env.HUB_DATA || '/var/lib/v2pn-hub');
  const origin = (env.HUB_ORIGIN || 'https://v2pn.angleford.co').replace(/\/+$/, '');
  let originUrl;
  try {
    originUrl = new URL(origin);
  } catch {
    throw new Error(`HUB_ORIGIN is not a URL: ${origin}`);
  }
  const repo = env.HUB_GITHUB_REPO || 'Piwqust/V2pn';
  if (!/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(repo)) throw new Error(`HUB_GITHUB_REPO is not owner/name: ${repo}`);
  return {
    host: env.HUB_HOST || '127.0.0.1',
    port: int(env.HUB_PORT, 18190, 1, 65535),
    data,
    dbFile: path.join(data, 'hub.db'),
    keysDir: path.join(data, 'keys'),
    keyFile: path.join(data, 'keys', 'hub-partner-key.pem'),
    uploadsDir: path.join(data, 'uploads'),
    publicDir: path.resolve(env.HUB_PUBLIC_DIR || '/var/www/v2pn.angleford.co'),
    origin: originUrl.origin,
    secureCookies: originUrl.protocol === 'https:',
    githubRepo: repo,
    checkUserAgent: env.HUB_CHECK_UA || DEFAULT_CHECK_UA,
    // The root-signed index the hub keeps ids and domains for (lib/roots.mjs), its copy, its keys, and a fixed list.
    rootIndexUrl: env.HUB_ROOT_INDEX_URL || `https://github.com/${repo}/releases/latest/download/partner-index.json`,
    rootIndexFile: path.join(data, 'root-partner-index.json'),
    rootKeys: (env.HUB_ROOT_KEYS || ROOT_PARTNER_KEY).split('|').map(k => k.trim()).filter(Boolean),
    rootPartners: parseRootList(env.HUB_ROOT_PARTNERS),
    // V2pn's own copy of runetfreedom's rule lists (lib/geo.mjs); HUB_GEO_MIRROR=0 turns it off.
    geoMirror: env.HUB_GEO_MIRROR !== '0',
    geoSource: env.HUB_GEO_SOURCE || undefined,
  };
}
