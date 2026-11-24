// Writes hub/test/fixtures/csharp/: a publication made by the hub with a throwaway key (Configra imported, and a
// partner "kestrel" saved on the hub with its special rights approved), its public key, and the colours the hub's
// port derives for a set of themes. v2rayN/ServiceLib.Tests/V2pn/V2pnHubFixtureTests.cs reads it with V2pn's own code.
//
//   node hub/test/make-csharp-fixture.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { colors as C } from '../lib/colors.mjs';
import { publicKeyOf } from '../lib/keys.mjs';
import { makePng, serve, startHub, testNet } from './helpers.mjs';

const out = fileURLToPath(new URL('./fixtures/csharp/', import.meta.url));
const configra = fileURLToPath(new URL('../../partners/configra', import.meta.url));

// A stand-in subscription for kestrel.example on this computer, so the hub's check passes as on the server.
const servers = await serve((req, res) => res.end());
const sub = await serve((req, res) => res.end(Buffer.from(`vless://id@s1.kestrel.example:${servers.port}?security=reality#S1`).toString('base64')));
const env = await startHub({
  net: { ...testNet, tcp: false, resolve: async host => (host.endsWith('kestrel.example') ? [{ address: '127.0.0.1', family: 4 }] : testNet.resolve(host)) },
});
try {
  const { hub, config } = env;
  hub.importPartner(configra);

  // Kestrel: a provider that went the whole way on the hub, every special right approved, routing included.
  const { account } = await hub.auth.register('kestrel', 'fixture-password', '127.0.0.1');
  const pics = {
    logo: hub.upload(account.id, 'logo', makePng(340, 96, [255, 255, 255])).sha256,
    logoOnLight: hub.upload(account.id, 'logoOnLight', makePng(340, 96, [17, 17, 20])).sha256,
    mark: hub.upload(account.id, 'mark', makePng(128, 128, [47, 123, 255])).sha256,
    backdrop: hub.upload(account.id, 'backdrop', makePng(1600, 1000, [20, 40, 120])).sha256,
  };
  await hub.savePartner(account.id, {
    name: 'Кестрел', subscription: `http://kestrel.example:${sub.port}/s/x`, brand: ['#2F7BFF', '#7C4DFF', '#FFD600'], background: 'image',
    layout: 'stickers', ...pics, links: { cabinet: 'https://kestrel.example/cabinet', support: 'https://t.me/kestrel_support' },
    rights: {
      account: { api: 'https://kestrel.example/v2pn/api/', login: true },
      lock: true,
      routing: 'domain:ru, geoip:ru → direct\ndomain:youtube.com → proxy\ndefault → direct',
      actions: [
        { label: 'Самый быстрый', icon: 'bolt', do: 'connect.best', strategy: 'stable' },
        { label: 'Для YouTube', icon: 'play', do: 'connect.recommend', purpose: 'youtube' },
        { label: 'Бот', icon: 'chat', do: 'open.link', url: 'https://t.me/kestrel_bot' },
        { label: 'Тариф', icon: 'card', do: 'info.card' },
      ],
    },
  }, 'fixture');
  const id = hub.ownPartner(account.id).id;
  for (const right of hub.rightsQueue()) hub.decideRight(id, right.right, true, { hash: right.hash }, 'fixture');

  fs.rmSync(out, { recursive: true, force: true });
  fs.mkdirSync(out, { recursive: true });
  const dir = path.join(config.publicDir, 'partners', 'latest', 'download');
  for (const f of fs.readdirSync(dir)) fs.copyFileSync(path.join(dir, f), path.join(out, f));
  fs.writeFileSync(path.join(out, 'hub-public-key.txt'), publicKeyOf(hub.key) + '\n');

  // The colours V2pn derives, as the hub's port computes them, for V2pn to compare with its own.
  const themes = [
    { brand: ['#FF7A2F', '#EDAA80'], background: { type: 'gradient', colors: ['#FF7A2F', '#EDAA80'] } },
    { brand: ['#6D5DFC'], background: { type: 'gradient', colors: ['#3B2FD9', '#6D28D9', '#C026D3'] } },
    { brand: ['#9BE15D'], background: { type: 'gradient', colors: ['#9BE15D', '#00A37A'] } },
    { brand: ['#FFFFFF'], background: { type: 'gradient', colors: ['#FFFFFF', '#F4F4F4'] } },
    { brand: ['#E11D48'], background: { type: 'image', colors: [], art: 'dunes.png' } },
    ...['#000000', '#808080', '#FFFF00', '#00FF00', '#3DD68C', '#F2B84B', '#FF5A3D', '#7F7FFF', '#2F7BFF'].map(h => ({ brand: [h], background: { type: 'solid', colors: [] } })),
  ];
  const colours = [];
  for (const theme of themes) {
    for (const dark of [false, true]) {
      const d = C.derive(theme, dark);
      const st = C.stage(theme, dark);
      colours.push({
        brand: theme.brand, background: theme.background, dark,
        accent: C.hex(d.accent), onAccent: C.hex(d.onAccent), accentText: C.hex(d.accentText),
        ground: C.hex(d.ground), surface: C.hex(d.surface), ink: C.hex(d.ink),
        stage: st ? { stops: st.stops.map(C.hex), ink: C.hex(st.ink), soft: C.hex(st.soft) } : null,
      });
    }
  }
  fs.writeFileSync(path.join(out, 'colours.json'), JSON.stringify(colours, null, 2) + '\n');
  process.stdout.write(`wrote ${fs.readdirSync(out).sort().join(', ')} into ${out}\n`);
} finally {
  await env.close();
  await sub.close();
  await servers.close();
}
