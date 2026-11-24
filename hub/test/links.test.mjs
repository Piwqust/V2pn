// A partner's cabinet and support links lead to its own domain or to Telegram, as its buttons do.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { setQuiet } from '../lib/log.mjs';
import { actionProblem, defaultLook, linkAllowed, lookProblems } from '../lib/look.mjs';
import { partnerLink } from '../lib/validate.mjs';
import { readZip } from '../lib/zip.mjs';
import { Client, register, serve, startHub, testNet } from './helpers.mjs';

test('links: https on the subscription domain, a name under it, t.me or telegram.me; buttons keep V2pn\'s narrower rule', () => {
  const domains = ['sub.nordline.test'];
  for (const ok of ['https://sub.nordline.test/cabinet', 'https://my.sub.nordline.test/', 'https://t.me/nordline', 'https://telegram.me/nordline', 'https://T.ME./x']) {
    assert.equal(linkAllowed(ok, domains), true, ok);
  }
  for (const bad of ['https://nordline.test/', 'https://evil.example/sub.nordline.test', 'https://sub.nordline.test.evil.example/', 'http://sub.nordline.test/',
    'tg://resolve?domain=nordline', 'https://user@sub.nordline.test/', 'https://xsub.nordline.test/', '']) {
    assert.equal(linkAllowed(bad, domains), false, bad);
  }
  assert.equal(linkAllowed('https://configra.angleford.co/cabinet', ['angleford.co', '*.angleford.co']), true);

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
    'links.cabinet': 'Ссылка должна вести на домен подписки (sub.nordline.test), его поддомен или t.me.',
  });
  assert.deepEqual(Object.keys(lookProblems({ ...look, links: { cabinet: 'http://sub.nordline.test/', support: '' } }, { pictures: new Map(), domains })), ['links.cabinet']);
});

test('saving refuses a link off the domain; a partner saved earlier with one goes out without it', async () => {
  const sub = await serve((req, res) => res.end(Buffer.from('vless://id@s1.nordline.test:443?security=reality#S1').toString('base64')));
  const env = await startHub({ net: { ...testNet, tcp: false } });
  try {
    const provider = new Client(env.base);
    await register(provider, 'nordline');
    const form = { name: 'Nordline', subscription: `http://sub.nordline.test:${sub.port}/s/x`, brand: ['#2F6BFF'], links: { support: 't.me/nordline' } };
    let r = await provider.put('/api/partner', { ...form, links: { ...form.links, cabinet: 'https://nordline-pay.example/' } });
    assert.equal(r.status, 422);
    assert.equal(r.data.error.code, 'look_invalid');
    assert.deepEqual(Object.keys(r.data.error.fields), ['links.cabinet']);
    r = await provider.put('/api/partner', { ...form, links: { ...form.links, cabinet: 'https://lk.sub.nordline.test/' } });
    assert.equal(r.status, 200, JSON.stringify(r.data));
    assert.deepEqual(r.data.partner.look.links, { cabinet: 'https://lk.sub.nordline.test/', support: 'https://t.me/nordline' });

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
    assert.match(r.data.review.problems['links.cabinet'], /домен подписки/);
  } finally {
    await sub.close();
    await env.close();
  }
});
