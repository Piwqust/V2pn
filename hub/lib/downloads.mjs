// Downloads through the hub: /r/<name> and /releases/... answer with a redirect to GitHub's release files (nothing is
// stored here: the server has little disk); /releases/beta/... to the newest release with the pre-releases (beta.mjs). /r/<name> is counted by day and name, with no IP and no cookie. So is every
// request a running V2pn makes for the update manifest, by day and the version in its User-Agent: how many copies are
// in use, and on which versions. And every report of a failed update (reports.mjs), by day, the version updated to,
// os, arch, stage and code.

export const SHORT_NAMES = {
  // Windows downloads are the installer since 1.4.0; the zip stays as the portable copy.
  'windows-x64': 'V2pn-windows-x64-setup.exe',
  'windows-arm64': 'V2pn-windows-arm64-setup.exe',
  'windows-x64-zip': 'V2pn-windows-x64.zip',
  'windows-arm64-zip': 'V2pn-windows-arm64.zip',
  'macos-arm64': 'V2pn-macos-arm64.dmg',
  'macos-x64': 'V2pn-macos-x64.dmg',
  'install.ps1': 'install.ps1',
  'install.sh': 'install.sh',
};

// The files a V2pn release carries (tools/sign-release.ps1): packages, update packages, the signed manifest, the
// installers, the partner index V2pn's own source signs and its zips.
const RELEASE_FILE = /^(?:release\.json(?:\.sig)?|V2pn-windows-(?:x64|arm64)(?:(?:-update)?\.zip|-setup\.exe)|V2pn-macos-(?:x64|arm64)\.(?:zip|dmg)|install\.(?:ps1|sh)|partner-index\.json(?:\.sig)?|partners\.json(?:\.sig)?|partner-[a-z0-9][a-z0-9-]{1,31}\.zip)$/;
const VERSION = /^v\d{1,4}\.\d{1,4}\.\d{1,6}(?:-[0-9A-Za-z.-]{1,40})?$/;

// What a running V2pn asks for shortly after its start and every six hours after (V2pnInfo.ManifestUrl, AppUpdates),
// naming itself "V2pn/<version>" (V2pnHttp); a copy that takes betas asks the beta channel too.
const MANIFEST = 'release.json';
const APP = /^V2pn\/(\d{1,4}\.\d{1,4}\.\d{1,6})(?:-([0-9A-Za-z]{1,20})[0-9A-Za-z.-]{0,40})?$/;
// A User-Agent is whatever the sender writes: a day keeps this many versions, the rest add up as one.
const MAX_VERSIONS = 40;
export const OTHER_VERSION = 'other';
// Kinds of failed update (version, os, arch, stage, code) a day keeps; past them the version and the code add up as
// "other".
const MAX_FAILURE_KINDS = 200;

/** The V2pn version a User-Agent names ("1.4.0"; a build made on someone's computer is "1.4.1-local"), or null. */
export function appVersion(userAgent) {
  const m = APP.exec(typeof userAgent === 'string' ? userAgent : '');
  if (!m) return null;
  return m[2] === 'local' ? `${m[1]}-local` : userAgent.slice(5);
}

export function releaseUrl(repo, file, tag) {
  return tag
    ? `https://github.com/${repo}/releases/download/${tag}/${file}`
    : `https://github.com/${repo}/releases/latest/download/${file}`;
}

/**
 * Where a download path leads: {location, count?, check?} or null when it is not one the hub serves. <betaTag> is the
 * newest release with the pre-releases (beta.mjs); without one the beta channel is the latest release.
 */
export function resolveDownload(pathname, repo, betaTag = null) {
  let m = /^\/r\/([A-Za-z0-9.-]{1,32})$/.exec(pathname);
  if (m) {
    const file = SHORT_NAMES[m[1]];
    return file ? { location: releaseUrl(repo, file), count: m[1] } : null;
  }
  m = /^\/releases\/latest\/download\/([A-Za-z0-9._-]{1,80})$/.exec(pathname);
  if (m) return RELEASE_FILE.test(m[1]) ? { location: releaseUrl(repo, m[1]), check: m[1] === MANIFEST } : null;
  m = /^\/releases\/beta\/download\/([A-Za-z0-9._-]{1,80})$/.exec(pathname);
  if (m) {
    const tag = betaTag && VERSION.test(betaTag) ? betaTag : null;
    return RELEASE_FILE.test(m[1]) ? { location: releaseUrl(repo, m[1], tag), check: m[1] === MANIFEST } : null;
  }
  m = /^\/releases\/download\/([A-Za-z0-9.-]{1,60})\/([A-Za-z0-9._-]{1,80})$/.exec(pathname);
  if (m) return VERSION.test(m[1]) && RELEASE_FILE.test(m[2]) ? { location: releaseUrl(repo, m[2], m[1]) } : null;
  return null;
}

