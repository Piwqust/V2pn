// Requests the hub makes to providers' servers (the test subscription, the sign-in API, the TCP check), safe against
// SSRF: the host is resolved here, every address it has must be public, and the connection goes to the address that
// was checked, with the original host name for TLS (SNI and the certificate) and the Host header, so a DNS answer that
// changes between the check and the connection cannot redirect it. Redirects are followed only to https, each hop
// checked the same way. Time and size are capped for the whole exchange.
import dns from 'node:dns';
import http from 'node:http';
import https from 'node:https';
import net from 'node:net';
import zlib from 'node:zlib';
import { isPublicIp } from './ip.mjs';

export class NetError extends Error {
  constructor(code, message, detail) {
    super(message);
    this.code = code;
    this.detail = detail;
  }
}

const MESSAGES = {
  url: 'Ссылка должна начинаться с https:// и не содержать логина и пароля.',
  private: 'Адрес ведёт во внутреннюю сеть. Такие адреса сервер не открывает.',
  dns: 'Домен не находится в DNS.',
  timeout: 'Сервер не ответил вовремя.',
  too_large: 'Ответ слишком большой.',
  redirect: 'Перенаправление не на https или их слишком много.',
  tls: 'Сертификат сайта не прошёл проверку.',
  connect: 'Не удалось подключиться к серверу.',
  aborted: 'Проверка прервана.',
};

const fail = (code, detail) => new NetError(code, MESSAGES[code], detail);

const TLS_CODES = /CERT|SSL|TLS|SELF_SIGNED|UNABLE_TO_(GET|VERIFY)|ALTNAME|HOSTNAME/i;

export async function defaultResolve(host) {
  const list = await dns.promises.lookup(host, { all: true, verbatim: true });
  return list.map(a => ({ address: a.address, family: a.family }));
}

function withDeadline(promise, signal) {
  if (!signal) return promise;
  return new Promise((resolve, reject) => {
    if (signal.aborted) { reject(signal.reason); return; }
    const onAbort = () => reject(signal.reason);
    signal.addEventListener('abort', onAbort, { once: true });
    promise.then(v => { signal.removeEventListener('abort', onAbort); resolve(v); },
      e => { signal.removeEventListener('abort', onAbort); reject(e); });
  });
}

/**
 * The one address of <host> the hub may connect to: an IP literal must be public; a name must resolve, and every
 * address it resolves to must be public (a name with one private address is refused as a whole).
 */
export async function vetHost(host, { resolve = defaultResolve, allow = isPublicIp, signal } = {}) {
  const bare = host.startsWith('[') && host.endsWith(']') ? host.slice(1, -1) : host;
  if (net.isIP(bare)) {
    if (!allow(bare)) throw fail('private', bare);
    return { address: bare, family: net.isIP(bare) };
  }
  let list;
  try {
    list = await withDeadline(resolve(bare), signal);
  } catch (e) {
    if (signal?.aborted) throw fail('timeout');
    throw fail('dns', e?.code);
  }
  if (!Array.isArray(list) || list.length === 0) throw fail('dns');
  for (const a of list) {
    if (!a || !net.isIP(a.address) || !allow(a.address)) throw fail('private', a?.address);
  }
  return { address: list[0].address, family: list[0].family || net.isIP(list[0].address) };
}

const sameIp = (a, b) => (a || '').replace(/^::ffff:/i, '') === (b || '').replace(/^::ffff:/i, '');

