#!/usr/bin/env python3
"""Textures procédurales (PIL + numpy) pour les modèles Fusion DarkRP."""
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

FONT_BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
FONT_REG = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
FONT_SERIF = "/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf"
FONT_MONO = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf"


def font(size, kind="bold"):
    path = {"bold": FONT_BOLD, "reg": FONT_REG, "serif": FONT_SERIF, "mono": FONT_MONO}[kind]
    return ImageFont.truetype(path, int(size))


def rng(seed):
    return np.random.default_rng(seed)


def noise(w, h, cells, seed, octaves=3):
    """Bruit de valeurs lisse dans [0,1]."""
    r = rng(seed)
    acc = np.zeros((h, w), dtype=np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        cw, ch = max(2, int(cells * (2 ** o))), max(2, int(cells * (2 ** o) * h / w))
        base = r.random((ch, cw)).astype(np.float32)
        img = Image.fromarray((base * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
        acc += np.asarray(img, dtype=np.float32) / 255.0 * amp
        total += amp
        amp *= 0.5
    return acc / total


def to_img(arr_rgb):
    return Image.fromarray(np.clip(arr_rgb, 0, 255).astype(np.uint8), "RGB")


def tint(c, k):
    return tuple(max(0, min(255, int(v * k))) for v in c)


def mix(a, b, t):
    return tuple(int(a[i] * (1 - t) + b[i] * t) for i in range(3))


# ----------------------------------------------------------------------
# Matières
# ----------------------------------------------------------------------

def brushed_metal(w, h, base=(120, 124, 130), var=22, seed=1, vertical=False, grain=0.5):
    r = rng(seed)
    if vertical:
        w, h = h, w
    rows = r.normal(0, 1, (h, 1)).astype(np.float32)
    streak = np.repeat(rows, w, axis=1)
    fine = r.normal(0, 1, (h, w)).astype(np.float32)
    img = Image.fromarray(np.clip(128 + streak * 40 + fine * 25, 0, 255).astype(np.uint8))
    img = img.filter(ImageFilter.GaussianBlur(radius=(0.2, 6.0)[0])) if False else img
    a = np.asarray(img, dtype=np.float32)
    # lissage horizontal pour l'effet brossé
    k = 15
    ker = np.ones(k, dtype=np.float32) / k
    a = np.apply_along_axis(lambda x: np.convolve(x, ker, mode="same"), 1, a)
    n = (a - 128) / 128.0
    big = noise(w, h, 3, seed + 7) - 0.5
    out = np.stack([np.clip(base[i] + n * var + big * var * 0.6 * grain, 0, 255) for i in range(3)], axis=-1)
    im = to_img(out)
    return im.transpose(Image.ROTATE_90) if vertical else im


def wood(w, h, base=(150, 100, 62), dark=(96, 60, 34), seed=3):
    r = rng(seed)
    y = np.linspace(0, 1, h, dtype=np.float32)[:, None]
    x = np.linspace(0, 1, w, dtype=np.float32)[None, :]
    warp = noise(w, h, 4, seed, 3) * 3.0
    rings = np.sin((y * 26 + warp * 2.0) * math.pi + x * 0.8)
    rings = (rings * 0.5 + 0.5) ** 1.6
    fine = noise(w, h, 40, seed + 5, 2)
    t = np.clip(rings * 0.65 + fine * 0.35, 0, 1)[..., None]
    out = np.array(base, dtype=np.float32) * (1 - t) + np.array(dark, dtype=np.float32) * t
    return to_img(out)


def marble(w, h, seed=5, base=(228, 228, 232), vein=(120, 122, 135)):
    n = noise(w, h, 5, seed, 5)
    v = np.abs(np.sin((n * 8.0 + np.linspace(0, 2, w)[None, :]) * math.pi))
    v = (1 - v) ** 6
    out = np.array(base, dtype=np.float32) * (1 - v[..., None]) + np.array(vein, dtype=np.float32) * v[..., None]
    out += (noise(w, h, 60, seed + 1, 2)[..., None] - 0.5) * 10
    return to_img(out)


def carbon(w, h, base=(30, 32, 36), seed=4):
    n = noise(w, h, 80, seed, 2)
    out = np.stack([np.clip(base[i] + (n - 0.5) * 24, 0, 255) for i in range(3)], axis=-1)
    return to_img(out)


def leather(w, h, base=(36, 30, 28), seed=8):
    n = noise(w, h, 90, seed, 3)
    out = np.stack([np.clip(base[i] + (n - 0.5) * 30, 0, 255) for i in range(3)], axis=-1)
    return to_img(out)


def fabric(w, h, base=(60, 64, 78), seed=9):
    r = rng(seed)
    yy, xx = np.mgrid[0:h, 0:w]
    weave = ((xx // 2 + yy // 2) % 2).astype(np.float32)
    n = noise(w, h, 50, seed, 2)
    out = np.stack([np.clip(base[i] + weave * 10 + (n - 0.5) * 16, 0, 255) for i in range(3)], axis=-1)
    return to_img(out)


def plain(w, h, color):
    return Image.new("RGB", (w, h), color)


def hazard(w, h, c1=(240, 200, 20), c2=(25, 25, 25), stripe=36):
    img = Image.new("RGB", (w, h), c2)
    d = ImageDraw.Draw(img)
    for i in range(-h, w + h, stripe * 2):
        d.polygon([(i, h), (i + stripe, h), (i + stripe + h, 0), (i + h, 0)], fill=c1)
    return img


def with_rivets(img, step=64, color=(70, 72, 78), r=3, margin=14):
    d = ImageDraw.Draw(img)
    w, h = img.size
    for x in range(margin, w - margin + 1, step):
        for y in (margin, h - margin):
            d.ellipse([x - r, y - r, x + r, y + r], fill=color)
    for y in range(margin + step, h - margin, step):
        for x in (margin, w - margin):
            d.ellipse([x - r, y - r, x + r, y + r], fill=color)
    return img


# ----------------------------------------------------------------------
# Billets
# ----------------------------------------------------------------------

NOTE_COLORS = {
    5: ((150, 154, 150), (88, 96, 92)),
    10: ((222, 112, 104), (140, 52, 48)),
    20: ((120, 168, 224), (44, 80, 140)),
    50: ((238, 166, 100), (150, 82, 30)),
    100: ((140, 200, 140), (46, 110, 56)),
}


def _guilloche(d, box, color, seed, lines=18, alpha_color=None):
    x0, y0, x1, y1 = box
    r = rng(seed)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    for i in range(lines):
        ph = r.random() * math.tau
        fr = 2 + r.random() * 4
        amp = (y1 - y0) * (0.1 + r.random() * 0.25)
        pts = []
        for xx in range(int(x0), int(x1), 3):
            t = (xx - x0) / (x1 - x0)
            pts.append((xx, cy + math.sin(t * math.tau * fr + ph) * amp * math.sin(t * math.pi)))
        if len(pts) > 1:
            d.line(pts, fill=color, width=1)


def note_texture(denom):
    light, dark = NOTE_COLORS[denom]
    W, H = 512, 512
    img = Image.new("RGB", (W, H), tint(light, 1.0))
    # papier : bruit fin
    n = noise(W, H, 70, denom, 2)
    arr = np.asarray(img, dtype=np.float32)
    arr = arr + (n[..., None] - 0.5) * 14
    img = to_img(arr)
    d = ImageDraw.Draw(img, "RGBA")
    fnt_big, fnt_mid, fnt_s = font(118), font(26), font(15, "mono")

    for side in (0, 1):
        y0 = 0 if side == 0 else 256
        # fond dégradé horizontal
        for x in range(W):
            t = x / (W - 1)
            col = mix(light, tint(light, 0.88), t * 0.6) if side == 0 else mix(tint(light, 0.96), light, t)
            d.line([(x, y0), (x, y0 + 255)], fill=col + (255,))
        # guillochis
        for k in range(3):
            _guilloche(d, (10, y0 + 12, W - 10, y0 + 244), dark + (55,), denom * 10 + k + side * 100, 16)
        # cadre
        d.rectangle([8, y0 + 8, W - 9, y0 + 247], outline=dark + (200,), width=3)
        d.rectangle([16, y0 + 16, W - 17, y0 + 239], outline=dark + (110,), width=1)
        if side == 0:
            # portail + arc
            d.rectangle([60, y0 + 96, 150, y0 + 226], outline=dark + (210,), width=4)
            d.pieslice([60, y0 + 56, 150, y0 + 136], 180, 360, outline=dark + (210,), width=4)
            d.rectangle([84, y0 + 150, 126, y0 + 226], fill=dark + (140,))
            # étoiles
            cx, cy = 395, y0 + 128
            for k in range(12):
                a = math.tau * k / 12
                x, y = cx + 78 * math.cos(a), cy + 78 * math.sin(a)
                d.polygon([(x, y - 7), (x + 2, y - 2), (x + 7, y - 2), (x + 3, y + 2), (x + 5, y + 7),
                           (x, y + 4), (x - 5, y + 7), (x - 3, y + 2), (x - 7, y - 2), (x - 2, y - 2)],
                          fill=dark + (210,))
            d.ellipse([cx - 52, cy - 52, cx + 52, cy + 52], outline=dark + (120,), width=2)
            # valeur
            d.text((176, y0 + 66), str(denom), font=fnt_big, fill=dark + (235,))
            d.text((30, y0 + 24), "EURO", font=fnt_mid, fill=dark + (230,))
            d.text((200, y0 + 22), "\u20ac", font=font(30), fill=dark + (230,))
            d.text((300, y0 + 214), "X%011d" % (denom * 1234567 % 99999999999), font=fnt_s, fill=dark + (220,))
            # bande holographique
            for yy in range(y0 + 22, y0 + 234):
                t = (yy - y0) / 255.0
                col = (int(180 + 70 * math.sin(t * 18)), int(180 + 70 * math.sin(t * 18 + 2)),
                       int(190 + 60 * math.sin(t * 18 + 4)))
                d.line([(462, yy), (484, yy)], fill=col + (200,))
        else:
            # ponts
            for k in range(5):
                x = 40 + k * 90
                d.arc([x, y0 + 130, x + 100, y0 + 220], 180, 360, fill=dark + (200,), width=5)
            d.line([(30, y0 + 175), (480, y0 + 175)], fill=dark + (200,), width=4)
            # carte stylisée
            pts = [(360, y0 + 40), (410, y0 + 34), (440, y0 + 60), (430, y0 + 96), (385, y0 + 110),
                   (350, y0 + 86)]
            d.polygon(pts, outline=dark + (200,), fill=dark + (60,))
            d.text((36, y0 + 36), str(denom), font=font(78), fill=dark + (230,))
            d.text((300, y0 + 196), "EURO", font=font(24), fill=dark + (230,))
    return img.filter(ImageFilter.GaussianBlur(0.4))


def strap_texture(denom):
    light, dark = NOTE_COLORS[denom]
    W, H = 128, 64
    img = Image.new("RGB", (W, H), (236, 232, 218))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, W - 1, 10], fill=dark)
    d.rectangle([0, H - 11, W - 1, H - 1], fill=dark)
    d.text((10, 16), "%d x %d EUR" % (100, denom), font=font(15), fill=(40, 40, 40))
    d.text((10, 36), "BANQUE", font=font(11, "reg"), fill=(80, 80, 80))
    return img


def paper_edge(w=64, h=64):
    img = Image.new("RGB", (w, h), (238, 236, 226))
    d = ImageDraw.Draw(img)
    r = rng(2)
    for y in range(0, h, 2):
        v = int(214 + r.random() * 22)
        d.line([(0, y), (w, y)], fill=(v, v - 2, v - 10))
    return img


# ----------------------------------------------------------------------
# Pièces
# ----------------------------------------------------------------------

def coin_texture(label, kind):
    W = 256
    if kind == "copper":
        base, rim, ink = (178, 100, 66), (150, 82, 52), (110, 58, 34)
    elif kind == "gold":
        base, rim, ink = (222, 184, 98), (200, 160, 80), (150, 112, 40)
    else:
        base, rim, ink = (198, 202, 208), (172, 176, 184), (120, 124, 132)
    img = Image.new("RGB", (W, W), base)
    d = ImageDraw.Draw(img)
    n = noise(W, W, 30, hash(label) % 1000, 2)
    arr = np.asarray(img, dtype=np.float32) + (n[..., None] - 0.5) * 20
    img = to_img(arr)
    d = ImageDraw.Draw(img)
    c = W / 2
    if kind == "bimetal_gold_out":
        d.ellipse([0, 0, W, W], fill=(222, 184, 98))
        d.ellipse([c - 0.62 * c, c - 0.62 * c, c + 0.62 * c, c + 0.62 * c], fill=(198, 202, 208))
    d.ellipse([4, 4, W - 4, W - 4], outline=rim, width=8)
    for k in range(12):
        a = math.tau * k / 12
        x, y = c + 0.82 * c * math.cos(a), c + 0.82 * c * math.sin(a)
        d.ellipse([x - 3, y - 3, x + 3, y + 3], fill=ink)
    f = font(120 if len(label) < 3 else 78)
    bb = d.textbbox((0, 0), label, font=f)
    d.text((c - (bb[2] - bb[0]) / 2 - bb[0], c - (bb[3] - bb[1]) / 2 - bb[1] - 6), label, font=f, fill=ink)
    unit = "CENT" if label.endswith("c") else "EURO"
    fb = font(26)
    bb = d.textbbox((0, 0), unit, font=fb)
    d.text((c - (bb[2] - bb[0]) / 2, c + 62), unit, font=fb, fill=ink)
    return img


# ----------------------------------------------------------------------
# Carte bancaire
# ----------------------------------------------------------------------

def card_texture():
    W, H = 512, 640
    img = Image.new("RGB", (W, H), (12, 20, 40))
    d = ImageDraw.Draw(img, "RGBA")
    # --- face avant (haut 512x320) ---
    for y in range(320):
        t = y / 319.0
        col = mix((16, 52, 112), (8, 120, 140), t)
        d.line([(0, y), (W, y)], fill=col)
    for k in range(14):
        pts = [(x, 40 + 20 * k + 14 * math.sin(x / 55 + k)) for x in range(0, W, 6)]
        d.line(pts, fill=(255, 255, 255, 18), width=2)
    d.ellipse([-100, 150, 280, 530][:2] + [280, 330], fill=(255, 255, 255, 10))
    d.rounded_rectangle([46, 118, 126, 184], radius=10, fill=(214, 178, 92), outline=(150, 116, 40), width=2)
    for x in (62, 86, 110):
        d.line([(x, 118), (x, 184)], fill=(150, 116, 40), width=2)
    for y in (140, 162):
        d.line([(46, y), (126, y)], fill=(150, 116, 40), width=2)
    d.text((40, 32), "FUSION BANK", font=font(38), fill=(255, 255, 255, 240))
    d.text((40, 76), "BANQUE DARKRP", font=font(15, "reg"), fill=(200, 230, 255, 220))
    # sans contact
    for r in (14, 24, 34):
        d.arc([150 - r, 150 - r, 150 + r, 150 + r], -50, 50, fill=(255, 255, 255, 220), width=4)
    d.text((40, 218), "4532  0151  1283  0366", font=font(32, "mono"), fill=(250, 250, 250, 245))
    d.text((40, 268), "VALID THRU 12/29", font=font(15, "mono"), fill=(220, 235, 250, 230))
    d.text((40, 290), "TITULAIRE", font=font(20), fill=(255, 255, 255, 235))
    d.ellipse([396, 252, 456, 312], outline=(255, 170, 60, 230), width=7)
    d.ellipse([430, 252, 490, 312], outline=(255, 90, 90, 230), width=7)
    # --- face arrière (bas 512x320) ---
    d.rectangle([0, 320, W, 639], fill=(214, 216, 222))
    d.rectangle([0, 350, W, 410], fill=(14, 14, 16))
    d.rectangle([30, 440, 340, 490], fill=(250, 250, 250), outline=(120, 120, 120))
    for x in range(34, 336, 10):
        d.line([(x, 442), (x + 20, 488)], fill=(205, 208, 214), width=2)
    d.rectangle([350, 440, 450, 490], fill=(255, 255, 255), outline=(120, 120, 120))
    d.text((366, 450), "123", font=font(28, "mono"), fill=(20, 20, 20))
    d.text((30, 520), "Carte reservee a un usage RP - Fusion DarkRP", font=font(14, "reg"), fill=(70, 70, 80))
    d.text((30, 546), "Service client : 0 800 000 000", font=font(14, "reg"), fill=(70, 70, 80))
    return img


# ----------------------------------------------------------------------
# Enseignes / DAB / coffres
# ----------------------------------------------------------------------

def atm_sign():
    W, H = 512, 96
    img = Image.new("RGB", (W, H), (14, 40, 92))
    d = ImageDraw.Draw(img, "RGBA")
    for y in range(H):
        d.line([(0, y), (W, y)], fill=mix((20, 64, 140), (8, 26, 70), y / H) + (255,))
    d.rectangle([4, 4, W - 5, H - 5], outline=(120, 190, 255, 180), width=3)
    d.text((26, 18), "FUSION BANK", font=font(46), fill=(255, 255, 255, 255))
    d.text((366, 30), "24/7", font=font(34), fill=(255, 220, 120, 255))
    return img


def atm_screen_dark():
    W, H = 128, 256
    img = Image.new("RGB", (W, H), (6, 18, 40))
    d = ImageDraw.Draw(img, "RGBA")
    for y in range(H):
        d.line([(0, y), (W, y)], fill=mix((8, 30, 70), (3, 10, 28), y / H) + (255,))
    return img


def keypad_atlas():
    """Atlas 256x256 : 4 colonnes x 4 lignes de touches (64 px)."""
    img = Image.new("RGB", (256, 256), (40, 42, 48))
    d = ImageDraw.Draw(img, "RGBA")
    labels = ["1", "2", "3", "OK", "4", "5", "6", "<", "7", "8", "9", "X", "*", "0", "#", "C"]
    colors = {"OK": (40, 150, 70), "<": (200, 160, 40), "X": (190, 50, 50), "C": (190, 50, 50)}
    for i, lab in enumerate(labels):
        cx, cy = (i % 4) * 64, (i // 4) * 64
        col = colors.get(lab, (210, 214, 220))
        d.rounded_rectangle([cx + 3, cy + 3, cx + 60, cy + 60], radius=9, fill=col, outline=(30, 30, 34), width=2)
        d.rectangle([cx + 6, cy + 6, cx + 57, cy + 24], fill=(255, 255, 255, 40))
        f = font(34 if len(lab) == 1 else 26)
        bb = d.textbbox((0, 0), lab, font=f)
        d.text((cx + 32 - (bb[2] - bb[0]) / 2 - bb[0], cy + 32 - (bb[3] - bb[1]) / 2 - bb[1]), lab, font=f,
               fill=(255, 255, 255) if lab in colors else (30, 32, 38))
    return img


def fascia_texture(seed=21):
    """Façade ATM 512x512 : acier foncé brossé + sérigraphie."""
    img = brushed_metal(512, 512, base=(58, 62, 70), var=14, seed=seed)
    d = ImageDraw.Draw(img, "RGBA")
    d.text((20, 480), "RETIREZ VOTRE CARTE", font=font(14, "reg"), fill=(220, 220, 230, 200))
    return img


def card_slot_plate():
    W, H = 256, 128
    img = Image.new("RGB", (W, H), (30, 32, 38))
    d = ImageDraw.Draw(img, "RGBA")
    d.rounded_rectangle([4, 4, W - 5, H - 5], radius=10, outline=(150, 160, 175, 220), width=3)
    d.rounded_rectangle([20, 52, W - 20, 76], radius=6, fill=(4, 4, 6))
    d.rounded_rectangle([20, 52, W - 20, 58], radius=3, fill=(60, 220, 120, 220))
    d.rounded_rectangle([20, 90, 86, 112], radius=4, outline=(200, 210, 225, 230), width=2)
    d.text((98, 90), "CARTE", font=font(18), fill=(220, 230, 245, 240))
    d.text((20, 18), "INSEREZ VOTRE CARTE", font=font(15), fill=(220, 230, 245, 230))
    return img


def cash_slot_plate():
    W, H = 512, 128
    img = Image.new("RGB", (W, H), (28, 30, 36))
    d = ImageDraw.Draw(img, "RGBA")
    d.rounded_rectangle([4, 4, W - 5, H - 5], radius=10, outline=(150, 160, 175, 220), width=3)
    d.rounded_rectangle([40, 56, W - 40, 88], radius=6, fill=(4, 4, 6))
    d.rectangle([40, 50, W - 40, 56], fill=(255, 210, 80, 230))
    d.text((40, 14), "RETRAIT ET DEPOT D'ESPECES", font=font(20), fill=(230, 235, 245, 240))
    return img


def vent_texture():
    img = Image.new("RGB", (128, 128), (24, 26, 30))
    d = ImageDraw.Draw(img)
    for y in range(8, 120, 10):
        d.rounded_rectangle([12, y, 116, y + 5], radius=2, fill=(8, 8, 10))
    return img


def safe_dial():
    W = 256
    img = Image.new("RGB", (W, W), (200, 204, 210))
    d = ImageDraw.Draw(img, "RGBA")
    c = W / 2
    d.ellipse([2, 2, W - 2, W - 2], fill=(40, 42, 48))
    d.ellipse([14, 14, W - 14, W - 14], fill=(210, 212, 218))
    for k in range(100):
        a = math.tau * k / 100 - math.pi / 2
        r0 = 0.84 * c if k % 5 else 0.78 * c
        d.line([(c + r0 * math.cos(a), c + r0 * math.sin(a)),
                (c + 0.92 * c * math.cos(a), c + 0.92 * c * math.sin(a))], fill=(20, 20, 24), width=2)
        if k % 10 == 0:
            f = font(20)
            t = str(k)
            bb = d.textbbox((0, 0), t, font=f)
            x, y = c + 0.64 * c * math.cos(a), c + 0.64 * c * math.sin(a)
            d.text((x - (bb[2] - bb[0]) / 2, y - (bb[3] - bb[1]) / 2 - 2), t, font=f, fill=(20, 20, 24))
    d.ellipse([c - 0.4 * c, c - 0.4 * c, c + 0.4 * c, c + 0.4 * c], fill=(150, 154, 162))
    d.polygon([(c, 6), (c - 8, 30), (c + 8, 30)], fill=(200, 40, 40))
    return img


def vault_label():
    W, H = 512, 128
    img = brushed_metal(W, H, base=(150, 130, 70), var=18, seed=5)
    d = ImageDraw.Draw(img, "RGBA")
    d.rectangle([4, 4, W - 5, H - 5], outline=(70, 56, 20, 255), width=4)
    d.text((24, 26), "FUSION BANK", font=font(50), fill=(60, 44, 14, 255))
    d.text((24, 88), "CHAMBRE FORTE - ACCES RESTREINT", font=font(16), fill=(60, 44, 14, 255))
    return img


def door_panel():
    W, H = 512, 1024
    img = brushed_metal(W, H, base=(112, 118, 126), var=16, seed=31, vertical=True)
    d = ImageDraw.Draw(img, "RGBA")
    d.rectangle([12, 12, W - 13, H - 13], outline=(60, 64, 70, 255), width=6)
    d.rectangle([40, 40, W - 41, H - 41], outline=(70, 74, 82, 255), width=3)
    img = with_rivets(img, 80, (60, 62, 68), 5, 26)
    d = ImageDraw.Draw(img, "RGBA")
    # bandeau de danger
    d.rectangle([60, 940, W - 60, 990], fill=(240, 200, 20))
    for i in range(60, W - 60, 40):
        d.polygon([(i, 990), (i + 20, 990), (i + 40, 940), (i + 20, 940)], fill=(25, 25, 25))
    d.text((150, 120), "ZONE SECURISEE", font=font(38), fill=(230, 230, 235, 255))
    d.text((120, 176), "ACCES AUTORISE UNIQUEMENT", font=font(22, "reg"), fill=(230, 230, 235, 230))
    return img


def status_panel(text="ACCES", color=(40, 200, 90)):
    W, H = 256, 128
    img = Image.new("RGB", (W, H), (6, 8, 12))
    d = ImageDraw.Draw(img, "RGBA")
    d.rounded_rectangle([4, 4, W - 5, H - 5], radius=8, outline=(120, 130, 150, 255), width=3)
    f = font(38)
    bb = d.textbbox((0, 0), text, font=f)
    d.text((W / 2 - (bb[2] - bb[0]) / 2, H / 2 - (bb[3] - bb[1]) / 2 - 4), text, font=f, fill=color + (255,))
    return img


def medical_texture():
    W = 256
    img = Image.new("RGB", (W, W), (244, 244, 244))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([6, 6, W - 7, W - 7], radius=18, outline=(200, 30, 40), width=8)
    d.rectangle([W / 2 - 22, 52, W / 2 + 22, W - 52], fill=(200, 30, 40))
    d.rectangle([52, W / 2 - 22, W - 52, W / 2 + 22], fill=(200, 30, 40))
    return img


def defib_texture():
    W = 256
    img = Image.new("RGB", (W, W), (230, 200, 30))
    d = ImageDraw.Draw(img, "RGBA")
    d.rounded_rectangle([8, 8, W - 9, W - 9], radius=16, outline=(30, 30, 30, 255), width=6)
    d.polygon([(128, 190), (60, 110), (60, 70), (98, 56), (128, 84), (158, 56), (196, 70), (196, 110)],
              fill=(200, 30, 40, 255))
    d.line([(70, 130), (100, 130), (112, 100), (132, 160), (146, 120), (186, 120)], fill=(255, 255, 255, 255), width=6)
    d.text((70, 210), "DEFIBRILLATEUR", font=font(18), fill=(30, 30, 30, 255))
    return img


def police_star():
    W = 256
    img = Image.new("RGB", (W, W), (196, 170, 70))
    d = ImageDraw.Draw(img, "RGBA")
    c = W / 2
    pts = []
    for k in range(10):
        a = -math.pi / 2 + math.pi * k / 5
        r = 0.46 * W if k % 2 == 0 else 0.2 * W
        pts.append((c + r * math.cos(a), c + r * math.sin(a)))
    d.polygon(pts, fill=(226, 200, 100, 255), outline=(120, 96, 24, 255))
    d.ellipse([c - 30, c - 30, c + 30, c + 30], fill=(40, 60, 130, 255), outline=(120, 96, 24, 255), width=4)
    d.text((c - 28, c - 12), "PD", font=font(28), fill=(255, 255, 255, 255))
    return img


def town_emblem():
    W = 256
    img = Image.new("RGB", (W, W), (36, 70, 140))
    d = ImageDraw.Draw(img, "RGBA")
    c = W / 2
    d.ellipse([10, 10, W - 11, W - 11], outline=(236, 206, 100, 255), width=8)
    d.polygon([(c, 40), (c + 60, 90), (c + 60, 180), (c, 220), (c - 60, 180), (c - 60, 90)],
              fill=(236, 206, 100, 255))
    d.rectangle([c - 22, 110, c + 22, 190], fill=(36, 70, 140, 255))
    d.polygon([(c - 36, 110), (c, 74), (c + 36, 110)], fill=(36, 70, 140, 255))
    return img


def radio_grille():
    img = Image.new("RGB", (128, 128), (20, 22, 26))
    d = ImageDraw.Draw(img)
    for y in range(10, 120, 12):
        d.line([(10, y), (118, y)], fill=(60, 64, 72), width=4)
    return img


def tablet_ui():
    W, H = 256, 384
    img = Image.new("RGB", (W, H), (14, 22, 40))
    d = ImageDraw.Draw(img, "RGBA")
    d.rectangle([0, 0, W, 44], fill=(30, 80, 160))
    d.text((12, 8), "COMMERCE", font=font(24), fill=(255, 255, 255))
    for i in range(4):
        y = 60 + i * 58
        d.rounded_rectangle([12, y, W - 12, y + 48], radius=8, fill=(30, 44, 74, 255))
        d.text((24, y + 12), ["Ventes", "Stocks", "Factures", "Clients"][i], font=font(20, "reg"), fill=(220, 230, 250))
    for i in range(10):
        h = 20 + (i * 37) % 60
        d.rectangle([20 + i * 22, 380 - h, 36 + i * 22, 380], fill=(60, 200, 130, 255))
    return img


def striped(w, h, c1=(200, 40, 40), c2=(245, 245, 245), n=8):
    img = Image.new("RGB", (w, h), c2)
    d = ImageDraw.Draw(img)
    sw = w // n
    for i in range(0, n, 2):
        d.rectangle([i * sw, 0, (i + 1) * sw, h], fill=c1)
    return img


def screen_glow(w=128, h=128, c=(30, 150, 220)):
    img = Image.new("RGB", (w, h), (4, 8, 14))
    d = ImageDraw.Draw(img, "RGBA")
    for y in range(h):
        t = y / h
        d.line([(0, y), (w, y)], fill=mix(c, tint(c, 0.35), t) + (255,))
    return img
