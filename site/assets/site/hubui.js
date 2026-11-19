// Pieces the provider pages share: a tiny DOM builder, the check of a subscription line by line, V2pn's window as
// the preview of a look (site.css .v2w), copy buttons, a toast, and a confirmation dialog before a step that stays.

export const $ = (sel, root = document) => root.querySelector(sel);
export const $$ = (sel, root = document) => [...root.querySelectorAll(sel)];

export function esc(s) {
  return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}

/** The plural Russian noun for <n>: plural(5, 'сервер', 'сервера', 'серверов'). */
export function plural(n, one, few, many) {
  const m10 = n % 10, m100 = n % 100;
  if (m10 === 1 && m100 !== 11) return one;
  if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
  return many;
}

const MONTHS = ['января', 'февраля', 'марта', 'апреля', 'мая', 'июня', 'июля', 'августа', 'сентября', 'октября', 'ноября', 'декабря'];

/** "17 ноября 2026" (a yyyy-mm-dd or an ISO time); "17 ноября, 14:05" with <time>. */
export function date(value, { time = false, year = true } = {}) {
  if (!value) return '';
  const d = new Date(/^\d{4}-\d{2}-\d{2}$/.test(value) ? value + 'T12:00:00' : value);
  if (Number.isNaN(d.getTime())) return String(value);
  const day = `${d.getDate()} ${MONTHS[d.getMonth()]}`;
  const y = year && d.getFullYear() !== new Date().getFullYear() ? ` ${d.getFullYear()}` : '';
  if (!time) return `${day}${year ? ` ${d.getFullYear()}` : ''}`;
  return `${day}${y}, ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
}

// ---------------------------------------------------------------- feedback

let toastTimer;
/** One short line at the foot of the screen, read out by screen readers. */
export function toast(text) {
  let el = $('#toast');
  if (!el) {
    el = document.createElement('div');
    el.id = 'toast';
    el.className = 'toast';
    el.setAttribute('role', 'status');
    el.setAttribute('aria-live', 'polite');
    document.body.appendChild(el);
  }
  el.textContent = text;
  el.classList.add('is-on');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => el.classList.remove('is-on'), 2600);
}

/** <btn> shows a spinner and stays pressed while <work> runs; resolves to what work resolves to. */
export async function busy(btn, work, label) {
  if (!btn) return work();
  const html = btn.innerHTML;
  const width = btn.offsetWidth;
  btn.disabled = true;
  btn.setAttribute('aria-busy', 'true');
  btn.style.minWidth = `${width}px`;
  btn.innerHTML = `<span class="spin" aria-hidden="true"></span>${label ? esc(label) : ''}`;
  try {
    return await work();
  } finally {
    btn.disabled = false;
    btn.removeAttribute('aria-busy');
    btn.innerHTML = html;
    btn.style.minWidth = '';
  }
}

export async function copyText(text) {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    const ta = document.createElement('textarea');
    ta.value = text;
    ta.setAttribute('readonly', '');
    ta.style.position = 'fixed';
    ta.style.opacity = '0';
    document.body.appendChild(ta);
    ta.select();
    let ok = false;
    try { ok = document.execCommand('copy'); } catch { ok = false; }
    ta.remove();
    return ok;
  }
}

/** Every [data-copy-text] / [data-copy] button under <root> copies its text or its target's. */
export function wireCopy(root = document) {
  $$('[data-copy-text], [data-copy-from]', root).forEach(btn => {
    if (btn.dataset.wired) return;
    btn.dataset.wired = '1';
    btn.addEventListener('click', async () => {
      const text = btn.dataset.copyText ?? $(`#${btn.dataset.copyFrom}`)?.textContent.trim() ?? '';
      const ok = await copyText(text);
      toast(ok ? 'Скопировано' : 'Не скопировалось: выделите и нажмите Ctrl+C');
    });
  });
}

/**
 * Asks before a step that stays: resolves to the text typed (when <field> is given) or true, or null on «Отмена».
 * <field> = {label, required, value, multiline, hint}.
 */
