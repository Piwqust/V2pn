// A provider proves it controls its subscription domain (a DNS TXT record or an https file with its token) before a new
// partner goes live or a live one moves to another domain (lib/proof.mjs). Partners live before proofs stay live.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { checkFile, checkRecord, proofPlaces } from '../lib/proof.mjs';
import { Client, register, serve, signInOwner, startHub, testNet, txtRecords } from './helpers.mjs';

const indexOf = env => JSON.parse(fs.readFileSync(path.join(env.config.publicDir, 'partners', 'latest', 'download', 'partner-index.json')));
const fail = code => Object.assign(new Error(code), { code });

test('the record and the file: TXT values in pieces are joined, another code or no record is told apart; the file must hold the token', async () => {
  const token = 'a'.repeat(32);
  assert.deepEqual(proofPlaces('sub.nordline.com', token), {
    record: { name: '_v2pn.sub.nordline.com', type: 'TXT', value: `v2pn-verify=${token}` },
    file: { url: 'https://sub.nordline.com/.well-known/v2pn-verify.txt', text: token },
  });
  const dns = records => ({ resolveTxt: async name => { assert.equal(name, '_v2pn.sub.nordline.com'); if (records instanceof Error) throw records; return records; } });
  assert.equal((await checkRecord('sub.nordline.com', token, dns([['v2pn-', `verify=${token}`]]))).ok, true);
  assert.equal((await checkRecord('sub.nordline.com', token, dns([[` v2pn-verify=${token} `], ['spf']]))).ok, true);
  assert.deepEqual(await checkRecord('sub.nordline.com', token, dns([[`v2pn-verify=${'b'.repeat(32)}`]])),
    { ok: false, text: 'Запись TXT _v2pn.sub.nordline.com есть, но код в ней другой.' });
  assert.equal((await checkRecord('sub.nordline.com', token, dns([[token]]))).ok, false, 'the bare token is not the record');
  for (const code of ['ENOTFOUND', 'ENODATA']) assert.equal((await checkRecord('sub.nordline.com', token, dns(fail(code)))).text, 'Записи TXT _v2pn.sub.nordline.com пока нет.');
  assert.equal((await checkRecord('sub.nordline.com', token, dns(fail('ESERVFAIL')))).text, 'DNS не ответил.');

  let body = `v2pn ${token}\n`;
  let status = 200;
  const site = await serve((req, res) => {
    assert.equal(req.url, '/.well-known/v2pn-verify.txt');
    res.writeHead(status);
    res.end(body);
  });
  try {
    const net = { ...testNet, proofUrl: domain => `http://${domain}:${site.port}/.well-known/v2pn-verify.txt` };
    assert.deepEqual(await checkFile('sub.nordline.test', token, net), { ok: true, text: 'Файл /.well-known/v2pn-verify.txt на месте.' });
    body = 'nothing here';
    assert.equal((await checkFile('sub.nordline.test', token, net)).text, 'Файл /.well-known/v2pn-verify.txt есть, но кода в нём нет.');
    status = 404;
    assert.equal((await checkFile('sub.nordline.test', token, net)).text, 'Файла /.well-known/v2pn-verify.txt нет: сайт ответил 404.');
    status = 302;
    assert.equal((await checkFile('sub.nordline.test', token, net)).ok, false, 'no redirect is followed');
    body = 'x'.repeat(5000) + token;
    status = 200;
    assert.match((await checkFile('sub.nordline.test', token, net)).text, /не открылся: ответ слишком большой/);
    assert.match((await checkFile('sub.nordline.test', token, { ...net, proofUrl: () => 'http://nowhere.invalid/' })).text, /не открылся: домен не находится в DNS/);
  } finally {
    await site.close();
  }
});

