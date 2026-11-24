// What the hub does, apart from HTTP: a provider's partner (saved = published), its special rights, the moderators'
// decisions, complaints, publication, import of a partners/<id> folder and the housekeeping timers.
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { Auth, RateLimiter, normalizeLogin, validLogin } from './auth.mjs';
import { checkSubscription } from './check.mjs';
import { colors as C } from './colors.mjs';
import { DownloadCounter } from './downloads.mjs';
import { HubError } from './errors.mjs';
import { ipKey } from './ip.mjs';
import { log } from './log.mjs';
import { defaultLook, lookProblems, parseLook, themeOf, PICTURES, SLOT_OF } from './look.mjs';
import { NetError, safeFetch } from './net.mjs';
import { pageTheme } from './page.mjs';
import { GRANT_ORDER, ROOT_ONLY_GRANTS, buildPublication, publishedStamp, writePublication } from './publish.mjs';
import { RIGHTS, contentHash, parseRights, rightState, rightView } from './rights.mjs';
import { RootPartners } from './roots.mjs';
import { parseRouting } from './routing.mjs';
import { RESERVED_IDS, domainAllowed, domainsOverlap, httpsUrl, multiLine, normalizeDomain, oneLine, validId, validName } from './validate.mjs';

const HOUR = 3600 * 1000;
const sha256 = buf => crypto.createHash('sha256').update(buf).digest('hex');
const nowIso = () => new Date().toISOString();
const today = () => new Date().toISOString().slice(0, 10);
const parseJson = (text, fallback) => { try { return text ? JSON.parse(text) : fallback; } catch { return fallback; } };

const PNG_SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

const TRANSLIT = {
  а: 'a', б: 'b', в: 'v', г: 'g', д: 'd', е: 'e', ё: 'e', ж: 'zh', з: 'z', и: 'i', й: 'y', к: 'k', л: 'l', м: 'm', н: 'n', о: 'o',
  п: 'p', р: 'r', с: 's', т: 't', у: 'u', ф: 'f', х: 'h', ц: 'ts', ч: 'ch', ш: 'sh', щ: 'sch', ъ: '', ы: 'y', ь: '', э: 'e', ю: 'yu', я: 'ya',
};

/** The id a name suggests: Latin letters, digits and dashes, Cyrillic spelled out ("Дамир и Ваня" → damir-i-vanya). */
export function idFromName(name) {
  const latin = [...String(name).toLowerCase()].map(c => TRANSLIT[c] ?? c).join('').normalize('NFKD');
  return latin.replace(/[^a-z0-9]+/g, '-').replace(/^-+/, '').slice(0, 32).replace(/-+$/, '');
}

export class Hub {
  /** <net> is passed to every outgoing request (tests give their resolver and allow-list). */
  constructor({ config, db, key, net = {} }) {
    this.config = config;
    this.db = db;
    this.key = key;
    this.net = net;
    this.limiter = new RateLimiter();
    this.auth = new Auth({ db, config, limiter: this.limiter });
    this.counter = new DownloadCounter(db);
    this.checksRunning = 0;
    this.lastPublishError = null;
    fs.mkdirSync(config.uploadsDir, { recursive: true, mode: 0o700 });
    // The partners the owner signs with the root key: their ids and domains are not the hub's to give.
    this.roots = new RootPartners({ file: config.rootIndexFile, url: config.rootIndexUrl, keys: config.rootKeys, fixed: config.rootPartners, net });
    if (this.roots.loadCached() || config.rootPartners.length) this.rootCollisions();
  }

  /**
   * An address the hub may request: https only. Tests set net.allowHttp (in code, never from the environment) to
   * talk to their local servers.
   */
  targetUrl(text, max = 2048) {
    const url = httpsUrl(text, max);
    if (url || !this.net.allowHttp || typeof text !== 'string') return url;
    try {
      const u = new URL(text.trim());
      return u.protocol === 'http:' && u.hostname && !u.username && text.length <= max ? text.trim() : null;
    } catch {
      return null;
    }
  }

  // ---------------------------------------------------------------- the provider's partner

  partnerRow(id) {
    const row = this.db.get('SELECT * FROM partners WHERE id = ?', id);
    if (!row) throw new HubError('not_found');
    return row;
  }

  ownPartner(accountId) {
    return this.db.get('SELECT * FROM partners WHERE account_id = ?', accountId) ?? null;
  }

  rightRows(partnerId) {
    return this.db.all('SELECT * FROM rights WHERE partner_id = ? ORDER BY name', partnerId);
  }

  pictureMap(accountId) {
    return new Map(this.db.all('SELECT sha256, slot, width, height, size FROM uploads WHERE account_id = ?', accountId).map(r => [r.sha256, r]));
  }

  /** What blocks a look, the colour notes and the colours V2pn will draw (for the live preview); <domains> are the partner's. */
  lookReview(look, accountId, domains) {
    const problems = lookProblems(look, { pictures: this.pictureMap(accountId), domains });
    const theme = themeOf(look);
    if (theme.brand.length && pageTheme(theme).failures.length) problems.colours = 'Текст на странице партнёра будет нечитаем. Возьмите другой цвет.';
    const derived = mode => {
      const d = C.derive(theme, mode === 'dark');
      const stage = C.stage(theme, mode === 'dark');
      return {
        accent: C.hex(d.accent), onAccent: C.hex(d.onAccent), accentText: C.hex(d.accentText), ground: C.hex(d.ground),
        surface: C.hex(d.surface), ink: C.hex(d.ink),
        stage: stage ? { kind: stage.kind, stops: stage.stops.map(C.hex), ink: C.hex(stage.ink), soft: C.hex(stage.soft),
          scrim: stage.scrim ? C.rgba(stage.scrim[0], stage.scrim[1]) : null, solid: C.hex(stage.solid), onSolid: C.hex(stage.onSolid) } : null,
      };
    };
    const notes = C.describe(theme).map(n => (n.kind === 'moved'
      ? `${n.from} похож на цвет состояния V2pn; V2pn возьмёт ${n.to}.`
      : n.kind === 'button'
        ? `${n.mode === 'dark' ? 'Тёмная' : 'Светлая'} тема: кнопки будут ${n.to} вместо ${n.from}, чтобы выделяться и читаться.`
        : `${n.mode === 'dark' ? 'Тёмная' : 'Светлая'} тема: там, где цвет бренда — текст, он будет ${n.to}.`));
    return { problems, notes, theme, colours: { light: derived('light'), dark: derived('dark') } };
  }

