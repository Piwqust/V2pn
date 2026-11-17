// /providers/cabinet/: an approved provider's own corner. The look of its tab (saved as a draft as it is edited, the
// hub's review drawn at once: the colours V2pn will use, contrast notes, what blocks it), sent to the owner for
// publication; the link for the bot's button; at tier 2 the sign-in API, its test and the provider's own buttons.
import { api, me, wireAccount } from './api.js';
import { $, $$, busy, checkHtml, date, esc, prefersDark, setFieldError, toast, windowHtml, wireCopy } from './hubui.js';

const ROOT = '../../';
const VIEWS = ['overview', 'look', 'share', 'api'];
const SLOT_TEXT = {
  logo: ['Логотип для тёмного фона', 'dark'],
  logoOnLight: ['Логотип для светлого фона', 'light'],
  mark: ['Знак', 'dark'],
  backdrop: ['Фон «Сцены»', ''],
};
const ICONS = [['', 'Без значка'], ['bolt', 'Молния'], ['shield', 'Щит'], ['play', 'Видео'], ['game', 'Игры'], ['card', 'Карта'], ['chat', 'Чат'], ['globe', 'Глобус'],
  ['star', 'Звезда'], ['refresh', 'Обновить'], ['check', 'Галочка'], ['info', 'Инфо'], ['gift', 'Подарок']];
const DOS = [['connect.best', 'Подключить лучший сервер', 'strategy'], ['connect.recommend', 'Сервер под задачу (нужен API)', 'purpose'],
  ['connect.server', 'Подключить сервер', 'server'], ['connect.group', 'Подключить группу', 'group'], ['open.cabinet', 'Открыть кабинет', ''],
  ['open.renew', 'Продлить', ''], ['open.support', 'Поддержка', ''], ['open.link', 'Открыть ссылку', 'url'], ['subscription.update', 'Обновить подписку', ''],
  ['check.services', 'Проверить сервисы', ''], ['info.card', 'Карточка с данными (нужен API)', '']];
const PARAM = {
  strategy: ['Как выбрать', [['ping', 'по пингу'], ['stable', 'самый стабильный'], ['load', 'наименее загруженный']]],
  purpose: ['Для чего', [['youtube', 'YouTube'], ['games', 'игры'], ['mobile', 'мобильный интернет'], ['whitelist', 'белые списки'], ['streaming', 'стриминг'], ['ai', 'нейросети'], ['torrents', 'торренты']]],
  server: ['Адрес сервера', null],
  group: ['Группа', null],
  url: ['Ссылка', null],
};

let account = null;
let data = null;            // GET /api/cabinet
let look = null;            // the draft being edited
let mode = prefersDark() ? 'dark' : 'light';
let saveTimer = null;
let saving = Promise.resolve();

// ---------------------------------------------------------------- loading and the sections

function showState(state, message) {
  $('#view-loading').hidden = state !== 'loading';
  $('#view-error').hidden = state !== 'error';
  $('#shell').hidden = state !== 'ready';
  if (message) $('[data-error-text]').textContent = message;
}

function route() {
  let view = location.hash.slice(1);
  if (!VIEWS.includes(view)) view = 'overview';
  for (const v of VIEWS) $(`[data-view="${v}"]`).hidden = v !== view;
  $$('.sidenav [data-tab]').forEach(a => {
    if (a.dataset.tab === view) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
  });
  if (view === 'look') drawPreview();
}
window.addEventListener('hashchange', () => { route(); window.scrollTo({ top: 0 }); });

async function load() {
  showState('loading');
  try {
    const who = await me(ROOT);
    account = who.account;
    wireAccount(ROOT, account);
    if (!who.provider) {
      location.replace(account.admin ? `${ROOT}admin/` : `${ROOT}providers/apply/`);
      return;
    }
    data = await api.get('/api/cabinet');
    look = structuredClone(data.look.draft);
    showState('ready');
    renderAll();
    route();
  } catch (e) {
    showState('error', e.message);
  }
}
$('[data-retry]').addEventListener('click', load);

const tierName = t => (t >= 2 ? 'Интеграция' : 'Проверенный');
const same = (a, b) => Boolean(a && b) && JSON.stringify(a) === JSON.stringify(b);
const picUrl = sha => (sha ? `/api/cabinet/pictures/${sha}` : null);
const revoked = () => data.provider.state === 'revoked';

