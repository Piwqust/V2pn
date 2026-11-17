// V2pn's colour rules for partner looks, ported line by line from ServiceLib/V2pn/V2pnThemePalette.cs (V2pnColor,
// AwayFromState, ReadableOn, Derive, Stage, Describe), Editions.PartnerAccent and Appearance.OnAccent, so the cabinet
// shows the colours the app will draw and the notes tools/partner-check.cs will print. Colours are [r, g, b] bytes.
// Keep it in step with V2pnThemePalette.cs (it began as the former look builder's v2pn-colors.js).


const parse = hex => {
  if (typeof hex !== 'string' || !/^#[0-9A-Fa-f]{6}$/.test(hex)) return null;
  const v = parseInt(hex.slice(1), 16);
  return [(v >> 16) & 255, (v >> 8) & 255, v & 255];
};
const hex = c => '#' + c.map(v => v.toString(16).toUpperCase().padStart(2, '0')).join('');
const same = (a, b) => a[0] === b[0] && a[1] === b[1] && a[2] === b[2];
// C#'s Math.Round rounds half to even.
const roundEven = x => {
  const f = Math.floor(x), d = x - f;
  if (d === 0.5) return f % 2 === 0 ? f : f + 1;
  return Math.round(x);
};
const clamp = (x, a, b) => Math.min(b, Math.max(a, x));
const byte = x => roundEven(clamp(x, 0, 1) * 255);
const linear = v => { const x = v / 255; return x <= 0.04045 ? x / 12.92 : Math.pow((x + 0.055) / 1.055, 2.4); };
const lum = c => 0.2126 * linear(c[0]) + 0.7152 * linear(c[1]) + 0.0722 * linear(c[2]);
const contrast = (a, b) => { const x = lum(a), y = lum(b); return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05); };

function hsl(c) {
  const r = c[0] / 255, g = c[1] / 255, b = c[2] / 255;
  const mx = Math.max(r, g, b), mn = Math.min(r, g, b), l = (mx + mn) / 2;
  if (mx - mn < 1e-9) return [0, 0, l];
  const d = mx - mn, s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
  let h;
  if (mx === r) h = (g - b) / d + (g < b ? 6 : 0);
  else if (mx === g) h = (b - r) / d + 2;
  else h = (r - g) / d + 4;
  return [h * 60, s, l];
}

function fromHsl(h, s, l) {
  h = (((h % 360) + 360) % 360) / 360;
  s = clamp(s, 0, 1); l = clamp(l, 0, 1);
  if (s < 1e-9) { const g = roundEven(l * 255); return [g, g, g]; }
  const q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
  const ch = t => {
    t = t < 0 ? t + 1 : t > 1 ? t - 1 : t;
    return t < 1 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2 / 3 ? p + (q - p) * (2 / 3 - t) * 6 : p;
  };
  return [byte(ch(h + 1 / 3)), byte(ch(h)), byte(ch(h - 1 / 3))];
}

function okLab(c) {
  const r = linear(c[0]), g = linear(c[1]), b = linear(c[2]);
  const l = Math.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
  const m = Math.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
  const s = Math.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
  return [0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
    1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
    0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s];
}
const oklch = c => { const [, a, b] = okLab(c); const h = Math.atan2(b, a) * 180 / Math.PI; return [Math.hypot(a, b), h < 0 ? h + 360 : h]; };
const okL = c => okLab(c)[0];

function fromOklch(lightness, chroma, hue) {
  const l = clamp(lightness, 0, 1), h = hue * Math.PI / 180, slack = 0.0005;
  for (let c = Math.max(0, chroma); ; c *= 0.96) {
    const a = c * Math.cos(h), b = c * Math.sin(h);
    const l3 = Math.pow(l + 0.3963377774 * a + 0.2158037573 * b, 3);
    const m3 = Math.pow(l - 0.1055613458 * a - 0.0638541728 * b, 3);
    const s3 = Math.pow(l - 0.0894841775 * a - 1.2914855480 * b, 3);
    const rgb = [4.0767416621 * l3 - 3.3077115913 * m3 + 0.2309699292 * s3,
      -1.2684380046 * l3 + 2.6097574011 * m3 - 0.3413193965 * s3,
      -0.0041960863 * l3 - 0.7034186147 * m3 + 1.7076147010 * s3];
    if (c < 0.0005 || rgb.every(v => v >= -slack && v <= 1 + slack)) {
      return rgb.map(x => { x = clamp(x, 0, 1); return byte(x <= 0.0031308 ? x * 12.92 : 1.055 * Math.pow(x, 1 / 2.4) - 0.055); });
    }
  }
}

