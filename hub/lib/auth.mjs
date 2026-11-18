// Sign-in by a code sent to the email, sessions in an httpOnly cookie, and the one-time sign-in link for the owner.
// Codes, session tokens and link tokens are stored hashed; codes are compared in constant time.
// The owner can also sign in by password (scrypt hash in HUB_DATA/keys), so the admin works without mail.
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { HubError } from './errors.mjs';
import { log } from './log.mjs';
import { normalizeEmail } from './validate.mjs';

export const COOKIE = 'hub_s';
const CODE_TTL = 10 * 60 * 1000;
const CODE_TRIES = 5;
const SESSION_TTL = 30 * 24 * 3600 * 1000;
const ADMIN_LINK_TTL = 15 * 60 * 1000;
const HOUR = 3600 * 1000;
// ~32 MB and ~100 ms per check on the server: slow enough against guessing, light enough for 1 CPU.
const SCRYPT = { N: 2 ** 15, r: 8, p: 1, maxmem: 64 * 1024 * 1024 };

/** Sliding-window counters in memory (they reset with the process, the database ones do not). */
export class RateLimiter {
  constructor(now = Date.now) {
    this.now = now;
    this.hits = new Map();
  }

  /** Counts a hit on <key>; false when there were already <limit> in the last <windowMs>. */
  hit(key, limit, windowMs) {
    const now = this.now();
    const list = (this.hits.get(key) || []).filter(t => now - t < windowMs);
    if (list.length >= limit) {
      this.hits.set(key, list);
      return false;
    }
    list.push(now);
    this.hits.set(key, list);
    return true;
  }

  sweep(maxWindowMs = 24 * HOUR) {
    const now = this.now();
    for (const [key, list] of this.hits) {
      const kept = list.filter(t => now - t < maxWindowMs);
      if (kept.length) this.hits.set(key, kept);
      else this.hits.delete(key);
    }
  }
}

const sha256 = text => crypto.createHash('sha256').update(text).digest('hex');

export function parseCookies(header) {
  const out = {};
  for (const part of String(header || '').split(';')) {
    const eq = part.indexOf('=');
    if (eq <= 0) continue;
    const name = part.slice(0, eq).trim();
    if (!(name in out)) out[name] = part.slice(eq + 1).trim();
  }
  return out;
}

export class Auth {
  constructor({ db, config, secret, mailer, limiter, now = Date.now }) {
    this.db = db;
    this.config = config;
    this.secret = secret;
    this.mailer = mailer;
    this.limiter = limiter;
    this.now = now;
  }

  codeHash(email, code) {
    return crypto.createHmac('sha256', this.secret).update(`${email}:${code}`).digest('hex');
  }

  isAdmin(email) {
    return this.config.admins.includes(email);
  }

  /** Makes and delivers a code; {delivery: 'mail' | 'outbox'}. */
  async requestCode(emailText, ip) {
    const email = normalizeEmail(emailText);
    if (!email) throw new HubError('invalid', { fields: { email: 'Нужна почта вида name@example.com.' } });
    if (!this.limiter.hit(`code-ip:${ip}`, 10, HOUR) || !this.limiter.hit('code-all', 300, HOUR)) throw new HubError('rate_limited');
    const now = this.now();
    const recent = this.db.all('SELECT created_at FROM login_codes WHERE email = ? AND created_at > ? ORDER BY created_at DESC', email, now - HOUR);
    if (recent.length >= 5 || (recent[0] && now - recent[0].created_at < 30 * 1000)) {
      throw new HubError('rate_limited', { message: 'Код уже отправлен. Новый можно запросить чуть позже.' });
    }
    const code = String(crypto.randomInt(0, 1_000_000)).padStart(6, '0');
    const expires = now + CODE_TTL;
    this.db.tx(() => {
      this.db.run('UPDATE login_codes SET used = 1 WHERE email = ? AND used = 0', email);
      this.db.run('INSERT INTO login_codes (email, code_hash, created_at, expires_at) VALUES (?, ?, ?, ?)', email, this.codeHash(email, code), now, expires);
    });
    const subject = `Код для входа: ${code}`;
    const text = `Ваш код для входа на ${new URL(this.config.origin).host}: ${code}\n\nОн действует 10 минут. Если вы не входили, просто не отвечайте на это письмо.\n`;
    if (!this.mailer) {
      // No mail yet: the owner sees the code in the admin and passes it on.
      this.db.run('INSERT INTO outbox (email, subject, body, created_at, expires_at) VALUES (?, ?, ?, ?, ?)', email, subject, text, now, expires);
      log.info('code to outbox', { domain: email.split('@')[1] });
      return { delivery: 'outbox' };
    }
    try {
      await this.mailer({ to: email, subject, text });
    } catch (e) {
      log.error('mail failed', { domain: email.split('@')[1], error: String(e?.message || e) });
      throw new HubError('mail_failed');
    }
    log.info('code sent', { domain: email.split('@')[1] });
    return { delivery: 'mail' };
  }