test('proved by the https file; a live partner does not move to a domain it has not proved, and does once it has', async () => {
  const servers = await serve((req, res) => res.end());
  const sub = await serve((req, res) => res.end(Buffer.from(`vless://id@s1.nordline.test:${servers.port}?security=reality#S1`).toString('base64')));
  // The provider's site, with the file for the domains in <files>.
  const files = new Map();
  const site = await serve((req, res) => {
    const host = req.headers.host.split(':')[0];
    if (req.url === '/.well-known/v2pn-verify.txt' && files.has(host)) res.end(files.get(host));
    else { res.writeHead(404); res.end(); }
  });
  const env = await startHub({ net: { ...testNet, tcp: false, proofUrl: domain => `http://${domain}:${site.port}/.well-known/v2pn-verify.txt` } });
  try {
    const c = new Client(env.base);
    await register(c, 'nordline');
    const token = (await c.get('/api/partner')).data.proof.token;
    const form = host => ({ name: 'Nordline', brand: ['#2F6BFF'], subscription: `http://${host}:${sub.port}/s/x` });
    files.set('sub.nordline.test', `${token}\n`);
    let r = await c.put('/api/partner', form('sub.nordline.test'));
    assert.equal(r.status, 200, JSON.stringify(r.data));
    assert.deepEqual([r.data.partner.state, r.data.proofCheck.how, r.data.proof.proved.how], ['live', 'https', 'https']);
    assert.match(r.data.proofCheck.dns.text, /пока нет/, 'DNS is asked first');
    assert.deepEqual(indexOf(env).partners.map(p => p.domains), [['sub.nordline.test']]);

    // A new domain without a record: refused with what to add; the partner stays where it was, live.
    r = await c.put('/api/partner', { ...form('vpn.nordline.test'), name: 'Nordline 2' });
    assert.equal(r.status, 422);
    assert.equal(r.data.error.code, 'domain_unproved');
    assert.equal(r.data.error.message, 'Домен vpn.nordline.test ещё не подтверждён. Партнёр остался на прежнем домене.');
    assert.match(r.data.error.fields.subscription, new RegExp(`запись TXT _v2pn\\.vpn\\.nordline\\.test со значением v2pn-verify=${token}`));
    assert.equal(r.data.error.detail.proof.record.name, '_v2pn.vpn.nordline.test');
    assert.equal(r.data.error.detail.proofCheck.ok, false);
    const view = (await c.get('/api/partner')).data.partner;
    assert.deepEqual([view.name, view.state, view.domains], ['Nordline', 'live', ['sub.nordline.test']]);
    assert.deepEqual(indexOf(env).partners.map(p => [p.name, p.domains.join()]), [['Nordline', 'sub.nordline.test']]);

    // «Проверить» for the link typed in the form, before saving; then the move goes through.
    r = await c.post('/api/partner/check', { subscription: `http://vpn.nordline.test:${sub.port}/s/x` });
    assert.deepEqual([r.data.proof.domain, r.data.proof.proved], ['vpn.nordline.test', null]);
    txtRecords.set('_v2pn.vpn.nordline.test', [[`v2pn-verify=${token}`]]);
    r = await c.post('/api/partner/proof', { subscription: `http://vpn.nordline.test:${sub.port}/s/x` });
    assert.deepEqual([r.data.proofCheck.domain, r.data.proofCheck.how, r.data.partner.domains], ['vpn.nordline.test', 'dns', ['sub.nordline.test']]);
    r = await c.put('/api/partner', { ...form('vpn.nordline.test'), name: 'Nordline 2' });
    assert.equal(r.status, 200, JSON.stringify(r.data));
    assert.equal(r.data.proofCheck, null, 'proved already');
    assert.deepEqual(indexOf(env).partners.map(p => [p.name, p.domains.join()]), [['Nordline 2', 'vpn.nordline.test']]);

    // Another account's token proves nothing: the record is the account's own.
    const rival = new Client(env.base);
    await register(rival, 'rival');
    r = await rival.put('/api/partner', { ...form('www.nordline.test'), name: 'Rival' });
    txtRecords.set('_v2pn.www.nordline.test', [[`v2pn-verify=${token}`]]);
    r = await rival.post('/api/partner/proof', {});
    assert.deepEqual([r.data.partner.state, r.data.proofCheck.ok], ['pending', false]);
    assert.equal(r.data.proofCheck.dns.text, 'Запись TXT _v2pn.www.nordline.test есть, но код в ней другой.');
  } finally {
    await env.close();
    await site.close();
    await sub.close();
    await servers.close();
  }
});

