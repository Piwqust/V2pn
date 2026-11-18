// /admin/: the owner's decisions. Applications with their checks (approve with a tier and a term, or return with a
// reason), looks waiting for publication (with V2pn's window in light and dark), providers (tier, term, revoke and
// restore), downloads by day and platform, the sign-in codes while there is no mail, and the audit log.
import { api, me, wireAccount } from './api.js';
import { $, $$, busy, checkCounts, checkHtml, confirmStep, copyText, date, esc, plural, toast, windowHtml } from './hubui.js';

const ROOT = '../';
const VIEWS = ['overview', 'applications', 'queue', 'providers', 'downloads', 'outbox', 'audit'];
let appState = 'submitted';
let days = 30;
let refresh = null;
let overview = null;

// ---------------------------------------------------------------- the frame

function showState(state, title, message) {
  $('#view-loading').hidden = state !== 'loading';
  $('#view-error').hidden = state !== 'error';
  $('#shell').hidden = state !== 'ready';
  if (title) $('[data-error-title]').textContent = title;
  if (message) $('[data-error-text]').textContent = message;
}

const currentView = () => (VIEWS.includes(location.hash.slice(1)) ? location.hash.slice(1) : 'overview');

async function route() {
  const view = currentView();
  for (const v of VIEWS) $(`[data-view="${v}"]`).hidden = v !== view;
  $$('.sidenav [data-tab]').forEach(a => { if (a.dataset.tab === view) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current'); });
  clearInterval(refresh);
  const load = { overview: loadOverview, applications: loadApplications, queue: loadQueue, providers: loadProviders, downloads: loadDownloads, outbox: loadOutbox, audit: loadAudit }[view];
  await load();
  // The codes live ten minutes: while they can be on screen, the page keeps them fresh.
  if (view === 'overview' || view === 'outbox') refresh = setInterval(() => { if (!document.hidden) (view === 'overview' ? loadOverview : loadOutbox)({ quiet: true }); }, 10000);
}
window.addEventListener('hashchange', () => { route(); window.scrollTo({ top: 0 }); });

function failed(box, e) {
  box.innerHTML = `<div class="state"><h3>Не загрузилось</h3><p>${esc(e.message)}</p><button class="btn btn--secondary" type="button" data-again>Попробовать ещё раз</button></div>`;
  $('[data-again]', box).addEventListener('click', route);
}
const loading = box => { box.innerHTML = '<div class="skel" aria-busy="true"><i></i><i></i><i></i></div>'; };
const empty = (title, text) => `<div class="state"><h3>${esc(title)}</h3>${text ? `<p>${esc(text)}</p>` : ''}</div>`;

function counts(o) {
  const n = (list, state) => (list.find(x => x.state === state) || {}).n || 0;
  const set = (key, value) => { const el = $(`[data-count="${key}"]`); el.textContent = value; el.hidden = !value; };
  set('applications', n(o.applications, 'submitted'));
  set('queue', o.queue);
  set('providers', n(o.providers, 'active'));
}

// ---------------------------------------------------------------- overview

async function loadOverview({ quiet = false } = {}) {
  const box = $('#ov-counts');
  if (!quiet) loading(box);
  try {
    const [o, out] = await Promise.all([api.get('/api/admin/overview'), api.get('/api/admin/outbox')]);
    overview = o;
    counts(o);
    setOutboxCount(out.outbox.length);
    const n = (list, state) => (list.find(x => x.state === state) || {}).n || 0;
    box.innerHTML = `<a href="#applications"><b>${n(o.applications, 'submitted')}</b><span>${plural(n(o.applications, 'submitted'), 'новая заявка', 'новые заявки', 'новых заявок')}</span></a>
      <a href="#queue"><b>${o.queue}</b><span>ждут публикации</span></a>
      <a href="#providers"><b>${o.tierRequests}</b><span>${plural(o.tierRequests, 'просит', 'просят', 'просят')} ступень 2</span></a>
      <a href="#providers"><b>${n(o.providers, 'active')}</b><span>${plural(n(o.providers, 'active'), 'провайдер работает', 'провайдера работают', 'провайдеров работают')}</span></a>`;
    const pub = o.publication;
    $('#ov-publication').innerHTML = `<div class="box__head"><h2>Публикация</h2>${pub.error ? '<span class="badge badge--fail">Ошибка</span>' : pub.last ? '<span class="badge badge--ok">В порядке</span>' : '<span class="badge">Ещё не было</span>'}</div>
      ${pub.last ? `<p>Последняя ${esc(date(pub.last.at, { time: true }))}: ${pub.last.partners.length ? pub.last.partners.map(esc).join(', ') : 'ни одного провайдера'}. Подпись — ключом сервера, файлы в <code>/partners/latest/download/</code>.</p>` : '<p>Индекс провайдеров ещё не собирался.</p>'}
      ${pub.error ? `<div class="note note--fail"><svg viewBox="0 0 20 20" aria-hidden="true"><circle cx="10" cy="10" r="8" fill="none" stroke="currentColor" stroke-width="1.6"/><path d="M10 5.8v5.2M10 13.8v.3" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg><div><p><b>${esc(date(pub.error.at, { time: true }))}: публикация не прошла.</b></p><ul class="problems">${pub.error.problems.map(p => `<li>${esc(p)}</li>`).join('')}</ul></div></div>` : ''}
      <p class="hint">Почта: ${o.mail === 'smtp' ? 'письма уходят (SMTP)' : 'не настроена — коды входа ждут здесь, в «Кодах входа»'}.</p>`;
    $('#ov-outbox').hidden = o.mail === 'smtp' || !out.outbox.length;
    drawOutbox(out.outbox, $('#ov-outbox [data-outbox-list]'));
  } catch (e) {
    if (!quiet) failed(box, e);
  }
}

// ---------------------------------------------------------------- outbox

function setOutboxCount(n) {
  const el = $('[data-count="outbox"]');
  el.textContent = n;
  el.hidden = !n;
}

function drawOutbox(list, box) {
  box.innerHTML = list.length ? list.map(o => {
    const code = (/\b(\d{6})\b/.exec(o.subject) || /\b(\d{6})\b/.exec(o.body) || [])[1] || '';
    const left = Math.max(0, Math.round((new Date(o.expiresAt) - Date.now()) / 60000));
    return `<div><span><b>${esc(o.email)}</b><small>${esc(date(o.createdAt, { time: true }))} · ещё ${left} ${plural(left, 'минута', 'минуты', 'минут')}</small></span>
      <span class="code" aria-label="Код ${code.split('').join(' ')}">${esc(code.replace(/(\d{3})(\d{3})/, '$1 $2'))}</span>
      <button class="btn btn--secondary btn--sm" type="button" data-copy-code="${esc(code)}">Копировать</button></div>`;
  }).join('') : empty('Кодов нет', 'Когда кто-то запросит вход, код появится здесь на 10 минут.');
  $$('[data-copy-code]', box).forEach(b => b.addEventListener('click', async () => toast((await copyText(b.dataset.copyCode)) ? 'Код скопирован' : 'Не скопировалось')));
}

async function loadOutbox({ quiet = false } = {}) {
  const box = $('[data-view="outbox"] [data-outbox-list]');
  if (!quiet) loading(box);
  try {
    const [out, h] = await Promise.all([api.get('/api/admin/outbox'), api.get('/api/health')]);
    $('#out-mode').textContent = h.mail === 'smtp' ? 'Почта настроена: коды уходят письмами' : 'Почта не настроена: передайте код человеку сами';
    setOutboxCount(out.outbox.length);
    drawOutbox(out.outbox, box);
  } catch (e) {
    if (!quiet) failed(box, e);
  }
}

// ---------------------------------------------------------------- applications

const yearFromNow = () => { const d = new Date(); d.setFullYear(d.getFullYear() + 1); return d.toISOString().slice(0, 10); };
const STATE = { draft: ['', 'Черновик'], checking: ['info', 'Проверяется'], submitted: ['info', 'Ждёт решения'], approved: ['ok', 'Одобрена'], rejected: ['warn', 'Отклонена'] };

function appItem(a) {
  const { fail, warn } = checkCounts(a.check);
  const [lvl, label] = STATE[a.state] || ['', a.state];
  const checkBadge = a.check ? (fail ? `<span class="badge badge--fail">${fail} ${plural(fail, 'ошибка', 'ошибки', 'ошибок')}</span>` : warn ? `<span class="badge badge--warn">${warn} ${plural(warn, 'замечание', 'замечания', 'замечаний')}</span>` : '<span class="badge badge--ok">Проверка чистая</span>') : '';
  const servers = a.check?.servers?.total;
  return `<article class="item" data-app-id="${a.id}" aria-labelledby="app-${a.id}-h">
    <div class="item__head"><h3 id="app-${a.id}-h">${esc(a.name || 'Без названия')} <span class="hint">/p/${esc(a.providerId || '—')}/</span></h3>
      <span class="badge${lvl ? ` badge--${lvl}` : ''}">${label}</span><span class="badge">Хочет: ${a.wantedTier === 2 ? 'ступень 2' : 'ступень 1'}</span>${checkBadge}</div>
    <div class="item__body">
      <div class="stack">
        <dl class="kv">
          <dt>Почта</dt><dd>${esc(a.email)}</dd>
          <dt>Контакт</dt><dd>${esc(a.contact || '—')}</dd>
          <dt>Домены</dt><dd>${(a.domains || []).map(esc).join(', ') || '—'}</dd>
          <dt>Тестовая ссылка</dt><dd><code>${esc(a.testUrl || '—')}</code></dd>
          ${servers ? `<dt>Серверы</dt><dd>${servers}</dd>` : ''}
          ${a.note ? `<dt>Заметка</dt><dd>${esc(a.note)}</dd>` : ''}
          <dt>Отправлена</dt><dd>${esc(date(a.submittedAt || a.updatedAt, { time: true }))}</dd>
          ${a.rejectReason ? `<dt>Причина отказа</dt><dd>${esc(a.rejectReason)}</dd>` : ''}
        </dl>
        ${a.state === 'submitted' ? `<div class="decide">
          <div class="row">
            <div class="field"><label for="tier-${a.id}">Ступень</label><select id="tier-${a.id}"><option value="1"${a.wantedTier === 1 ? ' selected' : ''}>1 — Проверенный</option><option value="2"${a.wantedTier === 2 ? ' selected' : ''}>2 — Интеграция</option></select></div>
            <div class="field"><label for="until-${a.id}">До</label><input id="until-${a.id}" type="date" value="${yearFromNow()}"></div>
          </div>
          <div class="row"><button class="btn btn--primary" type="button" data-approve="${a.id}">Одобрить</button><button class="btn btn--secondary" type="button" data-reject="${a.id}">Отклонить</button></div>
        </div>` : ''}
      </div>
      <div class="stack stack--sm">${a.check ? `<span class="hint">Проверка ${esc(date(a.checkedAt, { time: true }))}</span>${checkHtml(a.check)}` : '<p class="hint">Проверки ещё не было.</p>'}</div>
    </div></article>`;
}

async function loadApplications() {
  const box = $('#apps-list');
  loading(box);
  try {
    const { applications } = await api.get(`/api/admin/applications?state=${appState}`);
    box.innerHTML = applications.length ? applications.map(appItem).join('')
      : empty({ submitted: 'Новых заявок нет', rejected: 'Отклонённых нет', approved: 'Одобренных нет', draft: 'Черновиков нет' }[appState], appState === 'submitted' ? 'Когда провайдер отправит заявку, она появится здесь с результатом проверки.' : '');
    $$('[data-approve]', box).forEach(b => b.addEventListener('click', () => approve(b)));
    $$('[data-reject]', box).forEach(b => b.addEventListener('click', () => reject(b)));
  } catch (e) {
    failed(box, e);
  }
}
$$('#app-filter button').forEach(b => b.addEventListener('click', () => {
  appState = b.dataset.state;
  $$('#app-filter button').forEach(x => x.setAttribute('aria-pressed', String(x === b)));
  loadApplications();
}));

async function approve(btn) {
  const id = btn.dataset.approve;
  const item = btn.closest('[data-app-id]');
  const name = $('h3', item).firstChild.textContent.trim();
  const tier = Number($(`#tier-${id}`).value);
  const until = $(`#until-${id}`).value;
  const ok = await confirmStep({
    title: `Одобрить ${name}?`,
    text: `Ступень ${tier} до ${esc(date(until))}. Сервер подпишет индекс и опубликует страницу провайдера сразу; провайдер получит кабинет.`,
    action: 'Одобрить',
  });
  if (!ok) return;
  await busy(btn, async () => {
    try {
      await api.post(`/api/admin/applications/${id}/approve`, { tier, until: until || undefined });
      toast(`${name}: одобрено и опубликовано`);
      loadApplications();
      refreshCounts();
    } catch (e) {
      toast(e.fields ? Object.values(e.fields)[0] : e.message);
    }
  });
}

async function reject(btn) {
  const id = btn.dataset.reject;
  const name = $(`[data-app-id="${id}"] h3`).firstChild.textContent.trim();
  const reason = await confirmStep({
    title: `Отклонить заявку ${name}?`,
    text: 'Провайдер увидит причину у себя в заявке, исправит и отправит снова.',
    action: 'Отклонить',
    danger: true,
    field: { label: 'Причина', multiline: true, required: true, empty: 'Напишите, что исправить: провайдер увидит эти слова.' },
  });
  if (!reason) return;
  try {
    await api.post(`/api/admin/applications/${id}/reject`, { reason });
    toast(`${name}: заявка возвращена с причиной`);
    loadApplications();
    refreshCounts();
  } catch (e) {
    toast(e.message);
  }
}

async function refreshCounts() {
  try { counts(await api.get('/api/admin/overview')); } catch { /* the badges wait for the next view */ }
}

// ---------------------------------------------------------------- the queue

const adminPic = sha => (sha ? `/api/admin/pictures/${sha}` : null);

function queueItem({ provider: p, look, review }) {
  const problems = Object.entries(review.problems || {});
  const win = mode => windowHtml({ root: ROOT, mode, name: look.name, tab: true, colours: review.colours?.[mode] || null,
    mark: adminPic(look.mark), logo: adminPic(look.logo), logoOnLight: adminPic(look.logoOnLight), backdrop: adminPic(look.backdrop) });
  const bg = { none: '«Акцент» (карта)', gradient: '«Сцена», градиент', image: '«Сцена», картинка' }[look.background] || look.background;
  return `<article class="item" data-queue-id="${esc(p.id)}" aria-labelledby="q-${esc(p.id)}-h">
    <div class="item__head"><h3 id="q-${esc(p.id)}-h">${esc(look.name)} <span class="hint">/p/${esc(p.id)}/</span></h3>
      <span class="badge">Ступень ${p.tier}</span>${problems.length ? `<span class="badge badge--fail">${problems.length} ${plural(problems.length, 'проблема', 'проблемы', 'проблем')}</span>` : '<span class="badge badge--ok">Контраст в норме</span>'}</div>
    <div class="mini--pair"><div class="mini">${win('light')}</div><div class="mini">${win('dark')}</div></div>
    <dl class="kv"><dt>Отправлено</dt><dd>${esc(date(p.queuedAt, { time: true }))}</dd><dt>Цвета</dt><dd>${look.brand.map(h => `<code>${esc(h)}</code>`).join(' ') || '—'}</dd>
      <dt>Фон</dt><dd>${esc(bg)}</dd>${look.links.cabinet || look.links.support ? `<dt>Ссылки</dt><dd>${[look.links.cabinet, look.links.support].filter(Boolean).map(esc).join('<br>')}</dd>` : ''}
      ${look.api ? `<dt>API</dt><dd><code>${esc(look.api)}</code>${look.login ? ', вход по коду' : ''}${look.lock ? ', скрытые адреса' : ''}</dd>` : ''}
      ${look.actions.length ? `<dt>Кнопки</dt><dd>${look.actions.map(a => esc(a.label)).join(', ')}</dd>` : ''}</dl>
    ${(review.notes || []).length ? `<ul class="notes-list">${review.notes.map(n => `<li>${esc(n)}</li>`).join('')}</ul>` : ''}
    ${problems.length ? `<ul class="problems">${problems.map(([k, m]) => `<li>${esc(k)}: ${esc(m)}</li>`).join('')}</ul>` : ''}
    <div class="row"><button class="btn btn--primary" type="button" data-publish="${esc(p.id)}"${problems.length ? ' disabled' : ''}>Опубликовать</button><button class="btn btn--secondary" type="button" data-return="${esc(p.id)}">Вернуть</button></div>
  </article>`;
}

async function loadQueue() {
  const box = $('#queue-list');
  loading(box);
  try {
    const { queue } = await api.get('/api/admin/queue');
    box.innerHTML = queue.length ? queue.map(queueItem).join('') : empty('Ничего не ждёт', 'Когда провайдер отправит оформление, оно появится здесь с окном V2pn в светлой и тёмной теме.');
    $$('[data-publish]', box).forEach(b => b.addEventListener('click', async () => {
      const id = b.dataset.publish;
      const ok = await confirmStep({ title: `Опубликовать оформление ${id}?`, text: 'Сервер подпишет индекс; V2pn у людей провайдера подхватит новое в течение часа.', action: 'Опубликовать' });
      if (!ok) return;
      await busy(b, async () => {
        try {
          await api.post(`/api/admin/queue/${id}/approve`);
          toast('Опубликовано');
          loadQueue();
          refreshCounts();
        } catch (e) { toast(e.message); }
      });
    }));
    $$('[data-return]', box).forEach(b => b.addEventListener('click', async () => {
      const id = b.dataset.return;
      const reason = await confirmStep({ title: `Вернуть оформление ${id}?`, text: 'Люди продолжат видеть опубликованную версию. Провайдер увидит, что поправить.', action: 'Вернуть',
        field: { label: 'Что поправить', multiline: true, required: true, empty: 'Напишите, что поправить.' } });
      if (!reason) return;
      try {
        await api.post(`/api/admin/queue/${id}/return`, { reason });
        toast('Возвращено провайдеру');
        loadQueue();
        refreshCounts();
      } catch (e) { toast(e.message); }
    }));
  } catch (e) {
    failed(box, e);
  }
}

// ---------------------------------------------------------------- providers

async function loadProviders() {
  const box = $('#prov-list');
  loading(box);
  try {
    const { providers } = await api.get('/api/admin/providers');
    if (!providers.length) { box.innerHTML = empty('Провайдеров пока нет', 'Одобренная заявка становится провайдером.'); return; }
    box.innerHTML = `<div class="tbl-wrap"><table class="tbl tbl--stack"><thead><tr><th>Провайдер</th><th>Ступень</th><th>Срок</th><th>Состояние</th><th>Связь</th><th></th></tr></thead><tbody>
      ${providers.map(p => `<tr data-provider-id="${esc(p.id)}">
        <td data-label="Провайдер"><b>${esc(p.name)}</b><br><a href="${esc(p.page)}" target="_blank" rel="noopener">/p/${esc(p.id)}/</a>${p.source === 'import' ? ' <span class="hint">· перенесён</span>' : ''}</td>
        <td data-label="Ступень"><select aria-label="Ступень ${esc(p.name)}" data-tier="${esc(p.id)}"><option value="1"${p.tier === 1 ? ' selected' : ''}>1 — Проверенный</option><option value="2"${p.tier === 2 ? ' selected' : ''}>2 — Интеграция</option></select>
          ${p.tierRequest ? '<br><span class="badge badge--info">просит ступень 2</span>' : ''}</td>
        <td data-label="Срок"><div class="row" style="gap: 6px; flex-wrap: nowrap"><input type="date" value="${esc(p.until)}" aria-label="Срок ${esc(p.name)}" data-until="${esc(p.id)}" style="height: 36px; padding: 0 8px; border: 1px solid var(--line-strong); border-radius: var(--r-sm); background: var(--bg); color: var(--ink); font: inherit">
          <button class="btn btn--secondary btn--sm" type="button" data-save-until="${esc(p.id)}" hidden>Сохранить</button></div>${p.expired ? '<span class="badge badge--fail">истёк</span>' : ''}</td>
        <td data-label="Состояние">${p.state === 'revoked' ? `<span class="badge badge--fail">Отозван</span>${p.revokeReason ? `<br><span class="hint">${esc(p.revokeReason)}</span>` : ''}` : '<span class="badge badge--ok">Работает</span>'}
          ${p.queueState === 'queued' ? '<br><a href="#queue" class="hint">оформление ждёт</a>' : ''}</td>
        <td data-label="Связь">${esc(p.email || '—')}<br><span class="hint">${esc(p.contact || '')}</span></td>
        <td>${p.state === 'revoked' ? `<button class="btn btn--secondary btn--sm" type="button" data-restore="${esc(p.id)}">Вернуть</button>` : `<button class="btn btn--danger btn--sm" type="button" data-revoke="${esc(p.id)}">Отозвать</button>`}</td>
      </tr>`).join('')}</tbody></table></div>`;
    $$('[data-tier]', box).forEach(sel => {
      const before = sel.value;
      sel.addEventListener('change', async () => {
        const id = sel.dataset.tier;
        const ok = await confirmStep({ title: `Ступень ${sel.value} для ${id}?`, text: Number(sel.value) === 2 ? 'Провайдер получит вход по коду, свои кнопки и скрытые адреса.' : 'Вход по коду и свои кнопки пропадут из его вкладки после публикации.', action: 'Поменять', danger: Number(sel.value) === 1 });
        if (!ok) { sel.value = before; return; }
        try { await api.post(`/api/admin/providers/${id}/tier`, { tier: Number(sel.value) }); toast('Ступень изменена'); loadProviders(); } catch (e) { toast(e.message); sel.value = before; }
      });
    });
    $$('[data-until]', box).forEach(input => {
      const save = $(`[data-save-until="${input.dataset.until}"]`, box);
      input.addEventListener('input', () => { save.hidden = false; });
      save.addEventListener('click', async () => {
        try { await api.post(`/api/admin/providers/${input.dataset.until}/until`, { until: input.value }); toast('Срок сохранён'); loadProviders(); } catch (e) { toast(e.fields?.until || e.message); }
      });
    });
    $$('[data-revoke]', box).forEach(b => b.addEventListener('click', async () => {
      const id = b.dataset.revoke;
      const reason = await confirmStep({ title: `Отозвать ${id}?`, text: 'Вкладка пропадёт у людей при следующем обновлении индекса, страница /p/ уйдёт. Подписка провайдера останется обычной. Вернуть можно в любой момент.', action: 'Отозвать', danger: true,
        field: { label: 'Причина', multiline: true, required: true, empty: 'Напишите причину: провайдер увидит её в кабинете.' } });
      if (!reason) return;
      try { await api.post(`/api/admin/providers/${id}/revoke`, { reason }); toast('Отозван'); loadProviders(); } catch (e) { toast(e.message); }
    }));
    $$('[data-restore]', box).forEach(b => b.addEventListener('click', async () => {
      const id = b.dataset.restore;
      const ok = await confirmStep({ title: `Вернуть ${id}?`, text: 'Вкладка и страница снова опубликуются.', action: 'Вернуть' });
      if (!ok) return;
      try { await api.post(`/api/admin/providers/${id}/restore`); toast('Возвращён'); loadProviders(); } catch (e) { toast(e.message); }
    }));
  } catch (e) {
    failed(box, e);
  }
}
$('#publish-now').addEventListener('click', async e => {
  const ok = await confirmStep({ title: 'Опубликовать заново?', text: 'Сервер соберёт и подпишет индекс, пакеты и страницы всех провайдеров.', action: 'Опубликовать' });
  if (!ok) return;
  await busy(e.currentTarget, async () => {
    try { const r = await api.post('/api/admin/publish'); toast(`Опубликовано: ${r.publication.partners.length}`); } catch (err) { toast(err.detail ? `${err.message} ${[].concat(err.detail)[0]}` : err.message); }
  });
});

// ---------------------------------------------------------------- downloads: stacked bars by day, three groups

const GROUPS = [
  { key: 'win', name: 'Windows', names: ['windows-x64', 'windows-arm64'] },
  { key: 'mac', name: 'macOS', names: ['macos-arm64', 'macos-x64'] },
  { key: 'cmd', name: 'Командой', names: ['install.ps1', 'install.sh'] },
];

function chartHtml(stats, nDays) {
  const dayList = [];
  for (let i = nDays - 1; i >= 0; i--) dayList.push(new Date(Date.now() - i * 86400000).toISOString().slice(0, 10));
  const by = Object.fromEntries(dayList.map(d => [d, { win: 0, mac: 0, cmd: 0 }]));
  for (const r of stats.days) {
    const g = GROUPS.find(x => x.names.includes(r.name));
    if (g && by[r.day]) by[r.day][g.key] += r.count;
  }
  const totals = dayList.map(d => GROUPS.reduce((s, g) => s + by[d][g.key], 0));
  const max = Math.max(1, ...totals);
  const step = max <= 5 ? 1 : 10 ** Math.floor(Math.log10(max / 4));
  const tick = [1, 2, 5, 10].map(m => m * step).find(t => max / t <= 5) || step * 10;
  const top = Math.ceil(max / tick) * tick;
  const W = 720, H = 260, L = 36, B = 24, T = 8;
  const bw = (W - L) / dayList.length;
  const gap = Math.max(1, Math.min(4, bw * 0.25));
  const y = v => T + (H - T - B) * (1 - v / top);
  let svg = '';
  for (let v = 0; v <= top; v += tick) svg += `<line class="grid-line" x1="${L}" x2="${W}" y1="${y(v)}" y2="${y(v)}"/><text class="axis-label" x="${L - 8}" y="${y(v) + 4}" text-anchor="end">${v}</text>`;
  dayList.forEach((d, i) => {
    const x = L + i * bw + gap / 2;
    const w = Math.max(1, bw - gap);
    let acc = 0;
    const segs = GROUPS.map(g => ({ g, v: by[d][g.key] })).filter(s => s.v > 0);
    segs.forEach((s, j) => {
      const y0 = y(acc), y1 = y(acc + s.v);
      acc += s.v;
      // A 2px gap of the surface between the segments; the top one gets its round end.
      const h = Math.max(0, y0 - y1 - (j ? 2 : 0));
      const r = j === segs.length - 1 ? Math.min(4, w / 2, h) : 0;
      const path = r ? `M${x},${y1 + h} V${y1 + r} Q${x},${y1} ${x + r},${y1} H${x + w - r} Q${x + w},${y1} ${x + w},${y1 + r} V${y1 + h} Z` : `M${x},${y1} h${w} v${h} h${-w} Z`;
      svg += `<path d="${path}" fill="var(--dl-${s.g.key})"/>`;
    });
    const label = `${date(d, { year: false })}: всего ${totals[i]} — ${GROUPS.map(g => `${g.name} ${by[d][g.key]}`).join(', ')}`;
    svg += `<rect class="bar" x="${L + i * bw}" y="${T}" width="${bw}" height="${H - T - B}" fill="transparent" tabindex="0" role="img" aria-label="${esc(label)}" data-i="${i}"/>`;
    if (i % Math.ceil(dayList.length / 6) === 0 || i === dayList.length - 1) svg += `<text class="axis-label" x="${L + i * bw + bw / 2}" y="${H - 6}" text-anchor="middle">${esc(date(d, { year: false }).replace(/ (\S{3})\S*/, ' $1'))}</text>`;
  });
  const sum = g => dayList.reduce((s, d) => s + by[d][g.key], 0);
  const all = totals.reduce((a, b) => a + b, 0);
  return {
    by, dayList, totals,
    html: `<div class="chart" style="position: relative">
      <ul class="legend">${GROUPS.map(g => `<li><i style="background: var(--dl-${g.key})"></i>${g.name} <b>${sum(g)}</b></li>`).join('')}<li>Всего <b>${all}</b></li></ul>
      <svg viewBox="0 0 ${W} ${H}" role="group" aria-label="Скачивания по дням за ${nDays} дней">${svg}</svg>
      <div class="tip" hidden></div>
      <details><summary class="hint" style="cursor: pointer">Таблица по платформам</summary>
        <table class="tbl" style="margin-top: 12px"><thead><tr><th>Файл</th><th class="num">Скачиваний</th></tr></thead><tbody>
        ${Object.entries(stats.totals).sort((a, b) => b[1] - a[1]).map(([k, v]) => `<tr><td><code>/r/${esc(k)}</code></td><td class="num">${v}</td></tr>`).join('') || '<tr><td colspan="2">Пока ни одного</td></tr>'}
        </tbody></table></details></div>`,
  };
}

async function loadDownloads() {
  const box = $('#dl-out');
  loading(box);
  try {
    const { downloads } = await api.get(`/api/admin/downloads?days=${days}`);
    const c = chartHtml(downloads, days);
    box.innerHTML = c.html;
    const tip = $('.tip', box);
    const chart = $('.chart', box);
    const show = rect => {
      const i = Number(rect.dataset.i);
      const d = c.dayList[i];
      tip.innerHTML = `<b>${esc(date(d, { year: false }))}</b> · ${c.totals[i]}<br>${GROUPS.map(g => `${g.name}: ${c.by[d][g.key]}`).join('<br>')}`;
      tip.hidden = false;
      const r = rect.getBoundingClientRect(), base = chart.getBoundingClientRect();
      const left = Math.min(Math.max(0, r.left - base.left + r.width / 2 - tip.offsetWidth / 2), base.width - tip.offsetWidth);
      tip.style.left = `${left}px`;
      tip.style.top = `${r.top - base.top + 4}px`;
    };
    $$('.bar', box).forEach(rect => {
      rect.addEventListener('mouseenter', () => show(rect));
      rect.addEventListener('focus', () => show(rect));
      rect.addEventListener('mouseleave', () => { tip.hidden = true; });
      rect.addEventListener('blur', () => { tip.hidden = true; });
    });
  } catch (e) {
    failed(box, e);
  }
}
$$('#dl-days button').forEach(b => b.addEventListener('click', () => {
  days = Number(b.dataset.days);
  $$('#dl-days button').forEach(x => x.setAttribute('aria-pressed', String(x === b)));
  loadDownloads();
}));

// ---------------------------------------------------------------- audit

const ACTIONS = {
  'application.submit': 'отправил заявку', 'application.approve': 'одобрил заявку', 'application.reject': 'отклонил заявку',
  'provider.tier': 'сменил ступень', 'provider.until': 'сменил срок', 'provider.revoke': 'отозвал', 'provider.restore': 'вернул',
  'provider.import': 'перенёс', 'provider.tier_request': 'попросил ступень 2', 'look.submit': 'отправил оформление', 'look.approve': 'опубликовал оформление', 'look.return': 'вернул оформление',
};

async function loadAudit() {
  const body = $('#audit-table tbody');
  body.innerHTML = '<tr><td colspan="5"><div class="skel"><i></i><i></i></div></td></tr>';
  try {
    const { audit } = await api.get('/api/admin/audit?limit=200');
    body.innerHTML = audit.length ? audit.map(r => `<tr><td data-label="Когда">${esc(date(r.at, { time: true }))}</td><td data-label="Кто">${esc(r.actor)}</td>
      <td data-label="Что">${esc(ACTIONS[r.action] || r.action)}</td><td data-label="Кого">${esc(r.target || '')}</td>
      <td data-label="Подробно">${r.detail ? `<code>${esc(Object.entries(r.detail).map(([k, v]) => `${k}: ${typeof v === 'object' ? JSON.stringify(v) : v}`).join(', '))}</code>` : ''}</td></tr>`).join('')
      : '<tr><td colspan="5">Решений ещё не было.</td></tr>';
  } catch (e) {
    body.innerHTML = `<tr><td colspan="5">${esc(e.message)}</td></tr>`;
  }
}

// ---------------------------------------------------------------- start

async function start() {
  showState('loading');
  try {
    const who = await me(ROOT);
    wireAccount(ROOT, who.account);
    if (!who.account.admin) {
      showState('error', 'Это страница владельца V2pn', 'Ваша почта не среди владельцев. Кабинет провайдера — в разделе «Кабинет».');
      $('[data-retry]').hidden = true;
      return;
    }
    showState('ready');
    await route();
  } catch (e) {
    showState('error', 'Админка не загрузилась', e.message);
  }
}
$('[data-retry]').addEventListener('click', start);
start();
