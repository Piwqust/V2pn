"""A partner's download page: one static page from partners/<id>/ that sends the partner's people to V2pn with its tab.

    python tools/partner-page.py partners/configra             # into partners/out/site/configra/
    python tools/partner-page.py partners/configra --out D:/x  # somewhere else
    python tools/partner-page.py partners/configra --bot https://t.me/example_bot
    python tools/partner-page.py partners/configra --home ../../   # the V2pn lockup links to V2pn's site (tools/site.py)

The page carries the partner's logo and colours (its Scene background as the hero when the pack has one), one line on
what V2pn is, "Download for Windows" and "Download for macOS" pointing at the partner's package in the newest release
(V2pn-<id>-windows-x64.zip, V2pn-<id>-macos-arm64.zip and so on, made by tools/partner-packages.cs: V2pn's own signed
package with v2pn-partner.txt added, so V2pn opens with the partner's tab beside its own; the release is the first of
V2pnUpdateSources, as V2pn itself updates from), the one-command installers (install.ps1 / install.sh with
V2PN_PARTNER=<id>: the ordinary V2pn with the marker, or only the marker when V2pn is installed already), the three
steps to get the partner's subscription into it and, when the pack or --bot gives one, a link to the partner's bot. A
plain download of V2pn has no partner's tab, so the page never sends anyone to it. Russian first, English with ?lang=en
or the switch. A phone gets "V2pn is for computers" and a button that sends the page's link to the person instead of a
download.

Colours follow V2pn's own rules (ServiceLib/V2pn/V2pnThemePalette.cs, Editions.PartnerAccent): the brand turned away
from the state colours, the Scene's stage with its ink, scrim and buttons, the brand as text moved in lightness until it
reads. Every pair of text and ground is checked at 4.5:1 before the page is written; the build fails otherwise.

Standard library only; no external scripts or fonts: the page, its script (page.js, so a strict Content-Security-Policy
with script-src 'self' serves it), V2pn's font and the partner's pictures sit side by side.
"""
import argparse
import html
import json
import math
import os
import re
import shutil
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FONTS = os.path.join(REPO, "v2rayN", "v2rayN.Desktop", "Assets", "Fonts")
TARGETS = os.path.join(REPO, "v2rayN", "Directory.Build.targets")

# The partner's packages as tools/partner-packages.cs names them (ServiceLib/V2pn/V2pnPartnerBuilds.cs): V2pn's signed
# standard packages with v2pn-partner.txt. macOS has a zip only: a .dmg is not made on the computer that signs.
def packages(pid):
    return {
        "win": f"V2pn-{pid}-windows-x64.zip",
        "winArm": f"V2pn-{pid}-windows-arm64.zip",
        "mac": f"V2pn-{pid}-macos-arm64.zip",
        "macIntel": f"V2pn-{pid}-macos-x64.zip",
    }

# ---------------------------------------------------------------- colour: a port of V2pnColor and V2pnThemePalette


def parse(hex_):
    if not isinstance(hex_, str) or not re.fullmatch(r"#[0-9A-Fa-f]{6}", hex_):
        return None
    v = int(hex_[1:], 16)
    return ((v >> 16) & 255, (v >> 8) & 255, v & 255)


def hexs(c):
    return "#%02X%02X%02X" % c


def _byte(x):
    # C# (byte)Math.Round rounds half to even, as Python's round does.
    return int(round(max(0.0, min(1.0, x)) * 255))


def _linear(v):
    x = v / 255.0
    return x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4


def lum(c):
    return 0.2126 * _linear(c[0]) + 0.7152 * _linear(c[1]) + 0.0722 * _linear(c[2])


def contrast(a, b):
    la, lb = lum(a), lum(b)
    return (max(la, lb) + 0.05) / (min(la, lb) + 0.05)


def hsl(c):
    r, g, b = (c[0] / 255.0, c[1] / 255.0, c[2] / 255.0)
    mx, mn = max(r, g, b), min(r, g, b)
    l = (mx + mn) / 2
    if mx - mn < 1e-9:
        return (0.0, 0.0, l)
    d = mx - mn
    s = d / (2 - mx - mn) if l > 0.5 else d / (mx + mn)
    if mx == r:
        h = (g - b) / d + (6 if g < b else 0)
    elif mx == g:
        h = (b - r) / d + 2
    else:
        h = (r - g) / d + 4
    return (h * 60, s, l)


def from_hsl(h, s, l):
    h = ((h % 360) + 360) % 360 / 360
    s, l = max(0.0, min(1.0, s)), max(0.0, min(1.0, l))
    if s < 1e-9:
        g = int(round(l * 255))
        return (g, g, g)
    q = l * (1 + s) if l < 0.5 else l + s - l * s
    p = 2 * l - q

    def ch(t):
        t = t + 1 if t < 0 else t - 1 if t > 1 else t
        if t < 1 / 6:
            return p + (q - p) * 6 * t
        if t < 0.5:
            return q
        if t < 2 / 3:
            return p + (q - p) * (2 / 3 - t) * 6
        return p

    return (_byte(ch(h + 1 / 3)), _byte(ch(h)), _byte(ch(h - 1 / 3)))


def _oklab(c):
    r, g, b = (_linear(c[0]), _linear(c[1]), _linear(c[2]))
    l_ = (0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b) ** (1 / 3)
    m_ = (0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b) ** (1 / 3)
    s_ = (0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b) ** (1 / 3)
    return (0.2104542553 * l_ + 0.7936177850 * m_ - 0.0040720468 * s_,
            1.9779984951 * l_ - 2.4285922050 * m_ + 0.4505937099 * s_,
            0.0259040371 * l_ + 0.7827717662 * m_ - 0.8086757660 * s_)


def oklch(c):
    _, a, b = _oklab(c)
    hue = math.degrees(math.atan2(b, a))
    return (math.hypot(a, b), hue + 360 if hue < 0 else hue)


def ok_lightness(c):
    return _oklab(c)[0]


