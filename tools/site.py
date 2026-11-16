"""V2pn's website (site/, served at https://v2pn.angleford.co as plain files): builds its generated parts.

    python tools/site.py              # everything below, into site/
    python tools/site.py --check      # also says which files the upload holds

Hand-written: site/index.html (the product) and site/providers/index.html (for providers), with the kit in
site/assets (copied from the design-taste-frontend skill, unchanged) and site/assets/site (the site's own CSS and
scripts). Generated here, from their single sources in the repository:

- site/assets/fonts: Geist from the app (v2rayN/v2rayN.Desktop/Assets/Fonts).
- site/assets/v2pn: the world map of V2pn's window (Assets/Map/world.json, cropped to Europe as the window shows it)
  and the flags the window pictures use (Assets/Flags).
- site/assets/partners/<id>: the partner's logo and mark (partners/<id>), for the provider tab pictured on the pages.
- site/providers/guide/ and site/providers/guide/en/: the provider guide (docs/partners/README.ru.md, README.md),
  converted by tools/partner-docs.py's converter, in the site's header and footer.
- site/providers/builder/: the look builder (docs/partners/builder): its form and preview as they are, its scripts
  copied unchanged, in the site's header and footer.
- site/p/<id>/: each partner's download page, written by tools/partner-page.py (the generator stays the one source of
  that page; --home links its V2pn lockup back to the site).

Standard library only. Every page works under
    Content-Security-Policy: default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'
"""
import argparse
import html
import importlib.util
import os
import re
import shutil
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SITE = os.path.join(REPO, "site")
ASSETS = os.path.join(REPO, "v2rayN", "v2rayN.Desktop", "Assets")
DOCS = os.path.join(REPO, "docs", "partners")
RELEASES = "https://github.com/Piwqust/V2pn/releases"
REPO_URL = "https://github.com/Piwqust/V2pn"

# The flags the window pictures on the pages use (ISO codes, Assets/Flags/<code>.png).
FLAGS = ["de", "nl", "fi", "se", "pl", "ru", "tr", "kz", "us", "gb", "fr", "lv", "eu"]
# Partners shown on the site: their download page under /p/<id>/ and their pictures for the tab.
PARTNERS = ["configra"]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)


# ---------------------------------------------------------------- the window's map

def _rdp(pts, eps):
    """Ramer-Douglas-Peucker on a closed ring of integer points."""
    if len(pts) < 4:
        return pts
    keep = [False] * len(pts)
    keep[0] = keep[-1] = True
    stack = [(0, len(pts) - 1)]
    while stack:
        a, b = stack.pop()
        ax, ay = pts[a]
        bx, by = pts[b]
        dx, dy = bx - ax, by - ay
        norm = (dx * dx + dy * dy) ** 0.5 or 1.0
        best, idx = -1.0, -1
        for i in range(a + 1, b):
            px, py = pts[i]
            d = abs(dy * px - dx * py + bx * ay - by * ax) / norm
            if d > best:
                best, idx = d, i
        if best > eps and idx > 0:
            keep[idx] = True
            stack += [(a, idx), (idx, b)]
    return [p for p, k in zip(pts, keep) if k]


# The part of the world V2pn's window shows around Europe (the map's own 2400 x 1560 units).
MAP_VIEW = (560, 240, 1440, 900)
MAP_COLOURS = {
    "light": {"ocean": "#E6E9F0", "land": "#FAFBFC", "border": "#CDD2DC"},
    "dark": {"ocean": "#0E0E11", "land": "#24242C", "border": "#34343F"},
}


def build_map(out):
    import json
    with open(os.path.join(ASSETS, "Map", "world.json"), encoding="utf-8") as fh:
        world = json.load(fh)
    x0, y0, w, h = MAP_VIEW
    pad = 40
    paths = []
    for code, c in sorted(world["countries"].items()):
        bx0, by0, bx1, by1 = c["b"]
        if bx1 < x0 - pad or bx0 > x0 + w + pad or by1 < y0 - pad or by0 > y0 + h + pad:
            continue
        rings = []
        for sub in re.findall(r"M[^M]+", c["d"]):
            pts = [(int(a), int(b)) for a, b in re.findall(r"(-?\d+),(-?\d+)", sub)]
            pts = _rdp(pts, 1.2)
            if len(pts) < 3:
                continue
            xs, ys = [p[0] for p in pts], [p[1] for p in pts]
            if max(xs) - min(xs) < 3 and max(ys) - min(ys) < 3:
                continue
            rings.append("M" + "L".join(f"{x - x0},{y - y0}" for x, y in pts) + "Z")
        if rings:
            paths.append("".join(rings))
    d = "".join(paths)
    for mode, col in MAP_COLOURS.items():
        svg = (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" preserveAspectRatio="xMidYMid slice">'
               f'<rect width="{w}" height="{h}" fill="{col["ocean"]}"/>'
               f'<path d="{d}" fill="{col["land"]}" stroke="{col["border"]}" stroke-width="1.2" stroke-linejoin="round"/></svg>')
        write(os.path.join(out, f"map-{mode}.svg"), svg)


