// deploy/Caddyfile sends downloads to GitHub while the hub is down: the same files the hub would redirect to.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { test } from 'node:test';
import { SHORT_NAMES, resolveDownload } from '../lib/downloads.mjs';

const caddyfile = fs.readFileSync(new URL('../deploy/Caddyfile', import.meta.url), 'utf8').replace(/\r\n/g, '\n');
const REPO = 'Piwqust/V2pn';

test('while the hub is down, every /r/ name leads where the hub would send it', () => {
  const errors = caddyfile.slice(caddyfile.indexOf('handle_errors {'));
  assert.match(errors, /@hub_down `\{err\.status_code\} in \[502, 503, 504\]`/);
  const redirs = Object.fromEntries([...errors.matchAll(/^\s*redir (\/r\/\S+) (\S+) 302$/gm)].map(m => [m[1], m[2]]));
  assert.deepEqual(Object.keys(redirs).sort(), Object.keys(SHORT_NAMES).map(n => `/r/${n}`).sort());
  for (const [p, to] of Object.entries(redirs)) assert.equal(to, resolveDownload(p, REPO).location, p);
});

test('release files (update checks, partner files) fall back to the same path on GitHub; /api/ gets the hub\'s JSON error', () => {
  const m = /@release path_regexp (\S+)\n\s*redir @release (\S+) 302/.exec(caddyfile);
  assert.ok(m, 'the release fallback is there');
  const release = new RegExp(m[1]);
  assert.equal(m[2], `https://github.com/${REPO}{path}`);
  for (const p of ['/releases/latest/download/release.json', '/releases/latest/download/release.json.sig', '/releases/latest/download/partner-index.json',
    '/releases/latest/download/V2pn-windows-x64-update.zip', '/releases/download/v1.4.2/V2pn-macos-arm64.dmg', '/releases/download/v1.5.0-beta.2/install.sh']) {
    assert.ok(release.test(p), p);
    assert.equal(m[2].replace('{path}', p), resolveDownload(p, REPO).location, p);
  }
  for (const p of ['/releases/latest/download/a/b', '/releases/latest/download/', '/releases/download/x1/a.zip', '/releases/evil', '/r/windows-x64']) {
    assert.ok(!release.test(p), p);
  }
  // The beta channel falls back to the latest release, as the hub does before it knows a beta.
  const beta = /@beta path_regexp beta (\S+)\n\s*redir @beta (\S+) 302/.exec(caddyfile);
  assert.ok(beta, 'the beta fallback is there');
  for (const p of ['/releases/beta/download/release.json', '/releases/beta/download/release.json.sig', '/releases/beta/download/V2pn-macos-x64.zip']) {
    const file = new RegExp(beta[1]).exec(p)?.[1];
    assert.ok(file, p);
    assert.ok(!release.test(p), p);
    assert.equal(beta[2].replace('{re.beta.1}', file), resolveDownload(p, REPO, null).location, p);
  }
  for (const p of ['/releases/beta/download/a/b', '/releases/beta/download/', '/releases/beta/x']) assert.ok(!new RegExp(beta[1]).test(p), p);
  const body = /respond \/api\/\* `([^`]+)` 503/.exec(caddyfile)?.[1];
  assert.equal(JSON.parse(body).error.code, 'unavailable');
  assert.match(caddyfile, /handle \{\s*respond "\{err\.status_code\} \{err\.status_text\}" \{err\.status_code\}\s*\}\s*\}/, 'other errors keep their status');
  // The hub's own block stays as it was: the request limit for the API, file_server for the rest.
  assert.match(caddyfile, /handle \/api\/\* \{\s*request_body \{\s*max_size 3MB/);
  assert.match(caddyfile, /reverse_proxy 127\.0\.0\.1:18190 \{\s*transport http \{\s*dial_timeout 2s\s*response_header_timeout 5s/);
});
