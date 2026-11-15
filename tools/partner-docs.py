"""The provider guide (docs/partners) as a small local website.

    python tools/partner-docs.py                 # build into %TEMP%/v2pn-partner-docs and serve on 127.0.0.1:8765
    python tools/partner-docs.py --no-serve      # only build
    python tools/partner-docs.py --shots <dir>   # pictures of the theme templates from tools/edition-matrix.ps1 -Theme

Standard library only: a converter for the Markdown the guide uses (headings, paragraphs, lists, tables, code blocks,
inline code, bold, links), V2pn's fonts from the app's assets, light and dark by the system's setting.
"""
import argparse
import html
import http.server
import os
import re
import shutil
import socketserver
import sys
import tempfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DOCS = os.path.join(REPO, "docs", "partners")
FONTS = os.path.join(REPO, "v2rayN", "v2rayN.Desktop", "Assets", "Fonts")

# The theme templates, in the order the guide lists them. Their pictures are what tools/edition-matrix.ps1 -Theme
# writes ("<theme>-<template>-<mode>-<size>.png"), from any theme, in --shots or a folder under it.
TEMPLATES = [
    ("center", "Центр", "Centre"),
    ("half", "Половина", "Half"),
    ("split", "Две колонки", "Split"),
    ("list", "Список", "List"),
    ("map", "Карта", "Map"),
    ("compact", "Карточка", "Card"),
]
MODES = (("Тёмный", "dark"), ("Светлый", "light"))


def template_shots(shots):
    """{(template, mode): path} of the wide window's pictures, the first theme found for each."""
    found = {}
    if not shots:
        return found
    for folder, _, files in sorted(os.walk(shots)):
        for f in sorted(files):
            for key, _, _ in TEMPLATES:
                for mode_ru, mode_en in MODES:
                    if f.endswith(f"-{key}-{mode_ru}-Широкое.png") and (key, mode_en) not in found:
                        found[(key, mode_en)] = os.path.join(folder, f)
    return found


def slug(text):
    s = re.sub(r"[^\w\s-]", "", text.lower(), flags=re.UNICODE).strip()
    return re.sub(r"[\s_]+", "-", s) or "section"


def inline(text):
    """Inline Markdown: code first (its content stays literal), then links, bold, italics."""
    parts = re.split(r"(`[^`]+`)", text)
    out = []
    for part in parts:
        if part.startswith("`") and part.endswith("`") and len(part) > 1:
            out.append("<code>" + html.escape(part[1:-1]) + "</code>")
            continue
        p = html.escape(part, quote=False)
        p = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", lambda m: f'<a href="{fix_link(m.group(2))}">{m.group(1)}</a>', p)
        p = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", p)
        p = re.sub(r"(?<![\w*])\*([^*\s][^*]*)\*(?![\w*])", r"<em>\1</em>", p)
        out.append(p)
    return "".join(out)


def fix_link(target):
    # The guide links its two languages and the schema by file name.
    return {"README.md": "en.html", "README.ru.md": "index.html"}.get(target, target)


def convert(md):
    """Markdown to HTML for this guide; returns (html, [(level, id, text)])."""
    lines = md.replace("\r\n", "\n").split("\n")
    out, toc, i = [], [], 0
    para = []

    def flush():
        if para:
            out.append("<p>" + inline(" ".join(para)) + "</p>")
            para.clear()

    while i < len(lines):
        line = lines[i]
        if line.startswith("```"):
            flush()
            lang = line[3:].strip()
            block = []
            i += 1
            while i < len(lines) and not lines[i].startswith("```"):
                block.append(lines[i])
                i += 1
            out.append(f'<pre class="code" data-lang="{html.escape(lang)}"><code>' + html.escape("\n".join(block)) + "</code></pre>")
            i += 1
            continue
        m = re.match(r"^(#{1,4})\s+(.*)$", line)
        if m:
            flush()
            level, text = len(m.group(1)), m.group(2).strip()
            hid = slug(text)
            if level > 1:
                toc.append((level, hid, re.sub(r"[`*]", "", text)))
            out.append(f'<h{level} id="{hid}">{inline(text)}</h{level}>')
            i += 1
            continue
        if line.startswith("|") and i + 1 < len(lines) and re.match(r"^\|\s*:?-", lines[i + 1]):
            flush()
            header = [c.strip() for c in line.strip().strip("|").split("|")]
            rows = []
            i += 2
            while i < len(lines) and lines[i].startswith("|"):
                rows.append([c.strip() for c in lines[i].strip().strip("|").split("|")])
                i += 1
            t = ['<div class="table"><table><thead><tr>']
            t += [f"<th>{inline(h)}</th>" for h in header]
            t.append("</tr></thead><tbody>")
            for r in rows:
                # The column name goes with each cell: on a phone the rows stack and the cells carry their own labels.
                t.append("<tr>" + "".join(
                    f'<td data-label="{html.escape(re.sub(r"[`*]", "", header[k]) if k < len(header) else "")}">{inline(c)}</td>'
                    for k, c in enumerate(r)) + "</tr>")
            t.append("</tbody></table></div>")
            out.append("".join(t))
            continue
        m = re.match(r"^(\s*)([-*]|\d+\.)\s+(.*)$", line)
        if m:
            flush()
            ordered = m.group(2)[0].isdigit()
            tag = "ol" if ordered else "ul"
            items = []
            while i < len(lines):
                m = re.match(r"^(\s*)([-*]|\d+\.)\s+(.*)$", lines[i])
                if m and not m.group(1):
                    items.append([m.group(3)])
                elif m and m.group(1) and items:
                    items[-1].append("\u0000" + m.group(3))
                elif lines[i].startswith("   ") and items and lines[i].strip():
                    items[-1][-1] += " " + lines[i].strip()
                else:
                    break
                i += 1
            html_items = []
            for item in items:
                head, subs = item[0], [s[1:] for s in item[1:]]
                sub = ("<ul>" + "".join(f"<li>{inline(s)}</li>" for s in subs) + "</ul>") if subs else ""
                html_items.append(f"<li>{inline(head)}{sub}</li>")
            out.append(f"<{tag}>" + "".join(html_items) + f"</{tag}>")
            continue
        if not line.strip():
            flush()
            i += 1
            continue
        para.append(line.strip())
        i += 1
    flush()
    return "\n".join(out), toc


