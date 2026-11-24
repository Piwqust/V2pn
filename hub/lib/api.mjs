// The HTTP side: routes under /api/ (JSON, Cache-Control: no-store), the download redirects, CSRF and body limits.
import { HubError } from './errors.mjs';
import { ipKey } from './ip.mjs';
import { log } from './log.mjs';
import { appVersion, resolveDownload } from './downloads.mjs';
import { MAX_REPORT_BYTES, parseUpdateReport } from './reports.mjs';

const JSON_LIMIT = 64 * 1024;
const UPLOAD_LIMIT = 2 * 1024 * 1024;
const HOUR = 3600 * 1000;
// Reports of failed updates an hour from one network (an IPv4 address or an IPv6 /64).
const REPORTS_PER_NETWORK = 20;
export const VERSION = '1.0.0';

const BASE_HEADERS = {
  'cache-control': 'no-store',
  'x-content-type-options': 'nosniff',
  'referrer-policy': 'no-referrer',
};

function send(res, status, body, headers = {}) {
  const data = body === undefined ? '' : JSON.stringify(body);
  res.writeHead(status, { ...BASE_HEADERS, 'content-type': 'application/json; charset=utf-8', 'content-length': Buffer.byteLength(data), ...headers });
  res.end(res.req.method === 'HEAD' ? undefined : data);
}

function readBody(req, limit) {
  return new Promise((resolve, reject) => {
    const declared = Number(req.headers['content-length']);
    if (declared > limit) {
      reject(new HubError('too_large'));
      req.resume();
      return;
    }
    const chunks = [];
    let size = 0;
    req.on('data', chunk => {
      size += chunk.length;
      if (size > limit) {
        reject(new HubError('too_large'));
        req.destroy();
        return;
      }
      chunks.push(chunk);
    });
    req.on('end', () => resolve(Buffer.concat(chunks)));
    req.on('error', reject);
  });
}

async function readJson(req, limit = JSON_LIMIT) {
  const buf = await readBody(req, limit);
  if (buf.length === 0) return {};
  try {
    return JSON.parse(buf.toString('utf8'));
  } catch {
    throw new HubError('bad_json');
  }
}

/** The visitor's address: Caddy's X-Forwarded-For (its last entry) when the request comes from this machine. */
export function clientIp(req) {
  const remote = (req.socket.remoteAddress || '').replace(/^::ffff:/, '');
  const fwd = req.headers['x-forwarded-for'];
  if ((remote === '127.0.0.1' || remote === '::1') && typeof fwd === 'string' && fwd.trim()) {
    const parts = fwd.split(',').map(s => s.trim()).filter(Boolean);
    return parts[parts.length - 1] || remote;
  }
  return remote;
}