function renderAll() {
  const p = data.provider;
  document.title = `Кабинет ${p.name} | V2pn`;
  $('#title').textContent = p.name;
  $$('[data-page-link]').forEach(a => { a.href = data.share.page; });
  $('[data-api-lock]').hidden = p.tier >= 2;
  $('#revoked').hidden = !revoked();
  if (revoked()) $('[data-revoke-reason]').textContent = p.revokeReason ? `Причина: «${p.revokeReason}».` : '';
  renderOverview();
  renderLookForm();
  renderReview();
  renderShare();
  renderApi();
  $$('#look-form input, #look-form button, #api-tier2 input, #api-tier2 select, #api-tier2 button').forEach(el => { if (revoked()) el.disabled = true; });
}

// ---------------------------------------------------------------- overview

function lookState() {
  const p = data.provider;
  if (p.queueState === 'queued') return ['info', 'Оформление ждёт проверки'];
  if (p.queueState === 'returned') return ['warn', 'Оформление вернули'];
  if (data.look.published && data.look.published.brand?.length) return ['ok', 'Оформление опубликовано'];
  return ['', 'Оформление не настроено'];
}

function renderOverview() {
  const p = data.provider;
  const [lvl, text] = lookState();
  $('[data-name]').textContent = p.name;
  $('#ov-badges').innerHTML = `${revoked() ? '<span class="badge badge--fail">Отозван</span>' : '<span class="badge badge--ok">Одобрен</span>'}
    <span class="badge">Ступень ${p.tier}: ${tierName(p.tier)}</span><span class="badge${lvl ? ` badge--${lvl}` : ''}">${text}</span>`;
  $('#ov-kv').innerHTML = `<dt>Страница</dt><dd><a href="${esc(data.share.page)}" target="_blank" rel="noopener">${esc(data.share.page.replace(/^https?:\/\//, ''))}</a></dd>
    <dt>Домены подписки</dt><dd>${p.domains.map(esc).join(', ')}</dd>
    <dt>Срок</dt><dd>до ${esc(date(p.until))}${p.expired ? ' — истёк, напишите владельцу V2pn' : ''}</dd>
    <dt>Права</dt><dd>${p.grants.map(g => ({ edition: 'вкладка', account: 'вход по коду и тариф', actions: 'свои кнопки', lock: 'скрытые адреса' })[g] || g).join(', ')}</dd>
    ${p.publishedAt ? `<dt>Опубликовано</dt><dd>${esc(date(p.publishedAt, { time: true }))}</dd>` : ''}`;
  const designed = Boolean(data.look.published?.brand?.length);
  const tick = '<svg viewBox="0 0 16 16"><path d="M3.5 8.5 6.5 11.5 12.5 4.5"/></svg>';
  const item = (done, n, title, sub, href, action) => `<li class="${done ? 'is-done' : ''}"><span class="tick">${done ? tick : n}</span>
    <div><b>${title}</b><span>${sub}</span></div>${href ? `<a class="btn btn--secondary btn--sm" href="${href}">${action}</a>` : ''}</li>`;
  const ret = p.queueState === 'returned' ? ` Вернули: «${esc(p.returnReason || '')}».` : '';
  $('#todo').innerHTML = [
    item(designed, 1, 'Оформление', p.queueState === 'queued' ? 'Отправлено, ждёт проверки владельца V2pn.' : designed ? `Опубликовано. Меняйте когда угодно: изменения снова пройдут проверку.${ret}` : `Цвета и логотип. Минут пять.${ret}`, '#look', designed ? 'Изменить' : 'Настроить'),
    item(false, 2, 'Кнопка для бота', 'Одна ссылка: по ней V2pn ставится и добавляет подписку с вашей вкладкой.', '#share', 'Взять ссылку'),
    p.tier >= 2
      ? item(Boolean(data.look.published?.login), 3, 'Вход по коду', 'Два адреса API на вашем сервере, и человек входит по коду из бота.', '#api', data.look.published?.login ? 'Открыть' : 'Подключить')
      : item(false, 3, 'Вход по коду', p.tierRequest ? 'Запрос на ступень 2 у владельца V2pn.' : 'На ступени 2: вход по коду из бота и свои кнопки.', '#api', 'Подробнее'),
  ].join('');
}

// ---------------------------------------------------------------- the look form

const fieldOf = key => $(`[data-key="${key}"]`);

