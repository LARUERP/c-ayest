#!/usr/bin/env python3
"""Modèles v5.2 : cannabis refait (3 stades, mêmes noms de modèles : remplacement direct).
Usage : python3 Tools/GenerateModelsV5.py     (après GenerateModels.py)
"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import GenerateModels as G
from modellib import *      # noqa
import texlib as T
from PIL import Image, ImageDraw

mat, save, tex = G.mat, G.save, G.tex
OUT = G.RPDIR


# ----------------------------------------------------------------------
# Textures
# ----------------------------------------------------------------------
def textures():
    tex("Weed_Leaf.png", leaf_texture())


def leaf_texture(w=128, h=128):
    """Foliole : dégradé vert, nervure centrale claire, nervures latérales ; u = travers, v = longueur."""
    img = Image.new("RGB", (w, h), (30, 100, 28))
    px = img.load()
    for y in range(h):
        t = y / (h - 1)
        for x in range(w):
            s = abs((x / (w - 1)) - 0.5) * 2.0          # 0 au centre, 1 au bord
            base = (28 + int(26 * t), 96 + int(40 * (1 - s)), 26 + int(12 * t))
            px[x, y] = base
    d = ImageDraw.Draw(img)
    d.line([(w // 2, 0), (w // 2, h)], fill=(150, 196, 92), width=4)
    for k in range(1, 9):
        y0 = int(h * k / 9.0)
        d.line([(w // 2, y0), (4, max(0, y0 - h // 9))], fill=(88, 148, 56), width=1)
        d.line([(w // 2, y0), (w - 4, max(0, y0 - h // 9))], fill=(88, 148, 56), width=1)
    return img


# ----------------------------------------------------------------------
# Cannabis (même noms de modèles : remplacement direct)
# ----------------------------------------------------------------------
def leaflet(m, mt, base, yaw, pitch, length, width, fold=0.22):
    d = (math.cos(yaw) * math.cos(pitch), math.sin(pitch), math.sin(yaw) * math.cos(pitch))
    side = (-math.sin(yaw), 0.0, math.cos(yaw))
    nrm = cross(d, side)
    if nrm[1] < 0:
        nrm = mul(nrm, -1.0)
    n = 4
    rows = []
    for i in range(n + 1):
        t = i / n
        w = width * 0.5 * (math.sin(math.pi * min(1.0, t * 1.04)) ** 0.8) * (0.72 if i % 2 else 1.0)   # dents marquées
        c = add(base, mul(d, length * t))
        c = (c[0], c[1] - length * 0.24 * t * t, c[2])                                                   # port retombant
        rows.append((add(c, add(mul(side, w), mul(nrm, w * fold))), c,
                     add(c, add(mul(side, -w), mul(nrm, w * fold))), t))
    for i in range(n):
        a, b = rows[i], rows[i + 1]
        for p, q, u0, u1 in ((0, 1, 0.0, 0.5), (1, 2, 0.5, 1.0)):
            double_quad(m, mt, a[p], a[q], b[q], b[p],
                        uv=((u0, a[3]), (u1, a[3]), (u1, b[3]), (u0, b[3])))


def fan_leaf(m, base, yaw, size, count=7, pitch=-0.12):
    petiole = (base[0] + math.cos(yaw) * size * 0.3, base[1] + 0.004, base[2] + math.sin(yaw) * size * 0.3)
    tube(m, "Stem", base, petiole, size * 0.03, 5, True, size * 0.018)
    mid = (count - 1) / 2.0
    step = 0.40 if count <= 7 else 0.33
    for k in range(count):
        off = k - mid
        ln = size * (1.0 - 0.15 * abs(off))
        leaflet(m, "Leaf", petiole, yaw + off * step, pitch - 0.05 * abs(off), ln, ln * 0.17)


def nursery_pot(m, h=0.085, r=0.058):
    lathe(m, "Pot", (0, 0, 0), 'y',
          [(r * 0.70, 0.0), (r * 0.74, 0.004), (r * 0.96, h * 0.92), (r * 1.04, h)], 22)
    torus(m, "Pot", (0, h, 0), 'y', r * 1.02, 0.0055, 22, 6)
    cylinder(m, "Soil", "Soil", (0, h * 0.90, 0), 'y', r * 0.96, 0.016, 20)
    for i in range(7):                                      # petits cailloux de drainage
        a = i * 2.3
        sphere(m, "Pebble", (math.cos(a) * r * 0.6, h * 0.99, math.sin(a) * r * 0.6), 0.0065, 6, 4, (1, 0.6, 1))
    lathe(m, "Saucer", (0, -0.006, 0), 'y',
          [(0.0, 0.0), (r * 1.05, 0.0), (r * 1.3, 0.012), (r * 1.34, 0.016)], 22)


def cola(m, p, scale=1.0, height=0.05):
    """Cola « queue de renard » : calices empilés qui s'affinent, pistils, feuilles de sucre, cristaux."""
    kinds = ("Bud", "BudLight", "BudPurple")
    n = 6
    for i in range(n):
        t = i / (n - 1.0)
        r = 0.0125 * scale * (1.0 - 0.45 * t)
        a = i * 2.4
        c = (p[0] + math.cos(a) * 0.005 * scale * (1 - t), p[1] + (i * height / n) * scale,
             p[2] + math.sin(a) * 0.005 * scale * (1 - t))
        sphere(m, kinds[i % 3], c, r, 7, 4, (1.0, 1.3, 1.0))
        for k in range(2):
            b = a + k * 3.1 + 0.5
            q = add(c, (math.cos(b) * r * 0.8, r * 0.3, math.sin(b) * r * 0.8))
            tube(m, "Hair", q, add(q, (math.cos(b) * 0.011 * scale, 0.009 * scale, math.sin(b) * 0.011 * scale)),
                 0.0013, 4, False, 0.0003)
        if i % 2 == 0:
            leaflet(m, "Leaf", c, a + 1.2, 0.5, 0.032 * scale, 0.008 * scale)
    for i in range(3):
        a = i * 2.2
        sphere(m, "Crystal", add(p, (math.cos(a) * 0.012 * scale, 0.02 * scale + i * 0.012 * scale, math.sin(a) * 0.012 * scale)),
               0.0032, 4, 3)


