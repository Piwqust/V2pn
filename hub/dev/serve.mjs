#!/usr/bin/env node
// A local copy of v2pn.angleford.co for working on the site: the hub's routes (/api, /r, /releases) and the site's
// files on one origin, under the live Content-Security-Policy. Development only: it is not deployed (hub/README.md).
//
//   node dev/serve.mjs [--port 8790] [--site ../site/dist] [--data <dir>] [--owner owner]
//                      [--fixture [--unproved]] [--configra] [--downloads]
//
// --site      the built site (cd site && npm run build); unknown paths get its 404.html, as Caddy serves them.
// --data      where the hub keeps its database and keys (default: a new temporary folder). The hub's public folder
//             (HUB_PUBLIC_DIR: partners/ and p/) is <data>/www; files are looked up there first, then in --site.
// --owner     the owner's login; it is made (or given a new password) at start and the password is printed.
// --fixture   a stand-in subscription on this computer, so the subscription check has something to open: the hub
//             refuses private addresses and plain http, so here (and only here) names under .test resolve to
//             127.0.0.1 and http is allowed. The test link is printed at start. Their domains prove themselves (every
//             account's DNS record is there), so a partner goes live on save; with --unproved they have none, and a
//             partner waits for its domain as on the server without the record.
// --configra  imports ../partners/configra as a live partner with its account right approved (login: configra).
// --downloads fills the download and update-check counters of the last 30 days with made-up numbers for the
//             moderators' charts.
import dns from 'node:dns';
import fs from 'node:fs';
import http from 'node:http';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHandler } from '../lib/api.mjs';
import { loadConfig } from '../lib/config.mjs';
import { isPublicIp } from '../lib/ip.mjs';
import { defaultResolve } from '../lib/net.mjs';
import { createHub } from '../lib/server.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const opt = (name, fallback) => {
  const i = args.indexOf(`--${name}`);
  if (i < 0) return fallback;
  const v = args[i + 1];
  return v === undefined || v.startsWith('--') ? true : v;
};

const port = Number(opt('port', 8790));
const siteDir = path.resolve(opt('site', path.join(here, '..', '..', 'site', 'dist')));
const dataDir = path.resolve(opt('data', fs.mkdtempSync(path.join(os.tmpdir(), 'v2pn-hub-dev-'))));
const ownerLogin = String(opt('owner', 'owner'));
const origin = `http://127.0.0.1:${port}`;
const publicDir = path.join(dataDir, 'www');
fs.mkdirSync(publicDir, { recursive: true });

// The live policy (site/README.md, Caddy).
const CSP = "default-src 'self'; base-uri 'none'; frame-ancestors 'none'; object-src 'none'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'";
const MIME = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8', '.json': 'application/json; charset=utf-8', '.svg': 'image/svg+xml', '.png': 'image/png',
  '.jpg': 'image/jpeg', '.webp': 'image/webp', '.woff2': 'font/woff2', '.ttf': 'font/ttf', '.txt': 'text/plain; charset=utf-8',
  '.zip': 'application/zip', '.sig': 'text/plain; charset=utf-8', '.ico': 'image/x-icon', '.avif': 'image/avif',
  '.xml': 'application/xml; charset=utf-8',
};

