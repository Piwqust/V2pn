import assert from 'node:assert/strict';
import { test } from 'node:test';
import { checkSubscription, plural } from '../lib/check.mjs';
import { decodeText, parseBody, parseUserInfo, readHeaders, summarize } from '../lib/subscription.mjs';
import { fixture, serve, testNet } from './helpers.mjs';

test('base64 share links: every protocol counted, the body lines above them read', () => {
  const body = parseBody(fixture('links-base64.txt'));
  assert.equal(body.format, 'base64');
  assert.deepEqual(summarize(body.servers), {
    total: 11, supported: 10,
    byProtocol: { vless: 2, vmess: 1, trojan: 1, shadowsocks: 2, hysteria2: 2, tuic: 1, wireguard: 1, shadowsocksr: 1 },
  });
  assert.deepEqual(body.servers.slice(0, 3).map(s => `${s.host}:${s.port}`), ['fi1.kestrel.example:443', '2001:db8::10:8443', 'de1.kestrel.example:443']);
  assert.equal(body.servers.find(s => s.protocol === 'shadowsocks' && s.host === 'nl1.kestrel.example').port, 8388, 'the legacy ss:// form');
  const headers = readHeaders({}, body.meta);
  assert.equal(headers.title, 'Кестрел VPN');
  assert.equal(headers.updateIntervalHours, 12);
});

test('plain share links with the userinfo line', () => {
  const body = parseBody(fixture('links-plain.txt'));
  assert.equal(body.format, 'plain');
  assert.equal(body.servers.length, 11);
  const h = readHeaders({}, body.meta);
  assert.deepEqual(h.userInfo, { upload: 1073741824, download: 11811160064, total: 53687091200, expire: 1801353600 });
});

test('Xray JSON (a list of configs, as Remnawave gives it), sing-box and Clash', () => {
  const xray = parseBody(fixture('xray.json'));
  assert.equal(xray.format, 'xray-json');
  assert.deepEqual(summarize(xray.servers), { total: 3, supported: 3, byProtocol: { vless: 2, shadowsocks: 1 } });
  const singbox = parseBody(fixture('singbox.json'));
  assert.equal(singbox.format, 'singbox');
  assert.deepEqual(summarize(singbox.servers), { total: 5, supported: 4, byProtocol: { vless: 1, hysteria2: 1, tuic: 1, shadowtls: 1, wireguard: 1 } });
  const clash = parseBody(fixture('clash.yaml'));
  assert.equal(clash.format, 'clash');
  assert.deepEqual(summarize(clash.servers), { total: 5, supported: 4, byProtocol: { vless: 1, trojan: 1, hysteria2: 1, shadowsocks: 1, shadowsocksr: 1 } });
  assert.deepEqual(clash.servers.map(s => s.host), ['de1.kestrel.example', 'nl1.kestrel.example', 'fi1.kestrel.example', 'ss1.kestrel.example', 'ssr.kestrel.example']);
  assert.equal(parseBody(Buffer.from('<html>not a subscription</html>')).servers.length, 0);
  assert.equal(parseBody(Buffer.from('')).format, 'empty');
});