WEED_MATS = {
    "Pot": mat((0.07, 0.07, 0.08), ns=160),
    "Saucer": mat((0.16, 0.16, 0.17), ns=200),
    "Soil": mat((0.10, 0.06, 0.035), ns=30),
    "Pebble": mat((0.5, 0.48, 0.44), ns=60),
    "Stem": mat((0.22, 0.38, 0.12), ns=120),
    "Leaf": mat(map_kd="Weed_Leaf.png", ns=220),
    "Bud": mat((0.40, 0.58, 0.22), ns=150),
    "BudLight": mat((0.55, 0.72, 0.32), ns=150),
    "BudPurple": mat((0.36, 0.3, 0.46), ns=150),
    "Hair": mat((0.96, 0.52, 0.08), ns=120, ke=(0.18, 0.08, 0.0)),
    "Stake": mat((0.62, 0.45, 0.2), ns=80),
    "Crystal": mat((0.92, 0.96, 0.85), ns=800, ke=(0.25, 0.28, 0.22)),
}


def plants():
    # stade 1 : semis (cotylédons + premières vraies feuilles)
    m = Mesh()
    nursery_pot(m)
    tube(m, "Stem", (0, 0.085, 0), (0, 0.125, 0), 0.0028, 6, True, 0.002)
    for a in (0.0, math.pi):
        leaflet(m, "Leaf", (0, 0.122, 0), a, 0.05, 0.03, 0.014)
    for a in (1.57, 4.71):
        fan_leaf(m, (0, 0.127, 0), a, 0.04, 3)
    save("WeedPot1", m, WEED_MATS, OUT)

    # stade 2 : croissance végétative (tuteur, noeuds alternés, premières ramifications)
    m = Mesh()
    nursery_pot(m)
    tube(m, "Stake", (0.045, 0.08, 0.0), (0.045, 0.31, 0.0), 0.0035, 6, True)
    tube(m, "Stem", (0, 0.085, 0), (0, 0.28, 0), 0.0068, 8, True, 0.0028)
    for i, y in enumerate((0.115, 0.15, 0.185, 0.22)):
        for j in range(2):
            fan_leaf(m, (0, y, 0), j * math.pi + i * 1.1, 0.092 - i * 0.012, 9 if i < 2 else 7)
    for a, y in ((0.7, 0.17), (3.8, 0.205)):                  # rameaux latéraux
        tip = (math.cos(a) * 0.04, y + 0.03, math.sin(a) * 0.04)
        tube(m, "Stem", (0, y, 0), tip, 0.0032, 6, True, 0.002)
        fan_leaf(m, tip, a, 0.05, 5, 0.0)
    fan_leaf(m, (0, 0.277, 0), 0.3, 0.05, 5, 0.05)
    save("WeedPot2", m, WEED_MATS, OUT)

    # stade 3 : floraison (grandes feuilles basses, branches, colas queue de renard)
    m = Mesh()
    nursery_pot(m, 0.09, 0.062)
    tube(m, "Stake", (-0.055, 0.085, 0.0), (-0.055, 0.42, 0.0), 0.004, 6, True)
    tube(m, "Stem", (0, 0.09, 0), (0, 0.46, 0), 0.0098, 8, True, 0.0035)
    for i, y in enumerate((0.13, 0.17, 0.21)):
        for j in range(2):
            fan_leaf(m, (0, y, 0), j * math.pi + i * 0.9, 0.125 - i * 0.012, 9)
    for i, y in enumerate((0.25, 0.30, 0.35, 0.40)):
        a = i * 2.2
        tip = (math.cos(a) * 0.085, y + 0.05, math.sin(a) * 0.085)
        tube(m, "Stem", (0, y, 0), tip, 0.0044, 6, True, 0.0022)
        cola(m, tip, 1.15, 0.05)
        fan_leaf(m, (tip[0] * 0.5, y + 0.02, tip[2] * 0.5), a + 0.5, 0.06, 5)
    cola(m, (0.0, 0.46, 0.0), 1.7, 0.09)
    save("WeedPot3", m, WEED_MATS, OUT)


def main():
    textures()
    plants()
    for name, img in G.TEXTURES.items():
        img.save(os.path.join(G.MONEY, name), optimize=True)
    for row in G.REPORT:
        print("%-12s tris=%5d verts=%5d dim=%s" % row)
    try:
        for n in ("WeedPot1", "WeedPot2", "WeedPot3"):
            preview(G.MESHES[n], "/tmp/%s_preview.png" % n)
    except Exception as exc:
        print("aperçu ignoré :", exc)
    return 0


if __name__ == "__main__":
    sys.exit(main())
