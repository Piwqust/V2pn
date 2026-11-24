// Accounts by login and password (scrypt), sessions in an httpOnly cookie. There is no mail and no recovery: a
// moderator or the owner's command line gives a new password. Session tokens are stored hashed.
import crypto from 'node:crypto';
import { HubError } from './errors.mjs';
import { ipKey, wideIpKey } from './ip.mjs';
import { log } from './log.mjs';

export const COOKIE = 'hub_s';
export const ROLES = ['owner', 'moderator', 'provider'];
const SESSION_TTL = 30 * 24 * 3600 * 1000;
const HOUR = 3600 * 1000;
// ~32 MB and ~100 ms per check on the server: slow enough against guessing, light enough for 1 CPU.
const SCRYPT = { N: 2 ** 15, r: 8, p: 1, maxmem: 64 * 1024 * 1024 };
const LOGIN = /^[a-z0-9._-]{3,32}$/;
// eslint-disable-next-line no-control-regex
const CONTROL = /[\u0000-\u001f\u007f-\u009f]/;
// For a new password; an account made when six were enough still signs in with its own.
const MIN_PASSWORD = 8;
const MAX_PASSWORD = 200;
// Password hashes at once, each off the main thread on libuv's pool (four threads, which DNS and files share too) and
// with its 32 MB. A sign-in beyond them is turned away (rate_limited) instead of waiting in a queue anyone could fill.
export const MAX_HASHING = 2;
// A network is an IPv4 address or an IPv6 /64; an IPv6 /64 also counts toward its /48 (the wide network), so a site
// cannot walk round a limit from /64 to /64. Per hour:
// - sign-ins and registrations from one network, and from one /48;
export const SIGNINS_PER_NETWORK = 30;
export const SIGNINS_PER_WIDE_NETWORK = 60;
export const REGISTRATIONS_PER_NETWORK = 10;
export const REGISTRATIONS_PER_WIDE_NETWORK = 30;
// - failed sign-ins to one login: from one network, after which that network waits; from one /48; and from
//   everywhere, a ceiling far above them against guessing from many networks at once.
export const FAILS_PER_NETWORK = 10;
export const FAILS_PER_WIDE_NETWORK = 20;
export const FAILS_PER_LOGIN = 100;
// A network a login signed in from in the last 30 days is trusted for that login: there only that network's own
// limits apply, so a stranger elsewhere cannot close the login to the owner or a moderator, not even at the ceiling.
// Kept in memory, like the counters: a restart forgets both.
export const TRUST_MS = 30 * 24 * HOUR;

/** Sliding-window counters in memory (they reset with the process, the database ones do not). */
export class RateLimiter {
  constructor(now = Date.now) {
    this.now = now;
    this.hits = new Map();
  }

  /** The hits on <key> in the last <windowMs>. */
  count(key, windowMs) {
    const now = this.now();
    const list = (this.hits.get(key) || []).filter(t => now - t < windowMs);
    this.hits.set(key, list);
    return list.length;
  }

