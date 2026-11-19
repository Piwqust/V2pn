// Page chrome that works with or without the motion engine: the phone menu, copy buttons, the download for the
// visitor's computer. Each init returns its cleanup, so marketing pages run them inside onPage() and the provider
// tools run them once.

const ua = () => navigator.userAgent || '';
type UAData = { platform?: string; mobile?: boolean; getHighEntropyValues?: (h: string[]) => Promise<{ architecture?: string }> };
const uaData = () => (navigator as Navigator & { userAgentData?: UAData }).userAgentData;

/** win | mac | phone | other. An iPad asks for the desktop site and says "Macintosh"; its touch screen gives it away. */
export function detectOs(): 'win' | 'mac' | 'phone' | 'other' {
  const platform = uaData()?.platform || navigator.platform || '';
  const phone = uaData()?.mobile || /Android|iPhone|iPad|iPod|Mobile|Windows Phone/i.test(ua()) || (/Mac/i.test(platform) && navigator.maxTouchPoints > 1);
  if (phone) return 'phone';
  if (/Win/i.test(platform) || /Windows/i.test(ua())) return 'win';
  if (/Mac/i.test(platform) || /Mac OS X/i.test(ua())) return 'mac';
  return 'other';
}

/**
 * The download for this computer. Every [data-dl="<key>"] row knows its file; [data-dl-primary] links take the
 * detected one's href and words. Without this script the links lead to the full list on /download/.
 */
export function initDownloads(root: ParentNode = document) {
  const os = detectOs();
  document.documentElement.dataset.os = os;
  const rows = [...root.querySelectorAll<HTMLElement>('[data-dl]')];
  const primaries = [...root.querySelectorAll<HTMLAnchorElement>('[data-dl-primary]')];
  const label = (key: string) => ({
    'windows-x64': 'Скачать для Windows', 'windows-arm64': 'Скачать для Windows на ARM',
    'macos-arm64': 'Скачать для macOS', 'macos-x64': 'Скачать для Mac с Intel',
  })[key] || 'Скачать V2pn';
  const detail = (key: string) => ({
    'windows-x64': 'Windows 10 и 11, x64 · ZIP', 'windows-arm64': 'Windows на ARM64 · ZIP',
    'macos-arm64': 'Apple silicon, macOS 13.6+ · DMG', 'macos-x64': 'Intel, macOS 13.6+ · DMG',
  })[key] || '';
  function point(key: string) {
    rows.forEach((r) => r.classList.toggle('is-yours', r.dataset.dl === key));
    primaries.forEach((a) => {
      a.href = `/r/${key}`;
      const t = a.querySelector('[data-dl-label]');
      if (t) t.textContent = label(key);
    });
    root.querySelectorAll<HTMLElement>('[data-dl-detail]').forEach((el) => { el.textContent = detail(key); });
  }
  if (os === 'win') {
    point('windows-x64');
    uaData()?.getHighEntropyValues?.(['architecture']).then((v) => { if (v.architecture === 'arm') point('windows-arm64'); }).catch(() => {});
  } else if (os === 'mac') {
    point('macos-arm64');
  }
  return () => {};
}

/** [data-copy="<id>"] copies the text of #<id>; the button says so for two seconds. */
export function initCopy(root: ParentNode = document) {
  const off: (() => void)[] = [];
  root.querySelectorAll<HTMLButtonElement>('[data-copy]').forEach((btn) => {
    const target = document.getElementById(btn.dataset.copy || '');
    if (!target) return;
    const label = btn.textContent;
    let timer = 0;
    const click = async () => {
      const text = (target.textContent || '').trim();
      try {
        await navigator.clipboard.writeText(text);
        btn.textContent = 'Скопировано';
      } catch {
        const range = document.createRange();
        range.selectNodeContents(target);
        const sel = getSelection();
        sel?.removeAllRanges();
        sel?.addRange(range);
        btn.textContent = 'Выделено: Ctrl+C';
      }
      btn.classList.add('is-done');
      clearTimeout(timer);
      timer = window.setTimeout(() => { btn.textContent = label; btn.classList.remove('is-done'); }, 2000);
    };
    btn.addEventListener('click', click);
    off.push(() => { btn.removeEventListener('click', click); clearTimeout(timer); });
  });
  return () => off.forEach((f) => f());
}

/** The phone menu: a full-screen sheet; focus stays inside, Escape and every link close it. */
export function initMenu(onToggle?: (open: boolean) => void) {
  const btn = document.querySelector<HTMLButtonElement>('[data-menu-button]');
  const menu = document.getElementById('menu');
  if (!btn || !menu) return () => {};
  const html = document.documentElement;
  const focusables = () => [...menu.querySelectorAll<HTMLElement>('a, button')];
  const set = (open: boolean) => {
    btn.setAttribute('aria-expanded', String(open));
    btn.querySelector('[data-menu-word]')!.textContent = open ? 'Закрыть' : 'Меню';
    menu.classList.toggle('is-open', open);
    menu.toggleAttribute('inert', !open);
    html.classList.toggle('menu-open', open);
    onToggle?.(open);
    if (open) focusables()[0]?.focus();
  };
  const click = () => set(btn.getAttribute('aria-expanded') !== 'true');
  const key = (e: KeyboardEvent) => {
    if (btn.getAttribute('aria-expanded') !== 'true') return;
    if (e.key === 'Escape') { set(false); btn.focus(); return; }
    if (e.key !== 'Tab') return;
    const list = [btn, ...focusables()];
    const i = list.indexOf(document.activeElement as HTMLElement);
    const n = (i + (e.shiftKey ? -1 : 1) + list.length) % list.length;
    e.preventDefault();
    list[n].focus();
  };
  const linkClick = (e: Event) => { if ((e.target as HTMLElement).closest('a')) set(false); };
  menu.toggleAttribute('inert', true);
  btn.addEventListener('click', click);
  document.addEventListener('keydown', key);
  menu.addEventListener('click', linkClick);
  return () => {
    btn.removeEventListener('click', click);
    document.removeEventListener('keydown', key);
    menu.removeEventListener('click', linkClick);
    html.classList.remove('menu-open');
  };
}
