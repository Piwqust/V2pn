#!/usr/bin/env node
// One screenshot of a page of the local site (dev/serve.mjs), headless:
//   node dev/shot.mjs <url> <out.png> [--w 1440] [--h 900] [--dark] [--screen] [--wait <js expression>]
import { launch } from './cdp.mjs';

const [url, out, ...rest] = process.argv.slice(2);
const opt = (n, d) => { const i = rest.indexOf(`--${n}`); return i < 0 ? d : (rest[i + 1] && !rest[i + 1].startsWith('--') ? rest[i + 1] : true); };
if (!url || !out) {
  console.error('usage: node dev/shot.mjs <url> <out.png> [--w 1440] [--h 900] [--dark] [--screen] [--wait <expr>]');
  process.exit(2);
}
const browser = await launch();
try {
  const page = await browser.newPage();
  await page.viewport(Number(opt('w', 1440)), Number(opt('h', 900)));
  await page.scheme(opt('dark', false) ? 'dark' : 'light');
  await page.goto(url);
  const wait = opt('wait', null);
  if (wait) await page.until(wait);
  await new Promise(r => setTimeout(r, 400));
  await page.shot(out, { full: !opt('screen', false) });
  if (page.logs.length) console.log(page.logs.join('\n'));
  console.log(out);
} finally {
  browser.close();
}
