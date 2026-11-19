// «Развилка», the home page's signature (DESIGN.md). V2pn's mark drawn as two roads seen from above: site names
// climb the stem and take their road by the selected route preset. The left road goes direct, in plain sight; the
// right one runs under ribbed glass, where names become light. One driver, on the GSAP ticker:
//   input   time (the flow), the preset (the visitor's choice), the pointer (the glass leans a little)
//   output  the canvas frame, the two road labels, the preset caption
// Paused off screen. Under reduced motion it draws one still frame and redraws it when the preset changes.
import { gsap } from 'gsap';
import { ScrollTrigger } from 'gsap/ScrollTrigger';
import { onPage, prefersReducedMotion, isTouch } from '../engine';
import { direct, presets, traffic, type PresetId } from '../data/routes';
import { ribbed } from './glass';

gsap.registerPlugin(ScrollTrigger);

const MARK = [0, 0, 32, 0, 32, 40, 72, 0, 104, 0, 104, 32, 85.255, 32, 64, 53.255, 64, 112, 32, 112, 32, 72, 0, 40];
const SIGNAL = '#FF3D1F';
const TUNNEL = '#0B0B0D';
const INK = '#ECECF1';
const SPEED = 10; // units of the mark per second
const EVERY = 1.15; // seconds between two names
const LANE_L = 44; // the stem's lane of names going direct
const LANE_R = 52; // the stem's lane of names going through a server
const JUNCTION_L = 68; // where the left road leaves the stem (y, units)
const JUNCTION_R = 42.6; // where the right road leaves it: the centre line x + y = 94.6
const DIAG_L = 28 * Math.SQRT2; // the left road's diagonal, from (44, 68) to (16, 40)

type Packet = { name: string; kind: (typeof traffic)[number]['kind']; s: number; lane: number; left: boolean; committed: boolean; w: number };