// [method, pattern, handler, access]; access: 'public' | 'user' | 'mod' (owner or moderator) | 'owner' | 'app' (V2pn
// itself: not a browser, so no CSRF check and no session)
function routes(hub) {
  const signedIn = (res, { token, account }) => {
    res.setHeader('set-cookie', hub.auth.cookie(token));
    return { ok: true, account: { login: account.login, role: account.role } };
  };
  const picture = (res, bytes) => {
    res.writeHead(200, { ...BASE_HEADERS, 'content-type': 'image/png', 'content-length': bytes.length, 'content-security-policy': "default-src 'none'" });
    res.end(res.req.method === 'HEAD' ? undefined : bytes);
    return undefined;
  };
  const ID = '([a-z0-9][a-z0-9-]{1,31})';
  const login = m => { try { return decodeURIComponent(m); } catch { throw new HubError('not_found'); } };
  return [
    ['GET', /^\/api\/health$/, () => ({ ok: true, version: VERSION, publication: hub.publicationState().last?.at ?? null, beta: hub.beta.tag }), 'public'],

    ['POST', /^\/api\/auth\/register$/, async ({ req, ip, res }) => {
      const body = await readJson(req);
      return signedIn(res, await hub.auth.register(body.login, body.password, ip));
    }, 'public'],
    ['POST', /^\/api\/auth\/login$/, async ({ req, ip, res }) => {
      const body = await readJson(req);
      return signedIn(res, await hub.auth.signIn(body.login, body.password, ip));
    }, 'public'],
    ['POST', /^\/api\/auth\/signout$/, ({ session, res }) => {
      if (session) hub.auth.signOut(session.tokenHash);
      res.setHeader('set-cookie', hub.auth.clearCookie());
      return { ok: true };
    }, 'public'],
    ['POST', /^\/api\/auth\/password$/, async ({ req, session }) => {
      const body = await readJson(req);
      await hub.auth.changePassword(session, body.password, body.newPassword);
      return { ok: true };
    }, 'user'],
    ['GET', /^\/api\/me$/, ({ session }) => {
      const partner = hub.ownPartner(session.account.id);
      return {
        account: { login: session.account.login, role: session.account.role },
        partner: partner ? { id: partner.id, name: partner.name, state: partner.state } : null,
      };
    }, 'user'],

    ['POST', /^\/api\/complaints$/, async ({ req, ip }) => hub.complain(await readJson(req), ip), 'public'],

    // A failed update, from a V2pn whose person turned the reports on: 204 and nothing else (lib/reports.mjs).
    ['POST', /^\/api\/report\/update$/, async ({ req, ip, res }) => {
      if (!hub.limiter.hit(`report:${ipKey(ip)}`, REPORTS_PER_NETWORK, HOUR)) throw new HubError('rate_limited');
      const type = String(req.headers['content-type'] || '').split(';')[0].trim().toLowerCase();
      if (type !== 'application/json') throw new HubError('bad_request', { status: 415, message: 'Нужен JSON (Content-Type: application/json).' });
      hub.counter.failure(parseUpdateReport(await readJson(req, MAX_REPORT_BYTES)));
      res.writeHead(204, BASE_HEADERS);
      res.end();
      return undefined;
    }, 'app'],

    ['GET', /^\/api\/partner$/, ({ session }) => hub.myPartner(session.account.id), 'user'],
    ['PUT', /^\/api\/partner$/, async ({ req, session, actor }) => hub.savePartner(session.account.id, await readJson(req), actor), 'user'],
    ['POST', /^\/api\/partner\/check$/, async ({ req, session }) => hub.check(session.account.id, await readJson(req)), 'user'],
    ['POST', /^\/api\/partner\/proof$/, async ({ req, session, actor }) => hub.proof(session.account.id, await readJson(req), actor), 'user'],
    ['POST', /^\/api\/partner\/upload$/, async ({ req, url, session }) => {
      const type = String(req.headers['content-type'] || '').split(';')[0].trim().toLowerCase();
      if (type !== 'image/png') throw new HubError('invalid', { fields: { file: 'Нужен файл PNG (Content-Type: image/png).' } });
      return { picture: hub.upload(session.account.id, url.searchParams.get('slot'), await readBody(req, UPLOAD_LIMIT)) };
    }, 'user'],
    ['GET', /^\/api\/partner\/pictures\/([0-9a-f]{64})$/, ({ session, match, res }) => picture(res, hub.picture(session, match[1])), 'user'],
    ['POST', /^\/api\/partner\/login-test$/, async ({ req, session }) => ({ test: await hub.loginTest(session.account.id, await readJson(req)) }), 'user'],

    ['GET', /^\/api\/mod\/overview$/, () => hub.overview(), 'mod'],
    ['GET', /^\/api\/mod\/partners$/, () => ({ partners: hub.listPartners() }), 'mod'],
    ['GET', new RegExp(`^/api/mod/partners/${ID}$`), ({ match }) => ({ partner: hub.modPartner(match[1]) }), 'mod'],
    ['POST', new RegExp(`^/api/mod/partners/${ID}/down$`), async ({ req, match, actor }) => ({ partner: hub.takeDown(match[1], await readJson(req), actor) }), 'mod'],
    ['POST', new RegExp(`^/api/mod/partners/${ID}/restore$`), ({ match, actor }) => ({ partner: hub.restore(match[1], actor) }), 'mod'],
    ['POST', new RegExp(`^/api/mod/partners/${ID}/proof$`), async ({ req, match, actor }) => ({ partner: hub.markProved(match[1], await readJson(req), actor) }), 'mod'],
    ['POST', new RegExp(`^/api/mod/partners/${ID}/rights/([a-z]{2,16})/(approve|deny)$`), async ({ req, match, actor }) => (
      { right: hub.decideRight(match[1], match[2], match[3] === 'approve', await readJson(req), actor) }), 'mod'],
    ['GET', new RegExp(`^/api/mod/partners/${ID}/complaints$`), ({ match }) => ({ complaints: hub.complaintsOf(hub.partnerRow(match[1]).id) }), 'mod'],
    ['POST', new RegExp(`^/api/mod/partners/${ID}/complaints/clear$`), ({ match, actor }) => hub.clearComplaints(match[1], actor), 'mod'],
    ['GET', /^\/api\/mod\/rights$/, () => ({ rights: hub.rightsQueue() }), 'mod'],
    ['POST', /^\/api\/mod\/accounts\/([^/]{3,254})\/password$/, ({ match, session, actor }) => hub.resetPassword(session, login(match[1]), actor), 'mod'],
    ['GET', /^\/api\/mod\/pictures\/([0-9a-f]{64})$/, ({ session, match, res }) => picture(res, hub.picture(session, match[1], { mod: true })), 'mod'],
    ['GET', /^\/api\/mod\/audit$/, ({ url }) => ({ audit: hub.auditLog(Number(url.searchParams.get('limit')) || 200) }), 'mod'],
    ['GET', /^\/api\/mod\/downloads$/, ({ url }) => {
      const days = Math.min(Math.max(Number(url.searchParams.get('days')) || 30, 1), 366);
      return { downloads: hub.counter.stats(days), checks: hub.counter.checkStats(days), failures: hub.counter.failureStats(days) };
    }, 'mod'],
    ['POST', /^\/api\/mod\/publish$/, ({ actor }) => ({ publication: hub.publish(actor) }), 'mod'],

    ['GET', /^\/api\/mod\/moderators$/, () => ({ moderators: hub.moderators() }), 'owner'],
    ['POST', /^\/api\/mod\/moderators$/, async ({ req, actor }) => ({ moderator: hub.addModerator((await readJson(req)).login, actor) }), 'owner'],
    ['POST', /^\/api\/mod\/moderators\/([^/]{3,254})\/remove$/, ({ match, actor }) => ({ moderator: hub.removeModerator(login(match[1]), actor) }), 'owner'],
  ];
}

