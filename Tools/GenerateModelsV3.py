#!/usr/bin/env python3
"""Modèles v5 : trésor municipal, plants de cannabis (3 stades), sachet de récolte.
Usage : python3 Tools/GenerateModelsV3.py  (après GenerateModels.py / V2)"""
import math, os, re, shutil, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import GenerateModels as G
from modellib import *      # noqa

out = G.RPDIR
mat, save = G.mat, G.save


def treasury():
    """Coffre municipal : même géométrie que BankVault (mêmes zones d'interaction),
    acier bleu nuit + laiton doré."""
    src_obj = os.path.join(G.MONEY, "BankVault.obj")
    src_mtl = os.path.join(G.MONEY, "BankVault.mtl")
    dst_obj = os.path.join(G.MONEY, "TownTreasury.obj")
    dst_mtl = os.path.join(G.MONEY, "TownTreasury.mtl")
    o = open(src_obj, encoding="utf-8").read().replace("mtllib BankVault.mtl", "mtllib TownTreasury.mtl")
    open(dst_obj, "w", encoding="utf-8").write(o)
    t = open(src_mtl, encoding="utf-8").read()
    t = t.replace("newmtl Body\nKa 0.20 0.20 0.20\nKd 1.000 1.000 1.000", "newmtl Body\nKa 0.20 0.20 0.20\nKd 0.420 0.520 0.900")
    t = t.replace("newmtl Trim\nKa 0.20 0.20 0.20\nKd 1.000 1.000 1.000", "newmtl Trim\nKa 0.20 0.20 0.20\nKd 0.900 0.800 0.450")
    t = t.replace("newmtl DoorSide\nKa 0.20 0.20 0.20\nKd 1.000 1.000 1.000", "newmtl DoorSide\nKa 0.20 0.20 0.20\nKd 0.500 0.600 0.950")
    t = t.replace("Kd 0.820 0.660 0.300", "Kd 1.000 0.840 0.300")
    open(dst_mtl, "w", encoding="utf-8").write(t)
    print("TownTreasury   copie de BankVault (bleu nuit / or)")


def leaf(m, base, yaw, size, mt="Leaf"):
    """Feuille palmée : 7 folioles coniques en éventail, légèrement retombantes."""
    for k in range(-3, 4):
        a = yaw + k * 0.42
        ln = size * (1.0 - abs(k) * 0.16)
        tip = (base[0] + math.cos(a) * ln, base[1] - ln * 0.18 + 0.004 * (3 - abs(k)), base[2] + math.sin(a) * ln)
        tube(m, mt, base, tip, size * 0.075, 5, True, 0.0006)


def pot(m, h=0.07, r=0.055):
    lathe(m, "Pot", (0, 0, 0), 'y', [(r * 0.72, 0.0), (r * 0.8, 0.004), (r, h * 0.9), (r * 1.06, h)], 24)
    cylinder(m, "Soil", "Soil", (0, h * 0.93, 0), 'y', r * 0.98, 0.012, 22)
    torus(m, "Pot", (0, h, 0), 'y', r * 1.03, 0.006, 24, 6)


MATS = {"Pot": mat((0.62, 0.30, 0.18), ns=120), "Soil": mat((0.12, 0.07, 0.04), ns=40),
        "Stem": mat((0.2, 0.42, 0.14), ns=120), "Leaf": mat((0.12, 0.5, 0.12), ns=160),
        "LeafDark": mat((0.07, 0.34, 0.09), ns=160), "Bud": mat((0.45, 0.62, 0.25), ns=140),
        "BudPurple": mat((0.42, 0.25, 0.5), ns=140), "Hair": mat((0.95, 0.55, 0.1), ns=120, ke=(0.2, 0.1, 0.0))}


def plants():
    # stade 1 : semis
    m = Mesh(); pot(m)
    tube(m, "Stem", (0, 0.08, 0), (0, 0.115, 0), 0.003, 6)
    leaf(m, (0, 0.113, 0), 0.0, 0.022); leaf(m, (0, 0.113, 0), math.pi, 0.022)
    save("WeedPot1", m, MATS, out)
    # stade 2 : plante en croissance
    m = Mesh(); pot(m)
    tube(m, "Stem", (0, 0.08, 0), (0, 0.25, 0), 0.006, 8, True, 0.003)
    for i, y in enumerate((0.13, 0.17, 0.21, 0.245)):
        for j in range(3):
            leaf(m, (0, y, 0), j * 2.09 + i * 0.7, 0.05 - i * 0.006, "Leaf" if i % 2 == 0 else "LeafDark")
    save("WeedPot2", m, MATS, out)
    # stade 3 : plante mûre, têtes florales
    m = Mesh(); pot(m, 0.08, 0.06)
    tube(m, "Stem", (0, 0.09, 0), (0, 0.46, 0), 0.009, 8, True, 0.004)
    for i, y in enumerate((0.14, 0.19, 0.24, 0.29, 0.34, 0.39, 0.435)):
        for j in range(4):
            leaf(m, (0, y, 0), j * 1.57 + i * 0.6, 0.075 - i * 0.007, "Leaf" if (i + j) % 2 == 0 else "LeafDark")
    for i in range(9):                                            # têtes
        a = i * 2.4
        y = 0.20 + i * 0.03
        r0 = 0.012 + (i % 3) * 0.002
        p = (math.cos(a) * 0.045, y, math.sin(a) * 0.045)
        tube(m, "Stem", (0, y - 0.01, 0), p, 0.003, 5)
        sphere(m, "Bud" if i % 3 else "BudPurple", p, r0 * 1.6, 10, 6, (1.0, 1.5, 1.0))
        for k in range(3):
            sphere(m, "Hair", (p[0] + math.cos(a + k) * 0.01, p[1] + 0.012, p[2] + math.sin(a + k) * 0.01), 0.0035, 5, 4)
    sphere(m, "Bud", (0, 0.49, 0), 0.026, 12, 8, (1.0, 1.7, 1.0))
    for k in range(5):
        sphere(m, "Hair", (math.cos(k * 1.3) * 0.012, 0.5, math.sin(k * 1.3) * 0.012), 0.004, 5, 4)
    save("WeedPot3", m, MATS, out)


def bag():
    m = Mesh()
    rbox(m, "Plastic", (0, 0.045, 0), (0.12, 0.09, 0.035), 0.012, 2, tile=3)
    rbox(m, "Seal", (0, 0.088, 0), (0.12, 0.008, 0.038), 0.003, 1)
    for i in range(7):
        a = i * 2.1
        sphere(m, "Bud", (math.cos(a) * 0.028, 0.04 + (i % 3) * 0.012, math.sin(a) * 0.006), 0.016, 8, 5, (1.0, 0.9, 0.8))
    for i in range(4):
        sphere(m, "Hair", (math.cos(i * 1.7) * 0.03, 0.05, -0.012), 0.004, 5, 4)
    rbox(m, "Label", (0, 0.045, -0.0182), (0.05, 0.026, 0.001), 0.0004, 1)
    save("WeedBag", m, {"Plastic": mat((0.85, 0.9, 0.85), ns=700, d=0.55), "Seal": mat((0.2, 0.55, 0.25), ns=300),
                        "Bud": MATS["Bud"], "Hair": MATS["Hair"], "Label": mat((0.95, 0.95, 0.9), ns=100)}, out)


if __name__ == "__main__":
    G.common_textures()
    plants(); bag(); treasury()
    for r in G.REPORT:
        print("%-12s tris=%5d verts=%5d dim=%s" % r)
    sys.exit(0)
