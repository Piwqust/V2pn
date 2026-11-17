// /providers/apply/: the provider's one application. A draft saves itself field by field, the test link is checked on
// the server line by line, the preview shows the subscription in V2pn now (a plain subscription) and with the
// provider's tab, and a sent application waits here for the owner's decision.
import { api, me, wireAccount } from './api.js';
import { $, $$, busy, checkCounts, checkHtml, date, esc, plural, prefersDark, setFieldError, toast, windowHtml } from './hubui.js';

const ROOT = '../../';
const RESERVED = new Set(['v2pn', 'v2rayn', 'admin', 'api', 'www', 'p', 'r', 'releases', 'partners', 'providers', 'assets', 'static', 'site', 'hub', 'root',
  'null', 'undefined', 'test', 'example', 'official', 'support', 'help', 'login', 'cabinet', 'download', 'downloads', 'install', 'update', 'updates', 'mail',
  'docs', 'guide', 'builder', 'status', 'security']);
const DOMAIN = /^(\*\.)?(?!-)[a-z0-9-]{1,63}(?<!-)(\.(?!-)[a-z0-9-]{1,63}(?<!-))*\.(?!-)[a-z0-9-]*[a-z][a-z0-9-]*(?<!-)$/;

const form = $('#form');
const F = {
  name: $('#name'), providerId: $('#providerId'), domains: $('#domains'), testUrl: $('#testUrl'), contact: $('#contact'), note: $('#note'),
};
const tierRadios = $$('input[name="wantedTier"]');
const submitBtn = $('#submit');
const checkBtn = $('#check');
const status = $('#status');
const saved = $('#saved');

let account = null;
let app = null;             // the application as the hub has it
let lastCheck = null;       // {key, result}: the latest check and what it was made for
let idTouched = false;      // the person wrote the id themselves: the name no longer suggests it
let saveTimer = null;
let previewView = 'plain';
let pollTimer = null;

// ---------------------------------------------------------------- the rules (the hub's, so a mistake shows at once)

const LATIN = { а: 'a', б: 'b', в: 'v', г: 'g', д: 'd', е: 'e', ё: 'e', ж: 'zh', з: 'z', и: 'i', й: 'y', к: 'k', л: 'l', м: 'm', н: 'n', о: 'o', п: 'p', р: 'r', с: 's', т: 't',
  у: 'u', ф: 'f', х: 'h', ц: 'ts', ч: 'ch', ш: 'sh', щ: 'sch', ъ: '', ы: 'y', ь: '', э: 'e', ю: 'yu', я: 'ya' };
const slug = s => s.toLowerCase().split('').map(c => LATIN[c] ?? c).join('').normalize('NFKD').replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 32).replace(/-+$/, '');

function domainsOf(text) {
  return text.split(/[\s,]+/).map(d => d.trim().replace(/\.$/, '').toLowerCase()).filter(Boolean);
}

const RULES = {
  name(v) {
    const t = v.trim();
    if (!t) return 'Напишите название: так V2pn подпишет вкладку.';
    if (t.length > 40) return 'Не длиннее 40 символов.';
    if (t.replace(/ /g, '').toLowerCase().includes('v2pn')) return 'Без слова «V2pn»: вкладка — ваша.';
    return null;
  },
  providerId(v) {
    const t = v.trim().toLowerCase();
    if (!t) return 'Нужен адрес страницы: латиница, цифры и дефис.';
    if (!/^[a-z0-9][a-z0-9-]{1,31}$/.test(t)) return 'От 2 до 32 латинских букв, цифр и дефисов, начиная с буквы или цифры.';
    if (RESERVED.has(t)) return 'Этот адрес занят самим V2pn. Выберите другой.';
    return null;
  },
  domains(v) {
    const list = domainsOf(v);
    if (!list.length) return 'Нужен хотя бы один домен подписки.';
    if (list.length > 20) return 'Не больше 20 доменов.';
    const bad = list.find(d => !DOMAIN.test(d) && !/[^\x00-\x7f]/.test(d));
    if (bad) return `«${bad.slice(0, 60)}» — не домен. Пишите sub.example.com или *.example.com, без https://.`;
    return null;
  },
  testUrl(v) {
    const t = v.trim();
    if (!t) return 'Нужна тестовая ссылка на подписку.';
    // Plain http only on a local copy of the site (hub/dev/serve.mjs); the hub itself takes https only.
    const local = location.protocol === 'http:';
    try {
      const u = new URL(t);
      if (u.protocol !== 'https:' && !(local && u.protocol === 'http:')) return 'Ссылка должна начинаться с https://.';
      if (u.username || u.password) return 'Без имени и пароля в ссылке.';
    } catch {
      return 'Это не ссылка. Она начинается с https://.';
    }
    if (t.length > 2048) return 'Ссылка длиннее 2048 символов.';
    const host = new URL(t).hostname.toLowerCase();
    const list = domainsOf(F.domains.value);
    if (list.length && !list.some(d => (d.startsWith('*.') ? host.endsWith(d.slice(1)) : host === d))) {
      return `Домен ссылки ${host} не входит в домены подписки. Добавьте его выше.`;
    }
    return null;
  },
  contact(v) {
    const t = v.trim();
    if (!t) return 'Как с вами связаться: Telegram или почта.';
    if (t.length > 200 || /\n/.test(t)) return 'До 200 символов в одну строку.';
    return null;
  },
  note(v) {
    return v.length > 2000 ? 'До 2000 символов.' : null;
  },
};

