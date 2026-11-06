// Generates the V2pn map and flag assets from open data:
//  - world-atlas (Natural Earth, public domain) → per-country Mercator path data
//  - i18n-iso-countries → country names in Russian and English (other UI languages: names.py, CLDR)
//  - flag-icons (MIT) → small PNG flags (Windows does not render flag emoji)
// Run: node gen.mjs   (outputs into v2rayN.Desktop/Assets)
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { geoMercator, geoPath, geoArea, geoCentroid } from 'd3-geo';
import { feature } from 'topojson-client';
import countries from 'i18n-iso-countries';
import sharp from 'sharp';

const require = createRequire(import.meta.url);
countries.registerLocale(require('i18n-iso-countries/langs/ru.json'));
countries.registerLocale(require('i18n-iso-countries/langs/en.json'));

const out = path.resolve('../../v2rayN/v2rayN.Desktop/Assets');
const WIDTH = 2400;

const topo = require('world-atlas/countries-50m.json');
const fc = feature(topo, topo.objects.countries);
// Antarctica only eats vertical space on a Mercator map.
fc.features = fc.features.filter((f) => f.id !== '010');

const projection = geoMercator().scale(WIDTH / (2 * Math.PI)).translate([WIDTH / 2, 0]);
// Crop latitudes so the canvas starts at the arctic and ends below South America.
const [, top] = projection([0, 83.5]);
const [, bottom] = projection([0, -57]);
projection.translate([WIDTH / 2, -top]);
const HEIGHT = Math.round(bottom - top);
const toPath = geoPath(projection);

// Round to whole pixels and drop repeated points: keeps the path data small enough to parse fast.
function compact(d) {
  if (!d) return '';
  return d
    .replace(/(-?\d+\.\d+)/g, (m) => String(Math.round(parseFloat(m))))
    .replace(/L(-?\d+),(-?\d+)(?=L\1,\2)/g, '');
}

const result = {};
let total = 0;
for (const f of fc.features) {
  const alpha2 = countries.numericToAlpha2(f.id) || null;
  if (!alpha2) continue;
  // The largest polygon decides the pin, so overseas territories don't drag it into the ocean.
  let main = f;
  if (f.geometry.type === 'MultiPolygon') {
    let best = -1;
    for (const poly of f.geometry.coordinates) {
      const g = { type: 'Feature', geometry: { type: 'Polygon', coordinates: poly } };
      const a = geoArea(g);
      if (a > best) { best = a; main = g; }
    }
  }
  const [cx, cy] = projection(geoCentroid(main));
  const [[x0, y0], [x1, y1]] = toPath.bounds(main);
  const d = compact(toPath(f));
  total += d.length;
  result[alpha2] = {
    ru: countries.getName(alpha2, 'ru') || alpha2,
    en: countries.getName(alpha2, 'en') || alpha2,
    x: Math.round(cx), y: Math.round(cy),
    b: [Math.round(x0), Math.round(y0), Math.round(x1), Math.round(y1)],
    d,
  };
}

fs.mkdirSync(path.join(out, 'Map'), { recursive: true });
fs.writeFileSync(path.join(out, 'Map', 'world.json'), JSON.stringify({ width: WIDTH, height: HEIGHT, countries: result }));
console.log(`map: ${Object.keys(result).length} countries, ${WIDTH}x${HEIGHT}, path chars ${total}`);

const flagsIn = path.resolve('node_modules/flag-icons/flags/4x3');
const flagsOut = path.join(out, 'Flags');
fs.mkdirSync(flagsOut, { recursive: true });
let n = 0;
for (const file of fs.readdirSync(flagsIn)) {
  const code = path.basename(file, '.svg');
  if (code.length !== 2) continue;
  await sharp(path.join(flagsIn, file), { density: 300 }).resize(48, 36).png({ compressionLevel: 9 }).toFile(path.join(flagsOut, `${code}.png`));
  n++;
}
console.log(`flags: ${n}`);