function renderLookForm() {
  $('#look-name').value = look.name || '';
  $('#link-cabinet').value = look.links?.cabinet || '';
  $('#link-support').value = look.links?.support || '';
  $$('input[name="background"]').forEach(r => { r.checked = r.value === (look.background || 'none'); });
  renderColours();
  renderSlots();
}

function renderColours() {
  const box = $('#colours');
  const list = look.brand.length ? look.brand : [''];
  box.innerHTML = list.map((hex, i) => `<div class="colour">
    <input type="color" value="${esc(/^#[0-9A-Fa-f]{6}$/.test(hex) ? hex : '#E8391B')}" aria-label="Цвет ${i + 1}" data-i="${i}">
    <input type="text" value="${esc(hex)}" maxlength="7" spellcheck="false" placeholder="#RRGGBB" aria-label="Цвет ${i + 1}, код" data-i="${i}">
    ${list.length > 1 || hex ? `<button class="btn btn--secondary" type="button" data-remove-colour="${i}" aria-label="Убрать цвет ${i + 1}"><svg viewBox="0 0 16 16"><path d="M4 4l8 8M12 4l-8 8"/></svg></button>` : '<span></span>'}
  </div>`).join('');
  $('[data-add-colour]').hidden = look.brand.length >= 3 || !look.brand.length;
  $$('input', box).forEach(input => input.addEventListener(input.type === 'color' ? 'input' : 'input', () => {
    const i = Number(input.dataset.i);
    let v = input.value.trim().toUpperCase();
    if (input.type === 'text' && v && !v.startsWith('#')) v = `#${v}`;
    const pair = $$(`input[data-i="${i}"]`, box);
    if (/^#[0-9A-F]{6}$/.test(v)) {
      pair.forEach(x => { if (x !== input) x.value = x.type === 'color' ? v.toLowerCase() : v; });
      const brand = [...look.brand];
      brand[i] = v;
      look.brand = brand.filter(Boolean);
      setFieldError(input, '');
      $('[data-add-colour]').hidden = look.brand.length >= 3;
      changed();
    } else if (input.type === 'text') {
      input.addEventListener('blur', () => { if (!/^#[0-9A-F]{6}$/i.test(input.value.trim().replace(/^([^#])/, '#$1'))) setFieldError(input, input.value ? 'Цвет в виде #RRGGBB, например #2E6BFF.' : ''); }, { once: true });
    }
  }));
  $$('[data-remove-colour]', box).forEach(btn => btn.addEventListener('click', () => {
    look.brand.splice(Number(btn.dataset.removeColour), 1);
    renderColours();
    changed();
  }));
}
$('[data-add-colour]').addEventListener('click', () => {
  if (look.brand.length >= 3) return;
  const base = look.brand[look.brand.length - 1] || '#E8391B';
  look.brand.push(lighter(base));
  renderColours();
  $$('#colours input[type="text"]').at(-1).focus();
  changed();
});

function lighter(hex) {
  const n = parseInt(hex.slice(1), 16);
  const mix = c => Math.round(c + (255 - c) * 0.45);
  return `#${[n >> 16, (n >> 8) & 255, n & 255].map(mix).map(c => c.toString(16).padStart(2, '0')).join('').toUpperCase()}`;
}

function renderSlots() {
  $('#slots').innerHTML = Object.entries(SLOT_TEXT).map(([key, [label, tile]]) => {
    const sha = look[key];
    const pic = sha ? data.pictures.find(p => p.sha256 === sha) : null;
    const hint = data.slots?.[key]?.hint || '';
    return `<div class="slot" data-slot="${key}">
      <div class="slot__pic${tile ? ` slot__pic--${tile}` : ''}">${sha ? `<img src="${esc(picUrl(sha))}" alt="${esc(label)}">` : '<span class="empty">нет</span>'}</div>
      <div class="slot__body"><b>${label}</b><small>${esc(hint.charAt(0).toUpperCase() + hint.slice(1))}${pic ? ` · сейчас ${pic.width}×${pic.height}` : ''}</small>
        <span class="field__error" hidden></span>
        <div class="slot__acts">
          <label class="btn btn--secondary" for="file-${key}">${sha ? 'Заменить' : 'Загрузить'}</label>
          <input type="file" id="file-${key}" accept="image/png" data-upload="${key}">
          ${sha ? `<button class="btn btn--secondary" type="button" data-clear="${key}">Убрать</button>` : ''}
        </div></div></div>`;
  }).join('');
  $$('[data-upload]').forEach(input => input.addEventListener('change', () => upload(input)));
  $$('[data-clear]').forEach(btn => btn.addEventListener('click', () => {
    look[btn.dataset.clear] = null;
    renderSlots();
    changed({ now: true });
  }));
  // A label styled as a button takes the keyboard too.
  $$('.slot label.btn').forEach(l => {
    l.tabIndex = 0;
    l.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); $(`#${l.htmlFor}`).click(); } });
  });
}

