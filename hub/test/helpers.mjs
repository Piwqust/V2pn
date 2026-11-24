// Shared pieces of the hub's tests: temporary hubs on a free port, requests with a cookie jar, PNGs, local servers.
import fs from 'node:fs';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';
import zlib from 'node:zlib';
import { loadConfig } from '../lib/config.mjs';
import { setQuiet } from '../lib/log.mjs';
import { createHub } from '../lib/server.mjs';
import { createHandler } from '../lib/api.mjs';
import { crc32 } from '../lib/zip.mjs';

setQuiet(true);

export function tempDir(prefix = 'hub-test-') {
  return fs.mkdtempSync(path.join(os.tmpdir(), prefix));
}

/** A real PNG of one colour, <w>×<h>. */
export function makePng(w, h, rgb = [255, 122, 47]) {
  const chunk = (type, data) => {
    const len = Buffer.alloc(4);
    len.writeUInt32BE(data.length);
    const td = Buffer.concat([Buffer.from(type, 'ascii'), data]);
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(crc32(td));
    return Buffer.concat([len, td, crc]);
  };
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0);
  ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8; ihdr[9] = 2; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  const row = Buffer.alloc(1 + w * 3);
  for (let x = 0; x < w; x++) row.set(rgb, 1 + x * 3);
  const raw = Buffer.concat(Array.from({ length: h }, () => row));
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', zlib.deflateSync(raw)),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

/** A local HTTP server; <handler>(req, res). Resolves to {url, port, close}. */
export function serve(handler) {
  return new Promise(resolve => {
    const server = http.createServer(handler);
    server.listen(0, '127.0.0.1', () => {
      const { port } = server.address();
      resolve({ port, url: `http://127.0.0.1:${port}`, server, close: () => new Promise(r => { server.closeAllConnections(); server.close(r); }) });
    });
  });
}

// Test names that resolve to this computer; "everything allowed" stands in for public addresses.
export const testNet = {
  allowHttp: true,
  allow: () => true,
  resolve: async host => {
    if (host.endsWith('.test')) return [{ address: '127.0.0.1', family: 4 }];
    throw Object.assign(new Error('not found'), { code: 'ENOTFOUND' });
  },
};

/** A hub with its own data and public folders, on a free port; <env> adds settings. {hub, base, origin, close, dirs}. */
export async function startHub({ net = testNet, env = {} } = {}) {
  const root = tempDir();
  const server = http.createServer();
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const { port } = server.address();
  const origin = `http://127.0.0.1:${port}`;
  const config = loadConfig({
    HUB_DATA: path.join(root, 'data'),
    HUB_PUBLIC_DIR: path.join(root, 'www'),
    HUB_ORIGIN: origin,
    HUB_PORT: String(port),
    ...env,
  });
  const hub = createHub(config, { net });
  server.on('request', createHandler(hub));
  return {
    hub,
    config,
    base: origin,
    origin,
    root,
    close: async () => {
      server.closeAllConnections();
      await new Promise(r => server.close(r));
      hub.db.close();
      fs.rmSync(root, { recursive: true, force: true });
    },
  };
}

/** A browser-like client: keeps the session cookie, sends X-Hub and the hub's Origin on writes. */
export class Client {
  constructor(base, { origin = base, xhub = true } = {}) {
    this.base = base;
    this.origin = origin;
    this.xhub = xhub;
    this.cookie = '';
  }

  async request(method, pathName, body, headers = {}) {
    const h = { ...headers };
    if (this.cookie) h.cookie = this.cookie;
    if (method !== 'GET' && method !== 'HEAD') {
      if (this.xhub) h['x-hub'] = '1';
      if (this.origin) h.origin = this.origin;
    }
    let payload;
    if (Buffer.isBuffer(body)) payload = body;
    else if (body !== undefined) {
      payload = JSON.stringify(body);
      h['content-type'] = 'application/json';
    }
    const res = await fetch(this.base + pathName, { method, headers: h, body: payload, redirect: 'manual' });
    const set = res.headers.get('set-cookie');
    if (set) {
      const m = /hub_s=([^;]*)/.exec(set);
      if (m) this.cookie = m[1] ? `hub_s=${m[1]}` : '';
    }
    const type = res.headers.get('content-type') || '';
    const data = type.includes('json') ? await res.json() : Buffer.from(await res.arrayBuffer());
    return { status: res.status, headers: res.headers, data };
  }

  get(p) { return this.request('GET', p); }
  post(p, body) { return this.request('POST', p, body ?? {}); }
  put(p, body) { return this.request('PUT', p, body ?? {}); }
}

/** Registers <client> as a new provider <login> (password "secret-123"). */
export async function register(client, login, password = 'secret-123') {
  const r = await client.post('/api/auth/register', { login, password });
  if (r.status !== 200) throw new Error('register: ' + JSON.stringify(r.data));
  return r.data;
}

/** Signs <client> in as <login>. */
export async function signIn(client, login, password) {
  const r = await client.post('/api/auth/login', { login, password });
  if (r.status !== 200) throw new Error('login: ' + JSON.stringify(r.data));
  return r.data;
}

/** Makes <login> the owner (as hub.mjs owner does) and signs <client> in. */
export async function signInOwner(env, client, login = 'owner') {
  const password = env.hub.auth.makeOwner(login);
  await signIn(client, login, password);
  return password;
}

export const fixture = name => fs.readFileSync(new URL(`./fixtures/${name}`, import.meta.url));
