// A provider's whole way through the hub and the moderators' decisions, over HTTP, with the files it publishes.
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { publicKeyOf, verify } from '../lib/keys.mjs';
import { setQuiet } from '../lib/log.mjs';
import { verifyPack } from '../lib/publish.mjs';
import { readZip } from '../lib/zip.mjs';
import { Client, makePng, register, serve, signIn, signInOwner, startHub } from './helpers.mjs';

const sha = b => crypto.createHash('sha256').update(b).digest('hex');
const configraFolder = new URL('../../partners/configra', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1');

export function published(env) {
  const dir = path.join(env.config.publicDir, 'partners', 'latest', 'download');
  const index = fs.readFileSync(path.join(dir, 'partner-index.json'));
  const sig = fs.readFileSync(path.join(dir, 'partner-index.json.sig'), 'utf8');
  assert.match(sig, /^[A-Za-z0-9+/=]+\n$/);
  assert.ok(verify(publicKeyOf(env.hub.key), index, sig), 'the index is signed with the hub key');
  const parsed = JSON.parse(index);
  for (const p of parsed.partners) verifyPack(p, fs.readFileSync(path.join(dir, p.pack.file)));
  return { dir, index: parsed, files: fs.readdirSync(dir).sort() };
}

const packOf = (pub, id) => readZip(fs.readFileSync(path.join(pub.dir, `partner-${id}.zip`)));

test('register, save = published, special rights wait for a moderator, take down, complaints, passwords', async () => {
  // Every log line of the run, to prove subscription links, passwords and cookies never reach the log.
  const logged = [];
  const write = process.stdout.write.bind(process.stdout);
  const writeErr = process.stderr.write.bind(process.stderr);
  process.stdout.write = chunk => { logged.push(String(chunk)); return true; };
  process.stderr.write = chunk => { logged.push(String(chunk)); return true; };
  setQuiet(false);

  const servers = await serve((req, res) => res.end());
  let subBody = '';
  const sub = await serve((req, res) => {
    res.writeHead(200, { 'profile-title': 'Nordline', 'support-url': 'https://t.me/nordline_support' });
    res.end(subBody);
  });
  const env = await startHub();
  const subUrl = `http://sub.nordline.test:${sub.port}/s/secret-token-123`;
  try {
    const provider = new Client(env.base);
    const owner = new Client(env.base);
    await register(provider, 'nordline', 'nord-pass-1');
    const ownerPassword = await signInOwner(env, owner);

    // ---- nothing yet
    let r = await provider.get('/api/partner');
    assert.equal(r.data.partner, null);
    assert.equal(r.data.look.layout, 'v2pn');

    // ---- pictures before the first save
    const up = (slot, bytes, type = 'image/png') => provider.request('POST', `/api/partner/upload?slot=${slot}`, bytes, { 'content-type': type });
    assert.equal((await up('logo', Buffer.from('GIF89a'), 'image/gif')).data.error.code, 'invalid');
    assert.match((await up('mark', makePng(300, 96))).data.error.fields.file, /^Картинка 300×96; нужен квадратный знак/);
    const mark = makePng(128, 128, [47, 107, 255]);
    const logo = makePng(340, 96, [255, 255, 255]);
    const ids = {};
    for (const [slot, bytes] of [['mark', mark], ['logo', logo]]) {
      r = await up(slot, bytes);
      assert.equal(r.status, 200, slot);
      ids[slot] = r.data.picture.sha256;
      assert.equal(ids[slot], sha(bytes));
    }
    assert.deepEqual((await provider.get(`/api/partner/pictures/${ids.mark}`)).data, mark);

    // ---- the form, checked field by field; the link is checked from the server
    const form = {
      name: 'Nordline', subscription: subUrl, brand: ['#2F6BFF', '#13C4B5'], background: 'gradient', layout: 'stickers', ...ids,
      links: { support: 't.me/nordline_support' },
    };
    r = await provider.put('/api/partner', { ...form, name: 'Nordline V2pn', brand: [], layout: 'tiles' });
    assert.equal(r.status, 422);
    assert.deepEqual(Object.keys(r.data.error.fields), ['layout']);
    r = await provider.put('/api/partner', { ...form, subscription: 'ftp://x' });
    assert.deepEqual(Object.keys(r.data.error.fields), ['subscription']);
    r = await provider.put('/api/partner', { ...form, subscription: `http://127.0.0.1:${sub.port}/s/x` });
    assert.match(r.data.error.fields.subscription, /на домене/);
    r = await provider.put('/api/partner', { ...form, name: 'My V2pn', brand: [] });
    assert.equal(r.data.error.code, 'look_invalid');
    assert.deepEqual(Object.keys(r.data.error.fields).sort(), ['brand', 'name']);

    subBody = '<html>login</html>';
    r = await provider.put('/api/partner', form);
    assert.equal(r.status, 422);
    assert.equal(r.data.error.code, 'check_failed');
    assert.equal(r.data.error.detail.lines.find(l => l.id === 'servers').level, 'fail');
    assert.equal((await provider.get('/api/partner')).data.partner, null, 'nothing saved');

    subBody = Buffer.from([1, 2].map(i => `vless://id@s${i}.nordline.test:${servers.port}?security=reality#S${i}`).join('\n')).toString('base64');
    r = await provider.post('/api/partner/check', { subscription: subUrl });
    assert.deepEqual([r.data.check.ok, r.data.domain, r.data.taken], [true, 'sub.nordline.test', false]);
    r = await provider.put('/api/partner', form);
    assert.equal(r.status, 200, JSON.stringify(r.data));
    assert.equal(r.data.check.ok, true);
    const mine = r.data.partner;
    assert.deepEqual([mine.id, mine.state, mine.domains, mine.subscription], ['nordline', 'live', ['sub.nordline.test'], subUrl]);
    assert.equal(mine.look.links.support, 'https://t.me/nordline_support');
    assert.equal(mine.share.button, `${env.origin}/p/nordline/#sub=`);
    assert.deepEqual((await provider.get('/api/me')).data.partner, { id: 'nordline', name: 'Nordline', state: 'live' });

    // ---- published at once: the index, the pack with the layout, the page with «Пожаловаться» and no inline script
    let pub = published(env);
    assert.deepEqual(pub.index.partners.map(p => [p.id, p.name, p.domains.join(), p.grants.join(), p.reviewed]),
      [['nordline', 'Nordline', 'sub.nordline.test', 'edition', undefined]]);
    const term = new Date(Date.now() + 60 * 86400000);
    assert.equal(pub.index.partners[0].until, term.toISOString().slice(0, 10), 'a hub partner has no term: 60 days on, renewed daily');
    let pack = JSON.parse(packOf(pub, 'nordline').get('partner.json'));
    assert.deepEqual(pack.theme, { palette: { brand: ['#2F6BFF', '#13C4B5'] }, background: { type: 'gradient', colors: ['#2F6BFF', '#13C4B5'] }, layout: 'stickers' });
    assert.deepEqual(pack.links, { support: 'https://t.me/nordline_support' });
    assert.equal(pack.api, undefined);
    const pageDir = path.join(env.config.publicDir, 'p', 'nordline');
    const page = fs.readFileSync(path.join(pageDir, 'index.html'), 'utf8');
    assert.match(page, /<script src="page.js"><\/script>/);
    assert.match(page, /id="report-open" data-t="report">Пожаловаться</);
    assert.doesNotMatch(page.replace(/<script type="application\/json"[^]*?<\/script>/, ''), /<script>/, 'no inline script (CSP script-src self)');
    assert.match(fs.readFileSync(path.join(pageDir, 'page.js'), 'utf8'), /\/api\/complaints/);

    // ---- saving again with the same link does not check it again; the id stays when the name changes
    subBody = '';
    r = await provider.put('/api/partner', { ...form, name: 'Nordline Pro' });
    assert.equal(r.status, 200);
    assert.equal(r.data.check, null);
    assert.equal(r.data.partner.id, 'nordline');
    assert.equal(published(env).index.partners[0].name, 'Nordline Pro');
    subBody = Buffer.from(`vless://id@s1.nordline.test:${servers.port}?security=reality#S1`).toString('base64');

    // ---- special rights: asked, checked, pending; the partner stays live without them
    const rights = {
      routing: 'domain:ru, geoip:ru → direct\nyoutube.com → proxy\n# a comment\ndefault → proxy',
      lock: true,
      actions: [{ label: 'Продлить', icon: 'card', do: 'open.renew' }, { label: 'Тариф', icon: 'info', do: 'info.card' }],
      account: { api: 'https://sub.nordline.test/v2pn/api/', login: true },
    };
    r = await provider.put('/api/partner', { ...form, rights: { ...rights, account: { api: 'https://evil.example/api/' }, routing: 'geoip:ru → block' } });
    assert.equal(r.status, 422);
    assert.match(r.data.error.fields['rights.account.api'], /на домене подписки/);
    assert.match(r.data.error.fields['rights.routing'], /блокирует целую страну/);
    r = await provider.put('/api/partner', { ...form, rights });
    assert.equal(r.status, 200, JSON.stringify(r.data));
    assert.deepEqual(r.data.partner.pending, ['account', 'actions', 'lock', 'routing']);
    assert.deepEqual(r.data.partner.rights.routing.content,
      { schema: 1, default: 'proxy', rules: [{ domains: ['domain:ru'], ips: ['geoip:ru'], to: 'direct' }, { domains: ['domain:youtube.com'], to: 'proxy' }] });
    assert.equal(r.data.partner.rights.routing.text, 'domain:ru, geoip:ru → direct\ndomain:youtube.com → proxy');
    assert.deepEqual(published(env).index.partners[0].grants, ['edition'], 'nothing waiting goes out');

    // ---- the moderator's queue; an approval names the content it saw
    const queue = (await owner.get('/api/mod/rights')).data.rights;
    assert.deepEqual(queue.map(q => [q.partner, q.right, q.login, q.state]),
      [['nordline', 'account', 'nordline', 'pending'], ['nordline', 'actions', 'nordline', 'pending'], ['nordline', 'lock', 'nordline', 'pending'], ['nordline', 'routing', 'nordline', 'pending']]);
    const hashOf = right => queue.find(q => q.right === right).hash;
    assert.equal((await owner.post('/api/mod/partners/nordline/rights/routing/approve', { hash: 'f'.repeat(64) })).data.error.code, 'conflict');
    assert.equal((await provider.post('/api/mod/partners/nordline/rights/routing/approve', { hash: hashOf('routing') })).status, 403);
    for (const right of ['account', 'actions', 'lock', 'routing']) {
      r = await owner.post(`/api/mod/partners/nordline/rights/${right}/approve`, { hash: hashOf(right) });
      assert.equal(r.data.right.state, 'approved', right);
    }
    pub = published(env);
    let entry = pub.index.partners[0];
    assert.deepEqual(entry.grants, ['edition', 'account', 'actions', 'lock', 'routing']);
    let zip = packOf(pub, 'nordline');
    const routing1 = zip.get('routing.json');
    assert.equal(routing1.toString(), '{\n  "schema": 1,\n  "default": "proxy",\n  "rules": [\n    {\n      "domains": [\n        "domain:ru"\n      ],\n'
      + '      "ips": [\n        "geoip:ru"\n      ],\n      "to": "direct"\n    },\n    {\n      "domains": [\n        "domain:youtube.com"\n      ],\n      "to": "proxy"\n    }\n  ]\n}\n');
    assert.deepEqual(entry.reviewed, { 'routing.json': sha(routing1) });
    assert.equal(hashOf('routing'), sha(routing1), 'the approved hash is the reviewed file\'s');
    pack = JSON.parse(zip.get('partner.json'));
    assert.deepEqual([pack.api, pack.login, pack.lock], ['https://sub.nordline.test/v2pn/api/', true, true]);
    assert.deepEqual(pack.actions, [{ id: 'button-1', label: 'Продлить', icon: 'card', do: 'open.renew' }, { id: 'button-2', label: 'Тариф', icon: 'info', do: 'info.card' }]);
    assert.ok(pack.assets.some(a => a.name === 'routing.json' && a.sha256 === sha(routing1)));
    const rawIndex = fs.readFileSync(path.join(pub.dir, 'partner-index.json'), 'utf8');
    assert.ok(rawIndex.indexOf('"reviewed"') > rawIndex.indexOf('"grants"') && rawIndex.indexOf('"reviewed"') < rawIndex.indexOf('"until"'),
      'reviewed sits between grants and until, as V2pnPartnerPublisher.IndexBytes writes it');

    // ---- the provider changes the rules: pending again, the pack keeps what was approved
    r = await provider.put('/api/partner', { ...form, rights: { ...rights, routing: 'domain:ru → direct' } });
    assert.equal(r.data.partner.rights.routing.state, 'pending');
    assert.equal(r.data.partner.rights.routing.live, true);
    assert.equal(r.data.partner.rights.routing.approvedText, 'domain:ru, geoip:ru → direct\ndomain:youtube.com → proxy');
    assert.equal(r.data.partner.rights.lock.state, 'approved', 'the others stay approved');
    pub = published(env);
    assert.deepEqual(pub.index.partners[0].reviewed, { 'routing.json': sha(routing1) });
    assert.deepEqual(packOf(pub, 'nordline').get('routing.json'), routing1);

    // ---- denied with a reason: the provider sees it; the older approved rules stay
    const newHash = (await owner.get('/api/mod/rights')).data.rights.find(q => q.right === 'routing').hash;
    assert.equal((await owner.post('/api/mod/partners/nordline/rights/routing/deny', { hash: newHash })).data.error.code, 'invalid');
    r = await owner.post('/api/mod/partners/nordline/rights/routing/deny', { hash: newHash, reason: 'Слишком широко' });
    assert.equal(r.data.right.state, 'denied');
    let view = (await provider.get('/api/partner')).data.partner;
    assert.deepEqual([view.rights.routing.state, view.rights.routing.reason], ['denied', 'Слишком широко']);
    assert.deepEqual(published(env).index.partners[0].reviewed, { 'routing.json': sha(routing1) });

    // ---- a right no longer asked for leaves the pack; denying an approved right takes it back
    r = await provider.put('/api/partner', { ...form, rights: { ...rights, lock: false, routing: 'domain:ru → direct' } });
    assert.equal(r.data.partner.rights.lock, undefined);
    entry = published(env).index.partners[0];
    assert.deepEqual(entry.grants, ['edition', 'account', 'actions', 'routing']);
    r = await owner.post('/api/mod/partners/nordline/rights/account/deny', { hash: hashOf('account'), reason: 'API не отвечает' });
    assert.equal(r.data.right.live, false);
    pub = published(env);
    pack = JSON.parse(packOf(pub, 'nordline').get('partner.json'));
    assert.equal(pack.api, undefined);
    assert.deepEqual(pack.actions.map(a => a.do), ['open.renew'], 'a button that needs the API goes with it');
    assert.deepEqual(pub.index.partners[0].grants, ['edition', 'actions', 'routing']);

    // ---- one domain, one partner
    const rival = new Client(env.base);
    await register(rival, 'rival');
    r = await rival.put('/api/partner', { ...form, name: 'Rival', mark: null, logo: null, subscription: `http://sub.nordline.test:${sub.port}/other` });
    assert.equal(r.data.error.code, 'domain_taken');
    assert.equal((await rival.get(`/api/partner/pictures/${ids.mark}`)).status, 404, 'only the account and moderators see its pictures');
    assert.deepEqual((await owner.get(`/api/mod/pictures/${ids.mark}`)).data, mark);

    // ---- complaints: anonymous, limited, counted for moderators, cleared by them
    const visitor = new Client(env.base);
    assert.equal((await visitor.post('/api/complaints', { partner: 'nobody' })).status, 404);
    assert.equal((await visitor.post('/api/complaints', { partner: 'nordline', text: 'x'.repeat(1001) })).data.error.code, 'invalid');
    assert.equal((await visitor.post('/api/complaints', { partner: 'nordline', text: 'Не работает' })).status, 200);
    assert.equal((await visitor.post('/api/complaints', { partner: 'nordline' })).status, 429, 'one a day per address and partner');
    assert.equal((await visitor.request('POST', '/api/complaints', { partner: 'nordline' }, { 'x-forwarded-for': '203.0.113.9' })).status, 200);
    let list = (await owner.get('/api/mod/partners')).data.partners;
    assert.deepEqual(list.map(p => [p.id, p.login, p.complaints, p.state, p.layout, p.domains.join()]),
      [['nordline', 'nordline', 2, 'live', 'stickers', 'sub.nordline.test']]);
    assert.deepEqual(list[0].pending, []);
    assert.equal(list[0].check.ok, true);
    assert.equal(list[0].subscription, undefined, 'moderators do not see the subscription link');
    const detail = (await owner.get('/api/mod/partners/nordline')).data.partner;
    assert.deepEqual(detail.complaintList.map(c => c.text), ['', 'Не работает']);
    assert.deepEqual((await owner.post('/api/mod/partners/nordline/complaints/clear')).data, { cleared: 2 });

    // ---- taken down: out of the index, the zip gone, the page says so; restored: back
    assert.equal((await owner.post('/api/mod/partners/nordline/down', {})).data.error.code, 'invalid');
    r = await owner.post('/api/mod/partners/nordline/down', { reason: 'Жалобы пользователей' });
    assert.equal(r.data.partner.state, 'down');
    pub = published(env);
    assert.deepEqual(pub.index.partners, []);
    assert.ok(!pub.files.includes('partner-nordline.zip'));
    const downPage = fs.readFileSync(path.join(pageDir, 'index.html'), 'utf8');
    assert.match(downPage, /Nordline снят модератором V2pn/);
    assert.doesNotMatch(downPage, /<script/);
    assert.deepEqual(fs.readdirSync(pageDir), ['index.html']);
    assert.equal((await visitor.request('POST', '/api/complaints', { partner: 'nordline' }, { 'x-forwarded-for': '203.0.113.10' })).status, 404);
    view = (await provider.get('/api/partner')).data.partner;
    assert.deepEqual([view.state, view.downReason], ['down', 'Жалобы пользователей']);
    // Saving does not bring it back.
    assert.equal((await provider.put('/api/partner', form)).data.partner.state, 'down');
    assert.deepEqual(published(env).index.partners, []);
    r = await owner.post('/api/mod/partners/nordline/restore');
    assert.equal(r.data.partner.state, 'live');
    assert.equal(published(env).index.partners.length, 1);
    assert.match(fs.readFileSync(path.join(pageDir, 'index.html'), 'utf8'), /data-t="report"/);

    // ---- moderators: only the owner adds and removes them
    r = await owner.post('/api/mod/moderators', { login: 'damir' });
    assert.equal(r.status, 200);
    const modPassword = r.data.moderator.password;
    assert.match(modPassword, /^[a-z2-9]{4}-[a-z2-9]{4}-[a-z2-9]{4}$/);
    const mod = new Client(env.base);
    await signIn(mod, 'damir', modPassword);
    assert.equal((await mod.get('/api/mod/partners')).status, 200);
    assert.equal((await mod.get('/api/mod/moderators')).status, 403);
    assert.equal((await mod.post('/api/mod/moderators', { login: 'rival' })).status, 403);
    r = await owner.post('/api/mod/moderators', { login: 'rival' });
    assert.equal(r.data.moderator.password, null, 'an existing account keeps its password');
    assert.deepEqual((await owner.get('/api/mod/moderators')).data.moderators.map(m => [m.login, m.role]),
      [['owner', 'owner'], ['damir', 'moderator'], ['rival', 'moderator']]);

    // ---- password resets: a moderator resets a provider's, not another moderator's nor the owner's
    r = await mod.post('/api/mod/accounts/nordline/password');
    assert.equal(r.status, 200);
    assert.equal(r.data.login, 'nordline');
    assert.equal((await provider.get('/api/me')).status, 401, 'the old sessions end');
    await signIn(provider, 'nordline', r.data.password);
    assert.equal((await mod.post('/api/mod/accounts/rival/password')).status, 403);
    assert.equal((await mod.post('/api/mod/accounts/owner/password')).status, 403);
    assert.equal((await owner.post('/api/mod/accounts/rival/password')).status, 200);
    assert.equal((await owner.post('/api/mod/accounts/nobody/password')).status, 404);
    assert.equal((await owner.post('/api/mod/moderators/rival/remove')).data.moderator.role, 'provider');
    assert.equal((await owner.post('/api/mod/moderators/rival/remove')).status, 404);

    // ---- the record of every decision
    const audit = (await owner.get('/api/mod/audit')).data.audit.map(a => a.action);
    for (const action of ['partner.create', 'partner.save', 'right.request', 'right.approve', 'right.deny', 'right.withdraw', 'complaints.clear',
      'partner.down', 'partner.restore', 'moderator.add', 'moderator.remove', 'account.password']) {
      assert.ok(audit.includes(action), action);
    }
    const all = logged.join('');
    for (const secret of ['secret-token-123', 'sub.nordline.test', 'nord-pass-1', ownerPassword, modPassword, 'hub_s=']) {
      assert.ok(!all.includes(secret), `the log never has ${secret}`);
    }
    assert.ok(all.includes('"msg":"request"'), 'requests are logged');
  } finally {
    process.stdout.write = write;
    process.stderr.write = writeErr;
    setQuiet(true);
    await env.close();
    await sub.close();
    await servers.close();
  }
});

test('the sign-in API test calls login/start and login/poll as V2pn would', async () => {
  const servers = await serve((req, res) => res.end());
  const sub = await serve((req, res) => res.end(Buffer.from(`vless://id@s1.kestrel.test:${servers.port}?security=reality#S1`).toString('base64')));
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
  try {
    const c = new Client(env.base);
    await register(c, 'kestrel');
    assert.equal((await c.post('/api/partner/login-test', {})).data.error.code, 'state');
    const r = await c.put('/api/partner', { name: 'Kestrel', brand: ['#2F7BFF'], subscription: `http://api.kestrel.test:${sub.port}/s/1` });
    assert.equal(r.status, 200, JSON.stringify(r.data));
    const apiUrl = `http://api.kestrel.test:${api.port}/api/`;
    let t = await c.post('/api/partner/login-test', { api: apiUrl });
    assert.deepEqual(t.data.test.lines.map(l => [l.id, l.level]), [['start', 'ok'], ['poll', 'ok']]);
    apiMode = 'down';
    t = await c.post('/api/partner/login-test', { api: apiUrl });
    assert.deepEqual(t.data.test.lines.map(l => [l.id, l.level]), [['start', 'fail']]);
    assert.ok((await c.post('/api/partner/login-test', { api: 'https://evil.example/api/' })).data.error.fields.api);
  } finally {
    await env.close();
    await sub.close();
    await api.close();
    await servers.close();
  }
});

test('downloads redirect to GitHub and are counted by day and name, without anything about the person', async () => {
  const env = await startHub();
  try {
    const c = new Client(env.base);
    const gh = 'https://github.com/Piwqust/V2pn/releases';
    const cases = [
      ['/r/windows-x64', `${gh}/latest/download/V2pn-windows-x64-setup.exe`],
      ['/r/windows-arm64', `${gh}/latest/download/V2pn-windows-arm64-setup.exe`],
      ['/r/windows-x64-zip', `${gh}/latest/download/V2pn-windows-x64.zip`],
      ['/r/windows-arm64-zip', `${gh}/latest/download/V2pn-windows-arm64.zip`],
      ['/r/macos-arm64', `${gh}/latest/download/V2pn-macos-arm64.dmg`],
      ['/r/macos-x64', `${gh}/latest/download/V2pn-macos-x64.dmg`],
      ['/r/install.ps1', `${gh}/latest/download/install.ps1`],
      ['/r/install.sh', `${gh}/latest/download/install.sh`],
      ['/r/windows-x64', `${gh}/latest/download/V2pn-windows-x64-setup.exe`],
      ['/releases/latest/download/V2pn-windows-x64-setup.exe', `${gh}/latest/download/V2pn-windows-x64-setup.exe`],
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
    await signInOwner(env, owner);
    const stats = (await owner.get('/api/mod/downloads?days=7')).data.downloads;
    assert.deepEqual(stats.totals, { 'install.ps1': 1, 'install.sh': 1, 'macos-arm64': 1, 'macos-x64': 1, 'windows-arm64': 1, 'windows-arm64-zip': 1, 'windows-x64': 2, 'windows-x64-zip': 1 });
    assert.deepEqual(Object.keys(env.hub.db.get('SELECT * FROM downloads')).sort(), ['count', 'day', 'name']);
  } finally {
    await env.close();
  }
});

test("import-partner takes partners/configra as a live partner with Configra's look and its account right approved", async () => {
  const env = await startHub();
  try {
    const result = env.hub.importPartner(configraFolder);
    assert.deepEqual([result.id, result.login, result.grants.join(',')], ['configra', 'configra', 'edition,account']);
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
    // Imported again: updated in place, not doubled; another login moves it to that account.
    env.hub.importPartner(configraFolder, { login: 'configra-team' });
    assert.equal(env.hub.db.get('SELECT COUNT(*) AS n FROM partners').n, 1);
    assert.equal(env.hub.db.get("SELECT a.login FROM partners p JOIN accounts a ON a.id = p.account_id WHERE p.id = 'configra'").login, 'configra-team');
    assert.equal(env.hub.db.get("SELECT password FROM accounts WHERE login = 'configra-team'").password, null, 'no password until a reset');
    const configraPage = fs.readFileSync(path.join(env.config.publicDir, 'p', 'configra', 'index.html'), 'utf8');
    assert.match(configraPage, /Configra на компьютере/);
    assert.match(configraPage, /irm http:\/\/127\.0\.0\.1:\d+\/r\/install\.ps1 \| iex/);

    // Its provider signs in after a reset and edits it like any other; its domains stay while the link is on them.
    const password = env.hub.auth.resetPassword(env.hub.db.get("SELECT id FROM accounts WHERE login = 'configra-team'").id);
    const c = new Client(env.base);
    await signIn(c, 'configra-team', password);
    const view = (await c.get('/api/partner')).data.partner;
    assert.deepEqual([view.id, view.source, view.rights.account.state], ['configra', 'import', 'approved']);
  } finally {
    await env.close();
  }
});

test('once a day the index is made again, and an imported partner whose term has ended drops out', async () => {
  const env = await startHub();
  try {
    const { hub } = env;
    hub.importPartner(configraFolder);
    hub.publish('test');
    assert.equal(published(env).index.partners.length, 1);
    // The term ends today; the last publication was yesterday.
    hub.db.run('UPDATE partners SET until = ? WHERE id = ?', new Date().toISOString().slice(0, 10), 'configra');
    hub.db.setMeta('published_day', '2000-01-01');
    hub.maintain();
    assert.deepEqual(published(env).index.partners, []);
    assert.ok(!fs.existsSync(path.join(env.config.publicDir, 'p', 'configra')));
    assert.equal(hub.db.meta('published_day'), new Date().toISOString().slice(0, 10));
  } finally {
    await env.close();
  }
});
