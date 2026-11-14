#!/usr/bin/env python3
"""Bibliothèque de génération procédurale de modèles OBJ/MTL pour Fusion DarkRP.

Conventions du chargeur du mod (Economy/RuntimeObjUnityMeshBuilder.cs) :
  * positions utilisées telles quelles, enroulement des triangles inversé par le mod ;
    un triangle (a,b,c) doit donc avoir cross(b-a, c-a) . normale_sortante < 0 ;
  * les normales OBJ sont niées par le mod : on écrit l'opposé de la normale sortante ;
  * un sommet OBJ = (v, vt, vn) identiques ; tout est écrit avec des indices communs.
"""
import math
import os

# ----------------------------------------------------------------------
# Vecteurs
# ----------------------------------------------------------------------

def add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def mul(a, s): return (a[0] * s, a[1] * s, a[2] * s)
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def length(a): return math.sqrt(dot(a, a))
def norm(a):
    l = length(a)
    return (0.0, 1.0, 0.0) if l < 1e-12 else (a[0] / l, a[1] / l, a[2] / l)


class V:
    """Sommet : position, uv, normale sortante."""
    __slots__ = ("p", "uv", "n")

    def __init__(self, p, uv=(0.0, 0.0), n=(0.0, 1.0, 0.0)):
        self.p = (float(p[0]), float(p[1]), float(p[2]))
        self.uv = (float(uv[0]), float(uv[1]))
        self.n = norm(n)


class Mesh:
    def __init__(self):
        self.tris = {}
        self.order = []

    def tri(self, mat, a, b, c):
        if mat not in self.tris:
            self.tris[mat] = []
            self.order.append(mat)
        nav = add(add(a.n, b.n), c.n)
        cr = cross(sub(b.p, a.p), sub(c.p, a.p))
        if length(cr) < 1e-14:
            return
        if dot(cr, nav) > 0:
            b, c = c, b
        self.tris[mat].append((a, b, c))

    def quad(self, mat, a, b, c, d):
        self.tri(mat, a, b, c)
        self.tri(mat, a, c, d)

    def flat_quad(self, mat, p0, p1, p2, p3, uv0=(0, 0), uv1=(1, 0), uv2=(1, 1), uv3=(0, 1), n=None):
        if n is None:
            n = cross(sub(p1, p0), sub(p3, p0))
        self.quad(mat, V(p0, uv0, n), V(p1, uv1, n), V(p2, uv2, n), V(p3, uv3, n))

    # --- fusion d'un autre mesh avec translation ---
    def merge(self, other, offset=(0, 0, 0)):
        for mat in other.order:
            for (a, b, c) in other.tris[mat]:
                self.tri(mat,
                         V(add(a.p, offset), a.uv, a.n),
                         V(add(b.p, offset), b.uv, b.n),
                         V(add(c.p, offset), c.uv, c.n))

    def bounds(self):
        pts = [v.p for t in self.tris.values() for tri in t for v in tri]
        mn = tuple(min(p[i] for p in pts) for i in range(3))
        mx = tuple(max(p[i] for p in pts) for i in range(3))
        return mn, mx

    def tri_count(self):
        return sum(len(t) for t in self.tris.values())

    # ------------------------------------------------------------------
    def write_obj(self, path, mtl_name, comment=""):
        verts, uvs, nrms, index = [], [], [], {}

        def idx(v):
            key = (round(v.p[0], 6), round(v.p[1], 6), round(v.p[2], 6),
                   round(v.uv[0], 5), round(v.uv[1], 5),
                   round(v.n[0], 4), round(v.n[1], 4), round(v.n[2], 4))
            i = index.get(key)
            if i is None:
                verts.append(v.p)
                uvs.append(v.uv)
                nrms.append(mul(v.n, -1.0))  # le mod nie les normales
                i = len(verts)
                index[key] = i
            return i

        body = []
        for mat in self.order:
            if not self.tris[mat]:
                continue
            body.append("usemtl %s" % mat)
            for (a, b, c) in self.tris[mat]:
                ia, ib, ic = idx(a), idx(b), idx(c)
                body.append("f %d/%d/%d %d/%d/%d %d/%d/%d" % (ia, ia, ia, ib, ib, ib, ic, ic, ic))

        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write("# Fusion DarkRP - modèle généré (Tools/GenerateModels.py)\n")
            if comment:
                f.write("# %s\n" % comment)
            f.write("mtllib %s\n" % mtl_name)
            for p in verts:
                f.write("v %.6f %.6f %.6f\n" % p)
            for t in uvs:
                f.write("vt %.5f %.5f\n" % t)
            for n in nrms:
                f.write("vn %.4f %.4f %.4f\n" % n)
            f.write("\n".join(body) + "\n")
        return len(verts)


