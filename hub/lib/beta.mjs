// The beta channel: /releases/beta/download/<file> leads to the newest published V2pn release, pre-releases
// included, while /releases/latest/download/<file> stays GitHub's latest, which never names a pre-release. Only a copy
// whose person turned on "Get beta versions" asks for it (V2pnUpdateSettings.Manifests).
//
// GitHub has no address for "the newest release, pre-releases too", so the hub asks its API for the list of releases
// at start and every five minutes and keeps the tag in memory. An unchanged list answers 304 to the ETag it sent, which
// does not count against the 60 requests an hour GitHub allows without a token. Until the hub has a tag, and whenever
// GitHub fails, the last tag stays; with none at all the channel answers what /releases/latest does, so a beta copy
// simply gets the stable release. Nothing here is trusted: V2pn checks the manifest's signature and every package.
import { log } from './log.mjs';

export const BETA_REFRESH_MS = 5 * 60 * 1000;
const MAX_BYTES = 4 * 1024 * 1024;
// The same shape as a release tag the hub redirects to (downloads.mjs VERSION).
const TAG = /^v(\d{1,4})\.(\d{1,4})\.(\d{1,6})(?:-([0-9A-Za-z.-]{1,40}))?$/;

/** Orders V2pn versions as semantic versions: 1.5.0-beta.2 < 1.5.0-beta.10 < 1.5.0 < 1.5.1. Tags with "v". */
export function compareTags(a, b) {
  const x = TAG.exec(a);
  const y = TAG.exec(b);
  for (let i = 1; i <= 3; i++) {
    const d = Number(x[i]) - Number(y[i]);
    if (d) return Math.sign(d);
  }
  if (!x[4] || !y[4]) return (x[4] ? -1 : 0) + (y[4] ? 1 : 0);
  const p = x[4].split('.');
  const q = y[4].split('.');
  for (let i = 0; i < Math.max(p.length, q.length); i++) {
    if (p[i] === undefined) return -1;
    if (q[i] === undefined) return 1;
    const pn = /^\d+$/.test(p[i]);
    const qn = /^\d+$/.test(q[i]);
    if (pn && qn) {
      const d = Number(p[i]) - Number(q[i]);
      if (d) return Math.sign(d);
    } else if (pn !== qn) {
      return pn ? -1 : 1;
    } else if (p[i] !== q[i]) {
      return p[i] < q[i] ? -1 : 1;
    }
  }
  return 0;
}

/**
 * The tag of the newest release in GitHub's list that a V2pn can update from: published (not a draft), a V2pn
 * version, and carrying release.json with its signature. Null when there is none.
 */
export function newestTag(releases) {
  if (!Array.isArray(releases)) throw new Error('the list of releases is not a list');
  let best = null;
  for (const r of releases) {
    if (!r || r.draft || typeof r.tag_name !== 'string' || !TAG.test(r.tag_name)) continue;
    const names = new Set((Array.isArray(r.assets) ? r.assets : []).map(a => a?.name));
    if (!names.has('release.json') || !names.has('release.json.sig')) continue;
    if (!best || compareTags(r.tag_name, best) > 0) best = r.tag_name;
  }
  return best;
}

export class BetaChannel {
  /** <repo>: owner/name; <api>: GitHub's API root (tests serve their own); <fetchImpl>: fetch. */
  constructor({ repo, api = 'https://api.github.com', fetchImpl = fetch, timeoutMs = 15_000 }) {
    this.url = `${api.replace(/\/+$/, '')}/repos/${repo}/releases?per_page=20`;
    this.fetch = fetchImpl;
    this.timeoutMs = timeoutMs;
    this.tag = null;
    this.etag = null;
    this.checkedAt = null;
  }

  /** Reads the list of releases again; on any failure the tag in hand stays. True when GitHub answered. */
  async refresh() {
    try {
      const headers = { accept: 'application/vnd.github+json', 'user-agent': 'V2pn-Hub', 'x-github-api-version': '2022-11-28' };
      if (this.etag) headers['if-none-match'] = this.etag;
      const res = await this.fetch(this.url, { headers, redirect: 'follow', signal: AbortSignal.timeout(this.timeoutMs) });
      if (res.status === 304) {
        this.checkedAt = new Date().toISOString();
        return true;
      }
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      if (Number(res.headers.get('content-length')) > MAX_BYTES) throw new Error('the list of releases is too large');
      const text = await res.text();
      if (text.length > MAX_BYTES) throw new Error('the list of releases is too large');
      const tag = newestTag(JSON.parse(text));
      if (tag !== this.tag) log.info('beta channel', { tag });
      this.tag = tag;
      this.etag = res.headers.get('etag');
      this.checkedAt = new Date().toISOString();
      return true;
    } catch (e) {
      log.warn('beta channel not refreshed; the last tag stays', { error: String(e?.message || e).slice(0, 300), tag: this.tag });
      return false;
    }
  }
}
