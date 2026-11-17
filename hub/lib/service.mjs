// What the hub does, apart from HTTP: applications and their checks, the owner's decisions, the provider's cabinet
// (look, pictures, sign-in API test), publication, import of a partners/<id> folder and the housekeeping timers.
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { Auth, RateLimiter } from './auth.mjs';
import { checkSubscription } from './check.mjs';
import { colors as C } from './colors.mjs';
import { DownloadCounter } from './downloads.mjs';
import { HubError, assertFields } from './errors.mjs';
import { log } from './log.mjs';
import { defaultLook, lookProblems, parseLook, themeOf, PICTURES, SLOT_OF, grantsFor, FILE } from './look.mjs';
import { NetError, safeFetch } from './net.mjs';
import { pageTheme } from './page.mjs';
import { buildPublication, publishedStamp, writePublication, REVIEWED_GRANTS } from './publish.mjs';
import {
  RESERVED_IDS, domainAllowed, domainsOverlap, httpsUrl, multiLine, normalizeDomain, oneLine, validId, validName,
} from './validate.mjs';

const HOUR = 3600 * 1000;
const sha256 = buf => crypto.createHash('sha256').update(buf).digest('hex');
const nowIso = () => new Date().toISOString();
const today = () => new Date().toISOString().slice(0, 10);
const addYears = (n) => { const d = new Date(); d.setUTCFullYear(d.getUTCFullYear() + n); return d.toISOString().slice(0, 10); };
const parseJson = (text, fallback) => { try { return text ? JSON.parse(text) : fallback; } catch { return fallback; } };

const PNG_SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