  /** Checks a code; a new session {token, account} when it is right. */
  verifyCode(emailText, codeText, ip) {
    const email = normalizeEmail(emailText);
    const code = typeof codeText === 'string' ? codeText.replace(/\s+/g, '') : '';
    if (!email || !/^\d{6}$/.test(code)) throw new HubError('code_invalid');
    if (!this.limiter.hit(`verify-ip:${ip}`, 30, HOUR)) throw new HubError('rate_limited');
    const now = this.now();
    const row = this.db.get('SELECT id, code_hash, tries FROM login_codes WHERE email = ? AND used = 0 AND expires_at > ? ORDER BY created_at DESC LIMIT 1', email, now);
    if (!row || row.tries >= CODE_TRIES) throw new HubError('code_expired');
    this.db.run('UPDATE login_codes SET tries = tries + 1 WHERE id = ?', row.id);
    const expected = Buffer.from(row.code_hash, 'hex');
    const actual = Buffer.from(this.codeHash(email, code), 'hex');
    if (!crypto.timingSafeEqual(expected, actual)) {
      const left = CODE_TRIES - row.tries - 1;
      throw new HubError(left > 0 ? 'code_invalid' : 'code_expired', { detail: { triesLeft: Math.max(0, left) } });
    }
    this.db.run('UPDATE login_codes SET used = 1 WHERE id = ?', row.id);
    this.db.run('DELETE FROM outbox WHERE email = ?', email);
    return this.startSession(email);
  }

  startSession(email) {
    const now = this.now();
    const account = this.db.tx(() => {
      this.db.run('INSERT INTO accounts (email, created_at) VALUES (?, ?) ON CONFLICT (email) DO NOTHING', email, new Date(now).toISOString());
      this.db.run('UPDATE accounts SET last_login_at = ? WHERE email = ?', new Date(now).toISOString(), email);
      return this.db.get('SELECT id, email FROM accounts WHERE email = ?', email);
    });
    const token = crypto.randomBytes(32).toString('base64url');
    this.db.run('INSERT INTO sessions (token_hash, account_id, created_at, expires_at, seen_at) VALUES (?, ?, ?, ?, ?)',
      sha256(token), account.id, now, now + SESSION_TTL, now);
    return { token, account };
  }

  cookie(token) {
    const secure = this.config.secureCookies ? '; Secure' : '';
    return `${COOKIE}=${token}; HttpOnly${secure}; SameSite=Lax; Path=/; Max-Age=${SESSION_TTL / 1000}`;
  }

  clearCookie() {
    const secure = this.config.secureCookies ? '; Secure' : '';
    return `${COOKIE}=; HttpOnly${secure}; SameSite=Lax; Path=/; Max-Age=0`;
  }

  /** The signed-in person of a request: {account, admin, tokenHash}, or null. */
  session(cookieHeader) {
    const token = parseCookies(cookieHeader)[COOKIE];
    if (!token || !/^[A-Za-z0-9_-]{43}$/.test(token)) return null;
    const hash = sha256(token);
    const now = this.now();
    const row = this.db.get(
      'SELECT s.account_id, s.expires_at, s.seen_at, a.email FROM sessions s JOIN accounts a ON a.id = s.account_id WHERE s.token_hash = ?', hash);
    if (!row || row.expires_at <= now) return null;
    if (now - row.seen_at > HOUR) this.db.run('UPDATE sessions SET seen_at = ? WHERE token_hash = ?', now, hash);
    return { account: { id: row.account_id, email: row.email }, admin: this.isAdmin(row.email), tokenHash: hash };
  }

  signOut(tokenHash) {
    this.db.run('DELETE FROM sessions WHERE token_hash = ?', tokenHash);
  }

