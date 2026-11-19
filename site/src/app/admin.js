// /admin/ = moderation, for the owner and the moderators: the special rights waiting (with their content, the routing
// rules as lines), every partner newest first (take down, restore, reset the password, complaints), the moderators
// (the owner only), downloads and the audit log.
import { api, isMod, me, wireAccount } from './api.js';
import { $, $$, busy, confirmStep, copyText, date, esc, plural, toast } from './hubui.js';

const ROOT = '/';
const VIEWS = ['rights', 'partners', 'mods', 'downloads', 'audit'];
const RIGHT_NAMES = { routing: 'Свои правила маршрутизации', lock: 'Скрытые адреса серверов', actions: 'Свои кнопки', account: 'API и вход по коду' };
const DOS = { 'open.link': 'открывает ссылку', 'open.renew': 'продлить', 'open.support': 'поддержка', 'open.cabinet': 'кабинет', 'connect.best': 'самый быстрый сервер',
  'connect.recommend': 'сервер под задачу', 'connect.server': 'подключает сервер', 'connect.group': 'подключает группу', 'subscription.update': 'обновляет подписку',
  'check.services': 'проверяет сервисы', 'info.card': 'карточка с данными' };
let account = null;
let days = 30;
let filter = 'all';
let partners = [];

// ---------------------------------------------------------------- the frame

function showState(state, title, message) {
  $('#view-loading').hidden = state !== 'loading';
  $('#view-error').hidden = state !== 'error';
  $('#shell').hidden = state !== 'ready';
  if (title) $('[data-error-title]').textContent = title;
  if (message) $('[data-error-text]').textContent = message;
}

const owner = () => account && account.role === 'owner';
function currentView() {
  const v = location.hash.slice(1);
  return VIEWS.includes(v) && (v !== 'mods' || owner()) ? v : 'rights';
}

