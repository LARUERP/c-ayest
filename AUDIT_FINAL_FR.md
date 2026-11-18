# Audit — Fusion DarkRP Ultimate 5.7.0

## Statut
**Non compilé, non testé en jeu** (aucun SDK .NET dans l'environnement de travail). Le code a été vérifié statiquement.

## Base de travail
Dernière archive fournie : 2.2.0 (la 2.3.0 citée dans la mission n'était pas dans les fichiers envoyés). Mon travail précédent (objets saisissables, sécurité bancaire, ATM, placement) a été fusionné par fusion à trois voies sur 2.2.0 ; la structure 2.2.0 (un seul `MelonInfo` dans `Properties/AssemblyInfo.cs`, `PhoneMod` hébergé par `MainMod`) est conservée.

## Corrections de cette version
- **Harmony** : la refonte 2.x avait supprimé `PatchAll` : les patches de voix du téléphone (`PhoneAmbientVoicePatch`, `PhoneOutgoingVoicePatch`) n'étaient jamais appliqués. Rétabli dans `MainMod`.
- **Modèles RP non embarqués** : les 12 fichiers de `Assets/Models/RP` n'étaient pas dans le `.csproj`. Ajoutés.
- **Saisie physique** : objets mobiles créés via un vrai crate BONELAB + `NetworkAssetSpawner` (`CarrierObjects`, `CarrierVisualAdapter`), repli automatique sur l'objet simple.
- **ATM inversé** : rotation du canvas corrigée ; retraits 5/10/20/50/100 €.
- **Placement libre** : raycast regard/surface, distance, hauteur, rotation.
- **Nouveaux systèmes** : porte, coffre-fort, coffre mural (`BankSecurity`) ; canal RP commun (`RpHub`), services (`RpServices`) et menus (`RpMenus`) : missions, factures, amendes, recherchés, soins, administration.
- **Portefeuille** : `TryMoveCashToWalletBank` / `TryMoveBankToWalletCash` atomiques et idempotents.
- **Workflow** : contrôle des références, journal de compilation, archive source versionnée.

## Refonte des modèles 3D et rendu
- Les modèles d'origine étaient des boîtes et des prismes simples, sans UV, sans normales lissées et sans textures. Tout est regénéré (40 modèles, 49 textures) : formes arrondies, UV, textures imprimées (billets, carte, façade ATM, enseigne, claviers, cadran, panneaux), métal brossé, bois, marbre.
- **Chargeur** : `map_Kd` (texture PNG embarquée, `RuntimeObjTextureLoader`) et `Ke` (émission pour écrans/LED) ajoutés au parseur MTL. Les propriétés de shader sont appliquées seulement si elles existent (`HasProperty`) ; leurs noms exacts dans le shader BONELAB n'ont pas pu être confirmés hors du jeu.
- **ATM** : le canvas de l'écran était écrasé (600×1000 px sur 0,31×0,31 m) ; échelle désormais uniforme (0,27×0,45 m) sur un écran de taille correspondante. Écran du poste bancaire aligné sur l'écran du nouveau bureau.
- **Mobilier** : le bureau/comptoir/chaise étaient dessinés au double de leur taille avec une échelle 0,5 ; ils sont désormais à taille réelle (échelle 1).
- **Catalogue** : ajout de `BankSecurityDoorFrame` (cadre fixe) et de 12 équipements de métiers apparaissables (catégorie « Équipements des métiers », saisissables comme les billets).
- Contrôle automatique : indices OBJ/MTL/PNG, matériaux déclarés, correspondance catalogue/OBJ, orientation de tous les triangles (0 erreur).
- Réalisme vérifié par rendu logiciel de contrôle (géométrie) ; le rendu final dépend du shader et de l'éclairage de BONELAB et reste à valider en jeu.

## Signatures vérifiées dans les DLL réelles (lecteur de métadonnées .NET)
`LabFusion.dll` et `BoneLib.dll` du dépôt sont identiques (empreinte) à ceux de l'installation du jeu. Confirmés : `NetworkAssetSpawner` (`LabFusion.RPC`) et ses structures, `NetworkEntity.ID/IsRegistered/GetExtender`, `NetworkEntityManager.HookEntityRegistered`, `MultiplayerHooking.OnPlayerJoined/OnPlayerLeft/OnDisconnected`, `MessageRelay.RelayModule`, `CommonMessageRoutes`, `MessageRoute`, `ReceivedMessage`, `ModuleMessageHandler`, `Notifier/Notification/NotificationType`, `UIMachineUtilities`, `LaserCursorUtilities`, `PlayerIDManager.PlayerIDs`, `NetworkPlayerManager.TryGetPlayer`, `RigRefs.Head`, `BoneLib.BoneMenu.Page` (`CreatePage`, `CreateFunction`, `RemovePage`, `RemoveAll`), `MelonBase.HarmonyInstance`, `MelonPreferences_Category.CreateEntry`, `RigManager.health` et `Health.SetFullHealth`.

## À vérifier en jeu
1. Journal MelonLoader au lancement : « Patches Harmony appliques », « Services RP initialises », « Objets saisissables prêts ».
2. Saisie d'un billet : si le porteur est introuvable, changer `GrabbableCarrierTitle`. La prise est celle du porteur (pistolet) ; le visuel est centré sur sa forme.
3. Orientation de la porte (`SecurityDoorHingeRight`, `SecurityDoorOpenAngle`) selon le modèle.
4. Écran ATM : si le texte est encore inversé, remettre la rotation 180° dans `AtmPhysicalInteraction`.
5. Mods concurrents à retirer de `Mods/` (voir README).
6. Réseau multi-joueurs, arrivée tardive, sauvegardes, performances Quest.


## Audit de cohérence 5.7.0
Vérifié : toutes les actions de menu ont un gestionnaire hôte (et inversement) ; modèles/textures/catalogue cohérents (Tools/VerifyAssets.py : 55 modèles, 59 textures) ; noms d'API Marrow (Crate.Tags, Pallet.Title/Internal, Scannable.Redacted) lus dans Il2CppSLZ.Marrow.dll.
Corrigé : voir CHANGELOG_EOS.md 5.7.0.
Limites : non compilé, non testé en jeu ; reconnaissance des véhicules dépendante des tags/titres (voir spawnable-catalog.txt) ; véhicules non persistants entre sessions.