  /** The partner as its provider sees it. */
  partnerView(row) {
    const rights = Object.fromEntries(this.rightRows(row.id).map(r => [r.name, rightView(r)]));
    return {
      id: row.id,
      name: row.name,
      domains: parseJson(row.domains, []),
      state: row.state,
      downReason: row.state === 'down' ? row.down_reason : null,
      source: row.source,
      until: row.until,
      subscription: row.subscription,
      check: parseJson(row.check_json, null),
      checkedAt: row.checked_at,
      look: parseJson(row.look, null),
      rights,
      pending: Object.keys(rights).filter(k => rights[k].state === 'pending'),
      page: `${this.config.origin}/p/${row.id}/`,
      share: { page: `${this.config.origin}/p/${row.id}/`, button: `${this.config.origin}/p/${row.id}/#sub=` },
      createdAt: row.created_at,
      updatedAt: row.updated_at,
      publishedAt: row.published_at,
    };
  }

  /** GET /api/partner: the account's partner (null before the first save), the review of its look, the pictures. */
  myPartner(accountId) {
    const row = this.ownPartner(accountId);
    const look = row ? parseJson(row.look, defaultLook(row.name)) : defaultLook('');
    return {
      partner: row ? this.partnerView(row) : null,
      look,
      review: this.lookReview(look, accountId, row ? parseJson(row.domains, []) : undefined),
      pictures: [...this.pictureMap(accountId).values()].map(p => ({ sha256: p.sha256, slot: p.slot, width: p.width, height: p.height, size: p.size })),
      // What each picture slot takes, in the words the upload's refusal uses.
      slots: Object.fromEntries(PICTURES.map(key => [key, { hint: C.SLOTS[SLOT_OF[key]].desc[0], maxBytes: C.MAX_ASSET }])),
    };
  }

  /** Runs <fn> as one of at most two checks at a time (the server has one CPU). */
  async withCheckSlot(fn) {
    if (this.checksRunning >= 2) throw new HubError('check_busy');
    this.checksRunning++;
    try {
      return await fn();
    } finally {
      this.checksRunning--;
    }
  }

  runCheck(url) {
    return this.withCheckSlot(() => checkSubscription(url, { userAgent: this.config.checkUserAgent, net: this.net, tcp: this.net.tcp !== false }));
  }

  /** The subscription link of a request and the domain it gives; throws "invalid" under "subscription". */
  subscriptionOf(text) {
    const url = this.targetUrl(typeof text === 'string' ? text : '');
    if (!url) throw new HubError('invalid', { fields: { subscription: 'Нужна ссылка на подписку: https://…, до 2048 символов.' } });
    const domain = normalizeDomain(new URL(url).hostname);
    if (!domain) throw new HubError('invalid', { fields: { subscription: 'Ссылка должна быть на домене, а не на IP-адресе.' } });
    return { url, domain };
  }

  /**
   * The partner other than <id> that already claims one of <domains>, or null: a live one of the hub's, or one the
   * owner signs with the root key ({root: true}).
   */
  domainOwner(domains, id) {
    for (const p of this.db.all("SELECT id, name, domains FROM partners WHERE state = 'live' AND id != ?", id || '')) {
      if (domainsOverlap(domains, parseJson(p.domains, []))) return p;
    }
    const root = this.roots.owner(domains, id);
    return root ? { ...root, root: true } : null;
  }

  /** "domain_taken" for <domain>, which <owner> (as domainOwner gives it) claims. */
  domainTaken(owner, domain) {
    const message = owner.root
      ? `Домен ${domain} принадлежит партнёру ${owner.name}, которого V2pn подписал сам.`
      : `Домен ${domain} уже у другого партнёра.`;
    return new HubError('domain_taken', { message: owner.root ? message : undefined, fields: { subscription: message } });
  }

  /**
   * Hub partners in the way of a root partner, logged as warnings: one with a root partner's id that was not imported
   * from it, or one on a root partner's domain. V2pn would take the hub's and leave the root one out.
   */
  rootCollisions() {
    const found = [];
    for (const p of this.db.all('SELECT id, domains, source FROM partners ORDER BY id')) {
      const domains = parseJson(p.domains, []);
      if (this.holdsRootId(p)) found.push({ partner: p.id, root: p.id, why: 'id' });
      const root = this.roots.owner(domains, p.id);
      if (root) found.push({ partner: p.id, root: root.id, why: 'domain', clash: domainsOverlap(domains, root.domains) });
    }
    for (const c of found) log.warn('a hub partner collides with a root partner', c);
    return found;
  }

  /**
   * Whether the partner row <p> was made on the hub with the id of a root-signed partner (one made before the hub knew
   * that id). An imported partner with the id is that root partner itself (partners/configra) and holds nothing.
   */
  holdsRootId(p) {
    return p.source !== 'import' && this.roots.has(p.id);
  }

  /**
   * Takes down every partner of the hub that holds a root partner's id (publish(), before anything is built): V2pn
   * would show it instead of the root one. It is kept with its provider's work, not deleted; publish() leaves it out
   * of the index and removes its page, for /p/<id>/ is the root partner's address.
   */
  takeDownRootIds() {
    for (const p of this.db.all("SELECT id, source FROM partners WHERE state != 'down' ORDER BY id")) {
      if (!this.holdsRootId(p)) continue;
      const reason = `Id «${p.id}» принадлежит партнёру ${this.roots.get(p.id).name}, которого V2pn подписал сам. Напишите модераторам V2pn.`;
      this.db.tx(() => {
        this.db.run("UPDATE partners SET state = 'down', down_reason = ?, updated_at = ? WHERE id = ?", reason, nowIso(), p.id);
        this.db.audit('system', 'partner.down', p.id, { reason, root: p.id });
      });
      log.warn('a hub partner holds a root partner\'s id: taken down', { partner: p.id });
    }
  }

