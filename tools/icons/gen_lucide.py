"""Generates v2rayN.Desktop/Common/LucideIcons.g.cs from the lucide-static npm package.

V2pn draws every icon from Lucide (https://lucide.dev, ISC licence) so they share one outline
style. Each Lucide SVG (paths, circles, rects, lines, polylines) becomes a single path string on
Lucide's 24x24 grid; at start the app widens it into a filled outline (Common/Icons.cs) and puts it
under the Semi icon names the views already use.

Usage: python tools/icons/gen_lucide.py <lucide-static>/icons
Edit MAP below to change which Lucide icon stands for which name.
"""

import os
import re
import sys
import xml.etree.ElementTree as ET

# Semi icon name used by the views -> Lucide icon file.
MAP = {
    # V2pn: icons a partner's buttons may wear (V2pnActionIcons) that no Semi name stood for.
    "V2pnIconGame": "gamepad-2",
    "V2pnIconCard": "credit-card",
    "V2pnIconGift": "gift",
    "SemiIconRefresh": "refresh-cw",
    "SemiIconRefresh2": "refresh-ccw",
    "SemiIconRotate": "rotate-cw",
    "SemiIconGlobe": "globe",
    "SemiIconGlobeStroke": "globe",
    "SemiIconGlobeStroked": "globe",
    "SemiIconLock": "lock",
    "SemiIconLockStroked": "lock",
    "SemiIconMore": "ellipsis",
    "SemiIconMoreStroked": "ellipsis",
    "SemiIconBolt": "zap",
    "SemiIconShield": "shield",
    "SemiIconShieldStroked": "shield",
    "SemiIconTickCircle": "circle-check",
    "SemiIconAlertCircle": "circle-alert",
    "SemiIconAlertTriangle": "triangle-alert",
    "SemiIconInfoCircle": "info",
    "SemiIconLink": "link",
    "SemiIconChainStroked": "link",
    "SemiIconUnlink": "unlink",
    "SemiIconClose": "x",
    "SemiIconServer": "server",
    "SemiIconServerStroked": "server",
    "SemiIconPlus": "plus",
    "SemiIconPlusStroked": "plus",
    "SemiIconMinus": "minus",
    "SemiIconMinusStroked": "minus",
    "SemiIconMinimize": "minus",
    "SemiIconMaximize": "square",
    "SemiIconPlusCircleStroked": "circle-plus",
    "SemiIconMinusCircleStroked": "circle-minus",
    "SemiIconKey": "key-round",
    "SemiIconKeyStroked": "key-round",
    "SemiIconEditStroked": "pencil",
    "SemiIconCode": "code",
    "SemiIconCodeStroked": "code",
    "SemiIconChevronUpDown": "chevrons-up-down",
    "SemiIconChevronDown": "chevron-down",
    "SemiIconChevronDownStroked": "chevron-down",
    "SemiIconChevronUp": "chevron-up",
    "SemiIconChevronLeft": "chevron-left",
    "SemiIconChevronRight": "chevron-right",
    "SemiIconChevronRightStroked": "chevron-right",
    "SemiIconUser": "user",
    "SemiIconUserStroked": "user",
    "SemiIconStarStroked": "star",
    "SemiIconStar": "star",
    "SemiIconSetting": "settings",
    "SemiIconSettingStroked": "settings",
    "SemiIconSearch": "search",
    "SemiIconSearchStroked": "search",
    "SemiIconRoute": "route",
    "SemiIconBranch": "git-branch",
    "SemiIconFolder": "folder",
    "SemiIconFolderStroked": "folder",
    "SemiIconFolderOpen": "folder-open",
    "SemiIconFolderOpenStroked": "folder-open",
    "SemiIconDownload": "download",
    "SemiIconDownloadStroked": "download",
    "SemiIconCloudUploadStroked": "cloud-upload",
    "SemiIconDesktop": "monitor",
    "SemiIconTerminal": "terminal",
    "SemiIconPlayCircle": "circle-play",
    "SemiIconHelpCircleStroked": "circle-question-mark",
    "SemiIconFilterStroked": "funnel",
    "SemiIconExpand": "expand",
    "SemiIconShrink": "shrink",
    "SemiIconDelete": "trash-2",
    "SemiIconDeleteStroked": "trash-2",
    "SemiIconArchive": "archive",
    "SemiIconActivity": "activity",
    "SemiIconPulse": "activity",
    "SemiIconWifi": "wifi",
    "SemiIconScan": "scan",
    "SemiIconMenu": "menu",
    "SemiIconListView": "list",
    "SemiIconImage": "image",
    "SemiIconImageStroked": "image",
    "SemiIconHash": "hash",
    "SemiIconFile": "file",
    "SemiIconExternalOpen": "external-link",
    "SemiIconExternalOpenStroked": "external-link",
    "SemiIconExit": "log-out",
    "SemiIconCopy": "copy",
    "SemiIconCopyStroked": "copy",
    "SemiIconCopyAdd": "copy-plus",
    "SemiIconComponent": "component",
    "SemiIconCommand": "command",
    "SemiIconColorPalette": "palette",
    "SemiIconArrowUp": "arrow-up",
    "SemiIconArrowDown": "arrow-down",
    "SemiIconStopwatchStroked": "timer",
    "SemiIconQrCode": "qr-code",
    "SemiIconComment": "message-circle",
    "SemiIconHistory": "history",
    "SemiIconMoon": "moon",
    "SemiIconSun": "sun",
    "SemiIconPower": "power",
    "SemiIconHome": "house",
    "SemiIconSort": "arrow-down-wide-narrow",
    "SemiIconHistogram": "chart-column",
    "SemiIconTrophy": "trophy",
}