def write_mtl(path, materials):
    """materials : dict nom -> dict(Kd, Ns, Pm, Ke, map_Kd, d)"""
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        for name, m in materials.items():
            f.write("newmtl %s\n" % name)
            kd = m.get("Kd", (0.8, 0.8, 0.8))
            f.write("Ka 0.20 0.20 0.20\n")
            f.write("Kd %.3f %.3f %.3f\n" % tuple(kd))
            f.write("Ks 0.50 0.50 0.50\n")
            f.write("Ns %.1f\n" % m.get("Ns", 200.0))
            f.write("Pm %.2f\n" % m.get("Pm", 0.0))
            if "Ke" in m:
                f.write("Ke %.3f %.3f %.3f\n" % tuple(m["Ke"]))
            if "map_Kd" in m:
                f.write("map_Kd %s\n" % m["map_Kd"])
            f.write("d %.2f\n" % m.get("d", 1.0))
            f.write("illum 2\n\n")


# ----------------------------------------------------------------------
# Primitives
# ----------------------------------------------------------------------

def _grid(h, r, k):
    """Positions de grille le long d'un axe de demi-longueur h avec congé r."""
    if r <= 1e-9 or r >= h - 1e-9:
        return [-h, h]
    inner = h - r
    ang = [j * (math.pi / 4) / k for j in range(k + 1)]
    neg = [-(inner + r * math.tan(a)) for a in reversed(ang)]
    pos = [inner + r * math.tan(a) for a in ang]
    out = []
    for v in neg + pos:
        if not out or abs(out[-1] - v) > 1e-9:
            out.append(v)
    return out


def rbox(mesh, mats, c, s, r=0.0, k=2, uv=None, tile=1.0, skip=()):
    """Boîte à angles arrondis.
    mats : nom de matériau, ou dict '+x','-x','+y','-y','+z','-z' -> nom ;
    uv   : fonction(face, Q_local, size) -> (u, v) ou None (projection plane x tile)."""
    hx, hy, hz = s[0] / 2.0, s[1] / 2.0, s[2] / 2.0
    h = (hx, hy, hz)
    r = min(r, min(h) * 0.99)
    if min(s) < 0.06:
        k = 1          # petites pièces : congé léger (optimisation Quest)
    for axis in range(3):
        for sign in (1, -1):
            face = ("+" if sign > 0 else "-") + "xyz"[axis]
            if face in skip:
                continue
            mat = mats[face] if isinstance(mats, dict) else mats
            if mat is None:
                continue
            a, b = [i for i in range(3) if i != axis]
            ga, gb = _grid(h[a], r, k), _grid(h[b], r, k)
            rows = []
            for ub in gb:
                row = []
                for ua in ga:
                    P = [0.0, 0.0, 0.0]
                    P[axis] = sign * h[axis]
                    P[a] = ua
                    P[b] = ub
                    inner = [max(-(h[i] - r), min(h[i] - r, P[i])) for i in range(3)]
                    d = [P[i] - inner[i] for i in range(3)]
                    if r > 0 and length(d) > 1e-12:
                        nrm = norm(d)
                        Q = tuple(inner[i] + nrm[i] * r for i in range(3))
                    else:
                        nrm = [0.0, 0.0, 0.0]
                        nrm[axis] = sign
                        Q = tuple(P)
                    if uv:
                        uvv = uv(face, Q, s)
                    else:
                        uvv = (Q[a] * tile, Q[b] * tile)
                    row.append(V(add(c, Q), uvv, nrm))
                rows.append(row)
            for j in range(len(rows) - 1):
                for i in range(len(rows[j]) - 1):
                    mesh.quad(mat, rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i])


