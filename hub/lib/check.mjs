// The check of a partner's subscription link: opened from the server as V2pn opens it, every finding one line
// (ok / warn / fail) with a short Russian text for the person and the raw values. The hub takes the link's host as
// the partner's domain, so there is no list of domains to check it against.
import { NetError, safeFetch, tcpPing } from './net.mjs';
import { parseBody, readHeaders, summarize, SUPPORTED, PROTOCOL_NAMES } from './subscription.mjs';

export function plural(n, one, few, many) {
  const m10 = n % 10;
  const m100 = n % 100;
  if (m10 === 1 && m100 !== 11) return one;
  if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
  return many;
}

const seconds = ms => `${(ms / 1000).toFixed(1).replace('.', ',')} с`;

function gb(bytes) {
  if (!bytes) return '0';
  const v = bytes / 1024 ** 3;
  return v >= 10 ? String(Math.round(v)) : v.toFixed(1).replace('.', ',').replace(/,0$/, '');
}

function date(unix) {
  const d = new Date(unix * 1000);
  return `${String(d.getUTCDate()).padStart(2, '0')}.${String(d.getUTCMonth() + 1).padStart(2, '0')}.${d.getUTCFullYear()}`;
}

/** Up to <limit> calls of <fn> over <items> at a time, in order. */
async function pool(items, limit, fn) {
  const results = new Array(items.length);
  let next = 0;
  const workers = Array.from({ length: Math.min(limit, items.length) }, async () => {
    while (next < items.length) {
      const i = next++;
      results[i] = await fn(items[i]);
    }
  });
  await Promise.all(workers);
  return results;
}

/**
 * Checks <url>. <net> carries the fetch and ping options
 * (tests pass their resolver); <userAgent> is the one V2pn sends.
 */