const mix = (a, b, t) => { t = clamp(t, 0, 1); return a.map((x, i) => roundEven(x + (b[i] - x) * t)); };
const over = (colour, below, alpha) => mix(below, colour, alpha);
const alphaByte = a => roundEven(clamp(a, 0, 1) * 255);
const rgba = (c, a) => `rgba(${c[0]},${c[1]},${c[2]},${(alphaByte(a) / 255).toFixed(4)})`;
const withLightness = (c, l) => { const [h, s] = hsl(c); return fromHsl(h, s, l); };

const WHITE = [255, 255, 255], BLACK = [0, 0, 0];
const STAGE_DARK_INK = parse('#111114'), ON_ACCENT_INK = parse('#0B0B0D');
const TEXT = 4.5, UI = 3.0, FALLBACK_BRAND = '#3B6CF6';
const STATES = [[parse('#3DD68C'), 20, 25], [parse('#F2B84B'), 20, 25], [parse('#FF5A3D'), 8, 12]];
const STATE_NAMES = ['protected', 'attention', 'unprotected'];

function hueWithin(c, state, degrees) {
  const [chroma, hue] = oklch(c);
  if (chroma < 0.04) return false;
  const diff = Math.abs(hue - oklch(state)[1]) % 360;
  return Math.min(diff, 360 - diff) <= degrees;
}
const nearState = c => STATES.some(s => hueWithin(c, s[0], s[1]));
/** Which state colour a brand colour could pass for, or null. */
const nearWhich = c => { const i = STATES.findIndex(s => hueWithin(c, s[0], s[1])); return i < 0 ? null : STATE_NAMES[i]; };

function awayFromState(c) {
  if (!nearState(c)) return c;
  const [h, s, l] = hsl(c);
  for (let step = 3; step <= 120; step += 3) {
    for (const sign of [-1, 1]) {
      const turned = fromHsl(h + sign * step, s, l);
      if (!STATES.some(x => hueWithin(turned, x[0], x[2]))) return turned;
    }
  }
  return c;
}

function readable(c, grounds, min = TEXT) {
  if (grounds.every(g => contrast(c, g) >= min)) return c;
  const darker = grounds.reduce((a, g) => a + lum(g), 0) / grounds.length > 0.18;
  const [h, s, l] = hsl(c);
  for (let step = 1; step <= 100; step++) {
    const cand = fromHsl(h, s, l + (darker ? -1 : 1) * step * 0.01);
    if (grounds.every(g => contrast(cand, g) >= min)) return cand;
  }
  return darker ? BLACK : WHITE;
}

function readableOn(c, ground, min = TEXT) {
  if (contrast(c, ground) >= min) return c;
  const darker = contrast(BLACK, ground) >= contrast(WHITE, ground);
  const [chroma, hue] = oklch(c), start = okL(c);
  for (let step = 1; step <= 100; step++) {
    const cand = fromOklch(start + (darker ? -1 : 1) * step * 0.01, chroma, hue);
    if (contrast(cand, ground) >= min) return cand;
  }
  return darker ? BLACK : WHITE;
}

function withLabel(fill, light, darkInk, ground) {
  const better = contrast(light, fill) >= contrast(darkInk, fill) ? light : darkInk;
  if (contrast(better, fill) >= TEXT) return [fill, better];
  const darken = lum(ground) > 0.18, label = darken ? light : darkInk;
  const [h, s, l] = hsl(fill);
  for (let step = 1; step <= 100; step++) {
    const moved = fromHsl(h, s, l + (darken ? -1 : 1) * step * 0.01);
    if (contrast(label, moved) >= TEXT) return [moved, label];
  }
  return [darken ? BLACK : WHITE, label];
}

function minContrast(text, stops) {
  let min = Infinity;
  for (let i = 0; i < stops.length; i++) {
    min = Math.min(min, contrast(text, stops[i]));
    if (i + 1 < stops.length) for (let t = 0.25; t < 1; t += 0.25) min = Math.min(min, contrast(text, mix(stops[i], stops[i + 1], t)));
  }
  return min;
}

function softer(ink, stops) {
  const middle = stops[Math.floor(stops.length / 2)];
  for (let t = 0.4; t > 0; t -= 0.02) {
    const cand = mix(ink, middle, t);
    if (minContrast(cand, stops) >= TEXT + 0.2) return cand;
  }
  return ink;
}

function scrimAlpha(scrim, texts) {
  for (let step = 77; step < 255; step++) {
    const a = step / 255;
    if (texts.every(t => contrast(t, over(scrim, BLACK, a)) >= TEXT && contrast(t, over(scrim, WHITE, a)) >= TEXT)) return a;
  }
  return 1;
}

