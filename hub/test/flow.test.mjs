// The provider's whole way through the hub and the owner's decisions, over HTTP, with the files it publishes.
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { publicKeyOf, verify } from '../lib/keys.mjs';
import { setQuiet } from '../lib/log.mjs';
import { verifyPack } from '../lib/publish.mjs';
import { readZip } from '../lib/zip.mjs';
import { Client, makePng, serve, signIn, startHub } from './helpers.mjs';

const sha = b => crypto.createHash('sha256').update(b).digest('hex');

function published(env) {
  const dir = path.join(env.config.publicDir, 'partners', 'latest', 'download');
  const index = fs.readFileSync(path.join(dir, 'partner-index.json'));
  const sig = fs.readFileSync(path.join(dir, 'partner-index.json.sig'), 'utf8');
  assert.match(sig, /^[A-Za-z0-9+/=]+\n$/);
  assert.ok(verify(publicKeyOf(env.hub.key), index, sig), 'the index is signed with the hub key');
  const parsed = JSON.parse(index);
  for (const p of parsed.partners) verifyPack(p, fs.readFileSync(path.join(dir, p.pack.file)));
  return { dir, index: parsed, files: fs.readdirSync(dir).sort() };
}

test('apply, get checked, be rejected and approved, design a look, get published, be revoked', async () => {
  // Every log line of the run, to prove the test link and codes never reach the log.
  const logged = [];
  const write = process.stdout.write.bind(process.stdout);
  const writeErr = process.stderr.write.bind(process.stderr);
  process.stdout.write = chunk => { logged.push(String(chunk)); return true; };
  process.stderr.write = chunk => { logged.push(String(chunk)); return true; };
  setQuiet(false);

  const servers = await serve((req, res) => res.end());
  let subBody = '';
  const sub = await serve((req, res) => {
    res.writeHead(200, { 'profile-title': 'Kestrel', 'support-url': 'https://t.me/kestrel_support' });
    res.end(subBody);
  });
  let apiMode = 'good';
  const api = await serve((req, res) => {
    if (apiMode === 'down') { res.writeHead(500); res.end(); return; }
    if (req.method === 'POST' && req.url === '/api/login/start') {
      res.setHeader('content-type', 'application/json');
      res.end(JSON.stringify({ code: '482913', token: 'T'.repeat(40), url: 'https://t.me/kestrel_bot?start=v2pn-482913', expires: 600 }));
      return;
    }
    if (req.method === 'GET' && req.url === `/api/login/poll?token=${'T'.repeat(40)}`) { res.end(JSON.stringify({ state: 'waiting' })); return; }
    res.writeHead(404);
    res.end();
  });
  const env = await startHub();
  const testUrl = `http://sub.kestrel.test:${sub.port}/s/secret-token-123`;
  try {
    const provider = new Client(env.base);
    const owner = new Client(env.base);
    await signIn(env, provider, 'boss@kestrel.example');
    const link = new URL(env.hub.auth.createAdminLink('owner@example.com'));
    await owner.get(link.pathname + link.search);

    // ---- the application: fields checked one by one
    let r = await provider.put('/api/application', { providerId: 'V2PN', name: 'Kestrel V2pn', domains: 'kestrel.test, *.', testUrl: 'ftp://x' });
    assert.equal(r.status, 422);
    assert.deepEqual(Object.keys(r.data.error.fields).sort(), ['domains', 'name', 'providerId', 'testUrl']);
    r = await provider.put('/api/application', { providerId: 'kestrel', name: 'Kestrel' });
    assert.equal(r.data.application.state, 'draft');
    r = await provider.post('/api/application/submit');
    assert.equal(r.data.error.code, 'invalid', 'domains, link and contact are still missing');
    r = await provider.put('/api/application', { domains: ['kestrel.test', '*.kestrel.test'], testUrl, contact: '@kestrel_admin', wantedTier: 2, note: 'Hi' });
    assert.deepEqual(r.data.application.domains, ['kestrel.test', '*.kestrel.test']);

    // ---- the check fails (no servers): the application stays a draft, the result is kept
    subBody = '<html>login</html>';
    r = await provider.post('/api/application/submit');
    assert.equal(r.status, 422);
    assert.equal(r.data.error.code, 'check_failed');
    assert.equal(r.data.error.detail.lines.find(l => l.id === 'servers').level, 'fail');
    assert.equal((await provider.get('/api/application')).data.application.state, 'draft');

    // ---- a working link: submitted
    subBody = Buffer.from([1, 2].map(i => `vless://id@s${i}.kestrel.test:${servers.port}?security=reality#S${i}`).join('\n')).toString('base64');
    const check = await provider.post('/api/check', { url: testUrl, domains: ['*.kestrel.test'] });
    assert.equal(check.data.check.ok, true);
    r = await provider.post('/api/application/submit');
    assert.equal(r.status, 200);
    assert.equal(r.data.application.state, 'submitted');
    assert.equal(r.data.application.check.ok, true);
    assert.equal((await provider.put('/api/application', { note: 'later' })).data.error.code, 'state');

    // ---- someone else cannot take the id, nor see the application
    const rival = new Client(env.base);
    await signIn(env, rival, 'rival@example.com');
    assert.equal((await rival.put('/api/application', { providerId: 'kestrel' })).data.error.code, 'id_taken');
    assert.equal((await rival.get('/api/application')).data.application, null);
    assert.equal((await rival.get('/api/admin/applications')).status, 403);

    // ---- the owner rejects, the provider fixes and sends again
    const list = await owner.get('/api/admin/applications?state=submitted');
    const appId = list.data.applications[0].id;
    assert.equal(list.data.applications[0].testUrl, testUrl, 'the owner sees the test link');
    assert.equal(list.data.applications[0].email, 'boss@kestrel.example');
    assert.equal((await owner.post(`/api/admin/applications/${appId}/reject`, {})).data.error.code, 'invalid');
    r = await owner.post(`/api/admin/applications/${appId}/reject`, { reason: 'Нет ссылки на кабинет' });
    assert.equal(r.data.application.state, 'rejected');
    const seen = (await provider.get('/api/application')).data.application;
    assert.deepEqual([seen.state, seen.rejectReason], ['rejected', 'Нет ссылки на кабинет']);
    await provider.put('/api/application', { note: 'Добавили кабинет' });
    assert.equal((await provider.post('/api/application/submit')).data.application.state, 'submitted');

    // ---- approved at tier 1: published at once with its name
    r = await owner.post(`/api/admin/applications/${appId}/approve`, { tier: 1 });
    assert.equal(r.status, 200);
    assert.equal(r.data.provider.tier, 1);
    let pub = published(env);
    assert.deepEqual(pub.index.partners.map(p => [p.id, p.name, p.grants.join(',')]), [['kestrel', 'Kestrel', 'edition']]);
    assert.ok(pub.files.includes('partners.json') && pub.files.includes('partners.json.sig'));
    assert.deepEqual(JSON.parse(fs.readFileSync(path.join(pub.dir, 'partners.json'))).partners, []);
    const page = fs.readFileSync(path.join(env.config.publicDir, 'p', 'kestrel', 'index.html'), 'utf8');
    assert.match(page, /<script src="page.js"><\/script>/);
    assert.doesNotMatch(page.replace(/<script type="application\/json"[^]*?<\/script>/, ''), /<script>/, 'no inline script (CSP script-src self)');
    assert.ok(fs.existsSync(path.join(env.config.publicDir, 'p', 'kestrel', 'page.js')));

    // ---- a second provider cannot claim the same domains
    await rival.put('/api/application', { providerId: 'rival', name: 'Rival', domains: ['sub.kestrel.test'], testUrl, contact: '@rival' });
    assert.equal((await rival.post('/api/application/submit')).data.error.code, 'domain_taken');

    // ---- the cabinet: pictures
    let cab = await provider.get('/api/cabinet');
    assert.equal(cab.data.provider.id, 'kestrel');
    assert.equal(cab.data.share.button, `${env.origin}/p/kestrel/#sub=`);
    const up = (slot, bytes, type = 'image/png') => provider.request('POST', `/api/cabinet/upload?slot=${slot}`, bytes, { 'content-type': type });
    assert.equal((await up('logo', Buffer.from('GIF89a'), 'image/gif')).data.error.code, 'invalid');
    assert.equal((await up('logo', Buffer.from('not a png at all, not at all, not at all'))).data.error.fields.file, 'Нужен файл PNG.');
    assert.match((await up('mark', makePng(300, 96))).data.error.fields.file, /^Картинка 300×96; нужен квадратный знак/);
    const logo = makePng(340, 96, [255, 255, 255]);
    const logoDark = makePng(340, 96, [20, 20, 20]);
    const mark = makePng(128, 128);
    const backdrop = makePng(1600, 1000, [30, 60, 200]);
    const ids = {};
    for (const [slot, bytes] of [['logo', logo], ['logoOnLight', logoDark], ['mark', mark], ['backdrop', backdrop]]) {
      r = await up(slot, bytes);
      assert.equal(r.status, 200, slot);
      ids[slot] = r.data.picture.sha256;
      assert.equal(ids[slot], sha(bytes));
    }
    assert.deepEqual((await provider.get(`/api/cabinet/pictures/${ids.mark}`)).data, mark);
    assert.equal((await rival.get(`/api/cabinet/pictures/${ids.mark}`)).status, 404, 'only the provider and the owner see its pictures');
    assert.deepEqual((await owner.get(`/api/admin/pictures/${ids.mark}`)).data, mark);

    // ---- the look: tier 2 things are refused at tier 1, the rest goes to the queue
    const look = {
      name: 'Kestrel', brand: ['#2F7BFF', '#7C4DFF'], background: 'image', ...ids,
      links: { cabinet: 'https://kestrel.test/cabinet', support: 'https://t.me/kestrel_support' },
      api: 'https://api.kestrel.test/api/', login: true,
      actions: [{ label: 'Самый быстрый', icon: 'bolt', do: 'connect.best', strategy: 'ping' }],
    };
    r = await provider.put('/api/cabinet/look', look);
    assert.equal(r.status, 200);
    assert.deepEqual(Object.keys(r.data.review.problems).sort(), ['actions', 'api', 'login']);
    assert.ok(r.data.review.colours.dark.accent.startsWith('#'));
    r = await provider.post('/api/cabinet/look/submit');
    assert.equal(r.data.error.code, 'look_invalid');
    await provider.put('/api/cabinet/look', { ...look, api: '', login: false, actions: [] });
    r = await provider.post('/api/cabinet/look/submit');
    assert.equal(r.data.provider.queueState, 'queued');

    // ---- the owner returns it once, then publishes it
    let queue = await owner.get('/api/admin/queue');
    assert.equal(queue.data.queue.length, 1);
    assert.deepEqual(queue.data.queue[0].review.problems, {});
    r = await owner.post('/api/admin/queue/kestrel/return', { reason: 'Логотип мелковат' });
    assert.equal(r.data.provider.queueState, 'returned');
    cab = await provider.get('/api/cabinet');
    assert.equal(cab.data.provider.returnReason, 'Логотип мелковат');
    await provider.post('/api/cabinet/look/submit');
    r = await owner.post('/api/admin/queue/kestrel/approve');
    assert.equal(r.status, 200);
    pub = published(env);
    const entry = pub.index.partners[0];
    const zip = readZip(fs.readFileSync(path.join(pub.dir, entry.pack.file)));
    assert.deepEqual([...zip.keys()].sort(), ['background.png', 'logo-dark.png', 'logo-light.png', 'mark.png', 'partner.json']);
    const pack = JSON.parse(zip.get('partner.json'));
    assert.deepEqual(pack.brand, { accent: '#2F7BFF', logo: 'logo-light.png', logoOnLight: 'logo-dark.png', mark: 'mark.png' });
    assert.deepEqual(pack.theme, { palette: { brand: ['#2F7BFF', '#7C4DFF'] }, background: { type: 'image', art: 'background.png' } });
    assert.deepEqual(pack.links, { cabinet: 'https://kestrel.test/cabinet', support: 'https://t.me/kestrel_support' });
    assert.equal(pack.api, undefined);
    const pageDir = path.join(env.config.publicDir, 'p', 'kestrel');
    assert.deepEqual(fs.readdirSync(pageDir).sort(), ['background.png', 'index.html', 'logo-dark.png', 'logo-light.png', 'page.js']);

    // ---- tier 2: asked for, given, the sign-in API tested, the look with API and buttons published
    assert.equal((await provider.post('/api/cabinet/login-test', {})).data.error.code, 'tier');
    r = await provider.post('/api/cabinet/tier-request');
    assert.equal(r.data.provider.tierRequest, 2);
    r = await owner.post('/api/admin/providers/kestrel/tier', { tier: 2 });
    assert.deepEqual([r.data.provider.tier, r.data.provider.tierRequest], [2, null]);
    const apiUrl = `http://api.kestrel.test:${api.port}/api/`;
    r = await provider.post('/api/cabinet/login-test', { api: apiUrl });
    assert.equal(r.data.test.ok, true, JSON.stringify(r.data.test));
    assert.deepEqual(r.data.test.lines.map(l => [l.id, l.level]), [['start', 'ok'], ['poll', 'ok']]);
    apiMode = 'down';
    r = await provider.post('/api/cabinet/login-test', { api: apiUrl });
    assert.deepEqual(r.data.test.lines.map(l => [l.id, l.level]), [['start', 'fail']]);
    assert.equal((await provider.post('/api/cabinet/login-test', { api: 'https://evil.example/api/' })).data.error.fields.api !== undefined, true);
    await provider.put('/api/cabinet/look', look);
    await provider.post('/api/cabinet/look/submit');
    await owner.post('/api/admin/queue/kestrel/approve');
    pub = published(env);
    assert.deepEqual(pub.index.partners[0].grants, ['edition', 'account', 'actions']);
    const pack2 = JSON.parse(readZip(fs.readFileSync(path.join(pub.dir, 'partner-kestrel.zip'))).get('partner.json'));
    assert.deepEqual(pack2.actions, [{ id: 'button-1', label: 'Самый быстрый', icon: 'bolt', do: 'connect.best', strategy: 'ping' }]);
    assert.equal(pack2.login, true);

    // ---- term and revocation
    assert.equal((await owner.post('/api/admin/providers/kestrel/until', { until: '2020-01-01' })).data.error.code, 'invalid');
    r = await owner.post('/api/admin/providers/kestrel/until', { until: '2028-02-29' });
    assert.equal(r.data.provider.until, '2028-02-29');
    assert.equal(published(env).index.partners[0].until, '2028-02-29');
    r = await owner.post('/api/admin/providers/kestrel/revoke', { reason: 'Договор закончился' });
    assert.equal(r.data.provider.state, 'revoked');
    pub = published(env);
    assert.deepEqual(pub.index.partners, []);
    assert.ok(!pub.files.includes('partner-kestrel.zip'));
    assert.ok(!fs.existsSync(pageDir), 'the page goes with the partner');
    r = await owner.post('/api/admin/providers/kestrel/restore');
    assert.equal(published(env).index.partners.length, 1);

    // ---- the record of every decision
    const audit = (await owner.get('/api/admin/audit')).data.audit.map(a => a.action);
    for (const action of ['application.submit', 'application.reject', 'application.approve', 'look.submit', 'look.return', 'look.approve',
      'provider.tier_request', 'provider.tier', 'provider.until', 'provider.revoke', 'provider.restore']) {
      assert.ok(audit.includes(action), action);
    }
    const providers = (await owner.get('/api/admin/providers')).data.providers;
    assert.deepEqual(providers.map(p => [p.id, p.email, p.contact]), [['kestrel', 'boss@kestrel.example', '@kestrel_admin']]);
  } finally {
    process.stdout.write = write;
    process.stderr.write = writeErr;
    setQuiet(true);
    await env.close();
    await sub.close();
    await api.close();
    await servers.close();
  }
  const all = logged.join('');
  assert.ok(all.includes('"msg":"request"'), 'requests are logged');
  assert.ok(!all.includes('secret-token-123') && !all.includes('sub.kestrel.test'), 'the test link is never logged');
  assert.ok(!/\b\d{6}\b/.test(all.replace(/"ms":\d+/g, '').replace(/"t":"[^"]+"/g, '').replace(/"port":\d+/g, '')), 'no code in the log');
  assert.ok(!all.includes('hub_s='), 'no cookie in the log');
});

test('downloads redirect to GitHub and are counted by day and name, without anything about the person', async () => {
  const env = await startHub();
  try {
    const c = new Client(env.base);
    const gh = 'https://github.com/Piwqust/V2pn/releases';
    const cases = [
      ['/r/windows-x64', `${gh}/latest/download/V2pn-windows-x64.zip`],
      ['/r/windows-arm64', `${gh}/latest/download/V2pn-windows-arm64.zip`],
      ['/r/macos-arm64', `${gh}/latest/download/V2pn-macos-arm64.dmg`],
      ['/r/macos-x64', `${gh}/latest/download/V2pn-macos-x64.dmg`],
      ['/r/install.ps1', `${gh}/latest/download/install.ps1`],
      ['/r/install.sh', `${gh}/latest/download/install.sh`],
      ['/r/windows-x64', `${gh}/latest/download/V2pn-windows-x64.zip`],
      ['/releases/latest/download/release.json', `${gh}/latest/download/release.json`],
      ['/releases/latest/download/release.json.sig', `${gh}/latest/download/release.json.sig`],
      ['/releases/latest/download/V2pn-windows-arm64-update.zip', `${gh}/latest/download/V2pn-windows-arm64-update.zip`],
      ['/releases/download/v1.2.2/V2pn-windows-x64-update.zip', `${gh}/download/v1.2.2/V2pn-windows-x64-update.zip`],
    ];
    for (const [p, location] of cases) {
      const r = await c.get(p);
      assert.equal(r.status, 302, p);
      assert.equal(r.headers.get('location'), location);
      assert.equal(r.headers.get('set-cookie'), null);
    }
    for (const p of ['/r/linux', '/r/../etc/passwd', '/releases/latest/download/evil.exe', '/releases/download/latest/release.json', '/releases/download/v1.2.2/../x', '/']) {
      assert.equal((await c.get(p)).status, 404, p);
    }
    const owner = new Client(env.base);
    const link = new URL(env.hub.auth.createAdminLink('owner@example.com'));
    await owner.get(link.pathname + link.search);
    const stats = (await owner.get('/api/admin/downloads?days=7')).data.downloads;
    assert.deepEqual(stats.totals, { 'install.ps1': 1, 'install.sh': 1, 'macos-arm64': 1, 'macos-x64': 1, 'windows-arm64': 1, 'windows-x64': 2 });
    assert.deepEqual(Object.keys(env.hub.db.get('SELECT * FROM downloads')).sort(), ['count', 'day', 'name']);
  } finally {
    await env.close();
  }
});

test("import-partner takes partners/configra as an approved tier 2 provider with Configra's look", async () => {
  const env = await startHub();
  try {
    const result = env.hub.importPartner(new URL('../../partners/configra', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'), { email: 'owner@configra.example' });
    assert.deepEqual([result.id, result.tier, result.grants.join(',')], ['configra', 2, 'edition,account']);
    env.hub.publish('test');
    const pub = published(env);
    const entry = pub.index.partners.find(p => p.id === 'configra');
    assert.deepEqual({ ...entry, pack: undefined }, {
      id: 'configra', name: 'Configra', domains: ['angleford.co', '*.angleford.co'], grants: ['edition', 'account'], until: '2027-11-16', pack: undefined,
    });
    const zip = readZip(fs.readFileSync(path.join(pub.dir, entry.pack.file)));
    const folder = new URL('../../partners/configra/', import.meta.url);
    // The pack is Configra's own: the same partner.json (line ends aside) and the same pictures.
    assert.equal(zip.get('partner.json').toString(), fs.readFileSync(new URL('partner.json', folder), 'utf8').replace(/\r\n/g, '\n'));
    for (const f of ['logo-dark.png', 'logo-light.png', 'mark.png']) assert.deepEqual(zip.get(f), fs.readFileSync(new URL(f, folder)));
    // Imported again: updated in place, not doubled.
    env.hub.importPartner(new URL('../../partners/configra', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'));
    assert.equal(env.hub.db.get('SELECT COUNT(*) AS n FROM providers').n, 1);
    const configraPage = fs.readFileSync(path.join(env.config.publicDir, 'p', 'configra', 'index.html'), 'utf8');
    assert.match(configraPage, /Configra на компьютере/);
    assert.match(configraPage, /irm http:\/\/127\.0\.0\.1:\d+\/r\/install\.ps1 \| iex/);
  } finally {
    await env.close();
  }
});
