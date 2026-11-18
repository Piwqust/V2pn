import assert from 'node:assert/strict';
import { test } from 'node:test';
import { isPublicIp } from '../lib/ip.mjs';
import { NetError, safeFetch, tcpPing, vetHost } from '../lib/net.mjs';
import { serve } from './helpers.mjs';

test('only public unicast addresses are public', () => {
  for (const ip of ['1.1.1.1', '8.8.8.8', '93.184.216.34', '144.31.223.224', '2606:4700:4700::1111', '2a00:1450:4001:80b::200e']) assert.ok(isPublicIp(ip), ip);
  for (const ip of [
    '127.0.0.1', '127.8.8.8', '10.1.2.3', '172.16.0.1', '172.31.255.255', '192.168.1.1', '169.254.169.254', '100.64.0.1', '100.127.255.255',
    '0.0.0.0', '224.0.0.1', '239.255.255.250', '255.255.255.255', '240.0.0.1', '192.0.2.1', '198.18.0.1', '198.51.100.7', '203.0.113.9', '192.0.0.8',
    '::', '::1', 'fe80::1', 'fc00::1', 'fd12:3456::1', 'ff02::1', '::ffff:127.0.0.1', '::ffff:10.0.0.1', '::ffff:7f00:1', '64:ff9b::a00:1',
    '2001:db8::1', '2002:7f00:1::1', '2001::1', '::127.0.0.1', 'not-an-ip',
  ]) assert.equal(isPublicIp(ip), false, ip);
  assert.ok(isPublicIp('::ffff:8.8.8.8'));
  assert.ok(isPublicIp('64:ff9b::808:808'));
});

const refuses = async (promise, code) => {
  await assert.rejects(promise, e => e instanceof NetError && e.code === code);
};

test('private addresses are refused, whether literal, resolved or among several answers', async () => {
  await refuses(safeFetch('https://127.0.0.1/sub'), 'private');
  await refuses(safeFetch('https://[::1]/sub'), 'private');
  await refuses(safeFetch('https://169.254.169.254/latest/meta-data'), 'private');
  await refuses(safeFetch('https://sub.example/', { resolve: async () => [{ address: '10.0.0.5', family: 4 }] }), 'private');
  // One private answer among public ones refuses the name as a whole.
  await refuses(safeFetch('https://sub.example/', { resolve: async () => [{ address: '1.1.1.1', family: 4 }, { address: '192.168.0.10', family: 4 }] }), 'private');
  await refuses(safeFetch('https://sub.example/', { resolve: async () => [{ address: '::ffff:127.0.0.1', family: 6 }] }), 'private');
  await refuses(safeFetch('https://nothing.example/', { resolve: async () => [] }), 'dns');
  await refuses(vetHost('0.0.0.0'), 'private');
});

test('only https, without a user name or password in the link', async () => {
  await refuses(safeFetch('http://1.1.1.1/'), 'url');
  await refuses(safeFetch('ftp://1.1.1.1/'), 'url');
  await refuses(safeFetch('https://user:pass@1.1.1.1/'), 'url');
  await refuses(safeFetch('not a url'), 'url');
});

test('the connection goes to the address that was checked, with the original Host (DNS rebinding cannot move it)', async () => {
  const srv = await serve((req, res) => res.end(JSON.stringify({ host: req.headers.host, ua: req.headers['user-agent'] })));
  let calls = 0;
  // A rebinding resolver: the first answer is the "public" one the check sees, the next would be elsewhere.
  const resolve = async () => (calls++ === 0 ? [{ address: '127.0.0.1', family: 4 }] : [{ address: '10.9.9.9', family: 4 }]);
  try {
    const res = await safeFetch(`http://rebind.example:${srv.port}/sub`, { resolve, allow: ip => ip === '127.0.0.1', allowHttp: true, headers: { 'user-agent': 'v2rayN/7.25.6 V2pn/1.2.2' } });
    assert.equal(res.status, 200);
    assert.equal(res.address, '127.0.0.1');
    assert.equal(calls, 1, 'one lookup per hop: nothing resolves the name again behind the check');
    const body = JSON.parse(res.body);
    assert.equal(body.host, `rebind.example:${srv.port}`);
    assert.equal(body.ua, 'v2rayN/7.25.6 V2pn/1.2.2');
  } finally {
    await srv.close();
  }
});

test('redirects: followed to allowed places only, three at most', async () => {
  const srv = await serve((req, res) => {
    const n = Number(req.url.slice(1));
    if (req.url === '/to-private') { res.writeHead(302, { location: 'http://10.0.0.1/' }); res.end(); return; }
    if (req.url === '/to-name') { res.writeHead(301, { location: `http://inner.example:${req.socket.localPort}/0` }); res.end(); return; }
    if (n > 0) { res.writeHead(302, { location: `/${n - 1}` }); res.end(); return; }
    res.end('ok');
  });
  const net = { allowHttp: true, allow: ip => ip === '127.0.0.1', resolve: async host => (host === 'inner.example' ? [{ address: '192.168.1.1', family: 4 }] : [{ address: '127.0.0.1', family: 4 }]) };
  try {
    const three = await safeFetch(`${srv.url}/3`, net);
    assert.equal(three.status, 200);
    assert.equal(three.redirects, 3);
    await refuses(safeFetch(`${srv.url}/4`, net), 'redirect');
    await refuses(safeFetch(`${srv.url}/to-private`, net), 'private');
    await refuses(safeFetch(`${srv.url}/to-name`, net), 'private');
    // With redirects off the answer itself comes back (the sign-in API is asked that way).
    const raw = await safeFetch(`${srv.url}/1`, { ...net, redirects: 0 });
    assert.equal(raw.status, 302);
  } finally {
    await srv.close();
  }
});

test('size and time are capped', async () => {
  const srv = await serve((req, res) => {
    if (req.url === '/big') { res.end(Buffer.alloc(3 * 1024 * 1024, 65)); return; }
    if (req.url === '/slow') { setTimeout(() => res.end('late'), 3000); return; }
    res.end('fine');
  });
  const net = { allowHttp: true, allow: () => true };
  try {
    await refuses(safeFetch(`${srv.url}/big`, { ...net, maxBytes: 2 * 1024 * 1024 }), 'too_large');
    await refuses(safeFetch(`${srv.url}/slow`, { ...net, timeoutMs: 300 }), 'timeout');
    assert.equal((await safeFetch(`${srv.url}/`, net)).body.toString(), 'fine');
  } finally {
    await srv.close();
  }
});

test('the TCP check reaches public addresses only', async () => {
  const srv = await serve((req, res) => res.end());
  try {
    assert.deepEqual(await tcpPing('127.0.0.1', srv.port), { ok: false, error: 'private' });
    const ok = await tcpPing('127.0.0.1', srv.port, { allow: () => true });
    assert.equal(ok.ok, true);
    assert.equal(typeof ok.ms, 'number');
    const shut = await tcpPing('127.0.0.1', 1, { allow: () => true, timeoutMs: 500 });
    assert.equal(shut.ok, false);
  } finally {
    await srv.close();
  }
});