export class Hub {
  /**
   * <net> is passed to every outgoing request (tests give their resolver and allow-list); <mailer> is
   * ({to, subject, text}) => Promise, or null for the no-mail mode.
   */
  constructor({ config, db, key, secret, mailer = null, net = {} }) {
    this.config = config;
    this.db = db;
    this.key = key;
    this.net = net;
    this.limiter = new RateLimiter();
    this.auth = new Auth({ db, config, secret, mailer, limiter: this.limiter });
    this.counter = new DownloadCounter(db);
    this.checksRunning = 0;
    this.lastPublishError = null;
    fs.mkdirSync(config.uploadsDir, { recursive: true, mode: 0o700 });
    // A check cut short by a restart leaves its application editable again.
    db.run("UPDATE applications SET state = 'draft' WHERE state = 'checking'");
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

  // ---------------------------------------------------------------- applications (provider)

  applicationView(row, { withAccount = false } = {}) {
    if (!row) return null;
    const view = {
      id: row.id,
      providerId: row.provider_id,
      name: row.name,
      domains: parseJson(row.domains, []),
      testUrl: row.test_url,
      contact: row.contact,
      wantedTier: row.wanted_tier,
      note: row.note,
      state: row.state,
      rejectReason: row.reject_reason,
      check: parseJson(row.check_json, null),
      checkedAt: row.checked_at,
      createdAt: row.created_at,
      updatedAt: row.updated_at,
      submittedAt: row.submitted_at,
      decidedAt: row.decided_at,
    };
    if (withAccount) view.email = row.email;
    return view;
  }

  getApplication(accountId) {
    return this.applicationView(this.db.get('SELECT * FROM applications WHERE account_id = ?', accountId));
  }

  /** The fields of an application from a request body; every present field checked, missing ones left as they are. */
  parseApplication(input, { complete }) {
    if (!input || typeof input !== 'object' || Array.isArray(input)) throw new HubError('bad_request');
    const f = {};
    const out = {};
    if (input.name !== undefined || complete) {
      const name = validName(input.name ?? '');
      if (!name) f.name = 'Название от 1 до 40 символов, без «V2pn».';
      out.name = name ?? '';
    }
    if (input.providerId !== undefined || complete) {
      const id = typeof input.providerId === 'string' ? input.providerId.trim().toLowerCase() : '';
      if (!validId(id)) f.providerId = 'id: от 2 до 32 латинских букв, цифр и дефисов, с буквы или цифры.';
      else if (RESERVED_IDS.has(id)) f.providerId = 'Этот id занят V2pn. Выберите другой.';
      out.providerId = id;
    }
    if (input.domains !== undefined || complete) {
      const list = Array.isArray(input.domains) ? input.domains : typeof input.domains === 'string' ? input.domains.split(/[\s,]+/) : null;
      const domains = [];
      if (!list) f.domains = 'Список доменов.';
      else {
        for (const d of list.slice(0, 50)) {
          if (typeof d !== 'string' || !d.trim()) continue;
          const n = normalizeDomain(d);
          if (!n) { f.domains = `«${String(d).slice(0, 60)}» — не домен (example.com или *.example.com).`; break; }
          if (!domains.includes(n)) domains.push(n);
        }
        if (!f.domains && domains.length === 0) f.domains = 'Нужен хотя бы один домен подписки.';
        if (domains.length > 20) f.domains = 'Не больше 20 доменов.';
      }
      out.domains = domains;
    }
    if (input.testUrl !== undefined || complete) {
      const url = input.testUrl ? this.targetUrl(input.testUrl) : null;
      if (!url && (complete || input.testUrl)) f.testUrl = 'Ссылка должна начинаться с https:// и быть не длиннее 2048 символов.';
      out.testUrl = url ?? '';
    }
    if (input.contact !== undefined || complete) {
      const contact = oneLine(input.contact ?? '', 200);
      if (contact == null || (complete && !contact)) f.contact = 'Контакт для связи: до 200 символов в одну строку.';
      out.contact = contact ?? '';
    }
    if (input.wantedTier !== undefined || complete) {
      const tier = Number(input.wantedTier);
      if (tier !== 1 && tier !== 2) f.wantedTier = 'Ступень 1 или 2.';
      out.wantedTier = tier === 2 ? 2 : 1;
    }
    if (input.note !== undefined) {
      const note = multiLine(input.note, 2000);
      if (note == null) f.note = 'До 2000 символов.';
      out.note = note ?? '';
    }
    assertFields(f);
    return out;
  }

  assertIdFree(providerId, accountId) {
    if (!providerId) return;
    const app = this.db.get('SELECT account_id FROM applications WHERE provider_id = ?', providerId);
    const provider = this.db.get('SELECT account_id FROM providers WHERE id = ?', providerId);
    if ((app && app.account_id !== accountId) || (provider && provider.account_id !== accountId)) {
      throw new HubError('id_taken', { fields: { providerId: 'Такой id уже занят.' } });
    }
  }

  assertDomainsFree(domains, providerId) {
    for (const p of this.db.all("SELECT id, domains FROM providers WHERE state = 'active' AND id != ?", providerId || '')) {
      const clash = domainsOverlap(domains, parseJson(p.domains, []));
      if (clash) throw new HubError('domain_taken', { fields: { domains: `Домен ${clash.split(' / ')[0]} уже у другого провайдера.` } });
    }
  }

  saveApplication(accountId, input) {
    const fields = this.parseApplication(input, { complete: false });
    return this.db.tx(() => {
      if (this.db.get('SELECT 1 FROM providers WHERE account_id = ?', accountId)) throw new HubError('state', { message: 'Вы уже провайдер V2pn: всё в кабинете.' });
      const row = this.db.get('SELECT * FROM applications WHERE account_id = ?', accountId);
      if (row && !['draft', 'rejected'].includes(row.state)) throw new HubError('state', { message: 'Заявка на рассмотрении: менять её сейчас нельзя.' });
      this.assertIdFree(fields.providerId, accountId);
      const now = nowIso();
      if (!row) {
        this.db.run(`INSERT INTO applications (account_id, provider_id, name, domains, test_url, contact, wanted_tier, note, state, created_at, updated_at)
          VALUES (?, ?, ?, ?, ?, ?, ?, ?, 'draft', ?, ?)`,
        accountId, fields.providerId || null, fields.name ?? '', JSON.stringify(fields.domains ?? []), fields.testUrl ?? '', fields.contact ?? '',
        fields.wantedTier ?? 1, fields.note ?? '', now, now);
      } else {
        const merged = {
          provider_id: fields.providerId !== undefined ? fields.providerId || null : row.provider_id,
          name: fields.name ?? row.name,
          domains: fields.domains ? JSON.stringify(fields.domains) : row.domains,
          test_url: fields.testUrl ?? row.test_url,
          contact: fields.contact ?? row.contact,
          wanted_tier: fields.wantedTier ?? row.wanted_tier,
          note: fields.note ?? row.note,
        };
        this.db.run(`UPDATE applications SET provider_id = ?, name = ?, domains = ?, test_url = ?, contact = ?, wanted_tier = ?, note = ?,
          state = 'draft', updated_at = ? WHERE id = ?`,
        merged.provider_id, merged.name, merged.domains, merged.test_url, merged.contact, merged.wanted_tier, merged.note, now, row.id);
      }
      return this.getApplication(accountId);
    });
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

  runCheck(url, domains) {
    return this.withCheckSlot(() => checkSubscription(url, { domains, userAgent: this.config.checkUserAgent, net: this.net, tcp: this.net.tcp !== false }));
  }

  /** POST /api/check: the test link against the domains, without saving anything. */
  async check(accountId, input) {
    if (!this.limiter.hit(`check:${accountId}`, 20, HOUR)) throw new HubError('rate_limited');
    const url = this.targetUrl(input?.url ?? '');
    if (!url) throw new HubError('invalid', { fields: { url: 'Ссылка должна начинаться с https://.' } });
    const domains = Array.isArray(input?.domains) ? input.domains.map(normalizeDomain).filter(Boolean).slice(0, 20) : [];
    return this.runCheck(url, domains);
  }

  async submitApplication(accountId) {
    if (!this.limiter.hit(`check:${accountId}`, 20, HOUR)) throw new HubError('rate_limited');
    const row = this.db.tx(() => {
      const r = this.db.get('SELECT * FROM applications WHERE account_id = ?', accountId);
      if (!r) throw new HubError('not_found', { message: 'Сначала заполните заявку.' });
      if (!['draft', 'rejected'].includes(r.state)) throw new HubError('state', { message: 'Заявка уже отправлена.' });
      const view = this.applicationView(r);
      this.parseApplication({ ...view, note: view.note }, { complete: true });
      this.assertIdFree(r.provider_id, accountId);
      this.assertDomainsFree(view.domains, r.provider_id);
      this.db.run("UPDATE applications SET state = 'checking', updated_at = ? WHERE id = ?", nowIso(), r.id);
      return r;
    });
    let result;
    try {
      result = await this.runCheck(row.test_url, parseJson(row.domains, []));
    } catch (e) {
      this.db.run("UPDATE applications SET state = 'draft' WHERE id = ?", row.id);
      throw e;
    }
    const passed = result.ok;
    const now = nowIso();
    this.db.run('UPDATE applications SET check_json = ?, checked_at = ?, state = ?, submitted_at = ?, reject_reason = ?, updated_at = ? WHERE id = ?',
      JSON.stringify(result), now, passed ? 'submitted' : 'draft', passed ? now : row.submitted_at,
      passed ? null : row.reject_reason, now, row.id);
    if (!passed) throw new HubError('check_failed', { detail: result });
    this.db.audit(`account:${accountId}`, 'application.submit', row.provider_id, { wantedTier: row.wanted_tier });
    log.info('application submitted', { provider: row.provider_id });
    return this.getApplication(accountId);
  }

  // ---------------------------------------------------------------- owner decisions

  listApplications(state) {
    const rows = state
      ? this.db.all('SELECT a.*, c.email FROM applications a JOIN accounts c ON c.id = a.account_id WHERE a.state = ? ORDER BY a.updated_at DESC LIMIT 500', state)
      : this.db.all('SELECT a.*, c.email FROM applications a JOIN accounts c ON c.id = a.account_id ORDER BY a.updated_at DESC LIMIT 500');
    return rows.map(r => this.applicationView(r, { withAccount: true }));
  }

  adminApplication(id) {
    const row = this.db.get('SELECT a.*, c.email FROM applications a JOIN accounts c ON c.id = a.account_id WHERE a.id = ?', id);
    if (!row) throw new HubError('not_found');
    return this.applicationView(row, { withAccount: true });
  }

  parseTier(value) {
    const tier = Number(value);
    if (tier !== 1 && tier !== 2) throw new HubError('invalid', { fields: { tier: 'Ступень 1 или 2.' } });
    return tier;
  }

  parseUntil(value) {
    if (value == null || value === '') return addYears(1);
    if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value) || Number.isNaN(Date.parse(value + 'T00:00:00Z'))
      || new Date(value + 'T00:00:00Z').toISOString().slice(0, 10) !== value) {
      throw new HubError('invalid', { fields: { until: 'Дата в виде ГГГГ-ММ-ДД.' } });
    }
    if (value <= today()) throw new HubError('invalid', { fields: { until: 'Срок должен быть позже сегодняшнего дня.' } });
    if (value > addYears(5)) throw new HubError('invalid', { fields: { until: 'Не дальше пяти лет.' } });
    return value;
  }