async function upload(input) {
  const key = input.dataset.upload;
  const file = input.files && input.files[0];
  const slot = input.closest('.slot');
  const label = $('label.btn', slot);
  if (!file) return;
  const err = message => {
    const e = $('.field__error', slot);
    e.textContent = message;
    e.hidden = !message;
  };
  err('');
  if (file.type && file.type !== 'image/png') { err('Нужен файл PNG.'); return; }
  const max = data.slots?.[key]?.maxBytes || 1048576;
  if (file.size > max) { err(`Картинка ${(file.size / 1048576).toFixed(1).replace('.', ',')} МБ, а можно до 1 МБ. Сожмите её.`); return; }
  label.textContent = 'Загружаем…';
  try {
    const res = await api.upload(`/api/cabinet/upload?slot=${encodeURIComponent(key)}`, file);
    const pic = res.picture;
    data.pictures = data.pictures.filter(p => p.sha256 !== pic.sha256).concat(pic);
    look[key] = pic.sha256;
    if (key === 'backdrop' && look.background !== 'image') look.background = 'image';
    renderLookForm();
    changed({ now: true });
    toast('Картинка загружена');
  } catch (e) {
    label.textContent = look[key] ? 'Заменить' : 'Загрузить';
    err(e.fields?.file || e.fields?.slot || e.message);
  } finally {
    input.value = '';
  }
}

$('#look-name').addEventListener('input', e => { look.name = e.target.value; changed(); });
$('#link-cabinet').addEventListener('input', e => { look.links = { ...look.links, cabinet: e.target.value.trim() }; changed(); });
$('#link-support').addEventListener('input', e => { look.links = { ...look.links, support: e.target.value.trim() }; changed(); });
$$('input[name="background"]').forEach(r => r.addEventListener('change', () => { look.background = r.value; changed({ now: true }); }));

// ---------------------------------------------------------------- saving and the review

function changed({ now = false } = {}) {
  clearTimeout(saveTimer);
  $$('#saved, [data-saved]').forEach(el => { el.textContent = 'Сохраняем…'; });
  drawPreview();
  saveTimer = setTimeout(save, now ? 0 : 700);
}

function save() {
  clearTimeout(saveTimer);
  saving = saving.then(async () => {
    try {
      const res = await api.put('/api/cabinet/look', look);
      data = { ...res, slots: res.slots || data.slots };
      $$('#saved, [data-saved]').forEach(el => { el.textContent = 'Черновик сохранён'; });
      renderReview();
      renderOverview();
    } catch (e) {
      $$('#saved, [data-saved]').forEach(el => { el.textContent = e.code === 'offline' ? 'Нет связи: не сохранено' : 'Не сохранено'; });
      if (e.fields) showProblems(e.fields);
      else toast(e.message);
    }
  });
  return saving;
}

const PROBLEM_LABEL = {
  name: 'Название', brand: 'Цвета', colours: 'Цвета', logo: 'Логотип для тёмного фона', logoOnLight: 'Логотип для светлого фона', mark: 'Знак',
  backdrop: 'Фон', 'links.cabinet': 'Кабинет', 'links.support': 'Поддержка', api: 'Адрес API', login: 'Вход по коду', lock: 'Скрытые адреса', actions: 'Кнопки',
};