  /**
   * Reads the root-signed index again (startServer: at start and every six hours); the last good copy stays on failure.
   * A partner that now holds a root partner's id is taken down by a publication at once.
   */
  async refreshRoots() {
    const ok = await this.roots.refresh();
    if (ok) {
      this.rootCollisions();
      if (this.db.all("SELECT id, source FROM partners WHERE state != 'down'").some(p => this.holdsRootId(p))) this.publishSafely('system');
    }
    return ok;
  }

  /** POST /api/partner/check: a link checked without saving, with the domain the hub would take from it. */
  async check(accountId, input) {
    if (!this.limiter.hit(`check:${accountId}`, 20, HOUR)) throw new HubError('rate_limited');
    const { url, domain } = this.subscriptionOf(input?.subscription);
    const own = this.ownPartner(accountId);
    const check = await this.runCheck(url);
    return { check, domain, taken: Boolean(this.domainOwner([domain], own?.id)) };
  }

  freeId(name) {
    let base = idFromName(name);
    if (base.length < 2) base = 'partner';
    for (let i = 1; ; i++) {
      const id = i === 1 ? base : `${base.slice(0, 31 - String(i).length).replace(/-+$/, '')}-${i}`;
      if (validId(id) && !RESERVED_IDS.has(id) && !this.roots.has(id) && !this.db.get('SELECT 1 FROM partners WHERE id = ?', id)) return id;
    }
  }

  /**
   * PUT /api/partner: checks and saves the partner and publishes it at once. The subscription link is checked from
   * the server when it is new or changed; its host becomes the partner's domain (an imported partner keeps its own
   * domains while the link stays on them). Special rights wait for a moderator; what was approved stays out meanwhile.
   */
  async savePartner(accountId, input, actor) {
    if (!input || typeof input !== 'object' || Array.isArray(input)) throw new HubError('bad_request');
    const look = parseLook(input);
    const row = this.ownPartner(accountId);
    const { url, domain } = this.subscriptionOf(input.subscription === undefined && row ? row.subscription ?? '' : input.subscription);
    const kept = row ? parseJson(row.domains, []) : [];
    const domains = kept.length && domainAllowed(kept, url) ? kept : [domain];
    // Rights not sent stay as asked, but are checked again: the domain may have changed under them.
    const asked = input.rights !== undefined ? input.rights
      : row ? Object.fromEntries(this.rightRows(row.id).map(r => [r.name, JSON.parse(r.content)])) : null;
    const rights = parseRights(asked, { domains });
    const { problems } = this.lookReview(look, accountId, domains);
    if (Object.keys(problems).length) throw new HubError('look_invalid', { fields: problems });
    const taken = this.domainOwner(domains, row?.id);
    if (taken) throw this.domainTaken(taken, domain);

    let check = null;
    if (!row || row.subscription !== url || !row.check_json) {
      if (!this.limiter.hit(`check:${accountId}`, 20, HOUR)) throw new HubError('rate_limited');
      check = await this.runCheck(url);
      if (!check.ok) throw new HubError('check_failed', { detail: check });
    }

    const id = this.db.tx(() => {
      const now = nowIso();
      let current = this.ownPartner(accountId);
      const owner = this.domainOwner(domains, current?.id);
      if (owner) throw this.domainTaken(owner, domain);
      if (!current) {
        const newId = this.freeId(look.name);
        this.db.run(`INSERT INTO partners (id, account_id, name, domains, subscription, check_json, checked_at, look, state, source, created_at, updated_at)
          VALUES (?, ?, ?, ?, ?, ?, ?, ?, 'live', 'hub', ?, ?)`,
        newId, accountId, look.name, JSON.stringify(domains), url, JSON.stringify(check), now, JSON.stringify(look), now, now);
        current = this.partnerRow(newId);
        this.db.audit(actor, 'partner.create', newId, { domains });
      } else {
        this.db.run(`UPDATE partners SET name = ?, domains = ?, subscription = ?, look = ?, updated_at = ?,
          check_json = COALESCE(?, check_json), checked_at = COALESCE(?, checked_at) WHERE id = ?`,
        look.name, JSON.stringify(domains), url, JSON.stringify(look), now, check ? JSON.stringify(check) : null, check ? now : null, current.id);
        this.db.audit(actor, 'partner.save', current.id, { domains });
      }
      this.syncRights(current.id, rights, actor, now);
      return current.id;
    });
    this.publishSafely(actor);
    log.info('partner saved', { partner: id });
    return { ...this.myPartner(accountId), check };
  }

  /** Makes the rights table say what <rights> asks: a changed content waits again, a right not asked is dropped. */
  syncRights(partnerId, rights, actor, now) {
    const existing = new Map(this.rightRows(partnerId).map(r => [r.name, r]));
    for (const name of RIGHTS) {
      const content = rights[name];
      const old = existing.get(name);
      if (content === undefined) {
        if (old) {
          this.db.run('DELETE FROM rights WHERE partner_id = ? AND name = ?', partnerId, name);
          this.db.audit(actor, 'right.withdraw', partnerId, { right: name });
        }
        continue;
      }
      const hash = contentHash(name, content);
      if (old && old.hash === hash) continue;
      if (old) {
        this.db.run('UPDATE rights SET content = ?, hash = ?, requested_at = ? WHERE partner_id = ? AND name = ?', JSON.stringify(content), hash, now, partnerId, name);
      } else {
        this.db.run('INSERT INTO rights (partner_id, name, content, hash, requested_at) VALUES (?, ?, ?, ?, ?)', partnerId, name, JSON.stringify(content), hash, now);
      }
      this.db.audit(actor, 'right.request', partnerId, { right: name, hash });
    }
  }

