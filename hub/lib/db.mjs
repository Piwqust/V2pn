// The hub's SQLite database (node:sqlite): WAL, numbered migrations, parameterised statements only.
import fs from 'node:fs';
import path from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { parseActions } from './look.mjs';
import { contentHash } from './rights.mjs';

// The second version: accounts by login and password, one partner per account published on save, special rights
// waiting for a moderator, complaints. Applications, tiers, the look queue, codes, links and the outbox are gone.
const SCHEMA_V2 = `
  CREATE TABLE accounts (
    id INTEGER PRIMARY KEY,
    login TEXT NOT NULL UNIQUE,
    role TEXT NOT NULL DEFAULT 'provider',
    password TEXT,
    password_at TEXT,
    created_at TEXT NOT NULL,
    last_login_at TEXT
  );
  CREATE TABLE sessions (
    token_hash TEXT PRIMARY KEY,
    account_id INTEGER NOT NULL REFERENCES accounts (id) ON DELETE CASCADE,
    created_at INTEGER NOT NULL,
    expires_at INTEGER NOT NULL,
    seen_at INTEGER NOT NULL
  );
  CREATE INDEX sessions_account ON sessions (account_id);
  CREATE TABLE partners (
    id TEXT PRIMARY KEY,
    account_id INTEGER NOT NULL UNIQUE REFERENCES accounts (id),
    name TEXT NOT NULL,
    domains TEXT NOT NULL,
    subscription TEXT,
    check_json TEXT,
    checked_at TEXT,
    look TEXT NOT NULL,
    state TEXT NOT NULL,
    down_reason TEXT,
    source TEXT NOT NULL,
    until TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    published_at TEXT
  );
  CREATE TABLE rights (
    partner_id TEXT NOT NULL REFERENCES partners (id),
    name TEXT NOT NULL,
    content TEXT NOT NULL,
    hash TEXT NOT NULL,
    approved TEXT,
    approved_hash TEXT,
    denied_hash TEXT,
    reason TEXT,
    requested_at TEXT NOT NULL,
    decided_at TEXT,
    decided_by TEXT,
    PRIMARY KEY (partner_id, name)
  );
  CREATE TABLE complaints (
    id INTEGER PRIMARY KEY,
    partner_id TEXT NOT NULL,
    text TEXT NOT NULL DEFAULT '',
    created_at TEXT NOT NULL
  );
  CREATE INDEX complaints_partner ON complaints (partner_id);
  CREATE TABLE uploads (
    account_id INTEGER NOT NULL,
    sha256 TEXT NOT NULL,
    slot TEXT NOT NULL,
    width INTEGER NOT NULL,
    height INTEGER NOT NULL,
    size INTEGER NOT NULL,
    created_at INTEGER NOT NULL,
    PRIMARY KEY (account_id, sha256)
  );
`;

const parseJson = (text, fallback) => { try { return text ? JSON.parse(text) : fallback; } catch { return fallback; } };

/**
 * Version 1 to 2 on the live data. Every provider stays a partner with the look it had published; what its tier let
 * its look carry (API and sign-in by code, buttons, lock) becomes a special right already approved. Accounts were
 * keyed by email: a provider's account takes the provider's id as its login, any other keeps its email; nobody has a
 * password until `hub.mjs reset-password` (or `owner`) gives one. Providers imported without an account get one.
 * Sessions end: they were made by email codes.
 */