def from_oklch(lightness, chroma, hue):
    l = max(0.0, min(1.0, lightness))
    h = math.radians(hue)
    c = max(0.0, chroma)
    slack = 0.0005
    while True:
        a, b = c * math.cos(h), c * math.sin(h)
        l3 = (l + 0.3963377774 * a + 0.2158037573 * b) ** 3
        m3 = (l - 0.1055613458 * a - 0.0638541728 * b) ** 3
        s3 = (l - 0.0894841775 * a - 1.2914855480 * b) ** 3
        red = 4.0767416621 * l3 - 3.3077115913 * m3 + 0.2309699292 * s3
        green = -1.2684380046 * l3 + 2.6097574011 * m3 - 0.3413193965 * s3
        blue = -0.0041960863 * l3 - 0.7034186147 * m3 + 1.7076147010 * s3
        if c < 0.0005 or all(-slack <= v <= 1 + slack for v in (red, green, blue)):
            def enc(x):
                x = max(0.0, min(1.0, x))
                v = x * 12.92 if x <= 0.0031308 else 1.055 * x ** (1 / 2.4) - 0.055
                return _byte(v)
            return (enc(red), enc(green), enc(blue))
        c *= 0.96


def mix(a, b, t):
    t = max(0.0, min(1.0, t))
    return tuple(int(round(x + (y - x) * t)) for x, y in zip(a, b))


def over(colour, below, alpha):
    return mix(below, colour, alpha)


def with_alpha(c, alpha):
    """CSS rgba() of the "#AARRGGBB" V2pn writes: the alpha byte as V2pn rounds it."""
    a = int(round(max(0.0, min(1.0, alpha)) * 255))
    return "rgba(%d,%d,%d,%.4f)" % (c[0], c[1], c[2], a / 255), a / 255


WHITE, BLACK = (255, 255, 255), (0, 0, 0)
STAGE_DARK_INK = parse("#111114")
ON_ACCENT_INK = parse("#0B0B0D")
STATE_HUES = [(parse("#3DD68C"), 20, 25), (parse("#F2B84B"), 20, 25), (parse("#FF5A3D"), 8, 12)]
FALLBACK_BRAND = "#3B6CF6"
TEXT = 4.5


def _hue_within(c, state, degrees):
    chroma, hue = oklch(c)
    if chroma < 0.04:
        return False
    diff = abs(hue - oklch(state)[1]) % 360
    return min(diff, 360 - diff) <= degrees


def away_from_state(c):
    if not any(_hue_within(c, s, near) for s, near, _ in STATE_HUES):
        return c
    h, s, l = hsl(c)
    for step in range(3, 121, 3):
        for sign in (-1, 1):
            turned = from_hsl(h + sign * step, s, l)
            if not any(_hue_within(turned, x, clear) for x, _, clear in STATE_HUES):
                return turned
    return c


def readable_on(c, ground, minimum=TEXT):
    """The brand as text on a ground: only its OKLCH lightness moves (V2pnThemePalette.ReadableOn)."""
    if contrast(c, ground) >= minimum:
        return c
    darker = contrast(BLACK, ground) >= contrast(WHITE, ground)
    chroma, hue = oklch(c)
    start = ok_lightness(c)
    for step in range(1, 101):
        cand = from_oklch(start + (-1 if darker else 1) * step * 0.01, chroma, hue)
        if contrast(cand, ground) >= minimum:
            return cand
    return BLACK if darker else WHITE


def with_label(fill, light, dark_ink, ground):
    """A fill and the label that reads on it; a mid-tone fill moves away from the ground until it does (WithLabel)."""
    better = light if contrast(light, fill) >= contrast(dark_ink, fill) else dark_ink
    if contrast(better, fill) >= TEXT:
        return fill, better
    darken = lum(ground) > 0.18
    label = light if darken else dark_ink
    h, s, l = hsl(fill)
    for step in range(1, 101):
        moved = from_hsl(h, s, l + (-1 if darken else 1) * step * 0.01)
        if contrast(label, moved) >= TEXT:
            return moved, label
    return (BLACK if darken else WHITE), label


def min_contrast(text, stops):
    m = float("inf")
    for i, s in enumerate(stops):
        m = min(m, contrast(text, s))
        if i + 1 < len(stops):
            for t in (0.25, 0.5, 0.75):
                m = min(m, contrast(text, mix(s, stops[i + 1], t)))
    return m


