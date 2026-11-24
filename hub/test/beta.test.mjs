// The beta channel (lib/beta.mjs): /releases/beta/... leads to the newest release with the pre-releases, found in
// GitHub's list of releases (served here by a local stand-in for its API); /releases/latest/... stays as it was.
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import http from 'node:http';
import { test } from 'node:test';
import { BetaChannel, compareTags, newestTag } from '../lib/beta.mjs';
import { Client, signInOwner, startHub } from './helpers.mjs';

const release = (tag, { draft = false, prerelease = tag.includes('-'), assets = ['release.json', 'release.json.sig', 'V2pn-windows-x64-update.zip'] } = {}) =>
  ({ tag_name: tag, draft, prerelease, assets: assets.map(name => ({ name })) });

test('versions are ordered as V2pn orders them: a beta comes before its release', () => {
  const sorted = ['v1.5.0', 'v1.4.2', 'v1.5.0-beta.10', 'v1.5.0-beta.2', 'v1.5.1-beta.1', 'v1.5.0-rc.1', 'v1.4.10'].sort(compareTags);
  assert.deepEqual(sorted, ['v1.4.2', 'v1.4.10', 'v1.5.0-beta.2', 'v1.5.0-beta.10', 'v1.5.0-rc.1', 'v1.5.0', 'v1.5.1-beta.1']);
  assert.equal(compareTags('v1.5.0-beta.2', 'v1.5.0-beta.2'), 0);
});

test('the newest release a V2pn can update from: pre-releases count, drafts and releases without a manifest do not', () => {
  assert.equal(newestTag([release('v1.4.2'), release('v1.5.0-beta.1'), release('v1.5.0-beta.2')]), 'v1.5.0-beta.2');
  // A release newer than the betas is the beta channel's answer too.
  assert.equal(newestTag([release('v1.5.0-beta.2'), release('v1.5.0')]), 'v1.5.0');
  // A stable fix of the old line does not push a newer beta out.
  assert.equal(newestTag([release('v1.4.3'), release('v1.5.0-beta.1')]), 'v1.5.0-beta.1');
  assert.equal(newestTag([
    release('v1.4.2'),
    release('v9.0.0', { draft: true }),
    release('v8.0.0-beta.1', { assets: ['release.json'] }),
    release('v7.0.0', { assets: ['V2pn-windows-x64.zip'] }),
    release('7.25.6'),
    release('v6.0.0/evil'),
    null,
  ]), 'v1.4.2');
  assert.equal(newestTag([]), null);
  assert.throws(() => newestTag({ message: 'API rate limit exceeded' }), /not a list/);
});

/** A stand-in for GitHub's API: answers the list in <state.list> with an ETag, 304 to the same ETag, <state.status> when set. */
async function api(state) {
  const requests = [];
  const server = http.createServer((req, res) => {
    requests.push({ url: req.url, etag: req.headers['if-none-match'] ?? null, agent: req.headers['user-agent'] });
    if (state.status) { res.writeHead(state.status); res.end('{"message":"no"}'); return; }
    const body = typeof state.list === 'string' ? state.list : JSON.stringify(state.list);
    const etag = `W/"${crypto.createHash('sha256').update(body).digest('hex').slice(0, 16)}"`;
    if (req.headers['if-none-match'] === etag) { res.writeHead(304); res.end(); return; }
    res.writeHead(200, { 'content-type': 'application/json', etag });
    res.end(body);
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  return { base: `http://127.0.0.1:${server.address().port}`, requests, close: () => new Promise(r => server.close(r)) };
}

test('the channel reads GitHub\'s list, asks again with the ETag, and keeps its tag when GitHub fails', async () => {
  const state = { list: [release('v1.4.2'), release('v1.5.0-beta.1')] };
  const gh = await api(state);
  try {
    const channel = new BetaChannel({ repo: 'Piwqust/V2pn', api: gh.base });
    assert.equal(channel.tag, null);
    assert.equal(await channel.refresh(), true);
    assert.equal(channel.tag, 'v1.5.0-beta.1');
    assert.equal(gh.requests[0].url, '/repos/Piwqust/V2pn/releases?per_page=20');
    assert.equal(gh.requests[0].etag, null);
    assert.equal(gh.requests[0].agent, 'V2pn-Hub');

    // Unchanged: a 304, the tag stays.
    assert.equal(await channel.refresh(), true);
    assert.ok(gh.requests[1].etag, 'the second request sends the ETag');
    assert.equal(channel.tag, 'v1.5.0-beta.1');

    state.list = [...state.list, release('v1.5.0-beta.2')];
    assert.equal(await channel.refresh(), true);
    assert.equal(channel.tag, 'v1.5.0-beta.2');

    for (const broken of [{ status: 403 }, { status: 500 }, { list: '{"not json' }, { list: { message: 'rate limited' } }]) {
      Object.assign(state, { status: undefined, list: undefined }, broken);
      assert.equal(await channel.refresh(), false, JSON.stringify(broken));
      assert.equal(channel.tag, 'v1.5.0-beta.2', JSON.stringify(broken));
    }
  } finally {
    await gh.close();
  }
});

test('/releases/beta/... redirects to the beta\'s files, /releases/latest/... stays the latest release, and beta checks are counted', async () => {
  const env = await startHub();
  try {
    const c = new Client(env.base);
    const gh = 'https://github.com/Piwqust/V2pn/releases';
    const files = ['release.json', 'release.json.sig', 'V2pn-windows-x64-update.zip', 'V2pn-macos-arm64.zip'];

    // Before the hub knows a beta: the latest release.
    assert.equal(env.hub.beta.tag, null);
    for (const f of files) {
      const r = await c.get(`/releases/beta/download/${f}`);
      assert.equal(r.status, 302, f);
      assert.equal(r.headers.get('location'), `${gh}/latest/download/${f}`);
    }
    assert.equal((await c.get('/api/health')).data.beta, null);

    env.hub.beta.tag = 'v1.5.0-beta.2';
    for (const f of files) {
      assert.equal((await c.get(`/releases/beta/download/${f}`)).headers.get('location'), `${gh}/download/v1.5.0-beta.2/${f}`, f);
      assert.equal((await c.get(`/releases/latest/download/${f}`)).headers.get('location'), `${gh}/latest/download/${f}`, f);
    }
    assert.equal((await c.get('/api/health')).data.beta, 'v1.5.0-beta.2');
    for (const p of ['/releases/beta/download/evil.exe', '/releases/beta/download/a/release.json', '/releases/beta/release.json', '/releases/beta/download/']) {
      assert.equal((await c.get(p)).status, 404, p);
    }

    // A running V2pn that takes betas asks both channels; both requests count as checks of its version.
    for (const p of ['/releases/beta/download/release.json', '/releases/latest/download/release.json']) {
      assert.equal((await c.request('GET', p, undefined, { 'user-agent': 'V2pn/1.5.0-beta.1' })).status, 302);
    }
    const owner = new Client(env.base);
    await signInOwner(env, owner);
    assert.deepEqual((await owner.get('/api/mod/downloads?days=7')).data.checks.totals, { '1.5.0-beta.1': 2 });
  } finally {
    await env.close();
  }
});
