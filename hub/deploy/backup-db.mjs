#!/usr/bin/env node
// A consistent copy of the hub's database while the hub runs (deploy/BACKUP.md). The hub keeps hub.db in WAL mode
// through node:sqlite, so copying the file would miss what still sits in hub.db-wal and could catch a page half
// written. SQLite's online backup (node:sqlite backup()) copies a snapshot page by page while the hub goes on writing;
// the copy is then checked and turned into one file without a WAL.
//
//   node backup-db.mjs <hub.db> <copy.db>
import fs from 'node:fs';
import { DatabaseSync, backup } from 'node:sqlite';
import { pathToFileURL } from 'node:url';

/** Copies <source> to <target> (which must not exist yet); {pages, bytes}. Throws when the copy does not check out. */
export async function backupDatabase(source, target) {
  if (!fs.existsSync(source)) throw new Error(`${source} does not exist`);
  if (fs.existsSync(target)) throw new Error(`${target} exists already`);
  const tmp = `${target}.part`;
  fs.rmSync(tmp, { force: true });
  const db = new DatabaseSync(source);
  let pages;
  try {
    db.exec('PRAGMA busy_timeout = 10000');
    pages = await backup(db, tmp);
  } finally {
    db.close();
  }
  const copy = new DatabaseSync(tmp);
  try {
    const check = copy.prepare('PRAGMA integrity_check').all().map(r => Object.values(r)[0]);
    if (check.join() !== 'ok') throw new Error(`the copy is damaged: ${check.slice(0, 5).join('; ')}`);
    copy.exec('PRAGMA journal_mode = DELETE');
  } finally {
    copy.close();
  }
  fs.renameSync(tmp, target);
  return { pages, bytes: fs.statSync(target).size };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const [source, target] = process.argv.slice(2);
  if (!source || !target) {
    process.stderr.write('usage: node backup-db.mjs <hub.db> <copy.db>\n');
    process.exit(2);
  }
  backupDatabase(source, target).then(r => process.stdout.write(`copied ${r.pages} pages, ${r.bytes} bytes, to ${target}\n`), e => {
    process.stderr.write(`backup failed: ${e.message}\n`);
    process.exitCode = 1;
  });
}
