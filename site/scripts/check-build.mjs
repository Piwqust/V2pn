// After every build: the pages must run under the server's policy
//   default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'
// so no inline script, no inline event handler, and no script, style, font or image from another origin.
import fs from 'node:fs';
import path from 'node:path';

const dist = path.resolve(process.argv[2] || 'dist');
const problems = [];
const walk = (dir) => fs.readdirSync(dir, { withFileTypes: true }).flatMap((e) => (e.isDirectory() ? walk(path.join(dir, e.name)) : [path.join(dir, e.name)]));
const files = walk(dist);
for (const file of files.filter((f) => f.endsWith('.html'))) {
  const html = fs.readFileSync(file, 'utf8');
  const rel = path.relative(dist, file);
  for (const m of html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)) {
    const attrs = m[1];
    const type = /type=["']?([^"'\s>]+)/i.exec(attrs)?.[1] || '';
    if (/\bsrc=/i.test(attrs)) {
      const src = /src=["']?([^"'\s>]+)/i.exec(attrs)[1];
      if (/^(https?:)?\/\//i.test(src)) problems.push(`${rel}: script from another origin: ${src}`);
      continue;
    }
    if (type === 'application/json' || type === 'application/ld+json') continue;
    if (m[2].trim()) problems.push(`${rel}: inline script (${m[2].trim().slice(0, 60)}…)`);
  }
  for (const m of html.matchAll(/\son[a-z]+=["']/gi)) problems.push(`${rel}: inline event handler near ${html.slice(m.index, m.index + 40)}`);
  for (const m of html.matchAll(/<(link|img|source|iframe)\b[^>]*(?:href|src|srcset)=["']((?:https?:)?\/\/[^"']+)/gi)) {
    if (m[1] === 'link' && !/rel=["']?(stylesheet|preload|icon|modulepreload)/i.test(m[0])) continue;
    problems.push(`${rel}: ${m[1]} from another origin: ${m[2]}`);
  }
  if (/(href|src|action|formaction)=["']?\s*javascript:/i.test(html)) problems.push(`${rel}: javascript: URL`);
}
for (const file of files.filter((f) => f.endsWith('.css'))) {
  const css = fs.readFileSync(file, 'utf8');
  for (const m of css.matchAll(/url\(\s*['"]?((?:https?:)?\/\/[^'")]+)/gi)) problems.push(`${path.relative(dist, file)}: url() from another origin: ${m[1]}`);
  for (const m of css.matchAll(/@import\s+(?:url\()?['"]?((?:https?:)?\/\/[^'")]+)/gi)) problems.push(`${path.relative(dist, file)}: @import from another origin: ${m[1]}`);
}
if (problems.length) {
  console.error(`check-build: ${problems.length} problem(s) under the site's CSP:\n  ${problems.join('\n  ')}`);
  process.exit(1);
}
console.log(`check-build: ${files.filter((f) => f.endsWith('.html')).length} pages run under the CSP (no inline script, nothing from another origin).`);
