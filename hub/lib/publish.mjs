// Publication: partner-<id>.zip for every live partner, partner-index.json signed with the hub's key
// (V2pnPartnerPublisher.IndexBytes / WriteOut, byte for byte in layout), the empty legacy partners.json
// V2pnPartnerPublisher still writes, and each partner's page /p/<id>/. Everything is written atomically; an index is
// never half right: any problem stops the whole publication and the files already out stay as they were.
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { dotnetJson } from './jsonfmt.mjs';
import { colors as C } from './colors.mjs';
import { linkAllowed, needsApi, packObject, packPictures, themeOf } from './look.mjs';
import { buildDownPage, buildPage } from './page.mjs';
import { routingBytes } from './routing.mjs';
import { domainsOverlap } from './validate.mjs';
import { makeZip, readZip } from './zip.mjs';
import { signature } from './keys.mjs';

export const INDEX_FILE = 'partner-index.json';
export const LEGACY_INDEX_FILE = 'partners.json';
export const MAX_INDEX_BYTES = 256 * 1024;
export const MAX_PACK_BYTES = 64 * 1024;
export const MAX_ZIP_BYTES = 64 * 1024 + 8 * 1024 * 1024 + 3 * 64 * 1024 + 64 * 1024;

// routing.json of an approved routing right; its SHA-256 goes into the listing's "reviewed" (V2pnReviewedFiles).
// transport and core stay with the index the owner signs with the root key on his PC: the hub never offers them.
export const ROUTING_FILE = 'routing.json';
export const ROOT_ONLY_GRANTS = new Set(['transport', 'core']);
export const GRANT_ORDER = ['edition', 'account', 'actions', 'lock', 'routing', 'transport', 'core'];

const sha256 = buf => crypto.createHash('sha256').update(buf).digest('hex');

export function publishedStamp(date = new Date()) {
  return date.toISOString().replace(/\.\d{3}Z$/, 'Z');
}

/** The until of a partner without a term of its own: a year on, renewed by every publication. */
export function rollingUntil(today) {
  const d = new Date(`${today}T00:00:00Z`);
  d.setUTCFullYear(d.getUTCFullYear() + 1);
  return d.toISOString().slice(0, 10);
}

/** partner-index.json for <listings> ({id, name, domains, grants, until, reviewed?, pack: {file, size, sha256}}), by id. */
export function indexBytes(listings, published, newline = '\n') {
  const partners = [...listings].sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0)).map(l => {
    const item = { id: l.id, name: l.name, domains: [...l.domains], grants: [...l.grants] };
    if (l.reviewed && Object.keys(l.reviewed).length) item.reviewed = { ...l.reviewed };
    item.until = l.until;
    item.pack = { file: l.pack.file, size: l.pack.size, sha256: l.pack.sha256 };
    return item;
  });
  return dotnetJson({ schema: 1, published, partners }, newline);
}

/**
 * One partner's pack: {listing, zip, packBytes, look, pictures, dropped}. <partner> is {id, name, domains, until, look,
 * approved: {account?, actions?, lock?, routing?}} with only the approved rights; <readUpload>(sha) gives a picture.
 * A cabinet or support link off the partner's domains (saved before the hub checked that) is left out and named in
 * <dropped>: the rest of the partner still goes out.
 */
