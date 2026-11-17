#!/usr/bin/env python3
"""Modèles v6 : coffre mural PHYSIQUE (corps creux + porte séparée sur charnière).
Usage : python3 Tools/GenerateModelsV4.py   (n'écrit que WallSafe et WallSafeDoor)

Repère commun (les deux modèles partagent la même origine) :
  - origine au sol, centre du coffre ; face avant vers -Z ;
  - corps : x[-0.5, 0.5]  y[0, 0.8]  z[-0.35, 0.35] ;
  - cavité : x[-0.40, 0.22]  y[0.06, 0.74]  z[-0.35, 0.25] ;
  - axe de charnière : x = -0.41, z = -0.375 (vertical) ;
  - clavier : colonne droite, 3 colonnes x 4 lignes, pas 0.033, centre (0.36, 0.31),
    touches à z = -0.3735 ;  afficheur centre (0.36, 0.475, -0.371) ;
  - poignée : (0.15, 0.40, -0.44) dans le repère de la porte fermée.
Ces valeurs sont reprises dans Integration/PhysicalWallSafe.cs."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import GenerateModels as G
from modellib import *      # noqa
import texlib as T
from PIL import Image, ImageDraw

mat, save, tex = G.mat, G.save, G.tex


def textures():
    tex("Steel_Dark.png", T.brushed_metal(256, 256, base=(66, 70, 78), var=14, seed=11))
    tex("Steel_Mid.png", T.brushed_metal(256, 256, base=(110, 114, 122), var=18, seed=12))
    tex("Keypad.png", T.keypad_atlas())
    tex("Safe_Dial.png", T.safe_dial())
    img = Image.new("RGB", (128, 64), (10, 14, 18))
    d = ImageDraw.Draw(img)
    d.rectangle([1, 1, 126, 62], outline=(70, 90, 100), width=2)
    tex("Status_Blank.png", img)


def build_body():
    m = Mesh()
    # coque creuse
    rbox(m, "Body", (0, 0.40, 0.30), (1.00, 0.80, 0.10), 0.01, 2, tile=1)
    rbox(m, "Body", (-0.45, 0.40, 0.0), (0.10, 0.80, 0.70), 0.01, 2, tile=1)
    rbox(m, "Body", (0.36, 0.40, 0.0), (0.28, 0.80, 0.70), 0.01, 2, tile=1)
    rbox(m, "Body", (-0.09, 0.77, 0.0), (0.62, 0.06, 0.70), 0.01, 2, tile=1)
    rbox(m, "Body", (-0.09, 0.03, 0.0), (0.62, 0.06, 0.70), 0.01, 2, tile=1)
    rbox(m, "Shelf", (-0.09, 0.40, 0.03), (0.60, 0.02, 0.56), 0.004, 2, tile=2)
    # plaque avant de la colonne + cadre de feuillure
    rbox(m, "Trim", (0.36, 0.40, -0.352), (0.26, 0.78, 0.012), 0.004, 2, tile=2)
    for (cx, cy, sx, sy) in ((-0.45, 0.40, 0.08, 0.78), (-0.09, 0.775, 0.62, 0.03), (-0.09, 0.025, 0.62, 0.03)):
        rbox(m, "Trim", (cx, cy, -0.352), (sx, sy, 0.012), 0.004, 2, tile=2)
    # clavier + afficheur
    rbox(m, "KeyHousing", (0.36, 0.335, -0.364), (0.17, 0.39, 0.012), 0.004, 2, tile=2, skip=("+z",))
    m.flat_quad("Status", (0.285, 0.44, -0.3702), (0.435, 0.44, -0.3702), (0.435, 0.51, -0.3702),
                (0.285, 0.51, -0.3702), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    G.keypad_grid(m, (0.36, 0.31), 3, 4, 0.033, (0.026, 0.026, 0.006), z=-0.3735)
    # charnières
    for y in (0.16, 0.62):
        cylinder(m, "Chrome", "Chrome", (-0.41, y, -0.375), 'y', 0.016, 0.14, 14, 0.003)
    mats = {
        "Body": mat(ns=400, pm=0.8, map_kd="Steel_Dark.png"),
        "Trim": mat(ns=480, pm=0.9, map_kd="Steel_Mid.png"),
        "Shelf": mat(ns=300, pm=0.6, map_kd="Steel_Mid.png"),
        "KeyHousing": mat((0.08, 0.09, 0.11), ns=450, pm=0.5),
        "Status": mat(ns=700, ke=(0.02, 0.06, 0.05), map_kd="Status_Blank.png"),
        "Key": mat(ns=650, map_kd="Keypad.png"),
        "KeyBody": mat((0.1, 0.1, 0.12), ns=500),
        "Chrome": mat((0.88, 0.89, 0.92), ns=820, pm=1.0),
    }
    save("WallSafe", m, mats, note="Coffre mural v6 : corps creux (la porte est WallSafeDoor)")


def build_door():
    m = Mesh()
    rbox(m, "Door", (-0.09, 0.40, -0.375), (0.64, 0.70, 0.05), 0.012, 3, tile=1)
    rbox(m, "Raised", (-0.09, 0.40, -0.403), (0.54, 0.60, 0.008), 0.004, 2, tile=2)
    cylinder(m, "Knurl", "DialFace", (-0.17, 0.44, -0.420), 'z', 0.07, 0.03, 40, 0.004)
    # poignée verticale
    tube(m, "Chrome", (0.15, 0.27, -0.447), (0.15, 0.53, -0.447), 0.014, 12)
    for y in (0.29, 0.51):
        tube(m, "Chrome", (0.15, y, -0.404), (0.15, y, -0.447), 0.010, 10)
        sphere(m, "Chrome", (0.15, y, -0.447), 0.016, 10, 8)
    # verrous (côté libre)
    for y in (0.16, 0.40, 0.64):
        cylinder(m, "Bolt", "Bolt", (0.245, y, -0.375), 'x', 0.016, 0.04, 14, 0.003)
    # charnières côté porte
    for y in (0.16, 0.62):
        cylinder(m, "Chrome", "Chrome", (-0.41, y, -0.375), 'y', 0.018, 0.10, 14, 0.003)
    mats = {
        "Door": mat(ns=500, pm=0.85, map_kd="Steel_Mid.png"),
        "Raised": mat(ns=520, pm=0.85, map_kd="Steel_Dark.png"),
        "Knurl": mat((0.16, 0.16, 0.18), ns=500, pm=0.8),
        "DialFace": mat(ns=600, map_kd="Safe_Dial.png"),
        "Chrome": mat((0.88, 0.89, 0.92), ns=820, pm=1.0),
        "Bolt": mat((0.70, 0.72, 0.76), ns=700, pm=1.0),
    }
    save("WallSafeDoor", m, mats, note="Porte du coffre mural (pivote autour de x=-0.41, z=-0.375)")


if __name__ == "__main__":
    textures()
    build_body()
    build_door()
    for name, img in G.TEXTURES.items():
        img.save(os.path.join(G.MONEY, name))
    for row in G.REPORT:
        print("%-14s tris=%d verts=%d taille=%s" % row)
    try:
        preview(G.MESHES["WallSafe"], "/tmp/WallSafe_preview.png")
        merged = Mesh()
        for n in ("WallSafe", "WallSafeDoor"):
            for mt in G.MESHES[n].order:
                for tri_ in G.MESHES[n].tris[mt]:
                    merged.tri("M_" + mt, *tri_)
        preview(merged, "/tmp/WallSafe_all.png")
    except Exception as exc:
        print("aperçu ignoré :", exc)
