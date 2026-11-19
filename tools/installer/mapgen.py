"""Cuts the installer's map out of the app's world map.

Reads v2rayN/v2rayN.Desktop/Assets/Map/world.json (country outlines in a 2400x1560 plane) and writes
v2rayN/V2pnSetup/Assets/map.txt: the first line is one WPF path of every country in the window's
view, moved so the view starts at 0,0; the lines after it are "<code> <x> <y>", the country centres
the installer draws server dots on. Run it again only when the view or the world map changes:

    python tools/installer/mapgen.py
"""
import json
import math
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SOURCE = os.path.join(ROOT, 'v2rayN', 'v2rayN.Desktop', 'Assets', 'Map', 'world.json')
TARGET = os.path.join(ROOT, 'v2rayN', 'V2pnSetup', 'Assets', 'map.txt')

# Europe down to the Mediterranean: where most V2pn servers stand.
X0, Y0, W, H = 1060, 440, 470, 652
DOTS = ['DE', 'NL', 'FI', 'PL', 'SE', 'FR', 'GB', 'LV', 'AT', 'TR', 'IT', 'ES', 'EE', 'CH', 'NO', 'CZ', 'RO', 'BG', 'IE', 'PT', 'GR']
# Points closer than this to the last kept one are dropped; the panel shows about 0.77 px per unit.
STEP = 1.6


def figures(d):
    for part in re.findall(r'M[^M]*', d):
        yield [(float(x), float(y)) for x, y in re.findall(r'(-?[\d.]+),(-?[\d.]+)', part)]


def simplify(points):
    kept = [points[0]]
    for p in points[1:]:
        if math.dist(p, kept[-1]) >= STEP:
            kept.append(p)
    return kept


def main():
    world = json.load(open(SOURCE, encoding='utf-8'))
    out = []
    for code, country in sorted(world['countries'].items()):
        bx0, by0, bx1, by1 = country['b']
        if bx1 < X0 or by1 < Y0 or bx0 > X0 + W or by0 > Y0 + H:
            continue
        for points in figures(country['d']):
            # Figures entirely outside the view add bytes, not land.
            if all(x < X0 - 5 or x > X0 + W + 5 for x, _ in points) or all(y < Y0 - 5 or y > Y0 + H + 5 for _, y in points):
                continue
            points = simplify(points)
            if len(points) < 3:
                continue
            coords = ' '.join(f'{round(x - X0)} {round(y - Y0)}' for x, y in points)
            first, rest = coords.split(' ', 2)[:2], coords.split(' ', 2)[2] if coords.count(' ') > 1 else ''
            out.append(f'M{first[0]} {first[1]}L{rest}Z')
    lines = [f'{W} {H}', ''.join(out)]
    for code in DOTS:
        c = world['countries'][code]
        lines.append(f'{code} {c["x"] - X0} {c["y"] - Y0}')
    os.makedirs(os.path.dirname(TARGET), exist_ok=True)
    with open(TARGET, 'w', encoding='ascii', newline='\n') as f:
        f.write('\n'.join(lines) + '\n')
    print(f'{TARGET}: {os.path.getsize(TARGET) // 1024} KB, {len(out)} figures')


if __name__ == '__main__':
    main()
