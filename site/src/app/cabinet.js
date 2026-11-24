// /providers/cabinet/ = «Мой партнёр»: one form (name, subscription link, logo, colours, background, layout, links, special
// rights) beside V2pn's window that changes with it. «Опубликовать» saves and publishes at once (PUT /api/partner) once
// the subscription's domain is proved to be the provider's (a DNS record or a file, «Проверить»: POST
// /api/partner/proof); till then a new partner is saved and waits. The special rights wait for a moderator while the rest
// is live. The link is checked from the server as it is typed.
import { api, me, toSignIn, wireAccount } from './api.js';
import { $, $$, busy, checkHtml, esc, plural, prefersDark, setFieldError, toast, windowHtml, wireCopy } from './hubui.js';

const ROOT = '/';
const MAX_BYTES = 1048576;
const STARTER = '#2F6BFF';
const RIGHT_NAMES = { routing: 'свои правила маршрутизации', lock: 'скрытые адреса серверов', actions: 'свои кнопки', account: 'API и вход по коду' };
const ROUTING_EXAMPLE = 'domain:ru, geoip:ru → direct\ndomain:youtube.com, domain:openai.com → proxy';
// What a button can do here; the hub knows more (V2pnPartnerCatalog), and a button that does one of those stays as it is.
const KINDS = [['open.link', 'Открыть ссылку'], ['open.renew', 'Продлить'], ['open.support', 'Поддержка'], ['open.cabinet', 'Открыть кабинет'],
  ['connect.best', 'Самый быстрый сервер'], ['subscription.update', 'Обновить подписку'], ['check.services', 'Проверить сервисы']];
const STARTER_ACTIONS = [{ label: 'Продлить', do: 'open.renew' }, { label: 'Устройства', do: 'open.link', url: '' }];

let account = null;
let data = null;      // GET /api/partner (and every PUT's answer)
let form = null;      // what the page shows, edited in place
let savedKey = '';    // payload() as last saved
let mode = prefersDark() ? 'dark' : 'light';
let check = { state: 'idle' };
let checkSeq = 0;
let checkTimer = null;

const partner = () => data && data.partner;
const picUrl = sha => (sha ? `/api/partner/pictures/${sha}` : null);

// ---------------------------------------------------------------- loading

function showState(state, title, message) {
  $('#view-loading').hidden = state !== 'loading';
  $('#view-error').hidden = state !== 'error';
  $('#editor').hidden = state !== 'ready';
  $('#account').hidden = state !== 'ready';
  if (title) $('[data-error-title]').textContent = title;
  if (message) $('[data-error-text]').textContent = message;
}

async function load() {
  showState('loading');
  try {
    const who = await me(ROOT);
    account = who.account;
    wireAccount(ROOT, account);
    data = await api.get('/api/partner');
    fromServer();
    showState('ready');
    renderAll();
  } catch (e) {
    showState('error', e.code === 'offline' ? 'Нет связи' : 'Не загрузилось', e.message);
  }
}
$('[data-retry]').addEventListener('click', load);

/** The form from the hub's answer: what is saved, with the special rights as asked. */
function fromServer() {
  const look = data.look || {};
  const p = partner();
  const r = (p && p.rights) || {};
  form = {
    name: look.name || '',
    subscription: (p && p.subscription) || '',
    brand: look.brand && look.brand.length ? [...look.brand] : [STARTER],
    background: look.background === 'gradient' || look.background === 'image' ? look.background : 'none',
    layout: look.layout === 'stickers' ? 'stickers' : 'v2pn',
    logo: look.logo || null, logoOnLight: look.logoOnLight || null, mark: look.mark || null, backdrop: look.backdrop || null,
    links: { support: (look.links && look.links.support) || '', cabinet: (look.links && look.links.cabinet) || '' },
    rights: {
      routing: { on: Boolean(r.routing), text: r.routing ? r.routing.text || '' : ROUTING_EXAMPLE },
      lock: { on: Boolean(r.lock) },
      actions: { on: Boolean(r.actions), list: r.actions ? r.actions.content.map(a => ({ ...a })) : STARTER_ACTIONS.map(a => ({ ...a })) },
      account: { on: Boolean(r.account), api: r.account ? r.account.content.api : '', login: r.account ? r.account.content.login === true : true },
    },
  };
  savedKey = p ? JSON.stringify(payload()) : '';
  // The saved link's own check stands until the link changes.
  if (p && p.check) check = { state: p.check.ok ? 'done' : 'fail', url: p.subscription, result: p.check, domain: (p.domains || [])[0] || '', taken: false };
  else check = { state: 'idle' };
}

/** PUT /api/partner's body. */
function payload() {
  const rights = {};
  const r = form.rights;
  if (r.routing.on) rights.routing = r.routing.text;
  if (r.lock.on) rights.lock = true;
  if (r.actions.on) rights.actions = r.actions.list.map(a => ({ ...a, label: (a.label || '').trim(), url: withScheme((a.url || '').trim()) }));
  if (r.account.on) rights.account = { api: r.account.api.trim(), login: r.account.login };
  return {
    name: form.name.trim(), subscription: form.subscription.trim(), brand: form.brand, background: form.background, layout: form.layout,
    logo: form.logo, logoOnLight: form.logoOnLight, mark: form.mark, backdrop: form.backdrop,
    links: { support: form.links.support.trim(), cabinet: form.links.cabinet.trim() }, rights,
  };
}
// "t.me/x" as people type it, the way the hub takes the support link.
const withScheme = text => (text && !/^[a-z][a-z0-9+.-]*:/i.test(text) ? `https://${text}` : text);
const dirty = () => !partner() || JSON.stringify(payload()) !== savedKey;

// ---------------------------------------------------------------- drawing the form

