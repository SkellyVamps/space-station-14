#!/usr/bin/env python3
"""Draws an overhead plan of the Tutorial Station: python3 render.py plan.png"""
import sys
from PIL import Image, ImageDraw, ImageFont

import generate as g

g.build()
g.finish()

HEX = {"Basics": "#e8e8e8", "Command": "#3f7fe0", "Security": "#e03a3a", "Engineering": "#f59a23", "Science": "#a05ce0",
       "Medical": "#4ad6e0", "Cargo": "#b07a3c", "Food": "#5cc24a", "Culture": "#e070b8", "Civilian": "#9a9a9a", "Hub": "#f2d24b",
       "Threats": "#c2185b", "Outsiders": "#b5e61d"}
S = 9
xs = [x for x, _ in g.tiles]; ys = [y for _, y in g.tiles]
x0, x1, y0, y1 = min(xs) - 2, max(xs) + 2, min(ys) - 6, max(ys) + 6
W, H = (x1 - x0 + 1) * S, (y1 - y0 + 1) * S
img = Image.new("RGB", (W, H), "#0d1117")
d = ImageDraw.Draw(img)
P = lambda x, y: ((x - x0) * S, (y1 - y) * S)


def cell(x, y, col):
    px, py = P(x, y)
    d.rectangle([px, py, px + S - 1, py + S - 1], fill=col)


walls = {(round(e["x"] - .5), round(e["y"] - .5)) for e in g.ents if e["p"].startswith("Wall")}
for (x, y), t in g.tiles.items():
    cell(x, y, "#2b3038" if (x, y) in walls else ("#3a4a5a" if t == "Lattice" else "#5d6670"))


def tint(hexc, a=0.45):
    r, gg, b = (int(hexc[i:i + 2], 16) for i in (1, 3, 5))
    return tuple(int(c * a + 0x5d * (1 - a)) for c in (r, gg, b))


for wing, title, ((ax, ay), (bx, by)) in g.room_rects:
    col = tint(HEX[g.PORTAL_COLOUR[wing]])
    for x in range(min(ax, bx), max(ax, bx) + 1):
        for y in range(min(ay, by), max(ay, by) + 1):
            cell(x, y, col)
for e in g.ents:
    x, y = round(e["x"] - .5), round(e["y"] - .5)
    if e["p"].startswith("PortalGatewayTutorial"):
        px, py = P(x, y)
        d.ellipse([px + 1, py + 1, px + S - 2, py + S - 2], fill=HEX[e["p"].replace("PortalGatewayTutorial", "")])
    elif e["p"].startswith("Airlock"):
        cell(x, y, "#c9d1d9")

try:
    font = ImageFont.truetype("DejaVuSans-Bold.ttf", 15)
except OSError:
    font = ImageFont.load_default()
for i, w in enumerate(g.WINGS):
    cx = g.wing_cx(i)
    y = max(ys) + 3 if w["side"] == "N" else min(ys) - 2
    px, py = P(cx, y)
    d.text((px, py), w["name"], fill=HEX[g.PORTAL_COLOUR[w["name"]]], font=font, anchor="mm")
for label, (x, y) in {"Hub": (-5, 7.5), "Portal Hall": (-22, 2 * len(g.WINGS) - 6.5), "Power Core": (max(xs) - 5, 6.5)}.items():
    px, py = P(x, y)
    d.text((px, py), label, fill="#f2d24b", font=font, anchor="mm")
img.save(sys.argv[1])
print(img.size)