  approve(id, input, actor) {
    const tier = this.parseTier(input?.tier);
    const until = this.parseUntil(input?.until);
    const provider = this.db.tx(() => {
      const app = this.db.get('SELECT * FROM applications WHERE id = ?', id);
      if (!app) throw new HubError('not_found');
      if (app.state !== 'submitted') throw new HubError('state', { message: 'Одобрить можно только отправленную заявку.' });
      if (this.db.get('SELECT 1 FROM providers WHERE id = ?', app.provider_id)) throw new HubError('id_taken');
      const domains = parseJson(app.domains, []);
      this.assertDomainsFree(domains, app.provider_id);
      const now = nowIso();
      const look = JSON.stringify(defaultLook(app.name));
      this.db.run(`INSERT INTO providers (id, account_id, name, domains, contact, tier, until, state, source, look_draft, look_published, published_at, created_at, updated_at)
        VALUES (?, ?, ?, ?, ?, ?, ?, 'active', 'hub', ?, ?, ?, ?, ?)`,
      app.provider_id, app.account_id, app.name, app.domains, app.contact, tier, until, look, look, now, now, now);
      this.db.run("UPDATE applications SET state = 'approved', decided_at = ?, updated_at = ?, reject_reason = NULL WHERE id = ?", now, now, id);
      this.db.audit(actor, 'application.approve', app.provider_id, { tier, until });
      return app.provider_id;
    });
    this.publishSafely(actor);
    return this.providerView(this.db.get('SELECT * FROM providers WHERE id = ?', provider));
  }