  /** Stores a PNG for one picture slot of the account's look; {sha256, width, height, size}. */
  upload(accountId, slotKey, bytes) {
    if (!PICTURES.includes(slotKey)) throw new HubError('invalid', { fields: { slot: 'Слот: logo, logoOnLight, mark или backdrop.' } });
    if (!this.limiter.hit(`upload:${accountId}`, 60, HOUR)) throw new HubError('rate_limited');
    const slot = SLOT_OF[slotKey];
    if (bytes.length < 33 || !bytes.subarray(0, 8).equals(PNG_SIGNATURE) || bytes.toString('ascii', 12, 16) !== 'IHDR') {
      throw new HubError('invalid', { fields: { file: 'Нужен файл PNG.' } });
    }
    if (bytes.length > C.MAX_ASSET) throw new HubError('invalid', { fields: { file: 'Картинка больше 1 МБ. Сожмите её.' } });
    const [width, height] = C.imageSize(bytes);
    if (!C.SLOTS[slot].fits(width, height)) {
      throw new HubError('invalid', { fields: { file: `Картинка ${width}×${height}; нужен ${C.SLOTS[slot].desc[0]}.` }, detail: { width, height } });
    }
    const hash = sha256(bytes);
    this.storeUpload(hash, bytes);
    this.db.run(`INSERT INTO uploads (account_id, sha256, slot, width, height, size, created_at) VALUES (?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT (account_id, sha256) DO UPDATE SET slot = excluded.slot, created_at = excluded.created_at`,
    accountId, hash, slotKey, width, height, bytes.length, Date.now());
    return { sha256: hash, slot: slotKey, width, height, size: bytes.length };
  }

  storeUpload(hash, bytes) {
    const file = path.join(this.config.uploadsDir, `${hash}.png`);
    if (fs.existsSync(file)) return;
    const tmp = `${file}.${crypto.randomBytes(4).toString('hex')}.tmp`;
    fs.writeFileSync(tmp, bytes, { mode: 0o600 });
    fs.renameSync(tmp, file);
  }

  readUpload(hash) {
    if (!/^[0-9a-f]{64}$/.test(hash)) return null;
    try {
      return fs.readFileSync(path.join(this.config.uploadsDir, `${hash}.png`));
    } catch {
      return null;
    }
  }

  /** A picture for its own account, or for a moderator (any). */
  picture(session, hash, { mod = false } = {}) {
    if (!/^[0-9a-f]{64}$/.test(hash)) throw new HubError('not_found');
    const known = mod
      ? this.db.get('SELECT 1 FROM uploads WHERE sha256 = ?', hash)
      : this.db.get('SELECT 1 FROM uploads WHERE account_id = ? AND sha256 = ?', session.account.id, hash);
    const bytes = known ? this.readUpload(hash) : null;
    if (!bytes) throw new HubError('not_found');
    return bytes;
  }

  /** Calls the partner's login/start and one login/poll as V2pn would, under the same SSRF rules. */
  async loginTest(accountId, input) {
    const row = this.ownPartner(accountId);
    if (!row) throw new HubError('state', { message: 'Сначала сохраните партнёра.' });
    if (!this.limiter.hit(`login-test:${accountId}`, 10, HOUR)) throw new HubError('rate_limited');
    const domains = parseJson(row.domains, []);
    const asked = this.db.get("SELECT content FROM rights WHERE partner_id = ? AND name = 'account'", row.id);
    const api0 = this.targetUrl(input?.api ?? parseJson(asked?.content, {}).api ?? '', 512);
    if (!api0) throw new HubError('invalid', { fields: { api: 'Адрес API должен начинаться с https://.' } });
    if (!domainAllowed(domains, api0)) throw new HubError('invalid', { fields: { api: 'Адрес API должен быть на домене подписки.' } });
    const api = api0.endsWith('/') ? api0 : api0 + '/';
    const lines = [];
    const add = (id, level, text, detail = null) => lines.push({ id, level, text, detail });
    const ask = (url, options) => safeFetch(url, { ...this.net, ...options, maxBytes: 64 * 1024, timeoutMs: 8000, redirects: 0 });
    const json = buf => { try { return JSON.parse(buf.toString('utf8')); } catch { return null; } };
    let token = null;
    try {
      const res = await ask(api + 'login/start', {
        method: 'POST',
        headers: { 'content-type': 'application/json', 'user-agent': this.config.checkUserAgent },
        body: JSON.stringify({ device: 'V2pn Hub · проверка', app: 'V2pn Hub' }),
      });
      const body = res.status === 200 ? json(res.body) : null;
      if (!body) add('start', 'fail', 'login/start не отвечает как надо', `Ответ ${res.status}${res.status === 200 ? ', но не JSON' : ''}. Нужен 200 и JSON.`);
      else if (typeof body.code !== 'string' || !/^\d{6}$/.test(body.code)) add('start', 'fail', 'login/start: нет кода', 'Поле code — шесть цифр.');
      else if (typeof body.token !== 'string' || !/^[A-Za-z0-9_.~-]{32,256}$/.test(body.token)) add('start', 'fail', 'login/start: неверный token', 'Поле token — от 32 до 256 символов: латиница, цифры, _ . ~ -.');
      else {
        token = body.token;
        const bot = typeof body.url === 'string' && httpsUrl(body.url, 512) && (new URL(body.url).hostname === 't.me' || domainAllowed(domains, body.url)) ? body.url : null;
        const term = Number.isInteger(body.expires) ? body.expires : null;
        add('start', 'ok', 'login/start отвечает', `код 6 цифр${bot ? ', ссылка на бота' : ''}${term ? `, срок ${term} с` : ''}`);
        if (!bot) add('bot', 'warn', 'Нет ссылки на бота', 'Поле url — https на t.me или на домене подписки. Без него V2pn не покажет QR-код.');
      }
    } catch (e) {
      add('start', 'fail', 'login/start не открывается', e instanceof NetError ? e.message : 'Ошибка запроса.');
    }
    if (token) {
      try {
        const res = await ask(`${api}login/poll?token=${encodeURIComponent(token)}`, { headers: { 'user-agent': this.config.checkUserAgent } });
        const body = res.status === 200 ? json(res.body) : null;
        if (res.status === 404) add('poll', 'fail', 'login/poll не знает token', 'Только что выданный token должен отвечать «waiting», а не 404.');
        else if (!body || typeof body.state !== 'string') add('poll', 'fail', 'login/poll не отвечает как надо', 'Нужен 200 и JSON с полем state.');
        else if (body.state === 'done') add('poll', 'warn', 'login/poll сразу ответил «done»', 'До подтверждения в боте ответ должен быть «waiting».');
        else add('poll', 'ok', 'login/poll отвечает', `state: ${String(body.state).slice(0, 20)}`);
      } catch (e) {
        add('poll', 'fail', 'login/poll не открывается', e instanceof NetError ? e.message : 'Ошибка запроса.');
      }
    }
    return { ok: !lines.some(l => l.level === 'fail'), lines };
  }

  // ---------------------------------------------------------------- complaints

