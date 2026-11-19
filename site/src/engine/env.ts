import { gsap } from 'gsap';
import { CustomEase } from 'gsap/CustomEase';

gsap.registerPlugin(CustomEase);

export const prefersReducedMotion = () => matchMedia('(prefers-reduced-motion: reduce)').matches;
export const isTouch = () => matchMedia('(hover: none), (pointer: coarse)').matches;
export const isMobile = () => matchMedia('(max-width: 767px)').matches;

// The brand book's curves (chapter 08). Every tween on the site uses one of these names.
CustomEase.create('standard', '0.2, 0, 0, 1');
CustomEase.create('glass', '0.7, 0, 0.2, 1');
CustomEase.create('exit', '0.4, 0, 1, 1');

export const ease = {
  out: 'standard', // entrances, reveals, UI
  inOut: 'glass', // the pane: page transitions, the one long move
  soft: 'standard',
  exit: 'exit',
} as const;
