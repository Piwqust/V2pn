// The daily backup: a consistent copy of the database while the hub writes to it, and the archive with keys/ and
// uploads/ that keeps 14 days (deploy/backup-db.mjs, deploy/v2pn-hub-backup.sh).
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { test } from 'node:test';
import { backupDatabase } from '../deploy/backup-db.mjs';
import { Db } from '../lib/db.mjs';
import { tempDir } from './helpers.mjs';

const script = new URL('../deploy/v2pn-hub-backup.sh', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1');

function liveDb(dir) {
  const db = new Db(path.join(dir, 'hub.db'));
  for (let i = 0; i < 50; i++) db.run("INSERT INTO accounts (login, role, created_at) VALUES (?, 'provider', '2026-11-24')", `user-${i}`);
  return db;
}

test('the copy has what is still in the WAL, checks out, and is one file without a WAL', async () => {
  const dir = tempDir();
  const db = liveDb(dir);
  try {
    assert.ok(fs.statSync(path.join(dir, 'hub.db-wal')).size > 0, 'the rows are in the WAL, the hub still running');
    const result = await backupDatabase(path.join(dir, 'hub.db'), path.join(dir, 'copy.db'));
    assert.ok(result.pages > 0 && result.bytes > 0);
    // The hub goes on writing; the copy keeps its moment.
    db.run("INSERT INTO accounts (login, role, created_at) VALUES ('later', 'provider', '2026-11-24')");
    assert.ok(!fs.existsSync(path.join(dir, 'copy.db-wal')));
    const copy = new DatabaseSync(path.join(dir, 'copy.db'));
    try {
      assert.equal(copy.prepare('SELECT COUNT(*) AS n FROM accounts').get().n, 50);
      assert.equal(copy.prepare('PRAGMA journal_mode').get().journal_mode, 'delete');
      assert.equal(copy.prepare('PRAGMA integrity_check').get().integrity_check, 'ok');
    } finally {
      copy.close();
    }
    await assert.rejects(backupDatabase(path.join(dir, 'hub.db'), path.join(dir, 'copy.db')), /exists already/);
    await assert.rejects(backupDatabase(path.join(dir, 'none.db'), path.join(dir, 'other.db')), /does not exist/);
  } finally {
    db.close();
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

// The shell script needs a POSIX sh with tar and find: the server's, or a Mac's.
test('the daily script archives the database, keys/ and uploads/ and keeps 14 days', { skip: process.platform === 'win32' && 'no POSIX shell' }, () => {
  const root = tempDir();
  const data = path.join(root, 'data');
  const out = path.join(root, 'backups');
  fs.mkdirSync(path.join(data, 'keys'), { recursive: true });
  fs.mkdirSync(path.join(data, 'uploads'), { recursive: true });
  fs.mkdirSync(out);
  fs.writeFileSync(path.join(data, 'keys', 'hub-partner-key.pem'), 'key');
  fs.writeFileSync(path.join(data, 'uploads', `${'a'.repeat(64)}.png`), 'png');
  const db = liveDb(data);
  try {
    const day = 86400 * 1000;
    const old = path.join(out, 'v2pn-hub-20261001-033000.tar.gz');
    const recent = path.join(out, 'v2pn-hub-20261120-033000.tar.gz');
    const other = path.join(out, 'notes.txt');
    for (const [file, age] of [[old, 20 * day], [recent, 5 * day], [other, 40 * day]]) {
      fs.writeFileSync(file, 'x');
      fs.utimesSync(file, new Date(Date.now() - age), new Date(Date.now() - age));
    }
    const run = spawnSync('sh', [script], { env: { ...process.env, HUB_DATA: data, HUB_BACKUP_DIR: out, HUB_NODE: process.execPath }, encoding: 'utf8' });
    assert.equal(run.status, 0, run.stderr);
    assert.match(run.stdout, /^copied \d+ pages[^]*\nbackup .*v2pn-hub-\d{8}-\d{6}\.tar\.gz, \d+ bytes\n$/);
    const archives = fs.readdirSync(out).filter(f => f.startsWith('v2pn-hub-')).sort();
    assert.equal(archives.length, 2, archives.join());
    assert.equal(archives[0], path.basename(recent), 'the one of 20 days ago is gone');
    assert.ok(fs.existsSync(other), 'only archives are removed');
    assert.deepEqual(fs.readdirSync(out).filter(f => f.startsWith('.')), [], 'no work folder left');
    const listed = spawnSync('tar', ['-tzf', path.join(out, archives[1])], { encoding: 'utf8' }).stdout.split('\n').filter(Boolean).map(s => s.replace(/\/$/, '')).sort();
    assert.deepEqual(listed, ['hub.db', 'keys', 'keys/hub-partner-key.pem', 'uploads', `uploads/${'a'.repeat(64)}.png`]);
    assert.equal(fs.statSync(path.join(out, archives[1])).mode & 0o077, 0, 'readable by the hub only');

    const bad = spawnSync('sh', [script], { env: { ...process.env, HUB_DATA: data, HUB_BACKUP_DIR: out, HUB_BACKUP_KEEP_DAYS: 'x' }, encoding: 'utf8' });
    assert.equal(bad.status, 2);
  } finally {
    db.close();
    fs.rmSync(root, { recursive: true, force: true });
  }
});
