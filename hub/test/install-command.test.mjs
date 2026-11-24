// The one-line install commands on partner pages and on the site lead straight to GitHub's release files, so a server
// taken over could not hand out a script of its own.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { startHub } from './helpers.mjs';

const configraFolder = new URL('../../partners/configra', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1');
const repoFile = rel => fs.readFileSync(new URL(`../../${rel}`, import.meta.url), 'utf8');

test("a partner page's install commands name GitHub's release files of the hub's repository", async () => {
  const env = await startHub({ env: { HUB_GITHUB_REPO: 'someone/V2pn-fork' } });
  try {
    env.hub.importPartner(configraFolder);
    env.hub.publish('test');
    const page = fs.readFileSync(path.join(env.config.publicDir, 'p', 'configra', 'index.html'), 'utf8');
    assert.ok(page.includes('<code>irm https://github.com/someone/V2pn-fork/releases/latest/download/install.ps1 | iex</code>'));
    assert.ok(page.includes('<code>curl -fsSL https://github.com/someone/V2pn-fork/releases/latest/download/install.sh | bash</code>'));
    assert.doesNotMatch(page, /\/r\/install\./);
    assert.match(page, /href="\/r\/windows-x64"/, 'the download buttons still go through the hub (counted)');
  } finally {
    await env.close();
  }
});

test("the site's commands do the same", () => {
  const site = repoFile('site/src/data/site.ts');
  assert.match(site, /win: `irm \$\{site\.repo\}\/releases\/latest\/download\/install\.ps1 \| iex`/);
  assert.match(site, /mac: `curl -fsSL \$\{site\.repo\}\/releases\/latest\/download\/install\.sh \| bash`/);
  assert.match(site, /repo: 'https:\/\/github\.com\/Piwqust\/V2pn'/);
  const docs = repoFile('site/src/docs/install.md');
  assert.ok(docs.includes('irm https://github.com/Piwqust/V2pn/releases/latest/download/install.ps1 | iex'));
  assert.ok(docs.includes('curl -fsSL https://github.com/Piwqust/V2pn/releases/latest/download/install.sh | bash'));
  for (const [file, text] of [['site.ts', site], ['install.md', docs]]) assert.doesNotMatch(text, /\/r\/install\./, file);
});
