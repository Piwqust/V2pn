// Sign-in under load and under guessing: hashes off the main thread, two at a time, limits per network (IPv6 by /64
// and by /48), and a login that guessing from elsewhere cannot close to its owner.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  FAILS_PER_LOGIN, FAILS_PER_WIDE_NETWORK, MAX_HASHING, REGISTRATIONS_PER_WIDE_NETWORK, SIGNINS_PER_WIDE_NETWORK, TRUST_MS, hashPasswordSync,
} from '../lib/auth.mjs';
import { ipKey, wideIpKey } from '../lib/ip.mjs';
import { Client, register, startHub } from './helpers.mjs';

const HOUR = 3600 * 1000;
const from = (c, ip, login, password) => c.request('POST', '/api/auth/login', { login, password }, { 'x-forwarded-for': ip });

test('limits count an IPv4 address as it is and an IPv6 address by its /64, and by its /48 as well', () => {
  assert.equal(ipKey('203.0.113.7'), '203.0.113.7');
  assert.equal(ipKey('::ffff:203.0.113.7'), '203.0.113.7');
  assert.equal(ipKey('2001:db8:1:2:3:4:5:6'), '2001:db8:1:2::/64');
  assert.equal(ipKey('2001:DB8:1:2:ffff::1'), '2001:db8:1:2::/64');
  assert.notEqual(ipKey('2001:db8:1:3::1'), ipKey('2001:db8:1:2::1'));
  assert.equal(ipKey('::1'), '0:0:0:0::/64');
  assert.equal(ipKey('nonsense'), 'nonsense');
  assert.equal(wideIpKey('2001:db8:1:2:3:4:5:6'), '2001:db8:1::/48');
  assert.equal(wideIpKey('2001:DB8:1:ffff::1'), '2001:db8:1::/48');
  assert.notEqual(wideIpKey('2001:db8:2::1'), wideIpKey('2001:db8:1::1'));
  for (const v4 of ['203.0.113.7', '::ffff:203.0.113.7', 'nonsense', '']) assert.equal(wideIpKey(v4), null, v4);
});

test('a password is hashed off the main thread, at most two at a time; an unknown login costs the same hash', async () => {
  const env = await startHub();
  try {
    const { auth } = env.hub;
    assert.equal(MAX_HASHING, 2);
    await register(new Client(env.base), 'target', 'right-pass');

    // The event loop goes on while a sign-in is being checked.
    let ticked = false;
    const signing = auth.signIn('target', 'right-pass', '198.51.100.1');
    setImmediate(() => { ticked = true; });
    assert.equal((await signing).account.login, 'target');
    assert.ok(ticked, 'the hash did not block the main thread');

    // Two unknown logins take both slots, as two real ones would; a third sign-in is turned away at once.
    const first = assert.rejects(auth.signIn('nobody-1', 'whatever-1', '198.51.100.2'), e => e.code === 'password_invalid');
    const second = assert.rejects(auth.signIn('nobody-2', 'whatever-2', '198.51.100.3'), e => e.code === 'password_invalid');
    assert.equal(auth.hashing, 2);
    await assert.rejects(auth.signIn('target', 'right-pass', '198.51.100.4'), e => e.code === 'rate_limited');
    await assert.rejects(auth.register('newcomer', 'long-enough', '198.51.100.5'), e => e.code === 'rate_limited');
    await first;
    await second;
    assert.equal(auth.hashing, 0);
    assert.equal((await auth.signIn('target', 'right-pass', '198.51.100.4')).account.login, 'target', 'free again');
  } finally {
    await env.close();
  }
});

test('failed sign-ins close a login to their network only; the owner signs in from elsewhere; a ceiling for everyone', async () => {
  const env = await startHub();
  try {
    await register(new Client(env.base), 'target', 'right-pass');
    const c = new Client(env.base);
    // An attacker on one IPv6 /64, changing addresses inside it.
    const codes = [];
    for (let i = 0; i < 11; i++) codes.push((await from(c, `2001:db8:1:2::${i + 1}`, 'target', `guess-${i}`)).data.error.code);
    assert.deepEqual(codes, [...Array(10).fill('password_invalid'), 'rate_limited']);
    assert.equal((await from(c, '2001:db8:1:2:ffff::9', 'target', 'right-pass')).data.error.code, 'rate_limited', 'the whole /64 waits');
    // The owner, on another network, is not kept out.
    const owner = new Client(env.base);
    const r = await from(owner, '203.0.113.50', 'target', 'right-pass');
    assert.equal(r.status, 200);
    assert.equal((await from(new Client(env.base), '2001:db8:1:3::1', 'target', 'right-pass')).status, 200, 'the next /64 is another network');

    // Guessing from many networks at once still meets a ceiling, for everyone.
    const { limiter } = env.hub;
    assert.equal(limiter.count('signin-fail:target', HOUR), 10, 'the failures above count toward it');
    for (let i = 10; i < FAILS_PER_LOGIN; i++) limiter.hit('signin-fail:target', Infinity, HOUR);
    assert.equal((await from(new Client(env.base), '192.0.2.77', 'target', 'right-pass')).data.error.code, 'rate_limited');
    assert.equal((await from(new Client(env.base), '192.0.2.77', 'other', 'right-pass')).data.error.code, 'password_invalid', 'other logins go on');
    // The networks the login signed in from above are its own: the ceiling does not close them.
    assert.equal((await from(new Client(env.base), '203.0.113.50', 'target', 'right-pass')).status, 200);
    assert.equal((await from(new Client(env.base), '2001:db8:1:3::7', 'target', 'right-pass')).status, 200);
  } finally {
    await env.close();
  }
});

