// Page transitions. The adapter runs leave() while the next page loads. The look is set in motion/setup.ts.
import { gsap } from 'gsap';
import { prefersReducedMotion } from './env';

export type Transition = {
  leave: (curtain: HTMLElement) => Promise<unknown> | gsap.core.Animation;
  enter: (curtain: HTMLElement) => Promise<unknown> | gsap.core.Animation;
};

let current: Transition = {
  leave: (c) => gsap.timeline().set(c, { autoAlpha: 1 }).fromTo(c, { opacity: 0 }, { opacity: 1, duration: 0.3 }),
  enter: (c) => gsap.to(c, { autoAlpha: 0, duration: 0.3 }),
};

// Reduced motion: the brand's 120 ms cross-fade.
const reduced: Transition = {
  leave: (c) => gsap.timeline().set(c, { autoAlpha: 1, opacity: 0 }).to(c, { opacity: 1, duration: 0.12, ease: 'none' }),
  enter: (c) => gsap.to(c, { autoAlpha: 0, duration: 0.12, ease: 'none' }),
};

export const setTransition = (t: Transition) => { current = t; };
const pick = () => (prefersReducedMotion() ? reduced : current);
const curtain = () => document.getElementById('curtain') as HTMLElement;

export const playLeave = () => Promise.resolve(pick().leave(curtain()));
export const playEnter = () => Promise.resolve(pick().enter(curtain()));
