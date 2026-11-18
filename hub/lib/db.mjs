// The hub's SQLite database (node:sqlite): WAL, numbered migrations, parameterised statements only.
import fs from 'node:fs';
import path from 'node:path';
import { DatabaseSync } from 'node:sqlite';

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
];

export class Db {
  constructor(file) {
    if (file !== ':memory:') fs.mkdirSync(path.dirname(file), { recursive: true, mode: 0o700 });
    this.db = new DatabaseSync(file);
    this.db.exec('PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;');
    this.statements = new Map();
    this.migrate();
  }

  migrate() {
    this.db.exec('CREATE TABLE IF NOT EXISTS migrations (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL)');
    const done = new Set(this.all('SELECT version FROM migrations').map(r => r.version));
    MIGRATIONS.forEach((sql, i) => {
      const version = i + 1;
      if (done.has(version)) return;
      this.tx(() => {
        this.db.exec(sql);
        this.run('INSERT INTO migrations (version, applied_at) VALUES (?, ?)', version, new Date().toISOString());
      });
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