function validate(key, { show = true } = {}) {
  const why = RULES[key](F[key].value);
  if (show) setFieldError(F[key], why);
  return why;
}

function payload({ onlyValid }) {
  const out = {};
  for (const key of Object.keys(F)) {
    const value = F[key].value;
    if (onlyValid && (RULES[key](value) || (!value.trim() && key !== 'note'))) continue;
    out[key] = key === 'domains' ? domainsOf(value) : key === 'providerId' ? value.trim().toLowerCase() : value.trim();
  }
  out.wantedTier = Number(tierRadios.find(r => r.checked)?.value || 1);
  return out;
}

// ---------------------------------------------------------------- the draft saves itself

function scheduleSave() {
  clearTimeout(saveTimer);
  saved.textContent = '';
  saveTimer = setTimeout(saveDraft, 900);
}

async function saveDraft() {
  clearTimeout(saveTimer);
  const body = payload({ onlyValid: true });
  try {
    const res = await api.put('/api/application', body);
    app = res.application;
    saved.textContent = 'Черновик сохранён';
  } catch (e) {
    if (e.fields) {
      for (const [k, msg] of Object.entries(e.fields)) if (F[k]) setFieldError(F[k], msg);
      saved.textContent = '';
    } else {
      saved.textContent = e.code === 'offline' ? 'Нет связи: черновик не сохранён' : '';
    }
  }
}

// ---------------------------------------------------------------- the check

const checkKey = () => `${F.testUrl.value.trim()}|${domainsOf(F.domains.value).join(',')}`;

function showCheck(result, { into = $('#check-out'), badge = $('#check-badge') } = {}) {
  if (!result) return;
  const { fail, warn } = checkCounts(result);
  if (badge) {
    badge.innerHTML = fail ? `<span class="badge badge--fail">${fail} ${plural(fail, 'ошибка', 'ошибки', 'ошибок')}</span>`
      : warn ? `<span class="badge badge--warn">${warn} ${plural(warn, 'замечание', 'замечания', 'замечаний')}</span>`
        : '<span class="badge badge--ok">Всё в порядке</span>';
  }
  into.innerHTML = `${checkHtml(result)}${fail ? '<p class="hint">Пока есть красные строки, заявку не отправить: исправьте ссылку или домены и проверьте снова.</p>'
    : warn ? '<p class="hint">Жёлтые строки не мешают заявке: это то, что V2pn покажет лучше, если поправить.</p>' : ''}`;
}

function updateSubmit() {
  const stale = lastCheck && lastCheck.key !== checkKey();
  const failing = lastCheck && !stale && !lastCheck.result.ok;
  submitBtn.disabled = Boolean(failing);
  submitBtn.title = failing ? 'Ссылка не прошла проверку' : '';
  if (stale) $('#check-badge').innerHTML = '<span class="badge">Ссылка изменилась</span>';
}

async function runCheck() {
  const urlWhy = validate('testUrl');
  const domWhy = validate('domains');
  if (urlWhy || domWhy) {
    (urlWhy ? F.testUrl : F.domains).focus();
    return;
  }
  const key = checkKey();
  $('#check-badge').innerHTML = '';
  $('#check-out').innerHTML = '<div class="checking"><span class="spin" aria-hidden="true"></span><span>Сервер открывает ссылку и пингует серверы. Это до 15 секунд.</span></div>';
  await busy(checkBtn, async () => {
    try {
      const res = await api.post('/api/check', { url: F.testUrl.value.trim(), domains: domainsOf(F.domains.value) }, { timeout: 40000 });
      lastCheck = { key, result: res.check };
      showCheck(res.check);
      progress('check');
      drawPreview();
      $('#check-h').focus?.();
    } catch (e) {
      if (e.fields?.url) setFieldError(F.testUrl, e.fields.url);
      $('#check-out').innerHTML = `<div class="note note--fail"><svg viewBox="0 0 20 20" aria-hidden="true"><circle cx="10" cy="10" r="8" fill="none" stroke="currentColor" stroke-width="1.6"/><path d="M10 5.8v5.2M10 13.8v.3" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg><p>${esc(e.code === 'rate_limited' ? 'Слишком много проверок за час. Подождите немного и проверьте снова.' : e.message)}</p></div>`;
    }
  }, 'Проверяем');
  updateSubmit();
}