def gallery(lang, found):
    cards = []
    for key, ru, en in TEMPLATES:
        name = ru if lang == "ru" else en
        pics = [(f"templates/{key}-{mode_en}.png", mode_ru.lower() if lang == "ru" else mode_en)
                for mode_ru, mode_en in MODES if (key, mode_en) in found]
        if not pics:
            continue
        imgs = "".join(f'<figure><a href="{src}" target="_blank"><img src="{src}" alt="{html.escape(name)}, {label}" loading="lazy"></a><figcaption>{label}</figcaption></figure>' for src, label in pics)
        cards.append(f'<div class="look"><div class="look-name"><code>{key}</code> {html.escape(name)}</div><div class="look-pics">{imgs}</div></div>')
    return '<div class="gallery">' + "".join(cards) + "</div>" if cards else ""


PAGE = """<!doctype html>
<html lang="{lang}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{title}</title>
<style>
@font-face {{ font-family: Geist; src: url(fonts/Geist-Regular.ttf); font-weight: 400; }}
@font-face {{ font-family: Geist; src: url(fonts/Geist-Medium.ttf); font-weight: 500; }}
@font-face {{ font-family: Geist; src: url(fonts/Geist-SemiBold.ttf); font-weight: 600; }}
@font-face {{ font-family: "Geist Mono"; src: url(fonts/GeistMono-Regular.ttf); font-weight: 400; }}
:root {{
  --bg: #FAFAF8; --panel: #FFFFFF; --ink: #16161B; --soft: #3A3A44; --muted: #6A6A76; --line: #E6E6EA;
  --code: #F2F2F4; --accent: #E8391B; --accent-ink: #B5260C; --table-head: #F4F4F6;
}}
@media (prefers-color-scheme: dark) {{
  :root {{
    --bg: #0E0E12; --panel: #16161C; --ink: #ECECF1; --soft: #C9C9D2; --muted: #8C8C98; --line: #26262E;
    --code: #1D1D24; --accent: #FF3D1F; --accent-ink: #FF7A5F; --table-head: #1A1A21;
  }}
}}
* {{ box-sizing: border-box; }}
html {{ scroll-behavior: smooth; scroll-padding-top: 76px; }}
body {{ margin: 0; background: var(--bg); color: var(--ink); font: 16px/1.62 Geist, "Segoe UI", system-ui, sans-serif; -webkit-font-smoothing: antialiased; }}
header {{ position: sticky; top: 0; z-index: 5; display: flex; align-items: center; gap: 14px; padding: 14px 28px;
  background: color-mix(in srgb, var(--bg) 86%, transparent); backdrop-filter: blur(14px); border-bottom: 1px solid var(--line); }}
.brand {{ display: flex; align-items: center; gap: 10px; font-weight: 600; letter-spacing: -0.01em; }}
.brand svg {{ width: 18px; height: 19px; }}
.brand span {{ color: var(--muted); font-weight: 400; }}
.spacer {{ flex: 1; }}
.switch {{ display: inline-flex; padding: 3px; border-radius: 10px; background: var(--code); }}
.switch a {{ padding: 5px 12px; border-radius: 8px; color: var(--muted); text-decoration: none; font-size: 14px; font-weight: 500; }}
.switch a.on {{ background: var(--panel); color: var(--ink); box-shadow: 0 1px 2px rgba(0,0,0,.08); }}
.schema {{ color: var(--muted); font-size: 14px; text-decoration: none; }}
.schema:hover {{ color: var(--ink); }}
.wrap {{ display: grid; grid-template-columns: 250px minmax(0, 1fr); gap: 56px; max-width: 1180px; margin: 0 auto; padding: 36px 28px 96px; }}
nav {{ position: sticky; top: 92px; align-self: start; max-height: calc(100vh - 120px); overflow: auto; font-size: 14px; }}
nav a {{ display: block; padding: 5px 10px; border-left: 2px solid transparent; color: var(--muted); text-decoration: none; line-height: 1.35; }}
nav a.l3 {{ padding-left: 22px; font-size: 13px; }}
nav a:hover {{ color: var(--ink); }}
nav a.active {{ color: var(--ink); border-left-color: var(--accent); }}
main {{ min-width: 0; max-width: 780px; }}
h1 {{ font-size: 40px; line-height: 1.12; letter-spacing: -0.03em; font-weight: 600; margin: 4px 0 18px; }}
h2 {{ font-size: 26px; letter-spacing: -0.02em; font-weight: 600; margin: 56px 0 14px; padding-top: 8px; }}
h3 {{ font-size: 19px; font-weight: 600; margin: 32px 0 10px; }}
p, li {{ color: var(--soft); }}
main > p:first-of-type + p, main > h1 + p + p {{ }}
a {{ color: var(--accent-ink); text-underline-offset: 3px; }}
strong {{ color: var(--ink); font-weight: 600; }}
code {{ font-family: "Geist Mono", Consolas, monospace; font-size: 0.86em; background: var(--code); padding: 2px 6px; border-radius: 6px; color: var(--ink); }}
pre.code {{ background: var(--code); border: 1px solid var(--line); border-radius: 12px; padding: 16px 18px; overflow: auto; font-size: 13.5px; line-height: 1.55; }}
pre.code code {{ background: none; padding: 0; font-size: inherit; }}
ul, ol {{ padding-left: 22px; }}
li {{ margin: 5px 0; }}
li > ul {{ margin: 4px 0; }}
.table {{ overflow-x: auto; margin: 18px 0; border: 1px solid var(--line); border-radius: 12px; background: var(--panel); }}
table {{ border-collapse: collapse; width: 100%; font-size: 14.5px; }}
th, td {{ text-align: left; vertical-align: top; padding: 10px 14px; border-bottom: 1px solid var(--line); }}
th {{ background: var(--table-head); font-weight: 600; color: var(--ink); white-space: nowrap; }}
tr:last-child td {{ border-bottom: 0; }}
td {{ color: var(--soft); }}
.lead {{ font-size: 18.5px; color: var(--ink); }}
.gallery {{ display: grid; gap: 18px; margin: 22px 0 8px; }}
.look {{ border: 1px solid var(--line); border-radius: 14px; background: var(--panel); padding: 14px; }}
.look-name {{ font-weight: 600; margin: 0 0 10px; display: flex; gap: 10px; align-items: center; }}
.look-pics {{ display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }}
figure {{ margin: 0; }}
figure img {{ width: 100%; display: block; border-radius: 8px; border: 1px solid var(--line); }}
figcaption {{ font-size: 12px; color: var(--muted); margin-top: 4px; }}
footer {{ color: var(--muted); font-size: 13px; margin-top: 64px; padding-top: 18px; border-top: 1px solid var(--line); }}
@media (max-width: 900px) {{
  .wrap {{ grid-template-columns: 1fr; gap: 0; padding: 24px 16px 72px; }}
  nav {{ display: none; }}
  header {{ padding: 12px 16px; }}
  h1 {{ font-size: 30px; }}
  .look-pics {{ grid-template-columns: 1fr; }}
}}
@media (max-width: 560px) {{
  .schema {{ display: none; }}
  h1 {{ font-size: 26px; }}
  pre.code {{ font-size: 12.5px; }}
  .table {{ overflow: visible; }}
  table, tbody, tr, td {{ display: block; width: 100%; }}
  thead {{ display: none; }}
  tr {{ padding: 12px 14px; border-bottom: 1px solid var(--line); }}
  tr:last-child {{ border-bottom: 0; }}
  td {{ padding: 2px 0; border: 0; }}
  td:first-child {{ color: var(--ink); font-weight: 500; padding-bottom: 6px; }}
  td:not(:first-child)[data-label]:not([data-label=""])::before {{
    content: attr(data-label) " · "; color: var(--muted); font-size: 13px; }}
}}
</style>
</head>
<body>
<header>
  <div class="brand"><svg viewBox="0 0 17 18" aria-hidden="true"><path fill="#FF3D1F" d="M0 0h5.2l3.3 6.4L11.8 0H17L10.9 11.2V18H6.1v-6.8z"/></svg>V2pn <span>{for_providers}</span></div>
  <div class="spacer"></div>
  <a class="schema" href="partner.schema.json">partner.schema.json</a>
  <div class="switch"><a href="index.html" class="{ru_on}">RU</a><a href="en.html" class="{en_on}">EN</a></div>
</header>
<div class="wrap">
  <nav>{toc}</nav>
  <main>
{body}
<footer>{footer}</footer>
  </main>
</div>
<script>
// The section in view is marked in the contents.
const links = [...document.querySelectorAll('nav a')];
const byId = new Map(links.map(a => [a.getAttribute('href').slice(1), a]));
const io = new IntersectionObserver(entries => {{
  for (const e of entries) if (e.isIntersecting) {{
    links.forEach(a => a.classList.remove('active'));
    byId.get(e.target.id)?.classList.add('active');
  }}
}}, {{ rootMargin: '-80px 0px -70% 0px' }});
document.querySelectorAll('main h2[id], main h3[id]').forEach(h => io.observe(h));
</script>
</body>
</html>
"""


