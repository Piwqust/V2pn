// Ribbed glass in Canvas 2D: the brand book's renderer (docs/brand/v2pn-brand-book.html, chapter 05), cut down to
// what the site needs. A band of a source canvas is softened, then each vertical rib shows a slice of it wider
// than itself; a highlight on the left of every rib, a shade on its right and a 1 px glint between ribs.

export type GlassOptions = {
  ribs: number; // ribs across the band (36 to 64 across a full frame)
  refr: number; // 0..1; each rib shows a slice 1 + refr × 1.6 times its width
  soft: number; // softness before the glass, as a fraction of the band height (0.04 to 0.08)
  grain: number; // overlay noise, 0..0.1
};

const pool = new Map<string, HTMLCanvasElement>();
function scratch(key: string, w: number, h: number) {
  let c = pool.get(key);
  if (!c) { c = document.createElement('canvas'); pool.set(key, c); }
  if (c.width !== w) c.width = w;
  if (c.height !== h) c.height = h;
  return c;
}

let strip: HTMLCanvasElement | null = null;
function ribStrip() {
  if (strip) return strip;
  strip = document.createElement('canvas');
  strip.width = 96;
  strip.height = 1;
  const x = strip.getContext('2d')!;
  const g = x.createLinearGradient(0, 0, 96, 0);
  g.addColorStop(0, 'rgba(255,255,255,.16)');
  g.addColorStop(0.1, 'rgba(255,255,255,.05)');
  g.addColorStop(0.5, 'rgba(0,0,0,0)');
  g.addColorStop(0.86, 'rgba(0,0,0,.2)');
  g.addColorStop(1, 'rgba(0,0,0,.42)');
  x.fillStyle = g;
  x.fillRect(0, 0, 96, 1);
  return strip;
}

let noisePattern: CanvasPattern | null = null;
function noise(ctx: CanvasRenderingContext2D) {
  if (noisePattern) return noisePattern;
  const c = document.createElement('canvas');
  c.width = c.height = 160;
  const x = c.getContext('2d')!;
  const d = x.createImageData(160, 160);
  let s = 7;
  for (let i = 0; i < d.data.length; i += 4) {
    s = (s * 16807) % 2147483647;
    const v = s % 255;
    d.data[i] = d.data[i + 1] = d.data[i + 2] = v;
    d.data[i + 3] = 255;
  }
  x.putImageData(d, 0, 0);
  noisePattern = ctx.createPattern(c, 'repeat');
  return noisePattern;
}

/** Draws the band [x0, x1) of `src` onto `dst` (both the same size, device pixels) through ribbed glass. */
export function ribbed(dst: CanvasRenderingContext2D, src: HTMLCanvasElement, x0: number, x1: number, o: GlassOptions) {
  const H = src.height;
  const bw = Math.max(2, Math.round(x1 - x0));
  if (bw < 4 || H < 4) return;
  // Softness: down to a small canvas and back up through a middle size, so the blur stays smooth.
  const div = Math.max(2, (o.soft * H) / 2.2);
  const sw = Math.max(6, Math.round(bw / div));
  const sh = Math.max(6, Math.round(H / div));
  const small = scratch('small', sw, sh);
  const sx = small.getContext('2d')!;
  sx.imageSmoothingEnabled = true;
  sx.imageSmoothingQuality = 'high';
  sx.clearRect(0, 0, sw, sh);
  sx.drawImage(src, x0, 0, bw, H, 0, 0, sw, sh);
  const mid = scratch('mid', Math.max(1, Math.round(bw / 3)), Math.max(1, Math.round(H / 3)));
  const mx = mid.getContext('2d')!;
  mx.imageSmoothingEnabled = true;
  mx.imageSmoothingQuality = 'high';
  mx.clearRect(0, 0, mid.width, mid.height);
  mx.drawImage(small, 0, 0, mid.width, mid.height);
  const full = scratch('full', bw, H);
  const fx = full.getContext('2d')!;
  fx.imageSmoothingEnabled = true;
  fx.clearRect(0, 0, bw, H);
  fx.drawImage(mid, 0, 0, bw, H);

  const n = Math.max(1, Math.round(o.ribs));
  const rw = bw / n;
  const sl = Math.min(bw, rw * (1 + o.refr * 1.6));
  for (let i = 0; i < n; i++) {
    const c = i * rw + rw / 2;
    const s0 = Math.max(0, Math.min(bw - sl, c - sl / 2 + Math.sin(i * 1.37) * rw * 0.1));
    dst.drawImage(full, s0, 0, sl, H, Math.floor(x0 + i * rw), 0, Math.ceil(rw) + 1, H);
  }
  const st = ribStrip();
  for (let i = 0; i < n; i++) dst.drawImage(st, Math.floor(x0 + i * rw), 0, Math.ceil(rw) + 1, H);
  dst.fillStyle = 'rgba(255,255,255,.10)';
  for (let i = 1; i < n; i++) dst.fillRect(Math.floor(x0 + i * rw), 0, 1, H);
  if (o.grain > 0) {
    dst.save();
    dst.globalAlpha = o.grain;
    dst.globalCompositeOperation = 'overlay';
    dst.fillStyle = noise(dst)!;
    dst.fillRect(x0, 0, bw, H);
    dst.restore();
  }
}