function renderAll() {
  const p = partner();
  document.title = p ? `${p.name}: мой партнёр | V2pn` : 'Мой партнёр | V2pn';
  $('#f-name').value = form.name;
  $('#f-sub').value = form.subscription;
  $('#f-support').value = form.links.support;
  $('#f-cabinet').value = form.links.cabinet;
  $('#f-routing').value = form.rights.routing.text;
  $('#f-api').value = form.rights.account.api;
  $('#x-login').checked = form.rights.account.login;
  for (const k of Object.keys(RIGHT_NAMES)) $(`#x-${k}`).checked = form.rights[k].on;
  $('#first').hidden = Boolean(p);
  $('#down').hidden = !(p && p.state === 'down');
  if (p && p.state === 'down') $('[data-down-reason]').textContent = p.downReason ? `Причина: «${p.downReason}».` : '';
  renderSwatches();
  renderSegs();
  renderPicks();
  renderRights();
  renderActions();
  renderCheck();
  renderProof();
  renderState();
  renderShare();
  draw();
}

function renderState() {
  const p = partner();
  const pill = $('#pub-state');
  pill.hidden = false;
  if (!p) { pill.className = 'badge'; pill.textContent = 'Не опубликован'; }
  else if (p.state === 'down') { pill.className = 'badge badge--fail'; pill.textContent = 'Снят модератором'; }
  else if (p.state === 'pending') { pill.className = 'badge badge--warn'; pill.textContent = 'Ждёт подтверждения домена'; }
  else { pill.className = 'badge badge--ok'; pill.textContent = 'Опубликован'; }
  const btn = $('#publish');
  if (!btn.hasAttribute('aria-busy')) {
    btn.textContent = p ? 'Сохранить' : 'Опубликовать';
    btn.disabled = Boolean(p) && !dirty();
  }
  $('#publish-hint').textContent = !p ? 'Ссылка заработает сразу, если домен подтверждён. Если нет, партнёр сохранится и подождёт.'
    : p.state === 'pending' ? (dirty() ? 'Есть изменения. Партнёр опубликуется, когда домен будет подтверждён.' : 'Партнёр сохранён и ждёт подтверждения домена.')
      : dirty() ? (p.state === 'down' ? 'Есть изменения. Партнёр снят, поэтому пользователи их не увидят, пока модератор его не вернёт.' : 'Есть изменения. Они уйдут пользователям сразу после «Сохранить».')
        : 'Изменений нет. Всё уже у пользователей.';
}

function renderShare() {
  const p = partner();
  $('#published').hidden = !p || p.state === 'pending';
  if (!p || p.state === 'pending') return;
  const base = p.share.button;
  $('#share-base').textContent = base;
  $('#share-link').innerHTML = `${esc(base)}<mark>подписка человека</mark>`;
  $('#page-link').href = p.share.page;
  $('#snippet').innerHTML = `<span class="c">// Bot API: sendMessage с inline-кнопкой</span>
<span class="k">const</span> url = <span class="s">'${esc(base)}'</span> + encodeURIComponent(subscriptionUrl);
reply_markup: { inline_keyboard: [[
  { text: <span class="s">'Подключить в V2pn'</span>, url }
]] }`;
  const pending = (p.pending || []).map(k => RIGHT_NAMES[k] || k);
  $('#pending-note').hidden = !pending.length;
  $('[data-pending-what]').textContent = pending.length ? `${cap(pending.join(', '))}. Пока модератор не ответит, всё остальное уже работает.` : '';
  const denied = Object.entries(p.rights || {}).filter(([, r]) => r.state === 'denied');
  $('#denied-note').hidden = !denied.length;
  $('[data-denied-list]').innerHTML = denied.map(([k, r]) => `<li>${esc(cap(RIGHT_NAMES[k] || k))}${r.reason ? `: «${esc(r.reason)}»` : ''}</li>`).join('');
  wireCopy();
}

const cap = s => s.charAt(0).toUpperCase() + s.slice(1);

// ---------------------------------------------------------------- colours

const lum = hex => {
  const n = parseInt(hex.slice(1), 16);
  const c = [n >> 16, (n >> 8) & 255, n & 255].map(v => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; });
  return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
};
const contrast = (a, b) => { const x = lum(a), y = lum(b); return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05); };
const onColor = hex => (contrast(hex, '#FFFFFF') >= contrast(hex, '#0B0B0D') ? '#FFFFFF' : '#0B0B0D');
function lighter(hex, k = 0.45) {
  const n = parseInt(hex.slice(1), 16);
  return `#${[n >> 16, (n >> 8) & 255, n & 255].map(c => Math.round(c + (255 - c) * k).toString(16).padStart(2, '0')).join('').toUpperCase()}`;
}

function renderSwatches() {
  const box = $('#swatches');
  box.innerHTML = form.brand.map((c, i) => `<span class="swatch" style="background: ${esc(c)}"><input type="color" value="${esc(c.toLowerCase())}" data-i="${i}" aria-label="Цвет ${i + 1}, ${esc(c)}"></span>`).join('')
    + (form.brand.length < 3 ? '<button type="button" class="chip-btn" data-colour="add">+ цвет</button>' : '')
    + (form.brand.length > 1 ? '<button type="button" class="chip-btn" data-colour="remove">− цвет</button>' : '');
  contrastHint();
}

function contrastHint() {
  const b = form.brand[0];
  const on = onColor(b);
  $('#contrast-hint').textContent = `Надпись на кнопке будет ${on === '#FFFFFF' ? 'белой' : 'чёрной'}: контраст ${contrast(b, on).toFixed(1).replace('.', ',')}:1. V2pn подбирает её сам.`;
}