test('moderators mark a domain proved by hand; a partner live before proofs stays live, and its record replaces that', async () => {
  const sub = await serve((req, res) => res.end(Buffer.from('vless://id@s1.nordline.test:443?security=reality#S1').toString('base64')));
  const env = await startHub({ net: { ...testNet, tcp: false } });
  try {
    const owner = new Client(env.base);
    await signInOwner(env, owner);
    const c = new Client(env.base);
    await register(c, 'kestrel');
    const form = host => ({ name: 'Kestrel', brand: ['#2F7BFF'], subscription: `http://${host}:${sub.port}/s/x` });
    let r = await c.put('/api/partner', form('sub.kestrel.test'));
    assert.equal(r.data.partner.state, 'pending');
    let item = (await owner.get('/api/mod/partners')).data.partners[0];
    assert.deepEqual([item.state, item.proof], ['pending', { state: 'missing', how: null, missing: ['sub.kestrel.test'] }]);

    r = await owner.post('/api/mod/partners/kestrel/proof', {});
    assert.equal(r.status, 200, JSON.stringify(r.data));
    assert.deepEqual([r.data.partner.state, r.data.partner.proof.state, r.data.partner.proof.how], ['live', 'proved', 'moderator']);
    assert.deepEqual(indexOf(env).partners.map(p => p.id), ['kestrel']);
    const audit = (await owner.get('/api/mod/audit')).data.audit;
    const entry = audit.find(a => a.action === 'domain.proof');
    assert.deepEqual([entry.actor, entry.target, entry.detail], ['owner', 'kestrel', { domain: 'sub.kestrel.test', how: 'moderator' }]);
    assert.ok(audit.some(a => a.action === 'partner.live' && a.actor === 'owner'));
    assert.equal((await c.post('/api/mod/partners/kestrel/proof', {})).status, 403, 'moderators only');
    assert.equal((await owner.post('/api/mod/partners/kestrel/proof', { domain: 'not a domain' })).data.error.code, 'invalid');

    // Taken down, it moves to a domain it has not proved (allowed: nobody sees it); restored, it waits for that domain.
    await owner.post('/api/mod/partners/kestrel/down', { reason: 'Проверка' });
    r = await c.put('/api/partner', form('new.kestrel.test'));
    assert.deepEqual([r.status, r.data.partner.state, r.data.partner.domains], [200, 'down', ['new.kestrel.test']]);
    r = await owner.post('/api/mod/partners/kestrel/restore');
    assert.equal(r.data.partner.state, 'pending');
    assert.deepEqual(indexOf(env).partners, []);
    r = await owner.post('/api/mod/partners/kestrel/proof', { domain: 'NEW.kestrel.test' });
    assert.equal(r.data.partner.state, 'live');

    // A partner from before proofs (as migration 4 leaves it): live, «grandfathered»; with the record, proved.
    env.hub.db.run("UPDATE proofs SET how = 'grandfathered' WHERE domain = 'new.kestrel.test'");
    r = await c.get('/api/partner');
    assert.deepEqual([r.data.partner.state, r.data.proof.domain, r.data.proof.proved.how], ['live', 'new.kestrel.test', 'grandfathered']);
    assert.equal((await owner.get('/api/mod/partners')).data.partners[0].proof.state, 'grandfathered');
    r = await c.post('/api/partner/proof', {});
    assert.deepEqual([r.data.proofCheck.ok, r.data.partner.state, r.data.proof.proved.how], [false, 'live', 'grandfathered'], 'no record yet: nothing changes');
    txtRecords.set('_v2pn.new.kestrel.test', [[`v2pn-verify=${r.data.proof.token}`]]);
    r = await c.post('/api/partner/proof', {});
    assert.deepEqual([r.data.proofCheck.how, r.data.proof.proved.how], ['dns', 'dns']);
  } finally {
    await env.close();
    await sub.close();
  }
});

test('proof checks are limited per account; a save of a partner not live yet still goes through, waiting', async () => {
  const sub = await serve((req, res) => res.end(Buffer.from('vless://id@s1.nordline.test:443?security=reality#S1').toString('base64')));
  const env = await startHub({ net: { ...testNet, tcp: false } });
  try {
    const c = new Client(env.base);
    const { account } = await register(c, 'nordline');
    const id = env.hub.auth.accountByLogin(account.login).id;
    for (let i = 0; i < 30; i++) env.hub.limiter.hit(`proof:${id}`, Infinity, 3600 * 1000);
    let r = await c.put('/api/partner', { name: 'Nordline', brand: ['#2F6BFF'], subscription: `http://sub.nordline.test:${sub.port}/s/x` });
    assert.equal(r.status, 200, JSON.stringify(r.data));
    assert.deepEqual([r.data.partner.state, r.data.proofCheck.dns.text], ['pending', 'Проверим позже: проверок за час было слишком много.']);
    r = await c.post('/api/partner/proof', {});
    assert.equal(r.status, 429);
    assert.equal((await new Client(env.base).post('/api/partner/proof', {})).status, 401);
    const fresh = new Client(env.base);
    await register(fresh, 'fresh');
    assert.equal((await fresh.post('/api/partner/proof', {})).data.error.fields.subscription, 'Вставьте ссылку на подписку: домен берётся из неё.');
  } finally {
    await env.close();
    await sub.close();
  }
});
