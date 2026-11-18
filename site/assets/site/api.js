// The hub's API from the site's pages (hub/README.md): JSON over fetch on the site's own origin, the session in an
// httpOnly cookie the page never sees, X-Hub on every request (the hub refuses a write without it), and every
// failure turned into one ApiError with a Russian message a person can act on.

const TIMEOUT = 20000;
// A hint for the nav, not a session: the cookie decides. Set on sign-in, cleared on sign-out and on a 401.
const HINT = 'v2pn-hub';

export class ApiError extends Error {
  constructor(code, message, { status = 0, fields = null, detail = null } = {}) {
    super(message);
    this.code = code;
    this.status = status;
    this.fields = fields;
    this.detail = detail;
  }
}

// Messages the page says better than the bare API code would, in the place the person reads them.
const BETTER = {
  csrf: 'Запрос не дошёл: обновите страницу и попробуйте ещё раз.',
  bad_json: 'Запрос не дошёл: обновите страницу и попробуйте ещё раз.',
  method: 'Запрос не дошёл: обновите страницу и попробуйте ещё раз.',
  too_large: 'Файл слишком большой.',
};

function offlineError() {
  return new ApiError('offline', 'Нет связи с сервером. Проверьте интернет и попробуйте ещё раз.');
}

/**
 * One request to the hub. <body> is JSON, or a Blob / ArrayBuffer sent as it is (with <type>). Resolves to the JSON
 * answer; throws ApiError with code "offline", "timeout", "unavailable" (no hub behind the site) or the hub's own.
 */
export async function request(method, path, body, { type, timeout = TIMEOUT, signal } = {}) {
  if (navigator.onLine === false) throw offlineError();
  const headers = { 'X-Hub': '1', Accept: 'application/json' };
  let payload;
  if (body instanceof Blob || body instanceof ArrayBuffer) {
    payload = body;
    headers['Content-Type'] = type || 'application/octet-stream';
  } else if (body !== undefined) {
    payload = JSON.stringify(body);
    headers['Content-Type'] = 'application/json';
  }
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort('timeout'), timeout);
  if (signal) signal.addEventListener('abort', () => ctrl.abort('cancel'), { once: true });
  let res;
  try {
    res = await fetch(path, { method, headers, body: payload, credentials: 'same-origin', cache: 'no-store', signal: ctrl.signal });
  } catch (e) {
    if (ctrl.signal.aborted && ctrl.signal.reason === 'timeout') {
      throw new ApiError('timeout', 'Сервер не ответил вовремя. Попробуйте ещё раз.');
    }
    if (ctrl.signal.aborted) throw new ApiError('cancel', 'Отменено.');
    throw offlineError();
  } finally {
    clearTimeout(timer);
  }
  let data = null;
  const text = await res.text().catch(() => '');
  if (text) {
    try { data = JSON.parse(text); } catch { data = null; }
  }
  if (res.ok && data) return data;
  if (data && data.error && typeof data.error.code === 'string') {
    const e = data.error;
    if (e.code === 'unauthorized') forgetSignIn();
    throw new ApiError(e.code, BETTER[e.code] || e.message || 'Не получилось.', { status: res.status, fields: e.fields || null, detail: e.detail ?? null });
  }
  // Caddy's own page or nothing at all: the hub behind the site is down or restarting.
  throw new ApiError('unavailable', 'Сервер провайдеров сейчас недоступен. Попробуйте через несколько минут.', { status: res.status });
}

export const api = {
  get: (path, o) => request('GET', path, undefined, o),
  post: (path, body, o) => request('POST', path, body ?? {}, o),
  put: (path, body, o) => request('PUT', path, body ?? {}, o),
  upload: (path, file, o) => request('POST', path, file, { type: 'image/png', timeout: 60000, ...o }),
};

// ---------------------------------------------------------------- sign-in

export function rememberSignIn(account) {
  try { localStorage.setItem(HINT, account && account.admin ? 'admin' : 'provider'); } catch { /* private mode */ }
}

export function forgetSignIn() {
  try { localStorage.removeItem(HINT); } catch { /* private mode */ }
}

export function signedInHint() {
  try { return localStorage.getItem(HINT); } catch { return null; }
}

/** A path on this site to go back to after sign-in; anything else becomes null. */
export function safeNext(value) {
  if (typeof value !== 'string' || !value.startsWith('/') || value.startsWith('//') || value.includes('\\')) return null;
  return /^\/(providers\/(apply|cabinet)\/|admin\/)/.test(value) ? value : null;
}

/** To the sign-in page, and back here afterwards. */
export function toSignIn(root) {
  const next = location.pathname + location.hash;
  location.replace(`${root}providers/login/?next=${encodeURIComponent(next)}`);
}

/**
 * GET /api/me, or a trip to the sign-in page when the session has ended. <root> is the site's root relative to the
 * page ("../../"). Resolves to {account, application, provider}; never resolves when it leaves the page.
 */
export async function me(root) {
  try {
    const data = await api.get('/api/me');
    rememberSignIn(data.account);
    return data;
  } catch (e) {
    if (e.code === 'unauthorized') {
      toSignIn(root);
      return new Promise(() => {});
    }
    throw e;
  }
}

export async function signOut(root) {
  try { await api.post('/api/auth/signout'); } catch { /* the cookie goes with the hint anyway */ }
  forgetSignIn();
  location.assign(`${root}providers/`);
}

/**
 * The nav of a page: «Кабинет» (or «Админка») among its words when this browser has signed in, and on an app page
 * the email and «Выйти» in place of the main button. <who> is /api/me's account when the page knows it.
 */
export function markNav(root, who) {
  const links = document.querySelector('.nav__links');
  if (!links) return;
  const kind = who ? (who.admin ? 'admin' : 'provider') : signedInHint();
  if (!kind || links.querySelector('[data-nav-cabinet]')) return;
  const item = document.createElement('div');
  item.className = 'nav__item';
  const a = document.createElement('a');
  a.className = 'nav__link';
  a.dataset.navCabinet = '';
  a.href = kind === 'admin' ? `${root}admin/` : `${root}providers/cabinet/`;
  a.textContent = kind === 'admin' ? 'Админка' : 'Кабинет';
  const here = location.pathname.replace(/index\.html$/, '');
  if (new URL(a.href).pathname === here) a.setAttribute('aria-current', 'page');
  item.appendChild(a);
  links.appendChild(item);
}

/** The account's corner of an app page's nav: its email and «Выйти». */
export function wireAccount(root, account) {
  document.querySelectorAll('[data-email]').forEach(el => { el.textContent = account.email; el.title = account.email; });
  document.querySelectorAll('[data-signout]').forEach(btn => btn.addEventListener('click', () => signOut(root)));
  markNav(root, account);
}