def lathe(mesh, mat, c, axis, profile, segs=24, smooth=True, uv=None, start_angle=0.0):
    """Surface de révolution. profile : [(rayon, hauteur_axe), ...] du bas vers le haut ;
    axis : 'x', 'y' ou 'z'. uv : fonction(phi01, idx, (r,h)) -> (u,v)."""
    n = len(profile)
    seg_normals = []
    for i in range(n - 1):
        dr, dh = profile[i + 1][0] - profile[i][0], profile[i + 1][1] - profile[i][1]
        l = math.hypot(dr, dh) or 1.0
        seg_normals.append((dh / l, -dr / l))  # (composante radiale, composante axe)

    def point(rad, hh, phi):
        x, y = rad * math.cos(phi), rad * math.sin(phi)
        if axis == 'z': return (x, y, hh)
        if axis == 'y': return (x, hh, y)
        return (hh, x, y)

    def nvec(nr, nh, phi):
        x, y = nr * math.cos(phi), nr * math.sin(phi)
        if axis == 'z': return (x, y, nh)
        if axis == 'y': return (x, nh, y)
        return (nh, x, y)

    # longueur cumulée pour v
    lens = [0.0]
    for i in range(n - 1):
        lens.append(lens[-1] + math.hypot(profile[i + 1][0] - profile[i][0], profile[i + 1][1] - profile[i][1]))
    total = lens[-1] or 1.0

    def vnorm(i, side):
        # side : 0 = segment précédent, 1 = segment suivant (si non lissé)
        if smooth:
            cand = []
            if i > 0: cand.append(seg_normals[i - 1])
            if i < n - 1: cand.append(seg_normals[i])
            nr = sum(x[0] for x in cand) / len(cand)
            nh = sum(x[1] for x in cand) / len(cand)
            return (nr, nh)
        return seg_normals[min(i if side else i - 1, n - 2)] if n > 1 else (1, 0)

    for s in range(segs):
        phi0 = start_angle + 2 * math.pi * s / segs
        phi1 = start_angle + 2 * math.pi * (s + 1) / segs
        for i in range(n - 1):
            quad = []
            for (phi, ph01) in ((phi0, s / segs), (phi1, (s + 1) / segs)):
                for (j, side) in ((i, 1), (i + 1, 0)):
                    rad, hh = profile[j]
                    nr, nh = vnorm(j, side)
                    if uv:
                        uvv = uv(ph01, j, (rad, hh), lens[j] / total)
                    else:
                        uvv = (ph01, lens[j] / total)
                    quad.append(V(add(c, point(rad, hh, phi)), uvv, nvec(nr, nh, phi)))
            # quad = [p(phi0,i), p(phi0,i+1), p(phi1,i), p(phi1,i+1)]
            a, b, cc, d = quad[0], quad[1], quad[3], quad[2]
            if profile[i][0] < 1e-9 and profile[i + 1][0] < 1e-9:
                continue
            mesh.quad(mat, a, b, cc, d)


