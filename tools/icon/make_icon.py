#!/usr/bin/env python3
"""LoupixDeck KDE Plasma plugin icon: a 2x2 virtual desktop pager, matte, night blue.

Same look as the Audio plugin icon (tile, colours, matte parts, red marker). The active desktop is lit
and holds a window.

Needs:  pip install pillow numpy
Usage:  python make_icon.py [output folder]
Writes icon_{256,128,64,32,16}.png (RGBA, transparent corners).
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SIZE = 256   # design size (px)
SS = 4       # supersampling
N = SIZE * SS
YY, XX = np.mgrid[0:N, 0:N].astype(np.float32)
XX = (XX + 0.5) / SS
YY = (YY + 0.5) / SS


def oklch(L, C, h, a=1.0):
    hr = math.radians(h)
    A, B = C * math.cos(hr), C * math.sin(hr)
    l = (L + 0.3963377774 * A + 0.2158037573 * B) ** 3
    m = (L - 0.1055613458 * A - 0.0638541728 * B) ** 3
    s = (L - 0.0894841775 * A - 1.2914855480 * B) ** 3
    lin = [4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
           -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
           -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s]
    out = [12.92 * c if c <= 0.0031308 else 1.055 * max(c, 0) ** (1 / 2.4) - 0.055 for c in lin]
    return (*[min(max(c, 0.0), 1.0) for c in out], a)


# Colours, shared with the Audio plugin icon
BG = oklch(0.24, 0.02, 260)
EDGE = oklch(0.32, 0.02, 260)
ARC = oklch(0.80, 0.13, 200)
TRACK = oklch(0.34, 0.02, 260)
LINE = oklch(0.64, 0.21, 25)

# Pager: 2x2 desktops, one of them active
GRID_X, GRID_Y = 44, 52
CELL_W, CELL_H, CELL_R, GAP = 78, 66, 12, 12
ACTIVE = (0, 0)  # column, row


# ---------- Masks ----------
def _mask(draw_fn):
    im = Image.new("L", (N, N), 0)
    draw_fn(ImageDraw.Draw(im))
    return np.asarray(im, dtype=np.float32) / 255.0


def rrect(x, y, w, h, r):
    return _mask(lambda d: d.rounded_rectangle([x * SS, y * SS, (x + w) * SS - 1, (y + h) * SS - 1], radius=r * SS, fill=255))


def blur(mask, px):
    if px <= 0:
        return mask
    im = Image.fromarray((np.clip(mask, 0, 1) * 255).astype(np.uint8))
    im = im.filter(ImageFilter.GaussianBlur(px / 2 * SS))  # CSS blur = 2 * sigma
    return np.asarray(im, dtype=np.float32) / 255.0


def shift(mask, dx, dy, fill=0.0):
    out = np.full_like(mask, fill)
    sx, sy = int(round(dx * SS)), int(round(dy * SS))
    h, w = mask.shape
    out[max(sy, 0):h + min(sy, 0), max(sx, 0):w + min(sx, 0)] = mask[max(-sy, 0):h + min(-sy, 0), max(-sx, 0):w + min(-sx, 0)]
    return out


# ---------- Compositing ----------
canvas = np.zeros((N, N, 4), dtype=np.float32)  # straight RGBA


def paint(color, alpha):
    """color: RGBA tuple or HxWx3 array; alpha: HxW mask (multiplied by the colour's alpha)."""
    global canvas
    if isinstance(color, tuple):
        rgb = np.array(color[:3], dtype=np.float32)[None, None, :]
        a = alpha * color[3]
    else:
        rgb, a = color, alpha
    a = a[..., None]
    ca = canvas[..., 3:4]
    oa = a + ca * (1 - a)
    orgb = (rgb * a + canvas[..., :3] * ca * (1 - a)) / np.maximum(oa, 1e-6)
    canvas = np.concatenate([orgb, oa], axis=-1)


def drop_shadow(shape, dx, dy, blur_px, color, clip):
    paint(color, blur(shift(shape, dx, dy), blur_px) * clip)


def inset_shadow(shape, dx, dy, blur_px, color):
    paint(color, blur(shift(1 - shape, dx, dy, fill=1.0), blur_px) * shape)


def linear_gradient(box, css_deg, stops):
    x, y, w, h = box
    th = math.radians(css_deg)
    dx, dy = math.sin(th), -math.cos(th)
    L = abs(w * dx) + abs(h * dy)
    t = ((XX - (x + w / 2)) * dx + (YY - (y + h / 2)) * dy) / L + 0.5
    t = np.clip(t, 0, 1)
    pos = [s[0] for s in stops]
    return np.stack([np.interp(t, pos, [s[1][i] for s in stops]) for i in range(3)], axis=-1).astype(np.float32)


# ---------- Drawing ----------
icon = rrect(0, 0, SIZE, SIZE, 58)

# Background and 1 px inner edge
paint(BG, icon)
paint(EDGE, icon - rrect(1, 1, SIZE - 2, SIZE - 2, 57))

# Desktops: dim slots, the active one outlined in the level colour
for col in range(2):
    for row in range(2):
        x, y = GRID_X + col * (CELL_W + GAP), GRID_Y + row * (CELL_H + GAP)
        cell = rrect(x, y, CELL_W, CELL_H, CELL_R)
        paint(TRACK, cell)
        if (col, row) == ACTIVE:
            paint(ARC, cell - rrect(x + 4, y + 4, CELL_W - 8, CELL_H - 8, CELL_R - 4))
            paint(oklch(0.80, 0.13, 200, 0.14), rrect(x + 4, y + 4, CELL_W - 8, CELL_H - 8, CELL_R - 4))

# Window (plastic, matte) on the active desktop
col, row = ACTIVE
WX, WY = GRID_X + col * (CELL_W + GAP) + 12, GRID_Y + row * (CELL_H + GAP) + 12
WW, WH, WR = CELL_W - 24, CELL_H - 24, 7
win = rrect(WX, WY, WW, WH, WR)
drop_shadow(win, 0, 12, 18, oklch(0.04, 0.04, 260, 0.80), icon)
drop_shadow(win, 0, 3, 2, oklch(0.06, 0.03, 260, 0.55), icon)
paint(linear_gradient((WX, WY, WW, WH), 165, [(0, oklch(0.93, 0.006, 260)[:3]), (1, oklch(0.78, 0.01, 260)[:3])]), win)
inset_shadow(win, 0, -3, 5, oklch(0.4, 0.02, 260, 0.30))
inset_shadow(win, 0, 2, 2, (1, 1, 1, 0.50))
# Title bar: a separating groove and a red close button
paint(oklch(0.60, 0.01, 260, 0.45), rrect(WX + 5, WY + 13, WW - 10, 2, 1))
paint(LINE, rrect(WX + WW - 12, WY + 4, 7, 7, 3.5))

# Clip to the icon shape
canvas[..., 3] *= icon

# ---------- Export ----------
if __name__ == "__main__":
    out_dir = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(out_dir, exist_ok=True)
    big = Image.fromarray((np.clip(canvas, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA")
    for s in (256, 128, 64, 32, 16):
        path = os.path.join(out_dir, f"icon_{s}.png")
        big.resize((s, s), Image.LANCZOS).save(path)
        print("written:", path)