function showProblems(problems) {
  // Clear what was marked, then mark what the hub says now.
  $$('#look-form [aria-invalid], #api-tier2 [aria-invalid]').forEach(el => setFieldError(el, ''));
  $$('[data-error-for]').forEach(el => { el.hidden = true; el.textContent = ''; });
  $$('.slot .field__error').forEach(el => { if (!el.dataset.local) { el.hidden = true; el.textContent = ''; } });
  for (const [key, message] of Object.entries(problems)) {
    const input = fieldOf(key);
    if (input) { setFieldError(input, message); continue; }
    const slotErr = $(`[data-slot="${key}"] .field__error`);
    if (slotErr) { slotErr.textContent = message; slotErr.hidden = false; continue; }
    const m = /^actions\.(\d+)$/.exec(key);
    const actErr = m && $(`.act[data-i="${m[1]}"] .field__error`);
    if (actErr) { actErr.textContent = message; actErr.hidden = false; continue; }
    const forKey = $(`[data-error-for="${key === 'colours' ? 'brand' : key}"]`);
    if (forKey) { forKey.textContent = message; forKey.hidden = false; }
  }
  const list = Object.entries(problems);
  $('#problems').hidden = !list.length;
  $('#problems-list').innerHTML = list.map(([k, m]) => {
    const a = /^actions\.(\d+)$/.exec(k);
    return `<li>${esc(a ? `Кнопка ${Number(a[1]) + 1}` : PROBLEM_LABEL[k] || k)}: ${esc(m)}</li>`;
  }).join('');
}

function renderReview() {
  const r = data.review || { problems: {}, notes: [] };
  showProblems(r.problems || {});
  $('#notes-box').hidden = !(r.notes || []).length;
  $('#notes').innerHTML = (r.notes || []).map(n => `<li>${esc(n)}</li>`).join('');
  renderPublishBars();
  drawPreview();
}

function drawPreview() {
  if (!data || !look) return;
  const colours = data.review?.colours?.[mode] || null;
  // Until the hub has answered for the colours just typed, the window shows them as they are.
  const typed = look.brand[0];
  const exact = data.look.draft && same(data.look.draft.brand, look.brand) && data.look.draft.background === look.background;
  const c = exact ? colours : (typed ? { accent: typed, accentText: typed, onAccent: '#0B0B0D', stage: look.background === 'gradient' && colours?.stage ? { ...colours.stage, stops: look.brand.length > 1 ? look.brand : colours.stage.stops } : colours?.stage } : colours);
  $('#preview-light').innerHTML = windowHtml({
    root: ROOT, mode, name: look.name || data.provider.name, tab: true, colours: c,
    mark: picUrl(look.mark), logo: picUrl(look.logo), logoOnLight: picUrl(look.logoOnLight), backdrop: picUrl(look.backdrop),
    plan: { line: 'Личный доступ · 77 дней', traffic: '12,4 из 50 ГБ · до 31.01.2027' },
  });
}
$$('#modes button').forEach(b => {
  b.setAttribute('aria-pressed', String(b.dataset.mode === mode));
  b.addEventListener('click', () => {
    mode = b.dataset.mode;
    $$('#modes button').forEach(x => x.setAttribute('aria-pressed', String(x === b)));
    drawPreview();
  });
});

// ---------------------------------------------------------------- sending for publication

function publishState() {
  const p = data.provider;
  const draft = data.look.draft;
  const problems = Object.keys(data.review?.problems || {}).length;
  if (revoked()) return { text: 'Вкладка отозвана', sub: 'Оформление менять нельзя.', can: false };
  if (problems) return { text: 'Есть что исправить', sub: 'Отмеченное красным не даёт отправить оформление.', can: false, label: 'Отправить на публикацию' };
  if (p.queueState === 'queued' && same(draft, data.look.queued)) {
    return { text: `Ждёт проверки владельца V2pn${p.queuedAt ? ` с ${date(p.queuedAt, { time: true })}` : ''}`, sub: 'Люди пока видят прошлую версию.', can: false, label: 'Отправлено', state: 'queued' };
  }
  if (p.queueState === 'queued') return { text: 'Отправленная версия ждёт проверки', sub: 'После неё есть новые изменения: отправьте их, и в очереди будет новая версия.', can: true, label: 'Отправить заново' };
  if (p.queueState === 'returned') return { text: 'Владелец V2pn вернул оформление', sub: `«${p.returnReason || ''}» Исправьте и отправьте снова.`, can: true, label: 'Отправить снова', level: 'warn' };
  if (same(draft, data.look.published)) {
    return { text: p.publishedAt ? `Опубликовано ${date(p.publishedAt, { time: true })}` : 'Опубликовано', sub: 'Изменений нет. V2pn у ваших людей подхватывает новое в течение часа.', can: false, label: 'Отправить на публикацию', state: 'published' };
  }
  return { text: 'Есть изменения, которых люди ещё не видят', sub: 'Черновик сохранён. Отправьте его владельцу V2pn: после проверки он появится у людей.', can: true, label: 'Отправить на публикацию' };
}