# ---------------------------------------------------------------- copies

def copy_assets():
    fonts = os.path.join(SITE, "assets", "fonts")
    os.makedirs(fonts, exist_ok=True)
    for f in ("Geist-Regular.ttf", "Geist-Medium.ttf", "Geist-SemiBold.ttf", "GeistMono-Regular.ttf", "Geist-OFL.txt"):
        shutil.copy(os.path.join(ASSETS, "Fonts", f), os.path.join(fonts, f))
    v2 = os.path.join(SITE, "assets", "v2pn")
    os.makedirs(os.path.join(v2, "flags"), exist_ok=True)
    for code in FLAGS:
        src = os.path.join(ASSETS, "Flags", f"{code}.png")
        if os.path.isfile(src):
            shutil.copy(src, os.path.join(v2, "flags", f"{code}.png"))
    shutil.copy(os.path.join(ASSETS, "Flags", "LICENSE-flag-icons.txt"), os.path.join(v2, "flags", "LICENSE.txt"))
    build_map(v2)
    for pid in PARTNERS:
        dst = os.path.join(SITE, "assets", "partners", pid)
        os.makedirs(dst, exist_ok=True)
        for f in ("mark.png", "logo-light.png", "logo-dark.png"):
            src = os.path.join(REPO, "partners", pid, f)
            if os.path.isfile(src):
                shutil.copy(src, os.path.join(dst, f))


# ---------------------------------------------------------------- the site's header and footer (guide, builder)

V2MARK = ('<svg class="v2mark" viewBox="0 0 104 112" aria-hidden="true"><path d="M0,0 L32,0 L32,40 L72,0 L104,0 L104,32 '
          'L85.255,32 L64,53.255 L64,112 L32,112 L32,72 L0,40 Z"/></svg>')


def head(title, description, root, extra=""):
    return f"""<!doctype html>
<html lang="ru" data-type="software" data-radius="round" data-buttons="solid" data-motion>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<title>{html.escape(title)}</title>
<meta name="description" content="{html.escape(description)}">
<meta property="og:type" content="website">
<meta property="og:title" content="{html.escape(title)}">
<meta property="og:description" content="{html.escape(description)}">
<meta property="og:image" content="https://v2pn.angleford.co/share.jpg">
<meta name="twitter:card" content="summary_large_image">
<link rel="icon" href="{root}favicon.svg" type="image/svg+xml">
<meta name="theme-color" content="#eceef3">
<meta name="site-kind" content="software">
<meta name="page-type" content="software">
<link rel="stylesheet" href="{root}assets/system.css">
<link rel="stylesheet" href="{root}assets/motion/motion.css">
<link rel="stylesheet" href="{root}assets/site/tokens.css">
<link rel="stylesheet" href="{root}assets/site/site.css">
{extra}</head>
"""


def nav(root, current):
    def item(href, word, key):
        cur = ' aria-current="page"' if key == current else ""
        return f'<div class="nav__item"><a class="nav__link" href="{root}{href}"{cur}>{word}</a></div>'
    return f"""<header class="nav nav--centre">
  <div class="wrap nav__bar">
    <a class="skip-link" href="#top">К содержанию</a>
    <a class="nav__logo" href="{root}">{V2MARK}V2pn</a>
    <nav class="nav__links" aria-label="Разделы">
      {item("#features", "Возможности", "")}
      {item("#install", "Установка", "")}
      {item("providers/", "Провайдерам", "providers")}
      {item("providers/guide/", "Руководство", "guide")}
      {item("providers/builder/", "Конструктор", "builder")}
    </nav>
    <div class="nav__actions">
      <a class="btn btn--primary" href="{root}providers/builder/">Собрать вкладку</a>
    </div>
    <button class="nav__toggle" type="button" aria-expanded="false" aria-label="Меню">
      <svg width="18" height="18" viewBox="0 0 18 18" aria-hidden="true"><path d="M2 5h14M2 13h14" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/></svg>
    </button>
  </div>
</header>
"""