export function confirmStep({ title, text, action, danger = false, field = null }) {
  return new Promise(resolve => {
    const dlg = document.createElement('dialog');
    dlg.className = 'dlg';
    const id = `dlg-${Math.random().toString(36).slice(2, 8)}`;
    const input = field
      ? (field.multiline
        ? `<textarea id="${id}-f" rows="3" maxlength="500" ${field.required ? 'required' : ''}>${esc(field.value || '')}</textarea>`
        : `<input id="${id}-f" type="${field.type || 'text'}" value="${esc(field.value || '')}" ${field.required ? 'required' : ''}>`)
      : '';
    dlg.setAttribute('aria-labelledby', `${id}-t`);
    dlg.innerHTML = `<form method="dialog" novalidate>
      <h2 id="${id}-t">${esc(title)}</h2>
      ${text ? `<p>${text}</p>` : ''}
      ${field ? `<div class="field"><label for="${id}-f">${esc(field.label)}</label>${input}
        ${field.hint ? `<span class="field__hint">${esc(field.hint)}</span>` : ''}
        <span class="field__error" id="${id}-e" hidden></span></div>` : ''}
      <div class="row"><button class="btn btn--secondary" value="cancel" type="button" data-cancel>Отмена</button>
      <button class="btn ${danger ? 'btn--danger' : 'btn--primary'}" value="ok" type="submit">${esc(action)}</button></div>
    </form>`;
    document.body.appendChild(dlg);
    const f = field ? $(`#${id}-f`, dlg) : null;
    const done = value => { dlg.close(); dlg.remove(); resolve(value); };
    $('[data-cancel]', dlg).addEventListener('click', () => done(null));
    dlg.addEventListener('cancel', e => { e.preventDefault(); done(null); });
    $('form', dlg).addEventListener('submit', e => {
      e.preventDefault();
      if (f && field.required && !f.value.trim()) {
        f.setAttribute('aria-invalid', 'true');
        const err = $(`#${id}-e`, dlg);
        err.textContent = field.empty || 'Заполните это поле.';
        err.hidden = false;
        f.setAttribute('aria-describedby', `${id}-e`);
        f.focus();
        return;
      }
      done(f ? f.value.trim() : true);
    });
    dlg.showModal();
    (f || $('[type=submit]', dlg)).focus();
  });
}

export function setFieldError(input, message) {
  const field = input.closest('.field, .slot, .act');
  const err = field && $('.field__error', field);
  if (message) {
    input.setAttribute('aria-invalid', 'true');
    if (err) {
      err.textContent = message;
      err.hidden = false;
      if (!err.id) err.id = `${input.id || input.name || 'f'}-err`;
      input.setAttribute('aria-describedby', [input.dataset.hintId, err.id].filter(Boolean).join(' '));
    }
  } else {
    input.removeAttribute('aria-invalid');
    if (err) {
      err.textContent = '';
      err.hidden = true;
    }
    if (input.dataset.hintId) input.setAttribute('aria-describedby', input.dataset.hintId);
    else input.removeAttribute('aria-describedby');
  }
}

// ---------------------------------------------------------------- the check of a subscription

const ICON = {
  ok: '<svg viewBox="0 0 16 16"><path d="M3.5 8.5 6.5 11.5 12.5 4.5"/></svg>',
  warn: '<svg viewBox="0 0 16 16"><path d="M8 3.5v5.5M8 12.2v.3"/></svg>',
  fail: '<svg viewBox="0 0 16 16"><path d="M4.5 4.5l7 7M11.5 4.5l-7 7"/></svg>',
};
const LEVEL = { ok: 'в порядке', warn: 'стоит поправить', fail: 'мешает' };
const PROTO = { vless: 'VLESS', vmess: 'VMess', trojan: 'Trojan', shadowsocks: 'Shadowsocks', ss: 'Shadowsocks', hysteria2: 'Hysteria2', hy2: 'Hysteria2', tuic: 'TUIC', wireguard: 'WireGuard', socks: 'SOCKS', http: 'HTTP', anytls: 'AnyTLS' };

/** The lines of a check (hub/lib/check.mjs), with the protocols and the pings under them. */
export function checkHtml(check, { raw = false } = {}) {
  if (!check) return '';
  const lines = (check.lines || []).map(l => `<li><span class="lv lv--${esc(l.level)}" aria-hidden="true">${ICON[l.level] || ''}</span>
    <div><b><span class="sr-level">${LEVEL[l.level] || ''}: </span>${esc(l.text)}</b>${l.detail ? `<span>${esc(l.detail)}</span>` : ''}
    ${raw && l.raw ? `<span><code>${esc(JSON.stringify(l.raw))}</code></span>` : ''}</div></li>`).join('');
  const protos = check.servers && check.servers.byProtocol ? Object.entries(check.servers.byProtocol).sort((a, b) => b[1] - a[1]) : [];
  const facts = protos.length ? `<div class="facts" aria-label="Серверы по протоколам">${protos.map(([p, n]) => `<span>${esc(PROTO[p] || p)} <b>${n}</b></span>`).join('')}
    ${check.http ? `<span>Ответ <b>${check.http.ms} мс</b></span>` : ''}</div>` : '';
  const pings = (check.tcp || []).length ? `<div class="stack stack--sm"><span class="hint">Серверы с нашего сервера, TCP</span>
    <div class="pings">${check.tcp.map(t => `<span>${esc(t.host)}:${t.port}</span><span class="proto">${esc(PROTO[t.protocol] || t.protocol || '')}</span>
      <span class="ms${t.ok ? '' : ' ms--off'}">${t.ok ? `${t.ms} мс` : 'не ответил'}</span>`).join('')}</div></div>` : '';
  return `<ul class="lines">${lines}</ul>${facts}${pings}`;
}

