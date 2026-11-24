// The hub's index lives 60 days past each publication, and the hub publishes it again every day, right after start too.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { HUB_TERM_DAYS, rollingUntil } from '../lib/publish.mjs';
import { startServer } from '../lib/server.mjs';
import { Client, register, serve, startHub, testNet } from './helpers.mjs';

const today = () => new Date().toISOString().slice(0, 10);
const indexOf = env => JSON.parse(fs.readFileSync(path.join(env.config.publicDir, 'partners', 'latest', 'download', 'partner-index.json')));

async function withPartner(fn) {
  const sub = await serve((req, res) => res.end(Buffer.from('vless://id@s1.nordline.test:443?security=reality#S1').toString('base64')));
  const env = await startHub({ net: { ...testNet, tcp: false } });
  try {
    const c = new Client(env.base);
    await register(c, 'nordline');
    const r = await c.put('/api/partner', { name: 'Nordline', subscription: `http://sub.nordline.test:${sub.port}/s/x`, brand: ['#2F6BFF'] });
    assert.equal(r.status, 200, JSON.stringify(r.data));
    await fn(env);
  } finally {
    await sub.close();
    await env.close();
  }
}

test('a partner without a term of its own is in the index for 60 days from the publication', async () => {
  assert.equal(HUB_TERM_DAYS, 60);
  assert.equal(rollingUntil('2026-11-24'), '2027-01-23');
  assert.equal(rollingUntil('2028-01-15'), '2028-03-15', 'February 29th counted');
  await withPartner(async env => {
    const index = indexOf(env);
    assert.equal(index.partners[0].until, rollingUntil(today()));
    assert.ok(Date.parse(`${index.partners[0].until}T00:00:00Z`) - Date.parse(index.published) <= 60 * 86400000);
  });
});

test('the index is signed again once a day: due when not made today or a day old, redone by the maintenance round', async () => {
  await withPartner(async env => {
    const { hub } = env;
    assert.equal(hub.publishDue(), false, 'just published');
    const last = JSON.parse(hub.db.meta('published'));
    hub.db.setMeta('published', JSON.stringify({ ...last, at: new Date(Date.now() - 25 * 3600 * 1000).toISOString() }));
    assert.equal(hub.publishDue(), true, 'a day old');
    hub.db.setMeta('published', JSON.stringify(last));
    hub.db.setMeta('published_day', '2000-01-01');
    assert.equal(hub.publishDue(), true, 'not made today');

    // Two days without a publication (the hub was off): the round makes and signs it again.
    const old = new Date(Date.now() - 2 * 86400000).toISOString();
    hub.db.setMeta('published', JSON.stringify({ ...last, at: old }));
    hub.maintain();
    assert.ok(JSON.parse(hub.db.meta('published')).at > old);
    assert.equal(hub.db.meta('published_day'), today());
    assert.equal(hub.publishDue(), false);
  });
});

test('right after start the hub publishes when the last publication is more than a day old', async () => {
  await withPartner(async env => {
    const { hub } = env;
    const old = new Date(Date.now() - 3 * 86400000).toISOString();
    hub.db.setMeta('published', JSON.stringify({ ...JSON.parse(hub.db.meta('published')), at: old }));
    hub.db.setMeta('published_day', old.slice(0, 10));
    const signals = { SIGTERM: process.listeners('SIGTERM'), SIGINT: process.listeners('SIGINT') };
    hub.config.port = 0;
    const server = await startServer(hub);
    try {
      for (let i = 0; i < 100 && JSON.parse(hub.db.meta('published')).at === old; i++) await new Promise(r => setTimeout(r, 20));
      assert.ok(JSON.parse(hub.db.meta('published')).at > old, 'published at start');
      assert.equal(indexOf(env).partners[0].until, rollingUntil(today()));
    } finally {
      await new Promise(r => server.close(r));
      for (const [signal, before] of Object.entries(signals)) {
        for (const l of process.listeners(signal)) if (!before.includes(l)) process.removeListener(signal, l);
      }
    }
  });
});