def build(out, shots):
    os.makedirs(out, exist_ok=True)
    os.makedirs(os.path.join(out, "fonts"), exist_ok=True)
    for f in ("Geist-Regular.ttf", "Geist-Medium.ttf", "Geist-SemiBold.ttf", "GeistMono-Regular.ttf"):
        shutil.copy(os.path.join(FONTS, f), os.path.join(out, "fonts", f))
    shutil.copy(os.path.join(DOCS, "partner.schema.json"), os.path.join(out, "partner.schema.json"))
    found = template_shots(shots)
    if found:
        os.makedirs(os.path.join(out, "templates"), exist_ok=True)
        for (key, mode_en), src in found.items():
            shutil.copy(src, os.path.join(out, "templates", f"{key}-{mode_en}.png"))

    for lang, source, target in (("ru", "README.ru.md", "index.html"), ("en", "README.md", "en.html")):
        with open(os.path.join(DOCS, source), encoding="utf-8") as fh:
            md = fh.read()
        # The language link line at the top is the switch in the header.
        md = re.sub(r"^\[(По-русски|In English)\]\([^)]+\)\s*$", "", md, flags=re.MULTILINE)
        body, toc = convert(md)
        # The first paragraph after the title is the lead.
        body = body.replace("<p>", '<p class="lead">', 1)
        # Pictures of the templates under their table (section 9, "Шаблоны:" / "Templates:").
        pics = gallery(lang, found)
        if pics:
            m = re.search(r"<p>(Шаблоны|Templates):</p>\s*<div class=\"table\">.*?</div>", body, flags=re.S)
            if m:
                body = body[: m.end()] + pics + body[m.end():]
        nav = "".join(f'<a class="l{level}" href="#{hid}">{html.escape(text)}</a>' for level, hid, text in toc if level <= 3)
        m = re.search(r"^#\s+(.*)$", md, flags=re.MULTILINE)
        title = m.group(1) if m else "V2pn"
        page = PAGE.format(
            lang=lang,
            title=html.escape(title),
            for_providers="для провайдеров" if lang == "ru" else "for providers",
            ru_on="on" if lang == "ru" else "",
            en_on="on" if lang == "en" else "",
            toc=nav,
            body=body,
            footer=("Источник: docs/partners в репозитории V2pn. Проверка настройки: <code>dotnet run tools/partner-check.cs</code>."
                    if lang == "ru" else
                    "Source: docs/partners in the V2pn repository. Check a setup: <code>dotnet run tools/partner-check.cs</code>."),
        )
        with open(os.path.join(out, target), "w", encoding="utf-8") as fh:
            fh.write(page)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(tempfile.gettempdir(), "v2pn-partner-docs"))
    ap.add_argument("--shots", default=os.path.join(tempfile.gettempdir(), "v2pn-matrix-shots"))
    ap.add_argument("--port", type=int, default=8765)
    ap.add_argument("--no-serve", action="store_true")
    args = ap.parse_args()
    shots = args.shots if os.path.isdir(args.shots) else None
    out = build(args.out, shots)
    print(f"built {out}" + ("" if template_shots(shots) else " (no pictures of the templates: run tools/edition-matrix.ps1 -Theme <folder> first)"))
    if args.no_serve:
        return
    os.chdir(out)

    class Quiet(http.server.SimpleHTTPRequestHandler):
        def log_message(self, format, *args):  # noqa: A002 (the base class's name)
            pass

    socketserver.TCPServer.allow_reuse_address = True
    with socketserver.TCPServer(("127.0.0.1", args.port), Quiet) as httpd:
        print(f"serving http://127.0.0.1:{args.port}/")
        sys.stdout.flush()
        httpd.serve_forever()


if __name__ == "__main__":
    main()