// ---------------------------------------------------------------- V2pn's window as a preview

const FORK = '<svg class="fork" viewBox="0 0 104 112" aria-hidden="true"><path d="M0,0 L32,0 L32,40 L72,0 L104,0 L104,32 L85.255,32 L64,53.255 L64,112 L32,112 L32,72 L0,40 Z"/></svg>';
const POWER = '<svg class="ic" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3v8M6.3 6.3a8 8 0 1 0 11.4 0"/></svg>';
const MENU = '<svg class="ic" viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h16M4 12h16M4 17h16"/></svg>';
const FOLDER = '<svg class="ic" viewBox="0 0 24 24" aria-hidden="true"><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/></svg>';
const SERVERS = [['de', 'Германия', 'VLESS Reality', 41], ['nl', 'Нидерланды', 'VLESS Reality', 48], ['fi', 'Финляндия', 'Hysteria2', 52]];

/**
 * V2pn's window with a provider's subscription, drawn as the app draws it (site.css .v2w).
 *   root      the site's root from the page ("../../"), for the flags and the map
 *   mode      'light' | 'dark'
 *   name      the provider's (or the subscription's) name
 *   tab       true: the provider's tab (a person who came from it); false: plain V2pn, the subscription in its list
 *   colours   the hub's review.colours[mode] ({accent, onAccent, accentText, stage}) or null (V2pn's own)
 *   mark, logo, logoOnLight, backdrop   picture URLs or null
 *   plan      {line, traffic, days} for the plan card
 *   layout    'v2pn' (the sidebar and the stage) or 'stickers' (V2pn's sticker book, drawn at night)
 *   actions   the labels of the partner's own buttons
 *   lock      true: the servers show their names only
 */