test('a site walking from /64 to /64 meets the /48 limit; the networks a login signed in from lately stay open to it', async () => {
  const env = await startHub();
  try {
    const { auth, limiter } = env.hub;
    await register(new Client(env.base), 'target', 'right-pass');
    // The owner signs in at home, on IPv4 and on an IPv6 /64.
    assert.equal((await from(new Client(env.base), '198.51.100.20', 'target', 'right-pass')).status, 200);
    assert.equal((await from(new Client(env.base), '2001:db8:7:aa::5', 'target', 'right-pass')).status, 200);

    // An attacker with a /48 (the owner's), a new /64 for every two guesses.
    const c = new Client(env.base);
    const codes = [];
    for (let i = 0; i <= FAILS_PER_WIDE_NETWORK; i++) codes.push((await from(c, `2001:db8:7:${Math.floor(i / 2)}::1`, 'target', `guess-${i}`)).data.error.code);
    assert.deepEqual(codes, [...Array(FAILS_PER_WIDE_NETWORK).fill('password_invalid'), 'rate_limited']);
    assert.equal((await from(c, '2001:db8:7:ff::1', 'target', 'right-pass')).data.error.code, 'rate_limited', 'the whole /48 waits');
    assert.equal((await from(new Client(env.base), '2001:db8:7:aa::9', 'target', 'right-pass')).status, 200, "the owner's /64 inside it does not");
    assert.equal((await from(new Client(env.base), '2001:db8:8::1', 'target', 'right-pass')).status, 200, 'the next /48 is another site');

    // Guessing from everywhere up to the ceiling: a new network waits, the owner's own do not.
    for (let i = FAILS_PER_WIDE_NETWORK; i < FAILS_PER_LOGIN; i++) limiter.hit('signin-fail:target', Infinity, HOUR);
    assert.equal((await from(new Client(env.base), '192.0.2.80', 'target', 'right-pass')).data.error.code, 'rate_limited');
    assert.equal((await from(new Client(env.base), '198.51.100.20', 'target', 'right-pass')).status, 200);

    // Sign-ins from one /48 are limited as well, but not on a /64 the login knows.
    for (let i = 0; i < SIGNINS_PER_WIDE_NETWORK; i++) limiter.hit('signin-ip:2001:db8:9::/48', Infinity, HOUR);
    assert.equal((await from(new Client(env.base), '2001:db8:9:1::1', 'other', 'any-pass-1')).data.error.code, 'rate_limited');
    assert.equal((await from(new Client(env.base), '2001:db8:7:aa::5', 'target', 'right-pass')).status, 200);

    // Trust lasts 30 days from the last sign-in there; then the ceiling closes that network too.
    auth.trusted.set('target|198.51.100.20', Date.now() - TRUST_MS);
    assert.equal((await from(new Client(env.base), '198.51.100.20', 'target', 'right-pass')).data.error.code, 'rate_limited');
    auth.sweep();
    assert.ok(!auth.trusted.has('target|198.51.100.20'));
    assert.ok(auth.trusted.has('target|2001:db8:7:aa::/64'));

    // A trusted network keeps its own limit: ten failures there close it like any other.
    const home = new Client(env.base);
    const own = [];
    for (let i = 0; i < 11; i++) own.push((await from(home, '2001:db8:7:aa::5', 'target', `typo-${i}`)).data.error.code);
    assert.deepEqual(own, [...Array(10).fill('password_invalid'), 'rate_limited']);
  } finally {
    await env.close();
  }
});

test('registrations are counted per /64; new passwords need 8 characters, an older shorter one still signs in', async () => {
  const env = await startHub();
  try {
    const c = new Client(env.base);
    const reg = (ip, login, password = 'secret-123') => c.request('POST', '/api/auth/register', { login, password }, { 'x-forwarded-for': ip });
    let r = await reg('2001:db8:9:9::1', 'short-pass', 'seven77');
    assert.equal(r.status, 422);
    assert.equal(r.data.error.fields.password, 'Пароль от 8 символов.');
    const statuses = [];
    for (let i = 0; i < 10; i++) statuses.push((await reg(`2001:db8:9:9::${i + 2}`, `v6-user-${i}`)).status);
    assert.deepEqual(statuses, [...Array(9).fill(200), 429], 'the refused one above counts too: ten an hour from one /64');
    assert.equal((await reg('2001:db8:9:a::1', 'v6-next')).status, 200);
    // And per /48: a new /64 there waits once the /48 had its share.
    for (let i = 0; i < REGISTRATIONS_PER_WIDE_NETWORK; i++) env.hub.limiter.hit('register-ip:2001:db8:9::/48', Infinity, 3600 * 1000);
    assert.equal((await reg('2001:db8:9:b::1', 'v6-wide')).status, 429);
    assert.equal((await reg('2001:db8:a::1', 'v6-other-site')).status, 200);

    // An account from the days of six characters.
    env.hub.db.run("INSERT INTO accounts (login, role, password, created_at) VALUES ('old-timer', 'provider', ?, '2026-11-01')", hashPasswordSync('abc123'));
    const old = new Client(env.base);
    r = await from(old, '203.0.113.9', 'old-timer', 'abc123');
    assert.equal(r.status, 200);
    r = await old.post('/api/auth/password', { password: 'abc123', newPassword: 'short77' });
    assert.equal(r.data.error.fields.newPassword, 'Пароль от 8 символов.');
    assert.equal((await old.post('/api/auth/password', { password: 'abc123', newPassword: 'longer-one' })).status, 200);
  } finally {
    await env.close();
  }
});