  reject(id, input, actor) {
    const reason = oneLine(input?.reason ?? '', 500);
    if (!reason) throw new HubError('invalid', { fields: { reason: 'Напишите причину: до 500 символов.' } });
    return this.db.tx(() => {
      const app = this.db.get('SELECT * FROM applications WHERE id = ?', id);
      if (!app) throw new HubError('not_found');
      if (app.state !== 'submitted') throw new HubError('state', { message: 'Отклонить можно только отправленную заявку.' });
      const now = nowIso();
      this.db.run("UPDATE applications SET state = 'rejected', reject_reason = ?, decided_at = ?, updated_at = ? WHERE id = ?", reason, now, now, id);
      this.db.audit(actor, 'application.reject', app.provider_id, { reason });
      return this.adminApplication(id);
    });
  }

  providerRow(id) {
    const row = this.db.get('SELECT * FROM providers WHERE id = ?', id);
    if (!row) throw new HubError('not_found');
    return row;
  }

  providerView(row, { admin = false } = {}) {
    const published = parseJson(row.look_published, null);
    const view = {
      id: row.id,
      name: row.name,
      domains: parseJson(row.domains, []),
      tier: row.tier,
      until: row.until,
      expired: row.until <= today(),
      state: row.state,
      source: row.source,
      tierRequest: row.tier_request,
      revokeReason: row.revoke_reason,
      queueState: row.queue_state,
      returnReason: row.return_reason,
      queuedAt: row.queued_at,
      publishedAt: row.published_at,
      grants: grantsFor(row.tier, published),
      page: `${this.config.origin}/p/${row.id}/`,
    };
    if (admin) {
      view.contact = row.contact;
      view.email = row.account_id ? this.db.get('SELECT email FROM accounts WHERE id = ?', row.account_id)?.email ?? null : null;
    }
    return view;
  }

  listProviders() {
    return this.db.all('SELECT * FROM providers ORDER BY id').map(r => this.providerView(r, { admin: true }));
  }

  setTier(id, input, actor) {
    const tier = this.parseTier(input?.tier);
    const row = this.providerRow(id);
    this.db.run('UPDATE providers SET tier = ?, tier_request = NULL, updated_at = ? WHERE id = ?', tier, nowIso(), id);
    this.db.audit(actor, 'provider.tier', id, { from: row.tier, to: tier });
    this.publishSafely(actor);
    return this.providerView(this.providerRow(id), { admin: true });
  }

  setUntil(id, input, actor) {
    const until = this.parseUntil(input?.until);
    const row = this.providerRow(id);
    this.db.run('UPDATE providers SET until = ?, updated_at = ? WHERE id = ?', until, nowIso(), id);
    this.db.audit(actor, 'provider.until', id, { from: row.until, to: until });
    this.publishSafely(actor);
    return this.providerView(this.providerRow(id), { admin: true });
  }

  revoke(id, input, actor) {
    const reason = oneLine(input?.reason ?? '', 500);
    if (!reason) throw new HubError('invalid', { fields: { reason: 'Напишите причину: до 500 символов.' } });
    const row = this.providerRow(id);
    if (row.state === 'revoked') throw new HubError('state', { message: 'Уже отозван.' });
    this.db.run("UPDATE providers SET state = 'revoked', revoke_reason = ?, updated_at = ? WHERE id = ?", reason, nowIso(), id);
    this.db.audit(actor, 'provider.revoke', id, { reason });
    this.publishSafely(actor);
    return this.providerView(this.providerRow(id), { admin: true });
  }

