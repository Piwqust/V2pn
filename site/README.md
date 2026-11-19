# V2pn's website

The site served at https://v2pn.angleford.co: plain files, no build step on the server, no CDN, fonts served from
the site itself. Russian; the provider guide also in English (`/providers/guide/en/`), Configra's page switches to
English in place.

| URL | File | Made by |
| --- | --- | --- |
| `/` | `index.html` | by hand: the product, V2pn's window drawn as live HTML after the app's own screens and tokens |
| `/providers/` | `providers/index.html` | by hand: one client for every subscription; the subscription as it is, the tab, the special rights; the way in, «Создать партнёра» |
| `/providers/login/` | `providers/login/index.html` + `assets/site/login.js` | by hand: a login and a password, «Создать аккаунт» or «Войти»; moderators go on to `/admin/`, providers to «Мой партнёр» |
| `/providers/cabinet/` | `providers/cabinet/index.html` + `assets/site/cabinet.js` | by hand: «Мой партнёр», the one form beside V2pn's live window (light and dark, the partner's tab, the Scene, the sticker book), the link checked as it is typed, a PNG or JPG logo made into the PNG the hub takes, «Опубликовать», the special rights and their state, the share link, the password |
| `/admin/` | `admin/index.html` + `assets/site/admin.js` | by hand: moderation, for the owner and moderators: rights waiting (approve or deny the content seen), every partner (take down, restore, reset the password, complaints), moderators (the owner), downloads, the audit log |
| `/providers/guide/`, `/providers/guide/en/` | `providers/guide/index.html`, `providers/guide/en/index.html` | `tools/site.py` from `docs/partners/README.ru.md` and `README.md` |
| `/p/<id>/`, `/partners/` | not in this folder | the hub (`hub/lib/page.mjs`, `hub/lib/publish.mjs`) on the server, at every publication |
| `/api/*`, `/r/*`, `/releases/*` | not files | the hub's routes (`hub/README.md`), proxied by Caddy |

