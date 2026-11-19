// V2pn's site: the download for the visitor's computer, the copy buttons beside the commands, and «Кабинет» in the
// nav for a browser that has signed in to the provider pages.
// Without this script every "Скачать V2pn" leads to the install section, which lists every file.
// Downloads go through the hub's /r/<name>: a redirect to the latest GitHub release file, counted by day and name.
import { markNav } from './api.js';

const RELEASE = '/r/';
const FILES = {
  win: ['windows-x64', 'Windows 10 и 11, x64'],
  winArm: ['windows-arm64', 'Windows на ARM'],
  mac: ['macos-arm64', 'macOS 13.6 и новее, Apple silicon'],
};

markNav('/');

const ua = navigator.userAgent || '';
const platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || '';
// An iPad asks for the desktop site and says "Macintosh"; its touch screen gives it away. A phone keeps the
// install section: V2pn runs on computers only.
const phone = /Android|iPhone|iPad|iPod|Mobile|Windows Phone/i.test(ua) || (/Mac/i.test(platform) && navigator.maxTouchPoints > 1);
const os = phone ? 'phone' : /Win/i.test(platform) || /Windows/i.test(ua) ? 'win' : /Mac/i.test(platform) || /Mac OS X/i.test(ua) ? 'mac' : 'other';
document.documentElement.dataset.os = os;

function point(key) {
  const [file, label] = FILES[key];
  document.querySelectorAll('a[data-download]').forEach(a => { a.href = RELEASE + file; });
  document.querySelectorAll('[data-download-for]').forEach(el => { el.textContent = 'Для ' + label; });
}

if (os === 'win') {
  point('win');
  if (navigator.userAgentData && navigator.userAgentData.getHighEntropyValues) {
    navigator.userAgentData.getHighEntropyValues(['architecture']).then(v => { if (v.architecture === 'arm') point('winArm'); }).catch(() => {});
  }
} else if (os === 'mac') {
  point('mac');
}

// Copy a command: the button says so for two seconds; the text stays selectable by hand.
document.querySelectorAll('[data-copy]').forEach(btn => {
  const code = document.getElementById(btn.dataset.copy);
  if (!code) return;
  const label = btn.textContent;
  btn.addEventListener('click', async () => {
    try {
      await navigator.clipboard.writeText(code.textContent.trim());
      btn.textContent = 'Скопировано';
    } catch (e) {
      const range = document.createRange(); range.selectNodeContents(code);
      const sel = getSelection(); sel.removeAllRanges(); sel.addRange(range);
      btn.textContent = 'Выделено: Ctrl+C';
    }
    setTimeout(() => { btn.textContent = label; }, 2000);
  });
});