$('#swatches').addEventListener('input', e => {
  const i = e.target.dataset.i;
  if (i == null) return;
  form.brand[Number(i)] = e.target.value.toUpperCase();
  e.target.parentElement.style.background = e.target.value;
  e.target.setAttribute('aria-label', `Цвет ${Number(i) + 1}, ${e.target.value.toUpperCase()}`);
  contrastHint();
  changed();
});
$('#swatches').addEventListener('click', e => {
  const b = e.target.closest('[data-colour]');
  if (!b) return;
  if (b.dataset.colour === 'add' && form.brand.length < 3) form.brand.push(lighter(form.brand[form.brand.length - 1]));
  if (b.dataset.colour === 'remove' && form.brand.length > 1) form.brand.pop();
  renderSwatches();
  changed();
});

/** The colours the window draws: the hub's for the saved look; until the hub has seen the colours typed, close to them. */
function colours() {
  const look = data.look || {};
  const same = JSON.stringify(look.brand || []) === JSON.stringify(form.brand) && look.background === form.background && (look.backdrop || null) === form.backdrop;
  if (same && data.review && data.review.colours) return data.review.colours[mode];
  const accent = form.brand[0];
  const onAccent = onColor(accent);
  let stage = null;
  if (form.background === 'gradient') {
    const stops = form.brand.length > 1 ? form.brand : [accent, lighter(accent, 0.35)];
    const ink = mode === 'dark' ? '#FFFFFF' : onColor(stops[0]);
    stage = { kind: 'gradient', stops, ink, soft: ink, scrim: mode === 'dark' ? 'rgba(0, 0, 0, .32)' : null,
      solid: ink === '#FFFFFF' ? '#FFFFFF' : '#111111', onSolid: ink === '#FFFFFF' ? '#111111' : '#FFFFFF' };
  } else if (form.background === 'image' && form.backdrop) {
    stage = { kind: 'image', stops: [accent], ink: '#FFFFFF', soft: '#FFFFFF', scrim: 'rgba(0, 0, 0, .38)', solid: '#FFFFFF', onSolid: '#111111' };
  }
  return { accent, onAccent, accentText: accent, stage };
}

function draw() {
  if (!form) return;
  const r = form.rights;
  $('#preview').innerHTML = windowHtml({
    root: ROOT, mode, name: form.name.trim() || 'Ваш партнёр', tab: true, colours: colours(),
    mark: picUrl(form.mark), logo: picUrl(form.logo), logoOnLight: picUrl(form.logoOnLight), backdrop: picUrl(form.backdrop),
    plan: { line: 'Тариф «Год» · 214 дней', traffic: '38 из 100 ГБ · до 21.06.2027' },
    layout: form.layout, lock: r.lock.on,
    actions: r.actions.on ? r.actions.list.map(a => (a.label || '').trim()).filter(Boolean) : [],
  });
  const look = data.look || {};
  const same = JSON.stringify(look.brand || []) === JSON.stringify(form.brand) && look.background === form.background;
  const notes = same && data.review ? data.review.notes || [] : [];
  $('#notes-box').hidden = !notes.length;
  $('#notes').innerHTML = notes.map(n => `<li>${esc(n)}</li>`).join('');
}

$$('#modes button').forEach(b => {
  b.setAttribute('aria-pressed', String(b.dataset.mode === mode));
  b.addEventListener('click', () => {
    mode = b.dataset.mode;
    $$('#modes button').forEach(x => x.setAttribute('aria-pressed', String(x === b)));
    draw();
  });
});

// ---------------------------------------------------------------- background and layout

const BG_HINT = {
  none: 'Карта мира в ваших цветах, как у обычного V2pn.',
  gradient: 'Градиент из цветов бренда. С одним цветом V2pn добавит второй сам.',
  image: 'Картинка от 640 px по каждой стороне, не шире 3:1. JPG тоже подойдёт: страница сама сделает из него PNG.',
};
const LAYOUT_HINT = {
  v2pn: 'Боковая панель и главный экран, как у всех в V2pn.',
  stickers: 'Наклейки поверх всего окна, всегда в ночных цветах: название, кнопка-наклейка, серверы карточками.',
};

function renderSegs() {
  $$('#partner-form .seg[data-key] button').forEach(b => b.setAttribute('aria-pressed', String(form[b.parentElement.dataset.key] === b.dataset.v)));
  $('[data-slot="backdrop"]').hidden = form.background !== 'image';
  $('#bg-hint').textContent = BG_HINT[form.background];
  $('#layout-hint').textContent = LAYOUT_HINT[form.layout];
}

$$('#partner-form .seg[data-key] button').forEach(b => b.addEventListener('click', () => {
  form[b.parentElement.dataset.key] = b.dataset.v;
  renderSegs();
  changed();
}));

// ---------------------------------------------------------------- pictures: PNG or JPG in, a PNG that fits the slot out

function renderPicks() {
  for (const slot of ['mark', 'backdrop']) {
    const box = $(`[data-slot="${slot}"]`);
    const sha = form[slot];
    const pic = $('[data-pic]', box);
    if (sha) pic.innerHTML = `<img src="${esc(picUrl(sha))}" alt="">`;
    else if (slot === 'mark') {
      const b = form.brand[0];
      pic.innerHTML = `<span class="pick__letter" style="background: ${esc(b)}; color: ${onColor(b)}">${esc((form.name.trim() || '?').charAt(0).toUpperCase())}</span>`;
    } else pic.innerHTML = '<span class="pick__none">нет</span>';
    $('[data-clear]', box).hidden = !sha;
    const label = $('[data-pick-label]', box);
    if (!label.dataset.busy) label.textContent = sha ? 'Заменить' : (slot === 'mark' ? 'Загрузить PNG или JPG' : 'Загрузить картинку');
  }
}

