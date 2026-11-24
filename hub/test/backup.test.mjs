// The daily backup: a consistent copy of the database while the hub writes to it, the archive with keys/ and uploads/
// that keeps 14 days, and its copy off the server, encrypted with age when a recipient is set (deploy/backup-db.mjs,
// deploy/v2pn-hub-backup.sh).
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

/** HUB_DATA with a key and a picture, and the backup folder, under a new temporary folder. */
function backupRoot() {
  const root = tempDir();
  const data = path.join(root, 'data');
  fs.mkdirSync(path.join(data, 'keys'), { recursive: true });
  fs.mkdirSync(path.join(data, 'uploads'), { recursive: true });
  fs.mkdirSync(path.join(root, 'backups'));
  fs.writeFileSync(path.join(data, 'keys', 'hub-partner-key.pem'), 'key');
  fs.writeFileSync(path.join(data, 'uploads', `${'a'.repeat(64)}.png`), 'png');
  return { root, data, out: path.join(root, 'backups') };
}

/** An executable shell script <name> in <dir>. */
function tool(dir, name, body) {
  const file = path.join(dir, name);
  fs.writeFileSync(file, `#!/bin/sh\n${body}\n`);
  fs.chmodSync(file, 0o755);
  return file;
}

const has = command => spawnSync('sh', ['-c', `command -v ${command}`]).status === 0;

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
  const { root, data, out } = backupRoot();
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

test('the copy off the server is encrypted with age when HUB_BACKUP_AGE_RECIPIENT is set; without it a warning says it is not',
  { skip: process.platform === 'win32' && 'no POSIX shell' }, () => {
    const { root, data, out } = backupRoot();
    const remote = path.join(root, 'remote');
    const bin = path.join(root, 'bin');
    fs.mkdirSync(remote);
    fs.mkdirSync(bin);
    // Stand-ins: age writes a line naming the recipient before the bytes; rsync copies the file into the folder.
    const age = tool(bin, 'age', '[ "$1" = -r ] && [ "$3" = -o ] || exit 9\n{ echo "fake-age for $2"; cat "$5"; } > "$4"');
    const rsync = tool(bin, 'rsync', '[ "$1" = -t ] && [ "$2" = -e ] || exit 9\ncp "$4" "$5"');
    const recipient = 'age1ql3z7hjy54pw3hyww5ayyfg7zqgvc7w3j2elw8zmrj2kg5sfn9aqmcac8p';
    const db = liveDb(data);
    const run = extra => spawnSync('sh', [script], {
      env: { ...process.env, HUB_DATA: data, HUB_BACKUP_DIR: out, HUB_NODE: process.execPath, HUB_AGE: age, HUB_RSYNC: rsync, HUB_BACKUP_REMOTE: `${remote}/`, ...extra },
      encoding: 'utf8',
    });
    try {
      let r = run({ HUB_BACKUP_AGE_RECIPIENT: recipient });
      assert.equal(r.status, 0, r.stderr);
      assert.equal(r.stderr, '');
      assert.match(r.stdout, /\nsent v2pn-hub-\d{8}-\d{6}\.tar\.gz\.age to .*, encrypted with age\nbackup .*\.tar\.gz, \d+ bytes\n$/);
      const [sealed] = fs.readdirSync(remote);
      assert.match(sealed, /^v2pn-hub-\d{8}-\d{6}\.tar\.gz\.age$/);
      const bytes = fs.readFileSync(path.join(remote, sealed));
      assert.equal(bytes.toString('latin1').split('\n')[0], `fake-age for ${recipient}`);
      const local = fs.readdirSync(out).filter(f => f.startsWith('v2pn-hub-'));
      assert.deepEqual(local, [sealed.replace(/\.age$/, '')], 'the archive kept on the server is the plain one, with no .age beside it');
      assert.deepEqual(bytes.subarray(bytes.indexOf(10) + 1), fs.readFileSync(path.join(out, local[0])));

      fs.rmSync(remote, { recursive: true });
      fs.mkdirSync(remote);
      r = run({ HUB_BACKUP_AGE_RECIPIENT: '' });
      assert.equal(r.status, 0, r.stderr);
      assert.match(r.stderr, /^warning: HUB_BACKUP_AGE_RECIPIENT is not set: the copy on .* holds the hub's key unencrypted/);
      assert.match(fs.readdirSync(remote)[0], /^v2pn-hub-\d{8}-\d{6}\.tar\.gz$/);

      for (const bad of ['AGE1QL3Z', 'age1', 'age1 x', 'ssh-ed25519 AAAA']) assert.equal(run({ HUB_BACKUP_AGE_RECIPIENT: bad }).status, 2, bad);
      // Without age the archive here is still made; the run fails and nothing leaves.
      fs.rmSync(remote, { recursive: true });
      fs.mkdirSync(remote);
      for (const f of fs.readdirSync(out)) fs.rmSync(path.join(out, f), { recursive: true });
      r = run({ HUB_BACKUP_AGE_RECIPIENT: recipient, HUB_AGE: path.join(bin, 'none') });
      assert.equal(r.status, 1);
      assert.match(r.stderr, /age is not installed/);
      assert.deepEqual(fs.readdirSync(remote), []);
      assert.match(fs.readdirSync(out).join(), /^v2pn-hub-\d{8}-\d{6}\.tar\.gz$/);
    } finally {
      db.close();
      fs.rmSync(root, { recursive: true, force: true });
    }
  });

// With the real age (the server's, or a Mac's with it installed): what leaves the server opens with the private key.
test('an archive encrypted with age opens with its private key', { skip: (process.platform === 'win32' || !has('age') || !has('age-keygen')) && 'no age here' }, () => {
  const { root, data, out } = backupRoot();
  const remote = path.join(root, 'remote');
  const bin = path.join(root, 'bin');
  fs.mkdirSync(remote);
  fs.mkdirSync(bin);
  const db = liveDb(data);
  try {
    const keyFile = path.join(root, 'backup.key');
    const keygen = spawnSync('age-keygen', ['-o', keyFile], { encoding: 'utf8' });
    assert.equal(keygen.status, 0, keygen.stderr);
    const recipient = /age1[0-9a-z]+/.exec(keygen.stderr + keygen.stdout)[0];
    const rsync = tool(bin, 'rsync', 'cp "$4" "$5"');
    const r = spawnSync('sh', [script], {
      env: { ...process.env, HUB_DATA: data, HUB_BACKUP_DIR: out, HUB_NODE: process.execPath, HUB_RSYNC: rsync, HUB_BACKUP_REMOTE: `${remote}/`, HUB_BACKUP_AGE_RECIPIENT: recipient },
      encoding: 'utf8',
    });
    assert.equal(r.status, 0, r.stderr);
    const [sealed] = fs.readdirSync(remote);
    const opened = path.join(root, 'opened.tar.gz');
    const d = spawnSync('age', ['-d', '-i', keyFile, '-o', opened, path.join(remote, sealed)], { encoding: 'utf8' });
    assert.equal(d.status, 0, d.stderr);
    assert.deepEqual(fs.readFileSync(opened), fs.readFileSync(path.join(out, sealed.replace(/\.age$/, ''))));
  } finally {
    db.close();
    fs.rmSync(root, { recursive: true, force: true });
  }
});