export function buildPack(partner, readUpload, today) {
  const { look } = partner;
  const approved = partner.approved || {};
  const pictures = packPictures(look).map(p => ({ ...p, content: readUpload(p.sha) }));
  for (const p of pictures) {
    if (!p.content) throw new Error(`${partner.id}: the picture ${p.key} is missing`);
    if (sha256(p.content) !== p.sha) throw new Error(`${partner.id}: the picture ${p.key} is damaged`);
    const size = C.imageSize(p.content);
    if (!size || !C.SLOTS[p.key === 'backdrop' ? 'backdrop' : p.key === 'mark' ? 'mark' : 'logo'].fits(size[0], size[1]) || p.content.length > C.MAX_ASSET) {
      throw new Error(`${partner.id}: the picture ${p.key} does not fit its slot`);
    }
  }
  const api = approved.account?.api ?? '';
  // A button that needs the API goes out only with an approved API.
  const actions = (approved.actions ?? []).filter(a => api || !needsApi(a));
  const links = { cabinet: '', support: '' };
  const dropped = [];
  for (const key of Object.keys(links)) {
    const link = look.links?.[key] || '';
    if (link && !linkAllowed(link, partner.domains)) dropped.push(key);
    else links[key] = link;
  }
  const full = { ...look, links, api, login: Boolean(api && approved.account?.login), actions, lock: approved.lock === true };
  const pack = packObject(partner.id, full, pictures);
  const files = pictures.map(p => ({ name: p.name, content: p.content }));
  const reviewed = {};
  if (approved.routing) {
    const bytes = routingBytes(approved.routing);
    files.push({ name: ROUTING_FILE, content: bytes });
    reviewed[ROUTING_FILE] = sha256(bytes);
  }
  pack.assets = files.map(f => ({ name: f.name, size: f.content.length, sha256: sha256(f.content) }))
    .sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
  const packBytes = dotnetJson(pack);
  if (packBytes.length > MAX_PACK_BYTES) throw new Error(`${partner.id}: partner.json is larger than 64 KB`);
  const zip = makeZip([{ name: 'partner.json', content: packBytes }, ...files]);
  const grants = ['edition'];
  if (full.api) grants.push('account');
  if (full.actions.length) grants.push('actions');
  if (full.lock) grants.push('lock');
  if (approved.routing) grants.push('routing');
  const listing = {
    id: partner.id,
    name: look.name || partner.name,
    domains: [...partner.domains],
    grants: GRANT_ORDER.filter(g => grants.includes(g)),
    reviewed,
    until: partner.until || rollingUntil(today),
    pack: { file: `partner-${partner.id}.zip`, size: zip.length, sha256: sha256(zip) },
  };
  return { listing, zip, packBytes, look: full, pictures, dropped };
}

/** Reads a built pack back the way V2pn checks it (sizes, hashes, entries, id); throws on the first mismatch. */
export function verifyPack(listing, zip) {
  if (zip.length !== listing.pack.size || sha256(zip) !== listing.pack.sha256 || zip.length > MAX_ZIP_BYTES) throw new Error(`${listing.pack.file} is not the one the index names`);
  const entries = readZip(zip);
  const pack = JSON.parse(entries.get('partner.json')?.toString('utf8') ?? 'null');
  if (!pack || pack.schema !== 1 || pack.id !== listing.id) throw new Error(`${listing.pack.file}: partner.json does not belong to ${listing.id}`);
  const names = new Set(['partner.json']);
  for (const a of pack.assets || []) {
    const content = entries.get(a.name);
    if (!content || content.length !== a.size || sha256(content) !== a.sha256) throw new Error(`${listing.pack.file}: ${a.name} does not match partner.json`);
    names.add(a.name);
  }
  for (const name of entries.keys()) if (!names.has(name)) throw new Error(`${listing.pack.file} has ${name}, which partner.json does not list`);
  for (const [file, hash] of Object.entries(listing.reviewed || {})) {
    if (!pack.assets?.some(a => a.name === file && a.sha256 === hash)) throw new Error(`${listing.pack.file}: ${file} is not the reviewed one`);
  }
}

/**
 * Everything a publication writes, or {problems} when any partner cannot go out. <partners> are as buildPack takes
 * them, with their state: a partner taken down gets a page that says so instead of its pack. <notes> name what went
 * out without a part ({partner, links}).
 */