function canvasPng(src, sx, sy, sw, sh, w, h) {
  const canvas = document.createElement('canvas');
  canvas.width = w;
  canvas.height = h;
  const ctx = canvas.getContext('2d');
  ctx.imageSmoothingQuality = 'high';
  ctx.drawImage(src, sx, sy, sw, sh, 0, 0, w, h);
  return new Promise((resolve, reject) => canvas.toBlob(b => (b ? resolve(b) : reject(new Error('Не получилось сделать PNG.'))), 'image/png'));
}

/** A file the hub takes for <slot>: the PNG itself when it already fits, otherwise one drawn from the picture. */
async function toPng(file, slot) {
  if (file.size > 30 * MAX_BYTES) throw new Error('Файл больше 30 МБ. Возьмите картинку поменьше.');
  let img;
  try { img = await createImageBitmap(file); } catch { throw new Error('Не получилось открыть картинку. Нужен PNG или JPG.'); }
  const { width: w, height: h } = img;
  const png = file.type === 'image/png';
  if (slot === 'mark') {
    const side = Math.min(w, h);
    if (side < 64) throw new Error(`Картинка ${w}×${h} px, а нужно от 64 px по меньшей стороне.`);
    if (png && w <= 512 && Math.abs(w - h) <= Math.trunc(w / 20) && file.size <= MAX_BYTES) return file;
    // The middle square, at most 512 px: a wide picture loses its sides rather than being squeezed.
    const out = Math.min(side, 512);
    const blob = await canvasPng(img, (w - side) / 2, (h - side) / 2, side, side, out, out);
    if (blob.size > MAX_BYTES) throw new Error('Даже в 512 px картинка больше 1 МБ. Возьмите проще.');
    return blob;
  }
  if (Math.max(w, h) > Math.min(w, h) * 3) throw new Error(`Картинка ${w}×${h} px: нужна не шире 3:1.`);
  if (Math.min(w, h) < 640) throw new Error(`Картинка ${w}×${h} px, а нужно от 640 px по каждой стороне.`);
  if (png && w <= 3200 && h <= 3200 && file.size <= MAX_BYTES) return file;
  let scale = Math.min(1, 2400 / Math.max(w, h));
  for (;;) {
    const blob = await canvasPng(img, 0, 0, w, h, Math.round(w * scale), Math.round(h * scale));
    if (blob.size <= MAX_BYTES) return blob;
    scale *= 0.8;
    if (Math.min(w, h) * scale < 640) throw new Error('В PNG до 1 МБ эта картинка не помещается. Возьмите картинку попроще.');
  }
}

function slotError(slot, message) {
  const el = $(`[data-error-for="${slot}"]`);
  el.textContent = message || '';
  el.hidden = !message;
}

$$('[data-upload]').forEach(input => input.addEventListener('change', async () => {
  const slot = input.dataset.upload;
  const file = input.files && input.files[0];
  input.value = '';
  if (!file) return;
  const label = $(`[data-slot="${slot}"] [data-pick-label]`);
  slotError(slot, '');
  label.dataset.busy = '1';
  label.textContent = 'Загружаем…';
  try {
    const blob = await toPng(file, slot);
    const res = await api.upload(`/api/partner/upload?slot=${slot}`, blob);
    data.pictures = (data.pictures || []).filter(p => p.sha256 !== res.picture.sha256).concat(res.picture);
    form[slot] = res.picture.sha256;
    if (slot === 'backdrop') form.background = 'image';
    toast('Картинка загружена. Она уйдёт пользователям после «Сохранить».');
  } catch (e) {
    slotError(slot, (e.fields && (e.fields.file || e.fields.slot)) || e.message);
  } finally {
    delete label.dataset.busy;
    renderPicks();
    renderSegs();
    changed();
  }
}));
$$('[data-clear]').forEach(btn => btn.addEventListener('click', () => {
  form[btn.dataset.clear] = null;
  if (btn.dataset.clear === 'backdrop' && form.background === 'image') form.background = 'gradient';
  renderPicks();
  renderSegs();
  changed();
}));
// A label drawn as a button takes the keyboard too.
$$('.pick label.btn').forEach(l => {
  l.tabIndex = 0;
  l.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); $(`#${l.htmlFor}`).click(); } });
});

// ---------------------------------------------------------------- the subscription link, checked from the server

function looksLikeLink(text) {
  try { const u = new URL(text); return (u.protocol === 'https:' || u.protocol === 'http:') && Boolean(u.hostname); } catch { return false; }
}