function renderPublishBars() {
  const s = publishState();
  $$('[data-publish-bar]').forEach((bar, i) => {
    bar.innerHTML = `<div><b data-queue-state="${s.state || ''}">${esc(s.text)}</b><span>${esc(s.sub)}</span></div>
      ${s.label ? `<button class="btn btn--primary" type="button" ${i === 0 ? 'id="send-look"' : ''} data-send ${s.can ? '' : 'disabled'}>${esc(s.label)}</button>` : ''}`;
  });
  $$('[data-send]').forEach(btn => btn.addEventListener('click', () => sendLook(btn)));
}

async function sendLook(btn) {
  await busy(btn, async () => {
    try {
      await save();
      const res = await api.post('/api/cabinet/look/submit');
      data = { ...res, slots: res.slots || data.slots };
      renderReview();
      renderOverview();
      toast('Отправлено владельцу V2pn');
    } catch (e) {
      if (e.fields) showProblems(e.fields);
      toast(e.message);
    }
  }, 'Отправляем');
  renderPublishBars();
}

// ---------------------------------------------------------------- share

function renderShare() {
  const base = data.share.button;
  $('#share-base').textContent = base;
  $('#share-link').innerHTML = `${esc(base)}<mark>&lt;подписка человека&gt;</mark>`;
  $('#snippet').innerHTML = `<span class="c">// Bot API: sendMessage с inline-кнопкой</span>
<span class="k">const</span> url = <span class="s">'${esc(base)}'</span> + encodeURIComponent(subscriptionUrl);
reply_markup: { inline_keyboard: [[
  { text: <span class="s">'💻 Подключить в V2pn'</span>, url }
]] }`;
  wireCopy();
  const tryIn = $('#try-sub');
  const tryOpen = $('#try-open');
  tryIn.addEventListener('input', () => {
    const v = tryIn.value.trim();
    let ok = false;
    try { ok = new URL(v).protocol === 'https:'; } catch { ok = false; }
    tryOpen.href = ok ? base + encodeURIComponent(v) : '#';
    tryOpen.setAttribute('aria-disabled', String(!ok));
  });
}

// ---------------------------------------------------------------- integration

function renderApi() {
  const p = data.provider;
  const tier2 = p.tier >= 2;
  $('#api-tier1').hidden = tier2;
  $('#api-tier2').hidden = !tier2;
  if (!tier2) {
    const asked = Boolean(p.tierRequest);
    $('#tier-request').disabled = asked || revoked();
    $('#tier-request').textContent = asked ? 'Запрос отправлен' : 'Запросить ступень 2';
    $('#tier-request-state').textContent = asked ? 'Владелец V2pn ответит здесь: раздел откроется сам.' : '';
    return;
  }
  $('#api-url').value = look.api || '';
  $('#opt-login').checked = look.login === true;
  $('#opt-lock').checked = look.lock === true;
  renderActions();
}

$('#tier-request').addEventListener('click', async e => {
  await busy(e.currentTarget, async () => {
    try {
      const res = await api.post('/api/cabinet/tier-request');
      data.provider = { ...data.provider, ...res.provider };
      renderApi();
      renderOverview();
      toast('Запрос отправлен владельцу V2pn');
    } catch (err) {
      toast(err.message);
    }
  });
  renderApi();
});

$('#api-url').addEventListener('input', e => { look.api = e.target.value.trim(); changed(); });
$('#opt-login').addEventListener('change', e => { look.login = e.target.checked; changed({ now: true }); });
$('#opt-lock').addEventListener('change', e => { look.lock = e.target.checked; changed({ now: true }); });

