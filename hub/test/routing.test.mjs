// routing.json as V2pnRoutingPreset.Parse reads it: the same entries taken, the same ones refused.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { domainEntry, ipEntry, parseRouting, routingBytes, routingFromText, routingToText, tooBroadToBlock } from '../lib/routing.mjs';

const problem = input => {
  try {
    parseRouting(input);
    return null;
  } catch (e) {
    return e.fields['rights.routing'];
  }
};

test('entries are written the way V2pn routing writes them', () => {
  assert.equal(domainEntry('YouTube.com.').value, 'domain:youtube.com');
  assert.equal(domainEntry('full:api.example.com').value, 'full:api.example.com');
  assert.equal(domainEntry('geosite:category-ads-all').value, 'geosite:category-ads-all');
  assert.equal(domainEntry('keyword:goo').value, 'keyword:goo');
  assert.ok(domainEntry('keyword:go').problem);
  assert.ok(domainEntry('regexp:.*').problem, 'no regular expressions: a reviewer reads every entry');
  assert.ok(domainEntry('exa mple.com').problem);
  assert.equal(ipEntry('10.0.0.0/8').value, '10.0.0.0/8');
  assert.equal(ipEntry('2001:DB8::0:1').value, '2001:db8::1');
  assert.equal(ipEntry('geoip:ru').value, 'geoip:ru');
  assert.ok(ipEntry('1.2.3').problem);
  assert.ok(ipEntry('010.0.0.1').problem, 'a leading zero would be octal to .NET');
  assert.ok(ipEntry('1.2.3.4/33').problem);
});

test('block may not take anything broad', () => {
  assert.match(tooBroadToBlock('geoip:ru'), /страну/);
  assert.match(tooBroadToBlock('domain:ru'), /зону/);
  assert.match(tooBroadToBlock('keyword:abcd'), /короткое/);
  assert.match(tooBroadToBlock('10.0.0.0/8'), /шире/);
  assert.match(tooBroadToBlock('192.168.1.1'), /локальную/);
  assert.equal(tooBroadToBlock('domain:ads.example.com'), null);
  assert.equal(tooBroadToBlock('203.0.113.0/24'), null);
});

test('the preset: rules, default, limits; the text form reads back', () => {
  assert.match(problem({ rules: [] }), /от 1 до 50/);
  assert.match(problem({ default: 'block', rules: [{ domains: ['a.com'], to: 'direct' }] }), /proxy или direct/);
  assert.match(problem({ rules: [{ domains: ['a.com'], to: 'elsewhere' }] }), /Правило 1/);
  assert.match(problem({ rules: [{ to: 'direct' }] }), /нет ни сайтов/);
  assert.match(problem({ schema: 2, rules: [{ domains: ['a.com'], to: 'direct' }] }), /schema/);
  assert.match(problem({ rules: Array.from({ length: 5 }, () => ({ domains: Array.from({ length: 500 }, (_, i) => `s${i}.example.com`), to: 'proxy' })) }), /2000/);
  assert.match(problem('just words'), /Строка 1/);

  const preset = parseRouting({ name: ' Мой ', default: 'direct', rules: [{ domains: ['ads.example.com', 'ads.example.com'], to: 'block' }, { ips: ['8.8.8.8'], to: 'proxy' }] });
  assert.deepEqual(preset, { schema: 1, name: 'Мой', default: 'direct', rules: [{ domains: ['domain:ads.example.com'], to: 'block' }, { ips: ['8.8.8.8'], to: 'proxy' }] });
  assert.equal(routingToText(preset), 'domain:ads.example.com → block\n8.8.8.8 → proxy\ndefault → direct');
  // The text form has no name; the rest comes back the same.
  assert.deepEqual(parseRouting(routingFromText(routingToText(preset))), { schema: 1, default: 'direct', rules: preset.rules });
  assert.match(routingBytes(preset).toString(), /^\{\n {2}"schema": 1,\n {2}"name": "Мой",\n {2}"default": "direct",\n {2}"rules": \[/);
});