function renderCheck() {
  const box = $('#sub-found');
  const text = $('[data-found-text]', box);
  const more = $('#check-more');
  const c = check;
  box.className = 'found';
  more.hidden = true;
  if (c.state === 'checking') {
    box.classList.add('found--wait');
    text.innerHTML = '<span class="spin" aria-hidden="true"></span> Проверяем ссылку с сервера V2pn…';
    return;
  }
  if (c.state === 'idle') {
    box.classList.add('found--idle');
    text.textContent = c.message || 'Вставьте ссылку, и сервер сам найдёт домен и проверит серверы.';
    return;
  }
  if (c.state === 'error') {
    box.classList.add('found--idle');
    text.textContent = c.message;
    return;
  }
  const r = c.result;
  if (c.taken) {
    box.classList.add('found--fail');
    text.textContent = `Домен ${c.domain} уже у другого партнёра. Нужна ссылка на вашем домене.`;
  } else if (!r.ok) {
    const bad = (r.lines || []).find(l => l.level === 'fail');
    box.classList.add('found--fail');
    text.textContent = `Ссылка не прошла проверку: ${bad ? `${bad.text.toLowerCase()}${bad.detail ? `. ${bad.detail}` : ''}` : 'см. ниже'}`;
  } else {
    const total = (r.servers && r.servers.total) || 0;
    const tcp = r.tcp || [];
    const answered = tcp.filter(t => t.ok).length;
    const servers = `${total} ${plural(total, 'сервер', 'сервера', 'серверов')}`;
    // The domain proof, when it is still to come, is the one thing left (the box under the field).
    const left = c.proof && !c.proof.proved ? 'Осталось подтвердить, что домен ваш: ниже.' : 'Ничего больше указывать не нужно.';
    box.classList.add(tcp.length && answered < tcp.length ? 'found--warn' : 'found--ok');
    text.textContent = !tcp.length ? `Домен ${c.domain} найден, ${servers}. ${left}`
      : answered === tcp.length && tcp.length === total ? `Домен ${c.domain} найден, ${servers} ${plural(total, 'отвечает', 'отвечают', 'отвечают')}. ${left}`
        : answered === tcp.length ? `Домен ${c.domain} найден, ${servers}; проверили ${tcp.length}, все отвечают. ${left}`
          : `Домен ${c.domain} найден, ${servers}; с сервера V2pn ответили ${answered} из ${tcp.length}. ${c.proof && !c.proof.proved ? left : 'Опубликовать можно.'}`;
  }
  $('#check-lines').innerHTML = checkHtml(r);
  more.hidden = false;
  more.open = !r.ok;
}

function scheduleCheck() {
  clearTimeout(checkTimer);
  const url = form.subscription.trim();
  if (check.url === url && check.state !== 'idle' && check.state !== 'error') { renderCheck(); return; }
  checkSeq++;
  if (!url) { check = { state: 'idle' }; renderCheck(); return; }
  if (!looksLikeLink(url)) { check = { state: 'idle', message: 'Нужна ссылка целиком: https://…' }; renderCheck(); return; }
  check = { state: 'idle', message: 'Проверим, как только вы закончите вставлять ссылку.' };
  renderCheck();
  checkTimer = setTimeout(() => runCheck(url), 900);
}

async function runCheck(url) {
  const seq = ++checkSeq;
  check = { state: 'checking', url };
  renderCheck();
  try {
    const res = await api.post('/api/partner/check', { subscription: url }, { timeout: 40000 });
    if (seq !== checkSeq) return;
    check = { state: res.check.ok && !res.taken ? 'done' : 'fail', url, result: res.check, domain: res.domain, taken: res.taken, proof: res.proof };
    setFieldError($('#f-sub'), '');
  } catch (e) {
    if (seq !== checkSeq) return;
    const field = e.fields && e.fields.subscription;
    check = { state: 'error', url, message: field || (e.code === 'rate_limited' ? 'Проверок за час слишком много. Ссылку проверим при публикации.' : e.message) };
  }
  renderCheck();
  renderProof();
}

// ---------------------------------------------------------------- the domain proved: a DNS record or a file

/** The proof the page is about: the typed link's domain once the server has checked it, otherwise the saved one's. */
function proofNow() {
  const url = form.subscription.trim();
  if (check.proof && check.url === url && check.proof.domain) return check.proof;
  return data.proof && data.proof.domain ? data.proof : null;
}

/** The box under the link: the record and the file to add, and what the last «Проверить» found (<result>). */
function renderProof(result) {
  const box = $('#proof');
  const p = partner();
  const proof = proofNow();
  const how = proof && proof.proved && proof.proved.how;
  const savedDomain = p && (p.domains || [])[0];
  const other = Boolean(p && proof && proof.domain !== savedDomain);
  let kind = null;
  if (proof && !how) kind = !p ? 'new' : p.state === 'pending' ? 'pending' : p.state === 'down' ? 'down' : other ? 'move' : 'old';
  else if (how === 'grandfathered' && p && p.state === 'live' && !other) kind = 'old';
  box.hidden = !kind;
  if (!kind) { $('#proof-out').innerHTML = ''; return; }
  const d = proof.domain;
  const say = {
    new: ['note--info', `Подтвердите, что домен ${d} ваш.`, 'Тогда партнёр опубликуется сразу после «Опубликовать». Без этого он сохранится и будет ждать подтверждения.'],
    pending: ['note--warn', 'Партнёр сохранён, но ещё не опубликован.', `Подтвердите, что домен ${d} ваш, и нажмите «Проверить»: партнёр опубликуется сразу.`],
    move: ['note--warn', `Новый домен ${d} ещё не подтверждён.`, 'Пока его нет, «Сохранить» не переведёт партнёра на этот домен: он останется на прежнем.'],
    down: ['note--info', `Домен ${d} ещё не подтверждён.`, 'Подтвердите его, иначе после возвращения модератором партнёр будет ждать подтверждения.'],
    old: ['note--info', `Подтвердите домен ${d}, когда будет минутка.`, 'Партнёр работает и без этого: он опубликован до проверки доменов. Запись покажет, что домен ваш.'],
  }[kind];
  box.className = `note ${say[0]}`;
  $('#proof-title').textContent = say[1];
  $('[data-proof-lead]', box).textContent = say[2];
  $('#proof-name').textContent = proof.record.name;
  $('#proof-value').textContent = proof.record.value;
  $('#proof-file').textContent = proof.file.url;
  $('#proof-text').textContent = proof.file.text;
  wireCopy(box);
  if (result !== undefined) {
    const lines = result && !result.already ? [result.dns, result.https].filter(Boolean).map((l, i) => ({ id: i ? 'https' : 'dns', level: l.ok ? 'ok' : 'warn', text: l.text })) : [];
    $('#proof-out').innerHTML = lines.length ? checkHtml({ lines }) : '';
  }
}

