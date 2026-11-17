#!/usr/bin/env node
// The whole way of a provider through the local site (dev/serve.mjs --fixture --configra --downloads), in a headless
// Chrome, with screenshots of every page and state at 1440 and 390 wide:
//
//   node dev/walk.mjs --base http://127.0.0.1:8790 --data <serve's --data> --fixture <the printed test link>
//                     --api <the printed api> [--out ../site/shots/hub] [--to <step>]
//
// A provider signs up by the sign-in page (the code read from the owner's outbox: no mail), fills in and checks its
// application, sends it; the owner (signed in by a one-time link from `hub.mjs admin-link`) approves it; the provider
// designs its look and sends it; the owner publishes it; /p/<id>/ shows with #sub=. A second provider's link fails its
// check, and the owner returns its application with a reason.
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { makePng } from '../test/helpers.mjs';
import { launch } from './cdp.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const opt = (n, d) => { const i = args.indexOf(`--${n}`); return i < 0 ? d : args[i + 1]; };
const BASE = opt('base', 'http://127.0.0.1:8790');
const DATA = opt('data');
const FIXTURE = opt('fixture');
const API = opt('api');
const OUT = path.resolve(opt('out', path.join(here, '..', '..', 'site', 'shots', 'hub')));
const TO = opt('to', null);
const FROM = opt('from', null);
const ADMIN = 'owner@example.com';
const sleep = ms => new Promise(r => setTimeout(r, ms));
if (!DATA || !FIXTURE) {
  console.error('usage: node dev/walk.mjs --base <url> --data <dir> --fixture <link> --api <url> [--out <dir>] [--to <step>]');
  process.exit(2);
}
fs.mkdirSync(OUT, { recursive: true });

// ---------------------------------------------------------------- the owner, from Node (for the outbox's codes)

function adminLink() {
  const env = { ...process.env, HUB_DATA: DATA, HUB_ORIGIN: BASE, HUB_ADMINS: ADMIN, HUB_PUBLIC_DIR: path.join(DATA, 'www') };
  return execFileSync(process.execPath, [path.join(here, '..', 'hub.mjs'), 'admin-link', ADMIN], { env, encoding: 'utf8' }).trim();
}
async function cookieFrom(link) {
  const res = await fetch(link, { redirect: 'manual' });
  const set = res.headers.get('set-cookie');
  if (!set) throw new Error(`no cookie from ${link}: ${res.status}`);
  return set.split(';')[0];
}
const adminCookie = await cookieFrom(adminLink());
async function outboxCode(email) {
  for (let i = 0; i < 30; i++) {
    const res = await fetch(`${BASE}/api/admin/outbox`, { headers: { cookie: adminCookie } });
    const { outbox } = await res.json();
    const m = outbox.find(o => o.email === email);
    const code = m && /\b(\d{6})\b/.exec(`${m.subject}\n${m.body}`);
    if (code) return code[1];
    await sleep(300);
  }
  throw new Error(`no code for ${email}`);
}

// ---------------------------------------------------------------- pictures to upload

const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'v2pn-walk-png-'));
function png(name, w, h, rgb) {
  const f = path.join(tmp, name);
  fs.writeFileSync(f, makePng(w, h, rgb));
  return f;
}
const PNG = {
  logo: png('logo.png', 320, 80, [255, 255, 255]),
  logoOnLight: png('logo-dark.png', 320, 80, [22, 22, 27]),
  mark: png('mark.png', 128, 128, [46, 107, 255]),
  small: png('small.png', 40, 40, [46, 107, 255]),
};

// ---------------------------------------------------------------- shots

