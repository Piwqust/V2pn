// A partner's cabinet and support links lead to its own site (its domain, a parent domain that is no public suffix, the
// names under them) or to Telegram; its buttons keep V2pn's narrower rule.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { setQuiet } from '../lib/log.mjs';
import { actionProblem, defaultLook, lookProblems } from '../lib/look.mjs';
import { PUBLIC_SUFFIXES, linkAllowed, normalizeDomain, partnerLink, partnerSite } from '../lib/validate.mjs';
import { readZip } from '../lib/zip.mjs';
import { Client, proveDomain, register, serve, startHub, testNet } from './helpers.mjs';

test('the site of a domain: up to the last parent that is no public suffix', () => {
  assert.equal(partnerSite('sub.nordline.com'), 'nordline.com');
  assert.equal(partnerSite('a.b.sub.nordline.com'), 'nordline.com');
  assert.equal(partnerSite('*.angleford.co'), 'angleford.co');
  assert.equal(partnerSite('nordline.com'), 'nordline.com');
  assert.equal(partnerSite('sub.nordline.co.uk'), 'nordline.co.uk');
  assert.equal(partnerSite('vpn.nordline.com.ru'), 'nordline.com.ru');
  assert.equal(partnerSite('nordline.pp.ua'), 'nordline.pp.ua');
  assert.equal(partnerSite('sub.nordline.workers.dev'), 'nordline.workers.dev');
  assert.equal(partnerSite('bucket.s3.amazonaws.com'), 'bucket.s3.amazonaws.com', 'stops at the first suffix on the way up');
  assert.equal(partnerSite('co.uk'), null);
  assert.equal(partnerSite('workers.dev'), null);
  assert.equal(partnerSite('com'), null);
  // The list as V2pn copies it: lowercase domains of two labels or more, each once.
  assert.equal(new Set(PUBLIC_SUFFIXES).size, PUBLIC_SUFFIXES.length);
  for (const s of PUBLIC_SUFFIXES) assert.ok(normalizeDomain(s) === s && s.split('.').length >= 2, s);
  assert.ok(Object.isFrozen(PUBLIC_SUFFIXES));
});

test('links: https on the subscription domain or its site (a parent domain, never a public suffix), names under them, t.me or telegram.me; buttons keep V2pn\'s narrower rule', () => {
  const domains = ['sub.nordline.test'];
  for (const ok of ['https://sub.nordline.test/cabinet', 'https://my.sub.nordline.test/', 'https://nordline.test/pay', 'https://lk.nordline.test/',
    'https://xsub.nordline.test/', 'https://NORDLINE.test./', 'https://t.me/nordline', 'https://telegram.me/nordline', 'https://T.ME./x']) {
    assert.equal(linkAllowed(ok, domains), true, ok);
  }
  for (const bad of ['https://test/', 'https://evil.example/sub.nordline.test', 'https://sub.nordline.test.evil.example/', 'https://nordline.test.evil.example/',
    'https://xnordline.test/', 'http://sub.nordline.test/', 'http://nordline.test/', 'tg://resolve?domain=nordline', 'https://user@nordline.test/', '']) {
    assert.equal(linkAllowed(bad, domains), false, bad);
  }
  assert.equal(linkAllowed('https://configra.angleford.co/cabinet', ['angleford.co', '*.angleford.co']), true);
  // Never above a public suffix.
  const uk = ['sub.nordline.co.uk'];
  assert.deepEqual(['https://nordline.co.uk/', 'https://pay.nordline.co.uk/', 'https://co.uk/', 'https://other.co.uk/', 'https://uk/'].map(u => linkAllowed(u, uk)),
    [true, true, false, false, false]);
  const hosted = ['nordline.workers.dev'];
  assert.deepEqual(['https://nordline.workers.dev/lk', 'https://pay.nordline.workers.dev/', 'https://workers.dev/', 'https://rival.workers.dev/'].map(u => linkAllowed(u, hosted)),
    [true, true, false, false]);

  // The button rule is V2pnPartnerCatalog.ActionLink's: t.me and the listing's domains only.
  assert.equal(partnerLink('https://t.me/x', domains), true);
  assert.equal(partnerLink('https://telegram.me/x', domains), false);
  assert.equal(partnerLink('https://my.sub.nordline.test/', domains), false);
  const button = url => ({ label: 'Бот', icon: '', do: 'open.link', strategy: '', purpose: '', server: '', group: '', url });
  assert.equal(actionProblem(button('https://sub.nordline.test/pay'), '', domains), null);
  assert.equal(actionProblem(button('tg://resolve?domain=nordline'), '', domains), null);
  assert.match(actionProblem(button('https://telegram.me/nordline'), '', domains), /на домене вашей подписки/);

  // The look: checked against the partner's domains when they are known.
  const look = { ...defaultLook('Nordline'), brand: ['#2F6BFF'], links: { cabinet: 'https://evil.example/pay', support: 'https://t.me/nordline' } };
  assert.deepEqual(lookProblems(look, { pictures: new Map() }), {}, 'no domains yet: only https is checked');
  assert.deepEqual(lookProblems(look, { pictures: new Map(), domains }), {
    'links.cabinet': 'Ссылка должна вести на nordline.test или его поддомен, либо на t.me.',
  });
  assert.deepEqual(Object.keys(lookProblems({ ...look, links: { cabinet: 'http://sub.nordline.test/', support: '' } }, { pictures: new Map(), domains })), ['links.cabinet']);
});