$('#proof-check').addEventListener('click', async e => {
  const url = form.subscription.trim();
  const wasWaiting = partner() && partner().state === 'pending';
  $('#proof-out').innerHTML = '<div class="checking"><span class="spin" aria-hidden="true"></span><span>Сервер V2pn ищет запись в DNS, потом файл на сайте…</span></div>';
  await busy(e.currentTarget, async () => {
    try {
      const res = await api.post('/api/partner/proof', looksLikeLink(url) ? { subscription: url } : {}, { timeout: 30000 });
      // The form stays as typed: only the partner's state and the proof come from the answer.
      data = { ...data, partner: res.partner, proof: res.proof };
      const pc = res.proofCheck;
      if (pc && pc.ok && check.proof && check.proof.domain === pc.domain) check.proof = { ...check.proof, proved: { how: pc.how } };
      renderState();
      renderShare();
      renderCheck();
      renderProof(pc);
      $('#first').hidden = Boolean(partner());
      if (res.taken) toast('Домен подтверждён, но он уже у другого партнёра.');
      else if (wasWaiting && partner().state === 'live') toast('Домен подтверждён. Партнёр опубликован');
      else if (pc && pc.ok) toast('Домен подтверждён');
    } catch (err) {
      $('#proof-out').innerHTML = `<p class="hint">${esc(err.code === 'rate_limited' ? 'Слишком много проверок за час. Попробуйте позже.' : (err.fields && err.fields.subscription) || err.message)}</p>`;
    }
  }, 'Проверяем');
});

// ---------------------------------------------------------------- special rights

function renderRights() {
  const p = partner();
  const states = [];
  for (const k of Object.keys(RIGHT_NAMES)) {
    const box = $(`[data-right="${k}"]`);
    const on = form.rights[k].on;
    const body = $('.right__body', box);
    if (body) body.hidden = !on;
    const server = p && p.rights && p.rights[k];
    const el = $('[data-state]', box);
    let html = '';
    if (server && on) {
      if (server.state === 'pending') { html = '<span class="badge badge--warn">ждёт модератора</span>'; states.push('ждёт'); }
      else if (server.state === 'approved') { html = '<span class="badge badge--ok">одобрено</span>'; states.push('одобрено'); }
      else { html = `<span class="badge badge--fail">отказано</span>${server.reason ? `<span class="hint">«${esc(server.reason)}»</span>` : ''}`; states.push('отказано'); }
      if (server.live && server.state !== 'approved') html += '<span class="hint">В V2pn пока прежняя одобренная версия.</span>';
    } else if (on) {
      html = '<span class="hint">После «Сохранить» уйдёт модератору.</span>';
    } else if (server) {
      html = '<span class="hint">После «Сохранить» право уберётся.</span>';
    }
    el.innerHTML = html;
  }
  const waiting = states.filter(s => s === 'ждёт').length;
  $('[data-more-state]').textContent = waiting ? `${waiting} ${plural(waiting, 'ждёт', 'ждут', 'ждут')} модератора` : states.length ? states.join(', ') : '';
}

$$('[data-toggle]').forEach(input => input.addEventListener('change', () => {
  form.rights[input.dataset.toggle].on = input.checked;
  renderRights();
  changed();
}));
$('#f-routing').addEventListener('input', e => { form.rights.routing.text = e.target.value; setFieldError(e.target, ''); changed(); });
$('#f-api').addEventListener('input', e => { form.rights.account.api = e.target.value; setFieldError(e.target, ''); changed(); });
$('#x-login').addEventListener('change', e => { form.rights.account.login = e.target.checked; changed(); });

function renderActions() {
  const list = form.rights.actions.list;
  $('#actions').innerHTML = list.map((a, i) => {
    const known = KINDS.some(([v]) => v === a.do);
    const options = (known ? KINDS : KINDS.concat([[a.do, a.do]])).map(([v, t]) => `<option value="${esc(v)}"${a.do === v ? ' selected' : ''}>${esc(t)}</option>`).join('');
    return `<div class="act" data-i="${i}">
      <div class="field"><label for="act-${i}-l">Подпись</label><input id="act-${i}-l" type="text" maxlength="24" value="${esc(a.label || '')}" data-act="${i}" data-f="label"></div>
      <div class="field"><label for="act-${i}-d">Что делает</label><select id="act-${i}-d" data-act="${i}" data-f="do">${options}</select></div>
      ${a.do === 'open.link' ? `<div class="field act__url"><label for="act-${i}-u">Ссылка</label><input id="act-${i}-u" type="text" inputmode="url" spellcheck="false" placeholder="https://t.me/ваш_бот" value="${esc(a.url || '')}" data-act="${i}" data-f="url"></div>` : ''}
      <button class="btn btn--secondary act__rm" type="button" data-remove-act="${i}" aria-label="Убрать кнопку ${i + 1}"><svg viewBox="0 0 16 16" aria-hidden="true"><path d="M4 4l8 8M12 4l-8 8"/></svg></button>
      <span class="field__error" hidden></span></div>`;
  }).join('') || '<p class="hint">Кнопок пока нет.</p>';
  $('#add-action').hidden = list.length >= 4;
}

$('#actions').addEventListener('input', e => {
  const el = e.target.closest('[data-act]');
  if (!el || el.tagName === 'SELECT') return;
  form.rights.actions.list[Number(el.dataset.act)][el.dataset.f] = el.value;
  changed();
});
$('#actions').addEventListener('change', e => {
  const el = e.target.closest('select[data-act]');
  if (!el) return;
  const a = form.rights.actions.list[Number(el.dataset.act)];
  a.do = el.value;
  if (a.do === 'connect.best' && !a.strategy) a.strategy = 'ping';
  renderActions();
  $(`#act-${el.dataset.act}-d`).focus();
  changed();
});
$('#actions').addEventListener('click', e => {
  const b = e.target.closest('[data-remove-act]');
  if (!b) return;
  form.rights.actions.list.splice(Number(b.dataset.removeAct), 1);
  renderActions();
  changed();
});
$('#add-action').addEventListener('click', () => {
  if (form.rights.actions.list.length >= 4) return;
  form.rights.actions.list.push({ label: '', do: 'open.link', url: '' });
  renderActions();
  $(`#act-${form.rights.actions.list.length - 1}-l`).focus();
  changed();
});