export function createHandler(hub) {
  const table = routes(hub);
  return async function handle(req, res) {
    const started = Date.now();
    let url;
    try {
      url = new URL(req.url, 'http://hub.invalid');
    } catch {
      res.writeHead(400).end();
      return;
    }
    const method = req.method === 'HEAD' ? 'GET' : req.method;
    res.on('finish', () => {
      // The path only, never the query.
      log.info('request', { method: req.method, path: url.pathname.slice(0, 200), status: res.statusCode, ms: Date.now() - started });
    });
    try {
      if (!url.pathname.startsWith('/api/')) {
        const download = method === 'GET' ? resolveDownload(url.pathname, hub.config.githubRepo, hub.beta.tag) : null;
        if (!download) throw new HubError('not_found');
        if (download.count && req.method === 'GET') hub.counter.count(download.count);
        // A running V2pn asking for its update manifest: the version it names is all that is kept.
        const version = download.check && req.method === 'GET' ? appVersion(req.headers['user-agent']) : null;
        if (version) hub.counter.check(version);
        res.writeHead(302, { ...BASE_HEADERS, location: download.location, 'content-length': 0 });
        res.end();
        return;
      }
      const candidates = table.filter(r => r[1].test(url.pathname));
      if (!candidates.length) throw new HubError('not_found');
      const route = candidates.find(r => r[0] === method);
      if (!route) throw new HubError('method');
      const [, pattern, handler, access] = route;
      if (method !== 'GET' && access !== 'app') {
        // CSRF: a form or a script on another site can send neither this header nor this origin.
        if (req.headers['x-hub'] !== '1' || req.headers.origin !== hub.config.origin) throw new HubError('csrf');
      }
      const session = access === 'app' ? null : hub.auth.session(req.headers.cookie);
      if (access !== 'public' && access !== 'app' && !session) throw new HubError('unauthorized');
      if (access === 'mod' && !session.mod) throw new HubError('forbidden');
      if (access === 'owner' && !session.owner) throw new HubError('forbidden');
      const ctx = { req, res, url, match: url.pathname.match(pattern), session, ip: clientIp(req), actor: session ? session.account.login : 'anonymous' };
      const result = await handler(ctx);
      if (result !== undefined && !res.headersSent) send(res, 200, result);
    } catch (e) {
      if (res.headersSent) {
        res.destroy();
        return;
      }
      if (e instanceof HubError) {
        send(res, e.status, e.toJSON(), e.code === 'method' ? { allow: 'GET, POST, PUT' } : {});
      } else {
        log.error('handler failed', { path: url.pathname.slice(0, 200), error: String(e?.stack || e).slice(0, 2000) });
        const err = new HubError('internal');
        send(res, err.status, err.toJSON());
      }
    }
  };
}
