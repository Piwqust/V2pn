// The colour rules with the example values of V2pn's own tests (ServiceLib.Tests/V2pn/V2pnPartnerThemeTests.cs).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { colors as C } from '../lib/colors.mjs';
import { actionProblem, lookProblems, defaultLook, gradientStops, parseLook, themeOf } from '../lib/look.mjs';
import { pageTheme } from '../lib/page.mjs';

const P = C.parse;
const theme = (brand, background = { type: 'solid', colors: [], art: null }) => ({ brand, background });

test('brands near a state colour are turned away; the others are kept (Palette_TurnsBrandsAwayFromStateColours_AndKeepsOthers)', () => {
  for (const hex of ['#3DD68C', '#22B573', '#F2B84B', '#E8A21E', '#FF5A3D']) assert.equal(C.nearState(C.awayFromState(P(hex))), false, hex);
  for (const hex of ['#2F7BFF', '#FF7A2F', '#8B5CF6', '#E94F96', '#14A8A0', '#D7263D', '#7A7A7A']) assert.equal(C.hex(C.awayFromState(P(hex))), hex);
});

test('edge colours give a palette that reads in both modes (Palette_EdgeColours_PassAA)', () => {
  for (const hex of ['#000000', '#FFFFFF', '#808080', '#FFFF00', '#00FF00', '#FF0000', '#3DD68C', '#F2B84B', '#FF5A3D', '#777777', '#7F7FFF', '#FF3D1F']) {
    for (const dark of [false, true]) {
      for (const t of [theme([hex]), theme([hex], { type: 'gradient', colors: [hex, '#FFFFFF', '#000000'], art: null })]) {
        const d = C.derive(t, dark);
        for (const ground of [d.ground, d.surface, d.raised]) {
          for (const text of [d.ink, d.soft, d.muted, d.accentText]) assert.ok(C.contrast(text, ground) >= 4.5, `${hex} ${dark}`);
        }
        assert.ok(C.contrast(d.onAccent, d.accent) >= 4.5, `label on accent ${hex} ${dark}`);
        assert.ok(C.contrast(d.accent, d.ground) >= 3, `accent on ground ${hex} ${dark}`);
        assert.ok(C.contrast(d.accent, d.surface) >= 3, `accent on surface ${hex} ${dark}`);
        assert.equal(C.nearState(d.accent), false);
      }
    }
  }
});

test('a gradient stage reads everywhere in both modes (Stage_OfAGradient_ReadsEverywhere_InBothModes)', () => {
  const themes = [
    theme(['#6D5DFC'], { type: 'gradient', colors: ['#3B2FD9', '#6D28D9', '#C026D3'] }),
    theme(['#FF7A2F'], { type: 'gradient', colors: ['#FF7A2F', '#FFD9A8'] }),
    theme(['#9BE15D'], { type: 'gradient', colors: ['#9BE15D', '#00A37A'] }),
    theme(['#FFFFFF'], { type: 'gradient', colors: ['#FFFFFF', '#F4F4F4'] }),
    theme(['#808080'], { type: 'gradient', colors: ['#7A7A7A', '#8A8A8A'] }),
  ];
  for (const t of themes) {
    for (const dark of [false, true]) {
      const st = C.stage(t, dark);
      const stops = st.seen;
      assert.ok(C.minContrast(st.ink, stops) >= 4.5, 'ink');
      assert.ok(C.minContrast(st.soft, stops) >= 4.5, 'soft');
      assert.ok(C.minContrast(st.ink, stops.map(s => C.over(st.ghost[0], s, st.ghost[1]))) >= 4.5, 'ink on a ghost chip');
      assert.ok(C.contrast(st.onSolid, st.solid) >= 4.5, 'label on the solid button');
      assert.ok(!st.stops.some(C.nearState), 'a stop near a state colour');
      if (dark) {
        const l = st.stops.map(C.okL);
        assert.ok(l[0] <= 0.785 && l.slice(1).every(x => x <= 0.725), 'dark stops toned down');
      }
    }
  }
});