// ---------------------------------------------------------------- the preview

function drawPreview() {
  const r = lastCheck?.result;
  const title = r?.headers?.title || F.name.value.trim() || 'Ваша подписка';
  const total = r?.servers?.total;
  const u = r?.headers?.userInfo;
  const gb = b => { const v = b / 1024 ** 3; return v >= 10 ? Math.round(v) : v.toFixed(1).replace('.', ',').replace(/,0$/, ''); };
  const traffic = u ? `${gb(u.upload + u.download)}${u.total ? ` из ${gb(u.total)}` : ''} ГБ${u.expire ? ` · до ${new Date(u.expire * 1000).toLocaleDateString('ru-RU')}` : ''}` : null;
  const plan = { servers: total ? `${total} ${plural(total, 'сервер', 'сервера', 'серверов')}` : null, traffic, line: 'Ваш тариф · дни и трафик из заголовков' };
  const tab = previewView === 'tab';
  $('#preview').innerHTML = windowHtml({ root: ROOT, mode: prefersDark() ? 'dark' : 'light', name: tab ? (F.name.value.trim() || title) : title, tab, plan });
  $('#pv-note').textContent = tab
    ? 'Так будет у людей, которые пришли от вас. Цвета, логотип и фон настроите в кабинете после одобрения.'
    : 'Сейчас, без заявки, ваша ссылка уже работает в V2pn как обычная подписка: название, трафик и срок — из заголовков.';
}

$$('.seg [data-view]').forEach(b => b.addEventListener('click', () => {
  previewView = b.dataset.view;
  $$('.seg [data-view]').forEach(x => x.setAttribute('aria-pressed', String(x === b)));
  drawPreview();
}));
matchMedia('(prefers-color-scheme: dark)').addEventListener('change', drawPreview);

// ---------------------------------------------------------------- the views

function show(view) {
  for (const v of ['loading', 'error', 'form', 'wait']) $(`#view-${v}`).hidden = v !== view;
}

function progress(step) {
  const order = ['apply', 'check', 'review', 'cabinet'];
  const at = order.indexOf(step);
  $$('.progress [data-step]').forEach(li => {
    const i = order.indexOf(li.dataset.step);
    li.classList.toggle('is-done', i < at);
    li.classList.toggle('is-now', i === at);
    if (i === at) li.setAttribute('aria-current', 'step'); else li.removeAttribute('aria-current');
  });
}

function fillForm(a) {
  if (!a) return;
  F.name.value = a.name || '';
  F.providerId.value = a.providerId || '';
  idTouched = Boolean(a.providerId);
  F.domains.value = (a.domains || []).join('\n');
  F.testUrl.value = a.testUrl || '';
  F.contact.value = a.contact || '';
  F.note.value = a.note || '';
  tierRadios.forEach(r => { r.checked = Number(r.value) === (a.wantedTier || 1); });
  if (a.check) {
    lastCheck = { key: `${a.testUrl}|${(a.domains || []).join(',')}`, result: a.check };
    showCheck(a.check);
  }
  const rejected = a.state === 'rejected';
  $('#rejected').hidden = !rejected;
  if (rejected) $('[data-reason]').textContent = a.rejectReason ? `Причина: «${a.rejectReason}».` : '';
}

function showWait(a) {
  show('wait');
  progress('review');
  const checking = a.state === 'checking';
  $('#wait-badge').innerHTML = checking ? '<span class="badge badge--info">Проверяем ссылку</span>' : '<span class="badge badge--info">На рассмотрении</span>';
  $('#wait-h').textContent = checking ? 'Проверяем ссылку' : 'Заявка у владельца V2pn';
  $('#wait-text').textContent = checking
    ? 'Сервер открывает тестовую ссылку. Страница обновится сама.'
    : `Отправлена ${date(a.submittedAt, { time: true })}. Обычно решение за один-два дня; оно появится здесь, страница проверяет сама.`;
  const tier = a.wantedTier === 2 ? 'Интеграция (ступень 2)' : 'Проверенный (ступень 1)';
  $('#wait-kv').innerHTML = `<dt>Название</dt><dd>${esc(a.name)}</dd><dt>Страница</dt><dd>/p/${esc(a.providerId)}/</dd>
    <dt>Домены</dt><dd>${a.domains.map(esc).join(', ')}</dd><dt>Контакт</dt><dd>${esc(a.contact)}</dd><dt>Хотите</dt><dd>${tier}</dd>`;
  $('#wait-checked').textContent = a.checkedAt ? date(a.checkedAt, { time: true }) : '';
  $('#wait-check').innerHTML = a.check ? checkHtml(a.check) : '<div class="checking"><span class="spin" aria-hidden="true"></span><span>Проверяем…</span></div>';
  clearInterval(pollTimer);
  pollTimer = setInterval(poll, checking ? 3000 : 30000);
}

