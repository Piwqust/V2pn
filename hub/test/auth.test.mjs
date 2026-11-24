import assert from 'node:assert/strict';
import { test } from 'node:test';
import { Client, register, signIn, signInOwner, startHub } from './helpers.mjs';

test('register, sign out, sign in by login and password; only the hash is kept', async () => {
  const env = await startHub();
  try {
    const person = new Client(env.base);
    assert.equal((await person.get('/api/me')).status, 401);
    let r = await person.post('/api/auth/register', { login: 'ab', password: '123' });
    assert.equal(r.status, 422);
    assert.deepEqual(Object.keys(r.data.error.fields).sort(), ['login', 'password']);
    r = await person.post('/api/auth/register', { login: ' Nord.Line ', password: 'secret-123' });
    assert.equal(r.status, 200);
    assert.deepEqual(r.data.account, { login: 'nord.line', role: 'provider' });
    assert.match(r.headers.get('set-cookie'), /^hub_s=[A-Za-z0-9_-]{43}; HttpOnly; SameSite=Lax; Path=\/; Max-Age=2592000$/);
    assert.equal(r.headers.get('cache-control'), 'no-store');
    const me = await person.get('/api/me');
    assert.deepEqual(me.data, { account: { login: 'nord.line', role: 'provider' }, partner: null });

    const stored = env.hub.db.get('SELECT password FROM accounts WHERE login = ?', 'nord.line').password;
    assert.match(stored, /^scrypt\$32768\$8\$1\$/);
    assert.ok(!stored.includes('secret-123'));

    assert.equal((await new Client(env.base).post('/api/auth/register', { login: 'NORD.LINE', password: 'another1' })).data.error.code, 'login_taken');

    const out = await person.post('/api/auth/signout');
    assert.match(out.headers.get('set-cookie'), /Max-Age=0/);
    assert.equal((await person.get('/api/me')).status, 401);
    assert.equal((await person.post('/api/auth/login', { login: 'nord.line', password: 'wrong-one' })).data.error.code, 'password_invalid');
    assert.equal((await person.post('/api/auth/login', { login: 'nobody', password: 'secret-123' })).data.error.code, 'password_invalid',
      'an unknown login gets the same answer');
    await signIn(person, 'NORD.line', 'secret-123');
    assert.equal((await person.get('/api/me')).status, 200);
    assert.equal((await person.get('/api/mod/partners')).status, 403, 'a provider is not a moderator');

    // A new password of one's own ends the other sessions, not this one.
    const other = new Client(env.base);
    await signIn(other, 'nord.line', 'secret-123');
    assert.equal((await person.post('/api/auth/password', { password: 'wrong', newPassword: 'newer-456' })).data.error.code, 'password_invalid');
    assert.equal((await person.post('/api/auth/password', { password: 'secret-123', newPassword: 'x' })).data.error.fields.newPassword !== undefined, true);
    assert.equal((await person.post('/api/auth/password', { password: 'secret-123', newPassword: 'newer-456' })).status, 200);
    assert.equal((await person.get('/api/me')).status, 200);
    assert.equal((await other.get('/api/me')).status, 401);
    await signIn(new Client(env.base), 'nord.line', 'newer-456');
  } finally {
    await env.close();
  }
});

test('failed sign-ins are limited per login, registrations per address', async () => {
  const env = await startHub();
  try {
    await register(new Client(env.base), 'target', 'right-pass');
    const c = new Client(env.base);
    const codes = [];
    for (let i = 0; i < 11; i++) codes.push((await c.post('/api/auth/login', { login: 'target', password: `x${i}xxxx` })).data.error.code);
    assert.deepEqual(codes, [...Array(10).fill('password_invalid'), 'rate_limited']);
    // Even the right password waits now: guessing is cut off for this login from this network (login-limits.test.mjs).
    assert.equal((await c.post('/api/auth/login', { login: 'target', password: 'right-pass' })).data.error.code, 'rate_limited');

    const statuses = [];
    for (let i = 0; i < 10; i++) statuses.push((await c.post('/api/auth/register', { login: `p${i}-user`, password: 'secret-123' })).status);
    assert.deepEqual(statuses, [200, 200, 200, 200, 200, 200, 200, 200, 200, 429], 'ten an hour from one address, the first one included');
    // Behind Caddy the visitor's address comes from X-Forwarded-For.
    const other = await c.request('POST', '/api/auth/register', { login: 'q-user', password: 'secret-123' }, { 'x-forwarded-for': '203.0.113.7' });
    assert.equal(other.status, 200);
  } finally {
    await env.close();
  }
});

