import assert from 'node:assert/strict';
import net from 'node:net';
import { test } from 'node:test';
import { parseSmtpUrl, sendMail } from '../lib/mail.mjs';
import { Client, signIn, startHub } from './helpers.mjs';

test('no mail: the code goes to the outbox, the owner sees it, the person signs in with it', async () => {
  const env = await startHub();
  try {
    const owner = new Client(env.base);
    const person = new Client(env.base);
    const sent = await person.post('/api/auth/code', { email: ' Provider@Example.com ' });
    assert.equal(sent.status, 200);
    assert.equal(sent.data.delivery, 'outbox');
    assert.equal(sent.headers.get('cache-control'), 'no-store');

    // Not signed in yet.
    assert.equal((await person.get('/api/me')).status, 401);
    assert.equal((await person.get('/api/admin/outbox')).data.error.code, 'unauthorized');

    // The owner's first sign-in: the one-time link from the command line.
    const link = env.hub.auth.createAdminLink('owner@example.com');
    const opened = await owner.get(new URL(link).pathname + new URL(link).search);
    assert.equal(opened.status, 302);
    assert.equal(opened.headers.get('location'), '/admin/');
    assert.match(opened.headers.get('set-cookie'), /^hub_s=[A-Za-z0-9_-]{43}; HttpOnly; SameSite=Lax; Path=\/; Max-Age=2592000$/);
    const again = await new Client(env.base).get(new URL(link).pathname + new URL(link).search);
    assert.equal(again.data.error.code, 'link_invalid', 'the link works once');

    const outbox = await owner.get('/api/admin/outbox');
    assert.equal(outbox.status, 200);
    const mail = outbox.data.outbox.find(m => m.email === 'provider@example.com');
    const code = /(\d{6})/.exec(mail.body)[1];

    const wrong = await person.post('/api/auth/verify', { email: 'provider@example.com', code: code === '000000' ? '111111' : '000000' });
    assert.equal(wrong.data.error.code, 'code_invalid');
    assert.equal(wrong.data.error.detail.triesLeft, 4);
    const ok = await person.post('/api/auth/verify', { email: 'provider@example.com', code });
    assert.equal(ok.status, 200);
    assert.deepEqual(ok.data.account, { email: 'provider@example.com', admin: false });
    const me = await person.get('/api/me');
    assert.equal(me.data.account.email, 'provider@example.com');
    assert.equal((await person.get('/api/admin/outbox')).status, 403, 'a provider is not an owner');
    assert.equal((await owner.get('/api/admin/outbox')).data.outbox.length, 0, 'a used code leaves the outbox');

    // The code stored is a hash, never the digits.
    assert.ok(env.hub.db.all('SELECT code_hash FROM login_codes').every(r => !r.code_hash.includes(code)));

    const out = await person.post('/api/auth/signout');
    assert.match(out.headers.get('set-cookie'), /Max-Age=0/);
    assert.equal((await person.get('/api/me')).status, 401);
  } finally {
    await env.close();
  }
});

test('a code takes five tries, then a new one is needed', async () => {
  const env = await startHub();
  try {
    const c = new Client(env.base);
    await c.post('/api/auth/code', { email: 'a@example.com' });
    const code = /(\d{6})/.exec(env.hub.db.get('SELECT body FROM outbox').body)[1];
    const wrongCode = code === '123456' ? '654321' : '123456';
    const codes = [];
    for (let i = 0; i < 5; i++) codes.push((await c.post('/api/auth/verify', { email: 'a@example.com', code: wrongCode })).data.error.code);
    assert.deepEqual(codes, ['code_invalid', 'code_invalid', 'code_invalid', 'code_invalid', 'code_expired']);
    assert.equal((await c.post('/api/auth/verify', { email: 'a@example.com', code })).data.error.code, 'code_expired', 'even the right code is refused now');
  } finally {
    await env.close();
  }
});

test('codes are limited per email and per address', async () => {
  const env = await startHub();
  try {
    const c = new Client(env.base);
    assert.equal((await c.post('/api/auth/code', { email: 'b@example.com' })).status, 200);
    const soon = await c.post('/api/auth/code', { email: 'b@example.com' });
    assert.equal(soon.status, 429);
    assert.equal(soon.data.error.code, 'rate_limited');
    const statuses = [];
    for (let i = 0; i < 10; i++) statuses.push((await c.post('/api/auth/code', { email: `p${i}@example.com` })).status);
    assert.deepEqual(statuses, [200, 200, 200, 200, 200, 200, 200, 200, 429, 429], 'ten requests an hour from one address, the two above included');
    // Behind Caddy the visitor's address comes from X-Forwarded-For.
    const other = await c.request('POST', '/api/auth/code', { email: 'q@example.com' }, { 'x-forwarded-for': '203.0.113.7' });
    assert.equal(other.status, 200);
    assert.equal((await c.post('/api/auth/code', { email: 'not-an-email' })).data.error.fields.email !== undefined, true);
  } finally {
    await env.close();
  }
});

