// A database of the first version (email accounts, applications, tiers, the look queue) moved to the second.
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { loadConfig } from '../lib/config.mjs';
import { Db } from '../lib/db.mjs';
import { createHub } from '../lib/server.mjs';
import { readZip } from '../lib/zip.mjs';
import { tempDir, testNet } from './helpers.mjs';

const sha = b => crypto.createHash('sha256').update(b).digest('hex');
const configra = new URL('../../partners/configra/', import.meta.url);

test('a v1 database: providers stay partners with their look and their tier-2 parts approved; logins from ids and emails', async () => {
  const root = tempDir();
  const config = loadConfig({ HUB_DATA: path.join(root, 'data'), HUB_PUBLIC_DIR: path.join(root, 'www'), HUB_ORIGIN: 'https://hub.example' });
  try {
    const v1 = new Db(config.dbFile, { upTo: 1 });
    const now = new Date().toISOString();
    const add = email => Number(v1.run('INSERT INTO accounts (email, created_at) VALUES (?, ?)', email, now).lastInsertRowid);
    const configraAccount = add('team@configra.example');
    const kestrelAccount = add('boss@kestrel.example');
    add('owner@example.com');
    const applicant = add('maybe@example.com');
    v1.run('INSERT INTO sessions (token_hash, account_id, created_at, expires_at, seen_at) VALUES (?, ?, 0, 9999999999999, 0)', 'a'.repeat(64), configraAccount);
    v1.run("INSERT INTO outbox (email, subject, body, created_at, expires_at) VALUES ('maybe@example.com', 's', 'b', 0, 9999999999999)");
    v1.run(`INSERT INTO applications (account_id, provider_id, name, domains, test_url, state, created_at, updated_at)
      VALUES (?, 'maybe', 'Maybe', '[]', '', 'draft', ?, ?)`, applicant, now, now);
    v1.run(`INSERT INTO applications (account_id, provider_id, name, domains, test_url, check_json, state, created_at, updated_at)
      VALUES (?, 'kestrel', 'Kestrel', '["kestrel.example"]', 'https://sub.kestrel.example/s/x', '{"ok":true,"servers":{"total":3}}', 'approved', ?, ?)`, kestrelAccount, now, now);

    fs.mkdirSync(config.uploadsDir, { recursive: true });
    const pics = {};
    for (const [key, file] of [['logo', 'logo-light.png'], ['logoOnLight', 'logo-dark.png'], ['mark', 'mark.png']]) {
      const bytes = fs.readFileSync(new URL(file, configra));
      pics[key] = sha(bytes);
      fs.writeFileSync(path.join(config.uploadsDir, `${pics[key]}.png`), bytes);
      v1.run('INSERT INTO uploads (provider_id, sha256, slot, width, height, size, created_at) VALUES (?, ?, ?, 1, 1, ?, 0)', 'configra', pics[key], key, bytes.length);
    }
    const configraLook = {
      name: 'Configra', brand: ['#FF7A2F', '#EDAA80'], background: 'gradient', ...pics, backdrop: null,
      links: { cabinet: 'https://configra.angleford.co/cabinet', support: '' }, api: 'https://configra.angleford.co/v2pn/api/', login: true, actions: [], lock: false,
    };
    const kestrelLook = {
      name: 'Kestrel', brand: ['#2F7BFF'], background: 'none', logo: null, logoOnLight: null, mark: null, backdrop: null, links: { cabinet: '', support: '' },
      api: '', login: false, lock: true,
      actions: [{ label: 'Бот', icon: 'chat', do: 'open.link', url: 'https://t.me/kestrel_bot', strategy: '', purpose: '', server: '', group: '' }],
    };
    const insert = (id, account, domains, tier, until, state, source, look, extra = {}) => v1.run(`INSERT INTO providers (id, account_id, name, domains, contact,
      tier, until, state, source, revoke_reason, look_draft, look_published, queue_state, published_at, created_at, updated_at)
      VALUES (?, ?, ?, ?, '', ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`, id, account, look.name, JSON.stringify(domains), tier, until, state, source,
    extra.reason ?? null, JSON.stringify({ ...look, name: 'Draft name' }), JSON.stringify(look), extra.queue ?? null, now, now, now);
    insert('configra', configraAccount, ['angleford.co', '*.angleford.co'], 2, '2027-11-16', 'active', 'import', configraLook);
    insert('kestrel', kestrelAccount, ['kestrel.example'], 2, '2027-01-01', 'revoked', 'hub', kestrelLook, { reason: 'Нет договора', queue: 'queued' });
    insert('orphan', null, ['orphan.example'], 1, '2027-01-01', 'active', 'import', { ...kestrelLook, name: 'Orphan', actions: [], lock: false });
    v1.close();

    const hub = createHub(config, { net: testNet });
    try {
      const { db } = hub;
      assert.deepEqual(db.all('SELECT version FROM migrations ORDER BY version').map(r => r.version), [1, 2, 3, 4, 5]);
      const tables = db.all("SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name").map(r => r.name);
      for (const gone of ['applications', 'providers', 'login_codes', 'admin_links', 'outbox']) assert.ok(!tables.includes(gone), gone);
      assert.equal(db.get('SELECT COUNT(*) AS n FROM sessions').n, 0, 'sessions made by codes end');

      assert.deepEqual(db.all('SELECT login, role, password FROM accounts ORDER BY id').map(a => [a.login, a.role, a.password]), [
        ['configra', 'provider', null], ['kestrel', 'provider', null], ['owner@example.com', 'provider', null], ['maybe@example.com', 'provider', null],
        ['orphan', 'provider', null],
      ]);
      const partners = db.all('SELECT p.id, a.login, p.state, p.down_reason, p.source, p.until, p.subscription FROM partners p JOIN accounts a ON a.id = p.account_id ORDER BY p.id');
      assert.deepEqual(partners.map(p => [p.id, p.login, p.state, p.down_reason, p.source, p.until, p.subscription]), [
        ['configra', 'configra', 'live', null, 'import', '2027-11-16', null],
        ['kestrel', 'kestrel', 'down', 'Нет договора', 'hub', null, 'https://sub.kestrel.example/s/x'],
        ['orphan', 'orphan', 'live', null, 'import', '2027-01-01', null],
      ]);
      const look = JSON.parse(db.get("SELECT look FROM partners WHERE id = 'configra'").look);
      assert.equal(look.name, 'Configra', 'the published look, not the draft');
      assert.equal(look.layout, 'v2pn');
      assert.equal(look.api, undefined, 'the API is a right now');
      const rights = db.all('SELECT partner_id, name, content, approved_hash = hash AS approved FROM rights ORDER BY partner_id, name');
      assert.deepEqual(rights.map(r => [r.partner_id, r.name, r.approved]), [['configra', 'account', 1], ['kestrel', 'actions', 1], ['kestrel', 'lock', 1]]);
      assert.deepEqual(JSON.parse(rights[0].content), { api: 'https://configra.angleford.co/v2pn/api/', login: true });
      assert.equal(db.get("SELECT COUNT(*) AS n FROM uploads u JOIN partners p ON p.account_id = u.account_id WHERE p.id = 'configra'").n, 3);
      // Their domains need no proof: imported ones were reviewed by the owner, the others were partners before proofs.
      assert.deepEqual(db.all('SELECT a.login, r.domain, r.how FROM proofs r JOIN accounts a ON a.id = r.account_id ORDER BY a.login, r.domain').map(r => [r.login, r.domain, r.how]), [
        ['configra', '*.angleford.co', 'import'], ['configra', 'angleford.co', 'import'], ['kestrel', 'kestrel.example', 'grandfathered'], ['orphan', 'orphan.example', 'import'],
      ]);

      // Published as before: Configra's own pack, Kestrel's page saying it was taken down.
      hub.publish('test');
      const dir = path.join(config.publicDir, 'partners', 'latest', 'download');
      const index = JSON.parse(fs.readFileSync(path.join(dir, 'partner-index.json')));
      assert.deepEqual(index.partners.map(p => [p.id, p.grants.join()]), [['configra', 'edition,account'], ['orphan', 'edition']]);
      const zip = readZip(fs.readFileSync(path.join(dir, 'partner-configra.zip')));
      assert.equal(zip.get('partner.json').toString(), fs.readFileSync(new URL('partner.json', configra), 'utf8').replace(/\r\n/g, '\n'));
      assert.match(fs.readFileSync(path.join(config.publicDir, 'p', 'kestrel', 'index.html'), 'utf8'), /снят модератором/);

      // Restored, Kestrel goes out with its buttons and lock.
      hub.restore('kestrel', 'test');
      const kestrel = JSON.parse(readZip(fs.readFileSync(path.join(dir, 'partner-kestrel.zip'))).get('partner.json'));
      assert.deepEqual([kestrel.lock, kestrel.actions.map(a => a.url)], [true, ['https://t.me/kestrel_bot']]);

      // The provider gets in once a password is set (hub.mjs reset-password).
      const password = hub.auth.resetPassword(hub.auth.accountByLogin('configra').id);
      assert.equal((await hub.auth.signIn('configra', password, '127.0.0.1')).account.login, 'configra');
    } finally {
      hub.db.close();
    }
  } finally {
    try { fs.rmSync(root, { recursive: true, force: true }); } catch { /* an open database on Windows; the temporary folder stays */ }
  }
});