export function buildPublication(partners, { readUpload, today, published, origin }) {
  const problems = [];
  const notes = [];
  const built = [];
  const down = [];
  for (const partner of partners) {
    if (partner.until && partner.until <= today) continue;
    if (partner.state === 'down') {
      down.push({ id: partner.id, page: buildDownPage({ name: partner.look.name || partner.name }) });
      continue;
    }
    if (partner.state !== 'live') continue;
    try {
      const pack = buildPack(partner, readUpload, today);
      verifyPack(pack.listing, pack.zip);
      if (pack.dropped.length) notes.push({ partner: partner.id, links: pack.dropped });
      const pics = Object.fromEntries(pack.pictures.map(p => [p.key, p.content]));
      const page = buildPage({ id: partner.id, name: pack.listing.name, look: pack.look, theme: themeOf(pack.look), pictures: pics, origin });
      built.push({ ...pack, page });
    } catch (e) {
      problems.push(String(e.message || e));
    }
  }
  for (let i = 0; i < built.length; i++) {
    for (let j = i + 1; j < built.length; j++) {
      const clash = domainsOverlap(built[i].listing.domains, built[j].listing.domains);
      if (clash) problems.push(`${built[i].listing.id} and ${built[j].listing.id} both claim ${clash}`);
    }
  }
  const index = indexBytes(built.map(b => b.listing), published);
  if (index.length > MAX_INDEX_BYTES) problems.push('partner-index.json would be larger than 256 KB');
  return { problems, notes, built, down, index, legacy: indexBytes([], published) };
}

function writeAtomic(file, content) {
  const tmp = path.join(path.dirname(file), `.${path.basename(file)}.${crypto.randomBytes(4).toString('hex')}.tmp`);
  fs.writeFileSync(tmp, content, { mode: 0o644 });
  fs.renameSync(tmp, file);
}

function replaceDir(dir, files) {
  const parent = path.dirname(dir);
  const tag = crypto.randomBytes(4).toString('hex');
  const tmp = path.join(parent, `.${path.basename(dir)}.${tag}.tmp`);
  fs.mkdirSync(tmp, { recursive: true });
  for (const f of files) fs.writeFileSync(path.join(tmp, f.name), f.content, { mode: 0o644 });
  fs.chmodSync(tmp, 0o755);
  const old = path.join(parent, `.${path.basename(dir)}.${tag}.old`);
  if (fs.existsSync(dir)) fs.renameSync(dir, old);
  fs.renameSync(tmp, dir);
  fs.rmSync(old, { recursive: true, force: true });
}

export const isPublishedName = name => name === INDEX_FILE || name === `${INDEX_FILE}.sig` || name === LEGACY_INDEX_FILE
  || name === `${LEGACY_INDEX_FILE}.sig` || /^partner-[a-z0-9][a-z0-9-]{1,31}\.zip$/.test(name);

/**
 * Writes <publication> under <publicDir>: partners/latest/download/ (zips first, then the indexes and their
 * signatures) and p/<id>/ for each partner, published or taken down. <previousPages> are the ids whose pages the hub
 * wrote before; the pages of the others are removed. Returns the ids of the pages now out.
 */
export function writePublication(publicDir, publication, key, previousPages = []) {
  const out = path.join(publicDir, 'partners', 'latest', 'download');
  fs.mkdirSync(out, { recursive: true });
  const keep = new Set([INDEX_FILE, `${INDEX_FILE}.sig`, LEGACY_INDEX_FILE, `${LEGACY_INDEX_FILE}.sig`]);
  for (const b of publication.built) {
    writeAtomic(path.join(out, b.listing.pack.file), b.zip);
    keep.add(b.listing.pack.file);
  }
  writeAtomic(path.join(out, LEGACY_INDEX_FILE), publication.legacy);
  writeAtomic(path.join(out, `${LEGACY_INDEX_FILE}.sig`), signature(key, publication.legacy));
  const indexSig = signature(key, publication.index);
  writeAtomic(path.join(out, INDEX_FILE), publication.index);
  writeAtomic(path.join(out, `${INDEX_FILE}.sig`), indexSig);
  for (const name of fs.readdirSync(out)) {
    if (isPublishedName(name) && !keep.has(name)) fs.rmSync(path.join(out, name), { force: true });
  }

  const pagesDir = path.join(publicDir, 'p');
  fs.mkdirSync(pagesDir, { recursive: true });
  const pages = [];
  for (const b of publication.built) {
    replaceDir(path.join(pagesDir, b.listing.id), b.page);
    pages.push(b.listing.id);
  }
  for (const d of publication.down) {
    replaceDir(path.join(pagesDir, d.id), d.page);
    pages.push(d.id);
  }
  for (const id of previousPages) {
    if (!pages.includes(id) && /^[a-z0-9][a-z0-9-]{1,31}$/.test(id)) fs.rmSync(path.join(pagesDir, id), { recursive: true, force: true });
  }
  return pages;
}