def footer(root):
    return f"""<footer class="footer site-foot" id="contact">
  <div class="wrap">
    <div class="closing__links">
      <div class="grid">
        <div class="span-4 stack-4">
          <a class="nav__logo" href="{root}">{V2MARK}V2pn</a>
          <p class="footer__lede">Бесплатный VPN-клиент с открытым кодом для Windows и macOS.</p>
        </div>
        <div class="span-2 footer__col"><h3>Приложение</h3><ul><li><a href="{root}#app">Окно</a></li><li><a href="{root}#features">Возможности</a></li><li><a href="{root}#install">Установка</a></li><li><a href="{root}#faq">Вопросы</a></li></ul></div>
        <div class="span-2 footer__col"><h3>Провайдерам</h3><ul><li><a href="{root}providers/">Вкладка провайдера</a></li><li><a href="{root}providers/guide/">Руководство</a></li><li><a href="{root}providers/builder/">Конструктор</a></li><li><a href="{root}p/configra/">Configra</a></li></ul></div>
        <div class="span-2 footer__col"><h3>Проект</h3><ul><li><a href="{REPO_URL}">Код на GitHub</a></li><li><a href="{REPO_URL}/blob/v2pn/CHANGELOG.md">Что нового</a></li><li><a href="{RELEASES}">Все релизы</a></li><li><a href="{REPO_URL}/issues/new/choose">Сообщить о проблеме</a></li></ul></div>
      </div>
      <div class="footer__legal">
        <span>&copy; 2026 V2pn &nbsp; <a href="{REPO_URL}/blob/v2pn/LICENSE">Лицензия GPL-3.0</a></span>
        <span class="credits">Названия и логотипы продуктов принадлежат их владельцам.</span>
      </div>
    </div>
  </div>
</footer>
"""


SCRIPTS = """<script type="module" src="{root}assets/system.js"></script>
<script type="module" src="{root}assets/motion/motion.js"></script>
"""


# ---------------------------------------------------------------- the guide

def build_guide():
    docs = load("partner_docs", os.path.join(REPO, "tools", "partner-docs.py"))
    pages = [("ru", "README.ru.md", os.path.join(SITE, "providers", "guide"), "../../"),
             ("en", "README.md", os.path.join(SITE, "providers", "guide", "en"), "../../../")]
    for lang, source, out, root in pages:
        with open(os.path.join(DOCS, source), encoding="utf-8") as fh:
            md = fh.read()
        md = re.sub(r"^\[(По-русски|In English)\]\([^)]+\)\s*$", "", md, flags=re.MULTILINE)
        body, toc = docs.convert(md)
        body = body.replace("<p>", '<p class="doc__lead">', 1)
        # Links of the Markdown, as the site serves them.
        here = {"ru": {"en.html": "en/", "index.html": "./", "builder/": "../builder/",
                       "partner.schema.json": "partner.schema.json"},
                "en": {"en.html": "./", "index.html": "../", "builder/": "../../builder/?lang=en",
                       "partner.schema.json": "../partner.schema.json"}}[lang]
        body = re.sub(r'href="([^"#]+)"', lambda m: f'href="{here.get(m.group(1), m.group(1))}"', body)
        m = re.search(r"^#\s+(.*)$", md, flags=re.MULTILINE)
        title = m.group(1).strip() if m else "V2pn"
        # The page's own h1 comes from the template; the converted body starts with it.
        body = re.sub(r"^<h1[^>]*>.*?</h1>\s*", "", body, count=1, flags=re.S)
        links = "".join(f'<a class="l{level}" href="#{hid}">{html.escape(text)}</a>' for level, hid, text in toc if level == 2)
        switch = ('<a class="doc__lang" href="en/" hreflang="en">In English</a>' if lang == "ru"
                  else '<a class="doc__lang" href="../" hreflang="ru">По-русски</a>')
        desc = ("Как VPN-провайдеру получить свою вкладку в V2pn: оформление, вход по коду, API, страница загрузки."
                if lang == "ru" else
                "How a VPN provider gets its own tab in V2pn: its look, sign-in by code, the API, the download page.")
        page = (head(f"{title} | V2pn", desc, root)
                .replace('<html lang="ru"', f'<html lang="{lang}"')
                + "<body>\n<!-- generated by tools/site.py from docs/partners/" + source + "; edit the guide there -->\n"
                + nav(root, "guide")
                + f"""<main id="top" class="doc">
  <div class="wrap doc__grid">
    <aside class="doc__toc" aria-label="{'Разделы руководства' if lang == 'ru' else 'Sections'}">
      <p class="t-label">{'Разделы' if lang == 'ru' else 'Sections'}</p>
      <nav>{links}</nav>
    </aside>
    <article class="doc__body">
      <p class="doc__kicker"><a href="{root}providers/">{'Провайдерам' if lang == 'ru' else 'For providers'}</a> · {switch}</p>
      <h1>{docs.inline(title)}</h1>
      {body}
    </article>
  </div>
</main>
"""
                + footer(root) + SCRIPTS.format(root=root) + "</body>\n</html>\n")
        write(os.path.join(out, "index.html"), page)
    shutil.copy(os.path.join(DOCS, "partner.schema.json"), os.path.join(SITE, "providers", "guide", "partner.schema.json"))


