#!/usr/bin/env python3
"""Régénère TOUS les modèles et textures, dans l'ordre : V1 puis V2 à V7.
Chaque version complète ou remplace les modèles de la précédente ; lancer
GenerateModels.py seul redonne les anciens modèles. (Pillow + numpy requis.)
Usage : python3 Tools/GenerateAll.py
"""
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPTS = ["GenerateModels.py"] + ["GenerateModelsV%d.py" % v for v in range(2, 8)]

for name in SCRIPTS:
    print("== " + name)
    code = subprocess.call([sys.executable, os.path.join(HERE, name)], cwd=os.path.dirname(HERE))
    if code != 0:
        print("Échec de " + name + " (code %d)" % code)
        sys.exit(code)

print("Tous les modèles ont été régénérés.")