async function route() {
  const view = currentView();
  for (const v of VIEWS) $(`[data-view="${v}"]`).hidden = v !== view;
  $$('.sidenav [data-tab]').forEach(a => { if (a.dataset.tab === view) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current'); });
  const load = { rights: loadRights, partners: loadPartners, mods: loadMods, downloads: loadDownloads, audit: loadAudit }[view];
  await load();
}
window.addEventListener('hashchange', () => { route(); window.scrollTo({ top: 0 }); });

function failed(box, e, again) {
  box.innerHTML = `<div class="state"><h3>Не загрузилось</h3><p>${esc(e.message)}</p><button class="btn btn--secondary" type="button" data-again>Попробовать ещё раз</button></div>`;
  $('[data-again]', box).addEventListener('click', again || route);
}
const loading = box => { box.innerHTML = '<div class="skel" aria-busy="true"><i></i><i></i><i></i></div>'; };
const empty = (title, text) => `<div class="state"><h3>${esc(title)}</h3>${text ? `<p>${esc(text)}</p>` : ''}</div>`;
const setCount = (key, n) => { const el = $(`[data-count="${key}"]`); el.textContent = n; el.hidden = !n; };

/** The counts beside the sections and a failed publication, from /api/mod/overview. */
async function refreshOverview() {
  try {
    const o = await api.get('/api/mod/overview');
    setCount('rights', o.pendingRights);
    setCount('partners', o.partners.reduce((s, x) => s + x.n, 0));
    const err = o.publication && o.publication.error;
    $('#pub-error').hidden = !err;
    if (err) {
      $('[data-pub-error-title]').textContent = `${date(err.at, { time: true })}: публикация не прошла. Изменения сохранены, но к пользователям не ушли.`;
      $('[data-pub-error-list]').innerHTML = (err.problems || []).map(p => `<li>${esc(p)}</li>`).join('');
    }
  } catch { /* the counts wait for the next step */ }
}

/** A password shown once, with a copy button. */
function showPassword(login, password, text) {
  const dlg = document.createElement('dialog');
  dlg.className = 'dlg';
  dlg.setAttribute('aria-labelledby', 'pw-once-t');
  dlg.innerHTML = `<form method="dialog"><h2 id="pw-once-t">Пароль для ${esc(login)}</h2>
    <p>${esc(text)} Он показан один раз: передайте его человеку сейчас.</p>
    <div class="copyline"><code id="pw-once">${esc(password)}</code><button class="btn btn--primary" type="button" data-copy-pw>Копировать</button></div>
    <div class="row"><button class="btn btn--secondary" type="submit">Готово</button></div></form>`;
  document.body.appendChild(dlg);
  $('[data-copy-pw]', dlg).addEventListener('click', async () => toast((await copyText(password)) ? 'Пароль скопирован' : 'Не скопировалось: выделите и нажмите Ctrl+C'));
  dlg.addEventListener('close', () => dlg.remove());
  dlg.showModal();
}

// ---------------------------------------------------------------- rights waiting

function rightContent(r) {
  const c = r.content;
  if (r.right === 'routing') {
    return `<pre class="rules">${esc(r.text || '')}</pre>${r.approvedText != null ? `<details class="check-more"><summary>Сейчас одобрено и в V2pn</summary><pre class="rules">${esc(r.approvedText)}</pre></details>` : ''}`;
  }
  if (r.right === 'lock') return '<p>Скрыть адреса и настройки серверов: пользователь видит только названия.</p>';
  if (r.right === 'account') return `<dl class="kv"><dt>Адрес API</dt><dd><code>${esc(c.api)}</code></dd><dt>Вход по коду</dt><dd>${c.login ? 'да' : 'нет'}</dd></dl>`;
  if (r.right === 'actions') {
    return `<ol class="btn-list">${c.map(a => `<li><b>${esc(a.label)}</b> <span class="hint">${esc(DOS[a.do] || a.do)}${a.url ? `: <code>${esc(a.url)}</code>` : ''}${a.strategy ? ` (${esc(a.strategy)})` : ''}${a.purpose ? ` (${esc(a.purpose)})` : ''}</span></li>`).join('')}</ol>`;
  }
  return `<pre class="rules">${esc(JSON.stringify(c, null, 2))}</pre>`;
}

async function loadRights() {
  const box = $('#rights-list');
  loading(box);
  try {
    const { rights } = await api.get('/api/mod/rights');
    setCount('rights', rights.length);
    box.innerHTML = rights.length ? rights.map(r => `<article class="req" data-partner="${esc(r.partner)}" data-right="${esc(r.right)}" data-hash="${esc(r.hash)}">
        <div class="req__head"><h3>${esc(r.partnerName)}</h3><span class="badge badge--warn">ждёт</span></div>
        <p class="hint">${esc(RIGHT_NAMES[r.right] || r.right)} · логин ${esc(r.login)} · /p/${esc(r.partner)}/${r.requestedAt ? ` · ${esc(date(r.requestedAt, { time: true }))}` : ''}${r.live ? ' · прежняя версия уже одобрена' : ''}</p>
        ${rightContent(r)}
        <div class="row"><button class="btn btn--primary btn--sm" type="button" data-approve>Одобрить</button><button class="btn btn--secondary btn--sm" type="button" data-deny>Отказать</button></div>
      </article>`).join('')
      : empty('Пусто', 'Новые партнёры не ждут одобрения: они работают сразу. Сюда попадают только особые права.');
    $$('[data-approve]', box).forEach(b => b.addEventListener('click', () => decide(b, true)));
    $$('[data-deny]', box).forEach(b => b.addEventListener('click', () => decide(b, false)));
  } catch (e) {
    failed(box, e);
  }
}

async function decide(btn, approve) {
  const card = btn.closest('.req');
  const { partner, right, hash } = card.dataset;
  const name = $('h3', card).textContent;
  let reason = null;
  if (!approve) {
    reason = await confirmStep({ title: `Отказать ${name}?`, text: `${esc(RIGHT_NAMES[right] || right)}. Провайдер увидит причину у себя. Партнёр останется опубликованным.`,
      action: 'Отказать', danger: true, field: { label: 'Причина', multiline: true, required: true, empty: 'Напишите причину: провайдер увидит эти слова.' } });
    if (!reason) return;
  }
  await busy(btn, async () => {
    try {
      await api.post(`/api/mod/partners/${encodeURIComponent(partner)}/rights/${right}/${approve ? 'approve' : 'deny'}`, approve ? { hash } : { hash, reason });
      toast(approve ? 'Одобрено и подписано сервером' : 'Отказано. Провайдер увидит причину');
    } catch (e) {
      // 409: the provider changed the request after this page showed it; it is shown again as it is now.
      toast(e.code === 'conflict' ? 'Провайдер изменил запрос. Посмотрите его ещё раз.' : e.message);
    }
  });
  await loadRights();
  refreshOverview();
}

// ---------------------------------------------------------------- partners

const isNew = p => Date.now() - new Date(p.createdAt).getTime() < 48 * 3600 * 1000;

function thumb(p) {
  const bg = p.brand.length > 1 ? `linear-gradient(135deg, ${p.brand.join(', ')})` : p.brand[0] || '#E8391B';
  const pic = p.pictures.mark ? `<img src="/api/mod/pictures/${esc(p.pictures.mark)}" alt="">` : esc((p.name || '?').charAt(0).toUpperCase());
  return `<span class="pthumb" style="background: ${esc(bg)}">${pic}</span>`;
}

function partnerItem(p) {
  const pills = [
    p.state === 'down' ? '<span class="badge badge--fail">снят</span>' : isNew(p) ? '<span class="badge badge--ok">новый</span>' : '',
    p.complaints ? `<span class="badge badge--warn">${p.complaints} ${plural(p.complaints, 'жалоба', 'жалобы', 'жалоб')}</span>` : '',
    p.pending.length ? `<a class="badge badge--info" href="#rights">ждут права: ${p.pending.length}</a>` : '',
    p.layout === 'stickers' ? '<span class="badge">наклейки</span>' : '',
    p.source === 'import' ? '<span class="badge">перенесён</span>' : '',
  ].join('');
  return `<article class="pitem${p.state === 'down' ? ' is-off' : ''}" data-id="${esc(p.id)}" data-login="${esc(p.login)}">
    ${thumb(p)}
    <div class="pitem__meta"><div class="pitem__name"><b>${esc(p.name)}</b>${pills}</div>
      <span class="pitem__sub">${(p.domains || []).map(esc).join(', ')} · логин ${esc(p.login)} · ${esc(date(p.createdAt, { time: true }))} · <a href="${esc(p.page)}" target="_blank" rel="noopener">/p/${esc(p.id)}/</a></span>
      ${p.state === 'down' && p.downReason ? `<span class="pitem__sub">Причина: ${esc(p.downReason)}</span>` : ''}</div>
    <div class="pitem__act">${p.state === 'down'
    ? '<button class="btn btn--secondary btn--sm" type="button" data-restore>Вернуть</button>'
    : '<button class="btn btn--danger btn--sm" type="button" data-down>Снять</button>'}
      <button class="btn btn--secondary btn--sm" type="button" data-reset>Сбросить пароль</button>
      ${p.complaints ? '<button class="btn btn--secondary btn--sm" type="button" data-complaints aria-expanded="false">Жалобы</button>' : ''}</div>
    <div class="pitem__more" hidden></div>
  </article>`;
}

function drawPartners() {
  const box = $('#partners-list');
  const list = partners.filter(p => (filter === 'down' ? p.state === 'down' : filter === 'complaints' ? p.complaints > 0 : true));
  const live = partners.filter(p => p.state !== 'down').length;
  $('#partners-count').textContent = `${live} ${plural(live, 'работает', 'работают', 'работают')} · ${partners.length - live} ${plural(partners.length - live, 'снят', 'снято', 'снято')}`;
  box.innerHTML = list.length ? list.map(partnerItem).join('')
    : empty({ all: 'Партнёров пока нет', complaints: 'Жалоб нет', down: 'Снятых нет' }[filter], filter === 'all' ? 'Провайдер публикует партнёра сам, и он появится здесь.' : '');
}

async function loadPartners() {
  const box = $('#partners-list');
  loading(box);
  try {
    ({ partners } = await api.get('/api/mod/partners'));
    setCount('partners', partners.length);
    drawPartners();
  } catch (e) {
    failed(box, e);
  }
}

$$('#partners-filter button').forEach(b => b.addEventListener('click', () => {
  filter = b.dataset.filter;
  $$('#partners-filter button').forEach(x => x.setAttribute('aria-pressed', String(x === b)));
  drawPartners();
}));

$('#partners-list').addEventListener('click', async e => {
  const btn = e.target.closest('button');
  const item = btn && btn.closest('.pitem');
  if (!item) return;
  const { id, login } = item.dataset;
  const name = $('.pitem__name b', item).textContent;
  if (btn.hasAttribute('data-down')) {
    const reason = await confirmStep({ title: `Снять ${name}?`, text: 'Страница партнёра скажет, что он снят, а V2pn у пользователей уберёт его вкладку при следующей проверке. Вернуть можно в любой момент.',
      action: 'Снять', danger: true, field: { label: 'Причина', multiline: true, required: true, empty: 'Напишите причину: провайдер увидит её.' } });
    if (!reason) return;
    await act(btn, () => api.post(`/api/mod/partners/${id}/down`, { reason }), 'Снят. У пользователей вкладка пропадёт при следующей проверке');
  } else if (btn.hasAttribute('data-restore')) {
    await act(btn, () => api.post(`/api/mod/partners/${id}/restore`), 'Возвращён и опубликован');
  } else if (btn.hasAttribute('data-reset')) {
    const ok = await confirmStep({ title: `Сбросить пароль ${login}?`, text: 'Старый пароль перестанет работать, все входы этого аккаунта закончатся.', action: 'Сбросить', danger: true });
    if (!ok) return;
    await busy(btn, async () => {
      try {
        const res = await api.post(`/api/mod/accounts/${encodeURIComponent(login)}/password`, {});
        showPassword(res.login, res.password, 'Новый пароль.');
      } catch (err) { toast(err.message); }
    });
  } else if (btn.hasAttribute('data-complaints')) {
    await toggleComplaints(btn, item, id);
  } else if (btn.hasAttribute('data-clear')) {
    const ok = await confirmStep({ title: `Очистить жалобы на ${name}?`, text: 'Жалобы удалятся. Сделайте это, когда разобрались с ними.', action: 'Очистить' });
    if (!ok) return;
    await act(btn, () => api.post(`/api/mod/partners/${id}/complaints/clear`), 'Жалобы очищены');
  }
});

async function act(btn, call, done) {
  await busy(btn, async () => {
    try {
      await call();
      toast(done);
    } catch (err) {
      toast(err.message);
    }
  });
  await loadPartners();
  refreshOverview();
}

async function toggleComplaints(btn, item, id) {
  const more = $('.pitem__more', item);
  const open = btn.getAttribute('aria-expanded') === 'true';
  btn.setAttribute('aria-expanded', String(!open));
  more.hidden = open;
  if (open) return;
  loading(more);
  try {
    const { complaints } = await api.get(`/api/mod/partners/${id}/complaints`);
    more.innerHTML = `<ul class="complaints">${complaints.map(c => `<li><span class="hint">${esc(date(c.createdAt, { time: true }))}</span>${c.text ? esc(c.text) : '<span class="hint">без текста</span>'}</li>`).join('')}</ul>
      <button class="btn btn--secondary btn--sm" type="button" data-clear>Очистить жалобы</button>`;
  } catch (e) {
    failed(more, e, () => toggleComplaints(btn, item, id));
  }
}

$$('[data-publish-now]').forEach(b => b.addEventListener('click', async () => {
  const ok = await confirmStep({ title: 'Опубликовать заново?', text: 'Сервер соберёт и подпишет индекс, пакеты и страницы всех партнёров.', action: 'Опубликовать' });
  if (!ok) return;
  await busy(b, async () => {
    try {
      const r = await api.post('/api/mod/publish');
      toast(`Опубликовано: ${r.publication.partners.length}`);
    } catch (err) {
      toast(err.detail ? `${err.message} ${[].concat(err.detail)[0]}` : err.message);
    }
  });
  refreshOverview();
}));

// ---------------------------------------------------------------- moderators (the owner)

async function loadMods() {
  const box = $('#mods-list');
  loading(box);
  try {
    const { moderators } = await api.get('/api/mod/moderators');
    box.innerHTML = moderators.map(m => `<article class="pitem pitem--plain" data-login="${esc(m.login)}">
      <div class="pitem__meta"><div class="pitem__name"><b>${esc(m.login)}</b>${m.role === 'owner' ? '<span class="badge">владелец</span>' : '<span class="badge badge--info">модератор</span>'}</div>
        <span class="pitem__sub">${m.lastLoginAt ? `входил ${esc(date(m.lastLoginAt, { time: true }))}` : 'ещё не входил'}</span></div>
      <div class="pitem__act">${m.role === 'owner' ? '' : '<button class="btn btn--secondary btn--sm" type="button" data-mod-reset>Сбросить пароль</button><button class="btn btn--danger btn--sm" type="button" data-mod-remove>Убрать</button>'}</div>
    </article>`).join('');
  } catch (e) {
    failed(box, e);
  }
}

$('#mods-list').addEventListener('click', async e => {
  const btn = e.target.closest('button');
  const item = btn && btn.closest('[data-login]');
  if (!item) return;
  const { login } = item.dataset;
  if (btn.hasAttribute('data-mod-remove')) {
    const ok = await confirmStep({ title: `Убрать модератора ${login}?`, text: 'Аккаунт останется обычным аккаунтом провайдера, его входы закончатся.', action: 'Убрать', danger: true });
    if (!ok) return;
    await busy(btn, async () => {
      try { await api.post(`/api/mod/moderators/${encodeURIComponent(login)}/remove`, {}); toast(`${login} больше не модератор`); } catch (err) { toast(err.message); }
    });
    loadMods();
  } else if (btn.hasAttribute('data-mod-reset')) {
    const ok = await confirmStep({ title: `Сбросить пароль ${login}?`, text: 'Старый пароль перестанет работать, все входы этого аккаунта закончатся.', action: 'Сбросить', danger: true });
    if (!ok) return;
    await busy(btn, async () => {
      try { const res = await api.post(`/api/mod/accounts/${encodeURIComponent(login)}/password`, {}); showPassword(res.login, res.password, 'Новый пароль модератора.'); } catch (err) { toast(err.message); }
    });
  }
});

$('#mod-form').addEventListener('submit', async e => {
  e.preventDefault();
  const input = $('#mod-login');
  const err = $('.field__error', input.closest('.field'));
  const say = m => { err.textContent = m || ''; err.hidden = !m; input.setAttribute('aria-invalid', String(Boolean(m))); };
  say('');
  const login = input.value.trim();
  if (!login) { say('Напишите логин.'); input.focus(); return; }
  await busy($('#mod-add'), async () => {
    try {
      const { moderator } = await api.post('/api/mod/moderators', { login });
      input.value = '';
      toast(`${moderator.login} теперь модератор`);
      if (moderator.password) showPassword(moderator.login, moderator.password, 'Аккаунт создан.');
      loadMods();
    } catch (er) {
      say((er.fields && er.fields.login) || er.message);
    }
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
    const { downloads } = await api.get(`/api/mod/downloads?days=${days}`);
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
  'partner.create': 'опубликовал партнёра', 'partner.save': 'сохранил партнёра', 'partner.import': 'перенёс партнёра', 'partner.down': 'снял', 'partner.restore': 'вернул',
  'right.request': 'попросил право', 'right.withdraw': 'убрал право', 'right.approve': 'одобрил право', 'right.deny': 'отказал в праве',
  'complaints.clear': 'очистил жалобы', 'account.password': 'сбросил пароль', 'moderator.add': 'добавил модератора', 'moderator.remove': 'убрал модератора',
};

async function loadAudit() {
  const body = $('#audit-table tbody');
  body.innerHTML = '<tr><td colspan="5"><div class="skel"><i></i><i></i></div></td></tr>';
  try {
    const { audit } = await api.get('/api/mod/audit?limit=200');
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
    account = who.account;
    wireAccount(ROOT, account);
    if (!isMod(account)) {
      showState('error', 'Это страница модераторов', 'Ваш аккаунт провайдерский. Ваш партнёр на странице «Мой партнёр».');
      $('[data-retry]').hidden = true;
      $('[data-to-cabinet]').hidden = false;
      return;
    }
    $$('[data-owner-only]').forEach(el => { el.hidden = !owner(); });
    showState('ready');
    refreshOverview();
    await route();
  } catch (e) {
    showState('error', e.code === 'offline' ? 'Нет связи' : 'Модерация не загрузилась', e.message);
  }
}
$('[data-retry]').addEventListener('click', start);
window.addEventListener('online', () => { if (!$('#view-error').hidden && !$('[data-retry]').hidden) start(); });
start();
