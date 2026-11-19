// The provider guide is written for GitHub (docs/partners/README*.md): its links between the two languages and to
// the schema point at files there. On the site they point at the pages built from them.
const MAP = {
  'README.md': '/providers/guide/en/',
  'README.ru.md': '/providers/guide/',
  'partner.schema.json': '/providers/guide/partner.schema.json',
};

function walk(node, fn) {
  fn(node);
  if (node.children) for (const child of node.children) walk(child, fn);
}

export function guideLinks() {
  return (tree) => walk(tree, (node) => {
    if (node.type !== 'element' || node.tagName !== 'a' || !node.properties) return;
    const href = String(node.properties.href || '');
    const [file, hash] = href.split('#');
    if (MAP[file]) node.properties.href = MAP[file] + (hash ? `#${hash}` : '');
    else if (href.startsWith('https://v2pn.angleford.co/')) node.properties.href = href.slice('https://v2pn.angleford.co'.length);
  });
}
