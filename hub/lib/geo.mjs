// V2pn's own copy of runetfreedom's rule lists (geoip.dat, geosite.dat), for V2pn copies that cannot reach GitHub
// (ServiceLib/V2pn/V2pnGeoMirror.cs). The hub fetches them from the GitHub release every six hours, checks each
// against the release's own .sha256sum, and writes them under <publicDir>/geo/runetfreedom/, which Caddy serves as
// static files. A copy is replaced only by a file with the publisher's hash, and only after it is whole on disk. The
// app checks the hash again itself, against the publisher's sources, so this copy needs no trust of its own.
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { Readable, Transform } from 'node:stream';
import { pipeline } from 'node:stream/promises';

export const GEO_FILES = Object.freeze(['geoip.dat', 'geosite.dat']);
export const GEO_REFRESH_MS = 6 * 60 * 60 * 1000;
export const GEO_RETRY_MS = 30 * 60 * 1000;
export const GEO_MAX_BYTES = 200 * 1024 * 1024;
export const GEO_SOURCE = 'https://github.com/runetfreedom/russia-v2ray-rules-dat/releases/latest/download';

/** The hash in a ".sha256sum" text ("<64 hex>  geosite.dat"), lower case, or null. */
export function parseHash(text) {
  const first = String(text ?? '').trim().split(/\s+/)[0] ?? '';
  return /^[0-9a-fA-F]{64}$/.test(first) ? first.toLowerCase() : null;
}

export class GeoMirror {
  /**
   * <dir>: where the copies are written (served as /geo/runetfreedom/); <source>: the release download base;
   * <fetchImpl>: fetch, replaceable in tests; <timeoutMs>: per file.
   */
  constructor({ dir, source = GEO_SOURCE, fetchImpl = fetch, timeoutMs = 10 * 60 * 1000, maxBytes = GEO_MAX_BYTES }) {
    this.dir = dir;
    this.source = source.replace(/\/+$/, '');
    this.fetch = fetchImpl;
    this.timeoutMs = timeoutMs;
    this.maxBytes = maxBytes;
  }

  /** Brings every file up to the release; {updated, kept}. Throws on the first file it could not bring (the rest stay). */
  async refresh() {
    fs.mkdirSync(this.dir, { recursive: true });
    const updated = [];
    const kept = [];
    for (const name of GEO_FILES) {
      const hash = parseHash(await this.text(`${this.source}/${name}.sha256sum`));
      if (!hash) throw new Error(`${name}.sha256sum has no SHA-256`);
      const file = path.join(this.dir, name);
      const sum = `${file}.sha256sum`;
      if (fs.existsSync(file) && fs.existsSync(sum) && parseHash(fs.readFileSync(sum, 'utf8')) === hash) {
        kept.push(name);
        continue;
      }
      await this.download(`${this.source}/${name}`, file, hash);
      writeAtomic(sum, `${hash}  ${name}\n`);
      updated.push(name);
    }
    return { updated, kept };
  }

  async text(url) {
    const res = await this.fetch(url, { redirect: 'follow', signal: AbortSignal.timeout(30_000) });
    if (!res.ok) throw new Error(`${url}: HTTP ${res.status}`);
    const body = await res.text();
    if (body.length > 1024) throw new Error(`${url}: not a hash file`);
    return body;
  }

  /** Streams <url> to a part file beside <file>, checks size and hash, then renames it into place. */
  async download(url, file, hash) {
    const res = await this.fetch(url, { redirect: 'follow', signal: AbortSignal.timeout(this.timeoutMs) });
    if (!res.ok || !res.body) throw new Error(`${url}: HTTP ${res.status}`);
    const length = Number(res.headers.get('content-length'));
    if (length > this.maxBytes) throw new Error(`${url}: larger than ${this.maxBytes} bytes`);
    const part = `${file}.part-${crypto.randomBytes(4).toString('hex')}`;
    const digest = crypto.createHash('sha256');
    let total = 0;
    const max = this.maxBytes;
    const meter = new Transform({
      transform(chunk, _enc, done) {
        total += chunk.length;
        if (total > max) return done(new Error(`${url}: larger than ${max} bytes`));
        digest.update(chunk);
        done(null, chunk);
      },
    });
    try {
      await pipeline(Readable.fromWeb(res.body), meter, fs.createWriteStream(part));
      const got = digest.digest('hex');
      if (got !== hash) throw new Error(`${url}: SHA-256 ${got} is not the release's ${hash}`);
      fs.renameSync(part, file);
    } finally {
      fs.rmSync(part, { force: true });
    }
  }
}

function writeAtomic(file, text) {
  const part = `${file}.part-${crypto.randomBytes(4).toString('hex')}`;
  fs.writeFileSync(part, text);
  fs.renameSync(part, file);
}
