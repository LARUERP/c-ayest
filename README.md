# Fusion DarkRP Ultimate 5.7.0 — BONELAB Quest / LabFusion

Un seul mod (une seule DLL : `FusionDarkRPUltimate.dll`) : économie en euros, banque, ATM, cartes, argent physique saisissable, téléphone, porte/coffres de banque, métiers DarkRP, missions, factures, police, soins, administration.

## Compilation (GitHub Actions)
1. Déposez tout le contenu de ce dossier à la racine du dépôt GitHub (branche `main`).
2. L'onglet **Actions** lance « Build Fusion DarkRP Ultimate » : contrôle des références, compilation Release, journal `build.log`, DLL unique et archive source versionnée en artefacts.
3. Local : `dotnet build FusionDarkRP-Ultimate.sln -c Release` (SDK .NET 6). Sortie : `bin/Release/FusionDarkRPUltimate.dll`.

## Installation (Quest)
- Prérequis : MelonLoader 0.6.5 (version des bibliothèques de Libs/), BoneLib, LabFusion (+ Fusion Helper). Les DLL de `Libs/` correspondent exactement (même empreinte) à celles de votre installation.
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
Pour modifier un modèle : éditer `Tools/GenerateModels.py` puis `python3 Tools/GenerateAll.py` (Pillow + numpy). Vous pouvez aussi remplacer un `.obj/.mtl/.png` à la main en respectant le nom (les textures s'écrivent `map_Kd Nom.png` dans le `.mtl`).
Porte de sécurité : placez d'abord **Cadre de porte de sécurité** puis **Porte de sécurité bancaire** à la même position et orientation.

## Documents
`AUDIT_FINAL_FR.md` (audit, vérifications, limites) · `FONCTIONNALITES_FR.md`.

## 4.0.0-EOS (voir CHANGELOG_EOS.md)
Nouveautés : missions réelles, imprimante à billets, liasses de 50 billets, radio/menottes/porte refaites.

## 3.1.0-EOS
Version dédiée LabFusion EOS (Libs/LabFusion.dll = build EOS). Voir CHANGELOG_EOS.md.
Placement : regarder où poser, A = poser, B = annuler, clic stick gauche = tourner.

---

## Nouveautés 5.1.0 — mode d'emploi rapide

**Coffre mural physique** : approchez-vous, visez une touche du clavier (à droite de la porte) et appuyez sur la gâchette.
Coffre libre : tapez un code de 4 à 6 chiffres puis `#` pour le réserver. Coffre réservé : code + `#` pour déverrouiller,
puis saisissez la poignée (gâchette maintenue) et tirez. Fermez la porte : elle se reverrouille seule. Déverrouillé et
propriétaire : un nouveau code + `#` remplace l'ancien ; `*` maintenu 2,5 s libère le coffre (videz-le avant).

**Missions** : bouton « Abandonner la mission » dans le menu ; plus aucune reprise après relance, départ du joueur ou
changement de métier ; expiration après 40 minutes.

**Véhicules** : seul le concessionnaire en vend, directement à un client (plus aucun menu d'achat ni véhicule de métier). Le concessionnaire ouvre Joueurs connectés > le client > Actions RP > Vendre un véhicule ; le client (à moins de 8 m) reçoit une offre à accepter dans le menu Factures, et le véhicule apparaît devant lui au paiement (15 % de commission au concessionnaire, le reste au trésor municipal). Les modèles (Clio, TMAX…) viennent de VOS mods
de véhicules : adaptez `UserData/FusionDarkRP/vehicles.json` aux titres listés dans `spawnable-titles.txt`.

**Téléphone** : si la photo ou la visio est noire, regardez le journal MelonLoader : « Caméra téléphone : stratégie N ».

**Cartes bancaires** : nominatives, une seule carte active par personnage ; une nouvelle carte invalide l'ancienne.


---

## Nouveautés 5.5.0
- **Concessionnaire** : nommé par l'hôte via Joueurs connectés > joueur > Recrutement / gestion > Concession automobile. Seul lui vend des véhicules.
- **Cannabis** : plants et sachets refaits ; le rendement de la récolte dépend du moment (récoltez dans les 5 minutes après maturité pour 3 sachets).
- **Objets de métier** refaits (voir CHANGELOG_EOS.md), clés de voiture pour le concessionnaire.


## Nouveautés 5.6.0 — équilibrage
- Véhicules : de 150 EUR (vélo) à 3 000 EUR (Golf). Si vous aviez modifié `vehicles.json`, retrouvez-le en `.bak` après le premier lancement.
- Cannabis : culture plus lente (15 min) et revente plus basse, pour rester cohérent avec les salaires et les missions.
- Équipement des métiers : grades minimum respectés (voir le catalogue d'équipement).


## Nouveautés 5.7.0
- **Concessionnaire dynamique** : il vend TOUS les véhicules installés dans le BONELAB de l'hôte (jeu + mod.io), détectés par tag ou par titre. Un véhicule téléchargé en cours de partie apparaît seul (relecture toutes les 60 s) ou via « Actualiser » dans Joueurs connectés > client > Actions RP > Vendre un véhicule (catégories, pages).
- Fichiers dans `UserData/FusionDarkRP/` : `vehicles.json` (Include, Exclude, Overrides nom/prix/catégorie, PriceMultiplier), `vehicles-detected.txt`, `spawnable-catalog.txt`. Si un véhicule n'est pas reconnu : ajoutez un extrait de son titre dans `Include`.
- Vente : remboursement automatique si le véhicule n'apparaît pas ; délai entre achats appliqué seulement après une vente réussie ; un nouvel achat retire l'ancien véhicule.
- Cohérence : cannabis (semis 60 €, maturité 15 min, revente 25–50 €), imprimante 10 €/30 s (bac 600 €), salaire citoyen 20 € (migration unique), GitHub Actions ne réécrase plus les modèles.
- **Prix réglables en jeu** : dans « Vendre un véhicule », bouton « Mode : régler les prix » ; chaque véhicule propose -1000/-500/-100/+100/+500/+1000 EUR ou « Prix automatique ». Enregistré dans vehicles.json (Overrides).
