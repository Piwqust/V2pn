// A headless Chrome driven over the DevTools protocol, for walking the site in dev/walk.mjs. No window ever opens:
// --headless=new, its own profile in the temp folder, and the process tree is killed at the end.
import { execFileSync, spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const sleep = ms => new Promise(r => setTimeout(r, ms));

function findChrome() {
  const local = process.env.LOCALAPPDATA ? path.join(process.env.LOCALAPPDATA, 'Google/Chrome/Application/chrome.exe') : null;
  const list = [process.env.CHROME_PATH, 'C:/Program Files/Google/Chrome/Application/chrome.exe', 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe', local,
    'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', '/usr/bin/google-chrome', '/usr/bin/chromium'];
  const found = list.find(p => p && fs.existsSync(p));
  if (!found) throw new Error('no Chrome found (set CHROME_PATH)');
  return found;
}

export async function launch() {
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'v2pn-walk-'));
  const portFile = path.join(profile, 'DevToolsActivePort');
  const child = spawn(findChrome(), ['--headless=new', `--user-data-dir=${profile}`, '--remote-debugging-port=0', '--no-first-run', '--no-default-browser-check',
    '--disable-extensions', '--hide-scrollbars', '--mute-audio', '--disable-gpu', '--no-proxy-server', '--window-size=1440,900', 'about:blank'],
  { stdio: 'ignore', windowsHide: true });
  let port = null;
  for (let i = 0; i < 150 && !port; i++) {
    await sleep(100);
    try { port = parseInt(fs.readFileSync(portFile, 'utf8'), 10) || null; } catch { /* not yet */ }
  }
  if (!port) throw new Error('Chrome did not start');
  const ver = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();
  const ws = new WebSocket(ver.webSocketDebuggerUrl);
  await new Promise((r, j) => { ws.addEventListener('open', r, { once: true }); ws.addEventListener('error', j, { once: true }); });
  let id = 0;
  const pending = new Map();
  const subs = new Set();
  ws.addEventListener('message', e => {
    const m = JSON.parse(e.data);
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } else subs.forEach(f => f(m));
  });
  const send = (method, params = {}, sessionId) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params, sessionId })); });
  const close = () => {
    try { ws.close(); } catch { /* closed */ }
    if (child.exitCode == null) {
      try { execFileSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore' }); } catch { try { child.kill(); } catch { /* gone */ } }
    }
    setTimeout(() => { try { fs.rmSync(profile, { recursive: true, force: true }); } catch { /* locked */ } }, 500);
  };

  /** A tab; <isolated> gives it a cookie jar of its own (a second person in the same browser). */
  async function newPage({ isolated = false } = {}) {
    let browserContextId;
    if (isolated) ({ result: { browserContextId } } = await send('Target.createBrowserContext', { disposeOnDetach: true }));
    const { result: { targetId } } = await send('Target.createTarget', { url: 'about:blank', ...(browserContextId ? { browserContextId } : {}) });
    const { result: { sessionId } } = await send('Target.attachToTarget', { targetId, flatten: true });
    const S = async (m, p) => { const r = await send(m, p, sessionId); if (r.error) throw new Error(`${m}: ${r.error.message}`); return r.result; };
    const logs = [];
    subs.add(m => {
      if (m.sessionId !== sessionId) return;
      if (m.method === 'Runtime.exceptionThrown') logs.push('exception: ' + (m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text).split('\n')[0]);
      if (m.method === 'Runtime.consoleAPICalled' && ['error', 'warning'].includes(m.params.type)) logs.push(`console.${m.params.type}: ${m.params.args.map(a => a.value ?? a.description).join(' ')}`);
      if (m.method === 'Log.entryAdded' && ['error', 'warning'].includes(m.params.entry.level)) logs.push(`log: ${m.params.entry.text} ${m.params.entry.url || ''}`);
    });
    await S('Page.enable');
    await S('Runtime.enable');
    await S('Log.enable');
    let width = 1440, height = 900;
    const page = {
      logs,
      async viewport(w, h = 900) {
        width = w; height = h;
        await S('Emulation.setDeviceMetricsOverride', { width: w, height: h, deviceScaleFactor: 1, mobile: w < 600 });
      },
      async scheme(mode) {
        await S('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: mode }] });
      },
      async eval(expr) {
        const r = await S('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true });
        if (r.exceptionDetails) throw new Error(`eval: ${r.exceptionDetails.exception?.description || r.exceptionDetails.text}`);
        return r.result.value;
      },
      async goto(url) {
        const loaded = new Promise(r => { const f = m => { if (m.sessionId === sessionId && m.method === 'Page.loadEventFired') { subs.delete(f); r(); } }; subs.add(f); });
        await S('Page.navigate', { url });
        await Promise.race([loaded, sleep(15000)]);
        await page.eval('document.fonts ? document.fonts.ready.then(() => 1) : 1');
        await sleep(250);
      },
      /** Waits until <expr> is truthy in the page; returns its value. */
      async until(expr, timeout = 15000) {
        const t = Date.now();
        for (;;) {
          let v = null;
          try { v = await page.eval(expr); } catch { v = null; }
          if (v) return v;
          if (Date.now() - t > timeout) throw new Error(`timed out waiting for: ${expr}`);
          await sleep(120);
        }
      },
      async click(selector) {
        const ok = await page.eval(`(() => { const el = document.querySelector(${JSON.stringify(selector)}); if (!el) return false; el.scrollIntoView({block: 'center'}); el.click(); return true; })()`);
        if (!ok) throw new Error(`no element: ${selector}`);
        await sleep(120);
      },
      /** Types into a field as a person would: focus, set, input and change events. */
      async type(selector, text) {
        const ok = await page.eval(`(() => { const el = document.querySelector(${JSON.stringify(selector)}); if (!el) return false; el.focus();
          const proto = el.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
          Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, ${JSON.stringify(text)});
          el.dispatchEvent(new Event('input', {bubbles: true})); el.dispatchEvent(new Event('change', {bubbles: true})); return true; })()`);
        if (!ok) throw new Error(`no field: ${selector}`);
        await sleep(80);
      },
      async blur() { await page.eval('document.activeElement && document.activeElement.blur(), 1'); },
      /** Sets a file input's file (an absolute path). */
      async file(selector, filePath) {
        const { root } = await S('DOM.getDocument', { depth: 0 });
        const { nodeId } = await S('DOM.querySelector', { nodeId: root.nodeId, selector });
        if (!nodeId) throw new Error(`no file input: ${selector}`);
        await S('DOM.setFileInputFiles', { nodeId, files: [filePath] });
        await sleep(150);
      },
      async key(key, code = key, keyCode = 0) {
        await S('Input.dispatchKeyEvent', { type: 'keyDown', key, code, windowsVirtualKeyCode: keyCode });
        await S('Input.dispatchKeyEvent', { type: 'keyUp', key, code, windowsVirtualKeyCode: keyCode });
      },
      /** A screenshot of the whole page (full) or the screen, into <file>. */
      async shot(file, { full = true } = {}) {
        await page.eval('window.scrollTo(0, 0), 1');
        await sleep(150);
        let clip;
        if (full) {
          const h = await page.eval('Math.ceil(Math.max(document.documentElement.scrollHeight, document.body.scrollHeight))');
          clip = { x: 0, y: 0, width, height: Math.min(h, 16000), scale: 1 };
        }
        const { data } = await S('Page.captureScreenshot', { format: 'png', captureBeyondViewport: full, ...(clip ? { clip } : {}) });
        fs.mkdirSync(path.dirname(file), { recursive: true });
        fs.writeFileSync(file, Buffer.from(data, 'base64'));
      },
      async cookies() { return (await S('Network.getAllCookies')).cookies; },
    };
    return page;
  }
  return { newPage, close, send };
}
