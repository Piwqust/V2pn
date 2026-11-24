// The partners the owner of V2pn signs with the root key on his computer (tools/partner.cs publish): the release asset
// partner-index.json (.sig) on GitHub, which V2pn reads next to the hub's index. V2pn goes by the hub's index first
// (V2pnPartnerIndex.Merge): a hub partner with a root partner's id, or on one of its domains, would push the root
// partner out. So the hub keeps those ids and domains for them. It reads that index at start and every six hours,
// checks its signature with the root key as V2pn does (V2pnPartnerTrust.ReadIndex), keeps a copy in HUB_DATA and holds
// on to the last good one when GitHub does not answer or a file does not verify. HUB_ROOT_PARTNERS adds a fixed list.
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { verify } from './keys.mjs';
import { log } from './log.mjs';
import { safeFetch } from './net.mjs';
import { domainsOverlap, normalizeDomain, validId } from './validate.mjs';

export const MAX_ROOT_INDEX_BYTES = 256 * 1024;
export const ROOT_REFRESH_MS = 6 * 3600 * 1000;
// After a failed refresh the next try comes sooner.
export const ROOT_RETRY_MS = 30 * 60 * 1000;

const signedBy = (keys, bytes, sig) => keys.some(k => {
  try {
    return verify(k, bytes, sig);
  } catch {
    return false;
  }
});

/**
 * The partners of a root-signed partner-index.json, {published, partners: [{id, name, domains}]}, when <sig> (the .sig
 * file's text) is a signature of one of <keys> over these exact bytes. Throws with the reason otherwise. An entry
 * without a valid id or domain is left out: it cannot claim anything in V2pn either.
 */
export function readRootIndex(bytes, sig, keys) {
  if (!Buffer.isBuffer(bytes) || bytes.length === 0 || bytes.length > MAX_ROOT_INDEX_BYTES) throw new Error('missing or larger than 256 KB');
  if (typeof sig !== 'string' || !signedBy(keys, bytes, sig)) throw new Error('not signed by the root key');
  let doc;
  try {
    doc = JSON.parse(bytes.toString('utf8'));
  } catch {
    throw new Error('not JSON');
  }
  if (!doc || doc.schema !== 1 || !Array.isArray(doc.partners)) throw new Error('not a partner index');
  const partners = [];
  for (const p of doc.partners) {
    if (!p || !validId(p.id) || partners.some(x => x.id === p.id)) continue;
    const domains = (Array.isArray(p.domains) ? p.domains : []).map(normalizeDomain).filter(Boolean);
    if (!domains.length) continue;
    partners.push({ id: p.id, name: typeof p.name === 'string' && p.name.trim() ? p.name.trim().slice(0, 40) : p.id, domains });
  }
  return { published: typeof doc.published === 'string' ? doc.published : null, partners };
}

/**
 * HUB_ROOT_PARTNERS: "id=domain,domain; id=domain" (spaces allowed). Partners to keep for the root index even when
 * GitHub has none yet. Throws on an entry that is not one.
 */
export function parseRootList(text) {
  const out = [];
  for (const entry of String(text || '').split(';').map(s => s.trim()).filter(Boolean)) {
    const eq = entry.indexOf('=');
    const id = eq > 0 ? entry.slice(0, eq).trim() : '';
    const domains = eq > 0 ? entry.slice(eq + 1).split(',').map(normalizeDomain) : [];
    if (!validId(id) || !domains.length || domains.some(d => !d)) throw new Error(`HUB_ROOT_PARTNERS: "${entry}" is not id=domain,domain`);
    out.push({ id, name: id, domains });
  }
  return out;
}

function writeAtomic(file, text) {
  fs.mkdirSync(path.dirname(file), { recursive: true, mode: 0o700 });
  const tmp = `${file}.${crypto.randomBytes(4).toString('hex')}.tmp`;
  fs.writeFileSync(tmp, text, { mode: 0o600 });
  fs.renameSync(tmp, file);
}

export class RootPartners {
  /**
   * <file> keeps the last good copy, <url> is partner-index.json (its signature at <url>.sig), <keys> the root keys
   * (base64 SubjectPublicKeyInfo), <fixed> the static list, <net> the options of every request (tests).
   */
  constructor({ file, url, keys, fixed = [], net = {} }) {
    this.file = file;
    this.url = url;
    this.keys = keys;
    this.fixed = fixed;
    this.net = net;
    this.index = null;
    this.fetchedAt = null;
  }

  /** Takes the copy on disk, checked again; false when there is none or it does not verify. */
  loadCached() {
    let saved;
    try {
      saved = JSON.parse(fs.readFileSync(this.file, 'utf8'));
    } catch (e) {
      if (e?.code !== 'ENOENT') log.warn('root partner index: the saved copy cannot be read', { error: String(e.message || e) });
      return false;
    }
    try {
      this.index = readRootIndex(Buffer.from(String(saved.index || ''), 'base64'), saved.sig, this.keys);
      this.fetchedAt = typeof saved.fetchedAt === 'string' ? saved.fetchedAt : null;
      return true;
    } catch (e) {
      log.warn('root partner index: the saved copy is not used', { error: String(e.message || e) });
      return false;
    }
  }

  /** Reads the index from <url> again; on any failure the copy in hand stays. True when a new one was taken. */
  async refresh() {
    const get = async url => {
      const res = await safeFetch(url, {
        ...this.net, maxBytes: MAX_ROOT_INDEX_BYTES, timeoutMs: 15_000, redirects: 5, headers: { 'user-agent': 'V2pn-Hub' },
      });
      if (res.status !== 200) throw new Error(`${url.split('/').pop()}: HTTP ${res.status}`);
      return res.body;
    };
    try {
      const [bytes, sig] = await Promise.all([get(this.url), get(`${this.url}.sig`)]);
      const index = readRootIndex(bytes, sig.toString('utf8'), this.keys);
      const fetchedAt = new Date().toISOString();
      writeAtomic(this.file, JSON.stringify({ fetchedAt, url: this.url, sig: sig.toString('utf8'), index: bytes.toString('base64') }));
      this.index = index;
      this.fetchedAt = fetchedAt;
      log.info('root partner index', { partners: index.partners.length, published: index.published });
      return true;
    } catch (e) {
      log.warn('root partner index not refreshed; the last good copy stays', { error: String(e.message || e).slice(0, 300), kept: Boolean(this.index) });
      return false;
    }
  }

  /** Every partner kept for the root index: its own and the fixed list's (by id, the index first). */
  list() {
    const all = [...(this.index?.partners ?? [])];
    for (const p of this.fixed) if (!all.some(x => x.id === p.id)) all.push(p);
    return all;
  }

  has(id) {
    return this.list().some(p => p.id === id);
  }

  /**
   * The root partner whose domains overlap <domains>, or null. The hub partner <id> may keep the domains of the root
   * partner with its own id: that is the same partner (partners/configra imported on the hub and signed at home).
   */
  owner(domains, id) {
    return this.list().find(p => p.id !== id && domainsOverlap(domains, p.domains)) ?? null;
  }

  /** {fetchedAt, published, partners} for the moderators. */
  state() {
    return { fetchedAt: this.fetchedAt, published: this.index?.published ?? null, partners: this.list().map(p => p.id) };
  }
}