test('a picture stage has a scrim; a solid background has no stage (Stage_OfAPicture_HasAScrim_AndASolidBackgroundHasNone)', () => {
  const picture = theme(['#E11D48'], { type: 'image', colors: [], art: 'dunes.jpg' });
  for (const dark of [false, true]) {
    const st = C.stage(picture, dark);
    assert.equal(st.art, 'dunes.jpg');
    for (const pixel of [C.BLACK, C.WHITE, P('#808080')]) {
      assert.ok(C.contrast(st.ink, C.over(st.scrim[0], pixel, Math.round(st.scrim[1] * 255) / 255)) >= 4.5);
    }
  }
  assert.equal(C.stage(theme(['#2F7BFF']), true), null);
});

test('readableOn moves only the lightness (ReadableOn_MovesOnlyTheLightness)', () => {
  for (const [hex, ground] of [['#FF7A2F', '#F0F1F5'], ['#FFD600', '#FFFFFF'], ['#3B2FD9', '#26262F'], ['#121211', '#1E1E25']]) {
    const colour = P(hex);
    const readable = C.readableOn(colour, P(ground));
    assert.ok(C.contrast(readable, P(ground)) >= 4.5);
    if (C.oklch(colour)[0] > 0.08 && C.oklch(readable)[0] > 0.04) {
      const diff = Math.abs(C.oklch(readable)[1] - C.oklch(colour)[1]) % 360;
      assert.ok(Math.min(diff, 360 - diff) < 8);
    }
  }
  const blue = P('#2F7BFF');
  assert.equal(C.hex(C.fromOklch(C.okL(blue), C.oklch(blue)[0], C.oklch(blue)[1])), '#2F7BFF');
});

test('describe says what was changed (Describe_SaysWhatWasChanged)', () => {
  assert.ok(C.describe(theme(['#3DD68C'])).some(n => n.kind === 'moved'));
});

test("the provider page's pairs of text and ground all read for the C# examples and Configra", () => {
  for (const brand of [['#FF7A2F', '#EDAA80'], ['#2F7BFF'], ['#FFFFFF'], ['#000000'], ['#FFFF00'], ['#3DD68C']]) {
    for (const background of ['none', 'gradient']) {
      const look = { ...defaultLook('Kestrel'), brand, background };
      const page = pageTheme(themeOf(look));
      assert.deepEqual(page.failures, [], `${brand} ${background}`);
    }
  }
});

test('one brand colour still gives a two-stop gradient (a lighter, turned one is added)', () => {
  const stops = gradientStops(['#FF7A2F']);
  assert.equal(stops.length, 2);
  assert.equal(stops[0], '#FF7A2F');
  assert.notEqual(stops[1], '#FF7A2F');
});

test('a look and buttons are checked as V2pn checks them', () => {
  const pictures = new Map([['a'.repeat(64), { slot: 'logo', width: 340, height: 96, size: 1000 }], ['b'.repeat(64), { slot: 'mark', width: 300, height: 96, size: 1000 }]]);
  const base = { ...defaultLook('Kestrel'), brand: ['#2F7BFF'] };
  assert.deepEqual(lookProblems(base, { pictures }), {});
  const p = lookProblems({
    ...base, name: 'Kestrel V2pn', brand: [], logo: 'a'.repeat(64), mark: 'b'.repeat(64), background: 'image',
    links: { cabinet: 'http://kestrel.example', support: '' },
  }, { pictures });
  assert.deepEqual(Object.keys(p).sort(), ['backdrop', 'brand', 'links.cabinet', 'mark', 'name']);
  const look = parseLook({ name: 'K', background: 'map', layout: 'stickers', links: { support: 't.me/k' } });
  assert.deepEqual([look.background, look.layout, look.links.support], ['none', 'stickers', 'https://t.me/k']);
  assert.equal(parseLook({ background: 'picture' }).background, 'image');
  const domains = ['kestrel.example'];
  const button = (o) => ({ label: 'Кнопка', icon: '', strategy: '', purpose: '', server: '', group: '', url: '', ...o });
  assert.match(actionProblem(button({ do: 'open.link', url: 'https://evil.example/' }), '', domains), /на домене вашей подписки/);
  assert.equal(actionProblem(button({ do: 'open.link', url: 'https://t.me/kestrel' }), '', domains), null);
  assert.match(actionProblem(button({ do: 'info.card' }), '', domains), /API/);
  assert.equal(actionProblem(button({ do: 'info.card' }), 'https://kestrel.example/api/', domains), null);
  assert.match(actionProblem(button({ do: 'connect.best', strategy: 'fast' }), '', domains), /ping, stable или load/);
});