/** A stand-in subscription: a panel's headers and five servers on local ports that answer a TCP connection. */
async function startFixture() {
  const listeners = [];
  for (let i = 0; i < 5; i++) {
    const s = net.createServer(c => c.destroy());
    await new Promise(r => s.listen(0, '127.0.0.1', r));
    listeners.push(s);
  }
  const ports = listeners.map(s => s.address().port);
  const uuid = '3f1c2a9e-8b4d-4c6e-9a1f-5d2e7b8c9a01';
  const links = [
    `vless://${uuid}@de.kestrel.test:${ports[0]}?security=reality&type=tcp&flow=xtls-rprx-vision&sni=www.example.com&pbk=AAAA&fp=chrome#Германия`,
    `vless://${uuid}@nl.kestrel.test:${ports[1]}?security=reality&type=tcp&sni=www.example.com&pbk=AAAA&fp=chrome#Нидерланды`,
    `vless://${uuid}@fi.kestrel.test:${ports[2]}?security=reality&type=tcp&sni=www.example.com&pbk=AAAA&fp=chrome#Финляндия`,
    `hysteria2://${uuid}@se.kestrel.test:${ports[3]}?sni=se.kestrel.test#Швеция`,
    `hysteria2://${uuid}@pl.kestrel.test:${ports[4]}?sni=pl.kestrel.test#Польша`,
  ];
  const body = Buffer.from(links.join('\n')).toString('base64');
  const expire = Math.floor(Date.UTC(2027, 0, 31) / 1000);
  const server = http.createServer((req, res) => {
    // A provider's sign-in API (the account right), as the guide describes it: the form's «Проверить API» calls these two.
    if (req.method === 'POST' && req.url === '/v2pn/login/start') {
      const code = String(100000 + Math.floor(Math.random() * 900000));
      res.writeHead(200, { 'content-type': 'application/json' });
      res.end(JSON.stringify({ code, token: `tok_${Math.random().toString(36).slice(2)}${Math.random().toString(36).slice(2)}${Date.now()}`, url: `https://t.me/kestrel_bot?start=v2pn-${code}`, expires: 600 }));
      return;
    }
    if (req.method === 'GET' && req.url.startsWith('/v2pn/login/poll')) {
      res.writeHead(200, { 'content-type': 'application/json' });
      res.end(JSON.stringify({ state: 'waiting' }));
      return;
    }
    if (!req.url.startsWith('/s/')) {
      res.writeHead(404).end();
      return;
    }
    res.writeHead(200, {
      'content-type': 'text/plain; charset=utf-8',
      'profile-title': `base64:${Buffer.from('Kestrel').toString('base64')}`,
      'subscription-userinfo': `upload=1073741824; download=11811160064; total=53687091200; expire=${expire}`,
      'support-url': 'https://t.me/kestrel_support',
      'profile-update-interval': '12',
    });
    res.end(body);
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const p = server.address().port;
  return { url: `http://sub.kestrel.test:${p}/s/demo-7f3a`, api: `http://sub.kestrel.test:${p}/v2pn/`, close: () => { server.close(); listeners.forEach(s => s.close()); } };
}

const fixture = opt('fixture', false) ? await startFixture() : null;
let hub = null;
const devNet = fixture ? {
  allowHttp: true,
  allow: ip => ip === '127.0.0.1' || isPublicIp(ip),
  resolve: async host => (host.endsWith('.test') ? [{ address: '127.0.0.1', family: 4 }] : defaultResolve(host)),
  // Every account's proof record under .test (none with --unproved); real names ask real DNS.
  resolveTxt: async name => {
    if (!name.endsWith('.test')) return dns.promises.resolveTxt(name);
    if (opt('unproved', false)) throw Object.assign(new Error('not found'), { code: 'ENOTFOUND' });
    return hub.db.all('SELECT proof_token FROM accounts WHERE proof_token IS NOT NULL').map(r => [`v2pn-verify=${r.proof_token}`]);
  },
} : {};

const config = loadConfig({
  HUB_DATA: dataDir,
  HUB_PUBLIC_DIR: publicDir,
  HUB_ORIGIN: origin,
  HUB_PORT: String(port),
});
hub = createHub(config, { net: devNet });

if (opt('configra', false)) {
  const folder = path.join(here, '..', '..', 'partners', 'configra');
  try {
    hub.importPartner(folder);
  } catch (e) {
    console.error(`configra not imported: ${e.message}`);
  }
}
if (opt('downloads', false)) {
  const names = { 'windows-x64': 30, 'macos-arm64': 12, 'windows-arm64': 2, 'macos-x64': 2, 'install.ps1': 6, 'install.sh': 3 };
  let seed = 7;
  const rand = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
  for (let d = 29; d >= 0; d--) {
    const day = new Date(Date.now() - d * 86400000).toISOString().slice(0, 10);
    const wave = 1 + 0.5 * Math.sin(d / 3) + (d < 6 ? 0.6 : 0);
    for (const [name, base] of Object.entries(names)) {
      const n = Math.round(base * wave * (0.6 + rand() * 0.8));
      if (n > 0) hub.db.run('INSERT INTO downloads (day, name, count) VALUES (?, ?, ?) ON CONFLICT (day, name) DO UPDATE SET count = excluded.count', day, name, n);
    }
    // Update checks: 1.4.0 came out ten days ago and takes over from 1.3.2; a few stay on older versions.
    const share = Math.min(1, Math.max(0, (11 - d) / 8));
    const versions = { '1.4.0': 420 * share, '1.3.2': 400 * (1 - share) + 25, '1.3.0': 30, '1.2.2': 12, '1.4.1-local': d % 4 ? 0 : 2 };
    for (const [version, base] of Object.entries(versions)) {
      const n = Math.round(base * (0.9 + 0.01 * (29 - d)) * (0.85 + rand() * 0.3));
      if (n > 0) hub.db.run('INSERT INTO checks (day, version, count) VALUES (?, ?, ?) ON CONFLICT (day, version) DO UPDATE SET count = excluded.count', day, version, n);
    }
  }
}
try {
  hub.publish('dev');
} catch (e) {
  console.error(`publish: ${e.message}`);
}

const handle = createHandler(hub);

function staticFile(pathname) {
  let rel;
  try {
    rel = decodeURIComponent(pathname);
  } catch {
    return null;
  }
  if (rel.includes('\0')) return null;
  for (const root of [publicDir, siteDir]) {
    let f = path.join(root, rel);
    if (f !== root && !f.startsWith(root + path.sep)) return null;
    try {
      if (fs.statSync(f).isDirectory()) {
        if (!pathname.endsWith('/')) return { redirect: pathname + '/' };
        f = path.join(f, 'index.html');
      }
      if (fs.statSync(f).isFile()) return { file: f };
    } catch { /* next root */ }
  }
  return null;
}

const server = http.createServer((req, res) => {
  const url = new URL(req.url, origin);
  if (/^\/(api|r|releases)\//.test(url.pathname)) {
    handle(req, res);
    return;
  }
  const found = (req.method === 'GET' || req.method === 'HEAD') ? staticFile(url.pathname) : null;
  const headers = { 'content-security-policy': CSP, 'x-content-type-options': 'nosniff', 'referrer-policy': 'strict-origin-when-cross-origin', 'cache-control': 'no-store' };
  if (found?.redirect) {
    res.writeHead(301, { ...headers, location: found.redirect + url.search });
    res.end();
    return;
  }
  if (!found) {
    const page = path.join(siteDir, '404.html');
    if (fs.existsSync(page)) {
      res.writeHead(404, { ...headers, 'content-type': MIME['.html'] });
      fs.createReadStream(page).pipe(res);
      return;
    }
    res.writeHead(404, { ...headers, 'content-type': 'text/plain; charset=utf-8' });
    res.end('Не найдено');
    return;
  }
  const size = fs.statSync(found.file).size;
  res.writeHead(200, { ...headers, 'content-type': MIME[path.extname(found.file).toLowerCase()] || 'application/octet-stream', 'content-length': size });
  if (req.method === 'HEAD') res.end();
  else fs.createReadStream(found.file).pipe(res);
});

server.listen(port, '127.0.0.1', () => {
  console.log(`site     ${origin}/  (files: ${siteDir}, then ${publicDir})`);
  console.log(`data     ${dataDir}`);
  console.log(`owner    ${ownerLogin} / ${hub.auth.makeOwner(ownerLogin)}`);
  if (fixture) console.log(`fixture  ${fixture.url}  (domain: sub.kestrel.test)
api      ${fixture.api}`);
});

const stop = () => {
  server.close();
  fixture?.close();
  try { hub.counter.flush(); hub.db.close(); } catch { /* closing */ }
  process.exit(0);
};
process.on('SIGINT', stop);
process.on('SIGTERM', stop);