def cylinder(mesh, mat_side, mat_cap, c, axis, radius, length_, segs=24, bevel=0.0, cap_uv=None, tile=1.0):
    """Cylindre centré en c, axe 'x'|'y'|'z', avec chanfrein optionnel."""
    hl = length_ / 2.0
    b = min(bevel, radius * 0.45, hl * 0.9)
    prof_side = []
    if b > 0:
        prof_side = [(radius - b, -hl), (radius, -hl + b), (radius, hl - b), (radius - b, hl)]
    else:
        prof_side = [(radius, -hl), (radius, hl)]

    def side_uv(ph, i, rh, v):
        return (ph * tile * 3.0, v * tile)
    lathe(mesh, mat_side, c, axis, prof_side, segs, True, side_uv)
    for sign in (-1, 1):
        rin = radius - b
        prof = [(0.0, sign * hl), (rin, sign * hl)] if sign < 0 else [(rin, sign * hl), (0.0, sign * hl)]

        def disc_uv(ph, i, rh, v, sign=sign):
            rad = rh[0] / max(radius, 1e-9)
            ang = ph * 2 * math.pi
            u, vv = 0.5 + 0.5 * rad * math.cos(ang), 0.5 + 0.5 * rad * math.sin(ang)
            if cap_uv:
                return cap_uv(sign, u, vv)
            return (u, vv)
        lathe(mesh, mat_cap, c, axis, prof, segs, False, disc_uv)


def torus(mesh, mat, c, axis, major, minor, segs=24, rings=10, uv_scale=1.0):
    prof = []
    for i in range(rings + 1):
        a = 2 * math.pi * i / rings
        prof.append((major + minor * math.cos(a + math.pi), minor * math.sin(a + math.pi) * -1.0))
    # profil : cercle de rayon minor autour de (major, 0), parcouru dans le sens sortant
    prof = [(major + minor * math.cos(a), minor * math.sin(a))
            for a in [2 * math.pi * i / rings - math.pi / 2 for i in range(rings + 1)]]
    # le sens doit faire normale sortante = (dh, -dr) : parcours anti-horaire inversé
    prof = list(reversed(prof))
    lathe(mesh, mat, c, axis, prof, segs, True, lambda ph, i, rh, v: (ph * uv_scale, v))


def polygon_prism(mesh, mat_front, mat_back, mat_edge, pts2d, thickness, center=(0, 0, 0),
                  uv_front=None, uv_back=None):
    """Extrusion le long de Z d'un polygone étoilé par rapport au centroïde.
    Face avant = -z (lisible par un observateur placé en -z), face arrière = +z."""
    cx = sum(p[0] for p in pts2d) / len(pts2d)
    cy = sum(p[1] for p in pts2d) / len(pts2d)
    hz = thickness / 2.0
    n = len(pts2d)
    minx, maxx = min(p[0] for p in pts2d), max(p[0] for p in pts2d)
    miny, maxy = min(p[1] for p in pts2d), max(p[1] for p in pts2d)

    def uvf(p, front):
        u = (p[0] - minx) / ((maxx - minx) or 1)
        v = (p[1] - miny) / ((maxy - miny) or 1)
        if front:
            return uv_front(u, v) if uv_front else (u, v)
        return uv_back(1 - u, v) if uv_back else (1 - u, v)

    for i in range(n):
        p0, p1 = pts2d[i], pts2d[(i + 1) % n]
        for (z, nz, front, mat) in ((-hz, -1.0, True, mat_front), (hz, 1.0, False, mat_back)):
            mesh.tri(mat,
                     V(add(center, (cx, cy, z)), uvf((cx, cy), front), (0, 0, nz)),
                     V(add(center, (p0[0], p0[1], z)), uvf(p0, front), (0, 0, nz)),
                     V(add(center, (p1[0], p1[1], z)), uvf(p1, front), (0, 0, nz)))
        e = sub((p1[0], p1[1], 0), (p0[0], p0[1], 0))
        nrm = (e[1], -e[0], 0.0)
        mesh.quad(mat_edge,
                  V(add(center, (p0[0], p0[1], hz)), (0, 0), nrm),
                  V(add(center, (p1[0], p1[1], hz)), (1, 0), nrm),
                  V(add(center, (p1[0], p1[1], -hz)), (1, 1), nrm),
                  V(add(center, (p0[0], p0[1], -hz)), (0, 1), nrm))


