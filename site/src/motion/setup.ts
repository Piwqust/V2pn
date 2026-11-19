// Global motion for the marketing pages: runs once on the client, before the first page initializes.
import { gsap } from 'gsap';
import { ScrollTrigger } from 'gsap/ScrollTrigger';
import { ease, onPage, onScroll, setTransition, isMobile, prefersReducedMotion } from '../engine';
import { navDirection } from '../engine/astro';
import { fork } from './fork';
import { initCopy, initDownloads, initMenu } from './ui';
import { markNav } from '../app/api.js';

gsap.registerPlugin(ScrollTrigger);

/** The one long move: a pane of ribbed glass slides across, left to right forward and right to left back. */
function glassTransition() {
  setTransition({
    leave: (c) => {
      const panel = c.querySelector<HTMLElement>('.curtain__panel');
      const markEl = c.querySelector<SVGElement>('.curtain__mark');
      const from = navDirection() === 'back' ? 100 : -100;
      return gsap.timeline()
        .set(c, { autoAlpha: 1 })
        .fromTo(panel, { xPercent: from }, { xPercent: 0, duration: 0.72, ease: ease.inOut })
        .fromTo(markEl, { autoAlpha: 0, y: 24 }, { autoAlpha: 1, y: 0, duration: 0.4, ease: ease.out }, '-=0.25');
    },
    enter: (c) => {
      const panel = c.querySelector<HTMLElement>('.curtain__panel');
      const markEl = c.querySelector<SVGElement>('.curtain__mark');
      const to = navDirection() === 'back' ? -100 : 100;
      return gsap.timeline()
        .to(markEl, { autoAlpha: 0, y: -24, duration: 0.25, ease: ease.exit })
        .to(panel, { xPercent: to, duration: 0.78, ease: ease.inOut }, 0.05)
        .set(c, { autoAlpha: 0 });
    },
  });
}

/** The header steps aside while reading down and returns on the way up; it gets a ground once off the top. */
function header() {
  let last = 0;
  const html = document.documentElement;
  onScroll(({ y }) => {
    html.classList.toggle('hdr-solid', y > 24);
    if (Math.abs(y - last) < 6) return;
    html.classList.toggle('hdr-away', y > 220 && y > last && !html.classList.contains('menu-open'));
    last = y;
  });
  onPage(() => {
    html.classList.remove('hdr-away', 'hdr-solid');
    last = 0;
  });
}

/** The home page's window: three chapters move one frame over the real screenshot (sticky narrative). */
function windowNarrative() {
  onPage(() => {
    const stage = document.querySelector<HTMLElement>('[data-stage]');
    const frame = stage?.querySelector<HTMLElement>('[data-focus]');
    const chapters = [...document.querySelectorAll<HTMLElement>('[data-chapter]')];
    if (!stage || !frame || !chapters.length) return;
    const go = (i: number) => {
      const f = JSON.parse(chapters[i].dataset.chapter || '{}');
      stage.dataset.state = String(i);
      chapters.forEach((c, j) => c.classList.toggle('is-on', j === i));
      gsap.to(frame, {
        left: `${f.x * 100}%`, top: `${f.y * 100}%`, width: `${f.w * 100}%`, height: `${f.h * 100}%`,
        duration: 0.7, ease: ease.out, overwrite: true,
      });
    };
    go(0);
    chapters.forEach((ch, i) => ScrollTrigger.create({
      trigger: ch,
      start: isMobile() ? 'top 70%' : 'top 55%',
      end: isMobile() ? 'bottom 70%' : 'bottom 55%',
      onToggle: (self) => { if (self.isActive) go(i); },
    }));
  });
}

/** Numerals that count up once, from 0 to their real value (data-count). */
function counters() {
  onPage(() => {
    if (prefersReducedMotion()) return;
    document.querySelectorAll<HTMLElement>('[data-count]').forEach((el) => {
      const end = Number(el.dataset.count);
      const o = { v: 0 };
      gsap.to(o, {
        v: end, duration: 1.6, ease: ease.out,
        scrollTrigger: { trigger: el, start: 'top 85%', once: true },
        onUpdate: () => { el.textContent = String(Math.round(o.v)); },
      });
    });
  });
}

export function setupMotion() {
  glassTransition();
  header();
  fork();
  windowNarrative();
  counters();
  onPage(() => {
    const a = initMenu(() => document.documentElement.classList.remove('hdr-away'));
    const b = initCopy();
    const c = initDownloads();
    markNav('/');
    return () => { a(); b(); c(); };
  });
}
