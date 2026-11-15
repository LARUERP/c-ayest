# Fusion DarkRP Ultimate 3.0.0 — BONELAB Quest / LabFusion

Un seul mod (une seule DLL : `FusionDarkRPUltimate.dll`) : économie en euros, banque, ATM, cartes, argent physique saisissable, téléphone, porte/coffres de banque, métiers DarkRP, missions, factures, police, soins, administration.

## Compilation (GitHub Actions)
1. Déposez tout le contenu de ce dossier à la racine du dépôt GitHub (branche `main`).
2. L'onglet **Actions** lance « Build Fusion DarkRP Ultimate » : contrôle des références, compilation Release, journal `build.log`, DLL unique et archive source versionnée en artefacts.
3. Local : `dotnet build FusionDarkRP-Ultimate.sln -c Release` (SDK .NET 6). Sortie : `bin/Release/FusionDarkRPUltimate.dll`.

## Installation (Quest)
- Prérequis : MelonLoader 0.7, BoneLib, LabFusion (+ Fusion Helper). Les DLL de `Libs/` correspondent exactement (même empreinte) à celles de votre installation.
- Copiez `FusionDarkRPUltimate.dll` dans `Mods/`.
- **Supprimez les anciens mods concurrents** de `Mods/` : `DarkRPModule.dll`, `LabRP.dll`, `MoneyLab.dll`, `BonelabRP.Telephone.dll` (le téléphone est désormais intégré). Les données du téléphone restent dans `UserData/BonelabRP/Telephone`.
- Tous les joueurs doivent avoir la même version.

## Utilisation en jeu (menu BoneMenu « Fusion DarkRP »)
- **Jobs / Factions / Hiérarchies / Mon personnage** : métiers et grades. Recrutement par l'hôte ou le chef de métier (Joueurs connectés > joueur > Ouvrir gestion).
- **Banque** (Poste informatique, ATM, cartes) : voir les menus Banque et le catalogue d'objets (hôte).
- **Missions** : *Actualiser mes missions* > choisir > *Démarrer* > *Valider l'étape* (délai minimal imposé par l'hôte). Récompense versée en espèces.
- **Factures** : reçues des autres joueurs ; PAYER (banque, sinon espèces) ou REFUSER.
- **Police : recherches** : liste des recherchés, arrestation.
- **Joueurs connectés > joueur > Actions RP** : Facturer, Police (amendes, recherché, arrêter), Soins, Admin (hôte : voir solde, donner de l'argent).
- **Catalogue d'objets (hôte)** > *Réglages de placement* : mode « où je regarde » (sol/table/mur) ou « devant moi », distance, hauteur, rotation.

## Objets saisissables
Les billets, pièces, liasses et objets mobiles sont créés comme un vrai objet BONELAB (SpawnableCrate, par défaut l'Eder 22) via LabFusion, puis habillés du modèle OBJ chez tous les joueurs. Préférences (`UserData/MelonPreferences.cfg`, catégorie `FusionDarkRP`) :
- `GrabbableObjects` (true/false) ; `GrabbableCarrierTitle` (titre du crate porteur) ;
- `SecurityDoorHingeRight` (charnière de la porte) ; `SecurityDoorOpenAngle` (angle d'ouverture).

## Modèles 3D et textures (refonte complète)
Tous les modèles sont **originaux** (générés par `Tools/GenerateModels.py`, aucune licence tierce) : billets 5 à 100 € légèrement bombés avec recto/verso imprimés, pièces euro avec rebord et face texturée, liasses sanglées, carte bancaire à coins arrondis (puce, recto/verso), ATM (écran, clavier, lecteur, distributeur, enseigne lumineuse), coffre-fort à porte ronde, porte de sécurité à charnières (+ cadre de porte séparé), coffre mural (cadran, poignée, clavier), bureau avec écran vertical, comptoir vitré, chaise, et 12 accessoires de métiers. Normales lissées, UV, textures PNG (49 fichiers, 512 px max), matériaux métal/émissif. Budget Quest : ATM ≈ 3,8 k triangles, coffre-fort ≈ 6 k, porte ≈ 2,3 k, billets ≈ 130, pièces ≈ 450.
Pour modifier un modèle : éditer `Tools/GenerateModels.py` puis `python3 Tools/GenerateModels.py` (Pillow + numpy). Vous pouvez aussi remplacer un `.obj/.mtl/.png` à la main en respectant le nom (les textures s'écrivent `map_Kd Nom.png` dans le `.mtl`).
Porte de sécurité : placez d'abord **Cadre de porte de sécurité** puis **Porte de sécurité bancaire** à la même position et orientation.

## Documents
`AUDIT_FINAL_FR.md` (audit, vérifications, limites) · `FONCTIONNALITES_FR.md`.

## 4.0.0-EOS (voir CHANGELOG_EOS.md)
Nouveautés : missions réelles, imprimante à billets, liasses de 50 billets, radio/menottes/porte refaites.

## 3.1.0-EOS
Version dédiée LabFusion EOS (Libs/LabFusion.dll = build EOS). Voir CHANGELOG_EOS.md.
Placement : regarder où poser, A = poser, B = annuler, clic stick gauche = tourner.
