#!/usr/bin/env python3
"""Génère tous les modèles OBJ/MTL/PNG de Fusion DarkRP dans Assets/Models.

Usage (depuis la racine du dépôt) :  python3 Tools/GenerateModels.py
Dépendances : Pillow, numpy.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from modellib import *      # noqa
import texlib as T          # noqa
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
MONEY = os.path.join(ROOT, "Assets", "Models", "Money")
RPDIR = os.path.join(ROOT, "Assets", "Models", "RP")
os.makedirs(MONEY, exist_ok=True)
os.makedirs(RPDIR, exist_ok=True)

TEXTURES = {}       # nom de fichier -> Image (écrit une seule fois dans Money/)
REPORT = []
MESHES = {}


def tex(name, img):
    if name not in TEXTURES:
        TEXTURES[name] = img
    return name


def mat(kd=(0.8, 0.8, 0.8), ns=200.0, pm=0.0, ke=None, map_kd=None, d=1.0):
    m = {"Kd": kd, "Ns": ns, "Pm": pm, "d": d}
    if ke is not None:
        m["Ke"] = ke
    if map_kd:
        m["map_Kd"] = map_kd
        m["Kd"] = (1.0, 1.0, 1.0) if kd == (0.8, 0.8, 0.8) else kd
    return m


def save(name, mesh, mats, folder=None, note=""):
    folder = folder or MONEY
    objp = os.path.join(folder, name + ".obj")
    mtlp = os.path.join(folder, name + ".mtl")
    used = [m for m in mesh.order if mesh.tris[m]]
    missing = [m for m in used if m not in mats]
    if missing:
        raise RuntimeError("%s : matériaux manquants %s" % (name, missing))
    nv = mesh.write_obj(objp, name + ".mtl", note)
    write_mtl(mtlp, {k: mats[k] for k in used})
    mn, mx = mesh.bounds()
    REPORT.append((name, mesh.tri_count(), nv, tuple(round(mx[i] - mn[i], 3) for i in range(3))))
    MESHES[name] = mesh


# ======================================================================
# Textures communes
# ======================================================================

def common_textures():
    tex("Steel_Dark.png", T.brushed_metal(256, 256, base=(66, 70, 78), var=14, seed=11))
    tex("Steel_Mid.png", T.brushed_metal(256, 256, base=(110, 114, 122), var=18, seed=12))
    tex("Steel_Light.png", T.brushed_metal(256, 256, base=(170, 174, 182), var=20, seed=13))
    tex("Wood_Oak.png", T.wood(256, 256, (166, 116, 70), (108, 70, 40), 3))
    tex("Wood_Dark.png", T.wood(256, 256, (96, 62, 40), (56, 34, 20), 4))
    tex("Wood_Crate.png", T.wood(256, 256, (190, 150, 96), (140, 102, 60), 6))
    tex("Marble.png", T.marble(256, 256))
    tex("Carbon.png", T.carbon(128, 128))
    tex("Leather.png", T.leather(128, 128))
    tex("Keypad.png", T.keypad_atlas())
    tex("Paper_Edge.png", T.paper_edge())


# ======================================================================
# ARGENT
# ======================================================================

NOTE_SIZES = {5: (0.120, 0.062), 10: (0.127, 0.067), 20: (0.133, 0.072), 50: (0.140, 0.077), 100: (0.147, 0.082)}
COINS = {  # id: (diamètre m, épaisseur m, matière, libellé)
    "1Cent": (0.01625, 0.00167, "copper", "1c"),
    "2Cent": (0.01875, 0.00167, "copper", "2c"),
    "5Cent": (0.02125, 0.00167, "copper", "5c"),
    "10Cent": (0.01975, 0.00193, "gold", "10c"),
    "20Cent": (0.02225, 0.00214, "gold", "20c"),
    "50Cent": (0.02425, 0.00238, "gold", "50c"),
    "1EUR": (0.02325, 0.00233, "bimetal_silver_in", "1"),
    "2EUR": (0.02575, 0.00220, "bimetal_gold_out", "2"),
}


def build_note(d):
    w, h = NOTE_SIZES[d]
    t = 0.00012
    bow = 0.0016
    hw, hh = w / 2, h / 2
    nx, ny = 10, 2
    tx = tex("Note_%dEUR.png" % d, T.note_texture(d))
    m = Mesh()

    def z_at(x, y):
        return bow * (1 - (x / hw) ** 2) * (1 - 0.35 * (y / hh) ** 2)

    def normal(x, y, side):
        e = 1e-4
        dzdx = (z_at(x + e, y) - z_at(x - e, y)) / (2 * e)
        dzdy = (z_at(x, y + e) - z_at(x, y - e)) / (2 * e)
        n = (-dzdx, -dzdy, 1.0)
        return mul(n, side)

    for side in (-1, 1):  # -1 : face avant (vers -z), +1 : verso
        rows = []
        for j in range(ny + 1):
            row = []
            for i in range(nx + 1):
                x = -hw + w * i / nx
                y = -hh + h * j / ny
                z = z_at(x, y) + side * t / 2
                u = i / nx
                v = j / ny
                if side < 0:
                    uv = (u, 0.5 + 0.5 * v)
                else:
                    uv = (1 - u, 0.5 * v)
                row.append(V((x, y, z), uv, normal(x, y, side)))
            rows.append(row)
        for j in range(ny):
            for i in range(nx):
                m.quad("Paper", rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i])
    # tranches
    def edge(p0, p1, n):
        a = (p0[0], p0[1], z_at(p0[0], p0[1]) - t / 2)
        b = (p1[0], p1[1], z_at(p1[0], p1[1]) - t / 2)
        a2 = (a[0], a[1], a[2] + t)
        b2 = (b[0], b[1], b[2] + t)
        m.quad("Edge", V(a, (0, 0), n), V(b, (1, 0), n), V(b2, (1, 1), n), V(a2, (0, 1), n))
    for i in range(nx):
        x0, x1 = -hw + w * i / nx, -hw + w * (i + 1) / nx
        edge((x0, -hh), (x1, -hh), (0, -1, 0))
        edge((x0, hh), (x1, hh), (0, 1, 0))
    for j in range(ny):
        y0, y1 = -hh + h * j / ny, -hh + h * (j + 1) / ny
        edge((-hw, y0), (-hw, y1), (-1, 0, 0))
        edge((hw, y0), (hw, y1), (1, 0, 0))
    mats = {"Paper": mat(ns=60, map_kd=tx), "Edge": mat((0.93, 0.92, 0.88), ns=30)}
    save("Note_%dEUR" % d, m, mats)


def build_band(d):
    w, h = NOTE_SIZES[d]
    t = 0.010
    tx = tex("Note_%dEUR.png" % d, T.note_texture(d))
    sx = tex("Strap_%dEUR.png" % d, T.strap_texture(d))
    ex = tex("Paper_Edge.png", T.paper_edge())
    m = Mesh()
    rbox(m, {"-z": "Top", "+z": "Bottom", "+x": "Side", "-x": "Side", "+y": "Side", "-y": "Side"},
         (0, 0, 0), (w, h, t), 0.0007, 2,
         uv=lambda f, Q, s: (face_uv((0, 0.5, 1, 1))(f, Q, s) if f == "-z"
                              else face_uv((0, 0, 1, 0.5))(f, Q, s) if f == "+z"
                              else (Q[0] * 40, Q[2] * 40 + Q[1] * 3)))
    rbox(m, "Strap", (0, 0, 0), (0.026, h + 0.0014, t + 0.0014), 0.0005, 2,
         uv=lambda f, Q, s: face_uv((0, 0, 1, 1))(f, Q, s))
    mats = {"Top": mat(ns=60, map_kd=tx), "Bottom": mat(ns=60, map_kd=tx), "Side": mat(ns=30, map_kd=ex),
            "Strap": mat(ns=120, map_kd=sx)}
    save("Band_%dEUR" % d, m, mats)


def build_coin(cid, spec):
    dia, thick, kind, label = spec
    R, hz = dia / 2, thick / 2
    face_tex = tex("Coin_%s.png" % cid, T.coin_texture(label, kind))
    rim = 0.0011
    rin = R - rim
    rec = 0.00018
    c = 0.00028
    m = Mesh()

    def disc_uv(ph, i, rh, v):
        r = rh[0] / R
        a = ph * 2 * math.pi
        return (0.5 + 0.5 * r * math.cos(a), 0.5 + 0.5 * r * math.sin(a))

    edge_prof = [(R - c, -hz), (R, -hz + c), (R, hz - c), (R - c, hz)]
    lathe(m, "Edge", (0, 0, 0), 'z', edge_prof, 28, True, lambda ph, i, rh, v: (ph * 8, v))
    top = [(R - c, hz), (rin, hz), (rin, hz - rec), (0.0, hz - rec)]
    bot = [(0.0, -hz + rec), (rin, -hz + rec), (rin, -hz), (R - c, -hz)]
    lathe(m, "Face", (0, 0, 0), 'z', top, 28, True, disc_uv)
    lathe(m, "Face", (0, 0, 0), 'z', bot, 28, True, disc_uv)
    metal = {"copper": (0.70, 0.40, 0.26), "gold": (0.86, 0.70, 0.36)}.get(kind, (0.80, 0.80, 0.84))
    mats = {"Face": mat(ns=650, pm=0.9, map_kd=face_tex), "Edge": mat(metal, ns=700, pm=1.0)}
    save("Coin_%s" % cid, m, mats)


def build_card():
    W, H, Tk = 0.0856, 0.0540, 0.001
    tx = tex("BankCard.png", T.card_texture())
    m = Mesh()
    pts = rounded_rect_points(W, H, 0.00318, 8)
    polygon_prism(m, "CardFront", "CardBack", "CardEdge", pts, Tk,
                  uv_front=lambda u, v: (u, 0.5 + 0.5 * v),
                  uv_back=lambda u, v: (u, 0.5 * v))
    # Puce en relief intégrée à la carte (objet unique, plus de pièce séparée).
    # Position et taille calées sur la puce imprimée de la texture : face avant
    # (-z), coin gauche, légèrement au-dessus du milieu.
    chip_w, chip_h, chip_t = W * 80.0 / 512.0, H * 66.0 / 320.0, 0.0004
    chip_x = -W / 2.0 + W * 86.0 / 512.0
    chip_y = H / 2.0 - H * 151.0 / 320.0
    rbox(m, "Chip", (chip_x, chip_y, -(Tk / 2.0 + chip_t / 2.0 - 0.00005)),
         (chip_w, chip_h, chip_t), 0.0012, 2)
    mats = {"CardFront": mat(ns=520, map_kd=tx), "CardBack": mat(ns=380, map_kd=tx),
            "CardEdge": mat((0.88, 0.90, 0.94), ns=300),
            "Chip": mat((0.86, 0.70, 0.34), ns=750, pm=1.0)}
    save("BankCard", m, mats)


# ======================================================================
# ATM
# ======================================================================

def build_atm():
    m = Mesh()
    steel = tex("Steel_Dark.png", T.brushed_metal(256, 256, base=(66, 70, 78), var=14, seed=11))
    light = tex("Steel_Light.png", T.brushed_metal(256, 256, base=(170, 174, 182), var=20, seed=13))
    keys = tex("Keypad.png", T.keypad_atlas())
    sign = tex("ATM_Sign.png", T.atm_sign())
    screen = tex("ATM_Screen.png", T.atm_screen_dark())
    cardp = tex("ATM_CardPlate.png", T.card_slot_plate())
    cashp = tex("ATM_CashPlate.png", T.cash_slot_plate())
    vent = tex("ATM_Vent.png", T.vent_texture())
    fascia = tex("ATM_Fascia.png", T.fascia_texture())

    rbox(m, "Plinth", (0, 0.05, 0), (0.44, 0.10, 0.34), 0.01, 2, tile=3)
    rbox(m, "Body", (0, 0.47, 0), (0.45, 0.74, 0.35), 0.012, 2, tile=3)
    rbox(m, {"-z": "Fascia", "+z": "Body", "+x": "Body", "-x": "Body", "+y": "Body", "-y": "Body"},
         (0, 1.20, 0), (0.45, 0.72, 0.35), 0.02, 2, tile=3, uv=lambda f, Q, s: (face_uv()(f, Q, s)))
    rbox(m, "Cap", (0, 1.585, 0), (0.47, 0.05, 0.37), 0.02, 2, tile=3)
    # enseigne lumineuse
    rbox(m, {"-z": "Sign", "+z": "Body", "+x": "Body", "-x": "Body", "+y": "Body", "-y": "Body"},
         (0, 1.50, -0.178), (0.36, 0.07, 0.012), 0.004, 2, skip=("+z",), uv=face_uv())
    # cadre d'écran + écran
    rbox(m, "Bezel", (0, 1.10, -0.171), (0.33, 0.52, 0.03), 0.01, 3, skip=("+z",))
    m.flat_quad("Screen", (-0.135, 0.875, -0.1865), (0.135, 0.875, -0.1865), (0.135, 1.325, -0.1865),
                (-0.135, 1.325, -0.1865), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    # touches latérales
    for sx in (-1, 1):
        for y in (0.93, 1.03, 1.13, 1.23):
            rbox(m, "SideKey", (sx * 0.1925, y, -0.183), (0.035, 0.016, 0.014), 0.003, 2, skip=("+z",), tile=2)
    # clavier
    rbox(m, "KeyHousing", (0, 0.66, -0.18), (0.31, 0.36, 0.016), 0.006, 2, skip=("+z",), tile=3)
    cols_x = (-0.105, -0.035, 0.035, 0.105)
    rows_y = (0.78, 0.705, 0.63, 0.555)
    for r_i, ky in enumerate(rows_y):
        for c_i, kx in enumerate(cols_x):
            rect = atlas_rect(c_i, r_i, 4, 4)
            rbox(m, {"-z": "Key", "+z": "KeyBody", "+x": "KeyBody", "-x": "KeyBody", "+y": "KeyBody", "-y": "KeyBody"},
                 (kx, ky, -0.194), (0.058, 0.062, 0.012), 0.0035, 2, skip=("+z",), uv=face_uv(rect))
    # lecteur de carte
    rbox(m, {"-z": "CardPlate", "+z": "Body", "+x": "Body", "-x": "Body", "+y": "Body", "-y": "Body"},
         (-0.065, 0.3675, -0.183), (0.15, 0.07, 0.016), 0.005, 2, skip=("+z",), uv=face_uv())
    # distributeur de billets
    rbox(m, {"-z": "CashPlate", "+z": "Body", "+x": "Body", "-x": "Body", "+y": "Body", "-y": "Body"},
         (0, 0.255, -0.183), (0.36, 0.11, 0.016), 0.005, 2, skip=("+z",), uv=face_uv())
    rbox(m, "Slot", (0.10, 0.43, -0.181), (0.07, 0.018, 0.012), 0.003, 2, skip=("+z",))
    m.flat_quad("Vent", (-0.15, 0.125, -0.1752), (0.15, 0.125, -0.1752), (0.15, 0.19, -0.1752),
                (-0.15, 0.19, -0.1752), (0, 0), (2, 0), (2, 1), (0, 1), n=(0, 0, -1))
    rbox(m, "LedGreen", (0, 1.395, -0.18), (0.2, 0.01, 0.006), 0.002, 2, skip=("+z",))

    mats = {
        "Plinth": mat((0.12, 0.13, 0.15), ns=300, pm=0.6, map_kd=steel),
        "Body": mat(ns=380, pm=0.7, map_kd=steel),
        "Fascia": mat(ns=420, pm=0.7, map_kd=fascia),
        "Cap": mat(ns=520, pm=0.9, map_kd=light),
        "Sign": mat(ns=300, ke=(0.55, 0.65, 0.9), map_kd=sign),
        "Bezel": mat((0.02, 0.02, 0.025), ns=880, pm=0.1),
        "Screen": mat(ns=900, ke=(0.10, 0.28, 0.55), map_kd=screen),
        "SideKey": mat(ns=600, pm=0.9, map_kd=light),
        "KeyHousing": mat((0.09, 0.10, 0.12), ns=450, pm=0.5),
        "Key": mat(ns=650, map_kd=keys),
        "KeyBody": mat((0.10, 0.10, 0.12), ns=500),
        "CardPlate": mat(ns=500, map_kd=cardp),
        "CashPlate": mat(ns=500, map_kd=cashp),
        "Slot": mat((0.01, 0.01, 0.012), ns=700),
        "Vent": mat(ns=200, map_kd=vent),
        "LedGreen": mat((0.1, 0.8, 0.3), ns=500, ke=(0.1, 0.9, 0.35)),
    }
    save("ATM", m, mats)


# ======================================================================
# COFFRE-FORT, PORTE, COFFRE MURAL
# ======================================================================

def keypad_grid(m, center, cols=3, rows=4, pitch=0.045, key=(0.036, 0.036, 0.008), z=0.0):
    atlas_cols = [0, 1, 2]
    for r_i in range(rows):
        for c_i in range(cols):
            kx = center[0] + (c_i - (cols - 1) / 2.0) * pitch
            ky = center[1] + ((rows - 1) / 2.0 - r_i) * pitch
            rect = atlas_rect(c_i, r_i, 4, 4)
            rbox(m, {"-z": "Key", "+z": "KeyBody", "+x": "KeyBody", "-x": "KeyBody", "+y": "KeyBody", "-y": "KeyBody"},
                 (kx, ky, z), key, 0.0025, 2, uv=face_uv(rect), skip=("+z",))


def build_vault():
    m = Mesh()
    steel = tex("Steel_Dark.png", T.brushed_metal(256, 256, base=(66, 70, 78), var=14, seed=11))
    light = tex("Steel_Light.png", T.brushed_metal(256, 256, base=(170, 174, 182), var=20, seed=13))
    mid = tex("Steel_Mid.png", T.brushed_metal(256, 256, base=(110, 114, 122), var=18, seed=12))
    keys = tex("Keypad.png", T.keypad_atlas())
    dial = tex("Safe_Dial.png", T.safe_dial())
    label = tex("Vault_Label.png", T.vault_label())
    status = tex("Status_Access.png", T.status_panel("ACCES"))

    rbox(m, "Plinth", (0, 0.04, 0), (1.26, 0.08, 1.06), 0.02, 2, tile=1)
    rbox(m, "Body", (0, 0.74, 0), (1.2, 1.32, 1.0), 0.04, 3, tile=1)
    rbox(m, "Trim", (0, 0.70, -0.505), (1.12, 1.32, 0.02), 0.02, 3, tile=1)
    # porte ronde
    cx, cy = -0.10, 0.70
    cylinder(m, "DoorSide", "DoorFace", (cx, cy, -0.545), 'z', 0.44, 0.06, 40, 0.012, tile=1)
    torus(m, "Brass", (cx, cy, -0.578), 'z', 0.36, 0.014, 40, 6)
    for k in range(16):
        a = 2 * math.pi * k / 16
        cylinder(m, "Bolt", "Bolt", (cx + 0.40 * math.cos(a), cy + 0.40 * math.sin(a), -0.595), 'z', 0.018, 0.04, 8, 0.004)
    # volant
    cylinder(m, "Brass", "Brass", (cx, cy, -0.61), 'z', 0.075, 0.07, 28, 0.01)
    torus(m, "Chrome", (cx, cy, -0.635), 'z', 0.17, 0.016, 28, 6)
    for k in range(3):
        a = 2 * math.pi * k / 3 + math.pi / 2
        tube(m, "Chrome", (cx, cy, -0.635), (cx + 0.17 * math.cos(a), cy + 0.17 * math.sin(a), -0.635), 0.013, 10)
        sp = (cx + 0.17 * math.cos(a), cy + 0.17 * math.sin(a), -0.635)
        sphere(m, "Chrome", sp, 0.026, 12, 8)
    cylinder(m, "Dial", "DialFace", (cx, cy + 0.30, -0.585), 'z', 0.06, 0.02, 32, 0.003)
    # plaque
    rbox(m, {"-z": "Label", "+z": "Trim", "+x": "Trim", "-x": "Trim", "+y": "Trim", "-y": "Trim"},
         (cx, 1.27, -0.52), (0.5, 0.12, 0.012), 0.004, 2, uv=face_uv(), skip=("+z",))
    # clavier + écran d'accès
    rbox(m, "KeyHousing", (0.46, 0.64, -0.52), (0.17, 0.34, 0.02), 0.006, 2, tile=2, skip=("+z",))
    m.flat_quad("Status", (0.40, 0.76, -0.5311), (0.52, 0.76, -0.5311), (0.52, 0.81, -0.5311),
                (0.40, 0.81, -0.5311), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    keypad_grid(m, (0.46, 0.62), 3, 4, 0.043, (0.034, 0.034, 0.008), z=-0.536)
    # charnières
    for y in (0.35, 0.70, 1.05):
        cylinder(m, "Chrome", "Chrome", (-0.575, y, -0.53), 'y', 0.03, 0.18, 18, 0.004)
    rbox(m, "LedRed", (0.46, 0.43, -0.531), (0.05, 0.012, 0.006), 0.002, 2, skip=("+z",))

    mats = {
        "Plinth": mat((0.1, 0.1, 0.12), ns=300, pm=0.6, map_kd=steel),
        "Body": mat(ns=400, pm=0.8, map_kd=steel),
        "Trim": mat(ns=480, pm=0.85, map_kd=mid),
        "DoorSide": mat(ns=520, pm=0.9, map_kd=mid),
        "DoorFace": mat(ns=560, pm=0.9, map_kd=light),
        "Brass": mat((0.82, 0.66, 0.30), ns=700, pm=1.0),
        "Bolt": mat((0.62, 0.64, 0.68), ns=650, pm=1.0),
        "Chrome": mat((0.88, 0.89, 0.92), ns=820, pm=1.0),
        "Dial": mat((0.12, 0.12, 0.14), ns=500, pm=0.8),
        "DialFace": mat(ns=600, map_kd=dial),
        "Label": mat(ns=520, pm=0.8, map_kd=label),
        "KeyHousing": mat((0.08, 0.09, 0.11), ns=450, pm=0.5),
        "Status": mat(ns=700, ke=(0.1, 0.5, 0.25), map_kd=status),
        "Key": mat(ns=650, map_kd=keys),
        "KeyBody": mat((0.1, 0.1, 0.12), ns=500),
        "LedRed": mat((0.9, 0.12, 0.1), ns=500, ke=(0.9, 0.1, 0.08)),
    }
    save("BankVault", m, mats)


def build_door():
    m = Mesh()
    panel = tex("Door_Panel.png", T.door_panel())
    steel = tex("Steel_Dark.png", T.brushed_metal(256, 256, base=(66, 70, 78), var=14, seed=11))
    light = tex("Steel_Light.png", T.brushed_metal(256, 256, base=(170, 174, 182), var=20, seed=13))
    rbox(m, {"-z": "Front", "+z": "Front", "+x": "Edge", "-x": "Edge", "+y": "Edge", "-y": "Edge"},
         (0, 1.07, 0), (0.96, 2.06, 0.12), 0.012, 3, uv=face_uv())
    rbox(m, {"-z": "Raised", "+z": "Raised", "+x": "Edge", "-x": "Edge", "+y": "Edge", "-y": "Edge"},
         (0, 1.12, -0.066), (0.66, 1.4, 0.012), 0.004, 2, tile=2)
    rbox(m, {"-z": "Raised", "+z": "Raised", "+x": "Edge", "-x": "Edge", "+y": "Edge", "-y": "Edge"},
         (0, 1.12, 0.066), (0.66, 1.4, 0.012), 0.004, 2, tile=2)
    for sgn in (-1, 1):
        zf = sgn * 0.066
        tube(m, "Chrome", (0.34, 1.0, zf), (0.34, 1.0, sgn * 0.112), 0.015, 12)
        tube(m, "Chrome", (0.34, 1.0, sgn * 0.112), (0.17, 1.0, sgn * 0.112), 0.014, 12)
        sphere(m, "Chrome", (0.17, 1.0, sgn * 0.112), 0.02, 12, 8)
        rbox(m, "LedRed", (0.34, 1.38, sgn * 0.0665), (0.05, 0.012, 0.006), 0.002, 2)
    for y in (0.35, 1.07, 1.80):
        cylinder(m, "Chrome", "Chrome", (-0.50, y, 0), 'y', 0.028, 0.18, 18, 0.004)
        rbox(m, "Chrome", (-0.47, y, 0), (0.06, 0.12, 0.05), 0.008, 2, tile=2)
    mats = {
        "Front": mat(ns=480, pm=0.8, map_kd=panel),
        "Raised": mat(ns=520, pm=0.85, map_kd=steel),
        "Edge": mat(ns=520, pm=0.9, map_kd=light),
        "Chrome": mat((0.88, 0.89, 0.92), ns=820, pm=1.0),
        "LedRed": mat((0.9, 0.12, 0.1), ns=500, ke=(0.9, 0.1, 0.08)),
    }
    save("BankSecurityDoor", m, mats)

    # cadre de porte (nouveau modèle, fixe)
    f = Mesh()
    rbox(f, "Frame", (-0.56, 1.10, 0), (0.12, 2.20, 0.22), 0.01, 2, tile=2)
    rbox(f, "Frame", (0.56, 1.10, 0), (0.12, 2.20, 0.22), 0.01, 2, tile=2)
    rbox(f, "Frame", (0, 2.16, 0), (1.24, 0.08, 0.22), 0.01, 2, tile=2)
    rbox(f, "Threshold", (0, 0.01, 0), (1.0, 0.02, 0.22), 0.004, 2, tile=2)
    rbox(f, "Chrome", (0.49, 1.05, 0), (0.02, 0.20, 0.06), 0.004, 2, tile=2)
    save("BankSecurityDoorFrame", f, {"Frame": mat(ns=420, pm=0.8, map_kd=steel),
                                      "Threshold": mat((0.5, 0.52, 0.56), ns=400, pm=0.8),
                                      "Chrome": mat((0.88, 0.89, 0.92), ns=820, pm=1.0)})


def build_wall_safe():
    m = Mesh()
    steel = tex("Steel_Dark.png", T.brushed_metal(256, 256, base=(66, 70, 78), var=14, seed=11))
    mid = tex("Steel_Mid.png", T.brushed_metal(256, 256, base=(110, 114, 122), var=18, seed=12))
    keys = tex("Keypad.png", T.keypad_atlas())
    dial = tex("Safe_Dial.png", T.safe_dial())
    status = tex("Status_Access.png", T.status_panel("ACCES"))
    rbox(m, "Body", (0, 0.375, 0), (0.9, 0.75, 0.70), 0.02, 3, tile=1)
    rbox(m, "Trim", (0, 0.375, -0.352), (0.84, 0.70, 0.012), 0.008, 2, tile=2)
    rbox(m, "Door", (0, 0.375, -0.375), (0.78, 0.65, 0.05), 0.015, 3, tile=1)
    cylinder(m, "Knurl", "DialFace", (-0.14, 0.40, -0.425), 'z', 0.075, 0.04, 40, 0.004)
    cylinder(m, "Chrome", "Chrome", (0.14, 0.40, -0.43), 'z', 0.03, 0.04, 20, 0.004)
    tube(m, "Chrome", (0.07, 0.40, -0.457), (0.21, 0.40, -0.457), 0.012, 12)
    sphere(m, "Chrome", (0.07, 0.40, -0.457), 0.018, 10, 8)
    sphere(m, "Chrome", (0.21, 0.40, -0.457), 0.018, 10, 8)
    rbox(m, "KeyHousing", (0.30, 0.40, -0.41), (0.12, 0.27, 0.012), 0.004, 2, tile=2, skip=("+z",))
    m.flat_quad("Status", (0.245, 0.50, -0.4162), (0.355, 0.50, -0.4162), (0.355, 0.54, -0.4162),
                (0.245, 0.54, -0.4162), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    keypad_grid(m, (0.30, 0.38), 3, 4, 0.033, (0.026, 0.026, 0.006), z=-0.4195)
    rbox(m, "LedRed", (-0.14, 0.60, -0.4115), (0.05, 0.012, 0.006), 0.002, 2)
    for y in (0.2, 0.55):
        cylinder(m, "Chrome", "Chrome", (-0.405, y, -0.39), 'y', 0.018, 0.12, 14, 0.003)
    mats = {
        "Body": mat(ns=400, pm=0.8, map_kd=steel),
        "Trim": mat(ns=480, pm=0.9, map_kd=mid),
        "Door": mat(ns=500, pm=0.85, map_kd=mid),
        "Knurl": mat((0.16, 0.16, 0.18), ns=500, pm=0.8),
        "DialFace": mat(ns=600, map_kd=dial),
        "Chrome": mat((0.88, 0.89, 0.92), ns=820, pm=1.0),
        "KeyHousing": mat((0.08, 0.09, 0.11), ns=450, pm=0.5),
        "Status": mat(ns=700, ke=(0.1, 0.5, 0.25), map_kd=status),
        "Key": mat(ns=650, map_kd=keys),
        "KeyBody": mat((0.1, 0.1, 0.12), ns=500),
        "LedRed": mat((0.9, 0.12, 0.1), ns=500, ke=(0.9, 0.1, 0.08)),
    }
    save("WallSafe", m, mats)


# ======================================================================
# MOBILIER
# ======================================================================

def build_desk():
    m = Mesh()
    oak = tex("Wood_Oak.png", T.wood(256, 256, (166, 116, 70), (108, 70, 40), 3))
    dark = tex("Wood_Dark.png", T.wood(256, 256, (96, 62, 40), (56, 34, 20), 4))
    screen = tex("ATM_Screen.png", T.atm_screen_dark())
    rbox(m, "Top", (0, 0.735, 0), (1.5, 0.04, 0.75), 0.006, 2, tile=1)
    rbox(m, "Body", (0.55, 0.36, 0.0), (0.4, 0.71, 0.70), 0.005, 2, tile=1)
    for y in (0.14, 0.36, 0.58):
        rbox(m, "Drawer", (0.55, y, -0.356), (0.36, 0.19, 0.012), 0.004, 2, tile=1)
        rbox(m, "Handle", (0.55, y + 0.05, -0.37), (0.12, 0.014, 0.014), 0.004, 2, tile=2)
    rbox(m, "Body", (-0.70, 0.36, 0.0), (0.04, 0.71, 0.70), 0.004, 2, tile=1)
    rbox(m, "Body", (-0.15, 0.45, 0.33), (1.0, 0.5, 0.02), 0.004, 2, tile=1)
    # écran bancaire vertical
    rbox(m, "MonPlate", (0, 0.765, 0.10), (0.22, 0.012, 0.16), 0.004, 2, tile=2)
    rbox(m, "MonPlate", (0, 0.80, 0.115), (0.05, 0.07, 0.03), 0.004, 2, tile=2)
    rbox(m, "Monitor", (0, 1.10, 0.10), (0.40, 0.50, 0.035), 0.01, 3, tile=2)
    m.flat_quad("Screen", (-0.18, 0.88, 0.0818), (0.18, 0.88, 0.0818), (0.18, 1.32, 0.0818),
                (-0.18, 1.32, 0.0818), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    rbox(m, "Keyboard", (0, 0.7535, -0.17), (0.40, 0.015, 0.13), 0.004, 2, tile=2)
    rbox(m, "Keyboard", (0.26, 0.751, -0.17), (0.05, 0.012, 0.08), 0.004, 2, tile=2)
    rbox(m, "Reader", (-0.30, 0.7545, -0.14), (0.10, 0.016, 0.07), 0.004, 2, tile=2)
    mats = {
        "Top": mat(ns=520, map_kd=oak),
        "Body": mat(ns=380, map_kd=dark),
        "Drawer": mat(ns=420, map_kd=oak),
        "Handle": mat((0.8, 0.82, 0.86), ns=800, pm=1.0),
        "MonPlate": mat((0.08, 0.08, 0.1), ns=500, pm=0.5),
        "Monitor": mat((0.02, 0.02, 0.03), ns=800),
        "Screen": mat(ns=900, ke=(0.1, 0.25, 0.5), map_kd=screen),
        "Keyboard": mat((0.05, 0.05, 0.06), ns=500),
        "Reader": mat((0.12, 0.13, 0.16), ns=550, pm=0.5),
    }
    save("BankOfficeDesk", m, mats)


def build_counter():
    m = Mesh()
    dark = tex("Wood_Dark.png", T.wood(256, 256, (96, 62, 40), (56, 34, 20), 4))
    marble = tex("Marble.png", T.marble(256, 256))
    steel = tex("Steel_Mid.png", T.brushed_metal(256, 256, base=(110, 114, 122), var=18, seed=12))
    rbox(m, "Body", (0, 0.52, 0), (1.6, 1.04, 0.65), 0.008, 2, tile=1)
    for x in (-0.6, 0.0, 0.6):
        rbox(m, "Panel", (x, 0.52, -0.328), (0.5, 0.86, 0.01), 0.004, 2, tile=1)
    rbox(m, "Top", (0, 1.06, 0), (1.7, 0.04, 0.78), 0.008, 2, tile=1)
    rbox(m, "Chrome", (0, 0.1, -0.33), (1.55, 0.04, 0.02), 0.006, 2, tile=2)
    # vitre de protection + cadre
    rbox(m, "Glass", (0, 1.50, 0.12), (1.5, 0.78, 0.012), 0.002, 2, tile=1)
    rbox(m, "Frame", (0, 1.115, 0.12), (1.52, 0.03, 0.04), 0.006, 2, tile=2)
    rbox(m, "Frame", (0, 1.895, 0.12), (1.52, 0.03, 0.04), 0.006, 2, tile=2)
    for x in (-0.76, 0.76):
        rbox(m, "Frame", (x, 1.50, 0.12), (0.03, 0.81, 0.04), 0.006, 2, tile=2)
    rbox(m, "Brass", (0.0, 1.10, 0.02), (0.36, 0.012, 0.16), 0.004, 2, tile=2)
    rbox(m, "Frame", (-0.30, 1.50, 0.12), (0.04, 0.2, 0.05), 0.006, 2, tile=2)
    mats = {
        "Body": mat(ns=420, map_kd=dark), "Panel": mat(ns=380, map_kd=dark),
        "Top": mat(ns=820, map_kd=marble), "Chrome": mat((0.86, 0.87, 0.9), ns=800, pm=1.0),
        "Glass": mat((0.7, 0.85, 0.95), ns=950, d=0.22),
        "Frame": mat(ns=520, pm=0.9, map_kd=steel),
        "Brass": mat((0.82, 0.66, 0.30), ns=700, pm=1.0),
    }
    save("BankCounter", m, mats)


def build_chair():
    m = Mesh()
    leather = tex("Leather.png", T.leather(128, 128))
    for k in range(5):
        a = 2 * math.pi * k / 5
        end = (0.28 * math.cos(a), 0.06, 0.28 * math.sin(a))
        tube(m, "Base", (0, 0.10, 0), end, 0.018, 10)
        sphere(m, "Wheel", (end[0], 0.032, end[2]), 0.03, 10, 8)
    tube(m, "Base", (0, 0.10, 0), (0, 0.42, 0), 0.03, 14)
    rbox(m, "Seat", (0, 0.45, 0), (0.5, 0.07, 0.48), 0.03, 3, tile=2)
    rbox(m, "Back", (0, 0.75, 0.22), (0.46, 0.5, 0.06), 0.03, 3, tile=2)
    tube(m, "Base", (0, 0.45, 0.2), (0, 0.55, 0.22), 0.02, 10)
    for sx in (-1, 1):
        tube(m, "Base", (sx * 0.27, 0.48, -0.05), (sx * 0.27, 0.62, -0.05), 0.012, 8)
        rbox(m, "Pad", (sx * 0.27, 0.64, -0.03), (0.05, 0.03, 0.22), 0.01, 2, tile=2)
    mats = {"Base": mat((0.5, 0.52, 0.56), ns=650, pm=1.0), "Wheel": mat((0.05, 0.05, 0.06), ns=400),
            "Seat": mat(ns=480, map_kd=leather), "Back": mat(ns=480, map_kd=leather),
            "Pad": mat((0.05, 0.05, 0.06), ns=350)}
    save("BankOfficeChair", m, mats)


# ======================================================================
# ACCESSOIRES MÉTIERS
# ======================================================================

def build_rp():
    carbon = tex("Carbon.png", T.carbon(128, 128))
    med = tex("Medical_Cross.png", T.medical_texture())
    defib = tex("Defib.png", T.defib_texture())
    star = tex("Police_Star.png", T.police_star())
    town = tex("Town_Emblem.png", T.town_emblem())
    grille = tex("Radio_Grille.png", T.radio_grille())
    tabui = tex("Tablet_UI.png", T.tablet_ui())
    crate = tex("Wood_Crate.png", T.wood(256, 256, (190, 150, 96), (140, 102, 60), 6))
    stripes = tex("Awning.png", T.striped(128, 64))
    glow = tex("Screen_Glow.png", T.screen_glow())

    out = RPDIR
    shared = {}

    # --- ArmoryCase 0.36 x 0.131 x 0.22
    m = Mesh()
    rbox(m, "Case", (0, 0.0655, 0), (0.36, 0.131, 0.22), 0.012, 3, tile=3)
    rbox(m, "Seam", (0, 0.074, 0), (0.363, 0.004, 0.223), 0.002, 2)
    for sx in (-0.10, 0.10):
        rbox(m, "Latch", (sx, 0.074, -0.114), (0.04, 0.026, 0.01), 0.003, 2, tile=2)
    rbox(m, "Handle", (0, 0.139, 0), (0.1, 0.012, 0.022), 0.005, 2, tile=2)
    rbox(m, "Handle", (-0.05, 0.131, 0), (0.014, 0.016, 0.022), 0.003, 2, tile=2)
    rbox(m, "Handle", (0.05, 0.131, 0), (0.014, 0.016, 0.022), 0.003, 2, tile=2)
    for sx in (-1, 1):
        for sz in (-1, 1):
            rbox(m, "Corner", (sx * 0.172, 0.0655, sz * 0.102), (0.018, 0.133, 0.018), 0.004, 2, tile=2)
    save("ArmoryCase", m, {"Case": mat(ns=420, map_kd=carbon), "Seam": mat((0.03, 0.03, 0.035), ns=300),
                           "Latch": mat((0.85, 0.7, 0.3), ns=700, pm=1.0), "Handle": mat((0.1, 0.1, 0.12), ns=500),
                           "Corner": mat((0.55, 0.56, 0.6), ns=650, pm=1.0)}, out)

    # --- BusinessTablet 0.19 x 0.271 x 0.012
    m = Mesh()
    rbox(m, "Body", (0, 0.1355, 0), (0.19, 0.271, 0.012), 0.01, 3, tile=3)
    m.flat_quad("Screen", (-0.085, 0.017, -0.00615), (0.085, 0.017, -0.00615), (0.085, 0.254, -0.00615),
                (-0.085, 0.254, -0.00615), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    save("BusinessTablet", m, {"Body": mat((0.1, 0.11, 0.13), ns=650, pm=0.8),
                               "Screen": mat(ns=900, ke=(0.35, 0.45, 0.6), map_kd=tabui)}, out)

    # --- Defibrillator 0.22 x 0.181 x 0.09
    m = Mesh()
    rbox(m, {"-z": "Front", "+z": "Body", "+x": "Body", "-x": "Body", "+y": "Body", "-y": "Body"},
         (0, 0.075, 0), (0.22, 0.15, 0.09), 0.015, 3, uv=face_uv())
    rbox(m, "Handle", (0, 0.166, 0), (0.1, 0.03, 0.022), 0.008, 2, tile=2)
    for sx in (-1, 1):
        rbox(m, "Paddle", (sx * 0.085, 0.09, -0.052), (0.05, 0.075, 0.02), 0.008, 2, tile=2)
        tube(m, "Cable", (sx * 0.085, 0.05, -0.052), (sx * 0.085, 0.02, -0.08), 0.004, 8)
    m.flat_quad("Screen", (-0.03, 0.105, -0.0452), (0.03, 0.105, -0.0452), (0.03, 0.13, -0.0452),
                (-0.03, 0.13, -0.0452), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    save("Defibrillator", m, {"Front": mat(ns=520, map_kd=defib), "Body": mat((0.9, 0.78, 0.1), ns=520),
                              "Handle": mat((0.1, 0.1, 0.12), ns=500), "Paddle": mat((0.08, 0.08, 0.1), ns=450),
                              "Cable": mat((0.02, 0.02, 0.02), ns=300),
                              "Screen": mat((0.1, 0.6, 0.3), ns=800, ke=(0.1, 0.7, 0.3), map_kd=glow)}, out)

    # --- MedicalKit 0.25 x 0.161 x 0.12
    m = Mesh()
    rbox(m, {"-z": "Cross", "+z": "Cross", "+x": "Body", "-x": "Body", "+y": "Body", "-y": "Body"},
         (0, 0.0715, 0), (0.25, 0.143, 0.12), 0.012, 3, uv=face_uv())
    rbox(m, "Handle", (0, 0.152, 0), (0.1, 0.018, 0.02), 0.006, 2, tile=2)
    for sx in (-0.08, 0.08):
        rbox(m, "Latch", (sx, 0.09, -0.062), (0.03, 0.024, 0.008), 0.003, 2, tile=2)
    save("MedicalKit", m, {"Cross": mat(ns=520, map_kd=med), "Body": mat((0.95, 0.95, 0.95), ns=520),
                           "Handle": mat((0.15, 0.15, 0.17), ns=500),
                           "Latch": mat((0.7, 0.72, 0.76), ns=650, pm=1.0)}, out)

    # --- MobileStand 0.55 x 0.481 x 0.36
    m = Mesh()
    rbox(m, "Top", (0, 0.28, 0), (0.55, 0.02, 0.36), 0.004, 2, tile=3)
    for sx in (-1, 1):
        for sz in (-1, 1):
            tube(m, "Leg", (sx * 0.25, 0.0, sz * 0.15), (sx * 0.25, 0.27, sz * 0.15), 0.011, 8)
    for sx in (-1, 1):
        tube(m, "Leg", (sx * 0.265, 0.27, 0.165), (sx * 0.265, 0.455, 0.165), 0.008, 8)
        tube(m, "Leg", (sx * 0.265, 0.27, -0.165), (sx * 0.265, 0.455, -0.165), 0.008, 8)
    double_quad(m, "Awning", (-0.28, 0.455, -0.19), (0.28, 0.455, -0.19), (0.28, 0.481, 0.0), (-0.28, 0.481, 0.0),
                ((0, 0), (1, 0), (1, 1), (0, 1)))
    double_quad(m, "Awning", (-0.28, 0.481, 0.0), (0.28, 0.481, 0.0), (0.28, 0.455, 0.19), (-0.28, 0.455, 0.19),
                ((0, 0), (1, 0), (1, 1), (0, 1)))
    save("MobileStand", m, {"Top": mat(ns=420, map_kd=crate), "Leg": mat((0.5, 0.52, 0.56), ns=650, pm=1.0),
                            "Awning": mat(ns=150, map_kd=stripes)}, out)

    # --- PoliceBadge 0.09 x 0.111 x 0.008
    m = Mesh()
    shield = [(0.0, -0.0555), (0.03, -0.04), (0.045, -0.005), (0.045, 0.035), (0.03, 0.0555), (0.0, 0.045),
              (-0.03, 0.0555), (-0.045, 0.035), (-0.045, -0.005), (-0.03, -0.04)]
    polygon_prism(m, "Front", "Back", "Rim", shield, 0.008, (0, 0.0555, 0))
    tube(m, "Pin", (0.0, 0.05, 0.004), (0.0, 0.05, 0.012), 0.002, 6)
    save("PoliceBadge", m, {"Front": mat(ns=700, pm=0.9, map_kd=star), "Back": mat((0.7, 0.58, 0.25), ns=600, pm=1.0),
                            "Rim": mat((0.8, 0.66, 0.28), ns=700, pm=1.0), "Pin": mat((0.7, 0.7, 0.72), ns=600, pm=1.0)},
         out)

    # --- TownBadge 0.08 x 0.101 x 0.008
    m = Mesh()
    ell = [(0.04 * math.cos(a), 0.0505 * math.sin(a)) for a in [2 * math.pi * k / 32 for k in range(32)]]
    polygon_prism(m, "Front", "Back", "Rim", ell, 0.008, (0, 0.0505, 0))
    save("TownBadge", m, {"Front": mat(ns=700, pm=0.8, map_kd=town), "Back": mat((0.7, 0.58, 0.25), ns=600, pm=1.0),
                          "Rim": mat((0.8, 0.66, 0.28), ns=700, pm=1.0)}, out)

    # --- PoliceCuffs 0.12 x 0.045 x 0.012
    m = Mesh()
    for sx in (-1, 1):
        torus(m, "Steel", (sx * 0.0425, 0.0225, 0), 'z', 0.0175, 0.0045, 20, 6)
        rbox(m, "Steel", (sx * 0.0425 + sx * 0.0, 0.0445, 0), (0.014, 0.008, 0.009), 0.002, 1, tile=3)
    for k in range(3):
        cx = -0.0135 + k * 0.0135
        if k % 2 == 0:
            torus(m, "Steel", (cx, 0.0225, 0), 'y', 0.0045, 0.0015, 14, 6)
        else:
            torus(m, "Steel", (cx, 0.0225, 0), 'z', 0.0045, 0.0015, 14, 6)
    save("PoliceCuffs", m, {"Steel": mat((0.78, 0.8, 0.84), ns=720, pm=1.0)}, out)

    # --- PoliceRadio 0.07 x 0.131 x 0.035
    m = Mesh()
    rbox(m, "Body", (0, 0.047, 0), (0.07, 0.094, 0.035), 0.008, 3, tile=3)
    tube(m, "Antenna", (0.022, 0.094, 0), (0.022, 0.131, 0), 0.0045, 8)
    rbox(m, "Button", (0.0365, 0.065, 0), (0.008, 0.032, 0.018), 0.003, 2, tile=3)
    rbox(m, "Knob", (-0.018, 0.098, 0), (0.016, 0.01, 0.016), 0.003, 2, tile=3)
    m.flat_quad("Screen", (-0.025, 0.07, -0.0178), (0.025, 0.07, -0.0178), (0.025, 0.088, -0.0178),
                (-0.025, 0.088, -0.0178), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    m.flat_quad("Grille", (-0.025, 0.012, -0.0178), (0.025, 0.012, -0.0178), (0.025, 0.056, -0.0178),
                (-0.025, 0.056, -0.0178), (0, 0), (1, 0), (1, 1), (0, 1), n=(0, 0, -1))
    save("PoliceRadio", m, {"Body": mat((0.07, 0.08, 0.1), ns=550), "Antenna": mat((0.04, 0.04, 0.05), ns=400),
                            "Button": mat((0.8, 0.15, 0.12), ns=500), "Knob": mat((0.2, 0.2, 0.22), ns=500),
                            "Screen": mat((0.2, 0.8, 0.4), ns=800, ke=(0.15, 0.7, 0.3), map_kd=glow),
                            "Grille": mat(ns=250, map_kd=grille)}, out)

    # --- ShopCrate 0.32 x 0.251 x 0.25
    m = Mesh()
    for sx in (-1, 1):
        for sz in (-1, 1):
            rbox(m, "Post", (sx * 0.145, 0.1255, sz * 0.115), (0.03, 0.251, 0.02), 0.002, 1, tile=3)
    for k in range(3):
        y = 0.045 + k * 0.08
        for sz in (-1, 1):
            rbox(m, "Slat", (0, y, sz * 0.12), (0.32, 0.06, 0.01), 0.002, 1, tile=3)
        for sx in (-1, 1):
            rbox(m, "Slat", (sx * 0.155, y, 0), (0.01, 0.06, 0.24), 0.002, 1, tile=3)
    rbox(m, "Slat", (0, 0.012, 0), (0.30, 0.012, 0.24), 0.002, 1, tile=3)
    save("ShopCrate", m, {"Post": mat(ns=250, map_kd=crate), "Slat": mat(ns=220, map_kd=crate)}, out)

    # --- TrashBag 0.18 x 0.24 x 0.12
    m = Mesh()
    sphere(m, "Plastic", (0, 0.10, 0), 1.0, 22, 14, (0.09, 0.10, 0.06))
    tube(m, "Plastic", (0, 0.19, 0), (0, 0.235, 0), 0.016, 10, True, 0.007)
    sphere(m, "Plastic", (0, 0.236, 0), 0.012, 8, 6)
    save("TrashBag", m, {"Plastic": mat((0.05, 0.05, 0.06), ns=720)}, out)

    # --- TrashGrabber 0.035 x 0.48 x 0.035
    m = Mesh()
    tube(m, "Pole", (0, 0.06, 0), (0, 0.45, 0), 0.0085, 10)
    rbox(m, "Grip", (0, 0.03, 0), (0.035, 0.07, 0.03), 0.01, 2, tile=3)
    rbox(m, "Trigger", (0, 0.045, -0.0215), (0.012, 0.03, 0.008), 0.003, 2, tile=3)
    rbox(m, "Jaw", (-0.0125, 0.468, 0), (0.01, 0.024, 0.016), 0.003, 2, tile=3)
    rbox(m, "Jaw", (0.0125, 0.468, 0), (0.01, 0.024, 0.016), 0.003, 2, tile=3)
    rbox(m, "Pad", (-0.0125, 0.478, 0), (0.012, 0.004, 0.018), 0.001, 2, tile=3)
    rbox(m, "Pad", (0.0125, 0.478, 0), (0.012, 0.004, 0.018), 0.001, 2, tile=3)
    save("TrashGrabber", m, {"Pole": mat((0.85, 0.7, 0.1), ns=600, pm=0.3), "Grip": mat((0.1, 0.1, 0.12), ns=450),
                             "Trigger": mat((0.8, 0.15, 0.12), ns=450), "Jaw": mat((0.6, 0.62, 0.66), ns=600, pm=1.0),
                             "Pad": mat((0.05, 0.05, 0.05), ns=300)}, out)


# ======================================================================

def main():
    common_textures()
    for d in NOTE_SIZES:
        build_note(d)
        build_band(d)
    for cid, spec in COINS.items():
        build_coin(cid, spec)
    build_card()
    build_atm()
    build_vault()
    build_door()
    build_wall_safe()
    build_desk()
    build_counter()
    build_chair()
    build_rp()

    # --- textures : écriture
    for name, img in TEXTURES.items():
        img.save(os.path.join(MONEY, name), optimize=True)

    # --- matériau RP partagé : le RP_Props.mtl historique est remplacé par un .mtl par modèle
    legacy = os.path.join(RPDIR, "RP_Props.mtl")
    if os.path.exists(legacy):
        os.remove(legacy)

    # --- contrôles qualité
    bad = 0
    total = 0
    for name, mesh in MESHES.items():
        total += mesh.tri_count()
        for mat_ in mesh.order:
            for (a, b, c) in mesh.tris[mat_]:
                cr = cross(sub(b.p, a.p), sub(c.p, a.p))
                if dot(cr, add(add(a.n, b.n), c.n)) > 0:
                    bad += 1
    print("%-26s %7s %7s  %s" % ("modèle", "tris", "verts", "dimensions (m)"))
    for name, tris, nv, size in REPORT:
        print("%-26s %7d %7d  %s" % (name, tris, nv, size))
    print("\nTotal : %d modèles, %d triangles, %d textures, triangles mal orientés : %d" % (
        len(MESHES), total, len(TEXTURES), bad))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