  /** Counts a hit on <key>; false when there were already <limit> in the last <windowMs>. */
  hit(key, limit, windowMs) {
    if (this.count(key, windowMs) >= limit) return false;
    this.hits.get(key).push(this.now());
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

/** A login as it is looked up: trimmed, lowercase; migrated accounts may have an email for one. Null when it cannot be one. */
export function normalizeLogin(text) {
  if (typeof text !== 'string') return null;
  const t = text.trim().toLowerCase();
  return t.length >= 3 && t.length <= 254 && !CONTROL.test(t) && !/\s/.test(t) ? t : null;
}

/** A login a new account may take: 3 to 32 of a-z, 0-9, ".", "_", "-". */
export function validLogin(text) {
  const t = normalizeLogin(text);
  return t && LOGIN.test(t) ? t : null;
}

const scrypt = (password, salt, params) => new Promise((resolve, reject) => {
  crypto.scrypt(password, salt, 32, params, (e, key) => (e ? reject(e) : resolve(key)));
});

const encode = (salt, hash) => `scrypt$${SCRYPT.N}$${SCRYPT.r}$${SCRYPT.p}$${salt.toString('base64')}$${hash.toString('base64')}`;

/** The stored form of <password>, hashed off the main thread. */
export async function hashPassword(password) {
  const salt = crypto.randomBytes(16);
  return encode(salt, await scrypt(password, salt, SCRYPT));
}

/** The same on the main thread: the command line and a moderator's reset (a random password, one at a time). */
export function hashPasswordSync(password) {
  const salt = crypto.randomBytes(16);
  return encode(salt, crypto.scryptSync(password, salt, 32, SCRYPT));
}

/**
 * Whether <password> is the one <stored> was made of, hashed off the main thread. An unknown account, or one without
 * a password, costs the same work against a made-up hash, so the time of the answer does not tell which logins exist.
 */
export async function checkPassword(stored, password) {
  const m = /^scrypt\$(\d+)\$(\d+)\$(\d+)\$([A-Za-z0-9+/=]+)\$([A-Za-z0-9+/=]+)$/.exec(stored || '');
  const salt = m ? Buffer.from(m[4], 'base64') : crypto.randomBytes(16);
  const params = m ? { N: Number(m[1]), r: Number(m[2]), p: Number(m[3]), maxmem: SCRYPT.maxmem } : SCRYPT;
  const actual = await scrypt(String(password), salt, params);
  const expected = m ? Buffer.from(m[5], 'base64') : crypto.randomBytes(32);
  return Boolean(m) && expected.length === actual.length && crypto.timingSafeEqual(expected, actual);
}

/** A password to read off a screen: 12 characters without look-alikes in three groups (~68 bits). */
export function randomPassword() {
  const alphabet = 'abcdefghjkmnpqrstuvwxyz23456789';
  const raw = Array.from({ length: 12 }, () => alphabet[crypto.randomInt(alphabet.length)]).join('');
  return raw.match(/.{4}/g).join('-');
}

function passwordProblem(password) {
  if (typeof password !== 'string' || password.length < MIN_PASSWORD) return `Пароль от ${MIN_PASSWORD} символов.`;
  if (password.length > MAX_PASSWORD) return `Пароль до ${MAX_PASSWORD} символов.`;
  return null;
}

export class Auth {
  constructor({ db, config, limiter, now = Date.now, maxHashing = MAX_HASHING }) {
    this.db = db;
    this.config = config;
    this.limiter = limiter;
    this.now = now;
    this.maxHashing = maxHashing;
    this.hashing = 0;
    // "<login>|<network>" → when that login last signed in from that network.
    this.trusted = new Map();
  }

  /** Whether <login> signed in from <network> (ipKey) in the last TRUST_MS. */
  trusts(login, network) {
    const at = this.trusted.get(`${login}|${network}`);
    return at !== undefined && this.now() - at < TRUST_MS;
  }

  /** Runs <fn> (password hashing) as one of at most maxHashing at a time; rate_limited when all are busy. */
  async withHashSlot(fn) {
    if (this.hashing >= this.maxHashing) throw new HubError('rate_limited');
    this.hashing++;
    try {
      return await fn();
    } finally {
      this.hashing--;
    }
  }

  accountByLogin(login) {
    return login ? this.db.get('SELECT id, login, role, password FROM accounts WHERE login = ?', login) : undefined;
  }

  /** A new provider account and its session: {token, account}. */
  async register(loginText, password, ip) {
    if (!this.limiter.hit(`register-ip:${ipKey(ip)}`, REGISTRATIONS_PER_NETWORK, HOUR)) throw new HubError('rate_limited');
    const wide = wideIpKey(ip);
    if (wide && !this.limiter.hit(`register-ip:${wide}`, REGISTRATIONS_PER_WIDE_NETWORK, HOUR)) throw new HubError('rate_limited');
    const fields = {};
    const login = validLogin(loginText);
    if (!login) fields.login = 'Логин: от 3 до 32 латинских букв, цифр и знаков . _ -';
    const why = passwordProblem(password);
    if (why) fields.password = why;
    if (Object.keys(fields).length) throw new HubError('invalid', { fields });
    const taken = () => new HubError('login_taken', { fields: { login: 'Такой логин уже занят.' } });
    if (this.accountByLogin(login)) throw taken();
    const hash = await this.withHashSlot(() => hashPassword(password));
    const now = new Date(this.now()).toISOString();
    const id = this.db.tx(() => {
      if (this.accountByLogin(login)) throw taken();
      return Number(this.db.run("INSERT INTO accounts (login, role, password, password_at, created_at) VALUES (?, 'provider', ?, ?, ?)",
        login, hash, now, now).lastInsertRowid);
    });
    log.info('account registered');
    return this.startSession(id);
  }

  /**
   * Sign-in by login and password: {token, account}. Failures close the login to the network they come from
   * (FAILS_PER_NETWORK), to its /48 past FAILS_PER_WIDE_NETWORK, and to everyone past FAILS_PER_LOGIN; never to a
   * network the login signed in from lately (TRUST_MS), where only that network's own limits apply.
   */
  async signIn(loginText, password, ip) {
    const network = ipKey(ip);
    const wide = wideIpKey(ip);
    const login = normalizeLogin(loginText);
    const trusted = Boolean(login) && this.trusts(login, network);
    if (!this.limiter.hit(`signin-ip:${network}`, SIGNINS_PER_NETWORK, HOUR)) throw new HubError('rate_limited');
    if (wide && !trusted && !this.limiter.hit(`signin-ip:${wide}`, SIGNINS_PER_WIDE_NETWORK, HOUR)) throw new HubError('rate_limited');
    // [counter, the limit it closes the login at here]; the wider ones do not close a trusted network.
    const fails = [[`signin-fail:${login}|${network}`, FAILS_PER_NETWORK]];
    if (wide) fails.push([`signin-fail:${login}|${wide}`, trusted ? Infinity : FAILS_PER_WIDE_NETWORK]);
    fails.push([`signin-fail:${login}`, trusted ? Infinity : FAILS_PER_LOGIN]);
    if (login && fails.some(([key, limit]) => this.limiter.count(key, HOUR) >= limit)) throw new HubError('rate_limited');
    const account = this.accountByLogin(login);
    const typed = typeof password === 'string' ? password.slice(0, MAX_PASSWORD) : '';
    const ok = await this.withHashSlot(() => checkPassword(account?.password, typed));
    // A password reset while this one was being checked wins.
    if (!ok || this.accountByLogin(login)?.password !== account.password) {
      if (login) for (const [key] of fails) this.limiter.hit(key, Infinity, HOUR);
      log.info('sign-in refused');
      throw new HubError('password_invalid');
    }
    this.trusted.set(`${login}|${network}`, this.now());
    return this.startSession(account.id);
  }

  startSession(accountId) {
    const now = this.now();
    this.db.run('UPDATE accounts SET last_login_at = ? WHERE id = ?', new Date(now).toISOString(), accountId);
    const token = crypto.randomBytes(32).toString('base64url');
    this.db.run('INSERT INTO sessions (token_hash, account_id, created_at, expires_at, seen_at) VALUES (?, ?, ?, ?, ?)',
      sha256(token), accountId, now, now + SESSION_TTL, now);
    return { token, account: this.accountView(accountId) };
  }

  accountView(id) {
    const row = this.db.get('SELECT id, login, role FROM accounts WHERE id = ?', id);
    return row ? { id: row.id, login: row.login, role: row.role } : null;
  }

  cookie(token) {
    const secure = this.config.secureCookies ? '; Secure' : '';
    return `${COOKIE}=${token}; HttpOnly${secure}; SameSite=Lax; Path=/; Max-Age=${SESSION_TTL / 1000}`;
  }

  clearCookie() {
    const secure = this.config.secureCookies ? '; Secure' : '';
    return `${COOKIE}=; HttpOnly${secure}; SameSite=Lax; Path=/; Max-Age=0`;
  }

  /** The signed-in person of a request: {account: {id, login, role}, mod, owner, tokenHash}, or null. */
  session(cookieHeader) {
    const token = parseCookies(cookieHeader)[COOKIE];
    if (!token || !/^[A-Za-z0-9_-]{43}$/.test(token)) return null;
    const hash = sha256(token);
    const now = this.now();
    const row = this.db.get(
      'SELECT s.account_id, s.expires_at, s.seen_at, a.login, a.role FROM sessions s JOIN accounts a ON a.id = s.account_id WHERE s.token_hash = ?', hash);
    if (!row || row.expires_at <= now) return null;
    if (now - row.seen_at > HOUR) this.db.run('UPDATE sessions SET seen_at = ? WHERE token_hash = ?', now, hash);
    return {
      account: { id: row.account_id, login: row.login, role: row.role },
      mod: row.role === 'owner' || row.role === 'moderator',
      owner: row.role === 'owner',
      tokenHash: hash,
    };
  }

  signOut(tokenHash) {
    this.db.run('DELETE FROM sessions WHERE token_hash = ?', tokenHash);
  }

  /** The person's own new password; the other sessions of the account end. */
  async changePassword(session, oldPassword, newPassword) {
    if (!this.limiter.hit(`password:${session.account.id}`, 10, HOUR)) throw new HubError('rate_limited');
    const why = passwordProblem(newPassword);
    const row = this.db.get('SELECT password FROM accounts WHERE id = ?', session.account.id);
    const typed = typeof oldPassword === 'string' ? oldPassword.slice(0, MAX_PASSWORD) : '';
    const hash = await this.withHashSlot(async () => {
      if (!await checkPassword(row?.password, typed)) {
        throw new HubError('password_invalid', { message: 'Старый пароль не подходит.', fields: { password: 'Старый пароль не подходит.' } });
      }
      if (why) throw new HubError('invalid', { fields: { newPassword: why } });
      return hashPassword(newPassword);
    });
    this.db.run('UPDATE accounts SET password = ?, password_at = ? WHERE id = ?', hash, new Date(this.now()).toISOString(), session.account.id);
    this.db.run('DELETE FROM sessions WHERE account_id = ? AND token_hash != ?', session.account.id, session.tokenHash);
  }

  /** A new random password for <accountId>, returned once (only its hash is kept); its sessions end. */
  resetPassword(accountId) {
    const password = randomPassword();
    this.db.run('UPDATE accounts SET password = ?, password_at = ? WHERE id = ?', hashPasswordSync(password), new Date(this.now()).toISOString(), accountId);
    this.db.run('DELETE FROM sessions WHERE account_id = ?', accountId);
    return password;
  }

  /** hub.mjs owner <login>: the account made or promoted to owner, with a new password printed once. */
  makeOwner(loginText) {
    const login = normalizeLogin(loginText);
    if (!login) throw new Error(`${loginText} is not a login`);
    const id = this.db.tx(() => {
      const existing = this.accountByLogin(login);
      if (existing) {
        this.db.run("UPDATE accounts SET role = 'owner' WHERE id = ?", existing.id);
        return existing.id;
      }
      if (!validLogin(login)) throw new Error(`${loginText}: a new login is 3 to 32 of a-z, 0-9, ".", "_", "-"`);
      return Number(this.db.run("INSERT INTO accounts (login, role, created_at) VALUES (?, 'owner', ?)", login, new Date(this.now()).toISOString()).lastInsertRowid);
    });
    this.db.audit('cli', 'account.owner', login);
    return this.resetPassword(id);
  }

  /** Drops expired sessions, old counters and networks trusted no longer. */
  sweep() {
    this.db.run('DELETE FROM sessions WHERE expires_at < ?', this.now());
    this.limiter.sweep();
    for (const [key, at] of this.trusted) if (this.now() - at >= TRUST_MS) this.trusted.delete(key);
  }
}
