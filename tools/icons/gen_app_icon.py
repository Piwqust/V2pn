"""Draws V2pn's app icon for the systems that do not take the Windows .ico.

Usage: python tools/icons/gen_app_icon.py          (needs Pillow)

Writes into v2rayN/v2rayN.Desktop:
  v2rayN.icns  the Mac's icon; tools/package-macos.sh copies it into the bundle as AppIcon.icns
  v2rayN.png   256 px, for Linux desktop entries

The picture is the brand book's (docs/brand/v2pn-brand-book.html, "App icon and tray"): the white fork on a Signal
tile, the fork at 56% of the tile. The png has the book's 22% corner radius. The Mac's follows Apple's icon grid
instead, as every Mac app's does: the tile takes 824 of 1024 px with a Mac icon's corner radius and a soft shadow under
it, so in the Dock it is the same size and shape as its neighbours.
"""

import os

from PIL import Image, ImageChops, ImageDraw, ImageFilter

SIGNAL = (0xFF, 0x3D, 0x1F, 255)
WHITE = (255, 255, 255, 255)
# The fork of Assets/V2pnTheme.axaml (V2BrandFork), 104 x 112.
FORK = [(0, 0), (32, 0), (32, 40), (72, 0), (104, 0), (104, 32), (85.255, 32), (64, 53.255),
        (64, 112), (32, 112), (32, 72), (0, 40)]
FORK_W, FORK_H = 104, 112
FORK_SHARE = 0.56
# Drawn four times larger and scaled down: Pillow's shapes have hard edges.
SS = 4

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "v2rayN", "v2rayN.Desktop"))


def fork(draw, left, top, size):
    """The fork with its height equal to FORK_SHARE of the tile, in the middle of the tile."""
    height = size * FORK_SHARE
    scale = height / FORK_H
    x0 = left + (size - FORK_W * scale) / 2
    y0 = top + (size - height) / 2
    draw.polygon([(x0 + x * scale, y0 + y * scale) for x, y in FORK], fill=WHITE)


def mac_icon(size=1024):
    big = size * SS
    tile = 824 / 1024 * big
    left = top = (big - tile) / 2
    plate = Image.new("L", (big, big), 0)
    # 185.4 of 824: the corner radius of Apple's icon template.
    ImageDraw.Draw(plate).rounded_rectangle((left, top, left + tile, top + tile), radius=185.4 / 824 * tile, fill=255)

    # Apple's template: a shadow 10 px down, blurred by 10 px, at 30% black (of 1024).
    shadow = ImageChops.offset(plate, 0, int(10 / 1024 * big))
    shadow = shadow.filter(ImageFilter.GaussianBlur(10 / 1024 * big)).point(lambda a: int(a * 0.3))
    icon = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    icon.paste((0, 0, 0, 255), mask=shadow)
    icon.paste(SIGNAL, mask=plate)
    fork(ImageDraw.Draw(icon), left, top, tile)
    return icon.resize((size, size), Image.LANCZOS)


def tile_icon(size=256):
    big = size * SS
    icon = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    draw = ImageDraw.Draw(icon)
    draw.rounded_rectangle((0, 0, big - 1, big - 1), radius=0.22 * big, fill=SIGNAL)
    fork(draw, 0, 0, big)
    return icon.resize((size, size), Image.LANCZOS)


def main():
    master = mac_icon()
    # Every size macOS asks for, each scaled from the 1024 px drawing.
    master.save(os.path.join(OUT, "v2rayN.icns"), format="ICNS",
                sizes=[(16, 16), (32, 32), (64, 64), (128, 128), (256, 256), (512, 512), (1024, 1024)])
    tile_icon().save(os.path.join(OUT, "v2rayN.png"))
    print("written:", os.path.join(OUT, "v2rayN.icns"), os.path.join(OUT, "v2rayN.png"))


if __name__ == "__main__":
    main()