  restore(id, actor) {
    const row = this.providerRow(id);
    if (row.state !== 'revoked') throw new HubError('state', { message: 'Провайдер не отозван.' });
    this.assertDomainsFree(parseJson(row.domains, []), id);
    this.db.run("UPDATE providers SET state = 'active', revoke_reason = NULL, updated_at = ? WHERE id = ?", nowIso(), id);
    this.db.audit(actor, 'provider.restore', id);
    this.publishSafely(actor);
    return this.providerView(this.providerRow(id), { admin: true });
  }

  queue() {
    return this.db.all("SELECT * FROM providers WHERE queue_state = 'queued' ORDER BY queued_at").map(row => {
      const look = parseJson(row.look_queued, null);
      return { provider: this.providerView(row, { admin: true }), look, review: this.lookReview(row, look) };
    });
  }

  approveLook(id, actor) {
    this.db.tx(() => {
      const row = this.providerRow(id);
      if (row.queue_state !== 'queued' || !row.look_queued) throw new HubError('state', { message: 'Это оформление уже не ждёт публикации.' });
      const look = parseJson(row.look_queued, null);
      const problems = this.lookReview(row, look).problems;
      if (Object.keys(problems).length) throw new HubError('look_invalid', { fields: problems });
      const now = nowIso();
      this.db.run(`UPDATE providers SET look_published = look_queued, look_queued = NULL, queue_state = NULL, return_reason = NULL,
        name = ?, published_at = ?, updated_at = ? WHERE id = ?`, look.name, now, now, id);
      this.db.audit(actor, 'look.approve', id);
    });
    this.publishSafely(actor);
    return this.providerView(this.providerRow(id), { admin: true });
  }

  returnLook(id, input, actor) {
    const reason = oneLine(input?.reason ?? '', 500);
    if (!reason) throw new HubError('invalid', { fields: { reason: 'Напишите, что поправить: до 500 символов.' } });
    const row = this.providerRow(id);
    if (row.queue_state !== 'queued') throw new HubError('state', { message: 'Это оформление уже не ждёт публикации.' });
    this.db.run("UPDATE providers SET queue_state = 'returned', return_reason = ?, look_queued = NULL, updated_at = ? WHERE id = ?", reason, nowIso(), id);
    this.db.audit(actor, 'look.return', id, { reason });
    return this.providerView(this.providerRow(id), { admin: true });
  }

  auditLog(limit = 200) {
    return this.db.all('SELECT id, at, actor, action, target, detail FROM audit ORDER BY id DESC LIMIT ?', Math.min(Math.max(1, limit | 0), 1000))
      .map(r => ({ ...r, detail: parseJson(r.detail, null) }));
  }

  outbox() {
    return this.db.all('SELECT id, email, subject, body, created_at, expires_at FROM outbox WHERE expires_at > ? ORDER BY id DESC LIMIT 100', Date.now())
      .map(r => ({ id: r.id, email: r.email, subject: r.subject, body: r.body, createdAt: new Date(r.created_at).toISOString(), expiresAt: new Date(r.expires_at).toISOString() }));
  }

  // ---------------------------------------------------------------- cabinet (provider)

  ownProvider(accountId) {
    const row = this.db.get('SELECT * FROM providers WHERE account_id = ?', accountId);
    if (!row) throw new HubError('not_found', { message: 'Кабинет откроется после одобрения заявки.' });
    return row;
  }

  pictureMap(providerId) {
    return new Map(this.db.all('SELECT sha256, slot, width, height, size FROM uploads WHERE provider_id = ?', providerId).map(r => [r.sha256, r]));
  }

