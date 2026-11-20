# V2pn's website

https://v2pn.angleford.co, built with [Astro](https://astro.build) into static files that Caddy serves as they are.
The first site (hand-written HTML) is kept in `archive/site-v1/`. The idea, the palette, the type and the grid are in
[DESIGN.md](DESIGN.md).

| URL | Source | What it does |
| --- | --- | --- |
| `/` | `src/pages/index.astro` | the product: «Развилка» (the fork, `src/motion/fork.ts`), the window, the check, the provider tab, downloads, questions |
| `/download/` | `src/pages/download/` | the file for this computer, every file, the one command, three steps after the download |
| `/providers/` | `src/pages/providers/index.astro` | for providers: three levels, the link with `#sub=`, the headers, how to make a tab |
| `/providers/login/` | `…/login/` + `src/app/login.js` | a login and a password: «Войти» or «Создать аккаунт» |
| `/providers/cabinet/` | `…/cabinet/` + `src/app/cabinet.js` | «Мой партнёр»: the form beside V2pn's live window, the check of the link, publish, special rights, the share link, the password |
| `/admin/` | `src/pages/admin/` + `src/app/admin.js` | moderation: rights waiting, partners, complaints, moderators, downloads, the audit log |
| `/docs/`, `/docs/<page>/` | `src/docs/*.md` | the user documentation |
| `/providers/guide/`, `/providers/guide/en/` | `docs/partners/README.ru.md`, `README.md` | the provider guide, rendered from the repository's copy (edit it there) |
| `/providers/guide/partner.schema.json` | `docs/partners/partner.schema.json` | copied at every build (`scripts/prepare.mjs`) |
| `/404.html` | `src/pages/404.astro` | the page Caddy shows for an unknown path (see Caddy below) |
| `/p/<id>/`, `/partners/` | not here | the hub writes them on the server (`hub/lib/page.mjs`, `hub/lib/publish.mjs`) |
| `/api/*`, `/r/*`, `/releases/*` | not files | the hub's routes, proxied by Caddy |

`static/` is the public folder (Astro's `public/` name is taken: `site/public/` holds the owner's own files and is
not part of the build). Stable paths other code relies on live there:

- `static/assets/window.css`: V2pn's window drawn in HTML, used by «Мой партнёр» and by every `/p/<id>/` page the hub
  writes. Keep its class names.
- `static/assets/fonts/`: Inter Tight, Inter and JetBrains Mono (OFL), latin and cyrillic; the hub's pages load them.
- `static/assets/v2pn/`: the flags and maps the window pictures use. `static/assets/partners/configra/`: Configra's
  pictures on the providers page.
- `static/assets/head.js`: the flags set before the first paint (a file: the CSP allows no inline script).

Words and facts of the marketing pages are in `src/data/site.ts` and `src/data/routes.ts` (the route presets and the
sites the fork sends up its stem). The version in the footer and on the download page is the newest `## x.y.z` of
`CHANGELOG.md` at build time.

## Build

```
cd site
npm install            # once
npm run build          # dist/ ; then scripts/check-build.mjs fails the build on any inline script or foreign file
```

## Work on it locally

The hub's dev server serves `dist/` and the hub's API on one origin, under the live CSP, with a stand-in
subscription for the link check, Configra imported and made-up download counts:

```
cd site && npm run build
cd ../hub && node dev/serve.mjs --fixture --configra --downloads
```

It prints the owner's login and password (for `/admin/`) and the stand-in subscription link (for «Мой партнёр»).
`npm run dev` in `site/` also works for the marketing pages, without the hub.

## Upload

The site's root on the server is `/var/www/v2pn.angleford.co`. Upload the contents of `dist/`, keeping the paths:

```
rsync -a dist/ root@<server>:/var/www/v2pn.angleford.co/
```

**Never delete `partners/` or `p/` on the server**: the hub owns them. Upload without `--delete`.

Order with the hub, the first time:

1. Upload `dist/` (it brings `/assets/window.css` and the new fonts; the old `/assets/site/` stays on the server, so
   the providers' pages written by the old hub keep working).
2. Deploy `hub/` (its `lib/page.mjs` now writes the pages in the new system).
3. In `/admin/`, «Все партнёры» → «Опубликовать заново»: every `/p/<id>/` is rewritten.
4. After that the old files of the first site can go from the server: `assets/site/`, `assets/system.*`, `assets/ui/`,
   `assets/layout/`, `assets/motion/`, `assets/moments/`, `assets/backgrounds/`, `assets/diagrams/`,
   `assets/effects/`, `assets/icons/`, `assets/fonts/Geist*`, `assets/v2pn/shots/`, `providers/share.jpg`,
   `share.jpg`.

## Caddy

Every page is a folder with `index.html`, so `file_server` serves clean URLs. Add `handle_errors` so an unknown path
gets the designed 404 page:

```
v2pn.angleford.co {
	root * /var/www/v2pn.angleford.co
	@hub path /api/* /r/* /releases/*
	handle @hub {
		request_body {
			max_size 3MB
		}
		reverse_proxy 127.0.0.1:18190
	}
	handle {
		file_server
	}
	handle_errors 404 {
		rewrite * /404.html
		file_server
	}
	header {
		Content-Security-Policy "default-src 'self'; base-uri 'none'; frame-ancestors 'none'; object-src 'none'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'"
		X-Content-Type-Options nosniff
		Referrer-Policy strict-origin-when-cross-origin
	}
	@partners path /partners/*
	header @partners Cache-Control "no-cache"
	@static path /assets/* /_astro/*
	header @static Cache-Control "public, max-age=86400"
	@hashed path /_astro/*
	header @hashed Cache-Control "public, max-age=31536000, immutable"
}
```

`handle_errors 404` with a status code needs Caddy 2.8 or newer. `.woff2`, `.webp` and `.avif` need their media types; Caddy knows all three.

## Still to replace

- The window on the home page and the last download step are Russian captures of the current V2pn (2026-11-20,
  `src/assets/shots/ru/`, demo subscription; the pings other than Frankfurt's were seeded). The first two download
  steps (`welcome`, `import`) are still English captures of the first-run walkthrough (`placeholder: true` in
  `src/data/site.ts`). New captures: `tools/sandbox.ps1 -Launch` with `tools/demo/demo_sub.py`, then `tools/shot.ps1`;
  open popups from code, not with UI Automation (it can take focus).
- Configra's tab captures (`configra-*.webp`, home page and `/providers/`) are English; Russian ones would match the
  pages. The partner tab needs `tools/sandbox.ps1 -Partner configra`.
- The provider guide (`docs/partners/README*.md`) keeps its long dashes; the site renders it as written.
- Downloads go to the ZIP for Windows. When the Windows installer (CHANGELOG, Unreleased) ships, add its short names to
  `hub/lib/downloads.mjs` and point `downloads` in `src/data/site.ts` at them.
- The owner's contact for providers who forgot their password: `/providers/login/` says a moderator resets it but
  names no channel yet.