  /** A one-time sign-in link for an owner's email (hub.mjs admin-link), valid 15 minutes. */
  createAdminLink(emailText) {
    const email = normalizeEmail(emailText);
    if (!email || !this.isAdmin(email)) throw new Error(`${emailText} is not in HUB_ADMINS`);
    const token = crypto.randomBytes(32).toString('base64url');
    this.db.run('INSERT INTO admin_links (token_hash, email, expires_at) VALUES (?, ?, ?)', sha256(token), email, this.now() + ADMIN_LINK_TTL);
    return `${this.config.origin}/api/auth/admin-link?token=${token}`;
  }

  consumeAdminLink(token, ip) {
    if (!this.limiter.hit(`link-ip:${ip}`, 20, HOUR)) throw new HubError('rate_limited');
    if (typeof token !== 'string' || !/^[A-Za-z0-9_-]{43}$/.test(token)) throw new HubError('link_invalid');
    const hash = sha256(token);
    const row = this.db.tx(() => {
      const r = this.db.get('SELECT email, expires_at, used FROM admin_links WHERE token_hash = ?', hash);
      if (!r || r.used || r.expires_at <= this.now()) return null;
      this.db.run('UPDATE admin_links SET used = 1 WHERE token_hash = ?', hash);
      return r;
    });
    if (!row || !this.isAdmin(row.email)) throw new HubError('link_invalid');
    return this.startSession(row.email);
  }

  /**
   * Sets a new random password for an owner's email (hub.mjs admin-password) and returns it; only its scrypt hash is
   * kept. The owner signs in without mail this way; providers still sign in by code.
   */
  setAdminPassword(emailText) {
    const email = normalizeEmail(emailText);
    if (!email || !this.isAdmin(email)) throw new Error(`${emailText} is not in HUB_ADMINS`);
    // 20 characters from an alphabet without look-alikes: ~115 bits, easy to type from a screen.
    const alphabet = 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    const raw = Array.from({ length: 20 }, () => alphabet[crypto.randomInt(alphabet.length)]).join('');
    const password = raw.match(/.{5}/g).join('-');
    const salt = crypto.randomBytes(16);
    const hash = crypto.scryptSync(password, salt, 32, SCRYPT);
    const all = this.readPasswords();
    all[email] = { salt: salt.toString('base64'), hash: hash.toString('base64'), set: new Date(this.now()).toISOString() };
    fs.mkdirSync(path.dirname(this.config.passwordFile), { recursive: true, mode: 0o700 });
    const tmp = `${this.config.passwordFile}.tmp`;
    fs.writeFileSync(tmp, JSON.stringify(all, null, 2), { mode: 0o600 });
    fs.renameSync(tmp, this.config.passwordFile);
    // Sessions made before end, so a leaked old password or session stops working at once.
    this.db.run('DELETE FROM sessions WHERE account_id IN (SELECT id FROM accounts WHERE email = ?)', email);
    return password;
  }

  readPasswords() {
    try {
      return JSON.parse(fs.readFileSync(this.config.passwordFile, 'utf8')) || {};
    } catch {
      return {};
    }
  }

  /** The owner's sign-in by email and password; a new session {token, account}. */
  verifyPassword(emailText, passwordText, ip) {
    const email = normalizeEmail(emailText);
    const password = typeof passwordText === 'string' ? passwordText.trim().slice(0, 200) : '';
    if (!this.limiter.hit(`pw-ip:${ip}`, 10, HOUR) || !this.limiter.hit(`pw-email:${email}`, 20, HOUR)) throw new HubError('rate_limited');
    const entry = email && this.isAdmin(email) ? this.readPasswords()[email] : null;
    // An unknown email costs the same work as a wrong password, so the answer does not tell which emails are owners'.
    const salt = entry ? Buffer.from(entry.salt, 'base64') : crypto.randomBytes(16);
    const actual = crypto.scryptSync(password, salt, 32, SCRYPT);
    const expected = entry ? Buffer.from(entry.hash, 'base64') : crypto.randomBytes(32);
    if (!entry || !password || !crypto.timingSafeEqual(expected, actual)) {
      log.info('password sign-in refused');
      throw new HubError('password_invalid');
    }
    log.info('password sign-in');
    return this.startSession(email);
  }

  /** Drops what has expired: codes, sessions, links, the outbox. */
  sweep() {
    const now = this.now();
    this.db.run('DELETE FROM login_codes WHERE expires_at < ?', now - 24 * HOUR);
    this.db.run('DELETE FROM sessions WHERE expires_at < ?', now);
    this.db.run('DELETE FROM admin_links WHERE expires_at < ?', now - HOUR);
    this.db.run('DELETE FROM outbox WHERE expires_at < ?', now);
    this.limiter.sweep();
  }
}
