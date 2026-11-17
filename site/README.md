# V2pn's website

The site served at https://v2pn.angleford.co: plain files, no build step on the server, no CDN, fonts served from
the site itself. Russian; the provider guide also in English (`/providers/guide/en/`), the builder and Configra's
page switch to English in place.

| URL | File | Made by |
| --- | --- | --- |
| `/` | `index.html` | by hand: the product, V2pn's window drawn as live HTML after the app's own screens and tokens |
| `/providers/` | `providers/index.html` | by hand: what a provider's tab is, who sees it, how to become a partner |
| `/providers/guide/`, `/providers/guide/en/` | `providers/guide/index.html`, `providers/guide/en/index.html` | `tools/site.py` from `docs/partners/README.ru.md` and `README.md` |
| `/providers/builder/` | `providers/builder/index.html` and its scripts | `tools/site.py` from `docs/partners/builder/` (form, preview and scripts unchanged) |
| `/p/configra/` | `p/configra/` | `tools/site.py` through `tools/partner-page.py partners/configra --home ../../` |

## Rebuild

```
pip install fonttools brotli          # once: the fonts are written as WOFF2
python tools/site.py                  # from the repository root
python tools/site.py --check          # the same, and prints the files to upload
```

`tools/site.py` rewrites every generated file listed above, the fonts (`assets/fonts`, from the app's Geist), the
window's map and flags (`assets/v2pn`, from `Assets/Map/world.json` and `Assets/Flags`) and the partners' pictures
(`assets/partners/<id>`). Edit the guide, the builder, the partner's files or `tools/partner-page.py`, never their
output here. A new partner page: add its id to `PARTNERS` in `tools/site.py`.

Not generated: `index.html`, `providers/index.html`, `assets/site/` (the site's own CSS and scripts), `favicon.svg`,
`share.jpg`, `providers/share.jpg` and `assets/v2pn/shots/` (two captures of V2pn with Configra's tab, from
`tools/edition-matrix.ps1 -Partner configra`, cropped to the window and saved as WebP 1280 wide). The rest of `assets/`
is the design-taste-frontend skill's kit (2.5.1), copied unchanged; replace it only with a newer copy of the kit.

## Upload

Upload the folder as it is, keeping the paths, except `README.md`, `.taste-skill/` (the design checks' history and
waivers) and `shots/` (local screenshots, not in git). `python tools/site.py --check` prints the exact list. Today:

```
favicon.svg
index.html
share.jpg
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
assets/site/builder-site.css
assets/site/head.js
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
p/configra/index.html
p/configra/logo-dark.png
p/configra/logo-light.png
p/configra/page.js
p/configra/fonts/Geist-Regular.ttf
p/configra/fonts/Geist-SemiBold.ttf
providers/index.html
providers/share.jpg
providers/builder/builder.css
providers/builder/builder.js
providers/builder/index.html
providers/builder/v2pn-colors.js
providers/guide/index.html
providers/guide/partner.schema.json
providers/guide/en/index.html
```

## Caddy

Every page is a folder with `index.html`, so `file_server` serves clean URLs as they are. Every page works under this
policy (scripts are files, no inline script; inline styles are used):

```
v2pn.angleford.co {
	root * /srv/v2pn-site
	file_server
	header {
		Content-Security-Policy "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'"
		X-Content-Type-Options nosniff
		Referrer-Policy strict-origin-when-cross-origin
	}
	@static path /assets/*
	header @static Cache-Control "public, max-age=86400"
}
```

`.woff2` and `.webp` need their media types (`font/woff2`, `image/webp`); Caddy knows both.

## Still to supply

- The owner's contact for providers: the close of `/providers/` links `https://t.example/v2pn_owner`, a stand-in on
  `.example`. Replace it with the real Telegram link (search the page for `t.example`).