test('the owner from the command line; a migrated account without a password cannot sign in until reset', async () => {
  const env = await startHub();
  try {
    const owner = new Client(env.base);
    const password = await signInOwner(env, owner, 'boss');
    assert.match(password, /^[a-z2-9]{4}-[a-z2-9]{4}-[a-z2-9]{4}$/);
    assert.deepEqual((await owner.get('/api/me')).data.account, { login: 'boss', role: 'owner' });
    assert.equal((await owner.get('/api/mod/moderators')).status, 200);
    // Running it again gives a new password and ends the sessions of the old one.
    env.hub.auth.makeOwner('boss');
    assert.equal((await owner.get('/api/me')).status, 401);

    env.hub.db.run("INSERT INTO accounts (login, role, created_at) VALUES ('old@example.com', 'provider', ?)", new Date().toISOString());
    const old = new Client(env.base);
    assert.equal((await old.post('/api/auth/login', { login: 'old@example.com', password: '' })).data.error.code, 'password_invalid');
    const id = env.hub.db.get("SELECT id FROM accounts WHERE login = 'old@example.com'").id;
    const fresh = env.hub.auth.resetPassword(id);
    await signIn(old, 'Old@Example.com', fresh);
  } finally {
    await env.close();
  }
});

test('every write needs X-Hub and the hub origin (CSRF)', async () => {
  const env = await startHub();
  try {
    const body = { login: 'csrf-user', password: 'secret-123' };
    const noHeader = new Client(env.base, { xhub: false });
    const r1 = await noHeader.post('/api/auth/register', body);
    assert.equal(r1.status, 403);
    assert.equal(r1.data.error.code, 'csrf');
    assert.equal((await new Client(env.base, { origin: 'https://evil.example' }).post('/api/auth/register', body)).data.error.code, 'csrf');
    assert.equal((await new Client(env.base, { origin: null }).post('/api/auth/register', body)).data.error.code, 'csrf');
    // A signed-in person is protected the same way.
    const person = new Client(env.base);
    await register(person, 'csrf-user');
    const stolen = new Client(env.base, { origin: 'https://evil.example' });
    stolen.cookie = person.cookie;
    assert.equal((await stolen.put('/api/partner', { name: 'Evil' })).data.error.code, 'csrf');
    assert.equal((await stolen.post('/api/complaints', { partner: 'x' })).data.error.code, 'csrf');
  } finally {
    await env.close();
  }
});

test('bodies are limited, bad JSON and unknown routes answer with stable codes', async () => {
  const env = await startHub();
  try {
    const c = new Client(env.base);
    const big = await c.request('POST', '/api/auth/login', Buffer.alloc(70 * 1024, 32), { 'content-type': 'application/json' });
    assert.equal(big.data.error.code, 'too_large');
    const bad = await c.request('POST', '/api/auth/login', Buffer.from('{nope'), { 'content-type': 'application/json' });
    assert.equal(bad.data.error.code, 'bad_json');
    assert.equal((await c.get('/api/nothing')).data.error.code, 'not_found');
    assert.equal((await c.request('DELETE', '/api/me')).data.error.code, 'method');
    for (const gone of ['/api/auth/code', '/api/auth/verify', '/api/application', '/api/cabinet', '/api/admin/outbox']) {
      assert.equal((await c.get(gone)).status, 404, gone);
    }
    const health = await c.get('/api/health');
    assert.equal(health.data.ok, true);
    assert.equal(health.data.mail, undefined);
  } finally {
    await env.close();
  }
});