$('#api-test').addEventListener('click', async e => {
  const out = $('#api-out');
  const value = $('#api-url').value.trim();
  if (!value) {
    setFieldError($('#api-url'), 'Напишите адрес API: https://ваш-домен/путь/.');
    $('#api-url').focus();
    return;
  }
  setFieldError($('#api-url'), '');
  out.innerHTML = '<div class="checking"><span class="spin" aria-hidden="true"></span><span>Сервер V2pn вызывает login/start и login/poll…</span></div>';
  await busy(e.currentTarget, async () => {
    try {
      const res = await api.post('/api/cabinet/login-test', { api: value }, { timeout: 30000 });
      const t = res.test;
      out.innerHTML = `<div class="stack stack--sm">${t.ok ? '<span class="badge badge--ok">API отвечает как надо</span>' : '<span class="badge badge--fail">API пока не готов</span>'}${checkHtml({ lines: t.lines })}
        ${t.ok ? '<p class="hint">Проверка не завершает вход: код ждёт подтверждения в боте и истечёт сам.</p>' : ''}</div>`;
    } catch (err) {
      if (err.fields?.api) setFieldError($('#api-url'), err.fields.api);
      out.innerHTML = `<p class="hint">${esc(err.code === 'rate_limited' ? 'Слишком много проверок за час. Попробуйте позже.' : err.message)}</p>`;
    }
  }, 'Проверяем');
});

function renderActions() {
  const box = $('#actions');
  box.innerHTML = look.actions.map((a, i) => {
    const need = (DOS.find(d => d[0] === a.do) || [])[2] || '';
    const param = PARAM[need];
    const paramField = param
      ? (param[1]
        ? `<select id="act-${i}-p" data-act="${i}" data-f="${need}">${param[1].map(([v, t]) => `<option value="${v}"${a[need] === v ? ' selected' : ''}>${t}</option>`).join('')}</select>`
        : `<input id="act-${i}-p" type="text" data-act="${i}" data-f="${need}" value="${esc(a[need] || '')}" spellcheck="false">`)
      : '<input type="text" disabled value="—" aria-label="Не нужно">';
    return `<div class="act" data-i="${i}">
      <div class="field"><label for="act-${i}-l">Подпись</label><input id="act-${i}-l" type="text" maxlength="24" data-act="${i}" data-f="label" value="${esc(a.label)}"></div>
      <div class="field"><label for="act-${i}-i">Значок</label><select id="act-${i}-i" data-act="${i}" data-f="icon">${ICONS.map(([v, t]) => `<option value="${v}"${a.icon === v ? ' selected' : ''}>${t}</option>`).join('')}</select></div>
      <div class="field"><label for="act-${i}-d">Что делает</label><select id="act-${i}-d" data-act="${i}" data-f="do">${DOS.map(([v, t]) => `<option value="${v}"${a.do === v ? ' selected' : ''}>${t}</option>`).join('')}</select></div>
      <div class="field"><label for="act-${i}-p">${param ? param[0] : 'Параметр'}</label>${paramField}</div>
      <button class="btn btn--secondary" type="button" data-remove-act="${i}" aria-label="Убрать кнопку ${i + 1}"><svg viewBox="0 0 16 16"><path d="M4 4l8 8M12 4l-8 8"/></svg></button>
      <span class="field__error" hidden></span></div>`;
  }).join('') || '<p class="hint">Кнопок пока нет. Без них во вкладке — «Подключиться», «Продлить» и «Поддержка».</p>';
  $('#add-action').hidden = look.actions.length >= 4;
  $$('[data-act]', box).forEach(el => el.addEventListener(el.tagName === 'SELECT' ? 'change' : 'input', () => {
    const a = look.actions[Number(el.dataset.act)];
    a[el.dataset.f] = el.value;
    if (el.dataset.f === 'do') {
      const need = (DOS.find(d => d[0] === el.value) || [])[2];
      if (need && PARAM[need][1] && !a[need]) a[need] = PARAM[need][1][0][0];
      renderActions();
      $(`#act-${el.dataset.act}-d`).focus();
    }
    changed();
  }));
  $$('[data-remove-act]', box).forEach(btn => btn.addEventListener('click', () => {
    look.actions.splice(Number(btn.dataset.removeAct), 1);
    renderActions();
    changed({ now: true });
  }));
}
$('#add-action').addEventListener('click', () => {
  if (look.actions.length >= 4) return;
  look.actions.push({ label: 'Самый быстрый', icon: 'bolt', do: 'connect.best', strategy: 'ping', purpose: '', server: '', group: '', url: '' });
  renderActions();
  $(`#act-${look.actions.length - 1}-l`).focus();
  changed({ now: true });
});

matchMedia('(prefers-color-scheme: dark)').addEventListener('change', e => {
  mode = e.matches ? 'dark' : 'light';
  $$('#modes button').forEach(x => x.setAttribute('aria-pressed', String(x.dataset.mode === mode)));
  drawPreview();
});

load();
