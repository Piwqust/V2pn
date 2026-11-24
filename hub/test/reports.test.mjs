// Reports of failed updates from V2pn (lib/reports.mjs): strict, limited per network, counted without anything about
// the sender, shown to moderators.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { parseUpdateReport } from '../lib/reports.mjs';
import { Client, signInOwner, startHub } from './helpers.mjs';

const good = { from: '1.4.2', to: '1.4.3', os: 'windows', arch: 'x64', stage: 'verify', code: 'bad_signature' };

/** The request V2pn makes: JSON, no cookie, no X-Hub, no Origin; <ip> as Caddy forwards it. */
function report(env, body, { ip = '198.51.100.7', type = 'application/json' } = {}) {
  return fetch(`${env.base}/api/report/update`, {
    method: 'POST',
    headers: { 'content-type': type, 'x-forwarded-for': ip, 'user-agent': 'V2pn/1.4.2' },
    body: typeof body === 'string' ? body : JSON.stringify(body),
  });
}

test('a report is checked strictly: six fields, versions, the lists, the code', () => {
  assert.deepEqual(parseUpdateReport(good), good);
  assert.deepEqual(parseUpdateReport({ ...good, from: '1.4.1-local.202611141320', to: '1.5.0-beta.2', os: 'macos', arch: 'arm64', stage: 'rollback', code: 'e-42_x' }).to, '1.5.0-beta.2');
  const bad = (change, field) => assert.throws(() => parseUpdateReport({ ...good, ...change }), e => e.code === 'invalid' && Object.keys(e.fields).join() === field, field);
  bad({ from: '1.4' }, 'from');
  bad({ from: 'v1.4.2' }, 'from');
  bad({ to: `1.4.3-${'a'.repeat(26)}` }, 'to');
  bad({ to: 143 }, 'to');
  bad({ os: 'linux' }, 'os');
  bad({ arch: 'x86' }, 'arch');
  bad({ stage: 'install' }, 'stage');
  bad({ code: 'Bad_Signature' }, 'code');
  bad({ code: 'x'.repeat(41) }, 'code');
  bad({ code: '' }, 'code');
  bad({ code: 'a b' }, 'code');
  bad({ id: 'machine-1' }, 'id');
  assert.throws(() => parseUpdateReport({ from: '1.4.2' }), e => Object.keys(e.fields).sort().join() === 'arch,code,os,stage,to');
  for (const notObject of [null, [], 'x', 5]) assert.throws(() => parseUpdateReport(notObject), e => e.code === 'bad_request');
});

test('POST /api/report/update answers 204 to V2pn without a cookie or CSRF headers, and keeps counts only', async () => {
  const env = await startHub();
  try {
    let r = await report(env, good);
    assert.equal(r.status, 204);
    assert.equal(await r.text(), '');
    assert.equal(r.headers.get('set-cookie'), null);
    assert.equal(r.headers.get('access-control-allow-origin'), null, 'no CORS: the app is not a browser');
    await report(env, good, { ip: '2001:db8:5:1::9' });
    await report(env, { ...good, os: 'macos', arch: 'arm64', stage: 'swap', code: 'access_denied' });
    await report(env, { ...good, to: '1.4.4', stage: 'download', code: 'timeout' });

    // What is not a report.
    assert.equal((await report(env, { ...good, stage: 'nope' })).status, 422);
    assert.equal((await report(env, '{"from":')).status, 400);
    assert.equal((await report(env, good, { type: 'text/plain' })).status, 415, 'a form on a web page cannot send it as JSON');
    assert.equal((await report(env, { ...good, code: 'x'.repeat(1100).slice(0, 40), pad: 'y'.repeat(1100) })).status, 413, 'at most 1 KB');
    assert.equal((await fetch(`${env.base}/api/report/update`)).status, 405);

    const owner = new Client(env.base);
    await signInOwner(env, owner);
    const { failures } = (await owner.get('/api/mod/downloads?days=7')).data;
    assert.deepEqual(failures.totals, { '1.4.3': 3, '1.4.4': 1 });
    assert.deepEqual(failures.stages, { '1.4.3': { verify: 2, swap: 1 }, '1.4.4': { download: 1 } });
    const today = new Date().toISOString().slice(0, 10);
    assert.deepEqual(failures.days.map(f => [f.day, f.version, f.os, f.arch, f.stage, f.code, f.count]), [
      [today, '1.4.3', 'macos', 'arm64', 'swap', 'access_denied', 1],
      [today, '1.4.3', 'windows', 'x64', 'verify', 'bad_signature', 2],
      [today, '1.4.4', 'windows', 'x64', 'download', 'timeout', 1],
    ]);
    // The table has no room for anything about the sender, and the "from" version is not kept either.
    assert.deepEqual(Object.keys(env.hub.db.get('SELECT * FROM update_failures')).sort(), ['arch', 'code', 'count', 'day', 'os', 'stage', 'version']);
    assert.equal((await new Client(env.base).get('/api/mod/downloads')).status, 401);
  } finally {
    await env.close();
  }
});

test('reports are limited to 20 an hour per IPv4 address or IPv6 /64, and a day keeps 200 kinds', async () => {
  const env = await startHub();
  try {
    const statuses = [];
    for (let i = 0; i < 21; i++) statuses.push((await report(env, good, { ip: `2001:db8:6:1::${i + 1}` })).status);
    assert.deepEqual(statuses, [...Array(20).fill(204), 429]);
    assert.equal((await report(env, good, { ip: '2001:db8:6:2::1' })).status, 204, 'the next /64 is another network');
    assert.equal((await report(env, { ...good, stage: 'bad' }, { ip: '2001:db8:6:2::1' })).status, 422);
    for (let i = 0; i < 18; i++) await report(env, good, { ip: '2001:db8:6:2::1' });
    assert.equal((await report(env, good, { ip: '2001:db8:6:2::1' })).status, 429, 'refused ones count too');

    // Made-up versions and codes do not grow the table without end.
    for (let i = 0; i < 250; i++) env.hub.counter.failure({ ...good, to: `9.9.${i}`, code: `c${i}` });
    const { totals, days } = env.hub.counter.failureStats(1);
    assert.equal(days.length, 201, '200 kinds (the reports above among them) and one of «other»');
    assert.equal(totals.other, 51);
    assert.ok(days.some(d => d.version === 'other' && d.code === 'other' && d.stage === 'verify' && d.os === 'windows'));
  } finally {
    await env.close();
  }
});
