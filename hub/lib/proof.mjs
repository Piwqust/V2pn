// Proof that a provider controls its subscription domain, before a partner goes live on it: a DNS TXT record
// _v2pn.<domain> with the value "v2pn-verify=<token>", or an https file https://<domain>/.well-known/v2pn-verify.txt
// that contains the token. The token is the account's (one partner per account), shown in its cabinet. DNS is asked
// first; the file only when the record is not there. The file is fetched as everything else the hub fetches (net.mjs:
// public addresses only, the checked address, no redirect), 4 KB at most.
import dns from 'node:dns';
import { NetError, safeFetch } from './net.mjs';

export const PROOF_PREFIX = 'v2pn-verify=';
export const PROOF_PATH = '/.well-known/v2pn-verify.txt';
const DNS_TIMEOUT_MS = 5000;
const FILE_TIMEOUT_MS = 8000;
const FILE_MAX_BYTES = 4096;

/** What the provider adds for <domain>: {record: {name, type, value}, file: {url, text}}; nulls without a domain. */
export function proofPlaces(domain, token) {
  if (!domain) return { record: null, file: null };
  return {
    record: { name: `_v2pn.${domain}`, type: 'TXT', value: `${PROOF_PREFIX}${token}` },
    file: { url: `https://${domain}${PROOF_PATH}`, text: token },
  };
}

/** The TXT records of <name> as node:dns gives them (each a list of strings), from the system's resolvers. */
async function systemResolveTxt(name) {
  const resolver = new dns.promises.Resolver({ timeout: 2000, tries: 2 });
  return resolver.resolveTxt(name);
}

function withTimeout(promise, ms) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(Object.assign(new Error('timeout'), { code: 'ETIMEOUT' })), ms);
    timer.unref?.();
    promise.then(v => { clearTimeout(timer); resolve(v); }, e => { clearTimeout(timer); reject(e); });
  });
}

const NOT_THERE = new Set(['ENOTFOUND', 'ENODATA', 'NXDOMAIN']);

/** {ok, text}: whether _v2pn.<domain> has the token's record. <resolveTxt> is the tests' (net.resolveTxt). */
export async function checkRecord(domain, token, { resolveTxt = systemResolveTxt } = {}) {
  const want = `${PROOF_PREFIX}${token}`;
  try {
    const records = await withTimeout(Promise.resolve().then(() => resolveTxt(`_v2pn.${domain}`)), DNS_TIMEOUT_MS);
    const values = (Array.isArray(records) ? records : []).map(chunks => [].concat(chunks).join('').trim());
    if (values.includes(want)) return { ok: true, text: `Запись TXT _v2pn.${domain} на месте.` };
    if (values.some(v => v.startsWith(PROOF_PREFIX))) return { ok: false, text: `Запись TXT _v2pn.${domain} есть, но код в ней другой.` };
    return { ok: false, text: `Записи TXT _v2pn.${domain} пока нет.` };
  } catch (e) {
    if (NOT_THERE.has(e?.code)) return { ok: false, text: `Записи TXT _v2pn.${domain} пока нет.` };
    return { ok: false, text: e?.code === 'ETIMEOUT' || e?.code === 'ETIMEDOUT' ? 'DNS не ответил вовремя.' : 'DNS не ответил.' };
  }
}

/** {ok, text}: whether https://<domain>/.well-known/v2pn-verify.txt contains the token. <net> as safeFetch takes it. */
export async function checkFile(domain, token, net = {}) {
  // Tests point the file at their own server (net.proofUrl); the hub always asks the domain itself.
  const url = net.proofUrl ? net.proofUrl(domain) : `https://${domain}${PROOF_PATH}`;
  try {
    const res = await safeFetch(url, { ...net, maxBytes: FILE_MAX_BYTES, timeoutMs: FILE_TIMEOUT_MS, redirects: 0, headers: { 'user-agent': 'V2pn-Hub' } });
    if (res.status !== 200) return { ok: false, text: `Файла ${PROOF_PATH} нет: сайт ответил ${res.status}.` };
    if (res.body.toString('utf8').includes(token)) return { ok: true, text: `Файл ${PROOF_PATH} на месте.` };
    return { ok: false, text: `Файл ${PROOF_PATH} есть, но кода в нём нет.` };
  } catch (e) {
    return { ok: false, text: `Файл ${PROOF_PATH} не открылся: ${e instanceof NetError ? e.message.charAt(0).toLowerCase() + e.message.slice(1) : 'ошибка запроса.'}` };
  }
}

/**
 * Whether <domain> carries <token>: {ok, how: 'dns' | 'https' | null, dns: {ok, text}, https: {ok, text} | null}. The
 * file is asked for only when the record is not there.
 */
export async function checkProof(domain, token, net = {}) {
  const viaDns = await checkRecord(domain, token, net.resolveTxt ? { resolveTxt: net.resolveTxt } : {});
  if (viaDns.ok) return { ok: true, how: 'dns', dns: viaDns, https: null };
  const viaFile = await checkFile(domain, token, net);
  return { ok: viaFile.ok, how: viaFile.ok ? 'https' : null, dns: viaDns, https: viaFile };
}