$('#api-test').addEventListener('click', async e => {
  const out = $('#api-out');
  const input = $('#f-api');
  const value = input.value.trim();
  if (!value) {
    setFieldError(input, 'Напишите адрес API: https://ваш-домен/путь/.');
    input.focus();
    return;
  }
  if (!partner()) {
    out.innerHTML = '<p class="hint">Сначала опубликуйте партнёра: API проверяется на домене его подписки.</p>';
    return;
  }
  setFieldError(input, '');
  out.innerHTML = '<div class="checking"><span class="spin" aria-hidden="true"></span><span>Сервер V2pn вызывает login/start и login/poll…</span></div>';
  await busy(e.currentTarget, async () => {
    try {
      const res = await api.post('/api/partner/login-test', { api: value }, { timeout: 30000 });
      const t = res.test;
      out.innerHTML = `<div class="stack stack--sm">${t.ok ? '<span class="badge badge--ok">API отвечает как надо</span>' : '<span class="badge badge--fail">API пока не готов</span>'}${checkHtml({ lines: t.lines })}</div>`;
    } catch (err) {
      if (err.fields && err.fields.api) setFieldError(input, err.fields.api);
      out.innerHTML = `<p class="hint">${esc(err.code === 'rate_limited' ? 'Слишком много проверок за час. Попробуйте позже.' : err.message)}</p>`;
    }
  }, 'Проверяем');
});

// ---------------------------------------------------------------- the plain fields

$('#f-name').addEventListener('input', e => { form.name = e.target.value; setFieldError(e.target, ''); renderPicks(); changed(); });
$('#f-sub').addEventListener('input', e => { form.subscription = e.target.value; setFieldError(e.target, ''); scheduleCheck(); renderProof(); changed({ preview: false }); });
$('#f-support').addEventListener('input', e => { form.links.support = e.target.value; setFieldError(e.target, ''); changed({ preview: false }); });
$('#f-cabinet').addEventListener('input', e => { form.links.cabinet = e.target.value; setFieldError(e.target, ''); changed({ preview: false }); });

function changed({ preview = true } = {}) {
  if (preview) draw();
  renderState();
}

window.addEventListener('beforeunload', e => {
  if (form && partner() && dirty()) { e.preventDefault(); e.returnValue = ''; }
});

// ---------------------------------------------------------------- publishing

const PROBLEM_LABEL = {
  name: 'Название', subscription: 'Ссылка на подписку', brand: 'Цвета', colours: 'Цвета', background: 'Фон', layout: 'Раскладка',
  logo: 'Широкий логотип', logoOnLight: 'Логотип для светлого фона', mark: 'Логотип', backdrop: 'Картинка фона',
  'links.cabinet': 'Оплата', 'links.support': 'Поддержка', rights: 'Особые права', 'rights.routing': 'Правила маршрутизации',
  'rights.lock': 'Скрытые адреса', 'rights.actions': 'Свои кнопки', 'rights.account.api': 'Адрес API', 'rights.account.login': 'Вход по коду',
};
const FIELD_INPUT = {
  name: '#f-name', subscription: '#f-sub', 'links.cabinet': '#f-cabinet', 'links.support': '#f-support', 'rights.routing': '#f-routing', 'rights.account.api': '#f-api',
};
const ERROR_SLOT = { brand: 'brand', colours: 'brand', logo: 'mark', logoOnLight: 'mark', mark: 'mark', backdrop: 'backdrop', background: 'backdrop', rights: 'rights.actions', 'rights.lock': 'rights.lock', 'rights.actions': 'rights.actions' };

function clearProblems() {
  $$('#partner-form [aria-invalid]').forEach(el => setFieldError(el, ''));
  $$('[data-error-for]').forEach(el => { el.hidden = true; el.textContent = ''; });
  $$('.act .field__error').forEach(el => { el.hidden = true; el.textContent = ''; });
  $('#problems').hidden = true;
}

