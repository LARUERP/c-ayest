#!/usr/bin/env python3
"""Contrôle d'intégrité des modèles embarqués (sans Pillow) :
 - chaque .obj a son .mtl ;
 - chaque texture (map_Kd) d'un .mtl existe dans Assets/Models/Money (seul dossier embarqué en PNG) ;
 - chaque modèle déclaré au catalogue (RuntimeObjWorldModelCatalog) a son .obj.
Usage : python3 Tools/VerifyAssets.py   (code 1 si un écart est trouvé)
"""
import glob
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MONEY = os.path.join(ROOT, "Assets", "Models", "Money")
RP = os.path.join(ROOT, "Assets", "Models", "RP")

errors = []
objs = {}

for folder in (MONEY, RP):
    for path in glob.glob(os.path.join(folder, "*.obj")):
        objs[os.path.splitext(os.path.basename(path))[0]] = path

pngs = {os.path.basename(p) for p in glob.glob(os.path.join(MONEY, "*.png"))}

for name, path in sorted(objs.items()):
    mtl = path[:-4] + ".mtl"
    if not os.path.exists(mtl):
        errors.append("MTL manquant : " + name)
        continue
    with open(mtl, encoding="utf-8", errors="replace") as handle:
        for line in handle:
            match = re.match(r"\s*map_Kd\s+(\S+)", line)
            if match and match.group(1) not in pngs:
                errors.append("Texture manquante : %s -> %s" % (name, match.group(1)))

catalog = os.path.join(ROOT, "Economy", "RuntimeObjWorldModelCatalog.cs")
declared = []
if os.path.exists(catalog):
    with open(catalog, encoding="utf-8") as handle:
        declared = re.findall(r'new WorldModelDefinition\(\s*"([^"]+)"', handle.read())
for model_id in declared:
    if model_id not in objs:
        errors.append("Modèle déclaré sans .obj : " + model_id)

print("%d modèles, %d textures PNG, %d modèles au catalogue." % (len(objs), len(pngs), len(declared)))
if errors:
    print("\n".join("ERREUR : " + e for e in errors))
    sys.exit(1)
print("Modèles et textures : cohérents.")