  /** POST /api/complaints: anonymous; nothing about the person is kept, the address only counts against the limit. */
  complain(input, ip) {
    const id = typeof input?.partner === 'string' ? input.partner.trim().toLowerCase() : '';
    const text = multiLine(input?.text ?? '', 1000);
    if (text == null) throw new HubError('invalid', { fields: { text: 'До 1000 символов.' } });
    if (!validId(id) || !this.db.get("SELECT 1 FROM partners WHERE id = ? AND state = 'live'", id)) throw new HubError('not_found');
    const network = ipKey(ip);
    if (!this.limiter.hit(`complaint-ip:${network}`, 10, HOUR)) throw new HubError('rate_limited');
    if (!this.limiter.hit(`complaint:${network}:${id}`, 1, 24 * HOUR)) throw new HubError('rate_limited', { message: 'Вы уже пожаловались. Спасибо.' });
    this.db.run('INSERT INTO complaints (partner_id, text, created_at) VALUES (?, ?, ?)', id, text, nowIso());
    log.info('complaint', { partner: id });
    return { ok: true };
  }

  // ---------------------------------------------------------------- moderation

  checkSummary(row) {
    const check = parseJson(row.check_json, null);
    if (!check) return null;
    return { ok: check.ok, at: check.at, servers: check.servers?.total ?? 0, supported: check.servers?.supported ?? 0, title: check.headers?.title ?? null };
  }

  modView(row) {
    const look = parseJson(row.look, {});
    const rights = Object.fromEntries(this.rightRows(row.id).map(r => [r.name, rightState(r)]));
    return {
      id: row.id,
      name: row.name,
      login: row.login,
      domains: parseJson(row.domains, []),
      state: row.state,
      downReason: row.state === 'down' ? row.down_reason : null,
      source: row.source,
      until: row.until,
      expired: Boolean(row.until && row.until <= today()),
      complaints: row.complaints,
      layout: look.layout ?? 'v2pn',
      brand: look.brand ?? [],
      pictures: { logo: look.logo ?? null, logoOnLight: look.logoOnLight ?? null, mark: look.mark ?? null },
      rights,
      pending: Object.keys(rights).filter(k => rights[k] === 'pending'),
      check: this.checkSummary(row),
      page: `${this.config.origin}/p/${row.id}/`,
      createdAt: row.created_at,
      updatedAt: row.updated_at,
      publishedAt: row.published_at,
    };
  }

  modRow(id) {
    const row = this.db.get(`SELECT p.*, a.login, (SELECT COUNT(*) FROM complaints c WHERE c.partner_id = p.id) AS complaints
      FROM partners p JOIN accounts a ON a.id = p.account_id WHERE p.id = ?`, id);
    if (!row) throw new HubError('not_found');
    return row;
  }

  /** Every partner, newest first. */
  listPartners() {
    return this.db.all(`SELECT p.*, a.login, (SELECT COUNT(*) FROM complaints c WHERE c.partner_id = p.id) AS complaints
      FROM partners p JOIN accounts a ON a.id = p.account_id ORDER BY p.created_at DESC, p.id`).map(r => this.modView(r));
  }

  /** One partner with its look, the content of its rights and its complaints. */
  modPartner(id) {
    const row = this.modRow(id);
    return {
      ...this.modView(row),
      look: parseJson(row.look, null),
      rightDetails: Object.fromEntries(this.rightRows(id).map(r => [r.name, rightView(r)])),
      complaintList: this.complaintsOf(id),
    };
  }

  takeDown(id, input, actor) {
    const reason = oneLine(input?.reason ?? '', 500);
    if (!reason) throw new HubError('invalid', { fields: { reason: 'Напишите причину: до 500 символов.' } });
    const row = this.partnerRow(id);
    if (row.state === 'down') throw new HubError('state', { message: 'Уже снят.' });
    this.db.run("UPDATE partners SET state = 'down', down_reason = ?, updated_at = ? WHERE id = ?", reason, nowIso(), id);
    this.db.audit(actor, 'partner.down', id, { reason });
    this.publishSafely(actor);
    return this.modView(this.modRow(id));
  }

  restore(id, actor) {
    const row = this.partnerRow(id);
    if (row.state !== 'down') throw new HubError('state', { message: 'Партнёр не снят.' });
    if (this.holdsRootId(row)) {
      throw new HubError('state', { message: `Его id у партнёра ${this.roots.get(id).name}, которого V2pn подписал сам. Такой партнёр не возвращается.` });
    }
    const owner = this.domainOwner(parseJson(row.domains, []), id);
    if (owner) {
      throw new HubError('domain_taken', { message: owner.root ? `Его домен у партнёра ${owner.name}, которого V2pn подписал сам.` : 'Его домен уже у другого партнёра.' });
    }
    this.db.run("UPDATE partners SET state = 'live', down_reason = NULL, updated_at = ? WHERE id = ?", nowIso(), id);
    this.db.audit(actor, 'partner.restore', id);
    this.publishSafely(actor);
    return this.modView(this.modRow(id));
  }

  /** Every request waiting for a decision, oldest first, with what is approved now. */
  rightsQueue() {
    return this.db.all(`SELECT r.*, p.name AS partner_name, a.login FROM rights r JOIN partners p ON p.id = r.partner_id
      JOIN accounts a ON a.id = p.account_id ORDER BY r.requested_at, r.partner_id, r.name`)
      .filter(r => rightState(r) === 'pending')
      .map(r => ({ partner: r.partner_id, partnerName: r.partner_name, login: r.login, right: r.name, ...rightView(r) }));
  }

