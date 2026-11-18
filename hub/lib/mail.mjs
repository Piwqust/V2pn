// A minimal SMTP client (node:net / node:tls): implicit TLS (smtps://, port 465) or STARTTLS (smtp://, port 587),
// AUTH PLAIN or LOGIN, one plain-text UTF-8 message per connection. Enough for sign-in codes; no dependencies.
import crypto from 'node:crypto';
import net from 'node:net';
import os from 'node:os';
import tls from 'node:tls';

export function parseSmtpUrl(text) {
  const u = new URL(text);
  if (u.protocol !== 'smtp:' && u.protocol !== 'smtps:') throw new Error('HUB_SMTP_URL must be smtp:// or smtps://');
  const secure = u.protocol === 'smtps:';
  return {
    host: u.hostname.replace(/^\[|\]$/g, ''),
    port: Number(u.port) || (secure ? 465 : 587),
    secure,
    user: decodeURIComponent(u.username),
    pass: decodeURIComponent(u.password),
  };
}

export function addressOf(from) {
  const m = /<([^>]+)>/.exec(from);
  return (m ? m[1] : from).trim();
}

function encodeWord(text) {
  // eslint-disable-next-line no-control-regex
  return /^[\x20-\x7e]*$/.test(text) ? text : `=?UTF-8?B?${Buffer.from(text, 'utf8').toString('base64')}?=`;
}

function encodeFrom(from) {
  const m = /^\s*(.*?)\s*<([^>]+)>\s*$/.exec(from);
  if (!m || !m[1]) return `<${addressOf(from)}>`;
  return `${encodeWord(m[1].replace(/^"|"$/g, ''))} <${m[2]}>`;
}

export function buildMessage({ from, to, subject, text }) {
  // eslint-disable-next-line no-control-regex
  if (/[\r\n]/.test(to + subject + from)) throw new Error('header injection');
  const domain = addressOf(from).split('@')[1] || 'localhost';
  const body = Buffer.from(text.replace(/\r?\n/g, '\r\n'), 'utf8').toString('base64').replace(/.{1,76}/g, '$&\r\n');
  return [
    `From: ${encodeFrom(from)}`,
    `To: <${to}>`,
    `Subject: ${encodeWord(subject)}`,
    `Date: ${new Date().toUTCString().replace('GMT', '+0000')}`,
    `Message-ID: <${crypto.randomBytes(12).toString('hex')}@${domain}>`,
    'MIME-Version: 1.0',
    'Content-Type: text/plain; charset=utf-8',
    'Content-Transfer-Encoding: base64',
    '',
    body,
  ].join('\r\n');
}

class Conversation {
  constructor(socket, timeoutMs) {
    this.buffer = '';
    this.waiters = [];
    this.lines = [];
    this.timeoutMs = timeoutMs;
    this.attach(socket);
  }

  attach(socket) {
    this.socket = socket;
    socket.setEncoding('utf8');
    socket.on('data', chunk => {
      this.buffer += chunk;
      if (this.buffer.length > 64 * 1024) {
        socket.destroy(new Error('SMTP reply too long'));
        return;
      }
      let nl;
      while ((nl = this.buffer.indexOf('\n')) >= 0) {
        const line = this.buffer.slice(0, nl).replace(/\r$/, '');
        this.buffer = this.buffer.slice(nl + 1);
        this.lines.push(line);
        if (/^\d{3}(?: |$)/.test(line)) {
          const reply = { code: Number(line.slice(0, 3)), lines: this.lines };
          this.lines = [];
          this.waiters.shift()?.resolve(reply);
        }
      }
    });
    socket.on('error', e => { for (const w of this.waiters.splice(0)) w.reject(e); });
    socket.on('close', () => { for (const w of this.waiters.splice(0)) w.reject(new Error('SMTP connection closed')); });
  }

  read() {
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('SMTP timeout')), this.timeoutMs);
      this.waiters.push({
        resolve: v => { clearTimeout(timer); resolve(v); },
        reject: e => { clearTimeout(timer); reject(e); },
      });
    });
  }

  async command(line, expect) {
    const reply = this.read();
    this.socket.write(line + '\r\n');
    const r = await reply;
    if (!expect.includes(r.code)) {
      // The command itself is not in the error: it may carry the password.
      throw new Error(`SMTP ${r.code} after ${line.split(' ')[0]}`);
    }
    return r;
  }
}

function connect(options, secure, timeoutMs) {
  return new Promise((resolve, reject) => {
    const socket = secure ? tls.connect(options) : net.connect(options);
    const timer = setTimeout(() => socket.destroy(new Error('SMTP connect timeout')), timeoutMs);
    socket.once(secure ? 'secureConnect' : 'connect', () => { clearTimeout(timer); resolve(socket); });
    socket.once('error', e => { clearTimeout(timer); reject(e); });
  });
}

/**
 * Sends one message. <smtp> is parseSmtpUrl's result; <tlsOptions> lets tests trust their own certificate.
 * A password is never sent over a connection that is not encrypted, except to this computer (tests).
 */
export async function sendMail(smtp, { from, to, subject, text }, { timeoutMs = 20000, tlsOptions = {}, helo } = {}) {
  const loopback = ['127.0.0.1', '::1', 'localhost'].includes(smtp.host);
  const socket = await connect({ host: smtp.host, port: smtp.port, servername: net.isIP(smtp.host) ? undefined : smtp.host, ...tlsOptions }, smtp.secure, timeoutMs);
  const c = new Conversation(socket, timeoutMs);
  try {
    const greeting = await c.read();
    if (greeting.code !== 220) throw new Error(`SMTP greeting ${greeting.code}`);
    const name = helo || os.hostname() || 'localhost';
    let ehlo = await c.command(`EHLO ${name}`, [250]);
    let encrypted = smtp.secure;
    if (!encrypted && ehlo.lines.some(l => /^250[ -]STARTTLS\b/i.test(l))) {
      await c.command('STARTTLS', [220]);
      // From here the bytes on the raw socket are TLS records; only the TLS socket may read them.
      socket.removeAllListeners('data');
      const upgraded = await new Promise((resolve, reject) => {
        const s = tls.connect({ socket, servername: net.isIP(smtp.host) ? undefined : smtp.host, ...tlsOptions }, () => resolve(s));
        s.once('error', reject);
      });
      c.attach(upgraded);
      encrypted = true;
      ehlo = await c.command(`EHLO ${name}`, [250]);
    }
    if (smtp.user) {
      if (!encrypted && !loopback) throw new Error('SMTP server offers no TLS; refusing to send the password in clear');
      const auth = ehlo.lines.find(l => /^250[ -]AUTH\b/i.test(l)) || '';
      if (/\bPLAIN\b/i.test(auth)) {
        await c.command(`AUTH PLAIN ${Buffer.from(`\0${smtp.user}\0${smtp.pass}`, 'utf8').toString('base64')}`, [235]);
      } else {
        await c.command('AUTH LOGIN', [334]);
        await c.command(Buffer.from(smtp.user, 'utf8').toString('base64'), [334]);
        await c.command(Buffer.from(smtp.pass, 'utf8').toString('base64'), [235]);
      }
    }
    await c.command(`MAIL FROM:<${addressOf(from)}>`, [250]);
    await c.command(`RCPT TO:<${to}>`, [250, 251]);
    await c.command('DATA', [354]);
    const message = buildMessage({ from, to, subject, text }).replace(/^\./gm, '..');
    await c.command(message + '\r\n.', [250]);
    try {
      await c.command('QUIT', [221]);
    } catch { /* the message is sent */ }
  } finally {
    c.socket.destroy();
  }
}