# Drawn filled as well as outlined: a set favourite star.
FILLED = {"SemiIconStar"}

TOKEN = re.compile(r"[MmLlHhVvCcSsQqTtAaZz]|-?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")


def normalize_path(d):
    """Re-emits SVG path data with explicit separators; arc flags may be packed ("00-2.4")."""
    out = []
    i = 0
    cmd = None
    s = d.strip()
    while i < len(s):
        c = s[i]
        if c in " ,\n\t":
            i += 1
            continue
        if c.isalpha():
            cmd = c
            out.append(c)
            i += 1
            continue
        if cmd in "Aa":
            # seven numbers per arc; the 4th and 5th are single-digit flags
            params = []
            while len(params) < 7 and i < len(s):
                while i < len(s) and s[i] in " ,\n\t":
                    i += 1
                if len(params) in (3, 4):
                    params.append(s[i])
                    i += 1
                    continue
                m = TOKEN.match(s, i)
                params.append(m.group(0))
                i = m.end()
            out.append(" ".join(params))
            continue
        m = TOKEN.match(s, i)
        if not m:
            raise ValueError("bad path data near: " + s[i:i + 20])
        out.append(m.group(0))
        i = m.end()
    return explicit_lines(out)


def explicit_lines(tokens):
    """SVG treats extra coordinate pairs after a moveto as lineto ("M18 6 6 18"); Avalonia's
    parser does not, so the implied L/l is written out."""
    result = []
    cmd = None
    count = 0
    for index, t in enumerate(tokens):
        if t[0].isalpha():
            cmd = t
            count = 0
            # A path's first moveto is absolute even when written "m"; the paths are joined into
            # one string here, so a leading "m" would otherwise move relative to the previous one.
            result.append("M" if index == 0 and t == "m" else t)
            continue
        if cmd in ("M", "m"):
            if count == 2 or (count > 2 and count % 2 == 0):
                cmd = "L" if cmd == "M" else "l"
                result.append(cmd)
                count = 0
        result.append(t)
        count += 1
    return " ".join(result)


def f(v):
    return float(v)


def element_path(el):
    tag = el.tag.split("}")[-1]
    a = el.attrib
    if tag == "path":
        return normalize_path(a["d"])
    if tag == "circle":
        cx, cy, r = f(a["cx"]), f(a["cy"]), f(a["r"])
        return f"M {cx - r} {cy} A {r} {r} 0 1 0 {cx + r} {cy} A {r} {r} 0 1 0 {cx - r} {cy} Z"
    if tag == "ellipse":
        cx, cy, rx, ry = f(a["cx"]), f(a["cy"]), f(a["rx"]), f(a["ry"])
        return f"M {cx - rx} {cy} A {rx} {ry} 0 1 0 {cx + rx} {cy} A {rx} {ry} 0 1 0 {cx - rx} {cy} Z"
    if tag == "line":
        return f"M {f(a['x1'])} {f(a['y1'])} L {f(a['x2'])} {f(a['y2'])}"
    if tag in ("polyline", "polygon"):
        pts = re.findall(r"-?\d*\.?\d+", a["points"])
        pairs = [f"{pts[i]} {pts[i + 1]}" for i in range(0, len(pts), 2)]
        return "M " + " L ".join(pairs) + (" Z" if tag == "polygon" else "")
    if tag == "rect":
        x, y, w, h = f(a.get("x", 0)), f(a.get("y", 0)), f(a["width"]), f(a["height"])
        rx = f(a.get("rx", a.get("ry", 0)))
        ry = f(a.get("ry", rx))
        if rx == 0:
            return f"M {x} {y} H {x + w} V {y + h} H {x} Z"
        return (f"M {x + rx} {y} H {x + w - rx} A {rx} {ry} 0 0 1 {x + w} {y + ry} V {y + h - ry} "
                f"A {rx} {ry} 0 0 1 {x + w - rx} {y + h} H {x + rx} A {rx} {ry} 0 0 1 {x} {y + h - ry} "
                f"V {y + ry} A {rx} {ry} 0 0 1 {x + rx} {y} Z")
    raise ValueError("unsupported element " + tag)


def icon_path(folder, name):
    tree = ET.parse(os.path.join(folder, name + ".svg"))
    return " ".join(element_path(el) for el in tree.getroot().iter() if el.tag.split("}")[-1] != "svg")


def main():
    folder = sys.argv[1]
    missing = [n for n in set(MAP.values()) if not os.path.exists(os.path.join(folder, n + ".svg"))]
    if missing:
        sys.exit("missing Lucide icons: " + ", ".join(sorted(missing)))
    lines = []
    for key in sorted(MAP):
        lines.append(f'        ["{key}"] = "{icon_path(folder, MAP[key])}",')
    filled = ", ".join(f'"{k}"' for k in sorted(FILLED))
    out = f"""// <auto-generated> by tools/icons/gen_lucide.py from lucide-static (ISC licence). Do not edit.
namespace v2rayN.Desktop.Common;

internal static partial class Icons
{{
    /// <summary>Lucide outlines on the 24x24 grid, keyed by the Semi icon name they stand for.</summary>
    private static readonly Dictionary<string, string> Lucide = new()
    {{
{chr(10).join(lines)}
    }};

    /// <summary>Icons drawn filled as well as outlined.</summary>
    private static readonly HashSet<string> Filled = [{filled}];
}}
"""
    target = os.path.join(os.path.dirname(__file__), "..", "..", "v2rayN", "v2rayN.Desktop", "Common", "LucideIcons.g.cs")
    with open(target, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(out)
    print(f"{len(MAP)} icons -> {os.path.normpath(target)}")


if __name__ == "__main__":
    main()