function migrateToV2(db) {
  const now = new Date().toISOString();
  const accounts = db.all('SELECT id, email, created_at, last_login_at FROM accounts ORDER BY id');
  const providers = db.all('SELECT * FROM providers ORDER BY id');
  const apps = new Map(db.all('SELECT account_id, test_url, check_json, checked_at FROM applications').map(a => [a.account_id, a]));
  const uploads = db.all('SELECT * FROM uploads');

  const used = new Set();
  const take = base => {
    let login = base;
    for (let i = 2; used.has(login); i++) login = `${base}-${i}`;
    used.add(login);
    return login;
  };
  const byAccount = new Map(providers.filter(p => p.account_id).map(p => [p.account_id, p]));
  const newAccounts = [];
  for (const a of accounts) if (byAccount.has(a.id)) newAccounts.push({ ...a, login: take(byAccount.get(a.id).id) });
  for (const a of accounts) if (!byAccount.has(a.id)) newAccounts.push({ ...a, login: take(a.email) });
  let nextId = Math.max(0, ...accounts.map(a => a.id)) + 1;
  const accountOf = new Map(providers.filter(p => p.account_id).map(p => [p.id, p.account_id]));
  for (const p of providers.filter(x => !x.account_id)) {
    const id = nextId++;
    newAccounts.push({ id, login: take(p.id), created_at: p.created_at, last_login_at: null });
    accountOf.set(p.id, id);
  }

  for (const table of ['sessions', 'login_codes', 'admin_links', 'outbox', 'applications', 'uploads', 'providers', 'accounts']) {
    db.db.exec(`DROP TABLE IF EXISTS ${table}`);
  }
  db.db.exec(SCHEMA_V2);

  for (const a of newAccounts) {
    db.run("INSERT INTO accounts (id, login, role, created_at, last_login_at) VALUES (?, ?, 'provider', ?, ?)", a.id, a.login, a.created_at, a.last_login_at);
  }
  for (const p of providers) {
    const old = parseJson(p.look_published, null) || parseJson(p.look_draft, null) || { name: p.name };
    const look = {
      name: old.name || p.name, brand: old.brand || [], background: old.background || 'none', layout: 'v2pn',
      logo: old.logo ?? null, logoOnLight: old.logoOnLight ?? null, mark: old.mark ?? null, backdrop: old.backdrop ?? null,
      links: { cabinet: old.links?.cabinet ?? '', support: old.links?.support ?? '' },
    };
    const app = p.account_id ? apps.get(p.account_id) : null;
    db.run(`INSERT INTO partners (id, account_id, name, domains, subscription, check_json, checked_at, look, state, down_reason, source, until,
      created_at, updated_at, published_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
    p.id, accountOf.get(p.id), p.name, p.domains, p.source === 'hub' ? app?.test_url || null : null, app?.check_json ?? null, app?.checked_at ?? null,
    JSON.stringify(look), p.state === 'active' ? 'live' : 'down', p.state === 'active' ? null : p.revoke_reason, p.source,
    p.source === 'import' ? p.until : null, p.created_at, now, p.published_at);
    if (p.tier < 2) continue;
    const rights = {};
    if (old.api) rights.account = { api: old.api, login: old.login === true };
    if (old.actions?.length) {
      try {
        rights.actions = parseActions(old.actions);
      } catch { /* buttons V2pn would not take were never published either */ }
    }
    if (old.lock) rights.lock = true;
    for (const [name, content] of Object.entries(rights)) {
      const text = JSON.stringify(content);
      const hash = contentHash(name, content);
      db.run(`INSERT INTO rights (partner_id, name, content, hash, approved, approved_hash, requested_at, decided_at, decided_by)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, 'migration')`, p.id, name, text, hash, text, hash, now, now);
    }
  }
  for (const u of uploads) {
    const account = accountOf.get(u.provider_id);
    if (!account) continue;
    db.run(`INSERT INTO uploads (account_id, sha256, slot, width, height, size, created_at) VALUES (?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT (account_id, sha256) DO NOTHING`, account, u.sha256, u.slot, u.width, u.height, u.size, u.created_at);
  }
  db.audit('migration', 'hub.v2', null, { partners: providers.length, accounts: newAccounts.length });
}

/**
 * Version 3 to 4: domain proof (lib/proof.mjs). Each account gets a proof token when it first needs one; the domains it
 * proved are kept with how. Partners already there keep theirs: an imported one's as "import", the others' as
 * "grandfathered" (live without a record; the cabinet asks gently for one).
 */
function addDomainProof(db) {
  db.db.exec(`
    ALTER TABLE accounts ADD COLUMN proof_token TEXT;
    CREATE TABLE proofs (
      account_id INTEGER NOT NULL REFERENCES accounts (id) ON DELETE CASCADE,
      domain TEXT NOT NULL,
      how TEXT NOT NULL,
      at TEXT NOT NULL,
      by TEXT,
      PRIMARY KEY (account_id, domain)
    );
  `);
  const now = new Date().toISOString();
  for (const p of db.all('SELECT account_id, domains, source FROM partners')) {
    for (const domain of parseJson(p.domains, [])) {
      db.run('INSERT OR IGNORE INTO proofs (account_id, domain, how, at) VALUES (?, ?, ?, ?)', p.account_id, domain, p.source === 'import' ? 'import' : 'grandfathered', now);
    }
  }
}

const MIGRATIONS = [
  // 1: everything the first version needs.
  `
  CREATE TABLE accounts (
    id INTEGER PRIMARY KEY,
    email TEXT NOT NULL UNIQUE,
    created_at TEXT NOT NULL,
    last_login_at TEXT
  );
  CREATE TABLE login_codes (
    id INTEGER PRIMARY KEY,
    email TEXT NOT NULL,
    code_hash TEXT NOT NULL,
    created_at INTEGER NOT NULL,
    expires_at INTEGER NOT NULL,
    tries INTEGER NOT NULL DEFAULT 0,
    used INTEGER NOT NULL DEFAULT 0
  );
  CREATE INDEX login_codes_email ON login_codes (email, created_at);
  CREATE TABLE sessions (
    token_hash TEXT PRIMARY KEY,
    account_id INTEGER NOT NULL REFERENCES accounts (id) ON DELETE CASCADE,
    created_at INTEGER NOT NULL,
    expires_at INTEGER NOT NULL,
    seen_at INTEGER NOT NULL
  );
  CREATE INDEX sessions_account ON sessions (account_id);
  CREATE TABLE admin_links (
    token_hash TEXT PRIMARY KEY,
    email TEXT NOT NULL,
    expires_at INTEGER NOT NULL,
    used INTEGER NOT NULL DEFAULT 0
  );
  CREATE TABLE outbox (
    id INTEGER PRIMARY KEY,
    email TEXT NOT NULL,
    subject TEXT NOT NULL,
    body TEXT NOT NULL,
    created_at INTEGER NOT NULL,
    expires_at INTEGER NOT NULL
  );
  CREATE TABLE applications (
    id INTEGER PRIMARY KEY,
    account_id INTEGER NOT NULL UNIQUE REFERENCES accounts (id),
    provider_id TEXT UNIQUE,
    name TEXT NOT NULL DEFAULT '',
    domains TEXT NOT NULL DEFAULT '[]',
    test_url TEXT NOT NULL DEFAULT '',
    contact TEXT NOT NULL DEFAULT '',
    wanted_tier INTEGER NOT NULL DEFAULT 1,
    note TEXT NOT NULL DEFAULT '',
    state TEXT NOT NULL,
    reject_reason TEXT,
    check_json TEXT,
    checked_at TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    submitted_at TEXT,
    decided_at TEXT
  );
  CREATE TABLE providers (
    id TEXT PRIMARY KEY,
    account_id INTEGER UNIQUE REFERENCES accounts (id),
    name TEXT NOT NULL,
    domains TEXT NOT NULL,
    contact TEXT NOT NULL DEFAULT '',
    tier INTEGER NOT NULL,
    until TEXT NOT NULL,
    state TEXT NOT NULL,
    source TEXT NOT NULL,
    tier_request INTEGER,
    revoke_reason TEXT,
    look_draft TEXT,
    look_queued TEXT,
    look_published TEXT,
    queue_state TEXT,
    return_reason TEXT,
    queued_at TEXT,
    published_at TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
  );
  CREATE TABLE uploads (
    provider_id TEXT NOT NULL,
    sha256 TEXT NOT NULL,
    slot TEXT NOT NULL,
    width INTEGER NOT NULL,
    height INTEGER NOT NULL,
    size INTEGER NOT NULL,
    created_at INTEGER NOT NULL,
    PRIMARY KEY (provider_id, sha256)
  );
  CREATE TABLE audit (
    id INTEGER PRIMARY KEY,
    at TEXT NOT NULL,
    actor TEXT NOT NULL,
    action TEXT NOT NULL,
    target TEXT,
    detail TEXT
  );
  CREATE TABLE downloads (
    day TEXT NOT NULL,
    name TEXT NOT NULL,
    count INTEGER NOT NULL,
    PRIMARY KEY (day, name)
  );
  CREATE TABLE meta (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
  );
  `,
  // 2: the simple partner path (hub/README.md).
  migrateToV2,
  // 3: V2pn's requests for the update manifest, by day and the version that asked.
  `
  CREATE TABLE checks (
    day TEXT NOT NULL,
    version TEXT NOT NULL,
    count INTEGER NOT NULL,
    PRIMARY KEY (day, version)
  );
  `,
  // 4: domain proof; the partners already there keep their domains.
  addDomainProof,
];

export class Db {
  /** <upTo> stops the migrations at a version (the tests make a database of an older version this way). */
  constructor(file, { upTo = Infinity } = {}) {
    if (file !== ':memory:') fs.mkdirSync(path.dirname(file), { recursive: true, mode: 0o700 });
    this.db = new DatabaseSync(file);
    this.db.exec('PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;');
    this.statements = new Map();
    this.migrate(upTo);
  }

  migrate(upTo = Infinity) {
    this.db.exec('CREATE TABLE IF NOT EXISTS migrations (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL)');
    const done = new Set(this.all('SELECT version FROM migrations').map(r => r.version));
    MIGRATIONS.forEach((step, i) => {
      const version = i + 1;
      if (done.has(version) || version > upTo) return;
      this.tx(() => {
        if (typeof step === 'function') step(this);
        else this.db.exec(step);
        this.run('INSERT INTO migrations (version, applied_at) VALUES (?, ?)', version, new Date().toISOString());
      });
      // Statements prepared against the tables a step dropped are of no use any more.
      this.statements.clear();
    });
  }

  prepare(sql) {
    let st = this.statements.get(sql);
    if (!st) {
      st = this.db.prepare(sql);
      this.statements.set(sql, st);
    }
    return st;
  }

  get(sql, ...params) {
    const row = this.prepare(sql).get(...params);
    return row ? { ...row } : undefined;
  }

  all(sql, ...params) {
    return this.prepare(sql).all(...params).map(r => ({ ...r }));
  }

  run(sql, ...params) {
    return this.prepare(sql).run(...params);
  }

  /** Runs <fn> in one write transaction (taken at once, so two writers never interleave). */
  tx(fn) {
    if (this.inTx) return fn();
    this.db.exec('BEGIN IMMEDIATE');
    this.inTx = true;
    try {
      const result = fn();
      this.db.exec('COMMIT');
      return result;
    } catch (e) {
      this.db.exec('ROLLBACK');
      throw e;
    } finally {
      this.inTx = false;
    }
  }

  meta(key) {
    return this.get('SELECT value FROM meta WHERE key = ?', key)?.value ?? null;
  }

  setMeta(key, value) {
    this.run('INSERT INTO meta (key, value) VALUES (?, ?) ON CONFLICT (key) DO UPDATE SET value = excluded.value', key, String(value));
  }

  audit(actor, action, target, detail) {
    this.run('INSERT INTO audit (at, actor, action, target, detail) VALUES (?, ?, ?, ?, ?)',
      new Date().toISOString(), actor, action, target ?? null, detail === undefined ? null : JSON.stringify(detail));
  }

  close() {
    try {
      this.db.exec('PRAGMA optimize');
    } catch { /* closing anyway */ }
    this.db.close();
  }
}