def rounded_rect_points(w, h, r, seg=6):
    hw, hh = w / 2.0, h / 2.0
    r = min(r, hw, hh)
    pts = []
    corners = [(hw - r, hh - r, 0), (-hw + r, hh - r, 90), (-hw + r, -hh + r, 180), (hw - r, -hh + r, 270)]
    for (cx, cy, a0) in corners:
        for i in range(seg + 1):
            a = math.radians(a0 + 90.0 * i / seg)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def sphere(mesh, mat, c, radius, segs=16, rings=10, scale=(1, 1, 1)):
    prof = [(radius * math.sin(math.pi * i / rings), -radius * math.cos(math.pi * i / rings)) for i in range(rings + 1)]
    m = Mesh()
    lathe(m, mat, (0, 0, 0), 'y', prof, segs, True)
    for mm in m.order:
        for (a, b, cc) in m.tris[mm]:
            def f(v):
                p = (v.p[0] * scale[0], v.p[1] * scale[1], v.p[2] * scale[2])
                n = (v.n[0] / scale[0], v.n[1] / scale[1], v.n[2] / scale[2])
                return V(add(c, p), v.uv, n)
            mesh.tri(mat, f(a), f(b), f(cc))


# ----------------------------------------------------------------------
# Aperçu logiciel (contrôle visuel)
# ----------------------------------------------------------------------

def preview(mesh, path, size=420, views=(("avant", (0, 0)), ("côté", (90, 0)), ("3/4", (35, 22)))):
    from PIL import Image, ImageDraw
    mn, mx = mesh.bounds()
    ctr = tuple((mn[i] + mx[i]) / 2 for i in range(3))
    ext = max(mx[i] - mn[i] for i in range(3)) or 1.0
    W = size * len(views)
    img = Image.new("RGB", (W, size), (28, 30, 36))
    d = ImageDraw.Draw(img)
    for vi, (label, (yaw, pitch)) in enumerate(views):
        ya, pa = math.radians(yaw), math.radians(pitch)
        polys = []
        for mat in mesh.order:
            for (a, b, c) in mesh.tris[mat]:
                pts = []
                for v in (a, b, c):
                    p = sub(v.p, ctr)
                    x = p[0] * math.cos(ya) - p[2] * math.sin(ya)
                    z = p[0] * math.sin(ya) + p[2] * math.cos(ya)
                    y = p[1] * math.cos(pa) - z * math.sin(pa)
                    z2 = p[1] * math.sin(pa) + z * math.cos(pa)
                    pts.append((x, y, z2))
                nrm = norm(mul(cross(sub(b.p, a.p), sub(c.p, a.p)), -1.0))
                nx = nrm[0] * math.cos(ya) - nrm[2] * math.sin(ya)
                nz = nrm[0] * math.sin(ya) + nrm[2] * math.cos(ya)
                ny = nrm[1] * math.cos(pa) - nz * math.sin(pa)
                nz2 = nrm[1] * math.sin(pa) + nz * math.cos(pa)
                if nz2 > 0.0:  # face dos à la caméra (caméra côté -z)
                    continue
                light = max(0.15, 0.5 * (-nz2) + 0.35 * ny + 0.3 * nx + 0.25)
                depth = sum(p[2] for p in pts) / 3
                polys.append((depth, pts, light, mat))
        polys.sort(key=lambda t: -t[0])
        sc = (size * 0.86) / ext
        for depth, pts, light, mat in polys:
            xy = [(vi * size + size / 2 + p[0] * sc, size / 2 - p[1] * sc) for p in pts]
            base = (150, 160, 175)
            col = tuple(max(0, min(255, int(c * light * 1.3))) for c in base)
            d.polygon(xy, fill=col)
        d.text((vi * size + 6, 4), label, fill=(255, 255, 255))
    img.save(path)


