// The hub's settings, from the environment (documented in hub/README.md).
import path from 'node:path';
import { normalizeEmail } from './validate.mjs';

// The User-Agent V2pn sends for a subscription (V2pnSubs.SubscriptionUserAgent: v2rayN's agent, by which panels pick
// the format, then V2pn's own name). Remnawave and Marzban answer it with share links.
export const DEFAULT_CHECK_UA = 'v2rayN/7.25.6 V2pn/1.3.0';

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
  const admins = (env.HUB_ADMINS || '').split(',').map(normalizeEmail).filter(Boolean);
  const repo = env.HUB_GITHUB_REPO || 'Piwqust/V2pn';
  if (!/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(repo)) throw new Error(`HUB_GITHUB_REPO is not owner/name: ${repo}`);
  return {
    host: env.HUB_HOST || '127.0.0.1',
    port: int(env.HUB_PORT, 18190, 1, 65535),
    data,
    dbFile: path.join(data, 'hub.db'),
    keysDir: path.join(data, 'keys'),
    keyFile: path.join(data, 'keys', 'hub-partner-key.pem'),
    secretFile: path.join(data, 'keys', 'hub-secret'),
    uploadsDir: path.join(data, 'uploads'),
    publicDir: path.resolve(env.HUB_PUBLIC_DIR || '/var/www/v2pn.angleford.co'),
    origin: originUrl.origin,
    secureCookies: originUrl.protocol === 'https:',
    admins,
    smtpUrl: env.HUB_SMTP_URL || '',
    mailFrom: env.HUB_MAIL_FROM || `V2pn <no-reply@${originUrl.hostname}>`,
    githubRepo: repo,
    checkUserAgent: env.HUB_CHECK_UA || DEFAULT_CHECK_UA,
  };
}
