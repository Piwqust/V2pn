// The partners the owner signs with the root key keep their ids and domains on the hub (lib/roots.mjs).
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { ROOT_PARTNER_KEY, loadConfig } from '../lib/config.mjs';
import { publicKeyOf, signature } from '../lib/keys.mjs';
import { setQuiet } from '../lib/log.mjs';
import { parseRootList, readRootIndex } from '../lib/roots.mjs';
import { createHub } from '../lib/server.mjs';
import { Client, fixture, proveDomain, register, serve, startHub, testNet } from './helpers.mjs';

const configraFolder = new URL('../../partners/configra', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1');

/** A root-signed index of <partners> ({id, name, domains}) and its .sig, with a key of the test's own. */
function rootIndex(key, partners) {
  const bytes = Buffer.from(JSON.stringify({
    schema: 1,
    published: '2026-11-20T10:00:00Z',
    partners: partners.map(p => ({ ...p, grants: ['edition'], until: '2027-11-01', pack: { file: `partner-${p.id}.zip`, size: 10, sha256: 'a'.repeat(64) } })),
  }, null, 2) + '\n');
  return { bytes, sig: signature(key, bytes) };
}

/** Every log line written while <fn> runs. */
async function logged(fn) {
  const lines = [];
  const write = process.stdout.write.bind(process.stdout);
  process.stdout.write = chunk => { lines.push(String(chunk)); return true; };
  setQuiet(false);
  try {
    await fn();
  } finally {
    process.stdout.write = write;
    setQuiet(true);
  }
  return lines.filter(l => l.startsWith('{')).map(l => JSON.parse(l));
}

test("the index on GitHub verifies with V2pn's root key; a changed byte, another key or another format does not", () => {
  const bytes = fixture('root/partner-index.json');
  const sig = fixture('root/partner-index.json.sig').toString('utf8');
  assert.deepEqual(loadConfig({}).rootKeys, [ROOT_PARTNER_KEY]);
  const index = readRootIndex(bytes, sig, [ROOT_PARTNER_KEY]);
  assert.deepEqual(index.partners, [{ id: 'configra', name: 'Configra', domains: ['angleford.co', '*.angleford.co'] }]);
  assert.equal(index.published, '2026-11-23T23:30:43Z');

  const changed = Buffer.from(bytes);
  changed[changed.indexOf('angleford')] = 'b'.charCodeAt(0);
  assert.throws(() => readRootIndex(changed, sig, [ROOT_PARTNER_KEY]), /not signed/);
  const other = crypto.generateKeyPairSync('ec', { namedCurve: 'P-256' }).privateKey;
  assert.throws(() => readRootIndex(bytes, sig, [publicKeyOf(other)]), /not signed/);
  assert.throws(() => readRootIndex(bytes, 'not base64 at all', [ROOT_PARTNER_KEY]), /not signed/);
  const notIndex = Buffer.from('{"schema":2,"partners":[]}');
  assert.throws(() => readRootIndex(notIndex, signature(other, notIndex), [publicKeyOf(other)]), /not a partner index/);

  // The fixed list of the settings.
  assert.deepEqual(parseRootList(' configra = angleford.co, *.ANGLEFORD.co ; kestrel=kestrel.example '), [
    { id: 'configra', name: 'configra', domains: ['angleford.co', '*.angleford.co'] },
    { id: 'kestrel', name: 'kestrel', domains: ['kestrel.example'] },
  ]);
  assert.deepEqual(parseRootList(''), []);
  for (const bad of ['configra', 'x=angleford.co', 'configra=', 'configra=127.0.0.1', 'configra=a.example,']) {
    assert.throws(() => parseRootList(bad), /HUB_ROOT_PARTNERS/, bad);
  }
});

test('root partners keep their ids and domains: new ids skip them, saving on their domain fails, clashes are logged, a hub partner with their id is taken down', async () => {
  const key = crypto.generateKeyPairSync('ec', { namedCurve: 'P-256' }).privateKey;
  const roots = rootIndex(key, [
    { id: 'falcon', name: 'Falcon', domains: ['falcon.test', '*.falcon.test'] },
    { id: 'kestrel', name: 'Kestrel', domains: ['kestrel.test'] },
    { id: 'configra', name: 'Configra', domains: ['angleford.co', '*.angleford.co'] },
  ]);
  // GitHub's release files, on this computer.
  let mode = 'ok';
  const github = await serve((req, res) => {
    if (mode === 'down') { res.writeHead(503); res.end(); return; }
    const body = req.url.endsWith('.sig') ? roots.sig : mode === 'tampered' ? Buffer.from(roots.bytes.toString().replace('falcon.test', 'falcom.test')) : roots.bytes;
    res.end(body);
  });
  const sub = await serve((req, res) => res.end(Buffer.from('vless://id@s1.example.test:443?security=reality#S1').toString('base64')));
  const env = await startHub({
    net: { ...testNet, tcp: false },
    env: { HUB_ROOT_INDEX_URL: `http://github.test:${github.port}/latest/download/partner-index.json`, HUB_ROOT_KEYS: publicKeyOf(key) },
  });
  const save = async (login, name, host) => {
    const c = new Client(env.base);
    await register(c, login);
    await proveDomain(c, host);
    return { c, r: await c.put('/api/partner', { name, subscription: `http://${host}:${sub.port}/s/${login}`, brand: ['#2F6BFF'] }) };
  };
  try {
    const { hub } = env;
    assert.deepEqual(hub.roots.list(), [], 'nothing before the first read');

    // Two hub partners made before the hub knew the root index: one on Falcon's domain, one with Falcon's id.
    const sokol = await save('sokol', 'Sokol', 'sub.falcon.test');
    assert.equal(sokol.r.status, 200, JSON.stringify(sokol.r.data));
    const early = await save('falcon-team', 'Falcon', 'sub.early.test');
    assert.equal(early.r.data.partner.id, 'falcon');
    hub.importPartner(configraFolder);

    const pages = path.join(env.config.publicDir, 'p');
    assert.ok(fs.existsSync(path.join(pages, 'falcon', 'index.html')));
    const lines = await logged(async () => assert.equal(await hub.refreshRoots(), true));
    assert.deepEqual(hub.roots.list().map(p => p.id), ['falcon', 'kestrel', 'configra']);
    const clash = 'a hub partner collides with a root partner';
    const warnings = lines.filter(l => l.msg === clash).map(l => [l.partner, l.root, l.why]);
    assert.deepEqual(warnings, [['falcon', 'falcon', 'id'], ['sokol', 'falcon', 'domain']],
      'configra, imported from the root partner itself, is no clash');

    // The one with Falcon's id is taken down by a publication at once: kept with its work, out of the index, its page
    // gone (/p/falcon/ is the root partner's address), with the reason for its provider and a line in the audit log.
    // Sokol, on Falcon's domain under its own id, is taken down too: V2pn leaves it out, and its page would show a
    // provider's name on V2pn's site to people that provider does not serve.
    assert.deepEqual(lines.filter(l => l.level === 'warn' && l.msg !== clash).map(l => [l.msg, l.partner]),
      [["a hub partner holds a root partner's id: taken down", 'falcon'], ["a hub partner holds a root partner's domain: taken down", 'sokol']]);
    const sokolNow = (await sokol.c.get('/api/partner')).data.partner;
    assert.deepEqual([sokolNow.state, sokolNow.downReason],
      ['down', 'Домен подписки принадлежит партнёру Falcon, которого V2pn подписал сам. Напишите модераторам V2pn.']);
    assert.equal(hub.auditLog().find(a => a.action === 'partner.down' && a.target === 'sokol').detail.root, 'falcon');
    const falcon = (await early.c.get('/api/partner')).data.partner;
    assert.deepEqual([falcon.id, falcon.state, falcon.downReason],
      ['falcon', 'down', 'Id «falcon» принадлежит партнёру Falcon, которого V2pn подписал сам. Напишите модераторам V2pn.']);
    const index = JSON.parse(fs.readFileSync(path.join(env.config.publicDir, 'partners', 'latest', 'download', 'partner-index.json')));
    assert.deepEqual(index.partners.map(p => p.id), ['configra']);
    assert.ok(!fs.existsSync(path.join(pages, 'falcon')));
    assert.ok(fs.existsSync(path.join(pages, 'configra', 'index.html')), 'the imported Configra (source import) is not touched');
    const entry = hub.auditLog().find(a => a.action === 'partner.down' && a.target === 'falcon');
    assert.deepEqual([entry.actor, entry.detail.root], ['system', 'falcon']);
    assert.throws(() => hub.restore('falcon', 'owner'), e => e.code === 'state', 'a moderator cannot bring it back');
    assert.equal((await early.c.put('/api/partner', { name: 'Falcon', brand: ['#2F6BFF'] })).data.partner.state, 'down', 'saving keeps it down');
    assert.ok(!fs.existsSync(path.join(pages, 'falcon')));

    // A new partner never gets a root id.
    const kestrel = await save('kestrel-team', 'Kestrel', 'sub.k-team.test');
    assert.equal(kestrel.r.status, 200);
    assert.equal(kestrel.r.data.partner.id, 'kestrel-2');

    // A root partner's domain: refused before anything is checked, with the reason.
    const rival = await save('rival', 'Rival', 'vpn.falcon.test');
    assert.equal(rival.r.status, 409);
    assert.equal(rival.r.data.error.code, 'domain_taken');
    assert.equal(rival.r.data.error.message, 'Домен vpn.falcon.test принадлежит партнёру Falcon, которого V2pn подписал сам.');
    assert.equal(rival.r.data.error.fields.subscription, rival.r.data.error.message);
    assert.equal((await rival.c.get('/api/partner')).data.partner, null);
    const check = await rival.c.post('/api/partner/check', { subscription: `http://kestrel.test:${sub.port}/s/x` });
    assert.deepEqual([check.data.check.ok, check.data.domain, check.data.taken], [true, 'kestrel.test', true]);
    // The partner already on it (taken down) cannot save it again; the imported Configra keeps its own domains.
    const again = await sokol.c.put('/api/partner', { name: 'Sokol', brand: ['#2F6BFF'] });
    assert.equal(again.data.error.code, 'domain_taken');
    assert.throws(() => hub.restore('sokol', 'owner'), e => e.code === 'domain_taken', 'nor can a moderator bring it back on that domain');
    assert.equal(hub.domainOwner(['angleford.co'], 'configra'), null);
    assert.equal(hub.domainOwner(['vpn.angleford.co'], 'sokol').id, 'configra');

    // GitHub down or a file that does not verify: the last good copy stays.
    mode = 'down';
    assert.equal(await hub.refreshRoots(), false);
    mode = 'tampered';
    assert.equal(await hub.refreshRoots(), false);
    assert.deepEqual(hub.roots.list().map(p => p.id), ['falcon', 'kestrel', 'configra']);

    // The copy on disk is read at the next start, without GitHub; one that does not verify is left aside.
    await github.close();
    const second = createHub(env.config, { net: testNet });
    try {
      assert.deepEqual(second.roots.list().map(p => p.id), ['falcon', 'kestrel', 'configra']);
      assert.ok(second.roots.fetchedAt);
    } finally {
      second.db.close();
    }
    const saved = JSON.parse(fs.readFileSync(env.config.rootIndexFile, 'utf8'));
    const bytes = Buffer.from(saved.index, 'base64');
    bytes[bytes.indexOf('kestrel.test')] = 'K'.charCodeAt(0);
    fs.writeFileSync(env.config.rootIndexFile, JSON.stringify({ ...saved, index: bytes.toString('base64') }));
    const third = createHub(env.config, { net: testNet });
    try {
      assert.deepEqual(third.roots.list(), []);
    } finally {
      third.db.close();
    }
  } finally {
    await sub.close();
    await github.close().catch(() => {});
    await env.close();
  }
});

test('the fixed list of the settings is kept without any index', async () => {
  const env = await startHub({ env: { HUB_ROOT_PARTNERS: 'nordline=nordline.test,*.nordline.test' } });
  try {
    assert.equal(env.hub.freeId('Nordline'), 'nordline-2');
    assert.equal(env.hub.domainOwner(['sub.nordline.test']).root, true);
    assert.equal(path.basename(env.config.rootIndexFile), 'root-partner-index.json');
    assert.ok(!fs.existsSync(env.config.rootIndexFile));
  } finally {
    await env.close();
  }
});
