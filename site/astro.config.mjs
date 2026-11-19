// V2pn's website: static files for v2pn.angleford.co (see README.md).
// The server's CSP allows no inline script, so nothing may be inlined: scripts are always files (checked after
// every build by scripts/check-build.mjs). `static/` is the public folder; `public/` is not ours.
import { defineConfig } from 'astro/config';
import sitemap from '@astrojs/sitemap';
import { guideLinks } from './src/content/guide-links.mjs';

const PRIVATE = ['/providers/login/', '/providers/cabinet/', '/admin/', '/404'];

export default defineConfig({
  site: 'https://v2pn.angleford.co',
  publicDir: './static',
  outDir: './dist',
  trailingSlash: 'always',
  build: { format: 'directory', inlineStylesheets: 'never', assets: '_astro' },
  integrations: [sitemap({ filter: (page) => !PRIVATE.some((p) => new URL(page).pathname.startsWith(p)) })],
  prefetch: { prefetchAll: false, defaultStrategy: 'hover' },
  markdown: { syntaxHighlight: false, rehypePlugins: [guideLinks] },
  devToolbar: { enabled: false },
  vite: {
    build: { assetsInlineLimit: 0 },
  },
});