async function poll() {
  if (document.hidden) return;
  try {
    const data = await api.get('/api/me');
    if (data.provider) { location.replace(`${ROOT}providers/cabinet/`); return; }
    const a = data.application;
    if (!a || a.state !== app?.state) { app = a; route(); return; }
  } catch { /* the next tick tries again */ }
}

function route() {
  clearInterval(pollTimer);
  if (!app || app.state === 'draft' || app.state === 'rejected') {
    show('form');
    progress(lastCheck ? 'check' : 'apply');
    fillForm(app);
    drawPreview();
    updateSubmit();
    return;
  }
  if (app.state === 'approved') { location.replace(`${ROOT}providers/cabinet/`); return; }
  showWait(app);
}

async function load() {
  show('loading');
  try {
    const data = await me(ROOT);
    account = data.account;
    wireAccount(ROOT, account);
    if (data.provider) { location.replace(`${ROOT}providers/cabinet/`); return; }
    app = data.application;
    route();
  } catch (e) {
    show('error');
    $('[data-error-text]').textContent = e.message;
  }
}
$('[data-retry]').addEventListener('click', load);

// ---------------------------------------------------------------- the person types

for (const [key, input] of Object.entries(F)) {
  input.addEventListener('input', () => {
    if (input.getAttribute('aria-invalid') === 'true') validate(key);
    if (status.textContent && !$$('#form [aria-invalid="true"]').length) status.textContent = '';
    if (key === 'name' && !idTouched) {
      F.providerId.value = slug(input.value);
      if (F.providerId.getAttribute('aria-invalid') === 'true') validate('providerId');
    }
    if (key === 'providerId') idTouched = input.value.trim() !== '';
    if (key === 'name' || key === 'testUrl' || key === 'domains') { updateSubmit(); if (key === 'name') drawPreview(); }
    scheduleSave();
  });
  input.addEventListener('blur', () => {
    if (input.value.trim() || input.getAttribute('aria-invalid') === 'true') validate(key);
    if (key === 'domains' && F.testUrl.value.trim()) validate('testUrl');
  });
}
tierRadios.forEach(r => r.addEventListener('change', scheduleSave));
checkBtn.addEventListener('click', runCheck);
F.testUrl.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); runCheck(); } });

form.addEventListener('submit', async e => {
  e.preventDefault();
  const bad = Object.keys(F).filter(k => validate(k));
  if (bad.length) {
    status.textContent = bad.length === 1 ? 'Исправьте одно поле.' : `Исправьте поля: ${bad.length}.`;
    F[bad[0]].focus();
    return;
  }
  status.textContent = '';
  await busy(submitBtn, async () => {
    try {
      clearTimeout(saveTimer);
      await api.put('/api/application', payload({ onlyValid: false }));
      $('#check-out').innerHTML = '<div class="checking"><span class="spin" aria-hidden="true"></span><span>Проверяем ссылку перед отправкой…</span></div>';
      const res = await api.post('/api/application/submit', {}, { timeout: 45000 });
      app = res.application;
      toast('Заявка отправлена');
      route();
      window.scrollTo({ top: 0 });
    } catch (err) {
      if (err.code === 'check_failed' && err.detail) {
        lastCheck = { key: checkKey(), result: err.detail };
        showCheck(err.detail);
        updateSubmit();
        status.textContent = 'Ссылка не прошла проверку. Исправьте отмеченное красным справа и проверьте снова.';
      } else if (err.fields) {
        let n = 0;
        for (const [k, msg] of Object.entries(err.fields)) if (F[k]) { setFieldError(F[k], msg); n++; }
        status.textContent = n ? err.message : err.message;
        const first = Object.keys(err.fields).find(k => F[k]);
        if (first) F[first].focus();
        if (lastCheck) showCheck(lastCheck.result);
      } else {
        status.textContent = err.message;
        if (lastCheck) showCheck(lastCheck.result); else $('#check-out').innerHTML = '';
      }
    }
  }, 'Отправляем');
});

load();