export function fork() {
  return onPage(() => {
    const host = document.querySelector<HTMLElement>('[data-fork]');
    const cv = host?.querySelector<HTMLCanvasElement>('canvas');
    if (!host || !cv) return;
    const ctx = cv.getContext('2d');
    if (!ctx) return;
    const reduced = prefersReducedMotion();
    const labels = {
      direct: host.querySelector<HTMLElement>('[data-fork-label="direct"]'),
      server: host.querySelector<HTMLElement>('[data-fork-label="server"]'),
    };
    const group = document.querySelector<HTMLElement>('[data-presets]');
    const hint = document.querySelector<HTMLElement>('[data-preset-hint]');
    let preset: PresetId = (host.dataset.preset as PresetId) || 'ru';

    const scene = document.createElement('canvas');
    const sx = scene.getContext('2d')!;
    let W = 0, H = 0, dpr = 1, k = 1, ox = 0, oy = 0, yStart = 140, font = 12;
    let packets: Packet[] = [];
    let next = 0;
    let since = 0;
    let time = 0;
    let lean = 0, leanAim = 0;

    // ------------------------------------------------------------ geometry
    function layout() {
      const r = host!.getBoundingClientRect();
      const mobile = r.width < 560;
      dpr = Math.min(devicePixelRatio || 1, mobile ? 1.5 : 2);
      W = Math.round(r.width * dpr);
      H = Math.round(r.height * dpr);
      cv!.width = scene.width = W;
      cv!.height = scene.height = H;
      // The branches (the top 72 units) always fit; the stem runs on past the bottom edge.
      // The host starts at the top of the page on wide screens (under the header) and below it on phones; the road
      // names need room above the mark either way.
      const top = (mobile ? 46 : (parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--header-h')) || 4.5) * 16 + 64) * dpr;
      k = Math.min((W * (mobile ? 0.92 : 0.86)) / 104, ((H - top) * 0.92) / 74);
      ox = (W - 104 * k) / 2;
      oy = top;
      yStart = (H - oy) / k + 4;
      font = Math.round((mobile ? 11 : 12.5) * dpr);
      sx.font = `500 ${font}px "JetBrains Mono", ui-monospace, monospace`;
      packets.forEach((p) => { p.w = sx.measureText(p.name).width; });
      place();
    }
    const X = (u: number) => ox + u * k;
    const Y = (u: number) => oy + u * k;
    const glassX = () => X(66) + lean * dpr;

    function place() {
      // The two road names, in CSS pixels over the canvas: «Напрямую» above the left road, «Через сервер» on the
      // glass at the end of the right road. Text stays sharp on top of the glass (brand book, chapter 05).
      if (labels.direct) labels.direct.style.translate = `${X(16) / dpr}px ${Y(0) / dpr}px`;
      if (labels.server) labels.server.style.translate = `${X(92) / dpr}px ${Y(0) / dpr}px`;
    }

    /** A packet's point in units: up the stem, then its road. */
    function at(p: Packet): [number, number] {
      const y = yStart - p.s;
      if (p.left) {
        if (y > JUNCTION_L) return [p.lane, y];
        const d = JUNCTION_L - y; // distance travelled past the junction, along the stem's measure
        if (d < DIAG_L) return [LANE_L - d / Math.SQRT2, JUNCTION_L - d / Math.SQRT2];
        return [16, 40 - (d - DIAG_L)];
      }
      if (y > JUNCTION_R) return [p.lane, y];
      const d = JUNCTION_R - y;
      return [LANE_R + d / Math.SQRT2, JUNCTION_R - d / Math.SQRT2];
    }

    // ------------------------------------------------------------ the flow
    function spawn() {
      const t = traffic[next++ % traffic.length];
      const left = direct(t.kind, preset);
      packets.push({ name: t.name, kind: t.kind, s: 0, lane: left ? LANE_L : LANE_R, left, committed: false, w: sx.measureText(t.name).width });
    }

    function step(dt: number) {
      time += dt;
      since += dt;
      if (since >= EVERY) { since -= EVERY; spawn(); }
      for (const p of packets) {
        p.s += SPEED * dt;
        const y = yStart - p.s;
        if (!p.committed) {
          p.left = direct(p.kind, preset);
          const aim = p.left ? LANE_L : LANE_R;
          p.lane += (aim - p.lane) * Math.min(1, dt * 4);
          if (y <= (p.left ? JUNCTION_L : JUNCTION_R)) { p.committed = true; p.lane = aim; }
        }
      }
      packets = packets.filter((p) => {
        const [x, y] = at(p);
        return y > -14 && X(x) < W + 200 * dpr;
      });
    }

    function reroute(id: PresetId) {
      preset = id;
      host!.dataset.preset = id;
      if (reduced) { warm(); draw(); }
    }

    /** Fill the roads before the first frame: the first frame is the best one, not an empty road. */
    function warm() {
      packets = [];
      next = 0;
      since = 0;
      for (let i = 0; i < 260; i++) step(0.1);
    }

    // ------------------------------------------------------------ drawing
    function mark(c: CanvasRenderingContext2D) {
      c.beginPath();
      for (let i = 0; i < MARK.length; i += 2) {
        const px = X(MARK[i]), py = Y(MARK[i + 1]);
        if (i) c.lineTo(px, py); else c.moveTo(px, py);
      }
      c.closePath();
      c.fillStyle = SIGNAL;
      c.fill();
      c.fillRect(X(32), Y(111), 32 * k, H); // the stem runs on to the visitor, below the edge
    }

    function markings(c: CanvasRenderingContext2D) {
      // Road markings along both lanes; they flow upward with the traffic.
      c.save();
      c.strokeStyle = 'rgba(11,11,13,.3)';
      c.lineWidth = Math.max(1, 1.6 * dpr);
      c.setLineDash([9 * dpr, 13 * dpr]);
      c.lineDashOffset = reduced ? 0 : (time * SPEED * k) % (22 * dpr);
      c.beginPath();
      c.moveTo(X(LANE_L), H + 20); c.lineTo(X(LANE_L), Y(JUNCTION_L)); c.lineTo(X(16), Y(40)); c.lineTo(X(16), Y(-2));
      c.moveTo(X(LANE_R), H + 20); c.lineTo(X(LANE_R), Y(JUNCTION_R)); c.lineTo(X(98), Y(-3));
      c.stroke();
      c.restore();
    }

    function chips(c: CanvasRenderingContext2D) {
      const gx = glassX();
      const h = Math.round(font * 1.75);
      const pad = Math.round(font * 0.6);
      c.textBaseline = 'middle';
      c.font = `500 ${font}px "JetBrains Mono", ui-monospace, monospace`;
      for (const p of packets) {
        const [u, v] = at(p);
        const px = X(u), py = Y(v);
        // Through the server, the name turns into light as it reaches the glass.
        const light = p.left ? 0 : Math.max(0, Math.min(1, (px - (gx - 40 * dpr)) / (60 * dpr)));
        if (light > 0) {
          const r = 22 * dpr;
          c.save();
          c.globalAlpha = light;
          const tail = c.createLinearGradient(px, py, px - 70 * dpr, py + 70 * dpr);
          tail.addColorStop(0, 'rgba(255,214,170,.9)');
          tail.addColorStop(1, 'rgba(255,138,42,0)');
          c.strokeStyle = tail;
          c.lineWidth = 7 * dpr;
          c.lineCap = 'round';
          c.beginPath(); c.moveTo(px, py); c.lineTo(px - 70 * dpr, py + 70 * dpr); c.stroke();
          const g = c.createRadialGradient(px, py, 0, px, py, r);
          g.addColorStop(0, 'rgba(255,255,255,1)');
          g.addColorStop(0.35, 'rgba(255,190,120,.85)');
          g.addColorStop(1, 'rgba(255,138,42,0)');
          c.fillStyle = g;
          c.fillRect(px - r, py - r, r * 2, r * 2);
          c.restore();
        }
        if (light >= 1) continue;
        const w = Math.round(p.w + pad * 2 + h * 0.5);
        const x0 = Math.round(px - w / 2), y0 = Math.round(py - h / 2);
        c.globalAlpha = 1 - light;
        c.fillStyle = TUNNEL;
        c.fillRect(x0, y0, w, h);
        // A square at the start: Signal while the name climbs, then the colour of its road.
        c.fillStyle = !p.committed ? '#8F8D94' : p.left ? INK : SIGNAL;
        const sq = Math.round(h * 0.24);
        c.fillRect(x0 + pad, Math.round(py - sq / 2), sq, sq);
        c.fillStyle = INK;
        c.fillText(p.name, x0 + pad + sq + Math.round(pad * 0.7), py + 0.5 * dpr);
        c.globalAlpha = 1;
      }
    }

    function draw() {
      sx.fillStyle = TUNNEL;
      sx.fillRect(0, 0, W, H);
      mark(sx);
      markings(sx);
      chips(sx);
      ctx!.clearRect(0, 0, W, H);
      ctx!.drawImage(scene, 0, 0);
      const gx = Math.round(glassX());
      const band = W - gx;
      ribbed(ctx!, scene, gx, W, { ribs: Math.max(6, Math.round(band / (19 * dpr))), refr: 0.5, soft: 0.022, grain: 0.05 });
      // The pane's edge: a glint and a shade, so the glass reads as an object over the road.
      ctx!.fillStyle = 'rgba(255,255,255,.35)';
      ctx!.fillRect(gx, 0, Math.max(1, dpr), H);
      ctx!.fillStyle = 'rgba(0,0,0,.35)';
      ctx!.fillRect(gx - 6 * dpr, 0, 6 * dpr, H);
    }

    // ------------------------------------------------------------ the preset switch
    const buttons = group ? [...group.querySelectorAll<HTMLButtonElement>('[data-preset-id]')] : [];
    function choose(btn: HTMLButtonElement, focus = false) {
      const id = btn.dataset.presetId as PresetId;
      buttons.forEach((b) => {
        const on = b === btn;
        b.setAttribute('aria-checked', String(on));
        b.tabIndex = on ? 0 : -1;
      });
      if (focus) btn.focus();
      const p = presets.find((x) => x.id === id);
      if (hint && p) hint.textContent = p.hint;
      reroute(id);
    }
    const onClick = (e: Event) => {
      const b = (e.target as HTMLElement).closest<HTMLButtonElement>('[data-preset-id]');
      if (b) choose(b);
    };
    const onKey = (e: KeyboardEvent) => {
      const i = buttons.indexOf(document.activeElement as HTMLButtonElement);
      if (i < 0) return;
      const d = e.key === 'ArrowRight' || e.key === 'ArrowDown' ? 1 : e.key === 'ArrowLeft' || e.key === 'ArrowUp' ? -1 : 0;
      if (!d) return;
      e.preventDefault();
      choose(buttons[(i + d + buttons.length) % buttons.length], true);
    };
    group?.addEventListener('click', onClick);
    group?.addEventListener('keydown', onKey);

    // ------------------------------------------------------------ run
    const tick = (_t: number, deltaMs: number) => {
      step(Math.min(deltaMs, 50) / 1000);
      lean += (leanAim - lean) * 0.06;
      draw();
    };
    const move = (e: PointerEvent) => { leanAim = (e.clientX / innerWidth - 0.5) * 28; };
    const ro = new ResizeObserver(() => { layout(); draw(); });
    let st: ScrollTrigger | null = null;
    let alive = true;

    document.fonts.load(`500 12px "JetBrains Mono"`).catch(() => {}).finally(() => {
      if (!alive) return;
      layout();
      warm();
      draw();
      host.classList.add('is-live');
      ro.observe(host);
      if (reduced) return;
      if (!isTouch()) addEventListener('pointermove', move, { passive: true });
      st = ScrollTrigger.create({
        trigger: host,
        start: 'top bottom',
        end: 'bottom top',
        onToggle: (self) => (self.isActive ? gsap.ticker.add(tick) : gsap.ticker.remove(tick)),
      });
      if (st.isActive) gsap.ticker.add(tick);
    });

    return () => {
      alive = false;
      gsap.ticker.remove(tick);
      st?.kill();
      ro.disconnect();
      removeEventListener('pointermove', move);
      group?.removeEventListener('click', onClick);
      group?.removeEventListener('keydown', onKey);
    };
  });
}
