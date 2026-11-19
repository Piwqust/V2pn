// /providers/login/: a login and a password, «Создать аккаунт» or «Войти», then on to the page that sent the person
// here, or to the account's own: moderators to /admin/, providers to «Мой партнёр».
import { api, homeOf, isMod, markNav, rememberSignIn, safeNext } from './api.js';
import { $, setFieldError } from './hubui.js';

const ROOT = '/';
const LOGIN = /^[a-z0-9._-]{3,32}$/i;

const form = $('#signin');
const login = $('#login');
const password = $('#password');
const status = $('#status');
const next = safeNext(new URLSearchParams(location.search).get('next'));
let working = false;

login.dataset.hintId = 'login-hint';
password.dataset.hintId = 'password-hint';
markNav(ROOT);

function go(account) {
  // A provider sent to the moderation page by an old link lands on its own page instead of a refusal.
  const target = next && (isMod(account) || !next.startsWith('/admin/')) ? next : `${ROOT}${homeOf(account)}`;
  location.replace(target);
}

// A session already open goes straight on.
api.get('/api/me', { timeout: 8000 }).then(me => { rememberSignIn(me.account); go(me.account); }).catch(() => {});

function clear() {
  setFieldError(login, '');
  setFieldError(password, '');
  status.textContent = '';
}

function localProblems(kind) {
  const fields = {};
  const l = login.value.trim();
  if (!l) fields.login = 'Напишите логин.';
  else if (kind === 'register' && !LOGIN.test(l)) fields.login = 'Логин: от 3 до 32 латинских букв, цифр и знаков . _ -';
  if (!password.value) fields.password = 'Напишите пароль.';
  else if (kind === 'register' && password.value.length < 6) fields.password = 'Пароль от 6 символов.';
  return fields;
}

async function submit(kind) {
  if (working) return;
  clear();
  const problems = localProblems(kind);
  if (Object.keys(problems).length) {
    for (const [k, m] of Object.entries(problems)) setFieldError(k === 'login' ? login : password, m);
    (problems.login ? login : password).focus();
    return;
  }
  working = true;
  const btn = kind === 'register' ? $('#register') : $('#enter');
  const label = btn.textContent;
  lockButtons(true);
  btn.textContent = kind === 'register' ? 'Создаём…' : 'Входим…';
  try {
    const res = await api.post(kind === 'register' ? '/api/auth/register' : '/api/auth/login', { login: login.value.trim(), password: password.value });
    rememberSignIn(res.account);
    status.textContent = kind === 'register' ? 'Аккаунт создан. Открываем «Мой партнёр»…' : 'Вошли. Открываем…';
    go(res.account);
    return;
  } catch (e) {
    if (e.code === 'password_invalid') {
      setFieldError(password, 'Логин или пароль не подходят. Новый провайдер? Нажмите «Создать аккаунт».');
      password.focus();
    } else if (e.fields && (e.fields.login || e.fields.password)) {
      if (e.fields.login) setFieldError(login, e.fields.login);
      if (e.fields.password) setFieldError(password, e.fields.password);
      (e.fields.login ? login : password).focus();
      if (e.code === 'login_taken') setFieldError(login, 'Такой логин уже занят. Если он ваш, нажмите «Войти».');
    } else if (e.code === 'rate_limited') {
      status.textContent = 'Слишком много попыток. Подождите час или попросите модератора сбросить пароль.';
    } else {
      status.textContent = e.message;
    }
  } finally {
    btn.textContent = label;
    lockButtons(false);
    working = false;
  }
}

function lockButtons(disabled) {
  form.querySelectorAll('button[type="submit"]').forEach(b => { b.disabled = disabled; });
}

form.addEventListener('submit', e => {
  e.preventDefault();
  submit(e.submitter && e.submitter.value === 'register' ? 'register' : 'login');
});
// Enter in a field signs in: the person coming back is the usual case, and a new one is told what to press.
[login, password].forEach(input => input.addEventListener('keydown', e => {
  if (e.key === 'Enter' && !e.isComposing) {
    e.preventDefault();
    submit('login');
  }
}));
// A new account wants a new password, not the browser's saved one.
$('#register').addEventListener('pointerdown', () => { password.autocomplete = 'new-password'; });
$('#enter').addEventListener('pointerdown', () => { password.autocomplete = 'current-password'; });
