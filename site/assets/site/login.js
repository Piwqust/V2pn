// /providers/login/: an email, then the six digits sent to it (or waiting in the owner's outbox while there is no
// mail), then back to the page that sent the person here.
import { api, markNav, rememberSignIn, safeNext } from './api.js';
import { $, setFieldError } from './hubui.js';

const ROOT = '../../';
const RESEND = 30;
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

const formEmail = $('#step-email');
const formCode = $('#step-code');
const email = $('#email');
const code = $('#code');
const sendBtn = $('#send');
const verifyBtn = $('#verify');
const resend = $('#resend');
const resendWait = $('#resend-wait');
const emailStatus = $('#email-status');
const codeStatus = $('#code-status');
const next = safeNext(new URLSearchParams(location.search).get('next'));

let currentEmail = '';
let timer = null;
let busy = false;

markNav(ROOT);

// Whether mail goes out: without it the code waits for the owner, and the page says so before anything is sent.
api.get('/api/health', { timeout: 8000 }).then(h => { if (h.mail === 'outbox') $('#nomail').hidden = false; }).catch(() => {});

// A session already open goes straight on.
api.get('/api/me', { timeout: 8000 }).then(me => { rememberSignIn(me.account); go(me); }).catch(() => {});

function go(me) {
  if (next) { location.replace(next); return; }
  if (me.account.admin) location.replace(`${ROOT}admin/`);
  else if (me.provider) location.replace(`${ROOT}providers/cabinet/`);
  else location.replace(`${ROOT}providers/apply/`);
}

function countdown(seconds) {
  clearInterval(timer);
  let left = seconds;
  resend.hidden = true;
  resendWait.hidden = false;
  const tick = () => {
    if (left <= 0) {
      clearInterval(timer);
      resendWait.hidden = true;
      resend.hidden = false;
      return;
    }
    resendWait.textContent = `Новый код можно запросить через ${left} с`;
    left--;
  };
  tick();
  timer = setInterval(tick, 1000);
}

async function requestCode(button, status) {
  if (busy) return false;
  busy = true;
  button.disabled = true;
  const slow = setTimeout(() => { status.textContent = 'Отправляем код…'; }, 300);
  try {
    const res = await api.post('/api/auth/code', { email: currentEmail });
    clearTimeout(slow);
    status.textContent = '';
    $('#sent-to').textContent = res.delivery === 'outbox'
      ? `Письма пока не уходят: код для ${currentEmail} у владельца V2pn. Попросите его, он действует 10 минут.`
      : `Отправили код на ${currentEmail}. Он действует 10 минут. Нет письма — проверьте «Спам».`;
    countdown(RESEND);
    return true;
  } catch (e) {
    clearTimeout(slow);
    if (e.code === 'invalid' && e.fields?.email) {
      setFieldError(email, e.fields.email);
      status.textContent = '';
      if (!formEmail.hidden) email.focus();
    } else if (e.code === 'rate_limited') {
      status.textContent = 'Слишком много кодов подряд. Подождите минуту и запросите снова.';
      countdown(60);
    } else {
      status.textContent = e.message;
    }
    return false;
  } finally {
    busy = false;
    button.disabled = false;
  }
}

email.addEventListener('input', () => setFieldError(email, ''));
formEmail.addEventListener('submit', async e => {
  e.preventDefault();
  const value = email.value.trim();
  if (!EMAIL.test(value)) {
    setFieldError(email, 'Напишите почту целиком, с @: name@example.com.');
    email.focus();
    return;
  }
  currentEmail = value;
  if (await requestCode(sendBtn, emailStatus)) {
    formEmail.hidden = true;
    formCode.hidden = false;
    code.value = '';
    codeStatus.textContent = '';
    setFieldError(code, '');
    code.focus();
  }
});

$('#back').addEventListener('click', () => {
  clearInterval(timer);
  formCode.hidden = true;
  formEmail.hidden = false;
  email.focus();
  email.select();
});

resend.addEventListener('click', async () => {
  if (await requestCode(resend, codeStatus)) {
    codeStatus.textContent = 'Новый код отправлен. Старый больше не подойдёт.';
    code.value = '';
    code.focus();
  }
});

