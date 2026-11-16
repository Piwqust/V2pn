// V2pn's site: the download for the visitor's computer, and the copy buttons beside the commands.
// Without this script every "Скачать V2pn" leads to the install section, which lists every file.
const RELEASE = 'https://github.com/Piwqust/V2pn/releases/latest/download/';
const FILES = {
  win: ['V2pn-windows-x64.zip', 'Windows 10 и 11, x64'],
  winArm: ['V2pn-windows-arm64.zip', 'Windows на ARM'],
  mac: ['V2pn-macos-arm64.dmg', 'macOS 13.6 и новее, Apple silicon'],
};

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