# ---------------------------------------------------------------- the builder

def build_builder():
    src = os.path.join(DOCS, "builder")
    out = os.path.join(SITE, "providers", "builder")
    os.makedirs(out, exist_ok=True)
    for f in ("builder.js", "v2pn-colors.js", "builder.css"):
        shutil.copy(os.path.join(src, f), os.path.join(out, f))
    with open(os.path.join(src, "index.html"), encoding="utf-8") as fh:
        text = fh.read()
    m = re.search(r'<div class="layout">.*?</div>\s*(?=<script)', text, flags=re.S)
    if not m:
        raise SystemExit("docs/partners/builder/index.html: no <div class=\"layout\"> before its scripts")
    layout = m.group(0).strip()
    # builder.js reads its own bar: the link home (.brand, swapped by language) and the language switch (.top [data-lang]).
    bar = """<div class="top builder-bar">
  <div class="wrap builder-bar__in">
    <a class="brand" href="../guide/" data-href-ru="../guide/" data-href-en="../guide/en/">
      <span data-l="forProviders">для провайдеров</span>
    </a>
    <div class="sp"></div>
    <div class="seg small" role="group" aria-label="Язык" data-l-label="language">
      <button type="button" data-lang="ru" aria-pressed="true">RU</button><button type="button" data-lang="en" aria-pressed="false">EN</button>
    </div>
  </div>
</div>
"""
    root = "../../"
    page = (head("Оформление партнёра V2pn", "Соберите оформление V2pn для своей подписки и посмотрите его в окне V2pn.", root,
                 extra='<link rel="stylesheet" href="builder.css">\n<link rel="stylesheet" href="../../assets/site/builder-site.css">\n')
            + "<body class=\"builder-page\">\n<!-- generated by tools/site.py from docs/partners/builder/index.html; edit the builder there -->\n"
            + nav(root, "builder")
            + '<main id="top" class="builder-main">\n' + bar + layout + "\n</main>\n"
            + footer(root)
            + '<script src="v2pn-colors.js"></script>\n<script src="builder.js"></script>\n'
            + SCRIPTS.format(root=root) + "</body>\n</html>\n")
    write(os.path.join(out, "index.html"), page)


# ---------------------------------------------------------------- partner pages

def build_partners():
    gen = load("partner_page", os.path.join(REPO, "tools", "partner-page.py"))
    for pid in PARTNERS:
        out = os.path.join(SITE, "p", pid)
        gen.build(os.path.join(REPO, "partners", pid), out, gen.release_source().rstrip("/"), home="../../")


# ---------------------------------------------------------------- the upload

def upload_list():
    skip_dirs = {".taste-skill", "shots", "names"}
    files = []
    for folder, dirs, names in os.walk(SITE):
        dirs[:] = sorted(d for d in dirs if d not in skip_dirs)
        for n in sorted(names):
            rel = os.path.relpath(os.path.join(folder, n), SITE).replace(os.sep, "/")
            if rel in ("README.md", "names.json") or rel.endswith((".md",)):
                continue
            files.append(rel)
    return files


def main():
    ap = argparse.ArgumentParser(description="Build the generated parts of V2pn's website in site/.")
    ap.add_argument("--check", action="store_true", help="print the files to upload")
    args = ap.parse_args()
    copy_assets()
    build_guide()
    build_builder()
    build_partners()
    files = upload_list()
    print(f"site/: {len(files)} files to upload")
    if args.check:
        for f in files:
            print("  " + f)


if __name__ == "__main__":
    main()
