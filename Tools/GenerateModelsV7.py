#!/usr/bin/env python3
"""Modèles v5.5 : objets de métier restants + clés de concessionnaire (nouveau modèle CarKeys).
Usage : python3 Tools/GenerateModelsV7.py     (après GenerateModels.py)
Repère conservé : origine centrée en X/Z, base à y = 0, face avant vers -Z.
Modèles : PoliceRadio, ArmoryCase, WorkGloves, TrashBag, ShopCrate, WeedBag, CarKeys.
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
def label(text, size, bg, fg, border=None, pt=26):
    img = Image.new("RGB", size, bg)
    d = ImageDraw.Draw(img)
    if border:
        d.rectangle([3, 3, size[0] - 4, size[1] - 4], outline=border, width=3)
    f = T.font(pt)
    bb = d.textbbox((0, 0), text, font=f)
    d.text(((size[0] - (bb[2] - bb[0])) // 2, (size[1] - (bb[3] - bb[1])) // 2 - bb[1]), text, font=f, fill=fg)
    return img


def weed_label():
    img = Image.new("RGB", (128, 96), (245, 245, 238))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, 127, 22], fill=(40, 120, 60))
    d.text((8, 3), "RECOLTE", font=T.font(16), fill=(255, 255, 255))
    d.text((10, 36), "3,5 g", font=T.font(30), fill=(30, 60, 36))
    d.text((10, 74), "USAGE RP", font=T.font(14, "reg"), fill=(90, 90, 90))
    # petite feuille stylisée
    cx, cy = 98, 54
    for a in (-60, -30, 0, 30, 60, 90, 120, 150, 180, 210, 240):
        r = math.radians(a - 90)
        d.line([(cx, cy), (cx + 22 * math.cos(r), cy + 22 * math.sin(r))], fill=(40, 120, 60), width=3)
    return img


def textures():
    tex("Crate_Wood.png", T.wood(128, 128, (172, 128, 78), (112, 74, 40), 11))
    tex("Crate_Label.png", label("EPICERIE", (192, 64), (222, 214, 190), (30, 60, 36), (30, 60, 36), 30))
    tex("Armory_Label.png", label("ARMURERIE", (256, 64), (20, 22, 24), (230, 200, 110), (230, 200, 110), 30))
    tex("Weed_Label.png", weed_label())
    tex("Radio_Grille.png", T.radio_grille())
    tex("Police_Star.png", T.police_star())
    tex("Keypad.png", T.keypad_atlas())
    tex("Screen_Glow.png", T.screen_glow())


# ----------------------------------------------------------------------
# Radio de police : combiné portatif (antenne, grille haut-parleur, écran, clavier,
# bouton PTT latéral, bouton d'urgence, molette de canal, batterie, clip ceinture)
# ----------------------------------------------------------------------
def police_radio():
    m = Mesh()
    rbox(m, "Body", (0, 0.1, 0), (0.066, 0.13, 0.034), 0.008, 3, tile=2)
    rbox(m, "Battery", (0, 0.032, 0.001), (0.066, 0.064, 0.038), 0.008, 3, tile=2)
    rbox(m, "Seam", (0, 0.0655, 0), (0.0675, 0.0025, 0.0375), 0.001, 1)
    # haut-parleur (grille) et micro
    m.flat_quad("Grille", (-0.024, 0.128, -0.0172), (0.024, 0.128, -0.0172), (0.024, 0.15, -0.0172),
                (-0.024, 0.15, -0.0172), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    cylinder(m, "Dark", "Dark", (0.02, 0.1555, -0.0172), 'z', 0.0028, 0.001, 10)    # micro
    # écran + clavier
    rbox(m, "Bezel", (0, 0.1135, -0.0175), (0.042, 0.02, 0.0012), 0.003, 1)
    m.flat_quad("Screen", (-0.0185, 0.1055, -0.0182), (0.0185, 0.1055, -0.0182), (0.0185, 0.122, -0.0182),
                (-0.0185, 0.122, -0.0182), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    G.keypad_grid(m, (0, 0.082), 3, 3, 0.0165, (0.0135, 0.0135, 0.003), z=-0.0172)
    # batterie : badge et ergots de verrouillage
    m.flat_quad("Star", (-0.012, 0.016, -0.0192), (0.012, 0.016, -0.0192), (0.012, 0.044, -0.0192),
                (-0.012, 0.044, -0.0192), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    for x in (-0.026, 0.026):
        rbox(m, "Latch", (x, 0.0655, -0.0192), (0.008, 0.01, 0.003), 0.001, 1)
    # côté gauche : PTT (gros bouton) et deux touches latérales
    rbox(m, "PTT", (-0.0345, 0.105, -0.002), (0.004, 0.034, 0.02), 0.0015, 2)
    for y in (0.079, 0.068):
        rbox(m, "Side", (-0.0345, y, 0.0), (0.003, 0.008, 0.014), 0.001, 1)
    # dessus : antenne souple, molette de canal, bouton d'urgence, voyant
    cylinder(m, "Dark", "Dark", (0.017, 0.1665, 0.0), 'y', 0.0078, 0.004, 14)
    tube(m, "Antenna", (-0.016, 0.165, 0.002), (-0.0165, 0.236, 0.002), 0.0048, 10, True, 0.0024)
    cylinder(m, "Knob", "Knob", (0.017, 0.1695, 0.0), 'y', 0.0072, 0.007, 14, 0.001)
    rbox(m, "Emerg", (0.003, 0.1665, 0.007), (0.012, 0.004, 0.008), 0.0015, 1)
    sphere(m, "Led", (-0.004, 0.1665, -0.008), 0.0028, 8, 5)
    # dos : clip ceinture
    rbox(m, "Clip", (0, 0.07, 0.0215), (0.026, 0.1, 0.003), 0.001, 1)
    rbox(m, "Clip", (0, 0.022, 0.0235), (0.026, 0.012, 0.0045), 0.001, 1)
    save("PoliceRadio", m, {
        "Body": mat((0.07, 0.075, 0.09), ns=450, pm=0.2), "Battery": mat((0.05, 0.055, 0.065), ns=300),
        "Seam": mat((0.02, 0.02, 0.025), ns=200), "Grille": mat(ns=200, map_kd="Radio_Grille.png"),
        "Dark": mat((0.02, 0.02, 0.025), ns=300), "Bezel": mat((0.02, 0.02, 0.025), ns=700),
        "Screen": mat((0.1, 0.5, 0.8), ns=800, ke=(0.1, 0.45, 0.7), map_kd="Screen_Glow.png"),
        "Key": mat(ns=200, map_kd="Keypad.png"), "KeyBody": mat((0.75, 0.77, 0.8), ns=250),
        "Star": mat(ns=500, pm=0.3, map_kd="Police_Star.png"), "Latch": mat((0.6, 0.62, 0.66), ns=600, pm=0.9),
        "PTT": mat((0.8, 0.12, 0.1), ns=250), "Side": mat((0.15, 0.16, 0.18), ns=200),
        "Antenna": mat((0.03, 0.03, 0.035), ns=120), "Knob": mat((0.12, 0.12, 0.14), ns=250, pm=0.4),
        "Emerg": mat((0.95, 0.5, 0.05), ns=300), "Led": mat((0.1, 0.9, 0.3), ns=900, ke=(0.1, 0.9, 0.3)),
        "Clip": mat((0.18, 0.18, 0.2), ns=300, pm=0.6)}, OUT)


# ----------------------------------------------------------------------
# Mallette d'armes rigide : coque, couvercle à nervures, 3 fermoirs, poignée,
# valve de pressurisation, plaque « ARMURERIE », coins de protection
# ----------------------------------------------------------------------
def armory_case():
    m = Mesh()
    rbox(m, "Shell", (0, 0.045, 0), (0.363, 0.09, 0.23), 0.016, 3, tile=2)
    rbox(m, "Shell", (0, 0.1185, 0), (0.363, 0.055, 0.23), 0.016, 3, tile=2)
    rbox(m, "Seam", (0, 0.0905, 0), (0.366, 0.006, 0.233), 0.002, 1)
    for x in (-0.09, 0, 0.09):                                    # nervures d'empilage
        rbox(m, "Rib", (x, 0.1485, 0), (0.03, 0.004, 0.19), 0.0015, 1)
    for x in (-0.115, 0.0, 0.115):                                # fermoirs papillon (face avant)
        rbox(m, "Latch", (x, 0.082, -0.1195), (0.05, 0.036, 0.012), 0.004, 2)
        rbox(m, "LatchDark", (x, 0.083, -0.1262), (0.032, 0.014, 0.003), 0.002, 1)
    for x in (-0.115, 0.0, 0.115):                                # fermoirs arrière (charnières)
        rbox(m, "Hinge", (x, 0.09, 0.1195), (0.05, 0.03, 0.01), 0.003, 2)
    for x in (-0.07, 0.07):                                       # poignée supérieure
        rbox(m, "Handle", (x, 0.1465, 0.0), (0.02, 0.012, 0.032), 0.004, 2)
    rbox(m, "Grip", (0, 0.1515, 0), (0.16, 0.014, 0.03), 0.006, 2)
    cylinder(m, "Valve", "Valve", (0.15, 0.1485, -0.075), 'y', 0.009, 0.006, 12, 0.001)
    m.flat_quad("Plate", (-0.07, 0.108, -0.1167), (0.07, 0.108, -0.1167), (0.07, 0.128, -0.1167),
                (-0.07, 0.128, -0.1167), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    for sx in (-1, 1):                                            # coins renforcés
        for sz in (-1, 1):
            rbox(m, "Corner", (sx * 0.1745, 0.0165, sz * 0.1085), (0.02, 0.033, 0.02), 0.004, 2)
            rbox(m, "Corner", (sx * 0.1745, 0.1485, sz * 0.1085), (0.02, 0.033, 0.02), 0.004, 2)
    for x in (-0.1, 0.1):                                         # pieds
        rbox(m, "Corner", (x, 0.0015, -0.09), (0.05, 0.003, 0.025), 0.001, 1)
    save("ArmoryCase", m, {"Shell": mat((0.08, 0.085, 0.09), ns=350, pm=0.15),
                           "Seam": mat((0.02, 0.02, 0.025), ns=200), "Rib": mat((0.1, 0.105, 0.11), ns=300),
                           "Latch": mat((0.9, 0.55, 0.08), ns=300), "LatchDark": mat((0.05, 0.05, 0.06), ns=200),
                           "Hinge": mat((0.12, 0.12, 0.13), ns=300), "Handle": mat((0.12, 0.12, 0.13), ns=300),
                           "Grip": mat((0.9, 0.55, 0.08), ns=200), "Valve": mat((0.6, 0.62, 0.66), ns=700, pm=0.9),
                           "Plate": mat(ns=400, map_kd="Armory_Label.png"),
                           "Corner": mat((0.05, 0.05, 0.055), ns=150)}, OUT)


# ----------------------------------------------------------------------
# Gants de travail (paire à plat, dos vers le haut) : dos orange haute visibilité, paume grise,
# manchette élastique, bande réfléchissante, renforts articulaires, pouce articulé
# ----------------------------------------------------------------------
def glove(m, cx, cz, side):
    # paume / dos
    rbox(m, "Hi", (cx, 0.016, cz), (0.082, 0.024, 0.088), 0.01, 2, tile=1)
    rbox(m, "Palm", (cx, 0.0045, cz), (0.078, 0.009, 0.084), 0.004, 1, tile=1)
    # manchette
    rbox(m, "Cuff", (cx, 0.017, cz + 0.066), (0.088, 0.028, 0.05), 0.01, 2, tile=1)
    rbox(m, "Reflect", (cx, 0.0315, cz + 0.066), (0.07, 0.002, 0.012), 0.0006, 1)
    # doigts (index, majeur, annulaire, auriculaire) légèrement écartés
    lens = (0.07, 0.079, 0.072, 0.056)
    for i, ln in enumerate(lens):
        x0 = cx + side * (-0.03 + i * 0.02) * -1.0
        ang = (i - 1.5) * 0.07 * side * -1.0
        p0 = (x0, 0.016, cz - 0.04)
        p1 = (x0 + math.sin(ang) * ln, 0.0165, cz - 0.04 - math.cos(ang) * ln)
        tube(m, "Hi", p0, p1, 0.0092, 8, False, 0.0086)
        sphere(m, "Hi", p1, 0.0086, 8, 5)
        sphere(m, "Hi", p0, 0.0092, 8, 4)
        # renfort bout de doigt
        mid = (p1[0] * 0.85 + p0[0] * 0.15, 0.0245, p1[2] * 0.85 + p0[2] * 0.15)
        sphere(m, "Pad", mid, 0.0068, 6, 4, (1.0, 0.45, 1.0))
    # renforts d'articulations (jointures)
    rbox(m, "Pad", (cx, 0.0285, cz - 0.034), (0.07, 0.006, 0.014), 0.003, 1)
    # pouce
    t0 = (cx + side * 0.04, 0.014, cz - 0.006)
    t1 = (cx + side * 0.075, 0.015, cz - 0.04)
    tube(m, "Hi", t0, t1, 0.0098, 8, False, 0.0088)
    sphere(m, "Hi", t1, 0.0088, 8, 5)
    sphere(m, "Hi", t0, 0.0098, 8, 4)
    # languette de réglage
    rbox(m, "Pad", (cx + side * 0.012, 0.0325, cz + 0.044), (0.03, 0.003, 0.014), 0.001, 1)


def work_gloves():
    m = Mesh()
    glove(m, -0.057, -0.012, 1)
    glove(m, 0.057, -0.012, -1)
    save("WorkGloves", m, {"Hi": mat((0.95, 0.42, 0.06), ns=60), "Palm": mat((0.25, 0.26, 0.28), ns=100),
                           "Cuff": mat((0.14, 0.15, 0.17), ns=60), "Pad": mat((0.07, 0.07, 0.08), ns=140),
                           "Seam": mat((0.5, 0.2, 0.03), ns=40),
                           "Reflect": mat((0.85, 0.88, 0.75), ns=900, ke=(0.12, 0.13, 0.1))}, OUT)


# ----------------------------------------------------------------------
# Sac poubelle noué : corps bosselé (déchets), noeud torsadé, deux oreilles
# ----------------------------------------------------------------------
def trash_bag():
    m = Mesh()
    sphere(m, "Bag", (0, 0.105, 0), 0.075, 20, 12, (1.15, 1.18, 0.78))
    sphere(m, "Bag", (-0.032, 0.07, -0.006), 0.05, 14, 9, (1.0, 1.0, 0.85))
    sphere(m, "Bag", (0.036, 0.085, 0.006), 0.046, 14, 9, (1.0, 1.1, 0.82))
    sphere(m, "Bag", (0.004, 0.158, 0.0), 0.04, 14, 8, (1.0, 0.85, 0.75))
    # bosses de déchets sous le plastique
    for (x, y, z, r) in ((-0.052, 0.12, -0.034, 0.024), (0.045, 0.062, -0.03, 0.022), (0.0, 0.043, -0.04, 0.02),
                         (0.055, 0.126, -0.02, 0.018)):
        sphere(m, "Bag", (x, y, z), r, 8, 6)
    # fond plissé et noeud
    cylinder(m, "Bag", "Bag", (0, 0.004, 0), 'y', 0.052, 0.008, 14, 0.003)
    tube(m, "Knot", (0.0, 0.185, 0.0), (0.004, 0.216, 0.0), 0.011, 10, False, 0.0065)
    tube(m, "Knot", (0.004, 0.216, 0.0), (-0.002, 0.236, 0.002), 0.0065, 8, False, 0.0045)
    sphere(m, "Knot", (-0.002, 0.236, 0.002), 0.0048, 8, 5)
    for sx in (-1, 1):                                            # oreilles du noeud
        tube(m, "Knot", (0.0, 0.226, 0.0), (sx * 0.028, 0.248 - 0.012, 0.004 * sx), 0.0046, 6, False, 0.0016)
    save("TrashBag", m, {"Bag": mat((0.04, 0.045, 0.05), ns=850, pm=0.1),
                         "Knot": mat((0.035, 0.04, 0.045), ns=500)}, OUT)


# ----------------------------------------------------------------------
# Caisse d'épicerie en bois : fond, montants d'angle, lattes, poignées découpées, étiquette,
# produits (pommes, oranges) qui dépassent
# ----------------------------------------------------------------------
def shop_crate():
    m = Mesh()
    w, d, h = 0.32, 0.25, 0.22
    rbox(m, "Wood", (0, 0.01, 0), (w - 0.02, 0.02, d - 0.02), 0.002, 1, tile=2)
    for sx in (-1, 1):
        for sz in (-1, 1):
            rbox(m, "Post", (sx * (w / 2 - 0.0175), h / 2, sz * (d / 2 - 0.0175)), (0.035, h, 0.035), 0.002, 1, tile=2)
    for y in (0.045, 0.098, 0.151, 0.2):                          # lattes longues (avant/arrière)
        for sz in (-1, 1):
            rbox(m, "Wood", (0, y, sz * (d / 2 - 0.006)), (w - 0.07, 0.04, 0.012), 0.002, 1, tile=2)
    for y in (0.045, 0.098, 0.2):                                 # lattes courtes (côtés) : jour pour la poignée
        for sx in (-1, 1):
            rbox(m, "Wood", (sx * (w / 2 - 0.006), y, 0), (0.012, 0.04, d - 0.07), 0.002, 1, tile=2)
    for sx in (-1, 1):
        rbox(m, "Wood", (sx * (w / 2 - 0.006), 0.151, -0.075), (0.012, 0.04, 0.055), 0.002, 1, tile=2)
        rbox(m, "Wood", (sx * (w / 2 - 0.006), 0.151, 0.075), (0.012, 0.04, 0.055), 0.002, 1, tile=2)
    m.flat_quad("Label", (-0.05, 0.125, -d / 2 - 0.0005), (0.05, 0.125, -d / 2 - 0.0005),
                (0.05, 0.158, -d / 2 - 0.0005), (-0.05, 0.158, -d / 2 - 0.0005),
                (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    for sx in (-0.09, 0.0, 0.09):                                 # produits
        for sz in (-0.05, 0.05):
            sphere(m, "Apple" if (sx + sz) > 0 else "Orange", (sx, 0.205, sz), 0.036, 9, 6)
    for x, z in ((-0.045, 0.0), (0.045, 0.0)):
        sphere(m, "Apple", (x, 0.235, z), 0.03, 8, 6)
    for sx in (-1, 1):                                            # bandes de renfort (fer feuillard)
        rbox(m, "Strap", (sx * (w / 2 - 0.0005), 0.12, 0), (0.002, 0.012, d - 0.01), 0.0005, 1)
    save("ShopCrate", m, {"Wood": mat(ns=60, map_kd="Crate_Wood.png"), "Post": mat(ns=60, map_kd="Crate_Wood.png"),
                          "Label": mat(ns=100, map_kd="Crate_Label.png"),
                          "Apple": mat((0.75, 0.1, 0.08), ns=500), "Orange": mat((0.95, 0.5, 0.08), ns=400),
                          "Strap": mat((0.4, 0.42, 0.45), ns=500, pm=0.8)}, OUT)


# ----------------------------------------------------------------------
# Sachet de récolte : sachet zip transparent, têtes de cannabis visibles, bande zip, étiquette
# ----------------------------------------------------------------------
def weed_bag():
    m = Mesh()
    # film plastique transparent bombé
    rbox(m, "Film", (0, 0.046, 0), (0.12, 0.092, 0.018), 0.008, 2, tile=1)
    # soudures latérales et bande zip
    rbox(m, "Seal", (-0.0575, 0.046, 0), (0.005, 0.092, 0.003), 0.0008, 1)
    rbox(m, "Seal", (0.0575, 0.046, 0), (0.005, 0.092, 0.003), 0.0008, 1)
    rbox(m, "Zip", (0, 0.078, 0), (0.108, 0.006, 0.0125), 0.0015, 1)
    rbox(m, "ZipBlue", (0, 0.0782, -0.0066), (0.108, 0.0016, 0.0008), 0.0003, 1)
    rbox(m, "ZipYellow", (0, 0.0782, 0.0066), (0.108, 0.0016, 0.0008), 0.0003, 1)
    rbox(m, "Seal", (0, 0.09, 0), (0.12, 0.003, 0.003), 0.0008, 1)
    # étiquette
    m.flat_quad("Label", (-0.034, 0.012, -0.0094), (0.034, 0.012, -0.0094), (0.034, 0.05, -0.0094),
                (-0.034, 0.05, -0.0094), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    # têtes de cannabis à l'intérieur
    for (x, y, z, r, k) in ((-0.03, 0.024, 0.0, 0.018, "Bud"), (0.0, 0.03, 0.002, 0.021, "BudLight"),
                            (0.03, 0.022, 0.0, 0.017, "Bud"), (-0.012, 0.05, 0.001, 0.016, "BudLight"),
                            (0.022, 0.048, 0.0, 0.015, "Bud"), (-0.036, 0.048, 0.0, 0.013, "BudLight")):
        sphere(m, k, (x, y, z), r, 9, 6, (1.0, 1.1, 0.55))
        for i in range(4):
            a = i * 1.6
            p = (x + math.cos(a) * r * 0.6, y + r * 0.5, z + 0.002)
            tube(m, "Hair", p, (p[0] + math.cos(a) * 0.006, p[1] + 0.006, p[2]), 0.0011, 4, False, 0.0003)
    save("WeedBag", m, {"Film": mat((0.9, 0.95, 0.95), ns=900, d=0.28), "Seal": mat((0.85, 0.9, 0.9), ns=500, d=0.7),
                        "Zip": mat((0.9, 0.9, 0.92), ns=300), "ZipBlue": mat((0.15, 0.35, 0.85), ns=200),
                        "ZipYellow": mat((0.95, 0.8, 0.1), ns=200),
                        "Label": mat(ns=200, map_kd="Weed_Label.png"),
                        "Bud": mat((0.4, 0.58, 0.22), ns=150), "BudLight": mat((0.56, 0.74, 0.32), ns=150),
                        "Hair": mat((0.96, 0.52, 0.08), ns=120)}, OUT)


# ----------------------------------------------------------------------
# Clés de voiture du concessionnaire : télécommande, clé à lame, anneau, étiquette « VENDU »
# ----------------------------------------------------------------------
def car_keys():
    m = Mesh()
    # télécommande (coque noire, boutons verrouiller/déverrouiller/coffre, logo)
    rbox(m, "Fob", (0, 0.007, 0.02), (0.038, 0.014, 0.062), 0.007, 3, tile=1)
    rbox(m, "FobTop", (0, 0.0145, 0.02), (0.034, 0.002, 0.058), 0.005, 2, tile=1)
    rbox(m, "BtnLock", (-0.009, 0.0162, 0.03), (0.011, 0.002, 0.011), 0.004, 2)
    rbox(m, "BtnUnlock", (0.009, 0.0162, 0.03), (0.011, 0.002, 0.011), 0.004, 2)
    rbox(m, "BtnTrunk", (0, 0.0162, 0.011), (0.022, 0.002, 0.009), 0.0035, 2)
    cylinder(m, "Chrome", "Chrome", (0, 0.0162, 0.047), 'y', 0.0065, 0.0016, 14)
    sphere(m, "Led", (0, 0.0162, 0.0555), 0.0017, 6, 4)
    # clé mécanique (tête plastique + lame crantée)
    rbox(m, "KeyHead", (-0.032, 0.005, -0.028), (0.026, 0.01, 0.03), 0.005, 2, tile=1)
    rbox(m, "Blade", (-0.032, 0.005, -0.066), (0.0075, 0.004, 0.05), 0.0006, 1)
    for i in range(5):
        rbox(m, "Blade", (-0.032 + (0.003 if i % 2 else -0.003), 0.005, -0.082 + i * 0.0095),
             (0.0035, 0.004, 0.005), 0.0004, 1)
    # anneau + étiquette
    torus(m, "Chrome", (0.0, 0.004, -0.018), 'y', 0.015, 0.0016, 24, 6)
    rbox(m, "Tag", (0.034, 0.003, -0.034), (0.026, 0.006, 0.04), 0.003, 1, tile=1)
    cylinder(m, "Chrome", "Chrome", (0.034, 0.003, -0.05), 'y', 0.0038, 0.0065, 10)
    m.flat_quad("TagTxt", (0.021, 0.0065, -0.054), (0.047, 0.0065, -0.054), (0.047, 0.0065, -0.014),
                (0.021, 0.0065, -0.014), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 1, 0))
    save("CarKeys", m, {"Fob": mat((0.05, 0.05, 0.055), ns=800, pm=0.3), "FobTop": mat((0.08, 0.08, 0.09), ns=900, pm=0.5),
                        "BtnLock": mat((0.7, 0.72, 0.75), ns=300), "BtnUnlock": mat((0.7, 0.72, 0.75), ns=300),
                        "BtnTrunk": mat((0.7, 0.72, 0.75), ns=300), "Chrome": mat((0.85, 0.87, 0.9), ns=900, pm=1.0),
                        "Led": mat((0.9, 0.1, 0.1), ns=700, ke=(0.8, 0.05, 0.05)),
                        "KeyHead": mat((0.06, 0.06, 0.07), ns=300), "Blade": mat((0.75, 0.77, 0.8), ns=800, pm=1.0),
                        "Tag": mat((0.78, 0.1, 0.1), ns=200),
                        "TagTxt": mat(ns=200, map_kd="Keytag.png")}, OUT)


def main():
    textures()
    tex("Keytag.png", label("VENDU", (64, 192), (200, 25, 25), (255, 255, 255), (255, 255, 255), 24).rotate(90, expand=True))
    police_radio(); armory_case(); work_gloves(); trash_bag(); shop_crate(); weed_bag(); car_keys()
    for name, img in G.TEXTURES.items():
        img.save(os.path.join(G.MONEY, name), optimize=True)
    bad = 0
    for name, mesh in G.MESHES.items():
        for mt in mesh.order:
            for (a, b, c) in mesh.tris[mt]:
                if dot(cross(sub(b.p, a.p), sub(c.p, a.p)), add(add(a.n, b.n), c.n)) > 0:
                    bad += 1
    for row in G.REPORT:
        print("%-14s tris=%5d verts=%5d dim=%s" % row)
    print("triangles mal orientés :", bad)
    try:
        for n, mesh in G.MESHES.items():
            preview(mesh, "/tmp/v7_%s.png" % n, size=300)
    except Exception as exc:
        print("aperçu ignoré :", exc)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