test('saving refuses a link off the domain; a partner saved earlier with one goes out without it', async () => {
  const sub = await serve((req, res) => res.end(Buffer.from('vless://id@s1.nordline.test:443?security=reality#S1').toString('base64')));
  const env = await startHub({ net: { ...testNet, tcp: false } });
  try {
    const provider = new Client(env.base);
    await register(provider, 'nordline');
    await proveDomain(provider, 'sub.nordline.test');
    const form = { name: 'Nordline', subscription: `http://sub.nordline.test:${sub.port}/s/x`, brand: ['#2F6BFF'], links: { support: 't.me/nordline' } };
    let r = await provider.put('/api/partner', { ...form, links: { ...form.links, cabinet: 'https://nordline-pay.example/' } });
    assert.equal(r.status, 422);
    assert.equal(r.data.error.code, 'look_invalid');
    assert.deepEqual(Object.keys(r.data.error.fields), ['links.cabinet']);
    r = await provider.put('/api/partner', { ...form, links: { ...form.links, cabinet: 'https://lk.nordline.test/' } });
    assert.equal(r.status, 200, JSON.stringify(r.data));
    assert.deepEqual(r.data.partner.look.links, { cabinet: 'https://lk.nordline.test/', support: 'https://t.me/nordline' }, 'the parent domain\'s names pass');

    // A look saved before the rule, with a cabinet elsewhere.
    const look = JSON.parse(env.hub.db.get("SELECT look FROM partners WHERE id = 'nordline'").look);
    env.hub.db.run("UPDATE partners SET look = ? WHERE id = 'nordline'", JSON.stringify({ ...look, links: { cabinet: 'https://nordline-pay.example/', support: 'https://t.me/nordline' } }));
    const lines = [];
    const write = process.stdout.write.bind(process.stdout);
    process.stdout.write = chunk => { lines.push(String(chunk)); return true; };
    setQuiet(false);
    try {
      env.hub.publish('test');
    } finally {
      process.stdout.write = write;
      setQuiet(true);
    }
    const warning = lines.filter(l => l.startsWith('{')).map(l => JSON.parse(l)).find(l => l.level === 'warn');
    assert.deepEqual([warning.msg, warning.partner, warning.links], ['published without', 'nordline', ['cabinet']]);
    const dir = path.join(env.config.publicDir, 'partners', 'latest', 'download');
    const pack = JSON.parse(readZip(fs.readFileSync(path.join(dir, 'partner-nordline.zip'))).get('partner.json'));
    assert.deepEqual(pack.links, { support: 'https://t.me/nordline' });
    assert.ok(!fs.readFileSync(path.join(env.config.publicDir, 'p', 'nordline', 'index.html'), 'utf8').includes('nordline-pay.example'));
    // The provider sees why.
    r = await provider.get('/api/partner');
    assert.match(r.data.review.problems['links.cabinet'], /nordline\.test или его поддомен/);
  } finally {
    await sub.close();
    await env.close();
  }
});