def _softer(ink, stops):
    middle = stops[len(stops) // 2]
    t = 0.4
    while t > 0:
        cand = mix(ink, middle, t)
        if min_contrast(cand, stops) >= TEXT + 0.2:
            return cand
        t -= 0.02
    return ink


def scrim_alpha(scrim, texts):
    for step in range(77, 255):
        alpha = step / 255.0
        if all(contrast(t, over(scrim, BLACK, alpha)) >= TEXT and contrast(t, over(scrim, WHITE, alpha)) >= TEXT for t in texts):
            return alpha
    return 1.0


def stage(theme, dark):
    """V2pnThemePalette.Stage: the Scene's background in a mode, and what reads on it; None without a background."""
    bg = theme["background"]
    if bg["type"] == "image" and bg.get("art"):
        ink = WHITE if dark else STAGE_DARK_INK
        scrim = BLACK if dark else WHITE
        alpha = scrim_alpha(scrim, [ink])
        return _stage_of("image", [scrim], bg["art"], (scrim, alpha), ink, None)
    if bg["type"] == "gradient":
        source = bg["colors"] if len(bg["colors"]) >= 2 else theme["brand"][:2]
        stops = [away_from_state(c) for c in (parse(x) for x in source) if c]
        if not stops:
            return None
        if len(stops) == 1:
            stops.append(stops[0])
        if dark:
            out = []
            for i, c in enumerate(stops):
                chroma, hue = oklch(c)
                out.append(from_oklch(min(ok_lightness(c) * 0.78, 0.78 if i == 0 else 0.72), chroma if i == 0 else chroma * 0.95, hue))
            stops = out
        white, black = min_contrast(WHITE, stops), min_contrast(STAGE_DARK_INK, stops)
        ink = WHITE if white >= black else STAGE_DARK_INK
        scrim, seen = None, stops
        if max(white, black) < TEXT:
            veil = BLACK if ink == WHITE else WHITE
            for step in range(1, 256):
                alpha = step / 255.0
                under = [over(veil, s, alpha) for s in stops]
                if min_contrast(ink, under) >= TEXT:
                    scrim, seen = (veil, alpha), under
                    break
        return _stage_of("gradient", stops, None, scrim, ink, seen)
    return None


def _stage_of(kind, stops, art, scrim, ink, seen):
    light = ink == WHITE
    chip = 0.16 if light else 0.08
    opposite = BLACK if light else WHITE
    if seen is not None and min_contrast(ink, [over(ink, s, chip) for s in seen]) >= TEXT:
        ghost = (ink, chip)
    else:
        ghost = (opposite, 0.22 if light else 0.3)
    soft = ink if seen is None else _softer(ink, seen)
    return {"kind": kind, "stops": stops, "art": art, "scrim": scrim, "ink": ink, "soft": soft, "ghost": ghost,
            "solid": ink, "on_solid": STAGE_DARK_INK if light else WHITE, "seen": seen}


def on_accent(accent):
    """Appearance.OnAccent: near-black or white, whichever reads better on the accent."""
    return ON_ACCENT_INK if contrast(ON_ACCENT_INK, accent) >= contrast(WHITE, accent) else WHITE


# ---------------------------------------------------------------- the pack


def image_size(b):
    """Width and height from a PNG, JPEG or WebP header (V2pnPartnerSlots.ImageSize); None for anything else."""
    try:
        if len(b) >= 24 and b[:4] == b"\x89PNG":
            return int.from_bytes(b[16:20], "big"), int.from_bytes(b[20:24], "big")
        if len(b) >= 4 and b[0] == 0xFF and b[1] == 0xD8:
            i = 2
            while i + 9 < len(b):
                if b[i] != 0xFF:
                    i += 1
                    continue
                marker, length = b[i + 1], (b[i + 2] << 8) | b[i + 3]
                if 0xC0 <= marker <= 0xCF and marker not in (0xC4, 0xC8, 0xCC):
                    return (b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]
                i += 2 + length
            return None
        if len(b) >= 30 and b[:4] == b"RIFF" and b[8:12] == b"WEBP":
            chunk = b[12:16]
            if chunk == b"VP8 ":
                return ((b[27] << 8) | b[26]) & 0x3FFF, ((b[29] << 8) | b[28]) & 0x3FFF
            if chunk == b"VP8L":
                return 1 + (((b[22] & 0x3F) << 8) | b[21]), 1 + (((b[24] & 0x0F) << 10) | (b[23] << 2) | ((b[22] & 0xC0) >> 6))
            if chunk == b"VP8X":
                return 1 + (b[24] | (b[25] << 8) | (b[26] << 16)), 1 + (b[27] | (b[28] << 8) | (b[29] << 16))
    except IndexError:
        pass
    return None


SLOTS = {
    "logo": ("a wide logo, 2:1 to 8:1, 32-256 px tall", lambda w, h: 32 <= h <= 256 and h * 2 <= w <= h * 8),
    "mark": ("a square mark, 64-512 px", lambda w, h: 64 <= w <= 512 and abs(w - h) <= w // 20),
    "backdrop": ("a background picture, 640-3200 px a side, at most 3:1",
                 lambda w, h: 640 <= w <= 3200 and 640 <= h <= 3200 and w <= h * 3 and h <= w * 3),
}
MAX_ASSET = 1024 * 1024


def fit(folder, name, slot, role, notes):
    """The picture when it fits its slot as V2pn checks it; None (and why) when V2pn would not show it."""
    if not name:
        return None
    path = os.path.join(folder, name)
    if not re.fullmatch(r"[A-Za-z0-9_-]+\.[A-Za-z0-9]+", name) or not os.path.isfile(path):
        notes.append(f"{role} {name}: no such file in the pack")
        return None
    with open(path, "rb") as fh:
        data = fh.read()
    size = image_size(data)
    if len(data) > MAX_ASSET or not size:
        notes.append(f"{role} {name}: not a PNG, JPEG or WebP of at most 1 MB V2pn can read the size of")
        return None
    desc, fits = SLOTS[slot]
    if not fits(*size):
        notes.append(f"{role} {name} is {size[0]}×{size[1]}; the slot takes {desc}")
        return None
    return name


def theme_of(pack):
    """V2pnPartnerTheme.Parse, the parts the page uses: the brand colours and the background."""
    theme = pack.get("theme") if isinstance(pack.get("theme"), dict) else {}
    palette = theme.get("palette") if isinstance(theme.get("palette"), dict) else {}
    raw = palette.get("brand")
    items = raw if isinstance(raw, list) else [raw] if raw is not None else []
    brand = []
    for item in items:
        c = parse(item)
        if c and hexs(c) not in brand and len(brand) < 3:
            brand.append(hexs(c))
    accent = parse((pack.get("brand") or {}).get("accent"))
    if not brand and accent:
        brand.append(hexs(accent))
    bg = theme.get("background") if isinstance(theme.get("background"), dict) else {}
    kind = str(bg.get("type", "solid")).strip().lower()
    background = {"type": "solid", "colors": [], "art": None}
    if kind == "gradient":
        colors = bg.get("colors") if isinstance(bg.get("colors"), list) else []
        stops = [parse(c) for c in colors]
        if all(stops) and 2 <= len(stops) <= 4:
            background = {"type": "gradient", "colors": [hexs(c) for c in stops], "art": None}
    elif kind == "image" and isinstance(bg.get("art"), str):
        background = {"type": "image", "colors": [], "art": bg["art"]}
    return {"brand": brand, "background": background}


def release_source():
    """The first place V2pn updates from (V2pnUpdateSources in v2rayN/Directory.Build.targets)."""
    with open(TARGETS, encoding="utf-8") as fh:
        m = re.search(r"<V2pnUpdateSources[^>]*>([^<]+)</V2pnUpdateSources>", fh.read())
    return m.group(1).split("|")[0].strip().rstrip("/") if m else "https://github.com/Piwqust/V2pn/releases"


def bot_link(pack, override):
    if override:
        return override
    support = str((pack.get("links") or {}).get("support") or "")
    return support if re.match(r"^(https://t\.me/|tg:)", support) else None


# ---------------------------------------------------------------- the page

# Ground and text of the page: V2pn's own tokens (Assets/V2pnTheme.axaml).
LIGHT = {"bg": "#ECEEF3", "card": "#FFFFFF", "muted_bg": "#F0F1F5", "line": "#DCDEE5", "ink": "#16161B", "soft": "#34343F", "muted": "#5E5E6B"}
DARK = {"bg": "#0E0E11", "card": "#1A1A21", "muted_bg": "#26262F", "line": "#2A2A33", "ink": "#ECECF1", "soft": "#C9C9D3", "muted": "#9B9BA8"}

V2MARK = '<svg class="v2" viewBox="0 0 17 18" aria-hidden="true"><path fill="#FF3D1F" d="M0 0h5.2l3.3 6.4L11.8 0H17L10.9 11.2V18H6.1v-6.8z"/></svg>'


def strings(name, login, has_bot, cabinet):
    n = html.escape(name)
    href = html.escape(cabinet)
    cab_ru = f'в <a href="{href}">кабинете</a> ' if cabinet else "у "
    cab_en = f'in the <a href="{href}">{n} cabinet</a>' if cabinet else f"from {n}"
    ru = {
        "title": f"V2pn для {n}",
        "h1": f"{n} на компьютере",
        "lead": f"V2pn — простой VPN-клиент для Windows и macOS. Отсюда он ставится с вкладкой {n}: в ней ваш тариф, серверы и цвета {n}. "
                + (f"Подписка {n} входит в него по коду из бота, без ссылок." if login else f"Подписка {n} подключается в нём в один клик."),
        "win": "Скачать для Windows",
        "mac": "Скачать для macOS",
        "winNote": 'Windows 10 и 11 · <a href="{winArm}" data-os-link="win-alt">для ARM</a>',
        "winNoteArm": 'Windows на ARM · <a href="{win}" data-os-link="win-alt">для x64</a>',
        "macNote": 'macOS 13.6 и новее, Apple silicon · <a href="{macIntel}">для Intel</a>',
        "phoneTitle": "V2pn — для компьютера",
        "phoneText": "На телефоне V2pn не работает. Откройте эту страницу на компьютере с Windows или macOS.",
        "share": "Отправить ссылку себе",
        "copied": "Ссылка скопирована",
        "phoneFiles": 'Файлы для компьютера: <a href="{win}">Windows</a> · <a href="{mac}">macOS</a>',
        "stepsTitle": "Три шага",
        "s1": "Установите V2pn",
        "s1win": "Распакуйте архив в свою папку и запустите V2pn.exe. Если Windows скажет, что защитила компьютер, нажмите «Подробнее» → «Выполнить в любом случае».",
        "s1mac": "Распакуйте архив и перетащите V2pn в «Программы». В первый раз разрешите запуск: «Системные настройки» → «Конфиденциальность и безопасность» → «Всё равно открыть».",
        "cmdWin": f"Или одной командой в PowerShell. Если V2pn уже установлен, команда только добавит вкладку {n}, серверы и настройки останутся:",
        "cmdMac": f"Или одной командой в Терминале. Если V2pn уже установлен, команда только добавит вкладку {n}, серверы и настройки останутся:",
        "s2": f"Войдите в {n}" if login else "Вставьте ссылку на подписку",
        "s2t": (f"При первом запуске нажмите «У меня подписка от провайдера». Если V2pn уже настроен: «+» → «Войти через провайдера»."
                if login else
                f"Скопируйте ссылку на подписку {cab_ru}{n} и нажмите в V2pn «Вставить из буфера»."),
        "s3": "Подтвердите в боте" if login else "Подключитесь",
        "s3t": (f"V2pn покажет код. Подтвердите его в боте {n}, и V2pn сам добавит подписку: останется нажать «Подключиться»."
                if login else
                f"Нажмите «Подключиться». Серверы {n} — во вкладке {n} вверху окна."),
        "bot": "Открыть бота",
        "foot": 'V2pn — открытый код, GPL-3.0. <a href="https://github.com/Piwqust/V2pn">V2pn на GitHub</a>',
        "lang": "Язык",
    }
    en = {
        "title": f"V2pn for {n}",
        "h1": f"{n} on your computer",
        "lead": f"V2pn is a simple VPN client for Windows and macOS. From here it comes with a {n} tab: your plan, servers and {n}'s colours. "
                + (f"Your {n} subscription gets into it by a code from the bot, no links to copy." if login else f"Your {n} subscription connects in one click."),
        "win": "Download for Windows",
        "mac": "Download for macOS",
        "winNote": 'Windows 10 and 11 · <a href="{winArm}" data-os-link="win-alt">for ARM</a>',
        "winNoteArm": 'Windows on ARM · <a href="{win}" data-os-link="win-alt">for x64</a>',
        "macNote": 'macOS 13.6 or later, Apple silicon · <a href="{macIntel}">for Intel</a>',
        "phoneTitle": "V2pn is for computers",
        "phoneText": "V2pn does not run on phones. Open this page on a Windows or macOS computer.",
        "share": "Send the link to yourself",
        "copied": "Link copied",
        "phoneFiles": 'Files for a computer: <a href="{win}">Windows</a> · <a href="{mac}">macOS</a>',
        "stepsTitle": "Three steps",
        "s1": "Install V2pn",
        "s1win": "Unpack the archive into a folder of yours and run V2pn.exe. If Windows says it protected your PC, press “More info”, then “Run anyway”.",
        "s1mac": "Unpack the archive and drag V2pn to Applications. The first time, allow it: System Settings → Privacy & Security → “Open Anyway”.",
        "cmdWin": f"Or in one command in PowerShell. If V2pn is installed already, it only adds the {n} tab; your servers and settings stay:",
        "cmdMac": f"Or in one command in Terminal. If V2pn is installed already, it only adds the {n} tab; your servers and settings stay:",
        "s2": f"Sign in to {n}" if login else "Paste your subscription link",
        "s2t": (f"At first start press “I have a subscription from a provider”. If V2pn is set up already: “+” → “Sign in with a provider”."
                if login else
                f"Copy your subscription link {cab_en} and press “Paste from clipboard” in V2pn."),
        "s3": "Confirm in the bot" if login else "Connect",
        "s3t": (f"V2pn shows a code. Confirm it in {n}'s bot and V2pn adds the subscription itself: then press “Connect”."
                if login else
                f"Press “Connect”. {n}'s servers are in the {n} tab at the top of the window."),
        "bot": "Open the bot",
        "foot": 'V2pn is open source, GPL-3.0. <a href="https://github.com/Piwqust/V2pn">V2pn on GitHub</a>',
        "lang": "Language",
    }
    return ru, en


def build(folder, out, source, bot_override=None, home=None):
    folder = os.path.abspath(folder)
    with open(os.path.join(folder, "partner.json"), encoding="utf-8") as fh:
        pack = json.load(fh)
    listing = {}
    if os.path.isfile(os.path.join(folder, "listing.json")):
        with open(os.path.join(folder, "listing.json"), encoding="utf-8") as fh:
            listing = json.load(fh)
    pid = pack.get("id") or listing.get("id") or os.path.basename(folder)
    name = str(listing.get("name") or pack.get("name") or pid)
    notes = []
    brand_info = pack.get("brand") or {}
    logo = fit(folder, brand_info.get("logo"), "logo", "brand.logo", notes)
    logo_light = fit(folder, brand_info.get("logoOnLight"), "logo", "brand.logoOnLight", notes)
    # The page shows no mark; checked all the same, so the notes say what V2pn would leave out.
    fit(folder, brand_info.get("mark"), "mark", "brand.mark", notes)
    theme = theme_of(pack)
    if theme["background"]["type"] == "image" and not fit(folder, theme["background"]["art"], "backdrop", "theme.background.art", notes):
        theme["background"] = {"type": "solid", "colors": [], "art": None}
    login = pack.get("login") is True and bool(pack.get("api"))
    bot = bot_link(pack, bot_override)
    cabinet = str((pack.get("links") or {}).get("cabinet") or "")
    cabinet = cabinet if cabinet.startswith("https://") else ""

    # The partner's colour as V2pn's window wears it (Editions.PartnerAccent).
    accent = away_from_state(parse(theme["brand"][0]) if theme["brand"] else parse(FALLBACK_BRAND))
    moved = [f"{h} is close to one of V2pn's state colours; V2pn uses {hexs(away_from_state(parse(h)))} instead"
             for h in theme["brand"] + theme["background"]["colors"] if away_from_state(parse(h)) != parse(h)]

    checks = []  # (what, text, ground, ratio)

    def check(what, text, ground):
        checks.append((what, text, ground, contrast(text, ground)))

    css_modes = {}
    for mode, tokens in (("light", LIGHT, ), ("dark", DARK)):
        dark = mode == "dark"
        t = {k: parse(v) for k, v in tokens.items()}
        for k in ("ink", "soft", "muted"):
            for g in ("bg", "card", "muted_bg"):
                check(f"{mode}: {k} on {g}", t[k], t[g])
        # The app's label (Appearance.OnAccent) where it reads; a mid-tone brand moves until its label does.
        fill, label = (accent, on_accent(accent)) if contrast(on_accent(accent), accent) >= TEXT else with_label(accent, WHITE, ON_ACCENT_INK, t["card"])
        check(f"{mode}: label on the brand button", label, fill)
        link = readable_on(accent, t["card"])
        link_bg = readable_on(accent, t["bg"])
        check(f"{mode}: brand link on card", link, t["card"])
        check(f"{mode}: brand link on ground", link_bg, t["bg"])
        v = {
            "bg": tokens["bg"], "card": tokens["card"], "mutedBg": tokens["muted_bg"], "line": tokens["line"],
            "ink": tokens["ink"], "soft": tokens["soft"], "muted": tokens["muted"],
            "accent": hexs(fill), "onAccent": hexs(label), "link": hexs(link), "linkBg": hexs(link_bg),
            "focus": hexs(link),
        }
        st = stage(theme, dark)
        if st:
            ground = st["seen"] if st["seen"] is not None else None
            if st["kind"] == "gradient":
                stops = ", ".join(hexs(c) for c in st["stops"])
                v["hero"] = f"linear-gradient(135deg, {stops})"
                v["scrim"] = with_alpha(*st["scrim"])[0] if st["scrim"] else "transparent"
                for label_, colour in (("ink", st["ink"]), ("soft", st["soft"])):
                    checks.append((f"{mode}: stage {label_} at its worst point", colour, None, min_contrast(colour, ground)))
                ghost_seen = [over(st["ghost"][0], s, st["ghost"][1]) for s in ground]
                checks.append((f"{mode}: stage ink on a quiet button", st["ink"], None, min_contrast(st["ink"], ghost_seen)))
            else:
                v["hero"] = f"url({theme['background']['art']}) center / cover no-repeat, {hexs(st['stops'][0])}"
                css, alpha = with_alpha(*st["scrim"])
                v["scrim"] = css
                # Whatever the picture holds: its darkest and lightest pixels under the scrim.
                worst = [over(st["scrim"][0], BLACK, alpha), over(st["scrim"][0], WHITE, alpha)]
                checks.append((f"{mode}: stage ink over any pixel", st["ink"], None, min_contrast(st["ink"], worst)))
                ghost_seen = [over(st["ghost"][0], w, st["ghost"][1]) for w in worst]
                checks.append((f"{mode}: stage ink on a quiet button", st["ink"], None, min(contrast(st["ink"], g) for g in ghost_seen)))
            check(f"{mode}: label on the stage's strong button", st["on_solid"], st["solid"])
            v.update({"heroInk": hexs(st["ink"]), "heroSoft": hexs(st["soft"]), "ghost": with_alpha(*st["ghost"])[0],
                      "solid": hexs(st["solid"]), "onSolid": hexs(st["on_solid"])})
            v["heroLight"] = st["ink"] == WHITE
        else:
            v.update({"hero": tokens["card"], "scrim": "transparent", "heroInk": tokens["ink"], "heroSoft": tokens["muted"],
                      "ghost": tokens["muted_bg"], "solid": hexs(fill), "onSolid": hexs(label)})
            v["heroLight"] = dark
        css_modes[mode] = v

    bad = [c for c in checks if c[3] < TEXT]
    if bad:
        for what, _, _, ratio in bad:
            print(f"  FAIL {what}: {ratio:.2f}:1", file=sys.stderr)
        raise SystemExit("some text would not read at 4.5:1; the page is not written")

    # Which logo reads on the hero in each mode: the one for a dark ground on a light ink.
    def logo_for(light_ink):
        return (logo or logo_light) if light_ink else (logo_light or logo)

    hero_logo = {m: logo_for(css_modes[m]["heroLight"]) for m in ("light", "dark")}
    bar_logo = {"light": logo_light or logo, "dark": logo or logo_light}

    urls = {key: f"{source}/latest/download/{file}" for key, file in packages(pid).items()}
    # The installers take the same signed package as V2pn and add the partner's marker (tools/install.ps1, install.sh).
    commands = {
        "win": f"$env:V2PN_PARTNER='{pid}'; irm {source}/latest/download/install.ps1 | iex",
        "mac": f"curl -fsSL {source}/latest/download/install.sh | V2PN_PARTNER={pid} bash",
    }
    ru, en = strings(name, login, bool(bot), cabinet)
    for d in (ru, en):
        for k in d:
            for key, url in urls.items():
                d[k] = d[k].replace("{" + key + "}", url)

    def pic(cls, files, alt):
        if not files["light"] and not files["dark"]:
            return f'<span class="{cls} name">{html.escape(name)}</span>'
        if files["light"] == files["dark"]:
            return f'<img class="{cls}" src="{files["light"]}" alt="{alt}">'
        return (f'<picture><source srcset="{files["dark"]}" media="(prefers-color-scheme: dark)">'
                f'<img class="{cls}" src="{files["light"]}" alt="{alt}"></picture>')

    def tokens_css(v):
        keys = ["bg", "card", "mutedBg", "line", "ink", "soft", "muted", "accent", "onAccent", "link", "linkBg", "focus",
                "hero", "scrim", "heroInk", "heroSoft", "ghost", "solid", "onSolid"]
        return "; ".join(f"--{k}: {v[k]}" for k in keys)

    t = lambda k: ru[k]  # noqa: E731 (the page is written in Russian; the script swaps to English)
    bot_html = (f'<a class="btn quiet small" href="{html.escape(bot)}" data-t="bot">{t("bot")}</a>' if bot else "")
    page = PAGE.format(
        lang="ru",
        title=t("title"),
        light=tokens_css(css_modes["light"]),
        dark=tokens_css(css_modes["dark"]),
        v2mark=V2MARK,
        lockup_open=(f'<a class="lockup" href="{html.escape(home)}">' if home else '<div class="lockup">'),
        lockup_close=("</a>" if home else "</div>"),
        bar_logo=pic("barlogo", bar_logo, html.escape(name)),
        hero_logo=pic("herologo", hero_logo, html.escape(name)),
        name=html.escape(name),
        h1=t("h1"), lead=t("lead"), win=t("win"), mac=t("mac"),
        win_url=urls["win"], mac_url=urls["mac"], win_note=t("winNote"), mac_note=t("macNote"),
        phone_title=t("phoneTitle"), phone_text=t("phoneText"), share=t("share"), phone_files=t("phoneFiles"),
        steps_title=t("stepsTitle"), s1=t("s1"), s1win=t("s1win"), s1mac=t("s1mac"), s2=t("s2"), s2t=t("s2t"),
        cmd_win=t("cmdWin"), cmd_mac=t("cmdMac"), win_cmd=html.escape(commands["win"]), mac_cmd=html.escape(commands["mac"]),
        s3=t("s3"), s3t=t("s3t"), bot=bot_html, foot=t("foot"),
        data=json.dumps({"text": {"ru": ru, "en": en}, "urls": urls}, ensure_ascii=False).replace("</", "<\\/"),
    )

    out = os.path.abspath(out)
    os.makedirs(os.path.join(out, "fonts"), exist_ok=True)
    for f in os.listdir(out):
        if os.path.isfile(os.path.join(out, f)):
            os.remove(os.path.join(out, f))
    for f in ("Geist-Regular.ttf", "Geist-SemiBold.ttf"):
        shutil.copy(os.path.join(FONTS, f), os.path.join(out, "fonts", f))
    used = {x for x in (logo, logo_light, theme["background"]["art"]) if x}
    for f in sorted(used):
        shutil.copy(os.path.join(folder, f), os.path.join(out, f))
    with open(os.path.join(out, "index.html"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write(page)
    with open(os.path.join(out, "page.js"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write(PAGE_JS)

    print(f"{name} ({pid}): {out}")
    print(f"  downloads from {source}/latest/download/: {', '.join(packages(pid).values())}")
    print(f"  one command: {commands['win']}")
    print(f"               {commands['mac']}")
    look = f"Scene ({theme['background']['type']})" if stage(theme, False) else "Accent"
    print(f"  look: {look}; "
          f"sign-in by code: {'yes' if login else 'no'}; bot link: {bot or 'none'}")
    for n in moved + notes:
        print(f"  --   {n}")
    worst = min(checks, key=lambda c: c[3])
    print(f"  contrast: {len(checks)} pairs checked, the lowest {worst[3]:.2f}:1 ({worst[0]})")
    return out


PAGE = """<!doctype html>
<html lang="{lang}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{title}</title>
<meta name="description" content="V2pn — простой VPN-клиент для Windows и macOS.">
<style>
@font-face {{ font-family: Geist; src: url(fonts/Geist-Regular.ttf); font-weight: 400; font-display: swap; }}
@font-face {{ font-family: Geist; src: url(fonts/Geist-SemiBold.ttf); font-weight: 600; font-display: swap; }}
:root {{ color-scheme: light dark; {light}; }}
@media (prefers-color-scheme: dark) {{ :root {{ {dark}; }} }}
* {{ box-sizing: border-box; }}
html {{ -webkit-text-size-adjust: 100%; }}
body {{ margin: 0; background: var(--bg); color: var(--ink); font: 16px/1.55 Geist, "Segoe UI", system-ui, -apple-system, sans-serif;
  -webkit-font-smoothing: antialiased; }}
a {{ color: var(--linkBg); text-underline-offset: 3px; }}
:focus-visible {{ outline: 2px solid var(--focus); outline-offset: 2px; border-radius: 6px; }}
.wrap {{ max-width: 1040px; margin: 0 auto; padding: 0 24px; }}
.bar {{ display: flex; align-items: center; gap: 10px; height: 64px; }}
.lockup {{ display: flex; align-items: center; gap: 10px; min-width: 0; font-weight: 600; color: inherit; text-decoration: none; }}
.v2 {{ width: 17px; height: 18px; flex: none; }}
.x {{ color: var(--muted); font-weight: 400; }}
.barlogo {{ height: 20px; width: auto; display: block; max-width: 160px; object-fit: contain; object-position: left center; }}
.name {{ font-weight: 600; }}
.sp {{ flex: 1; }}
.switch {{ display: inline-flex; padding: 3px; border-radius: 10px; background: var(--mutedBg); flex: none; }}
.switch button {{ font: inherit; font-size: 13px; font-weight: 600; border: 0; background: none; color: var(--muted); height: 30px; min-width: 40px;
  padding: 0 10px; border-radius: 8px; cursor: pointer; }}
.switch button[aria-pressed=true] {{ background: var(--card); color: var(--ink); box-shadow: 0 1px 2px rgba(0,0,0,.12); }}
.hero {{ position: relative; overflow: hidden; border-radius: 24px; background: var(--hero); color: var(--heroInk); margin-top: 8px;
  box-shadow: 0 1px 2px rgba(0,0,0,.06), 0 18px 40px -22px rgba(0,0,0,.35); }}
.hero::before {{ content: ""; position: absolute; inset: 0; background: var(--scrim); pointer-events: none; }}
.hero-in {{ position: relative; padding: 56px 56px 48px; display: grid; gap: 22px; max-width: 720px; }}
.herologo {{ height: 44px; width: auto; max-width: 320px; display: block; object-fit: contain; object-position: left center; }}
.hero .name {{ font-size: 28px; }}
h1 {{ margin: 0; font-size: 46px; line-height: 1.08; letter-spacing: -0.03em; font-weight: 600; text-wrap: balance; }}
.lead {{ margin: 0; font-size: 18.5px; color: var(--heroSoft); max-width: 36em; text-wrap: pretty; }}
.dl {{ display: flex; flex-wrap: wrap; gap: 14px 18px; margin-top: 6px; }}
.dl .opt {{ display: grid; gap: 8px; justify-items: start; }}
.btn {{ display: inline-flex; align-items: center; justify-content: center; gap: 10px; height: 52px; padding: 0 24px; border-radius: 999px;
  font: inherit; font-weight: 600; font-size: 16px; text-decoration: none; border: 0; cursor: pointer; white-space: nowrap;
  transition: transform 140ms cubic-bezier(.23,1,.32,1), filter 150ms ease; }}
.btn:active {{ transform: scale(.97); }}
.btn:hover {{ filter: brightness(1.06); }}
.btn svg {{ width: 18px; height: 18px; flex: none; }}
.hero .btn {{ background: var(--ghost); color: var(--heroInk); }}
.hero .btn.primary {{ background: var(--solid); color: var(--onSolid); box-shadow: 0 1px 2px rgba(0,0,0,.18), 0 10px 22px -12px rgba(0,0,0,.5); }}
.btn.small {{ height: 40px; padding: 0 16px; font-size: 14.5px; }}
.note {{ font-size: 13.5px; color: var(--heroSoft); padding-left: 4px; }}
.hero a:not(.btn) {{ color: var(--heroInk); }}
.phone {{ display: none; gap: 12px; justify-items: start; }}
.phone h2 {{ margin: 0; font-size: 20px; letter-spacing: -0.01em; }}
.phone p {{ margin: 0; color: var(--heroSoft); }}
body.is-phone .phone {{ display: grid; }}
body.is-phone .dl {{ display: none; }}
.steps {{ margin: 44px 0 0; }}
.steps h2 {{ font-size: 24px; letter-spacing: -0.02em; margin: 0 0 16px; }}
ol {{ list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 14px; }}
li {{ background: var(--card); border-radius: 18px; padding: 22px; display: grid; gap: 10px; align-content: start;
  box-shadow: 0 0 0 1px var(--line); }}
.num {{ width: 32px; height: 32px; border-radius: 50%; display: grid; place-items: center; background: var(--accent); color: var(--onAccent);
  font-weight: 600; font-size: 15px; font-variant-numeric: tabular-nums; }}
li h3 {{ margin: 0; font-size: 17px; font-weight: 600; }}
li p {{ margin: 0; color: var(--soft); font-size: 15px; }}
li p b {{ color: var(--ink); font-weight: 600; }}
li a {{ color: var(--link); }}
li .btn.quiet {{ background: var(--accent); color: var(--onAccent); justify-self: start; margin-top: 4px; }}
li .cmd {{ font-size: 13.5px; color: var(--muted); }}
li code {{ display: block; margin-top: 4px; padding: 8px 10px; border-radius: 10px; background: var(--mutedBg); color: var(--ink);
  font: 12.5px/1.45 ui-monospace, "Cascadia Mono", Consolas, Menlo, monospace; overflow-wrap: anywhere; user-select: all; }}
body.os-win [data-os=mac], body.os-mac [data-os=win] {{ display: none; }}
.osl {{ display: none; }}
body.os-any .osl, body.os-phone .osl {{ display: inline; }}
footer {{ margin: 48px 0 40px; color: var(--muted); font-size: 13.5px; }}
footer a {{ color: var(--muted); }}
@media (max-width: 860px) {{
  ol {{ grid-template-columns: 1fr; }}
  .hero-in {{ padding: 40px 32px 36px; }}
  h1 {{ font-size: 36px; }}
}}
@media (max-width: 560px) {{
  .wrap {{ padding: 0 16px; }}
  .bar {{ height: 56px; }}
  .hero {{ border-radius: 20px; }}
  .hero-in {{ padding: 28px 20px 26px; gap: 18px; }}
  .herologo {{ height: 34px; max-width: 100%; }}
  h1 {{ font-size: 30px; }}
  .lead {{ font-size: 16.5px; }}
  .dl, .dl .opt {{ width: 100%; }}
  .btn {{ width: 100%; }}
  .btn.small {{ width: auto; }}
  li {{ padding: 18px; }}
}}
@media (prefers-reduced-motion: reduce) {{ .btn {{ transition: none; }} .btn:active {{ transform: none; }} }}
</style>
</head>
<body class="os-any">
<div class="wrap">
  <header class="bar">
    {lockup_open}{v2mark}<span>V2pn</span><span class="x">×</span>{bar_logo}{lockup_close}
    <div class="sp"></div>
    <div class="switch" role="group" aria-label="Язык" data-t-label="lang">
      <button type="button" data-lang="ru" aria-pressed="true">RU</button><button type="button" data-lang="en" aria-pressed="false">EN</button>
    </div>
  </header>
  <main>
    <section class="hero">
      <div class="hero-in">
        {hero_logo}
        <h1 data-t="h1">{h1}</h1>
        <p class="lead" data-t="lead">{lead}</p>
        <div class="dl">
          <div class="opt" data-dl="win">
            <a class="btn primary" id="win" href="{win_url}"><svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M3 5.5 10.4 4.5v7H3zm8.4-1.1L21 3v8.5h-9.6zM3 12.5h7.4v7L3 18.5zm8.4 0H21V21l-9.6-1.4z"/></svg><span data-t="win">{win}</span></a>
            <span class="note" id="win-note" data-t="winNote">{win_note}</span>
          </div>
          <div class="opt" data-dl="mac">
            <a class="btn" id="mac" href="{mac_url}"><svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M16.4 12.6c0-2.4 2-3.6 2.1-3.7a4.6 4.6 0 0 0-3.6-1.9c-1.5-.2-3 .9-3.7.9-.8 0-2-.9-3.2-.9a4.8 4.8 0 0 0-4 2.5c-1.7 3-.4 7.4 1.2 9.8.8 1.2 1.8 2.5 3 2.4 1.2 0 1.7-.8 3.1-.8s1.9.8 3.1.8c1.3 0 2.1-1.2 2.9-2.4a10 10 0 0 0 1.3-2.7 4.2 4.2 0 0 1-2.2-4zM14 5.5A4.3 4.3 0 0 0 15 2.4a4.4 4.4 0 0 0-2.9 1.5 4.1 4.1 0 0 0-1 3 3.6 3.6 0 0 0 2.9-1.4z"/></svg><span data-t="mac">{mac}</span></a>
            <span class="note" data-t="macNote">{mac_note}</span>
          </div>
        </div>
        <div class="phone">
          <h2 data-t="phoneTitle">{phone_title}</h2>
          <p data-t="phoneText">{phone_text}</p>
          <button type="button" class="btn primary" id="share" data-t="share">{share}</button>
          <span class="note" data-t="phoneFiles">{phone_files}</span>
        </div>
      </div>
    </section>
    <section class="steps">
      <h2 data-t="stepsTitle">{steps_title}</h2>
      <ol>
        <li><span class="num">1</span><h3 data-t="s1">{s1}</h3><p data-os="win"><b class="osl">Windows. </b><span data-t="s1win">{s1win}</span></p><p class="cmd" data-os="win"><span data-t="cmdWin">{cmd_win}</span><code>{win_cmd}</code></p><p data-os="mac"><b class="osl">macOS. </b><span data-t="s1mac">{s1mac}</span></p><p class="cmd" data-os="mac"><span data-t="cmdMac">{cmd_mac}</span><code>{mac_cmd}</code></p></li>
        <li><span class="num">2</span><h3 data-t="s2">{s2}</h3><p data-t="s2t">{s2t}</p></li>
        <li><span class="num">3</span><h3 data-t="s3">{s3}</h3><p data-t="s3t">{s3t}</p>{bot}</li>
      </ol>
    </section>
  </main>
  <footer data-t="foot">{foot}</footer>
</div>
<script type="application/json" id="page-data">{data}</script>
<script src="page.js"></script>
</body>
</html>
"""


PAGE_JS = """(() => {
  // The texts in both languages and the download links, from the page itself (a strict CSP allows no inline script).
  const data = JSON.parse(document.getElementById("page-data").textContent);
  const T = data.text, U = data.urls;
  const ua = navigator.userAgent || "";
  const platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || "";
  // An iPad asks for the desktop site and says "Macintosh"; its touch screen gives it away.
  const phone = /Android|iPhone|iPad|iPod|Mobile|Windows Phone/i.test(ua) || (/Mac/i.test(platform) && navigator.maxTouchPoints > 1);
  const os = phone ? "phone" : /Win/i.test(platform) || /Windows/i.test(ua) ? "win" : /Mac/i.test(platform) || /Mac OS X/i.test(ua) ? "mac" : "any";
  let arm = false;
  const body = document.body;
  body.classList.remove("os-any");
  body.classList.add("os-" + os);
  if (phone) body.classList.add("is-phone");
  const win = document.getElementById("win"), mac = document.getElementById("mac");
  // The visitor's system first and as the strong button; elsewhere Windows leads, as it is the larger part.
  if (os === "mac") {
    win.classList.remove("primary"); mac.classList.add("primary");
    const dl = document.querySelector(".dl"); dl.insertBefore(dl.querySelector('[data-dl="mac"]'), dl.firstChild);
  }
  let lang = new URLSearchParams(location.search).get("lang") === "en" ? "en" : "ru";
  const apply = () => {
    const s = T[lang];
    document.documentElement.lang = lang;
    document.title = s.title.replace(/&amp;/g, "&");
    document.querySelectorAll("[data-t]").forEach(el => {
      let key = el.dataset.t;
      if (key === "winNote" && arm) key = "winNoteArm";
      el.innerHTML = s[key];
    });
    document.querySelector(".switch").setAttribute("aria-label", s.lang);
    document.querySelectorAll(".switch button").forEach(b => b.setAttribute("aria-pressed", String(b.dataset.lang === lang)));
  };
  document.querySelectorAll(".switch button").forEach(b => b.addEventListener("click", () => {
    lang = b.dataset.lang;
    const url = new URL(location.href);
    if (lang === "en") url.searchParams.set("lang", "en"); else url.searchParams.delete("lang");
    history.replaceState(null, "", url);
    apply();
  }));
  if (os === "win" && navigator.userAgentData && navigator.userAgentData.getHighEntropyValues) {
    navigator.userAgentData.getHighEntropyValues(["architecture"]).then(v => {
      if (v.architecture === "arm") { arm = true; win.href = U.winArm; apply(); }
    }).catch(() => {});
  }
  const share = document.getElementById("share");
  share.addEventListener("click", async () => {
    const url = location.href;
    try {
      if (navigator.share) { await navigator.share({ title: document.title, url }); return; }
    } catch (e) { if (e && e.name === "AbortError") return; }
    try { await navigator.clipboard.writeText(url); } catch (e) {
      const ta = document.createElement("textarea"); ta.value = url; document.body.appendChild(ta); ta.select();
      try { document.execCommand("copy"); } catch (_) {}
      ta.remove();
    }
    share.textContent = T[lang].copied;
    setTimeout(() => { share.textContent = T[lang].share; }, 2400);
  });
  apply();
})();
"""


def main():
    ap = argparse.ArgumentParser(description="Build a partner's download page from partners/<id>/.")
    ap.add_argument("folder", help="partners/<id>")
    ap.add_argument("--out", help="where to write the page (default partners/out/site/<id>)")
    ap.add_argument("--source", help="the releases page the buttons download from (default: the first V2pnUpdateSources)")
    ap.add_argument("--bot", help="the partner's bot link for «Открыть бота» (default: links.support when it is on t.me)")
    ap.add_argument("--home", help="where the V2pn lockup in the bar leads (V2pn's site; default: no link)")
    args = ap.parse_args()
    folder = args.folder if os.path.isdir(args.folder) else os.path.join(REPO, "partners", args.folder)
    if not os.path.isfile(os.path.join(folder, "partner.json")):
        raise SystemExit(f"{args.folder}: no partner.json there")
    pid = os.path.basename(os.path.abspath(folder))
    out = args.out or os.path.join(REPO, "partners", "out", "site", pid)
    build(folder, out, (args.source or release_source()).rstrip("/"), args.bot, args.home)


if __name__ == "__main__":
    main()