/** theme: {brand: ['#RRGGBB'…], background: {type: 'solid'|'gradient'|'image', colors: […], art: name|null}}. */
function stage(theme, dark) {
  const bg = theme.background || { type: 'solid', colors: [] };
  if (bg.type === 'image' && bg.art) {
    const ink = dark ? WHITE : STAGE_DARK_INK, scrim = dark ? BLACK : WHITE;
    const a = scrimAlpha(scrim, [ink]);
    return stageOf('image', [scrim], bg.art, [scrim, a], ink, null);
  }
  if (bg.type === 'gradient') {
    const source = bg.colors.length >= 2 ? bg.colors : theme.brand.slice(0, 2);
    let stops = source.map(parse).filter(Boolean).map(awayFromState);
    if (!stops.length) return null;
    if (stops.length === 1) stops.push(stops[0]);
    if (dark) {
      stops = stops.map((c, i) => {
        const [chroma, hue] = oklch(c);
        return fromOklch(Math.min(okL(c) * 0.78, i === 0 ? 0.78 : 0.72), i === 0 ? chroma : chroma * 0.95, hue);
      });
    }
    const white = minContrast(WHITE, stops), black = minContrast(STAGE_DARK_INK, stops);
    const ink = white >= black ? WHITE : STAGE_DARK_INK;
    let scrim = null, seen = stops;
    if (Math.max(white, black) < TEXT) {
      const veil = ink === WHITE ? BLACK : WHITE;
      for (let step = 1; step <= 255; step++) {
        const a = step / 255, under = stops.map(s => over(veil, s, a));
        if (minContrast(ink, under) >= TEXT) { scrim = [veil, a]; seen = under; break; }
      }
    }
    return stageOf('gradient', stops, null, scrim, ink, seen);
  }
  return null;
}

function stageOf(kind, stops, art, scrim, ink, seen) {
  const light = ink === WHITE, chip = light ? 0.16 : 0.08, opposite = light ? BLACK : WHITE;
  const ghost = seen && minContrast(ink, seen.map(s => over(ink, s, chip))) >= TEXT ? [ink, chip] : [opposite, light ? 0.22 : 0.3];
  const soft = seen ? softer(ink, seen) : ink;
  return { kind, stops, art, scrim, ink, soft, ghost, solid: ink, onSolid: light ? STAGE_DARK_INK : WHITE, seen };
}

/** The part of V2pnThemePalette.Derive that Describe and the look need: the neutrals, the button fill, the brand as text. */
function derive(theme, dark) {
  let brands = theme.brand.map(parse).filter(Boolean);
  if (!brands.length) brands.push(parse(FALLBACK_BRAND));
  brands = brands.map(awayFromState);
  const brand = brands[0];
  const [hue, bs] = hsl(brand);
  const sat = (bs < 0.08 ? 0 : 0.07) * (dark ? 1 : 0.85);
  const N = (l, f = 1) => fromHsl(hue, sat * f, l);
  let ground, surface, raised, ink, soft, muted;
  if (dark) { ground = N(0.075); surface = N(0.115); raised = N(0.16); ink = N(0.95, 0.35); soft = N(0.80, 0.4); muted = N(0.66, 0.4); }
  else { ground = N(0.95); surface = N(0.995); raised = N(0.915); ink = N(0.10, 0.5); soft = N(0.30, 0.5); muted = N(0.42, 0.5); }
  const neutrals = [ground, surface, raised];
  ink = readable(ink, neutrals); soft = readable(soft, neutrals); muted = readable(muted, neutrals);
  let fill = brand, onFill = ink;
  for (let round = 0; round < 6; round++) {
    let moved = readable(fill, [ground, surface], UI);
    [moved, onFill] = withLabel(moved, dark ? ink : surface, dark ? ground : ink, ground);
    const away = awayFromState(moved);
    fill = away;
    if (same(away, moved)) break;
  }
  return { accent: fill, onAccent: onFill, accentText: readable(brand, neutrals), ground, surface, raised, ink, soft, muted };
}

/** What partner-check prints about the colours (V2pnThemePalette.Describe). */
function describe(theme) {
  const notes = [];
  for (const h of theme.brand.concat(theme.background.colors || [])) {
    const c = parse(h);
    if (c && !same(awayFromState(c), c)) notes.push({ kind: 'moved', from: hex(c), to: hex(awayFromState(c)), state: nearWhich(c) });
  }
  for (const dark of [false, true]) {
    const d = derive(theme, dark);
    const brand = awayFromState(parse(theme.brand[0]) || parse(FALLBACK_BRAND));
    if (!same(d.accent, brand)) notes.push({ kind: 'button', mode: dark ? 'dark' : 'light', from: hex(brand), to: hex(d.accent) });
    if (!same(d.accentText, brand)) notes.push({ kind: 'text', mode: dark ? 'dark' : 'light', to: hex(d.accentText) });
  }
  return notes;
}