test('every write needs X-Hub and the hub origin (CSRF)', async () => {
  const env = await startHub();
  try {
    const noHeader = new Client(env.base, { xhub: false });
    const r1 = await noHeader.post('/api/auth/code', { email: 'c@example.com' });
    assert.equal(r1.status, 403);
    assert.equal(r1.data.error.code, 'csrf');
    const otherSite = new Client(env.base, { origin: 'https://evil.example' });
    assert.equal((await otherSite.post('/api/auth/code', { email: 'c@example.com' })).data.error.code, 'csrf');
    const noOrigin = new Client(env.base, { origin: null });
    assert.equal((await noOrigin.post('/api/auth/code', { email: 'c@example.com' })).data.error.code, 'csrf');
    // A signed-in person is protected the same way.
    const person = new Client(env.base);
    await signIn(env, person, 'c@example.com');
    const stolen = new Client(env.base, { origin: 'https://evil.example' });
    stolen.cookie = person.cookie;
    assert.equal((await stolen.put('/api/application', { name: 'Evil' })).data.error.code, 'csrf');
    assert.equal((await person.put('/api/application', { name: 'Kestrel' })).status, 200);
  } finally {
    await env.close();
  }
});

test('bodies are limited, bad JSON and unknown routes answer with stable codes', async () => {
  const env = await startHub();
  try {
    const c = new Client(env.base);
    const big = await c.request('POST', '/api/auth/code', Buffer.alloc(70 * 1024, 32), { 'content-type': 'application/json' });
    assert.equal(big.data.error.code, 'too_large');
    const bad = await c.request('POST', '/api/auth/code', Buffer.from('{nope'), { 'content-type': 'application/json' });
    assert.equal(bad.data.error.code, 'bad_json');
    assert.equal((await c.get('/api/nothing')).data.error.code, 'not_found');
    assert.equal((await c.request('DELETE', '/api/me')).data.error.code, 'method');
    const health = await c.get('/api/health');
    assert.deepEqual({ ok: health.data.ok, mail: health.data.mail }, { ok: true, mail: 'outbox' });
  } finally {
    await env.close();
  }
});

test('with SMTP the code is mailed: AUTH, a UTF-8 message, nothing in the outbox', async () => {
  const received = [];
  const smtp = net.createServer(socket => {
    socket.setEncoding('utf8');
    let data = false;
    let buffer = '';
    const session = { lines: [] };
    socket.write('220 test ESMTP\r\n');
    socket.on('data', chunk => {
      buffer += chunk;
      let nl;
      while ((nl = buffer.indexOf('\r\n')) >= 0) {
        const line = buffer.slice(0, nl);
        buffer = buffer.slice(nl + 2);
        if (data) {
          if (line === '.') { data = false; received.push(session); socket.write('250 queued\r\n'); } else session.lines.push(line);
          continue;
        }
        if (/^EHLO/.test(line)) socket.write('250-test\r\n250-AUTH LOGIN PLAIN\r\n250 8BITMIME\r\n');
        else if (/^AUTH PLAIN /.test(line)) { session.auth = Buffer.from(line.slice(11), 'base64').toString(); socket.write('235 ok\r\n'); }
        else if (/^MAIL FROM:/.test(line)) { session.from = line; socket.write('250 ok\r\n'); }
        else if (/^RCPT TO:/.test(line)) { session.to = line; socket.write('250 ok\r\n'); }
        else if (line === 'DATA') { data = true; socket.write('354 go\r\n'); }
        else if (line === 'QUIT') { socket.write('221 bye\r\n'); socket.end(); }
        else socket.write('500 what\r\n');
      }
    });
  });
  await new Promise(r => smtp.listen(0, '127.0.0.1', r));
  const smtpConfig = parseSmtpUrl(`smtp://hub%40example.com:p%40ss@127.0.0.1:${smtp.address().port}`);
  const env = await startHub({ mailer: message => sendMail(smtpConfig, { from: 'V2pn Hub <no-reply@example.com>', ...message }) });
  try {
    const c = new Client(env.base);
    const r = await c.post('/api/auth/code', { email: 'mail@example.com' });
    assert.equal(r.data.delivery, 'mail');
    assert.equal(received.length, 1);
    const s = received[0];
    assert.equal(s.auth, '\0hub@example.com\0p@ss');
    assert.equal(s.from, 'MAIL FROM:<no-reply@example.com>');
    assert.equal(s.to, 'RCPT TO:<mail@example.com>');
    const sep = s.lines.indexOf('');
    const headers = s.lines.slice(0, sep);
    const body = Buffer.from(s.lines.slice(sep + 1).join(''), 'base64').toString('utf8');
    assert.ok(headers.includes('Content-Type: text/plain; charset=utf-8'));
    assert.ok(headers.some(h => h.startsWith('Subject: =?UTF-8?B?')));
    const code = /(\d{6})/.exec(body)[1];
    assert.equal(env.hub.db.get('SELECT COUNT(*) AS n FROM outbox').n, 0);
    assert.equal((await c.post('/api/auth/verify', { email: 'mail@example.com', code })).status, 200);
  } finally {
    await env.close();
    smtp.close();
  }
});