test('panel headers: base64 and raw UTF-8 titles, userinfo in any order, milliseconds, links', () => {
  assert.equal(decodeText('base64:' + Buffer.from('Мой VPN 🚀').toString('base64')), 'Мой VPN 🚀');
  // UTF-8 sent raw in a header arrives as Latin-1.
  assert.equal(decodeText(Buffer.from('Кестрел', 'utf8').toString('latin1')), 'Кестрел');
  assert.deepEqual(parseUserInfo('expire=1801353600000; total=0, download=5;upload=x'), { upload: 0, download: 5, total: 0, expire: 1801353600 });
  assert.equal(parseUserInfo('nothing'), null);
  const h = readHeaders({
    'profile-title': 'base64:' + Buffer.from('Кестрел').toString('base64'),
    'subscription-userinfo': 'upload=1; download=2; total=3; expire=4',
    'support-url': 'https://t.me/kestrel_support',
    'profile-web-page-url': 'https://kestrel.example/cabinet',
    announce: 'base64:' + Buffer.from('Новые серверы').toString('base64'),
    'announce-url': 'http://insecure.example/',
    'profile-update-interval': '24',
    'sub-info-text': 'Скидка',
    'hide-settings': 'true',
    'x-other': 'ignored',
  }, { 'profile-title': 'from the body' });
  assert.equal(h.title, 'Кестрел', 'a header wins over the body');
  assert.deepEqual(h.userInfo, { upload: 1, download: 2, total: 3, expire: 4 });
  assert.equal(h.supportUrl, 'https://t.me/kestrel_support');
  assert.equal(h.webPageUrl, 'https://kestrel.example/cabinet');
  assert.equal(h.announce, 'Новые серверы');
  assert.equal(h.announceUrl, null, 'announce-url takes https or tg only');
  assert.equal(h.updateIntervalHours, 24);
  assert.equal(h.infoText, 'Скидка');
  assert.equal(h.hideSettings, true);
  assert.equal(readHeaders({ 'content-disposition': "attachment; filename*=UTF-8''%D0%9A%D0%B5%D1%81%D1%82%D1%80%D0%B5%D0%BB.yaml" }).title, 'Кестрел');
});

test('Russian plurals', () => {
  assert.deepEqual([1, 2, 5, 11, 21, 22, 25, 112].map(n => plural(n, 'сервер', 'сервера', 'серверов')),
    ['сервер', 'сервера', 'серверов', 'серверов', 'сервер', 'сервера', 'серверов', 'серверов']);
});

test('the whole check: one line per finding, servers pinged', async () => {
  const servers = await serve((req, res) => res.end());
  const sub = await serve((req, res) => {
    if (req.headers['user-agent'] !== 'v2rayN/7.25.6 V2pn/1.2.2') { res.writeHead(403); res.end(); return; }
    if (req.url === '/empty') { res.end('<html></html>'); return; }
    res.writeHead(200, {
      'profile-title': 'base64:' + Buffer.from('Кестрел').toString('base64'),
      'subscription-userinfo': 'upload=1073741824; download=11811160064; total=53687091200; expire=1801353600',
      'support-url': 'https://t.me/kestrel_support',
    });
    const links = [1, 2, 3].map(i => `vless://id@s${i}.kestrel.test:${servers.port}?security=reality#S${i}`).concat(['ssr://eA']);
    res.end(Buffer.from(links.join('\n')).toString('base64'));
  });
  try {
    const url = `http://sub.kestrel.test:${sub.port}/s/abc`;
    const result = await checkSubscription(url, { userAgent: 'v2rayN/7.25.6 V2pn/1.2.2', net: testNet });
    const byId = Object.fromEntries(result.lines.map(l => [l.id, l]));
    assert.equal(result.ok, true);
    assert.equal(byId.open.level, 'ok');
    assert.equal(byId.servers.level, 'warn');
    assert.equal(byId.servers.text, '4 сервера');
    assert.match(byId.servers.detail, /VLESS — 3, ShadowsocksR — 1\. V2pn не запустит 1 из них\./);
    assert.equal(byId.title.detail, 'Кестрел');
    assert.equal(byId.userinfo.detail, '12 из 50 ГБ, до 31.01.2027');
    assert.equal(byId.support.level, 'ok');
    assert.equal(byId.cabinet.level, 'warn');
    assert.equal(byId.tcp.level, 'ok');
    assert.equal(result.tcp.length, 3);

    const empty = await checkSubscription(`http://sub.kestrel.test:${sub.port}/empty`, { userAgent: 'v2rayN/7.25.6 V2pn/1.2.2', net: testNet });
    assert.equal(empty.lines.find(l => l.id === 'servers').level, 'fail');

    const forbidden = await checkSubscription(url, { userAgent: 'curl/8', net: testNet });
    assert.equal(forbidden.lines.find(l => l.id === 'open').level, 'fail');

    const privateOne = await checkSubscription('https://10.0.0.1/sub', { userAgent: 'x' });
    assert.match(privateOne.lines.find(l => l.id === 'open').detail, /внутреннюю сеть/);
  } finally {
    await sub.close();
    await servers.close();
  }
});