const browser = await launch();
const shots = [];
async function shoot(page, name, { mobile = true, full = true } = {}) {
  await sleep(300);
  await page.shot(path.join(OUT, `${name}-1440.png`), { full });
  shots.push(`${name}-1440.png`);
  if (mobile) {
    await page.viewport(390, 844);
    await sleep(400);
    await page.shot(path.join(OUT, `${name}-390.png`), { full });
    shots.push(`${name}-390.png`);
    await page.viewport(1440, 900);
    await sleep(250);
  }
  if (page.logs.length) {
    console.log(`  ${name}: ${page.logs.splice(0).filter(l => !/401 \(Unauthorized\)/.test(l)).join('\n    ') || 'ok'}`);
  }
  console.log(`shot ${name}`);
}
let skipping = Boolean(FROM);
const step = name => {
  if (name === FROM) skipping = false;
  console.log(`\n# ${name}${skipping ? ' (skipped)' : ''}`);
  return TO && name === TO;
};

try {
  const p = await browser.newPage({ isolated: true });
  await p.viewport(1440, 900);
  await p.scheme('light');
  const owner = await browser.newPage({ isolated: true });
  await owner.viewport(1440, 900);
  await owner.scheme('light');

  // ------------------------------------------------ the public page
  if (step('providers')) throw 'stop';
  if (!skipping) {
  await p.goto(`${BASE}/providers/`);
  await shoot(p, '01-providers');

  // ------------------------------------------------ sign-in
  }
  if (step('login')) throw 'stop';
  if (!skipping) {
  await p.goto(`${BASE}/providers/login/`);
  await p.until("!document.querySelector('#nomail').hidden");
  await shoot(p, '02-login-email');
  await p.type('#email', 'kestrel-at-example');
  await p.click('#send');
  await shoot(p, '03-login-email-error', { mobile: false, full: false });
  await p.type('#email', 'team@kestrel.example');
  await p.click('#send');
  await p.until("!document.querySelector('#step-code').hidden");
  await shoot(p, '04-login-code');
  await p.type('#code', '000000');
  await p.until("document.querySelector('#code').getAttribute('aria-invalid') === 'true'");
  await shoot(p, '05-login-code-wrong', { mobile: false, full: false });
  const code = await outboxCode('team@kestrel.example');
  await p.type('#code', code);
  await p.until("location.pathname === '/providers/apply/'");

  // ------------------------------------------------ the application
  }
  if (step('apply')) throw 'stop';
  if (!skipping) {
  await p.until("!document.querySelector('#view-form').hidden");
  await shoot(p, '06-apply-empty');
  await p.type('#name', 'Kestrel');
  await p.type('#domains', 'https://kestrel');
  await p.blur();
  await p.type('#contact', '');
  await p.click('#submit');
  await shoot(p, '07-apply-errors', { mobile: false });
  await p.type('#domains', 'kestrel.test\n*.kestrel.test');
  await p.type('#testUrl', FIXTURE);
  await p.type('#contact', '@kestrel_support');
  await p.click('input[name="wantedTier"][value="2"]');
  await p.click('#check');
  await p.until("document.querySelector('#check-badge .badge')", 30000);
  await shoot(p, '08-apply-checked');
  await p.click('.seg [data-view="tab"]');
  await shoot(p, '09-apply-preview-tab', { mobile: false, full: false });
  await p.click('#submit');
  await p.until("!document.querySelector('#view-wait').hidden", 45000);
  await shoot(p, '10-apply-sent');

  // ------------------------------------------------ the owner approves
  }
  if (step('admin')) throw 'stop';
  if (!skipping) {
  await owner.goto(adminLink());
  await owner.until("location.pathname === '/admin/' && document.querySelector('[data-view=overview]:not([hidden])')");
  await shoot(owner, '11-admin-overview');
  await owner.goto(`${BASE}/admin/#applications`);
  await owner.until("document.querySelector('[data-app-id]')");
  await shoot(owner, '12-admin-applications');
  await owner.click('[data-approve]');
  await owner.until("document.querySelector('dialog[open]')");
  await shoot(owner, '13-admin-approve-dialog', { mobile: false, full: false });
  await owner.click('dialog[open] [type=submit]');
  await owner.until("!document.querySelector('dialog[open]')");
  await sleep(600);

  // ------------------------------------------------ the cabinet
  }
  if (step('cabinet')) throw 'stop';
  if (!skipping) {
  await p.goto(`${BASE}/providers/cabinet/`);
  await p.until("document.querySelector('[data-view=overview]:not([hidden])')");
  await shoot(p, '14-cabinet-overview');
  await p.goto(`${BASE}/providers/cabinet/#look`);
  await p.until("document.querySelector('[data-view=look]:not([hidden]) #preview-light .v2w')");
  await shoot(p, '15-cabinet-look-default');
  await p.file('#file-mark', PNG.small);
  await p.until("document.querySelector('[data-slot=mark] .field__error:not([hidden])')");
  await shoot(p, '16-cabinet-look-upload-error', { mobile: false, full: false });
  await p.file('#file-mark', PNG.mark);
  await p.until("document.querySelector('[data-slot=mark] img')");
  await p.file('#file-logo', PNG.logo);
  await p.until("document.querySelector('[data-slot=logo] img')");
  await p.file('#file-logoOnLight', PNG.logoOnLight);
  await p.until("document.querySelector('[data-slot=logoOnLight] img')");
  await p.type('.colour input[type=text]', '#2E6BFF');
  await p.click('[data-add-colour]');
  await p.type('.colour:nth-child(2) input[type=text]', '#7AA8FF');
  await p.click('input[name="background"][value="gradient"]');
  await p.until("document.querySelector('#saved').textContent.includes('сохран')", 10000);
  await sleep(800);
  await shoot(p, '17-cabinet-look-designed');
  await p.scheme('dark');
  await sleep(500);
  await shoot(p, '18-cabinet-look-dark');
  await p.scheme('light');
  await p.click('#send-look');
  await p.until("document.querySelector('[data-queue-state]')?.textContent.includes('Ждёт')", 10000);
  await shoot(p, '19-cabinet-look-sent', { mobile: false, full: false });
  await p.goto(`${BASE}/providers/cabinet/#share`);
  await p.until("document.querySelector('[data-view=share]:not([hidden])')");
  await shoot(p, '20-cabinet-share');
  await p.goto(`${BASE}/providers/cabinet/#api`);
  await p.until("document.querySelector('[data-view=api]:not([hidden])')");
  await p.type('#api-url', API);
  await p.click('#api-test');
  await p.until("document.querySelector('#api-out .lines')", 20000);
  await shoot(p, '21-cabinet-api');
  // The stand-in API is plain http, which a published look may not carry: the draft goes back to no API.
  await p.type('#api-url', '');
  await sleep(1500);

  // ------------------------------------------------ the owner publishes
  }
  if (step('publish')) throw 'stop';
  if (!skipping) {
  await owner.goto(`${BASE}/admin/#queue`);
  await owner.until("document.querySelector('[data-queue-id]')");
  await shoot(owner, '22-admin-queue');
  await owner.click('[data-publish]');
  await owner.until("document.querySelector('dialog[open]')");
  await owner.click('dialog[open] [type=submit]');
  await owner.until("!document.querySelector('[data-queue-id]')", 15000);
  await owner.goto(`${BASE}/admin/#providers`);
  await owner.until("document.querySelector('[data-provider-id]')");
  await shoot(owner, '23-admin-providers');
  await owner.goto(`${BASE}/admin/#downloads`);
  await owner.until("document.querySelector('.chart svg')");
  await shoot(owner, '24-admin-downloads');
  await owner.goto(`${BASE}/admin/#audit`);
  await owner.until("document.querySelector('#audit-table tbody tr')");
  await shoot(owner, '25-admin-audit');

  await p.goto(`${BASE}/providers/cabinet/`);
  await p.until("document.querySelector('[data-view=overview]:not([hidden])')");
  await shoot(p, '26-cabinet-published', { mobile: false });

  // ------------------------------------------------ the provider's page
  }
  if (step('page')) throw 'stop';
  if (!skipping) {
  const sub = encodeURIComponent('https://sub.kestrel.test/s/9f2c7a1e');
  await p.goto(`${BASE}/p/kestrel/#sub=${sub}`);
  await shoot(p, '27-page-sub');
  await p.goto(`${BASE}/p/kestrel/`);
  await shoot(p, '28-page-plain', { mobile: false });
  await p.click('#win');
  await sleep(300);
  await shoot(p, '29-page-after-download', { mobile: false, full: false });
  await p.scheme('dark');
  await p.goto(`${BASE}/p/kestrel/#sub=${sub}`);
  await shoot(p, '30-page-sub-dark');
  await p.goto(`${BASE}/p/configra/#sub=${encodeURIComponent('https://sub.configra.app/s/abc')}`);
  await shoot(p, '31-page-configra-dark', { mobile: false });
  await p.scheme('light');
  await p.goto(`${BASE}/p/configra/#sub=${encodeURIComponent('https://sub.configra.app/s/abc')}`);
  await shoot(p, '32-page-configra');

  // ------------------------------------------------ a second provider: a failing link, then a returned application
  }
  if (step('reject')) throw 'stop';
  if (FROM) await owner.goto(adminLink());
  const q = await browser.newPage({ isolated: true });
  await q.viewport(1440, 900);
  await q.scheme('light');
  await q.goto(`${BASE}/providers/login/`);
  await q.type('#email', 'hello@owl.example');
  await q.click('#send');
  await q.until("!document.querySelector('#step-code').hidden");
  await q.type('#code', await outboxCode('hello@owl.example'));
  await q.until("location.pathname === '/providers/apply/' && !document.querySelector('#view-form').hidden");
  await q.type('#name', 'Owl VPN');
  await q.type('#domains', '*.owl.test');
  await q.type('#testUrl', FIXTURE.replace('sub.kestrel.test', 'sub.owl.test').replace('/s/', '/missing/'));
  await q.type('#contact', 'hello@owl.example');
  await q.click('#check');
  await q.until("document.querySelector('#check-badge .badge')", 30000);
  await shoot(q, '33-apply-check-failed');
  await q.type('#testUrl', FIXTURE.replace('sub.kestrel.test', 'sub.owl.test'));
  await q.click('#check');
  await q.until("document.querySelector('#check-badge .badge--ok, #check-badge .badge--warn')", 30000);
  await q.click('#submit');
  await q.until("!document.querySelector('#view-wait').hidden", 45000);
  await owner.goto(`${BASE}/admin/#applications`);
  await owner.until("document.querySelector('[data-app-id]')");
  await owner.click('[data-reject]');
  await owner.until("document.querySelector('dialog[open]')");
  await owner.type('dialog[open] textarea', 'Нужна ссылка на пробный аккаунт с несколькими странами, сейчас только одна страна.');
  await shoot(owner, '34-admin-reject-dialog', { mobile: false, full: false });
  await owner.click('dialog[open] [type=submit]');
  await owner.until("!document.querySelector('dialog[open]')");
  await q.goto(`${BASE}/providers/apply/`);
  await q.until("!document.querySelector('#view-form').hidden");
  await shoot(q, '35-apply-returned');
  await owner.goto(`${BASE}/admin/#outbox`);
  await owner.until("document.querySelector('[data-view=outbox]:not([hidden])')");
  await shoot(owner, '36-admin-outbox');

  // ------------------------------------------------ dark and the rest
  await owner.scheme('dark');
  await owner.goto(`${BASE}/admin/`);
  await owner.until("document.querySelector('[data-view=overview]:not([hidden])')");
  await shoot(owner, '37-admin-overview-dark', { mobile: false });
  await p.scheme('dark');
  await p.goto(`${BASE}/providers/`);
  await shoot(p, '38-providers-dark', { mobile: false });
  console.log('\nwalk: done');
} catch (e) {
  if (e !== 'stop') {
    console.error('walk failed:', e.message || e);
    process.exitCode = 1;
  } else {
    console.log('\nwalk: stopped');
  }
} finally {
  browser.close();
  fs.rmSync(tmp, { recursive: true, force: true });
  console.log(`${shots.length} shots in ${OUT}`);
}
