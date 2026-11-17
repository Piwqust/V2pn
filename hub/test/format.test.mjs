import assert from 'node:assert/strict';
import fs from 'node:fs';
import { test } from 'node:test';
import { dotnetJson } from '../lib/jsonfmt.mjs';
import { makeZip, readZip } from '../lib/zip.mjs';
import { indexBytes } from '../lib/publish.mjs';
import { makePng } from './helpers.mjs';

const configra = new URL('../../partners/configra/', import.meta.url);

test('JSON is written as Utf8JsonWriter writes it: two spaces, relaxed escaping, a newline at the end', () => {
  const text = 'Кестрел \u0001 " \\ \n \t <>&\' ' + String.fromCodePoint(0x1f600) + ' \u2028 \u00ad é \ufffd \u007f \ufeff \ue000';
  const bytes = dotnetJson({ a: text, n: 12345678901, e: [], o: {}, b: true }, '\r\n');
  // What .NET 10 wrote for the same object on Windows (see the comment in jsonfmt.mjs).
  const expected = '{\r\n  "a": "Кестрел \\u0001 \\" \\\\ \\n \\t <>&\' \\uD83D\\uDE00 \\u2028 \u00ad é \ufffd \\u007F \\uFEFF \\uE000",\r\n'
    + '  "n": 12345678901,\r\n  "e": [],\r\n  "o": {},\r\n  "b": true\r\n}\n';
  assert.equal(bytes.toString('utf8'), expected);
});

test("Configra's partner.json comes out the same when the hub writes it again", () => {
  const original = fs.readFileSync(new URL('partner.json', configra), 'utf8');
  const again = dotnetJson(JSON.parse(original)).toString('utf8');
  assert.equal(again, original.replace(/\r\n/g, '\n'));
});

test('a zip has its entries by name, one date, and reads back unchanged', () => {
  const files = [
    { name: 'partner.json', content: Buffer.from('{"schema":1}\n') },
    { name: 'mark.png', content: makePng(128, 128) },
    { name: 'logo-light.png', content: makePng(340, 96, [1, 2, 3]) },
  ];
  const zip = makeZip(files);
  assert.deepEqual(makeZip([...files].reverse()), zip, 'the same files always give the same bytes');
  const entries = readZip(zip);
  assert.deepEqual([...entries.keys()], ['logo-light.png', 'mark.png', 'partner.json']);
  for (const f of files) assert.deepEqual(entries.get(f.name), f.content);
  // Local header: version 2.0, no flags, deflate, 2020-01-01 00:00.
  assert.equal(zip.readUInt16LE(4), 20);
  assert.equal(zip.readUInt16LE(6), 0);
  assert.equal(zip.readUInt16LE(8), 8);
  assert.equal(zip.readUInt16LE(10), 0);
  assert.equal(zip.readUInt16LE(12), (40 << 9) | (1 << 5) | 1);
});

test('the index has the layout of V2pnPartnerPublisher.IndexBytes, partners by id', () => {
  const bytes = indexBytes([
    { id: 'zeta', name: 'Zeta', domains: ['zeta.example'], grants: ['edition'], until: '2027-01-01', pack: { file: 'partner-zeta.zip', size: 10, sha256: 'a'.repeat(64) } },
    { id: 'alpha', name: 'Альфа', domains: ['alpha.example', '*.alpha.example'], grants: ['edition', 'account'], until: '2027-11-16', pack: { file: 'partner-alpha.zip', size: 123, sha256: 'b'.repeat(64) } },
  ], '2026-11-17T08:00:00Z');
  assert.equal(bytes.toString('utf8'), `{
  "schema": 1,
  "published": "2026-11-17T08:00:00Z",
  "partners": [
    {
      "id": "alpha",
      "name": "Альфа",
      "domains": [
        "alpha.example",
        "*.alpha.example"
      ],
      "grants": [
        "edition",
        "account"
      ],
      "until": "2027-11-16",
      "pack": {
        "file": "partner-alpha.zip",
        "size": 123,
        "sha256": "${'b'.repeat(64)}"
      }
    },
    {
      "id": "zeta",
      "name": "Zeta",
      "domains": [
        "zeta.example"
      ],
      "grants": [
        "edition"
      ],
      "until": "2027-01-01",
      "pack": {
        "file": "partner-zeta.zip",
        "size": 10,
        "sha256": "${'a'.repeat(64)}"
      }
    }
  ]
}
`);
  assert.equal(indexBytes([], '2026-11-17T08:00:00Z').toString(), '{\n  "schema": 1,\n  "published": "2026-11-17T08:00:00Z",\n  "partners": []\n}\n');
});