/** The hub's (or the page's) problems under their fields and in one list at the top; null fields = a message only. */
function showProblems(fields, title = 'Не опубликовано.', message = '') {
  clearProblems();
  const entries = Object.entries(fields || {});
  let first = null;
  for (const [key, text] of entries) {
    const m = /^rights\.actions\.(\d+)$/.exec(key);
    if (m) {
      const err = $(`.act[data-i="${m[1]}"] .field__error`);
      if (err) { err.textContent = text; err.hidden = false; first ??= $(`.act[data-i="${m[1]}"] input`); }
      continue;
    }
    if (FIELD_INPUT[key]) {
      setFieldError($(FIELD_INPUT[key]), text);
      first ??= $(FIELD_INPUT[key]);
      continue;
    }
    const slot = ERROR_SLOT[key];
    if (slot) slotError(slot, text);
    if (key === 'rights.account.login') $('#api-out').innerHTML = `<p class="field__error">${esc(text)}</p>`;
  }
  if (entries.some(([k]) => k.startsWith('rights'))) $('#more').open = true;
  $('[data-problems-title]').textContent = title;
  $('#problems-list').innerHTML = message ? `<li>${esc(message)}</li>`
    : entries.map(([k, t]) => {
      const a = /^rights\.actions\.(\d+)$/.exec(k);
      return `<li>${esc(a ? `Кнопка ${Number(a[1]) + 1}` : PROBLEM_LABEL[k] || k)}: ${esc(t)}</li>`;
    }).join('');
  $('#problems').hidden = false;
  if (first) first.focus({ preventScroll: true });
  $('#problems').scrollIntoView({ block: 'center', behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
}

function localProblems() {
  const f = {};
  if (!form.name.trim()) f.name = 'Напишите название.';
  if (!form.subscription.trim()) f.subscription = 'Вставьте ссылку на подписку.';
  else if (!looksLikeLink(form.subscription.trim())) f.subscription = 'Нужна ссылка целиком: https://…';
  if (form.background === 'image' && !form.backdrop) f.backdrop = 'Загрузите картинку или выберите другой фон.';
  if (form.rights.routing.on && !form.rights.routing.text.trim()) f['rights.routing'] = 'Напишите правила или снимите галочку.';
  if (form.rights.account.on && !form.rights.account.api.trim()) f['rights.account.api'] = 'Напишите адрес API или снимите галочку.';
  if (form.rights.actions.on) {
    if (!form.rights.actions.list.length) f['rights.actions'] = 'Добавьте кнопку или снимите галочку.';
    form.rights.actions.list.forEach((a, i) => {
      if (!(a.label || '').trim()) f[`rights.actions.${i}`] = 'Напишите подпись.';
      else if (a.do === 'open.link' && !(a.url || '').trim()) f[`rights.actions.${i}`] = 'Напишите ссылку.';
    });
  }
  return f;
}

$('#partner-form').addEventListener('submit', async e => {
  e.preventDefault();
  const local = localProblems();
  if (Object.keys(local).length) { showProblems(local, 'Пока нельзя опубликовать.'); return; }
  const first = !partner();
  const btn = $('#publish');
  await busy(btn, async () => {
    try {
      const res = await api.put('/api/partner', payload(), { timeout: 45000 });
      data = { ...res, slots: res.slots || (data && data.slots) };
      fromServer();
      if (res.check) check = { state: 'done', url: res.partner.subscription, result: res.check, domain: (res.partner.domains || [])[0] || '', taken: false };
      clearProblems();
      renderAll();
      if (res.proofCheck && !res.proofCheck.ok) renderProof(res.proofCheck);
      const waiting = res.partner.state === 'pending';
      toast(waiting ? 'Сохранено. Подтвердите домен, и партнёр опубликуется' : first ? 'Опубликовано. Ссылка уже работает' : 'Сохранено и опубликовано');
      if (waiting) $('#proof').scrollIntoView({ block: 'center', behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
      else if (first) $('#published').scrollIntoView({ block: 'start', behavior: 'smooth' });
    } catch (err) {
      if (err.code === 'unauthorized') { showProblems(null, 'Вход закончился.', 'Войдите снова: страница сейчас откроет вход.'); setTimeout(() => toSignIn(ROOT), 2500); return; }
      if (err.code === 'check_failed' && err.detail) {
        check = { state: 'fail', url: form.subscription.trim(), result: err.detail, domain: hostOf(form.subscription), taken: false };
        renderCheck();
        const bad = (err.detail.lines || []).find(l => l.level === 'fail');
        showProblems({ subscription: bad ? `${bad.text}${bad.detail ? `. ${bad.detail}` : ''}` : err.message });
        return;
      }
      if (err.code === 'domain_unproved' && err.detail && err.detail.proof) {
        // A live partner moving to a domain it has not proved: it stays where it was; the box says what to add.
        check = { ...check, url: form.subscription.trim(), domain: err.detail.proof.domain, proof: err.detail.proof };
        renderProof(err.detail.proofCheck);
        showProblems(null, 'Не сохранено: новый домен не подтверждён.', err.message);
        return;
      }
      if (err.fields) { showProblems(err.fields, err.code === 'domain_taken' ? 'Домен занят.' : 'Не опубликовано.'); return; }
      showProblems(null, err.code === 'offline' ? 'Нет связи.' : 'Не опубликовано.', err.message);
    }
  }, first ? 'Публикуем' : 'Сохраняем');
  renderState();
});

const hostOf = u => { try { return new URL(u.trim()).hostname; } catch { return ''; } };

// ---------------------------------------------------------------- the account

$('#password-form').addEventListener('submit', async e => {
  e.preventDefault();
  const old = $('#pw-old');
  const neu = $('#pw-new');
  setFieldError(old, '');
  setFieldError(neu, '');
  $('#pw-status').textContent = '';
  if (!old.value) { setFieldError(old, 'Напишите текущий пароль.'); old.focus(); return; }
  if (neu.value.length < 6) { setFieldError(neu, 'Новый пароль от 6 символов.'); neu.focus(); return; }
  await busy($('#pw-save'), async () => {
    try {
      await api.post('/api/auth/password', { password: old.value, newPassword: neu.value });
      old.value = '';
      neu.value = '';
      $('#pw-status').textContent = 'Пароль сменён. На других устройствах вход закончился.';
    } catch (err) {
      if (err.fields && err.fields.password) { setFieldError(old, err.fields.password); old.focus(); }
      else if (err.fields && err.fields.newPassword) { setFieldError(neu, err.fields.newPassword); neu.focus(); }
      else $('#pw-status').textContent = err.message;
    }
  });
});

// ---------------------------------------------------------------- the connection

function online() {
  $('#offline').hidden = navigator.onLine !== false;
}
window.addEventListener('offline', online);
window.addEventListener('online', () => {
  online();
  if (!$('#view-error').hidden) load();
});
online();

matchMedia('(prefers-color-scheme: dark)').addEventListener('change', ev => {
  mode = ev.matches ? 'dark' : 'light';
  $$('#modes button').forEach(x => x.setAttribute('aria-pressed', String(x.dataset.mode === mode)));
  draw();
});

load();
