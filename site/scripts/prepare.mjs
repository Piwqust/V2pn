// Before every build: files the site serves whose one source lives elsewhere in the repository.
//   docs/partners/partner.schema.json -> static/providers/guide/partner.schema.json (the guide links it)
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const site = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const repo = path.dirname(site);
const copies = [['docs/partners/partner.schema.json', 'static/providers/guide/partner.schema.json']];
for (const [from, to] of copies) {
  const dest = path.join(site, to);
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.copyFileSync(path.join(repo, from), dest);
}
console.log(`prepare: ${copies.length} file(s) copied from the repository`);
