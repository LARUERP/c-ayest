#!/usr/bin/env python3
"""Modèles v4 : radio de police réaliste, menottes réalistes, tas (caca) de mission,
balise de mission, imprimante à billets.  Usage : python3 Tools/GenerateModelsV2.py"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import GenerateModels as G
from modellib import *      # noqa

out = G.RPDIR
mat, save, tex, T = G.mat, G.save, G.tex, G.T


def radio():
    grille = tex("Radio_Grille.png", T.radio_grille())
    glow = tex("Screen_Glow.png", T.screen_glow())
    m = Mesh()
    # bloc batterie (bas, plus épais) + corps (haut)
    rbox(m, "Body", (0, 0.045, 0), (0.060, 0.090, 0.036), 0.006, 3, tile=3)
    rbox(m, "Body", (0, 0.118, 0), (0.056, 0.064, 0.030), 0.008, 3, tile=3)
    rbox(m, "Trim", (0, 0.0905, 0), (0.0615, 0.004, 0.0375), 0.002, 1, tile=3)
    # antenne conique + embase
    cylinder(m, "Trim", "Trim", (0.018, 0.153, 0), 'y', 0.0075, 0.012, 14)
    tube(m, "Antenna", (0.018, 0.158, 0), (0.018, 0.232, 0), 0.0058, 12, True, 0.0035)
    # molettes crantées (canal + volume)
    for x, h in ((-0.014, 0.020), (0.004, 0.016)):
        cylinder(m, "Knob", "Knob", (x, 0.158 + h / 2, 0), 'y', 0.0075, h, 14)
    # bouton d'urgence orange
    cylinder(m, "Orange", "Orange", (-0.0235, 0.154, 0.0), 'y', 0.0045, 0.008, 10)
    # PTT latéral
    rbox(m, "Ptt", (0.0345, 0.118, 0), (0.007, 0.040, 0.020), 0.0025, 2, tile=3)
    rbox(m, "Button", (0.0335, 0.083, 0), (0.005, 0.014, 0.012), 0.002, 1, tile=3)
    rbox(m, "Button", (0.0335, 0.068, 0), (0.005, 0.014, 0.012), 0.002, 1, tile=3)
    # écran
    rbox(m, "Frame", (0, 0.133, -0.0155), (0.040, 0.026, 0.004), 0.002, 1, tile=3)
    m.flat_quad("Screen", (-0.018, 0.1235, -0.0176), (0.018, 0.1235, -0.0176), (0.018, 0.1425, -0.0176),
                (-0.018, 0.1425, -0.0176), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    # clavier 3x3
    for r in range(3):
        for c in range(3):
            rbox(m, "Key", ((c - 1) * 0.0125, 0.1075 - r * 0.0105 + 0.012, -0.0155), (0.0098, 0.0078, 0.0035), 0.0015, 1)
    # haut-parleur
    m.flat_quad("Grille", (-0.022, 0.010, -0.0186), (0.022, 0.010, -0.0186), (0.022, 0.078, -0.0186),
                (-0.022, 0.078, -0.0186), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    # clip ceinture + vis
    rbox(m, "Clip", (0, 0.060, 0.0205), (0.020, 0.090, 0.005), 0.002, 1, tile=3)
    rbox(m, "Clip", (0, 0.016, 0.0235), (0.020, 0.012, 0.011), 0.002, 1, tile=3)
    for sx in (-1, 1):
        cylinder(m, "Trim", "Trim", (sx * 0.0215, 0.012, -0.0185), 'z', 0.0035, 0.002, 8)
    save("PoliceRadio", m, {
        "Body": mat((0.05, 0.055, 0.065), ns=520), "Trim": mat((0.16, 0.17, 0.19), ns=650, pm=0.6),
        "Antenna": mat((0.03, 0.03, 0.035), ns=350), "Knob": mat((0.1, 0.1, 0.11), ns=450),
        "Orange": mat((0.95, 0.4, 0.05), ns=500, ke=(0.35, 0.12, 0.0)), "Ptt": mat((0.02, 0.02, 0.025), ns=300),
        "Button": mat((0.15, 0.15, 0.17), ns=420), "Frame": mat((0.02, 0.02, 0.025), ns=300),
        "Screen": mat((0.2, 0.8, 0.4), ns=800, ke=(0.15, 0.7, 0.3), map_kd=glow),
        "Key": mat((0.22, 0.22, 0.25), ns=400), "Grille": mat(ns=250, map_kd=grille),
        "Clip": mat((0.12, 0.12, 0.13), ns=500, pm=0.8)}, out)


def cuffs():
    m = Mesh()
    steel = "Steel"
    for sx in (-1, 1):
        cx = sx * 0.062
        # anneau (arceau) + boîtier de serrure à crémaillère
        torus(m, steel, (cx, 0.0215, 0), 'z', 0.0205, 0.0042, 28, 8)
        rbox(m, "Lock", (cx, 0.0455, 0), (0.026, 0.016, 0.014), 0.003, 2, tile=3)
        rbox(m, "Dark", (cx, 0.0455, -0.0072), (0.006, 0.009, 0.002), 0.0008, 1)   # trou de clé
        for k in range(5):                                                    # dents de cliquet
            rbox(m, "Dark", (cx + sx * 0.0125, 0.0105 + k * 0.0048, 0), (0.003, 0.0022, 0.009), 0.0005, 1)
        cylinder(m, steel, steel, (cx, 0.0006, 0), 'z', 0.0045, 0.012, 12)         # charnière
        # pivot vers la chaîne
        rbox(m, steel, (sx * 0.0385, 0.0215, 0), (0.012, 0.010, 0.010), 0.003, 2, tile=3)
    # chaîne : 4 maillons alternés
    for k in range(4):
        cx = -0.0225 + k * 0.015
        if k % 2 == 0:
            torus(m, steel, (cx, 0.0215, 0), 'z', 0.0062, 0.0017, 14, 6)
        else:
            torus(m, steel, (cx, 0.0215, 0), 'y', 0.0062, 0.0017, 14, 6)
    save("PoliceCuffs", m, {steel: mat((0.80, 0.82, 0.86), ns=760, pm=1.0),
                            "Lock": mat((0.68, 0.70, 0.74), ns=700, pm=1.0), "Dark": mat((0.03, 0.03, 0.035), ns=200)}, out)


def poop():
    m = Mesh()
    layers = [(0.0, 0.050, 0.030), (0.026, 0.038, 0.024), (0.048, 0.027, 0.020), (0.066, 0.016, 0.016)]
    for y, r, h in layers:
        sphere(m, "Poop", (0, y + h * 0.5, 0), 1.0, 18, 10, (r, h * 0.62, r))
    tube(m, "Poop", (0.002, 0.078, 0), (0.010, 0.098, 0.002), 0.011, 10, True, 0.002)
    for sx in (-1, 1):                                    # petits yeux (drôle et reconnaissable)
        sphere(m, "Eye", (sx * 0.013, 0.040, -0.026), 0.0075, 10, 6)
        sphere(m, "Pupil", (sx * 0.013, 0.040, -0.0325), 0.0035, 8, 5)
    save("PoopPile", m, {"Poop": mat((0.33, 0.19, 0.08), ns=420), "Eye": mat((0.95, 0.95, 0.95), ns=700),
                         "Pupil": mat((0.02, 0.02, 0.02), ns=800)}, out)


def marker():
    m = Mesh()
    torus(m, "Base", (0, 0.015, 0), 'y', 0.45, 0.018, 36, 8)
    tube(m, "Beam", (0, 0.03, 0), (0, 2.6, 0), 0.05, 12, True, 0.012)
    sphere(m, "Beam", (0, 2.65, 0), 0.07, 12, 8)
    save("MissionMarker", m, {"Base": mat((1.0, 0.8, 0.1), ns=500, ke=(0.6, 0.45, 0.0)),
                              "Beam": mat((1.0, 0.85, 0.2), ns=400, ke=(0.9, 0.7, 0.1))}, out)


def printer():
    glow = tex("Screen_Glow.png", T.screen_glow())
    ui = tex("Tablet_UI.png", T.tablet_ui())
    m = Mesh()
    rbox(m, "Body", (0, 0.13, 0), (0.46, 0.26, 0.34), 0.02, 3, tile=3)
    rbox(m, "Top", (0, 0.275, 0.02), (0.44, 0.03, 0.30), 0.01, 2, tile=3)
    rbox(m, "Slot", (0, 0.29, -0.06), (0.30, 0.006, 0.05), 0.001, 1)
    for k in range(6):                                                    # liasse de papier-monnaie qui sort
        rbox(m, "Paper", (0, 0.296 + k * 0.0012, -0.075), (0.25, 0.0012, 0.12), 0.0004, 1)
    rbox(m, "Tray", (0, 0.045, -0.19), (0.36, 0.05, 0.08), 0.008, 2, tile=3)
    rbox(m, "Dark", (0, 0.062, -0.19), (0.32, 0.012, 0.06), 0.003, 1)
    # panneau avant : écran + voyants + boutons
    rbox(m, "Frame", (0.0, 0.17, -0.172), (0.20, 0.10, 0.012), 0.004, 2, tile=3)
    m.flat_quad("Screen", (-0.088, 0.125, -0.1795), (0.088, 0.125, -0.1795), (0.088, 0.215, -0.1795),
                (-0.088, 0.215, -0.1795), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    for i, c in enumerate(("LedG", "LedO", "LedR")):
        cylinder(m, c, c, (-0.17 + i * 0.03, 0.225, -0.172), 'z', 0.007, 0.006, 10)
    for i in range(3):
        rbox(m, "Button", (0.14 + i * 0.0, 0.215 - i * 0.03, -0.175), (0.045, 0.018, 0.008), 0.003, 1)
    # grilles d'aération latérales
    for sx in (-1, 1):
        for k in range(7):
            rbox(m, "Dark", (sx * 0.231, 0.07 + k * 0.025, 0.02), (0.004, 0.008, 0.22), 0.0008, 1)
    # pieds + câble
    for sx in (-1, 1):
        for sz in (-1, 1):
            cylinder(m, "Dark", "Dark", (sx * 0.19, 0.008, sz * 0.13), 'y', 0.02, 0.016, 12)
    tube(m, "Dark", (0.0, 0.10, 0.17), (0.0, 0.05, 0.28), 0.008, 8)
    save("MoneyPrinter", m, {
        "Body": mat((0.11, 0.12, 0.14), ns=480, pm=0.4), "Top": mat((0.17, 0.18, 0.2), ns=520, pm=0.5),
        "Slot": mat((0.02, 0.02, 0.02), ns=200), "Paper": mat((0.55, 0.78, 0.55), ns=250),
        "Tray": mat((0.3, 0.31, 0.34), ns=600, pm=0.8), "Dark": mat((0.03, 0.03, 0.035), ns=250),
        "Frame": mat((0.05, 0.05, 0.06), ns=300),
        "Screen": mat((0.2, 0.8, 0.4), ns=800, ke=(0.15, 0.7, 0.3), map_kd=glow),
        "LedG": mat((0.1, 0.9, 0.2), ke=(0.1, 0.9, 0.2)), "LedO": mat((1.0, 0.6, 0.1), ke=(0.9, 0.5, 0.0)),
        "LedR": mat((0.95, 0.1, 0.1), ke=(0.9, 0.05, 0.05)), "Button": mat((0.25, 0.27, 0.3), ns=400)}, out)


def door():
    """Porte de sécurité v2 : volant de manœuvre, pênes, charnières renforcées, hublot de contrôle."""
    face_uv_ = face_uv
    panel = tex("Door_Panel.png", T.door_panel())
    steel = tex("Steel_Dark.png", T.brushed_metal(256, 256, base=(66, 70, 78), var=14, seed=11))
    light = tex("Steel_Light.png", T.brushed_metal(256, 256, base=(170, 174, 182), var=20, seed=13))
    m = Mesh()
    rbox(m, {"-z": "Front", "+z": "Front", "+x": "Edge", "-x": "Edge", "+y": "Edge", "-y": "Edge"},
         (0, 1.07, 0), (0.96, 2.06, 0.12), 0.012, 3, uv=face_uv_())
    for sgn in (-1, 1):
        rbox(m, {"-z": "Raised", "+z": "Raised", "+x": "Edge", "-x": "Edge", "+y": "Edge", "-y": "Edge"},
             (0, 1.12, sgn * 0.066), (0.66, 1.4, 0.012), 0.004, 2, tile=2)
        zf = sgn * 0.072
        # volant de manœuvre (torus + 4 rayons + moyeu)
        torus(m, "Chrome", (0.27, 1.02, sgn * 0.108), 'z', 0.085, 0.011, 26, 8)
        for a in range(4):
            ang = a * math.pi / 2 + math.pi / 4
            tube(m, "Chrome", (0.27, 1.02, sgn * 0.108),
                 (0.27 + 0.085 * math.cos(ang), 1.02 + 0.085 * math.sin(ang), sgn * 0.108), 0.008, 8)
        cylinder(m, "Chrome", "Chrome", (0.27, 1.02, sgn * 0.094), 'z', 0.026, 0.040, 16)
        tube(m, "Chrome", (0.27, 1.02, zf), (0.27, 1.02, sgn * 0.094), 0.02, 12)
        # hublot de contrôle + voyant
        rbox(m, "Dark", (0.0, 1.78, sgn * 0.0665), (0.30, 0.05, 0.006), 0.002, 1)
        rbox(m, "LedRed", (0.34, 1.50, sgn * 0.0665), (0.06, 0.012, 0.006), 0.002, 2)
        # rivets de la plaque
        for ix in range(5):
            for iy in (0.48, 1.76):
                cylinder(m, "Chrome", "Chrome", (-0.30 + ix * 0.15, iy, sgn * 0.0725), 'z', 0.0075, 0.006, 8)
    # pênes de verrouillage (côté libre)
    for y in (0.30, 0.70, 1.10, 1.50, 1.85):
        cylinder(m, "Chrome", "Chrome", (0.50, y, 0), 'x', 0.022, 0.07, 14)
    # charnières renforcées (côté charnière)
    for y in (0.35, 1.07, 1.80):
        cylinder(m, "Chrome", "Chrome", (-0.50, y, 0), 'y', 0.030, 0.20, 18, 0.004)
        rbox(m, "Chrome", (-0.47, y, 0), (0.07, 0.14, 0.06), 0.008, 2, tile=2)
        for sgn in (-1, 1):
            cylinder(m, "Dark", "Dark", (-0.44, y, sgn * 0.0725), 'z', 0.011, 0.006, 8)
    mats = {"Front": mat(ns=480, pm=0.8, map_kd=panel), "Raised": mat(ns=520, pm=0.85, map_kd=steel),
            "Edge": mat(ns=520, pm=0.9, map_kd=light), "Chrome": mat((0.88, 0.89, 0.92), ns=820, pm=1.0),
            "LedRed": mat((0.9, 0.12, 0.1), ns=500, ke=(0.9, 0.1, 0.08)), "Dark": mat((0.03, 0.03, 0.035), ns=250)}
    save("BankSecurityDoor", m, mats)


if __name__ == "__main__":
    G.common_textures()
    radio(); cuffs(); poop(); marker(); printer(); door()
    for name, img in G.TEXTURES.items():
        p = os.path.join(G.MONEY, name)
        if not os.path.exists(p):
            img.save(p, optimize=True)
    bad = 0
    for name, mesh in G.MESHES.items():
        for mt in mesh.order:
            for (a, b, c) in mesh.tris[mt]:
                if dot(cross(sub(b.p, a.p), sub(c.p, a.p)), add(add(a.n, b.n), c.n)) > 0:
                    bad += 1
    for r in G.REPORT:
        print("%-16s tris=%5d verts=%5d dim=%s" % r)
    print("triangles mal orientés :", bad)
    sys.exit(1 if bad else 0)