  /**
   * Approves or denies a right. <input.hash> must be the content the moderator saw: a request changed meanwhile is
   * not decided blind. Denying an approved right takes it back.
   */
  decideRight(id, name, approve, input, actor) {
    if (!RIGHTS.includes(name)) throw new HubError('not_found');
    const view = this.db.tx(() => {
      const row = this.db.get('SELECT * FROM rights WHERE partner_id = ? AND name = ?', id, name);
      if (!row) throw new HubError('not_found');
      if (input?.hash !== row.hash) throw new HubError('conflict', { message: 'Провайдер изменил запрос. Посмотрите его ещё раз.' });
      const now = nowIso();
      if (approve) {
        if (rightState(row) === 'approved') throw new HubError('state', { message: 'Уже одобрено.' });
        this.db.run(`UPDATE rights SET approved = content, approved_hash = hash, denied_hash = NULL, reason = NULL, decided_at = ?, decided_by = ?
          WHERE partner_id = ? AND name = ?`, now, actor, id, name);
        this.db.audit(actor, 'right.approve', id, { right: name, hash: row.hash });
      } else {
        const reason = oneLine(input?.reason ?? '', 500);
        if (!reason) throw new HubError('invalid', { fields: { reason: 'Напишите причину: до 500 символов.' } });
        if (rightState(row) === 'denied') throw new HubError('state', { message: 'Уже отказано.' });
        const revoke = row.approved_hash === row.hash;
        this.db.run(`UPDATE rights SET denied_hash = hash, reason = ?, decided_at = ?, decided_by = ?,
          approved = CASE WHEN ? THEN NULL ELSE approved END, approved_hash = CASE WHEN ? THEN NULL ELSE approved_hash END
          WHERE partner_id = ? AND name = ?`, reason, now, actor, revoke ? 1 : 0, revoke ? 1 : 0, id, name);
        this.db.audit(actor, 'right.deny', id, { right: name, hash: row.hash, reason, revoked: revoke });
      }
      return rightView(this.db.get('SELECT * FROM rights WHERE partner_id = ? AND name = ?', id, name));
    });
    this.publishSafely(actor);
    return view;
  }

  complaintsOf(id) {
    return this.db.all('SELECT id, text, created_at FROM complaints WHERE partner_id = ? ORDER BY id DESC LIMIT 200', id)
      .map(c => ({ id: c.id, text: c.text, createdAt: c.created_at }));
  }

  clearComplaints(id, actor) {
    this.partnerRow(id);
    const n = this.db.run('DELETE FROM complaints WHERE partner_id = ?', id).changes;
    this.db.audit(actor, 'complaints.clear', id, { count: Number(n) });
    return { cleared: Number(n) };
  }

  /**
   * A new password for an account, shown once. Moderators reset providers'; the owner also moderators'. An owner's
   * password is changed on the server only (hub.mjs reset-password).
   */
  resetPassword(session, loginText, actor) {
    const account = this.auth.accountByLogin(normalizeLogin(loginText));
    if (!account) throw new HubError('not_found');
    if (account.role === 'owner') throw new HubError('forbidden', { message: 'Пароль владельца меняется только на сервере.' });
    if (account.role === 'moderator' && !session.owner) throw new HubError('forbidden', { message: 'Пароль модератора сбрасывает только владелец.' });
    const password = this.auth.resetPassword(account.id);
    this.db.audit(actor, 'account.password', account.login);
    return { login: account.login, password };
  }

  moderators() {
    return this.db.all("SELECT login, role, created_at, last_login_at FROM accounts WHERE role IN ('owner', 'moderator') ORDER BY role = 'moderator', login")
      .map(a => ({ login: a.login, role: a.role, createdAt: a.created_at, lastLoginAt: a.last_login_at }));
  }

  /** Makes an account a moderator; a login not taken yet gets a new account with a password shown once. */
  addModerator(loginText, actor) {
    const login = normalizeLogin(loginText);
    if (!login) throw new HubError('invalid', { fields: { login: 'Укажите логин.' } });
    let password = null;
    this.db.tx(() => {
      const account = this.auth.accountByLogin(login);
      if (account) {
        if (account.role === 'owner') throw new HubError('state', { message: 'Это владелец.' });
        if (account.role === 'moderator') throw new HubError('state', { message: 'Уже модератор.' });
        this.db.run("UPDATE accounts SET role = 'moderator' WHERE id = ?", account.id);
      } else {
        if (!validLogin(login)) throw new HubError('invalid', { fields: { login: 'Логин: от 3 до 32 латинских букв, цифр и знаков . _ -' } });
        const id = Number(this.db.run("INSERT INTO accounts (login, role, created_at) VALUES (?, 'moderator', ?)", login, nowIso()).lastInsertRowid);
        password = this.auth.resetPassword(id);
      }
      this.db.audit(actor, 'moderator.add', login);
    });
    return { login, role: 'moderator', password };
  }

  removeModerator(loginText, actor) {
    const account = this.auth.accountByLogin(normalizeLogin(loginText));
    if (!account || account.role !== 'moderator') throw new HubError('not_found', { message: 'Такого модератора нет.' });
    this.db.run("UPDATE accounts SET role = 'provider' WHERE id = ?", account.id);
    // Their open sessions carried the role; they sign in again as a provider.
    this.db.run('DELETE FROM sessions WHERE account_id = ?', account.id);
    this.db.audit(actor, 'moderator.remove', account.login);
    return { login: account.login, role: 'provider' };
  }

  auditLog(limit = 200) {
    return this.db.all('SELECT id, at, actor, action, target, detail FROM audit ORDER BY id DESC LIMIT ?', Math.min(Math.max(1, limit | 0), 1000))
      .map(r => ({ ...r, detail: parseJson(r.detail, null) }));
  }

  overview() {
    return {
      partners: this.db.all('SELECT state, COUNT(*) AS n FROM partners GROUP BY state'),
      pendingRights: this.rightsQueue().length,
      complaints: this.db.get('SELECT COUNT(*) AS n FROM complaints').n,
      publication: this.publicationState(),
    };
  }

  // ---------------------------------------------------------------- publication

  /**
   * The partners as the publication takes them, with only the approved content of their rights. One that holds a root
   * partner's id is not among them: no pack, and no page either (the one it had is removed).
   */
  publicationPartners() {
    const approved = new Map();
    for (const r of this.db.all('SELECT partner_id, name, approved FROM rights WHERE approved_hash IS NOT NULL')) {
      if (!approved.has(r.partner_id)) approved.set(r.partner_id, {});
      approved.get(r.partner_id)[r.name] = JSON.parse(r.approved);
    }
    return this.db.all('SELECT * FROM partners ORDER BY id').filter(p => !this.holdsRootId(p)).map(p => ({
      id: p.id, name: p.name, domains: parseJson(p.domains, []), until: p.until, state: p.state, look: parseJson(p.look, defaultLook(p.name)),
      approved: approved.get(p.id) ?? {},
    }));
  }