  /** What blocks a look, the colour notes and the colours V2pn will draw (for the live preview). */
  lookReview(row, look) {
    if (!look) return { problems: {}, notes: [], colours: null };
    const problems = lookProblems(look, { tier: row.tier, domains: parseJson(row.domains, []), pictures: this.pictureMap(row.id) });
    const theme = themeOf(look);
    const page = pageTheme(theme);
    if (page.failures.length) problems.colours = 'Текст на странице провайдера будет нечитаем. Возьмите другой цвет.';
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

  cabinet(accountId) {
    const row = this.ownProvider(accountId);
    const draft = parseJson(row.look_draft, null) || parseJson(row.look_published, null) || defaultLook(row.name);
    return {
      provider: this.providerView(row),
      look: { draft, queued: parseJson(row.look_queued, null), published: parseJson(row.look_published, null) },
      review: this.lookReview(row, draft),
      share: { page: `${this.config.origin}/p/${row.id}/`, button: `${this.config.origin}/p/${row.id}/#sub=` },
      pictures: [...this.pictureMap(row.id).values()].map(p => ({ sha256: p.sha256, slot: p.slot, width: p.width, height: p.height, size: p.size })),
      // What each picture slot takes, in the words the upload's refusal uses (the cabinet shows them by the fields).
      slots: Object.fromEntries(PICTURES.map(key => [key, { hint: C.SLOTS[SLOT_OF[key]].desc[0], maxBytes: C.MAX_ASSET }])),
    };
  }

  saveLook(accountId, input) {
    const row = this.ownProvider(accountId);
    if (row.state !== 'active') throw new HubError('state', { message: 'Провайдер отозван: оформление менять нельзя.' });
    const look = parseLook(input);
    this.db.run('UPDATE providers SET look_draft = ?, updated_at = ? WHERE id = ?', JSON.stringify(look), nowIso(), row.id);
    return this.cabinet(accountId);
  }

  submitLook(accountId) {
    const row = this.ownProvider(accountId);
    if (row.state !== 'active') throw new HubError('state', { message: 'Провайдер отозван.' });
    const look = parseJson(row.look_draft, null);
    if (!look) throw new HubError('state', { message: 'Сначала сохраните оформление.' });
    const { problems } = this.lookReview(row, look);
    if (Object.keys(problems).length) throw new HubError('look_invalid', { fields: problems });
    const now = nowIso();
    this.db.run("UPDATE providers SET look_queued = look_draft, queue_state = 'queued', return_reason = NULL, queued_at = ?, updated_at = ? WHERE id = ?", now, now, row.id);
    this.db.audit(`account:${accountId}`, 'look.submit', row.id);
    return this.cabinet(accountId);
  }

  /** Stores a PNG for one picture slot of the provider's look; {sha256, width, height, size}. */
  upload(accountId, slotKey, bytes) {
    const row = this.ownProvider(accountId);
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
    const file = path.join(this.config.uploadsDir, `${hash}.png`);
    if (!fs.existsSync(file)) {
      const tmp = `${file}.${crypto.randomBytes(4).toString('hex')}.tmp`;
      fs.writeFileSync(tmp, bytes, { mode: 0o600 });
      fs.renameSync(tmp, file);
    }
    this.db.run(`INSERT INTO uploads (provider_id, sha256, slot, width, height, size, created_at) VALUES (?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT (provider_id, sha256) DO UPDATE SET slot = excluded.slot, created_at = excluded.created_at`,
    row.id, hash, slotKey, width, height, bytes.length, Date.now());
    return { sha256: hash, slot: slotKey, width, height, size: bytes.length };
  }

  readUpload(hash) {
    if (!/^[0-9a-f]{64}$/.test(hash)) return null;
    try {
      return fs.readFileSync(path.join(this.config.uploadsDir, `${hash}.png`));
    } catch {
      return null;
    }
  }

  /** A picture for the cabinet (the provider's own) or the admin (any). */
  picture(session, hash) {
    if (!/^[0-9a-f]{64}$/.test(hash)) throw new HubError('not_found');
    if (!session.admin) {
      const row = this.ownProvider(session.account.id);
      if (!this.db.get('SELECT 1 FROM uploads WHERE provider_id = ? AND sha256 = ?', row.id, hash)) throw new HubError('not_found');
    } else if (!this.db.get('SELECT 1 FROM uploads WHERE sha256 = ?', hash)) {
      throw new HubError('not_found');
    }
    const bytes = this.readUpload(hash);
    if (!bytes) throw new HubError('not_found');
    return bytes;
  }

  requestTier(accountId) {
    const row = this.ownProvider(accountId);
    if (row.tier >= 2) throw new HubError('state', { message: 'У вас уже ступень 2.' });
    this.db.run('UPDATE providers SET tier_request = 2, updated_at = ? WHERE id = ?', nowIso(), row.id);
    this.db.audit(`account:${accountId}`, 'provider.tier_request', row.id, { tier: 2 });
    return this.providerView(this.providerRow(row.id));
  }

  /** Calls the provider's login/start and one login/poll as V2pn would, under the same SSRF rules. */
  async loginTest(accountId, input) {
    const row = this.ownProvider(accountId);
    if (row.tier < 2) throw new HubError('tier');
    if (!this.limiter.hit(`login-test:${accountId}`, 10, HOUR)) throw new HubError('rate_limited');
    const domains = parseJson(row.domains, []);
    const draft = parseJson(row.look_draft, null) || {};
    const api0 = this.targetUrl(input?.api ?? draft.api ?? '', 512);
    if (!api0) throw new HubError('invalid', { fields: { api: 'Адрес API должен начинаться с https://.' } });
    if (!domainAllowed(domains, api0)) throw new HubError('invalid', { fields: { api: 'Адрес API должен быть на одном из ваших доменов.' } });
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
        if (!bot) add('bot', 'warn', 'Нет ссылки на бота', 'Поле url — https на t.me или на ваших доменах. Без него V2pn не покажет QR-код.');
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

  // ---------------------------------------------------------------- publication

  /** Rebuilds and re-signs everything; throws with the problems when nothing could be written. */
  publish(actor = 'system') {
    const providers = this.db.all('SELECT * FROM providers ORDER BY id');
    const publication = buildPublication(providers, {
      readUpload: h => this.readUpload(h), today: today(), published: publishedStamp(), origin: this.config.origin,
    });
    if (publication.problems.length) {
      this.lastPublishError = { at: nowIso(), problems: publication.problems };
      log.error('publish refused', { problems: publication.problems });
      throw new HubError('publish_failed', { detail: publication.problems });
    }
    const previous = parseJson(this.db.meta('pages'), []);
    const pages = writePublication(this.config.publicDir, publication, this.key, previous);
    this.db.setMeta('pages', JSON.stringify(pages));
    this.db.setMeta('published', JSON.stringify({ at: nowIso(), partners: pages, index: sha256(publication.index) }));
    this.db.setMeta('published_day', today());
    this.lastPublishError = null;
    log.info('published', { partners: pages.length, actor });
    return { partners: pages, at: nowIso() };
  }

  /** publish() after a decision: the decision stands even when the files could not be written (the owner sees why). */
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
   * Takes partners/<id> (listing.json, partner.json, PNG pictures) as an approved provider, its look published.
   * The grants that need a reviewed file stay with the owner's root-signed index and are left out here.
   */
  importPartner(folder, { tier, email } = {}) {
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
    const grants = listing.grants || [];
    for (const g of grants.filter(x => REVIEWED_GRANTS.has(x))) notes.push(`grant ${g} left out: it needs the owner's root-signed index`);
    const known = new Set(['schema', 'id', 'name', 'brand', 'theme', 'api', 'login', 'links', 'actions', 'lock', 'assets']);
    const unknown = Object.keys(pack).filter(k => !known.has(k));
    if (unknown.length) throw new Error(`partner.json has fields the hub does not edit: ${unknown.join(', ')}`);
    const resolvedTier = tier ?? (grants.some(g => ['account', 'actions', 'lock'].includes(g)) ? 2 : 1);

    const brand = (pack.theme?.palette?.brand ?? (pack.brand?.accent ? [pack.brand.accent] : [])).map(h => String(h).toUpperCase());
    const bg = pack.theme?.background;
    let background = 'none';
    if (bg?.type === 'gradient') {
      background = 'gradient';
      const want = JSON.stringify((bg.colors || []).map(h => String(h).toUpperCase()));
      const look0 = { ...defaultLook(name), brand, background };
      if (JSON.stringify(themeOf(look0).background.colors) !== want) throw new Error('theme.background: the hub makes the gradient from the brand colours; these colours differ');
    } else if (bg?.type === 'image') {
      background = 'image';
    }
    const files = new Map((pack.assets || []).map(a => [a.name, a]));
    const pictureOf = (file, slotKey) => {
      if (!file) return null;
      const asset = files.get(file);
      if (!asset || !/\.png$/i.test(file)) throw new Error(`${file}: the hub takes PNG pictures only`);
      const bytes = fs.readFileSync(path.join(folder, file));
      if (bytes.length !== asset.size || sha256(bytes) !== asset.sha256) throw new Error(`${file} does not match partner.json's assets`);
      return { slotKey, bytes };
    };
    const pics = [
      pictureOf(pack.brand?.logo, 'logo'), pictureOf(pack.brand?.logoOnLight, 'logoOnLight'), pictureOf(pack.brand?.mark, 'mark'),
      background === 'image' ? pictureOf(bg.art, 'backdrop') : null,
    ].filter(Boolean);
    const look = parseLook({
      name: pack.name ?? name, brand, background, links: { cabinet: pack.links?.cabinet ?? '', support: pack.links?.support ?? '' },
      api: pack.api ?? '', login: pack.login === true, lock: pack.lock === true,
      actions: (pack.actions || []).map(a => ({ label: a.label, icon: a.icon, do: a.do, strategy: a.strategy, purpose: a.purpose, server: a.server, group: a.group, url: a.url })),
    });
    if ((pack.actions || []).some(a => a.scope)) notes.push('action scopes are not kept: the hub does not edit them');
    for (const p of pics) look[p.slotKey] = sha256(p.bytes);

    const now = nowIso();
    let accountId = null;
    if (email) {
      this.db.run('INSERT INTO accounts (email, created_at) VALUES (?, ?) ON CONFLICT (email) DO NOTHING', email, now);
      accountId = this.db.get('SELECT id FROM accounts WHERE email = ?', email).id;
    }
    this.db.tx(() => {
      const existing = this.db.get('SELECT * FROM providers WHERE id = ?', listing.id);
      if (existing && existing.source !== 'import') throw new Error(`${listing.id} is a provider that applied through the hub; not replaced`);
      this.assertDomainsFree(domains, listing.id);
      for (const p of pics) {
        const hash = sha256(p.bytes);
        const file = path.join(this.config.uploadsDir, `${hash}.png`);
        if (!fs.existsSync(file)) fs.writeFileSync(file, p.bytes, { mode: 0o600 });
        const [w, h] = C.imageSize(p.bytes);
        this.db.run(`INSERT INTO uploads (provider_id, sha256, slot, width, height, size, created_at) VALUES (?, ?, ?, ?, ?, ?, ?)
          ON CONFLICT (provider_id, sha256) DO UPDATE SET slot = excluded.slot`, listing.id, hash, p.slotKey, w, h, p.bytes.length, Date.now());
      }
      const problems = lookProblems(look, { tier: resolvedTier, domains, pictures: this.pictureMap(listing.id) });
      if (Object.keys(problems).length) throw new Error(`the look does not pass: ${JSON.stringify(problems)}`);
      const lookJson = JSON.stringify(look);
      if (existing) {
        this.db.run(`UPDATE providers SET account_id = COALESCE(?, account_id), name = ?, domains = ?, contact = ?, tier = ?, until = ?, state = 'active',
          look_draft = ?, look_published = ?, published_at = ?, updated_at = ? WHERE id = ?`,
        accountId, name, JSON.stringify(domains), listing.contact ?? '', resolvedTier, until, lookJson, lookJson, now, now, listing.id);
      } else {
        this.db.run(`INSERT INTO providers (id, account_id, name, domains, contact, tier, until, state, source, look_draft, look_published, published_at, created_at, updated_at)
          VALUES (?, ?, ?, ?, ?, ?, ?, 'active', 'import', ?, ?, ?, ?, ?)`,
        listing.id, accountId, name, JSON.stringify(domains), listing.contact ?? '', resolvedTier, until, lookJson, lookJson, now, now, now);
      }
      this.db.audit('cli', 'provider.import', listing.id, { tier: resolvedTier, until });
    });
    const expectedGrants = grantsFor(resolvedTier, look);
    const lost = grants.filter(g => !REVIEWED_GRANTS.has(g) && g !== 'edition' && !expectedGrants.includes(g));
    for (const g of lost) notes.push(`grant ${g} is not given: the look does not use it`);
    return { id: listing.id, tier: resolvedTier, grants: expectedGrants, notes, files: FILE };
  }

  // ---------------------------------------------------------------- housekeeping

  /** Every few minutes: expired codes and sessions out, counters in; once a day a fresh index (terms run out). */
  maintain() {
    try {
      this.auth.sweep();
      this.counter.flush();
      if (this.db.meta('published_day') !== today()) {
        const due = this.db.get("SELECT 1 FROM providers WHERE state = 'active' AND look_published IS NOT NULL LIMIT 1");
        if (due || this.db.meta('published_day')) this.publishSafely('system');
        this.collectUploads();
      }
    } catch (e) {
      log.error('maintenance failed', { error: String(e.message || e) });
    }
  }

  /** Pictures no look uses any more, a day after their upload, and files no provider holds. */
  collectUploads() {
    const used = new Set();
    for (const row of this.db.all('SELECT id, look_draft, look_queued, look_published FROM providers')) {
      for (const text of [row.look_draft, row.look_queued, row.look_published]) {
        const look = parseJson(text, null);
        if (look) for (const key of PICTURES) if (look[key]) used.add(`${row.id}|${look[key]}`);
      }
    }
    const dayAgo = Date.now() - 24 * HOUR;
    for (const u of this.db.all('SELECT provider_id, sha256, created_at FROM uploads')) {
      if (!used.has(`${u.provider_id}|${u.sha256}`) && u.created_at < dayAgo) this.db.run('DELETE FROM uploads WHERE provider_id = ? AND sha256 = ?', u.provider_id, u.sha256);
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