/** The colour Connect wears (Editions.PartnerAccent) and its label (Appearance.OnAccent). */
function partnerAccent(theme) {
  const accent = awayFromState(parse(theme.brand[0]) || parse(FALLBACK_BRAND));
  return { accent, onAccent: contrast(ON_ACCENT_INK, accent) >= contrast(WHITE, accent) ? ON_ACCENT_INK : WHITE };
}

// ------------------------------------------------------------------ picture slots (V2pnPartnerSlots)

const SLOTS = {
  logo: { desc: ['широкий логотип, от 2:1 до 8:1, высотой 32–256 px', 'a wide logo, 2:1 to 8:1, 32-256 px tall'],
    fits: (w, h) => h >= 32 && h <= 256 && w >= h * 2 && w <= h * 8 },
  mark: { desc: ['квадратный знак, 64–512 px', 'a square mark, 64-512 px'],
    fits: (w, h) => w >= 64 && w <= 512 && Math.abs(w - h) <= Math.trunc(w / 20) },
  backdrop: { desc: ['фоновая картинка, 640–3200 px по стороне, не больше 3:1', 'a background picture, 640-3200 px a side, at most 3:1'],
    fits: (w, h) => w >= 640 && w <= 3200 && h >= 640 && h <= 3200 && w <= h * 3 && h <= w * 3 }
};
const MAX_ASSET = 1024 * 1024;

/** Width and height from a PNG, JPEG or WebP header (V2pnPartnerSlots.ImageSize); null for anything else. */
function imageSize(b) {
  const be = at => ((b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3]) >>> 0;
  try {
    if (b.length >= 24 && b[0] === 0x89 && b[1] === 0x50 && b[2] === 0x4E && b[3] === 0x47) return [be(16), be(20)];
    if (b.length >= 4 && b[0] === 0xFF && b[1] === 0xD8) {
      let i = 2;
      while (i + 9 < b.length) {
        if (b[i] !== 0xFF) { i++; continue; }
        const marker = b[i + 1], length = (b[i + 2] << 8) | b[i + 3];
        if (marker >= 0xC0 && marker <= 0xCF && marker !== 0xC4 && marker !== 0xC8 && marker !== 0xCC) {
          return [(b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]];
        }
        i += 2 + length;
      }
      return null;
    }
    const tag = (at, s) => [...s].every((ch, k) => b[at + k] === ch.charCodeAt(0));
    if (b.length >= 30 && tag(0, 'RIFF') && tag(8, 'WEBP')) {
      if (tag(12, 'VP8 ')) return [((b[27] << 8) | b[26]) & 0x3FFF, ((b[29] << 8) | b[28]) & 0x3FFF];
      if (tag(12, 'VP8L')) return [1 + (((b[22] & 0x3F) << 8) | b[21]), 1 + (((b[24] & 0x0F) << 10) | (b[23] << 2) | ((b[22] & 0xC0) >> 6))];
      if (tag(12, 'VP8X')) return [1 + (b[24] | (b[25] << 8) | (b[26] << 16)), 1 + (b[27] | (b[28] << 8) | (b[29] << 16))];
    }
  } catch (e) { /* a short file: not a picture */ }
  return null;
}

/** {problem: 'none'|'tooLarge'|'notPicture'|'wrongSize', width, height} as V2pnPartnerSlots.Check answers. */
function checkSlot(slot, bytes) {
  const size = imageSize(bytes);
  const [w, h] = size || [0, 0];
  if (bytes.length > MAX_ASSET) return { problem: 'tooLarge', width: w, height: h };
  if (!size || w <= 0 || h <= 0) return { problem: 'notPicture', width: 0, height: 0 };
  return { problem: SLOTS[slot].fits(w, h) ? 'none' : 'wrongSize', width: w, height: h };
}

export const colors = { parse, hex, contrast, lum, hsl, fromHsl, oklch, okL, fromOklch, mix, over, rgba, withLightness, awayFromState,
  nearState, nearWhich, readable, readableOn, withLabel, minContrast, scrimAlpha, stage, derive, describe, partnerAccent,
  imageSize, checkSlot, SLOTS, MAX_ASSET, WHITE, BLACK, STAGE_DARK_INK, TEXT, FALLBACK_BRAND };