/** Per-day counters kept in memory and written to the database once a minute and at shutdown. */
export class DownloadCounter {
  constructor(db) {
    this.db = db;
    this.pending = new Map();
    this.pendingChecks = new Map();
    this.versionsDay = null;
    this.versions = new Set();
    this.pendingFailures = new Map();
    this.failuresDay = null;
    this.failureKinds = new Set();
  }

  count(name, date = new Date()) {
    const key = `${date.toISOString().slice(0, 10)}|${name}`;
    this.pending.set(key, (this.pending.get(key) || 0) + 1);
  }

  /** One request for the update manifest from V2pn <version> (appVersion's answer). */
  check(version, date = new Date()) {
    const day = date.toISOString().slice(0, 10);
    if (this.versionsDay !== day) {
      this.versionsDay = day;
      this.versions = new Set(this.db.all('SELECT version FROM checks WHERE day = ?', day).map(r => r.version));
    }
    const name = this.versions.has(version) || this.versions.size < MAX_VERSIONS ? version : OTHER_VERSION;
    this.versions.add(name);
    const key = `${day}|${name}`;
    this.pendingChecks.set(key, (this.pendingChecks.get(key) || 0) + 1);
  }

  /** One report of a failed update ({to, os, arch, stage, code}, as parseUpdateReport gives it). */
  failure(report, date = new Date()) {
    const day = date.toISOString().slice(0, 10);
    if (this.failuresDay !== day) {
      this.failuresDay = day;
      this.failureKinds = new Set(this.db.all('SELECT version, os, arch, stage, code FROM update_failures WHERE day = ?', day)
        .map(r => [r.version, r.os, r.arch, r.stage, r.code].join('|')));
    }
    let kind = [report.to, report.os, report.arch, report.stage, report.code].join('|');
    if (!this.failureKinds.has(kind) && this.failureKinds.size >= MAX_FAILURE_KINDS) kind = [OTHER_VERSION, report.os, report.arch, report.stage, OTHER_VERSION].join('|');
    this.failureKinds.add(kind);
    const key = `${day}|${kind}`;
    this.pendingFailures.set(key, (this.pendingFailures.get(key) || 0) + 1);
  }

  flush() {
    if (!this.pending.size && !this.pendingChecks.size && !this.pendingFailures.size) return;
    const downloads = [...this.pending];
    const checks = [...this.pendingChecks];
    const failures = [...this.pendingFailures];
    this.pending.clear();
    this.pendingChecks.clear();
    this.pendingFailures.clear();
    this.db.tx(() => {
      for (const [key, n] of downloads) {
        const [day, name] = key.split('|');
        this.db.run('INSERT INTO downloads (day, name, count) VALUES (?, ?, ?) ON CONFLICT (day, name) DO UPDATE SET count = count + excluded.count', day, name, n);
      }
      for (const [key, n] of checks) {
        const [day, version] = key.split('|');
        this.db.run('INSERT INTO checks (day, version, count) VALUES (?, ?, ?) ON CONFLICT (day, version) DO UPDATE SET count = count + excluded.count', day, version, n);
      }
      for (const [key, n] of failures) {
        const [day, version, os, arch, stage, code] = key.split('|');
        this.db.run(`INSERT INTO update_failures (day, version, os, arch, stage, code, count) VALUES (?, ?, ?, ?, ?, ?, ?)
          ON CONFLICT (day, version, os, arch, stage, code) DO UPDATE SET count = count + excluded.count`, day, version, os, arch, stage, code, n);
      }
    });
  }

  /** {days: [{day, name, count}], totals: {name: count}} for the last <days> days. */
  stats(days = 30) {
    this.flush();
    const rows = this.db.all('SELECT day, name, count FROM downloads WHERE day >= ? ORDER BY day, name', since(days));
    const totals = {};
    for (const r of rows) totals[r.name] = (totals[r.name] || 0) + r.count;
    return { since: since(days), days: rows, totals };
  }

  /** The same for the update checks: {days: [{day, version, count}], totals: {version: count}}. */
  checkStats(days = 30) {
    this.flush();
    const rows = this.db.all('SELECT day, version, count FROM checks WHERE day >= ? ORDER BY day, version', since(days));
    const totals = {};
    for (const r of rows) totals[r.version] = (totals[r.version] || 0) + r.count;
    return { since: since(days), days: rows, totals };
  }

  /**
   * The failed updates of the last <days> days: {days: [{day, version, os, arch, stage, code, count}], totals:
   * {version: count}, stages: {version: {stage: count}}}.
   */
  failureStats(days = 30) {
    this.flush();
    const rows = this.db.all(`SELECT day, version, os, arch, stage, code, count FROM update_failures WHERE day >= ?
      ORDER BY day, version, stage, os, arch, code`, since(days));
    const totals = {};
    const stages = {};
    for (const r of rows) {
      totals[r.version] = (totals[r.version] || 0) + r.count;
      stages[r.version] ??= {};
      stages[r.version][r.stage] = (stages[r.version][r.stage] || 0) + r.count;
    }
    return { since: since(days), days: rows, totals, stages };
  }
}

const since = days => new Date(Date.now() - (days - 1) * 86400000).toISOString().slice(0, 10);
