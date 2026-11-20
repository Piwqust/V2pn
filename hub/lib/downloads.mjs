// Downloads through the hub: /r/<name> and /releases/... answer with a redirect to GitHub's release files (nothing is
// stored here: the server has little disk). /r/<name> is counted by day and name, with no IP and no cookie.

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

export function releaseUrl(repo, file, tag) {
  return tag
    ? `https://github.com/${repo}/releases/download/${tag}/${file}`
    : `https://github.com/${repo}/releases/latest/download/${file}`;
}

/** Where a download path leads: {location, count?} or null when it is not one the hub serves. */
export function resolveDownload(pathname, repo) {
  let m = /^\/r\/([A-Za-z0-9.-]{1,32})$/.exec(pathname);
  if (m) {
    const file = SHORT_NAMES[m[1]];
    return file ? { location: releaseUrl(repo, file), count: m[1] } : null;
  }
  m = /^\/releases\/latest\/download\/([A-Za-z0-9._-]{1,80})$/.exec(pathname);
  if (m) return RELEASE_FILE.test(m[1]) ? { location: releaseUrl(repo, m[1]) } : null;
  m = /^\/releases\/download\/([A-Za-z0-9.-]{1,60})\/([A-Za-z0-9._-]{1,80})$/.exec(pathname);
  if (m) return VERSION.test(m[1]) && RELEASE_FILE.test(m[2]) ? { location: releaseUrl(repo, m[2], m[1]) } : null;
  return null;
}

/** Per-day counters kept in memory and written to the database once a minute and at shutdown. */
export class DownloadCounter {
  constructor(db) {
    this.db = db;
    this.pending = new Map();
  }

  count(name, date = new Date()) {
    const key = `${date.toISOString().slice(0, 10)}|${name}`;
    this.pending.set(key, (this.pending.get(key) || 0) + 1);
  }

  flush() {
    if (!this.pending.size) return;
    const entries = [...this.pending];
    this.pending.clear();
    this.db.tx(() => {
      for (const [key, n] of entries) {
        const [day, name] = key.split('|');
        this.db.run('INSERT INTO downloads (day, name, count) VALUES (?, ?, ?) ON CONFLICT (day, name) DO UPDATE SET count = count + excluded.count', day, name, n);
      }
    });
  }

  /** {days: [{day, name, count}], totals: {name: count}} for the last <days> days. */
  stats(days = 30) {
    this.flush();
    const since = new Date(Date.now() - (days - 1) * 86400000).toISOString().slice(0, 10);
    const rows = this.db.all('SELECT day, name, count FROM downloads WHERE day >= ? ORDER BY day, name', since);
    const totals = {};
    for (const r of rows) totals[r.name] = (totals[r.name] || 0) + r.count;
    return { since, days: rows, totals };
  }
}