// Digits only, whatever was pasted ("482 913", "482-913"); six of them sign in at once.
code.addEventListener('input', () => {
  const digits = code.value.replace(/\D/g, '').slice(0, 6);
  if (code.value !== digits) code.value = digits;
  setFieldError(code, '');
  if (digits.length === 6) formCode.requestSubmit();
});

// The owner signs in by password; /admin/ sends here with next=/admin/, so that form opens first then.
const formPw = $('#step-password');
const pwEmail = $('#pw-email');
const pw = $('#pw');
const pwGo = $('#pw-go');
const pwStatus = $('#pw-status');

function showPassword(focus = true) {
  clearInterval(timer);
  formEmail.hidden = true;
  formCode.hidden = true;
  formPw.hidden = false;
  if (!pwEmail.value && email.value) pwEmail.value = email.value.trim();
  if (focus) (pwEmail.value ? pw : pwEmail).focus();
}
if (next && /\/admin\/?$/.test(new URL(next, location.href).pathname)) showPassword(false);
$('#to-password').addEventListener('click', () => showPassword());
$('#to-code').addEventListener('click', () => {
  formPw.hidden = true;
  formEmail.hidden = false;
  email.focus();
});
pwEmail.addEventListener('input', () => setFieldError(pwEmail, ''));
pw.addEventListener('input', () => setFieldError(pw, ''));

formPw.addEventListener('submit', async e => {
  e.preventDefault();
  if (busy) return;
  const mail = pwEmail.value.trim();
  if (!EMAIL.test(mail)) {
    setFieldError(pwEmail, 'Напишите почту целиком, с @.');
    pwEmail.focus();
    return;
  }
  if (!pw.value.trim()) {
    setFieldError(pw, 'Нужен пароль.');
    pw.focus();
    return;
  }
  busy = true;
  pwGo.disabled = true;
  const slow = setTimeout(() => { pwStatus.textContent = 'Проверяем…'; }, 300);
  try {
    const res = await api.post('/api/auth/password', { email: mail, password: pw.value });
    clearTimeout(slow);
    rememberSignIn(res.account);
    pwStatus.textContent = 'Вы вошли. Открываем…';
    const me = await api.get('/api/me').catch(() => ({ account: res.account, provider: null }));
    go(me);
  } catch (err) {
    clearTimeout(slow);
    pwStatus.textContent = '';
    if (err.code === 'password_invalid') setFieldError(pw, 'Почта или пароль не подходят.');
    else if (err.code === 'rate_limited') setFieldError(pw, 'Слишком много попыток. Подождите час или войдите по коду.');
    else pwStatus.textContent = err.message;
    pw.select();
    pw.focus();
  } finally {
    busy = false;
    pwGo.disabled = false;
  }
});

formCode.addEventListener('submit', async e => {
  e.preventDefault();
  if (busy) return;
  const value = code.value.replace(/\D/g, '');
  if (value.length !== 6) {
    setFieldError(code, 'Код — шесть цифр из письма.');
    code.focus();
    return;
  }
  busy = true;
  verifyBtn.disabled = true;
  const slow = setTimeout(() => { codeStatus.textContent = 'Проверяем…'; }, 300);
  try {
    const res = await api.post('/api/auth/verify', { email: currentEmail, code: value });
    clearTimeout(slow);
    rememberSignIn(res.account);
    codeStatus.textContent = 'Вы вошли. Открываем…';
    const me = await api.get('/api/me').catch(() => ({ account: res.account, provider: null }));
    go(me);
  } catch (err) {
    clearTimeout(slow);
    codeStatus.textContent = '';
    if (err.code === 'code_invalid') setFieldError(code, 'Код не подходит. Проверьте цифры или запросите новый.');
    else if (err.code === 'code_expired') {
      setFieldError(code, 'Код устарел или кончились попытки. Запросите новый.');
      clearInterval(timer);
      resendWait.hidden = true;
      resend.hidden = false;
    } else if (err.code === 'rate_limited') setFieldError(code, 'Слишком много попыток. Подождите минуту.');
    else codeStatus.textContent = err.message;
    code.select();
    code.focus();
  } finally {
    busy = false;
    verifyBtn.disabled = false;
  }
});