  /** Rebuilds and re-signs everything; throws with the problems when nothing could be written. */
  publish(actor = 'system') {
    this.takeDownRootIds();
    const publication = buildPublication(this.publicationPartners(), {
      readUpload: h => this.readUpload(h), today: today(), published: publishedStamp(), origin: this.config.origin, repo: this.config.githubRepo,
    });
    if (publication.problems.length) {
      this.lastPublishError = { at: nowIso(), problems: publication.problems };
      log.error('publish refused', { problems: publication.problems });
      throw new HubError('publish_failed', { detail: publication.problems });
    }
    for (const note of publication.notes) log.warn('published without', note);
    const previous = parseJson(this.db.meta('pages'), []);
    const pages = writePublication(this.config.publicDir, publication, this.key, previous);
    const now = nowIso();
    const live = publication.built.map(b => b.listing.id);
    this.db.tx(() => {
      for (const id of live) this.db.run('UPDATE partners SET published_at = ? WHERE id = ?', now, id);
      this.db.setMeta('pages', JSON.stringify(pages));
      this.db.setMeta('published', JSON.stringify({ at: now, partners: live, index: sha256(publication.index) }));
      this.db.setMeta('published_day', today());
    });
    this.lastPublishError = null;
    log.info('published', { partners: live.length, actor });
    return { partners: live, down: publication.down.map(d => d.id), at: now };
  }

  /** publish() after a change: the change stands even when the files could not be written (moderators see why). */
  publishSafely(actor) {
    try {
      return this.publish(actor);
    } catch (e) {
      if (!(e instanceof HubError)) {
        this.lastPublishError = { at: nowIso(), problems: [String(e.message || e)] };
        log.error('publish failed', { error: String(e.message || e) });
      }
      return null;
    }
  }

  publicationState() {
    return { last: parseJson(this.db.meta('published'), null), error: this.lastPublishError };
  }

  // ---------------------------------------------------------------- import (CLI)

  /**
   * Takes partners/<id> (listing.json, partner.json, PNG pictures, routing.json) as a live partner with the rights
   * its listing grants already approved: the owner reviewed it on his PC. Its account is <login> (default: the id),
   * made without a password when new. transport and core stay with the root-signed index.
   */
  importPartner(folder, { login } = {}) {
    const read = name => JSON.parse(fs.readFileSync(path.join(folder, name), 'utf8'));
    const listing = read('listing.json');
    const pack = read('partner.json');
    const notes = [];
    if (!validId(listing.id) || listing.id !== path.basename(path.resolve(folder))) throw new Error('listing.json: id must be the folder name');
    if (pack.id !== listing.id) throw new Error('partner.json belongs to another id');
    const name = validName(listing.name);
    if (!name) throw new Error('listing.json: name is not valid');
    const domains = (listing.domains || []).map(normalizeDomain);
    if (!domains.length || domains.some(d => !d)) throw new Error('listing.json: a domain is not valid');
    const until = listing.until;
    if (!/^\d{4}-\d{2}-\d{2}$/.test(until || '') || until <= today()) throw new Error('listing.json: until is not a future yyyy-MM-dd date');
    const grants = new Set(listing.grants || []);
    for (const g of [...grants].filter(x => ROOT_ONLY_GRANTS.has(x))) notes.push(`grant ${g} left out: it needs the owner's root-signed index`);
    const known = new Set(['schema', 'id', 'name', 'brand', 'theme', 'api', 'login', 'links', 'actions', 'lock', 'assets']);
    const unknown = Object.keys(pack).filter(k => !known.has(k));
    if (unknown.length) throw new Error(`partner.json has fields the hub does not edit: ${unknown.join(', ')}`);

    const brand = (pack.theme?.palette?.brand ?? (pack.brand?.accent ? [pack.brand.accent] : [])).map(h => String(h).toUpperCase());
    const bg = pack.theme?.background;
    let background = 'none';
    if (bg?.type === 'gradient') {
      background = 'gradient';
      const want = JSON.stringify((bg.colors || []).map(h => String(h).toUpperCase()));
      if (JSON.stringify(themeOf({ ...defaultLook(name), brand, background }).background.colors) !== want) {
        throw new Error('theme.background: the hub makes the gradient from the brand colours; these colours differ');
      }
    } else if (bg?.type === 'image') {
      background = 'image';
    }
    const files = new Map((pack.assets || []).map(a => [a.name, a]));
    const fileOf = (file, what) => {
      const asset = files.get(file);
      if (!asset) throw new Error(`${file} is not in partner.json's assets`);
      const bytes = fs.readFileSync(path.join(folder, file));
      if (bytes.length !== asset.size || sha256(bytes) !== asset.sha256) throw new Error(`${file} does not match partner.json's assets (${what})`);
      return bytes;
    };
    const pictureOf = (file, slotKey) => {
      if (!file) return null;
      if (!/\.png$/i.test(file)) throw new Error(`${file}: the hub takes PNG pictures only`);
      return { slotKey, bytes: fileOf(file, 'picture') };
    };
    const pics = [
      pictureOf(pack.brand?.logo, 'logo'), pictureOf(pack.brand?.logoOnLight, 'logoOnLight'), pictureOf(pack.brand?.mark, 'mark'),
      background === 'image' ? pictureOf(bg.art, 'backdrop') : null,
    ].filter(Boolean);
    const look = parseLook({
      name: pack.name ?? name, brand, background, layout: pack.theme?.layout ?? 'v2pn',
      links: { cabinet: pack.links?.cabinet ?? '', support: pack.links?.support ?? '' },
    });
    for (const p of pics) look[p.slotKey] = sha256(p.bytes);

    const asked = {};
    if (grants.has('account') && pack.api) asked.account = { api: pack.api, login: pack.login === true };
    if (grants.has('actions') && pack.actions?.length) {
      asked.actions = pack.actions.map(a => ({ label: a.label, icon: a.icon, do: a.do, strategy: a.strategy, purpose: a.purpose, server: a.server, group: a.group, url: a.url }));
      if (pack.actions.some(a => a.scope)) notes.push('action scopes are not kept: the hub does not edit them');
    }
    if (grants.has('lock') && pack.lock === true) asked.lock = true;
    if (files.has('routing.json')) {
      if (grants.has('routing')) asked.routing = parseRouting(JSON.parse(fileOf('routing.json', 'routing').toString('utf8')));
      else notes.push('routing.json is not used: listing.json has no routing grant');
    }
    for (const g of ['account', 'actions', 'lock', 'routing']) if (grants.has(g) && !asked[g]) notes.push(`grant ${g} does nothing: the pack does not use it`);
    let rights;
    try {
      rights = parseRights(asked, { domains });
    } catch (e) {
      throw new Error(`the rights do not pass: ${JSON.stringify(e.fields || e.message)}`);
    }

    const now = nowIso();
    const result = this.db.tx(() => {
      const existing = this.db.get('SELECT * FROM partners WHERE id = ?', listing.id);
      if (existing && existing.source !== 'import') throw new Error(`${listing.id} is a partner made on the hub; not replaced`);
      const owner = this.domainOwner(domains, listing.id);
      if (owner) throw new Error(`a domain of ${listing.id} belongs to ${owner.root ? 'the root-signed partner' : 'another live partner'} ${owner.id}`);
      let accountId = existing?.account_id;
      if (login || !accountId) {
        const wanted = normalizeLogin(login ?? listing.id);
        if (!wanted) throw new Error(`${login} is not a login`);
        const account = this.auth.accountByLogin(wanted);
        if (account) accountId = account.id;
        else accountId = Number(this.db.run("INSERT INTO accounts (login, role, created_at) VALUES (?, 'provider', ?)", wanted, now).lastInsertRowid);
        const other = this.db.get('SELECT id FROM partners WHERE account_id = ? AND id != ?', accountId, listing.id);
        if (other) throw new Error(`${wanted} already has the partner ${other.id}`);
      }
      for (const p of pics) {
        const hash = sha256(p.bytes);
        this.storeUpload(hash, p.bytes);
        const [w, h] = C.imageSize(p.bytes);
        this.db.run(`INSERT INTO uploads (account_id, sha256, slot, width, height, size, created_at) VALUES (?, ?, ?, ?, ?, ?, ?)
          ON CONFLICT (account_id, sha256) DO UPDATE SET slot = excluded.slot`, accountId, hash, p.slotKey, w, h, p.bytes.length, Date.now());
      }
      const problems = lookProblems(look, { pictures: this.pictureMap(accountId), domains });
      if (Object.keys(problems).length) throw new Error(`the look does not pass: ${JSON.stringify(problems)}`);
      if (existing) {
        this.db.run(`UPDATE partners SET account_id = ?, name = ?, domains = ?, look = ?, state = 'live', down_reason = NULL, until = ?, updated_at = ?
          WHERE id = ?`, accountId, name, JSON.stringify(domains), JSON.stringify(look), until, now, listing.id);
      } else {
        this.db.run(`INSERT INTO partners (id, account_id, name, domains, look, state, source, until, created_at, updated_at)
          VALUES (?, ?, ?, ?, ?, 'live', 'import', ?, ?, ?)`, listing.id, accountId, name, JSON.stringify(domains), JSON.stringify(look), until, now, now);
      }
      this.db.run('DELETE FROM rights WHERE partner_id = ?', listing.id);
      for (const [right, content] of Object.entries(rights)) {
        const text = JSON.stringify(content);
        const hash = contentHash(right, content);
        this.db.run(`INSERT INTO rights (partner_id, name, content, hash, approved, approved_hash, requested_at, decided_at, decided_by)
          VALUES (?, ?, ?, ?, ?, ?, ?, ?, 'cli')`, listing.id, right, text, hash, text, hash, now, now);
      }
      this.db.audit('cli', 'partner.import', listing.id, { until, rights: Object.keys(rights) });
      return { login: this.db.get('SELECT login FROM accounts WHERE id = ?', accountId).login };
    });
    const out = ['edition', ...Object.keys(rights)];
    return { id: listing.id, login: result.login, grants: GRANT_ORDER.filter(g => out.includes(g)), notes };
  }