export async function checkSubscription(url, { userAgent, net = {}, tcp = true } = {}) {
  const started = Date.now();
  const lines = [];
  const add = (id, level, text, detail = null, raw = null) => lines.push({ id, level, text, detail, raw });
  const result = { at: new Date().toISOString(), ok: false, http: null, headers: null, servers: null, tcp: [], lines };

  let res;
  try {
    res = await safeFetch(url, { ...net, headers: { 'user-agent': userAgent }, maxBytes: 2 * 1024 * 1024, timeoutMs: 8000, redirects: 3 });
  } catch (e) {
    const message = e instanceof NetError ? e.message : 'Не удалось открыть ссылку.';
    add('open', 'fail', 'Ссылка не открывается', message, { error: e?.code || 'error' });
    result.durationMs = Date.now() - started;
    return result;
  }
  result.http = { status: res.status, ms: res.ms, bytes: res.body.length, redirects: res.redirects, contentType: res.headers['content-type'] || null };
  if (res.status !== 200) {
    add('open', 'fail', 'Ссылка не открывается', `Сервер ответил ${res.status}. Нужен ответ 200 со списком серверов.`, { status: res.status });
    result.durationMs = Date.now() - started;
    return result;
  }
  add('open', 'ok', 'Ссылка открывается', `200 OK за ${seconds(res.ms)}${res.redirects ? `, перенаправлений: ${res.redirects}` : ''}`, { status: 200, ms: res.ms, bytes: res.body.length });

  const body = parseBody(res.body);
  const headers = readHeaders(res.headers, body.meta);
  result.headers = headers;
  const summary = summarize(body.servers);
  result.servers = { format: body.format, ...summary };

  if (summary.total === 0) {
    add('servers', 'fail', 'Серверов не нашлось', 'В ответе нет ссылок на серверы, конфига Xray, sing-box или Clash. Проверьте, что ссылка отдаёт подписку, а не страницу.', { format: body.format });
  } else {
    const parts = Object.entries(summary.byProtocol).sort((a, b) => b[1] - a[1]).map(([p, n]) => `${PROTOCOL_NAMES[p] || p} — ${n}`);
    const unsupported = summary.total - summary.supported;
    const tail = unsupported === 0 ? 'Все V2pn понимает.'
      : summary.supported === 0 ? 'Ни один V2pn не запустит.'
        : `V2pn не запустит ${unsupported} из них.`;
    const level = summary.supported === 0 ? 'fail' : unsupported ? 'warn' : 'ok';
    add('servers', level, `${summary.total} ${plural(summary.total, 'сервер', 'сервера', 'серверов')}`, `${parts.join(', ')}. ${tail}`, { format: body.format, byProtocol: summary.byProtocol });
  }

  if (headers.title) add('title', 'ok', 'Название', headers.title, { 'profile-title': headers.title });
  else add('title', 'warn', 'Нет названия', 'Добавьте заголовок profile-title — V2pn назовёт подписку им.');

  if (headers.userInfo) {
    const u = headers.userInfo;
    const used = u.upload + u.download;
    const traffic = u.total ? `${gb(used)} из ${gb(u.total)} ГБ` : `${gb(used)} ГБ, без лимита`;
    const term = u.expire ? `до ${date(u.expire)}` : 'бессрочно';
    add('userinfo', 'ok', 'Трафик и срок', `${traffic}, ${term}`, u);
  } else {
    add('userinfo', 'warn', 'Нет трафика и срока', 'Добавьте заголовок subscription-userinfo — V2pn покажет остаток и срок подписки.');
  }

  if (headers.supportUrl) add('support', 'ok', 'Поддержка', headers.supportUrl, { 'support-url': headers.supportUrl });
  else add('support', 'warn', 'Нет ссылки на поддержку', 'Добавьте заголовок support-url — в V2pn появится кнопка «Поддержка».');

  if (headers.webPageUrl) add('cabinet', 'ok', 'Кабинет', headers.webPageUrl, { 'profile-web-page-url': headers.webPageUrl });
  else add('cabinet', 'warn', 'Нет ссылки на кабинет', 'Добавьте заголовок profile-web-page-url — тогда в V2pn появится «Продлить». Не обязательно.');

  if (headers.updateIntervalHours) add('interval', 'ok', 'Обновление', `каждые ${headers.updateIntervalHours} ч`, { 'profile-update-interval': headers.updateIntervalHours });
  if (headers.announce) add('announce', 'ok', 'Объявление', headers.announce.slice(0, 120), { announce: headers.announce });

  if (tcp) {
    const targets = [];
    const seen = new Set();
    for (const s of body.servers) {
      if (!s.host || !s.port || !SUPPORTED.has(s.protocol)) continue;
      const key = `${s.host}:${s.port}`;
      if (seen.has(key)) continue;
      seen.add(key);
      targets.push(s);
      if (targets.length >= 5) break;
    }
    if (targets.length) {
      const pings = await pool(targets, 3, s => tcpPing(s.host, s.port, { timeoutMs: 3000, resolve: net.resolve, allow: net.allow }));
      result.tcp = targets.map((s, i) => ({ host: s.host, port: s.port, protocol: s.protocol, ...pings[i] }));
      const ok = result.tcp.filter(t => t.ok);
      if (ok.length === targets.length) {
        const ms = ok.map(t => t.ms);
        const range = Math.min(...ms) === Math.max(...ms) ? `${ms[0]} мс` : `${Math.min(...ms)}–${Math.max(...ms)} мс`;
        add('tcp', 'ok', 'Серверы отвечают', `Проверили ${targets.length} из ${summary.total} с нашего сервера: ${range}`, { tried: targets.length, answered: ok.length });
      } else {
        add('tcp', 'warn', ok.length ? 'Отвечают не все серверы' : 'Серверы не ответили',
          `С нашего сервера ответили ${ok.length} из ${targets.length}. Порты могут быть закрыты для чужих адресов — это не всегда ошибка.`,
          { tried: targets.length, answered: ok.length });
      }
    }
  }

  result.ok = !lines.some(l => l.level === 'fail');
  result.durationMs = Date.now() - started;
  return result;
}