def tube(mesh, mat, p0, p1, r, segs=10, caps=True, r1=None):
    """Cylindre (ou cône tronqué si r1) entre deux points quelconques."""
    ax = sub(p1, p0)
    L = length(ax)
    if L < 1e-9:
        return
    a = mul(ax, 1.0 / L)
    helper = (0, 1, 0) if abs(a[1]) < 0.9 else (1, 0, 0)
    u = norm(cross(helper, a))
    w = cross(a, u)
    r1 = r if r1 is None else r1
    slope = (r - r1) / L
    ring0, ring1 = [], []
    for i in range(segs + 1):
        ph = 2 * math.pi * i / segs
        d = add(mul(u, math.cos(ph)), mul(w, math.sin(ph)))
        n = norm(add(d, mul(a, slope)))
        ring0.append(V(add(p0, mul(d, r)), (i / segs, 0.0), n))
        ring1.append(V(add(p1, mul(d, r1)), (i / segs, 1.0), n))
    for i in range(segs):
        mesh.quad(mat, ring0[i], ring0[i + 1], ring1[i + 1], ring1[i])
    if caps:
        for (pc, rr, sgn) in ((p0, r, -1.0), (p1, r1, 1.0)):
            nrm = mul(a, sgn)
            for i in range(segs):
                ph0, ph1 = 2 * math.pi * i / segs, 2 * math.pi * (i + 1) / segs
                q0 = add(pc, mul(add(mul(u, math.cos(ph0)), mul(w, math.sin(ph0))), rr))
                q1 = add(pc, mul(add(mul(u, math.cos(ph1)), mul(w, math.sin(ph1))), rr))
                mesh.tri(mat, V(pc, (0.5, 0.5), nrm),
                         V(q0, (0.5 + 0.5 * math.cos(ph0), 0.5 + 0.5 * math.sin(ph0)), nrm),
                         V(q1, (0.5 + 0.5 * math.cos(ph1), 0.5 + 0.5 * math.sin(ph1)), nrm))


def double_quad(mesh, mat, p0, p1, p2, p3, uv=((0, 0), (1, 0), (1, 1), (0, 1))):
    n = cross(sub(p1, p0), sub(p3, p0))
    mesh.quad(mat, V(p0, uv[0], n), V(p1, uv[1], n), V(p2, uv[2], n), V(p3, uv[3], n))
    m = mul(n, -1.0)
    mesh.quad(mat, V(p0, uv[0], m), V(p3, uv[3], m), V(p2, uv[2], m), V(p1, uv[1], m))


def face_uv(rect=(0.0, 0.0, 1.0, 1.0)):
    """UV plein cadre orientées pour un observateur extérieur (non miroir)."""
    def f(face, Q, s):
        hx, hy, hz = s[0] / 2.0, s[1] / 2.0, s[2] / 2.0
        if face == "-z":
            u, v = (Q[0] + hx) / (2 * hx), (Q[1] + hy) / (2 * hy)
        elif face == "+z":
            u, v = 1 - (Q[0] + hx) / (2 * hx), (Q[1] + hy) / (2 * hy)
        elif face == "-x":
            u, v = 1 - (Q[2] + hz) / (2 * hz), (Q[1] + hy) / (2 * hy)
        elif face == "+x":
            u, v = (Q[2] + hz) / (2 * hz), (Q[1] + hy) / (2 * hy)
        elif face == "+y":
            u, v = (Q[0] + hx) / (2 * hx), (Q[2] + hz) / (2 * hz)
        else:
            u, v = (Q[0] + hx) / (2 * hx), 1 - (Q[2] + hz) / (2 * hz)
        return (rect[0] + u * (rect[2] - rect[0]), rect[1] + v * (rect[3] - rect[1]))
    return f


def atlas_rect(col, row, cols, rows):
    """Rectangle UV d'une cellule d'atlas (ligne 0 = haut de l'image)."""
    return (col / cols, 1 - (row + 1) / rows, (col + 1) / cols, 1 - row / rows)