  // ---------------------------------------------------------------- housekeeping

  /**
   * Whether the index is due to be signed and written again: not yet today, or the last one is a day old. Every
   * publication moves every listing's term 60 days on, an imported partner's no further than its own (listingUntil).
   */
  publishDue(now = Date.now()) {
    if (this.db.meta('published_day') !== today()) return true;
    const at = Date.parse(parseJson(this.db.meta('published'), null)?.at ?? '');
    return !(at > now - 24 * HOUR);
  }

  /**
   * Every ten minutes and right after start (startServer): expired sessions out, counters in; once a day a fresh index
   * (terms run on), retried at the next round while it fails.
   */
  maintain() {
    try {
      this.auth.sweep();
      this.counter.flush();
      if (this.publishDue()) {
        const due = this.db.get('SELECT 1 FROM partners LIMIT 1');
        if (due || this.db.meta('published_day')) this.publishSafely('system');
        this.collectUploads();
      }
    } catch (e) {
      log.error('maintenance failed', { error: String(e.message || e) });
    }
  }

  /** Pictures no look uses any more, a day after their upload, and files no account holds. */
  collectUploads() {
    const used = new Set();
    for (const row of this.db.all('SELECT account_id, look FROM partners')) {
      const look = parseJson(row.look, null);
      if (look) for (const key of PICTURES) if (look[key]) used.add(`${row.account_id}|${look[key]}`);
    }
    const dayAgo = Date.now() - 24 * HOUR;
    for (const u of this.db.all('SELECT account_id, sha256, created_at FROM uploads')) {
      if (!used.has(`${u.account_id}|${u.sha256}`) && u.created_at < dayAgo) this.db.run('DELETE FROM uploads WHERE account_id = ? AND sha256 = ?', u.account_id, u.sha256);
    }
    const kept = new Set(this.db.all('SELECT DISTINCT sha256 FROM uploads').map(r => r.sha256));
    for (const f of fs.readdirSync(this.config.uploadsDir)) {
      const m = /^([0-9a-f]{64})\.png$/.exec(f);
      const stale = m ? !kept.has(m[1]) : /\.tmp$/.test(f);
      if (stale) {
        const file = path.join(this.config.uploadsDir, f);
        if (fs.statSync(file).mtimeMs < dayAgo) fs.rmSync(file, { force: true });
      }
    }
  }
}
