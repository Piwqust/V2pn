// The HTTP side: routes under /api/ (JSON, Cache-Control: no-store), the download redirects, CSRF and body limits.
import { HubError } from './errors.mjs';
import { log } from './log.mjs';
import { resolveDownload } from './downloads.mjs';

const JSON_LIMIT = 64 * 1024;
const UPLOAD_LIMIT = 2 * 1024 * 1024;
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

async function readJson(req) {
  const buf = await readBody(req, JSON_LIMIT);
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

// [method, pattern, handler, access] ; access: 'public' | 'user' | 'admin'
function routes(hub) {
  const me = s => ({ email: s.account.email, admin: s.admin });
  return [
    ['GET', /^\/api\/health$/, () => ({ ok: true, version: VERSION, mail: hub.auth.mailer ? 'smtp' : 'outbox', publication: hub.publicationState().last?.at ?? null }), 'public'],

    ['POST', /^\/api\/auth\/code$/, async ({ req, ip }) => ({ ok: true, ...(await hub.auth.requestCode((await readJson(req)).email, ip)) }), 'public'],
    ['POST', /^\/api\/auth\/verify$/, async ({ req, ip, res }) => {
      const body = await readJson(req);
      const { token, account } = hub.auth.verifyCode(body.email, body.code, ip);
      res.setHeader('set-cookie', hub.auth.cookie(token));
      return { ok: true, account: { email: account.email, admin: hub.auth.isAdmin(account.email) } };
    }, 'public'],
    ['POST', /^\/api\/auth\/signout$/, ({ session, res }) => {
      if (session) hub.auth.signOut(session.tokenHash);
      res.setHeader('set-cookie', hub.auth.clearCookie());
      return { ok: true };
    }, 'public'],
    ['GET', /^\/api\/auth\/admin-link$/, ({ url, ip, res }) => {
      const { token } = hub.auth.consumeAdminLink(url.searchParams.get('token'), ip);
      res.writeHead(302, { ...BASE_HEADERS, 'set-cookie': hub.auth.cookie(token), location: '/admin/' });
      res.end();
      return undefined;
    }, 'public'],
    ['GET', /^\/api\/me$/, ({ session }) => {
      const provider = hub.db.get('SELECT * FROM providers WHERE account_id = ?', session.account.id);
      return { account: me(session), application: hub.getApplication(session.account.id), provider: provider ? hub.providerView(provider) : null };
    }, 'user'],

    ['GET', /^\/api\/application$/, ({ session }) => ({ application: hub.getApplication(session.account.id) }), 'user'],
    ['PUT', /^\/api\/application$/, async ({ req, session }) => ({ application: hub.saveApplication(session.account.id, await readJson(req)) }), 'user'],
    ['POST', /^\/api\/application\/submit$/, async ({ session }) => ({ application: await hub.submitApplication(session.account.id) }), 'user'],
    ['POST', /^\/api\/check$/, async ({ req, session }) => ({ check: await hub.check(session.account.id, await readJson(req)) }), 'user'],

    ['GET', /^\/api\/cabinet$/, ({ session }) => hub.cabinet(session.account.id), 'user'],
    ['PUT', /^\/api\/cabinet\/look$/, async ({ req, session }) => hub.saveLook(session.account.id, await readJson(req)), 'user'],
    ['POST', /^\/api\/cabinet\/look\/submit$/, ({ session }) => hub.submitLook(session.account.id), 'user'],
    ['POST', /^\/api\/cabinet\/upload$/, async ({ req, url, session }) => {
      const type = String(req.headers['content-type'] || '').split(';')[0].trim().toLowerCase();
      if (type !== 'image/png') throw new HubError('invalid', { fields: { file: 'Нужен файл PNG (Content-Type: image/png).' } });
      return { picture: hub.upload(session.account.id, url.searchParams.get('slot'), await readBody(req, UPLOAD_LIMIT)) };
    }, 'user'],
    ['GET', /^\/api\/(?:cabinet|admin)\/pictures\/([0-9a-f]{64})$/, ({ session, match, res }) => {
      const bytes = hub.picture(session, match[1]);
      res.writeHead(200, { ...BASE_HEADERS, 'content-type': 'image/png', 'content-length': bytes.length, 'content-security-policy': "default-src 'none'" });
      res.end(res.req.method === 'HEAD' ? undefined : bytes);
      return undefined;
    }, 'user'],
    ['POST', /^\/api\/cabinet\/login-test$/, async ({ req, session }) => ({ test: await hub.loginTest(session.account.id, await readJson(req)) }), 'user'],
    ['POST', /^\/api\/cabinet\/tier-request$/, ({ session }) => ({ provider: hub.requestTier(session.account.id) }), 'user'],

    ['GET', /^\/api\/admin\/overview$/, () => ({
      applications: hub.db.all('SELECT state, COUNT(*) AS n FROM applications GROUP BY state'),
      providers: hub.db.all('SELECT state, COUNT(*) AS n FROM providers GROUP BY state'),
      queue: hub.db.get("SELECT COUNT(*) AS n FROM providers WHERE queue_state = 'queued'").n,
      tierRequests: hub.db.get('SELECT COUNT(*) AS n FROM providers WHERE tier_request IS NOT NULL').n,
      publication: hub.publicationState(),
      mail: hub.auth.mailer ? 'smtp' : 'outbox',
    }), 'admin'],
    ['GET', /^\/api\/admin\/applications$/, ({ url }) => ({ applications: hub.listApplications(url.searchParams.get('state') || null) }), 'admin'],
    ['GET', /^\/api\/admin\/applications\/(\d{1,9})$/, ({ match }) => ({ application: hub.adminApplication(Number(match[1])) }), 'admin'],
    ['POST', /^\/api\/admin\/applications\/(\d{1,9})\/approve$/, async ({ req, match, actor }) => ({ provider: hub.approve(Number(match[1]), await readJson(req), actor) }), 'admin'],
    ['POST', /^\/api\/admin\/applications\/(\d{1,9})\/reject$/, async ({ req, match, actor }) => ({ application: hub.reject(Number(match[1]), await readJson(req), actor) }), 'admin'],
    ['GET', /^\/api\/admin\/providers$/, () => ({ providers: hub.listProviders() }), 'admin'],
    ['POST', /^\/api\/admin\/providers\/([a-z0-9-]{2,32})\/tier$/, async ({ req, match, actor }) => ({ provider: hub.setTier(match[1], await readJson(req), actor) }), 'admin'],
    ['POST', /^\/api\/admin\/providers\/([a-z0-9-]{2,32})\/until$/, async ({ req, match, actor }) => ({ provider: hub.setUntil(match[1], await readJson(req), actor) }), 'admin'],
    ['POST', /^\/api\/admin\/providers\/([a-z0-9-]{2,32})\/revoke$/, async ({ req, match, actor }) => ({ provider: hub.revoke(match[1], await readJson(req), actor) }), 'admin'],
    ['POST', /^\/api\/admin\/providers\/([a-z0-9-]{2,32})\/restore$/, ({ match, actor }) => ({ provider: hub.restore(match[1], actor) }), 'admin'],
    ['GET', /^\/api\/admin\/queue$/, () => ({ queue: hub.queue() }), 'admin'],
    ['POST', /^\/api\/admin\/queue\/([a-z0-9-]{2,32})\/approve$/, ({ match, actor }) => ({ provider: hub.approveLook(match[1], actor) }), 'admin'],
    ['POST', /^\/api\/admin\/queue\/([a-z0-9-]{2,32})\/return$/, async ({ req, match, actor }) => ({ provider: hub.returnLook(match[1], await readJson(req), actor) }), 'admin'],
    ['GET', /^\/api\/admin\/audit$/, ({ url }) => ({ audit: hub.auditLog(Number(url.searchParams.get('limit')) || 200) }), 'admin'],
    ['GET', /^\/api\/admin\/downloads$/, ({ url }) => ({ downloads: hub.counter.stats(Math.min(Math.max(Number(url.searchParams.get('days')) || 30, 1), 366)) }), 'admin'],
    ['GET', /^\/api\/admin\/outbox$/, () => ({ outbox: hub.outbox() }), 'admin'],
    ['POST', /^\/api\/admin\/publish$/, ({ actor }) => ({ publication: hub.publish(actor) }), 'admin'],
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
      // The path only: a query may carry a sign-in token.
      log.info('request', { method: req.method, path: url.pathname.slice(0, 200), status: res.statusCode, ms: Date.now() - started });
    });
    try {
      if (!url.pathname.startsWith('/api/')) {
        const download = method === 'GET' ? resolveDownload(url.pathname, hub.config.githubRepo) : null;
        if (!download) throw new HubError('not_found');
        if (download.count && req.method === 'GET') hub.counter.count(download.count);
        res.writeHead(302, { ...BASE_HEADERS, location: download.location, 'content-length': 0 });
        res.end();
        return;
      }
      const candidates = table.filter(r => r[1].test(url.pathname));
      if (!candidates.length) throw new HubError('not_found');
      const route = candidates.find(r => r[0] === method);
      if (!route) throw new HubError('method');
      const [, pattern, handler, access] = route;
      if (method !== 'GET') {
        // CSRF: a form or a script on another site can send neither this header nor this origin.
        if (req.headers['x-hub'] !== '1' || req.headers.origin !== hub.config.origin) throw new HubError('csrf');
      }
      const session = hub.auth.session(req.headers.cookie);
      if (access !== 'public' && !session) throw new HubError('unauthorized');
      if (access === 'admin' && !session.admin) throw new HubError('forbidden');
      const ctx = { req, res, url, match: url.pathname.match(pattern), session, ip: clientIp(req), actor: session ? session.account.email : 'anonymous' };
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