The provider pages share `assets/site/api.js` (requests to the hub with `X-Hub: 1`, errors in Russian, offline and
timeout states, the sign-in hint for the nav: «Мой партнёр» or «Модерация»), `assets/site/hubui.js` (the check's lines, V2pn's window as a preview,
copy buttons, confirmations) and `assets/site/hub.css` (the app screens in the site's system, and the dark mode of
every page that loads it, in V2pn's dark tokens). Every script is a file: the CSP allows no inline script.

### Working on the provider pages

`hub/dev/serve.mjs` serves this folder and the hub on one local origin under the live CSP, with an optional
stand-in subscription for the check (`--fixture`: names under `.test` resolve to this computer, which the real hub
refuses), Configra imported (`--configra`) and made-up download counts (`--downloads`). It prints the owner's login and
a new password at start. `hub/dev/shot.mjs` takes a headless screenshot of a page; `hub/dev/cdp.mjs` is the headless
Chrome it drives, for scripts that walk the pages. None of `hub/dev/` is deployed.

## Rebuild

```
pip install fonttools brotli          # once: the fonts are written as WOFF2
python tools/site.py                  # from the repository root
python tools/site.py --check          # the same, and prints the files to upload
```

`tools/site.py` rewrites every generated file listed above, the fonts (`assets/fonts`, from the app's Geist), the
window's map and flags (`assets/v2pn`, from `Assets/Map/world.json` and `Assets/Flags`) and the partners' pictures the
pages show (`assets/partners/<id>`). Edit the guide or the partner's files, never their output here.
Providers' own pages are no longer built here: the hub writes `/p/<id>/` on the server.

Not generated: `index.html`, `providers/index.html`, the provider pages (`providers/login`, `cabinet`,
`admin/`), `assets/site/` (the site's own CSS and scripts), `favicon.svg`, `share.jpg`, `providers/share.jpg` and
`assets/v2pn/shots/` (two captures of V2pn with Configra's tab, from `tools/edition-matrix.ps1 -Partner configra`,
cropped to the window and saved as WebP 1280 wide). The rest of `assets/` is the design-taste-frontend skill's kit
(2.5.1), copied unchanged; replace it only with a newer copy of the kit.

## Upload

The site's root on the server is `/var/www/v2pn.angleford.co`. Upload the folder as it is, keeping the paths, except
`README.md`, `.taste-skill/` (the design checks' history and waivers) and `shots/` (local screenshots, not in git).
`python tools/site.py --check` prints the exact list.

**Never delete `partners/` or `p/` on the server**: the hub owns them (the signed partner index and packs, every
provider's page, Configra's included). Upload without `--delete`, or exclude both (`rsync -a --exclude /partners/
--exclude /p/ …`). Today's list:

```
favicon.svg
index.html
share.jpg
admin/index.html
assets/system.css
assets/system.js
assets/backgrounds/backgrounds.css
assets/backgrounds/backgrounds.js
assets/diagrams/diagram.css
assets/diagrams/diagram.js
assets/diagrams/mount.js
assets/diagrams/presets.json
assets/effects/fx.js
assets/effects/silk-folds/effect.js
assets/effects/silk-folds/meta.json
assets/fonts/Geist-Medium.woff2
assets/fonts/Geist-OFL.txt
assets/fonts/Geist-Regular.woff2
assets/fonts/Geist-SemiBold.woff2
assets/fonts/GeistMono-Regular.woff2
assets/icons/apple.svg
assets/icons/github.svg
assets/icons/icon.css
assets/icons/icons-credits.json
assets/icons/image.svg
assets/icons/json.svg
assets/icons/telegram.svg
assets/icons/ui-download.svg
assets/icons/ui-file-archive.svg
assets/icons/ui-file-check.svg
assets/icons/ui-folder.svg
assets/icons/ui-package.svg
assets/icons/windows.svg
assets/icons/zip.svg
assets/layout/layout.css
assets/layout/layout.js
assets/moments/lib.js
assets/moments/moments.js
assets/moments/marquee-row/meta.json
assets/moments/marquee-row/moment.css
assets/moments/marquee-row/moment.js
assets/moments/word-highlight/meta.json
assets/moments/word-highlight/moment.css
assets/moments/word-highlight/moment.js
assets/motion/motion.css
assets/motion/motion.js
assets/partners/configra/logo-dark.png
assets/partners/configra/logo-light.png
assets/partners/configra/mark.png
assets/site/admin.js
assets/site/api.js
assets/site/cabinet.js
assets/site/head.js
assets/site/hub.css
assets/site/hubui.js
assets/site/login.js
assets/site/site.css
assets/site/site.js
assets/site/tokens.css
assets/ui/base.css
assets/ui/cards.css
assets/ui/ui.js
assets/v2pn/map-dark.svg
assets/v2pn/map-light.svg
assets/v2pn/flags/LICENSE.txt
assets/v2pn/flags/de.png
assets/v2pn/flags/eu.png
assets/v2pn/flags/fi.png
assets/v2pn/flags/fr.png
assets/v2pn/flags/gb.png
assets/v2pn/flags/kz.png
assets/v2pn/flags/lv.png
assets/v2pn/flags/nl.png
assets/v2pn/flags/pl.png
assets/v2pn/flags/ru.png
assets/v2pn/flags/se.png
assets/v2pn/flags/tr.png
assets/v2pn/flags/us.png
assets/v2pn/shots/configra-accent-light.webp
assets/v2pn/shots/configra-scene-dark.webp
providers/index.html
providers/share.jpg
providers/cabinet/index.html
providers/guide/index.html
providers/guide/partner.schema.json
providers/guide/en/index.html
providers/login/index.html
```

## Caddy

Every page is a folder with `index.html`, so `file_server` serves clean URLs as they are. The hub's paths go to it in
front of the files; every page works under this policy (scripts are files, no inline script; inline styles are used;
uploaded pictures are previewed as `data:` or same-origin URLs):

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
	header {
		Content-Security-Policy "default-src 'self'; base-uri 'none'; frame-ancestors 'none'; object-src 'none'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'"
		X-Content-Type-Options nosniff
		Referrer-Policy strict-origin-when-cross-origin
	}
	@partners path /partners/*
	header @partners Cache-Control "no-cache"
	@static path /assets/*
	header @static Cache-Control "public, max-age=86400"
}
```

`.woff2` and `.webp` need their media types (`font/woff2`, `image/webp`); Caddy knows both. The provider pages and the
home page's downloads (`/r/*`) need the hub running behind this block (`hub/README.md`, Deployment).

## Still to supply

- The owner's contact for providers who forgot their password: `/providers/login/` says a moderator resets it but names
  no channel yet. Add the owner's Telegram there once there is one.