export function windowHtml({ root, mode = 'light', name = 'Провайдер', tab = true, colours = null, mark = null, logo = null, logoOnLight = null, backdrop = null,
  plan = {}, layout = 'v2pn', actions = [], lock = false }) {
  const stickers = tab && layout === 'stickers';
  const dark = mode === 'dark' || stickers;
  const c = colours || {};
  const style = [];
  if (c.accent) style.push(`--w-brand: ${c.accent}`);
  if (c.accentText) style.push(`--w-brand-text: ${c.accentText}`);
  const stage = tab && c.stage ? c.stage : null;
  const n = esc(name || 'Провайдер');
  const letter = esc((name || 'P').trim().charAt(0).toUpperCase() || 'P');
  const markHtml = mark ? `<img src="${esc(mark)}" alt="">` : `<span class="mini__letter" style="background: ${esc(c.accent || '#E8391B')}; color: ${esc(c.onAccent || '#0B0B0D')}">${letter}</span>`;
  const bar = tab
    ? `<span class="v2-tabs-bar"><span class="v2-tab">${FORK}V2pn</span><span class="v2-tab is-on">${markHtml}${n}</span></span>`
    : `<span class="v2w__brand">${FORK}V2pn</span>`;
  const proto = p => (lock ? 'адрес скрыт' : p);
  const rows = SERVERS.map(([cc, country, p, ms], i) => `<div class="v2-row${i === 0 ? ' is-on' : ''}"><img src="${root}assets/v2pn/flags/${cc}.png" alt="" width="24" height="18"><span><b>${country}</b><small>${proto(p)}</small></span><span class="v2-ping">${ms} мс</span></div>`).join('');
  const traffic = esc(plan.traffic || '12 из 50 ГБ · до 31.01.2027');
  const chips = actions.length ? `<div class="v2-chips">${actions.map(a => `<span>${esc(a)}</span>`).join('')}</div>` : '';
  const scene = () => {
    if (!stage) return null;
    const bg = stage.kind === 'image' && backdrop
      ? `${stage.scrim ? `linear-gradient(${stage.scrim}, ${stage.scrim}), ` : ''}url(${backdrop}) center / cover no-repeat, ${stage.solid}`
      : `linear-gradient(135deg, ${(stage.stops || []).join(', ')})`;
    const scrim = stage.kind !== 'image' && stage.scrim ? `linear-gradient(${stage.scrim}, ${stage.scrim}), ` : '';
    return scrim + bg;
  };
  const head = `<div class="v2w__bar">${MENU}${bar}<span class="v2w__sp"></span></div>`;
  const open = `<div class="v2w ui-v2pn${stickers ? ' v2w--stk' : ''}"${dark ? ' data-theme="dark"' : ''} style="${style.join('; ')}" aria-hidden="true">`;

  if (stickers) {
    // The sticker book: the partner's picture (or its colours) full height, the name as a meme caption, Connect as a
    // die-cut sticker with the mark, the servers as cards and the plan as a ticket.
    const bg = scene() || `linear-gradient(160deg, ${c.accent || '#E8391B'} 0%, #16161B 78%)`;
    const cards = SERVERS.map(([cc, country]) => `<span class="v2-stk__card"><img src="${root}assets/v2pn/flags/${cc}.png" alt="" width="24" height="18">${country}</span>`).join('');
    return `${open}${head}
      <div class="v2w__body v2w__body--stk"><div class="v2-stk" style="--w-scene: ${bg}">
        <span class="v2-stk__cap">${n}</span>
        <span class="v2-pill v2-pill--off">Не защищено</span>
        <span class="v2-stk__sticker"><span class="v2-stk__bubble">погнали!</span>${markHtml}
          <span class="v2-stk__go" style="background: ${esc(c.accent || '#E8391B')}; color: ${esc(c.onAccent || '#0B0B0D')}">${POWER}Подключиться</span></span>
        <span class="v2-stk__cards">${cards}</span>
        ${actions.length ? `<span class="v2-stk__acts">${actions.map(a => `<span>${esc(a)}</span>`).join('')}</span>` : ''}
        <span class="v2-stk__ticket">77 дней · ${traffic}</span>
      </div></div></div>`;
  }

  const side = tab
    ? `<div class="v2-seg"><span class="is-on">Главная</span><span>Страны</span><span>Подписки</span></div>
       ${stage ? '' : `<div class="v2-plan"><div class="v2-plan__head">${markHtml}<span><b>${n}</b><small>${esc(plan.line || 'Ваш тариф · 77 дней')}</small></span></div>
       <div class="v2-bar" style="--v: 24%"><i></i></div><small>${traffic}</small><div class="v2-plan__links"><span>Продлить</span><span>Поддержка</span></div></div>`}
       <div class="v2-label"><span>Самые быстрые</span></div>${rows}`
    : `<div class="v2-seg"><span>Главная</span><span>Страны</span><span class="is-on">Подписки</span></div>
       <div class="v2-row is-on"><span class="v2-folder">${FOLDER}</span><span><b>${n}</b><small>${esc(plan.servers || '14 серверов')} · ${traffic}</small></span><span></span></div>${rows}`;
  let main;
  if (stage) {
    const sceneLogo = stage.ink && stage.ink.toUpperCase() === '#FFFFFF' ? (logo || logoOnLight) : (logoOnLight || logo);
    main = `<div class="v2-main v2-main--scene" style="--w-scene: ${scene()}">
      <div class="v2-panel-dark"><div class="v2-head" style="margin-top: 10px"><span class="v2-pill v2-pill--off">Не защищено</span><span class="v2-title">Германия</span></div>
      <span class="v2-connect v2-connect--go" style="color: ${esc(c.onAccent || '#0B0B0D')}">${POWER}Подключиться</span>${chips}</div>
      <div class="v2-scene-plan" style="color: ${esc(stage.ink)}">
        ${sceneLogo ? `<img src="${esc(sceneLogo)}" alt="">` : `<span class="v2-scene-name">${n}</span>`}
        <span class="days">77<small style="color: ${esc(stage.soft || stage.ink)}">осталось дней<br>${traffic}</small></span>
        <div class="v2-scene-plan__btns"><span style="background: ${esc(stage.solid)}; color: ${esc(stage.onSolid)}">Продлить</span><span style="background: rgba(255,255,255,.18); color: ${esc(stage.ink)}">Поддержка</span></div>
      </div></div>`;
  } else {
    main = `<div class="v2-main"><span class="v2-map"></span>
      <div class="v2-head"><span class="v2-pill v2-pill--off">Не защищено</span><span class="v2-title"><img class="v2-flag" src="${root}assets/v2pn/flags/de.png" alt="" width="34" height="25">Германия</span><span class="v2-sub">${proto('VLESS Reality')}</span></div>
      <span class="v2-connect v2-connect--go"${tab && c.onAccent ? ` style="color: ${esc(c.onAccent)}"` : ''}>${POWER}Подключиться</span>${chips}</div>`;
  }
  return `${open}${head}<div class="v2w__body"><div class="v2-side">${side}</div>${main}</div></div>`;
}

export function prefersDark() {
  return matchMedia('(prefers-color-scheme: dark)').matches;
}
