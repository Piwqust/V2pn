// Writes hub/test/fixtures/csharp/: a publication made by the hub with a throwaway key (Configra imported, and a
// provider "kestrel" that went through the hub with every tier-2 field), its public key, and the colours the hub's
// port derives for a set of themes. v2rayN/ServiceLib.Tests/V2pn/V2pnHubFixtureTests.cs reads it with V2pn's own code.
//
//   node hub/test/make-csharp-fixture.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { colors as C } from '../lib/colors.mjs';
import { publicKeyOf } from '../lib/keys.mjs';
import { makePng, startHub } from './helpers.mjs';

const out = fileURLToPath(new URL('./fixtures/csharp/', import.meta.url));
const configra = fileURLToPath(new URL('../../partners/configra', import.meta.url));

const env = await startHub();
try {
  const { hub, config } = env;
  hub.importPartner(configra);

  const now = new Date().toISOString();
  hub.db.run('INSERT INTO accounts (email, created_at) VALUES (?, ?)', 'boss@kestrel.example', now);
  const account = hub.db.get('SELECT id FROM accounts WHERE email = ?', 'boss@kestrel.example').id;
  hub.db.run(`INSERT INTO applications (account_id, provider_id, name, domains, test_url, contact, wanted_tier, state, created_at, updated_at)
    VALUES (?, 'kestrel', 'Kestrel', ?, 'https://sub.kestrel.example/s/x', '@kestrel', 2, 'submitted', ?, ?)`,
  account, JSON.stringify(['kestrel.example', '*.kestrel.example']), now, now);
  const app = hub.db.get('SELECT id FROM applications WHERE provider_id = ?', 'kestrel').id;
  hub.approve(app, { tier: 2, until: '2031-11-01' }, 'fixture');
  const pics = {
    logo: hub.upload(account, 'logo', makePng(340, 96, [255, 255, 255])).sha256,
    logoOnLight: hub.upload(account, 'logoOnLight', makePng(340, 96, [17, 17, 20])).sha256,
    mark: hub.upload(account, 'mark', makePng(128, 128, [47, 123, 255])).sha256,
    backdrop: hub.upload(account, 'backdrop', makePng(1600, 1000, [20, 40, 120])).sha256,
  };
  hub.saveLook(account, {
    name: 'Кестрел', brand: ['#2F7BFF', '#7C4DFF', '#FFD600'], background: 'image', ...pics,
    links: { cabinet: 'https://kestrel.example/cabinet', support: 'https://t.me/kestrel_support' },
    api: 'https://api.kestrel.example/v2pn/api/', login: true, lock: true,
    actions: [
      { label: 'Самый быстрый', icon: 'bolt', do: 'connect.best', strategy: 'stable' },
      { label: 'Для YouTube', icon: 'play', do: 'connect.recommend', purpose: 'youtube' },
      { label: 'Бот', icon: 'chat', do: 'open.link', url: 'https://t.me/kestrel_bot' },
      { label: 'Тариф', icon: 'card', do: 'info.card' },
    ],
  });
  hub.submitLook(account);
  hub.approveLook('kestrel', 'fixture');

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
}
