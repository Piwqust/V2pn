// V2pn's own copy of runetfreedom's rule lists (lib/geo.mjs): fetched from a release served on this computer.
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { GeoMirror, parseHash } from '../lib/geo.mjs';

const sha = b => crypto.createHash('sha256').update(b).digest('hex');

async function release(files, { lie = false } = {}) {
  const requests = [];
  const server = http.createServer((req, res) => {
    requests.push(req.url);
    const name = req.url.split('/').pop();
    if (name.endsWith('.sha256sum')) {
      const data = files[name.replace(/\.sha256sum$/, '')];
      if (!data) { res.writeHead(404); res.end(); return; }
      res.end(`${lie ? sha(Buffer.from('other')) : sha(data)}  ${name.replace(/\.sha256sum$/, '')}\n`);
      return;
    }
    if (!files[name]) { res.writeHead(404); res.end(); return; }
    res.writeHead(200, { 'content-length': files[name].length });
    res.end(files[name]);
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  return { base: `http://127.0.0.1:${server.address().port}/latest/download`, requests, close: () => new Promise(r => server.close(r)) };
}

const tempDir = () => fs.mkdtempSync(path.join(os.tmpdir(), 'v2pn-geo-'));

test('parseHash reads a .sha256sum file and nothing else', () => {
  assert.equal(parseHash(`${'A'.repeat(64)}  geosite.dat\n`), 'a'.repeat(64));
  assert.equal(parseHash('<html>404</html>'), null);
  assert.equal(parseHash(''), null);
});

test('the mirror brings both files with their hashes, and fetches nothing when the release has not changed', async () => {
  const files = { 'geoip.dat': Buffer.from('geoip v1'), 'geosite.dat': Buffer.alloc(300_000, 7) };
  const gh = await release(files);
  const dir = tempDir();
  try {
    const mirror = new GeoMirror({ dir, source: gh.base });
    assert.deepEqual(await mirror.refresh(), { updated: ['geoip.dat', 'geosite.dat'], kept: [] });
    assert.deepEqual(fs.readFileSync(path.join(dir, 'geosite.dat')), files['geosite.dat']);
    assert.equal(parseHash(fs.readFileSync(path.join(dir, 'geoip.dat.sha256sum'), 'utf8')), sha(files['geoip.dat']));

    const before = gh.requests.length;
    assert.deepEqual(await mirror.refresh(), { updated: [], kept: ['geoip.dat', 'geosite.dat'] });
    assert.ok(gh.requests.slice(before).every(u => u.endsWith('.sha256sum')), 'only the hashes are asked again');
    assert.deepEqual(fs.readdirSync(dir).filter(f => f.includes('.part-')), [], 'no part files left');
  } finally {
    await gh.close();
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('a file that does not match the release hash never replaces the copy', async () => {
  const dir = tempDir();
  fs.writeFileSync(path.join(dir, 'geoip.dat'), 'the good old copy');
  const gh = await release({ 'geoip.dat': Buffer.from('tampered'), 'geosite.dat': Buffer.from('x') }, { lie: true });
  try {
    await assert.rejects(new GeoMirror({ dir, source: gh.base }).refresh(), /SHA-256/);
    assert.equal(fs.readFileSync(path.join(dir, 'geoip.dat'), 'utf8'), 'the good old copy');
    assert.deepEqual(fs.readdirSync(dir).filter(f => f.includes('.part-')), []);
  } finally {
    await gh.close();
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('a file larger than the limit is stopped', async () => {
  const dir = tempDir();
  const gh = await release({ 'geoip.dat': Buffer.alloc(5000, 1), 'geosite.dat': Buffer.from('x') });
  try {
    await assert.rejects(new GeoMirror({ dir, source: gh.base, maxBytes: 1000 }).refresh(), /larger than/);
    assert.equal(fs.existsSync(path.join(dir, 'geoip.dat')), false);
  } finally {
    await gh.close();
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