function oneRequest(u, address, family, { method, headers, body, maxBytes, signal }) {
  return new Promise((resolve, reject) => {
    const lib = u.protocol === 'https:' ? https : http;
    const hostname = u.hostname.startsWith('[') ? u.hostname.slice(1, -1) : u.hostname;
    const req = lib.request({
      protocol: u.protocol,
      hostname,
      port: u.port || (u.protocol === 'https:' ? 443 : 80),
      path: u.pathname + u.search,
      method,
      headers,
      agent: false,
      signal,
      // The connection goes to the address checked above, whatever DNS would say now.
      lookup: (_name, options, cb) => {
        if (options && options.all) cb(null, [{ address, family }]);
        else cb(null, address, family);
      },
      servername: net.isIP(hostname) ? undefined : hostname,
    }, res => {
      if (!sameIp(res.socket?.remoteAddress, address)) {
        req.destroy();
        reject(fail('private', res.socket?.remoteAddress));
        return;
      }
      const chunks = [];
      let size = 0;
      let stream = res;
      const encoding = String(res.headers['content-encoding'] || '').toLowerCase().trim();
      if (encoding === 'gzip' || encoding === 'x-gzip') stream = res.pipe(zlib.createGunzip({ maxOutputLength: maxBytes + 1 }));
      else if (encoding === 'deflate') stream = res.pipe(zlib.createInflate({ maxOutputLength: maxBytes + 1 }));
      else if (encoding === 'br') stream = res.pipe(zlib.createBrotliDecompress({ maxOutputLength: maxBytes + 1 }));
      stream.on('data', chunk => {
        size += chunk.length;
        if (size > maxBytes) {
          req.destroy();
          reject(fail('too_large'));
          return;
        }
        chunks.push(chunk);
      });
      stream.on('end', () => resolve({ status: res.statusCode, headers: res.headers, body: Buffer.concat(chunks) }));
      stream.on('error', e => reject(e instanceof NetError ? e : e?.code === 'ERR_BUFFER_TOO_LARGE' ? fail('too_large') : fail('connect', e?.code)));
      res.on('error', e => reject(fail('connect', e?.code)));
    });
    req.on('error', e => {
      if (e instanceof NetError) reject(e);
      else if (signal?.aborted) reject(fail('timeout'));
      else if (TLS_CODES.test(String(e?.code))) reject(fail('tls', e.code));
      else reject(fail('connect', e?.code));
    });
    if (body) req.end(body);
    else req.end();
  });
}

/**
 * A request to a provider's server. Options: method, headers, body (Buffer|string), maxBytes, timeoutMs, redirects
 * (how many to follow; 0 for none), resolve and allow (tests), allowHttp (tests only: plain http for a local server).
 * Returns {status, headers, body, ms, url, redirects, address}; throws NetError.
 */
export async function safeFetch(url, opts = {}) {
  const {
    method = 'GET', headers = {}, body = null, maxBytes = 2 * 1024 * 1024, timeoutMs = 8000, redirects = 3,
    resolve, allow, allowHttp = false,
  } = opts;
  const started = Date.now();
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(fail('timeout')), timeoutMs);
  timer.unref?.();
  const signal = opts.signal ? AbortSignal.any([controller.signal, opts.signal]) : controller.signal;
  try {
    let current = url;
    let currentMethod = method;
    let currentBody = body;
    for (let hop = 0; ; hop++) {
      let u;
      try {
        u = new URL(current);
      } catch {
        throw fail(hop ? 'redirect' : 'url');
      }
      const okScheme = u.protocol === 'https:' || (allowHttp && u.protocol === 'http:');
      if (!okScheme || u.username || u.password) throw fail(hop ? 'redirect' : 'url');
      const { address, family } = await vetHost(u.hostname, { resolve, allow, signal });
      const res = await oneRequest(u, address, family, {
        method: currentMethod,
        headers: { accept: '*/*', ...headers, ...(currentBody ? { 'content-length': Buffer.byteLength(currentBody) } : {}) },
        body: currentBody,
        maxBytes,
        signal,
      });
      if ([301, 302, 303, 307, 308].includes(res.status) && res.headers.location && redirects > 0) {
        if (hop >= redirects) throw fail('redirect');
        current = new URL(res.headers.location, u).toString();
        if (res.status === 303 || ((res.status === 301 || res.status === 302) && currentMethod === 'POST')) {
          currentMethod = 'GET';
          currentBody = null;
        }
        continue;
      }
      return { ...res, ms: Date.now() - started, url: u.toString(), redirects: hop, address };
    }
  } catch (e) {
    if (e instanceof NetError) throw e;
    if (signal.aborted) throw fail('timeout');
    throw fail('connect', e?.code);
  } finally {
    clearTimeout(timer);
  }
}

/** A TCP connection to host:port (a public address only); {ok, ms} or {ok: false, error}. */
export async function tcpPing(host, port, { timeoutMs = 3000, resolve, allow } = {}) {
  const started = Date.now();
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(fail('timeout')), timeoutMs);
  timer.unref?.();
  try {
    const { address } = await vetHost(host, { resolve, allow, signal: controller.signal });
    await new Promise((resolveConn, reject) => {
      const socket = net.connect({ host: address, port, signal: controller.signal });
      socket.once('connect', () => { socket.destroy(); resolveConn(); });
      socket.once('error', e => { socket.destroy(); reject(controller.signal.aborted ? fail('timeout') : fail('connect', e?.code)); });
    });
    return { ok: true, ms: Date.now() - started };
  } catch (e) {
    return { ok: false, error: e instanceof NetError ? e.code : 'connect' };
  } finally {
    clearTimeout(timer);
  }
}
