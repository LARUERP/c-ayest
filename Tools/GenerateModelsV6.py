#!/usr/bin/env python3
"""Modèles v5.3 : REFONTE des équipements de métier (mêmes noms de modèles : remplacement direct).
Usage : python3 Tools/GenerateModelsV6.py     (après GenerateModels.py ; Pillow + numpy)

Repère conservé : origine au centre en X/Z, base à y = 0, face avant vers -Z.
Modèles : PoliceCuffs, PoliceBadge, PoliceNotepad, TownBadge, TownFolder, MedicalKit,
Defibrillator, BankTerminal, ShopScanner, TrashGrabber, ArmoryCatalog, BusinessTablet.
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
def terminal_ui():
    W, H = 256, 384
    img = Image.new("RGB", (W, H), (10, 18, 30))
    d = ImageDraw.Draw(img, "RGBA")
    d.rectangle([0, 0, W, 46], fill=(24, 90, 70))
    d.text((12, 10), "PAIEMENT", font=T.font(24), fill=(255, 255, 255))
    d.text((18, 80), "MONTANT", font=T.font(20, "reg"), fill=(150, 180, 210))
    d.text((18, 112), "12,50 EUR", font=T.font(44), fill=(255, 255, 255))
    d.rounded_rectangle([18, 190, W - 18, 246], radius=10, fill=(30, 60, 100, 255))
    d.text((34, 206), "INSEREZ OU APPROCHEZ", font=T.font(17), fill=(230, 240, 255))
    d.rounded_rectangle([18, 280, W - 18, 330], radius=10, fill=(26, 140, 80, 255))
    d.text((70, 292), "VALIDER", font=T.font(26), fill=(255, 255, 255))
    return img


def nfc_icon():
    img = Image.new("RGB", (64, 64), (24, 26, 30))
    d = ImageDraw.Draw(img)
    for r in (10, 20, 30):
        d.arc([8 - r // 2, 32 - r, 8 + r, 32 + r], -50, 50, fill=(220, 224, 232), width=4)
    return img


def catalog_cover():
    W, H = 256, 340
    img = Image.new("RGB", (W, H), (24, 30, 26))
    d = ImageDraw.Draw(img, "RGBA")
    gold = (214, 178, 80, 255)
    d.rounded_rectangle([10, 10, W - 11, H - 11], radius=10, outline=gold, width=4)
    d.rounded_rectangle([20, 20, W - 21, H - 21], radius=6, outline=(214, 178, 80, 110), width=2)
    cx, cy = W // 2, 120
    d.ellipse([cx - 46, cy - 46, cx + 46, cy + 46], outline=gold, width=5)
    d.line([(cx - 62, cy), (cx + 62, cy)], fill=gold, width=4)
    d.line([(cx, cy - 62), (cx, cy + 62)], fill=gold, width=4)
    d.ellipse([cx - 5, cy - 5, cx + 5, cy + 5], fill=gold)
    d.text((34, 214), "CATALOGUE", font=T.font(34), fill=gold)
    d.text((44, 258), "ARMURERIE", font=T.font(34), fill=gold)
    d.text((70, 306), "USAGE RP", font=T.font(16, "reg"), fill=(200, 200, 190, 255))
    return img


def textures():
    tex("Terminal_UI.png", terminal_ui())
    tex("Nfc_Icon.png", nfc_icon())
    tex("Catalog_Cover.png", catalog_cover())
    tex("Folder_Cover.png", T.fabric(128, 128, (52, 86, 140), 21))
    tex("Leather.png", T.leather(128, 128))
    tex("Carbon.png", T.carbon(128, 128))
    tex("Paper_Edge.png", T.paper_edge())
    tex("Medical_Cross.png", T.medical_texture())
    tex("Defib.png", T.defib_texture())
    tex("Police_Star.png", T.police_star())
    tex("Town_Emblem.png", T.town_emblem())
    tex("Tablet_UI.png", T.tablet_ui())
    tex("Screen_Glow.png", T.screen_glow())
    tex("Keypad.png", T.keypad_atlas())


# ----------------------------------------------------------------------
# Menottes : deux bracelets à crémaillère (arceau + boîtier de verrouillage à double
# verrou + trou de clé), reliés par une chaîne de quatre maillons croisés.
# ----------------------------------------------------------------------
def cuff(m, cx, side):
    cy, major, minor = 0.031, 0.0245, 0.0042
    torus(m, "Steel", (cx, cy, 0), 'z', major, minor, 28, 10)
    # crémaillère sur l'intérieur de l'arceau (face opposée à la chaîne)
    for i in range(11):
        a = math.radians(-70 + i * 14) + (0 if side < 0 else math.pi)
        d = (math.cos(a), math.sin(a))
        r0, r1 = major - minor * 0.8, major - minor * 0.8 - 0.0032
        p0 = (cx + d[0] * r0, cy + d[1] * r0, 0.0)
        p1 = (cx + d[0] * r1, cy + d[1] * r1, 0.0)
        tube(m, "Lock", p0, p1, 0.0021, 5, True, 0.001)
    # boîtier de verrouillage côté chaîne
    hx = cx - side * (major + 0.002)
    rbox(m, "Steel", (hx, cy, 0), (0.026, 0.034, 0.0165), 0.006, 2, tile=1)
    rbox(m, "Lock", (hx, cy - 0.001, -0.0087), (0.019, 0.026, 0.0016), 0.003, 1, tile=1)
    cylinder(m, "Dark", "Dark", (hx, cy + 0.006, -0.0097), 'z', 0.0034, 0.0012, 14)
    rbox(m, "Dark", (hx, cy + 0.0016, -0.0097), (0.0016, 0.0075, 0.0012), 0.0005, 1)
    cylinder(m, "Dark", "Dark", (hx, cy - 0.008, -0.0097), 'z', 0.0019, 0.0012, 10)   # trou du double verrou
    for dy in (0.012, -0.012):
        cylinder(m, "Rivet", "Rivet", (hx + side * 0.0085, cy + dy, -0.0094), 'z', 0.0014, 0.0014, 8)
    # axe de pivot + oeillet de chaîne
    tube(m, "Steel", (hx - side * 0.012, cy, 0), (hx - side * 0.0185, cy, 0), 0.0032, 8, True)
    return hx - side * 0.0185


def handcuffs():
    m = Mesh()
    ends = []
    for side in (-1, 1):                       # -1 = bracelet gauche
        ends.append(cuff(m, side * 0.0585, side))
    cy = 0.031
    xs = (-0.0185, -0.0062, 0.0062, 0.0185)
    for i, x in enumerate(xs):
        axis = 'z' if i % 2 == 0 else 'y'
        torus(m, "Steel", (x, cy, 0), axis, 0.0072, 0.0018, 14, 6)
    # maillons de jonction avec les boîtiers
    for end, link in ((ends[0], xs[0]), (ends[1], xs[-1])):
        tube(m, "Steel", (end, cy, 0), (link, cy, 0), 0.0022, 6, True)
    save("PoliceCuffs", m, {"Steel": mat((0.82, 0.84, 0.88), ns=900, pm=1.0),
                            "Lock": mat((0.16, 0.17, 0.19), ns=500, pm=0.8),
                            "Dark": mat((0.02, 0.02, 0.025), ns=200),
                            "Rivet": mat((0.6, 0.62, 0.66), ns=800, pm=1.0)}, OUT)


# ----------------------------------------------------------------------
# Insignes
# ----------------------------------------------------------------------
def shield_points(w, h, n=9):
    half = w / 2.0
    pts = []
    for i in range(n + 1):
        t = i / n
        pts.append((-half * (1.0 - t ** 2.3), h / 2.0 - t * h))
    for i in range(n - 1, -1, -1):
        t = i / n
        pts.append((half * (1.0 - t ** 2.3), h / 2.0 - t * h))
    pts.append((0.0, h / 2.0 - 0.009))          # creux du bandeau supérieur
    return pts


def scaled(pts, k):
    cx = sum(p[0] for p in pts) / len(pts)
    cy = sum(p[1] for p in pts) / len(pts)
    return [(cx + (p[0] - cx) * k, cy + (p[1] - cy) * k) for p in pts]


def police_badge():
    m = Mesh()
    w, h = 0.082, 0.098
    outer = shield_points(w, h)
    polygon_prism(m, "Rim", "Back", "Rim", outer, 0.0055, (0, h / 2 + 0.002, 0.0))
    polygon_prism(m, "Rim", "Rim", "Rim", scaled(outer, 0.93), 0.0066, (0, h / 2 + 0.002, 0.0))
    polygon_prism(m, "Front", "Rim", "Rim", scaled(outer, 0.80), 0.0078, (0, h / 2 + 0.002, 0.0))
    torus(m, "Rim", (0, h + 0.010, 0), 'z', 0.0065, 0.0017, 14, 6)
    tube(m, "Pin", (-0.032, 0.05, 0.0042), (0.032, 0.05, 0.0042), 0.0013, 6, True)
    rbox(m, "Pin", (0.0, 0.05, 0.0036), (0.012, 0.007, 0.0016), 0.002, 1)
    save("PoliceBadge", m, {"Rim": mat((0.78, 0.64, 0.24), ns=900, pm=1.0),
                            "Back": mat((0.5, 0.42, 0.18), ns=300, pm=0.6),
                            "Front": mat(ns=700, pm=0.5, map_kd="Police_Star.png"),
                            "Pin": mat((0.7, 0.72, 0.76), ns=700, pm=1.0)}, OUT)


def town_badge():
    m = Mesh()
    r = 0.034
    pts = [(r * math.cos(2 * math.pi * i / 28), r * math.sin(2 * math.pi * i / 28)) for i in range(28)]
    c = (0, 0.037, 0)
    polygon_prism(m, "Rim", "Back", "Rim", pts, 0.0052, c)
    polygon_prism(m, "Rim", "Rim", "Rim", scaled(pts, 0.9), 0.0064, c)
    polygon_prism(m, "Face", "Rim", "Rim", scaled(pts, 0.8), 0.0074, c)
    # ruban tricolore avec noeud
    for i, col in enumerate(("Blue", "White", "Red")):
        rbox(m, col, ((i - 1) * 0.0105, 0.088, 0.0004), (0.0105, 0.026, 0.0012), 0.0004, 1)
    rbox(m, "Rim", (0, 0.1, 0.0), (0.04, 0.007, 0.004), 0.002, 1)
    rbox(m, "Rim", (0, 0.073, 0.0), (0.016, 0.006, 0.0042), 0.002, 1)
    save("TownBadge", m, {"Rim": mat((0.82, 0.68, 0.26), ns=900, pm=1.0),
                          "Back": mat((0.45, 0.38, 0.16), ns=300, pm=0.6),
                          "Face": mat(ns=700, pm=0.4, map_kd="Town_Emblem.png"),
                          "Blue": mat((0.08, 0.2, 0.62), ns=60), "White": mat((0.94, 0.94, 0.95), ns=60),
                          "Red": mat((0.78, 0.1, 0.12), ns=60)}, OUT)


# ----------------------------------------------------------------------
# Papeterie : carnet de procès-verbal, dossier administratif, catalogue d'armurerie
# ----------------------------------------------------------------------
def notepad():
    m = Mesh()
    rbox(m, "Leather", (0, 0.0765, 0.0065), (0.1, 0.153, 0.003), 0.003, 2, tile=2)
    rbox(m, "Paper", (0, 0.0745, 0.0005), (0.092, 0.145, 0.0095), 0.0008, 1, tile=2)
    rbox(m, "Leather", (0, 0.0765, -0.0055), (0.1, 0.153, 0.003), 0.003, 2, tile=2)
    m.flat_quad("Star", (-0.018, 0.058, -0.00705), (0.018, 0.058, -0.00705), (0.018, 0.094, -0.00705),
                (-0.018, 0.094, -0.00705), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    for i in range(9):                                       # reliure à spirale
        torus(m, "Ring", (-0.04 + i * 0.01, 0.1535, 0.0005), 'x', 0.0078, 0.0008, 12, 5)
    rbox(m, "Strap", (0.032, 0.0765, -0.00725), (0.011, 0.155, 0.0007), 0.0002, 1)
    tube(m, "Pen", (0.0575, 0.012, -0.0045), (0.0575, 0.138, -0.0045), 0.0036, 8, True)
    tube(m, "PenTip", (0.0575, 0.138, -0.0045), (0.0575, 0.15, -0.0045), 0.0036, 8, True, 0.0012)
    rbox(m, "Clip", (0.0575, 0.118, -0.0083), (0.0022, 0.05, 0.0014), 0.0005, 1)
    save("PoliceNotepad", m, {"Leather": mat(ns=180, map_kd="Leather.png"),
                              "Paper": mat((0.96, 0.95, 0.9), ns=40, map_kd="Paper_Edge.png"),
                              "Star": mat(ns=600, pm=0.4, map_kd="Police_Star.png"),
                              "Ring": mat((0.75, 0.77, 0.8), ns=800, pm=1.0),
                              "Strap": mat((0.04, 0.04, 0.05), ns=60),
                              "Pen": mat((0.06, 0.12, 0.45), ns=700, pm=0.3),
                              "PenTip": mat((0.8, 0.82, 0.85), ns=800, pm=1.0),
                              "Clip": mat((0.8, 0.82, 0.85), ns=800, pm=1.0)}, OUT)


def folder():
    m = Mesh()
    rbox(m, "Cover", (0, 0.159, 0.0105), (0.23, 0.318, 0.0045), 0.004, 2, tile=2)
    rbox(m, "Cover", (0, 0.159, -0.0105), (0.23, 0.318, 0.0045), 0.004, 2, tile=2)
    rbox(m, "Cover", (-0.1135, 0.159, 0.0), (0.0035, 0.318, 0.026), 0.002, 1, tile=2)
    for i, (dx, dy, hgt) in enumerate(((0.0, 0.0, 0.3), (0.004, 0.003, 0.294), (-0.003, 0.002, 0.296))):
        rbox(m, "Paper", (dx, 0.15 + dy, 0.0), (0.212, hgt, 0.0125), 0.0008, 1, tile=2)
    rbox(m, "Tab", (0.075, 0.322, -0.006), (0.06, 0.016, 0.0045), 0.003, 2, tile=2)
    rbox(m, "Label", (0.0, 0.215, -0.0131), (0.1, 0.06, 0.0007), 0.0004, 1)
    m.flat_quad("Emblem", (-0.025, 0.19, -0.01375), (0.025, 0.19, -0.01375), (0.025, 0.24, -0.01375),
                (-0.025, 0.24, -0.01375), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    for y in (0.07, 0.25):                                   # sangles élastiques
        rbox(m, "Strap", (0.09, y, -0.0133), (0.012, 0.05, 0.0009), 0.0003, 1)
        rbox(m, "Strap", (0.09, y, 0.0133), (0.012, 0.05, 0.0009), 0.0003, 1)
    save("TownFolder", m, {"Cover": mat(ns=120, map_kd="Folder_Cover.png"),
                           "Paper": mat((0.96, 0.95, 0.9), ns=40, map_kd="Paper_Edge.png"),
                           "Tab": mat((0.8, 0.66, 0.24), ns=300),
                           "Label": mat((0.95, 0.95, 0.92), ns=60),
                           "Emblem": mat(ns=400, map_kd="Town_Emblem.png"),
                           "Strap": mat((0.06, 0.06, 0.07), ns=60)}, OUT)


def catalog():
    m = Mesh()
    mats = {"-z": "Cover", "+z": "Back", "+x": "Back", "-x": "Back", "+y": "Back", "-y": "Back"}
    rbox(m, mats, (0, 0.14, -0.0125), (0.213, 0.28, 0.0045), 0.004, 2, uv=face_uv())
    rbox(m, "Back", (0, 0.14, 0.0125), (0.213, 0.28, 0.0045), 0.004, 2, tile=2)
    rbox(m, "Back", (-0.1035, 0.14, 0.0), (0.0055, 0.28, 0.03), 0.003, 1, tile=2)
    rbox(m, "Paper", (0.004, 0.14, 0.0), (0.198, 0.268, 0.0195), 0.0008, 1, tile=2)
    for i, y in enumerate((0.07, 0.14, 0.21)):               # anneaux métalliques
        torus(m, "Ring", (-0.0975, y, 0.0), 'x', 0.0105, 0.0014, 16, 6)
    for i, col in enumerate(("TabA", "TabB", "TabC")):       # intercalaires colorés
        rbox(m, col, (0.1055, 0.215 - i * 0.07, -0.002 + i * 0.002), (0.014, 0.05, 0.004), 0.0015, 1)
    save("ArmoryCatalog", m, {"Cover": mat(ns=300, map_kd="Catalog_Cover.png"),
                              "Back": mat((0.09, 0.11, 0.09), ns=200),
                              "Paper": mat((0.96, 0.95, 0.9), ns=40, map_kd="Paper_Edge.png"),
                              "Ring": mat((0.78, 0.8, 0.84), ns=800, pm=1.0),
                              "TabA": mat((0.8, 0.2, 0.15), ns=100), "TabB": mat((0.85, 0.7, 0.15), ns=100),
                              "TabC": mat((0.15, 0.45, 0.8), ns=100)}, OUT)


# ----------------------------------------------------------------------
# Médical : trousse de secours rigide, défibrillateur
# ----------------------------------------------------------------------
def medical_kit():
    m = Mesh()
    rbox(m, "Body", (0, 0.0475, 0), (0.25, 0.095, 0.12), 0.012, 3, tile=2)
    rbox(m, "Body", (0, 0.1175, 0), (0.25, 0.045, 0.12), 0.012, 3, tile=2)
    rbox(m, "Seam", (0, 0.0955, 0), (0.2535, 0.007, 0.1235), 0.002, 1)
    for z, nz in ((-0.0603, -1), (0.0603, 1)):                # plaque croix rouge sur les deux grandes faces
        rbox(m, "Plate", (0, 0.066, z), (0.11, 0.092, 0.0022), 0.004, 2)
        m.flat_quad("Cross", (-0.04, 0.026, z + nz * 0.0012), (0.04, 0.026, z + nz * 0.0012),
                    (0.04, 0.106, z + nz * 0.0012), (-0.04, 0.106, z + nz * 0.0012),
                    (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, nz))
    for z, nz in ((-0.0607, -1), (0.0607, 1)):                # fermoirs métalliques
        for x in (-0.085, 0.085):
            rbox(m, "Metal", (x, 0.094, z), (0.032, 0.03, 0.006), 0.003, 2)
            rbox(m, "Dark", (x, 0.094, z + nz * 0.0032), (0.016, 0.014, 0.0018), 0.002, 1)
    # poignée : deux montants + prise caoutchouc
    for x in (-0.052, 0.052):
        rbox(m, "Metal", (x, 0.1465, 0), (0.014, 0.016, 0.026), 0.003, 2)
    rbox(m, "Grip", (0, 0.1565, 0), (0.12, 0.014, 0.026), 0.006, 2)
    for sx in (-1, 1):                                        # coins de protection
        for sz in (-1, 1):
            rbox(m, "Corner", (sx * 0.1195, 0.0225, sz * 0.0545), (0.022, 0.044, 0.022), 0.005, 2)
            rbox(m, "Corner", (sx * 0.1195, 0.1375, sz * 0.0545), (0.022, 0.044, 0.022), 0.005, 2)
    for x in (-0.1, 0.1):                                     # bandes réfléchissantes
        rbox(m, "Reflect", (x, 0.1175, -0.0606), (0.02, 0.03, 0.0008), 0.0003, 1)
    save("MedicalKit", m, {"Body": mat((0.76, 0.08, 0.1), ns=700, pm=0.2),
                           "Seam": mat((0.04, 0.04, 0.045), ns=300),
                           "Plate": mat((0.95, 0.95, 0.96), ns=500),
                           "Cross": mat(ns=500, map_kd="Medical_Cross.png"),
                           "Metal": mat((0.8, 0.82, 0.86), ns=850, pm=1.0),
                           "Dark": mat((0.05, 0.05, 0.06), ns=300),
                           "Grip": mat((0.04, 0.04, 0.05), ns=80),
                           "Corner": mat((0.09, 0.09, 0.1), ns=120),
                           "Reflect": mat((0.9, 0.92, 0.7), ns=900, ke=(0.15, 0.16, 0.1))}, OUT)


def defibrillator():
    m = Mesh()
    mats = {"-z": "Front", "+z": "Body", "+x": "Body", "-x": "Body", "+y": "Body", "-y": "Body"}
    rbox(m, mats, (0, 0.077, 0.0), (0.21, 0.15, 0.07), 0.016, 3, uv=face_uv())
    rbox(m, "Handle", (0, 0.168, 0), (0.1, 0.026, 0.022), 0.008, 2, tile=2)
    for x in (-0.042, 0.042):
        rbox(m, "Handle", (x, 0.153, 0), (0.012, 0.02, 0.02), 0.004, 2, tile=2)
    # écran, boutons, haut-parleur, voyant d'état
    m.flat_quad("Screen", (-0.036, 0.098, -0.0356), (0.036, 0.098, -0.0356), (0.036, 0.136, -0.0356),
                (-0.036, 0.136, -0.0356), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    rbox(m, "Bezel", (0, 0.117, -0.0352), (0.082, 0.046, 0.0016), 0.004, 1)
    cylinder(m, "ShockBtn", "ShockBtn", (0.058, 0.083, -0.0385), 'z', 0.0155, 0.007, 18, 0.0015)
    cylinder(m, "OnBtn", "OnBtn", (-0.058, 0.083, -0.0375), 'z', 0.0105, 0.005, 16, 0.001)
    for i in range(5):
        rbox(m, "Dark", (0, 0.078 - i * 0.0055, -0.0362), (0.05, 0.0022, 0.002), 0.0008, 1)
    sphere(m, "Led", (-0.058, 0.108, -0.0365), 0.0035, 8, 5)
    # poche d'électrodes (deux plaques adhésives) + câble vers la prise latérale
    rbox(m, "Pocket", (0, 0.032, -0.0405), (0.15, 0.04, 0.012), 0.005, 2)
    for x in (-0.034, 0.034):
        rbox(m, "Pad", (x, 0.038, -0.0478), (0.056, 0.032, 0.0035), 0.004, 2)
    pts = [(0.095, 0.05, -0.012), (0.112, 0.045, -0.026), (0.108, 0.024, -0.045), (0.085, 0.012, -0.062),
           (0.05, 0.016, -0.054), (0.036, 0.034, -0.05)]
    for a, b in zip(pts[:-1], pts[1:]):
        tube(m, "Cable", a, b, 0.0036, 6, True)
        sphere(m, "Cable", b, 0.0036, 6, 4)
    rbox(m, "Handle", (0.0985, 0.05, -0.002), (0.012, 0.022, 0.026), 0.003, 1)
    save("Defibrillator", m, {"Front": mat(ns=520, map_kd="Defib.png"), "Body": mat((0.93, 0.9, 0.82), ns=520),
                              "Handle": mat((0.1, 0.1, 0.12), ns=500), "Bezel": mat((0.04, 0.04, 0.05), ns=500),
                              "Screen": mat((0.1, 0.6, 0.3), ns=800, ke=(0.1, 0.7, 0.3), map_kd="Screen_Glow.png"),
                              "ShockBtn": mat((0.95, 0.5, 0.05), ns=700, ke=(0.55, 0.22, 0.0)),
                              "OnBtn": mat((0.1, 0.7, 0.25), ns=700, ke=(0.04, 0.4, 0.1)),
                              "Led": mat((0.1, 0.9, 0.3), ns=900, ke=(0.1, 0.9, 0.3)),
                              "Dark": mat((0.05, 0.05, 0.06), ns=200),
                              "Pocket": mat((0.15, 0.15, 0.17), ns=300),
                              "Pad": mat((0.25, 0.5, 0.85), ns=300),
                              "Cable": mat((0.03, 0.03, 0.035), ns=300)}, OUT)


# ----------------------------------------------------------------------
# Banque, commerce, voirie
# ----------------------------------------------------------------------
def bank_terminal():
    m = Mesh()
    rbox(m, "Body", (0, 0.092, 0), (0.088, 0.182, 0.034), 0.012, 3, tile=2)
    rbox(m, "Foot", (0, 0.004, 0.0), (0.07, 0.008, 0.03), 0.004, 2)
    rbox(m, "Bezel", (0, 0.138, -0.0166), (0.074, 0.062, 0.0016), 0.004, 1)
    m.flat_quad("Screen", (-0.032, 0.11, -0.0176), (0.032, 0.11, -0.0176), (0.032, 0.167, -0.0176),
                (-0.032, 0.167, -0.0176), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    G.keypad_grid(m, (0, 0.062), 3, 4, 0.0215, (0.0175, 0.0175, 0.004), z=-0.0172)
    rbox(m, "Slot", (0, 0.1825, -0.002), (0.05, 0.003, 0.016), 0.001, 1)          # fente de carte
    m.flat_quad("Nfc", (-0.011, 0.171, -0.0176), (0.011, 0.171, -0.0176), (0.011, 0.1795, -0.0176),
                (-0.011, 0.1795, -0.0176), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    rbox(m, "Green", (0.026, 0.026, -0.0173), (0.016, 0.009, 0.002), 0.002, 1)
    rbox(m, "Red", (-0.026, 0.026, -0.0173), (0.016, 0.009, 0.002), 0.002, 1)
    save("BankTerminal", m, {"Body": mat((0.1, 0.11, 0.13), ns=600, pm=0.5),
                             "Foot": mat((0.05, 0.05, 0.06), ns=200),
                             "Bezel": mat((0.02, 0.02, 0.025), ns=600),
                             "Screen": mat(ns=900, ke=(0.35, 0.45, 0.6), map_kd="Terminal_UI.png"),
                             "Key": mat(ns=250, map_kd="Keypad.png"), "KeyBody": mat((0.78, 0.8, 0.84), ns=250),
                             "Slot": mat((0.01, 0.01, 0.012), ns=100),
                             "Nfc": mat(ns=300, map_kd="Nfc_Icon.png"),
                             "Green": mat((0.15, 0.7, 0.3), ns=300), "Red": mat((0.75, 0.15, 0.15), ns=300)}, OUT)


def shop_scanner():
    m = Mesh()
    rbox(m, "Body", (0, 0.146, -0.0275), (0.062, 0.05, 0.085), 0.014, 3, tile=2)
    rbox(m, "Window", (0, 0.146, -0.0701), (0.052, 0.03, 0.0016), 0.004, 1)
    m.flat_quad("Beam", (-0.024, 0.133, -0.0711), (0.024, 0.133, -0.0711), (0.024, 0.159, -0.0711),
                (-0.024, 0.159, -0.0711), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    tube(m, "Grip", (0, 0.138, -0.004), (0, 0.012, 0.02), 0.0205, 14, True, 0.0175)
    rbox(m, "Trigger", (0, 0.098, -0.0155), (0.012, 0.03, 0.01), 0.004, 2)
    for i in range(4):
        rbox(m, "Dark", (0, 0.1715, -0.03 + i * 0.012), (0.034, 0.0012, 0.004), 0.0004, 1)
    sphere(m, "Led", (0.018, 0.1715, 0.0), 0.0035, 8, 5)
    tube(m, "Cable", (0, 0.004, 0.022), (0, 0.004, 0.042), 0.004, 8, True)
    save("ShopScanner", m, {"Body": mat((0.12, 0.13, 0.15), ns=600, pm=0.3), "Window": mat((0.03, 0.03, 0.04), ns=900),
                            "Beam": mat((0.9, 0.05, 0.05), ns=800, ke=(0.9, 0.05, 0.05), d=0.92),
                            "Grip": mat((0.06, 0.06, 0.07), ns=120), "Trigger": mat((0.85, 0.5, 0.1), ns=400),
                            "Dark": mat((0.02, 0.02, 0.025), ns=200), "Led": mat((0.1, 0.9, 0.3), ns=900, ke=(0.1, 0.9, 0.3)),
                            "Cable": mat((0.03, 0.03, 0.035), ns=250)}, OUT)


def trash_grabber():
    m = Mesh()
    # poignée pistolet, détente, tige alu, tête à deux mâchoires caoutchoutées
    rbox(m, "Grip", (0, 0.045, 0.004), (0.03, 0.09, 0.027), 0.01, 3, tile=2)
    rbox(m, "Grip", (0, 0.098, 0.0), (0.034, 0.03, 0.034), 0.012, 3, tile=2)
    rbox(m, "Trigger", (0, 0.074, -0.0185), (0.012, 0.045, 0.01), 0.004, 2)
    tube(m, "Shaft", (0, 0.11, 0), (0, 0.43, 0), 0.0085, 14, True)
    for y in (0.17, 0.25, 0.33):
        tube(m, "Band", (0, y, 0), (0, y + 0.012, 0), 0.0092, 14, True)
    tube(m, "Cable", (0.0, 0.1, -0.0098), (0.0, 0.43, -0.0098), 0.0016, 5, True)
    rbox(m, "Head", (0, 0.442, 0), (0.026, 0.036, 0.022), 0.006, 2)
    rbox(m, "JawA", (-0.0105, 0.478, 0.0), (0.008, 0.052, 0.018), 0.003, 2)
    rbox(m, "JawB", (0.0125, 0.474, 0.0), (0.008, 0.048, 0.018), 0.003, 2)
    for x, y in ((-0.0105, 0.5), (0.0125, 0.4975)):
        rbox(m, "Rubber", (x, y, 0.0), (0.0095, 0.012, 0.02), 0.003, 2)
    cylinder(m, "Pin", "Pin", (0, 0.452, -0.0124), 'z', 0.004, 0.0024, 10)
    save("TrashGrabber", m, {"Grip": mat((0.1, 0.35, 0.16), ns=200), "Trigger": mat((0.9, 0.7, 0.1), ns=400),
                             "Shaft": mat((0.78, 0.8, 0.84), ns=800, pm=1.0), "Band": mat((0.9, 0.7, 0.1), ns=300),
                             "Cable": mat((0.04, 0.04, 0.045), ns=200), "Head": mat((0.1, 0.35, 0.16), ns=250),
                             "JawA": mat((0.9, 0.7, 0.1), ns=300), "JawB": mat((0.9, 0.7, 0.1), ns=300),
                             "Rubber": mat((0.04, 0.04, 0.045), ns=80), "Pin": mat((0.8, 0.82, 0.86), ns=700, pm=1.0)}, OUT)


def business_tablet():
    m = Mesh()
    rbox(m, "Body", (0, 0.1355, 0), (0.19, 0.271, 0.012), 0.012, 3, tile=2)
    rbox(m, "Bezel", (0, 0.1355, -0.0058), (0.176, 0.257, 0.0016), 0.01, 2)
    m.flat_quad("Screen", (-0.0805, 0.0235, -0.00675), (0.0805, 0.0235, -0.00675), (0.0805, 0.2475, -0.00675),
                (-0.0805, 0.2475, -0.00675), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    for sx in (-1, 1):                                       # coques de protection aux angles
        for y in (0.026, 0.245):
            rbox(m, "Case", (sx * 0.0825, y, 0.0), (0.03, 0.03, 0.0148), 0.008, 2)
    cylinder(m, "Cam", "Cam", (0, 0.2615, -0.0062), 'z', 0.0028, 0.0016, 12)
    rbox(m, "Btn", (0.0955, 0.19, 0.0), (0.0025, 0.03, 0.006), 0.001, 1)
    rbox(m, "Btn", (0.0955, 0.15, 0.0), (0.0025, 0.016, 0.006), 0.001, 1)
    save("BusinessTablet", m, {"Body": mat((0.1, 0.11, 0.13), ns=650, pm=0.8), "Bezel": mat((0.02, 0.02, 0.025), ns=800),
                               "Screen": mat(ns=900, ke=(0.35, 0.45, 0.6), map_kd="Tablet_UI.png"),
                               "Case": mat((0.05, 0.06, 0.07), ns=100), "Cam": mat((0.01, 0.01, 0.02), ns=900),
                               "Btn": mat((0.55, 0.57, 0.6), ns=700, pm=1.0)}, OUT)


def main():
    textures()
    handcuffs(); police_badge(); town_badge(); notepad(); folder(); catalog()
    medical_kit(); defibrillator(); bank_terminal(); shop_scanner(); trash_grabber(); business_tablet()
    for name, img in G.TEXTURES.items():
        img.save(os.path.join(G.MONEY, name), optimize=True)
    bad = 0
    for name, mesh in G.MESHES.items():
        for mt in mesh.order:
            for (a, b, c) in mesh.tris[mt]:
                if dot(cross(sub(b.p, a.p), sub(c.p, a.p)), add(add(a.n, b.n), c.n)) > 0:
                    bad += 1
    for row in G.REPORT:
        print("%-16s tris=%5d verts=%5d dim=%s" % row)
    print("triangles mal orientés :", bad)
    try:
        for n, mesh in G.MESHES.items():
            preview(mesh, "/tmp/new_%s.png" % n, size=300)
    except Exception as exc:
        print("aperçu ignoré :", exc)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
