# 5.6.0 — équilibrage et cohérence (non compilé ici ; à valider par GitHub Actions puis en jeu)

## Véhicules chers mais accessibles
- Prix : vélo 150 EUR, scooter 700, Twingo 1 500, Clio 2 000, 208 2 200, TMAX 2 600, Golf 3 000 ; véhicule générique BONELAB 1 500 (avant : 50 à 600 EUR). Repères : salaire 35 à 55 EUR
  par palier de 15 min, missions 40 à 475 EUR : un véhicule demande quelques missions, le vélo et le scooter permettent de démarrer.
- vehicles.json est versionné (Version 3) : l'ancien fichier est sauvegardé en `vehicles.json.v1.bak` (ou `.vN.bak`) puis remplacé, sinon les anciens prix auraient été conservés.

## Incohérences corrigées
- Cannabis beaucoup trop rentable par rapport aux métiers (≈ 7 500 EUR/h) : maturité 15 min (stade 2 à 5 min), flétrissement 15 min après maturité, graines 60 EUR,
  prix de rue 25 à 50 EUR par sachet (x1,2 pour les gangs) soit ≈ 2 000 EUR/h pour 4 plants, comparable aux missions ; saisie par la police revalorisée (80 EUR par plant, 40 EUR par sachet, plafond 500 EUR).
- Équipement : les kits donnés à l'embauche respectent maintenant les grades minimum du catalogue d'équipement (mallette d'armes à partir du grade 2, défibrillateur grade 3 ; menottes et terminal bancaire
  disponibles dès le premier grade, comme un vrai policier ou guichetier). Le concessionnaire figure enfin dans le catalogue d'équipement (tablette de vente, clés de voiture).
- (rappel 5.4.0/5.5.0) trésor municipal unifié (`system:town_treasury`), équipe 11 dans les menus et annonces.

# 5.5.0 — build finale (non compilée ici : aucun SDK .NET dans le bac à sable ; à valider par GitHub Actions puis en jeu)

## Métier de concessionnaire enfin accessible
- Cause : l'équipe 11 (Concession automobile) existait mais manquait dans le menu de recrutement de l'hôte (Class1.cs : équipes 1 à 10 seulement), dans
  « Factions » et « Hiérarchies », et dans les annonces de faction (DarkRPNetwork : bornes 1 à 10). Corrigé : l'hôte peut maintenant nommer un concessionnaire
  (Joueurs connectés > joueur > Recrutement / gestion > Concession automobile) ; le patron de concession recrute ensuite son équipe.
- Rappel : seul un concessionnaire vend des véhicules (Joueurs connectés > client > Actions RP > Vendre un véhicule). Équipement : tablette + clés de voiture (nouveau modèle CarKeys).

## Cannabis
- Plants refaits (Tools/GenerateModelsV5.py) : grandes feuilles palmées à 9 folioles dentées et nervurées, rameaux latéraux, colas « queue de renard » (calices empilés,
  pistils, feuilles de sucre, cristaux), certains calices violacés. Stade 3 : 5 306 triangles (6 680 avant).
- Sachet de récolte refait : sachet zip transparent avec têtes visibles et étiquette.
- Récolte : le rendement dépend du moment (3 sachets dans les 5 min après maturité, 2 jusqu'à 10 min, puis 1) ; avant maturité le message indique le temps restant.

## Objets de métier refaits (Tools/GenerateModelsV7.py)
- PoliceRadio (écran, clavier, PTT, antenne, clip), ArmoryCase (mallette rigide, 3 fermoirs, plaque), WorkGloves (paire haute visibilité), TrashBag (sac noué),
  ShopCrate (caisse en bois avec produits), WeedBag, et nouveau CarKeys. Avec les modèles de la 5.3.0 (menottes, insignes, trousse, défibrillateur, terminal, etc.),
  tous les objets liés aux métiers sont maintenant refaits, sauf MoneyPrinter, MobileStand, MissionMarker et PoopPile.

# 5.4.0 (non compilé ici : aucun SDK .NET dans le bac à sable ; à valider par GitHub Actions puis en jeu)

## Véhicules : vente directe par le concessionnaire (plus de menu d'achat)
- Plus aucun véhicule de métier ni menu « acheter » : le concessionnaire choisit Joueurs connectés > client > Actions RP > Vendre un véhicule
  (client à moins de 8 m, nouvelle commande hôte `veh.sell`). Une offre apparaît dans les Factures du client ; au paiement, contrôles (concessionnaire toujours
  en service, véhicule installé, fonds en banque ou en liquide, position), paiement (15 % au concessionnaire, reste au trésor) puis apparition du véhicule devant le client.
  Rien n'est débité si un contrôle échoue. Commandes `veh.spawn` et `veh.list` supprimées.
- `JobVehicleCatalog.ForJob` ne propose que les véhicules à l'achat, même avec un ancien vehicles.json.
- Correction de cohérence : le trésor municipal avait deux identifiants (`system:treasury` dans RpServices : amendes, primes, « Voir le trésor » ; `system:town_treasury`
  ailleurs : véhicules, semis, coffre du trésor). Tout utilise désormais `system:town_treasury`. L'ancien solde éventuel de `system:treasury` n'est pas migré.

## Modèles 3D refaits (Tools/GenerateModelsV6.py ; mêmes noms, origine et orientation conservées)
- PoliceCuffs : deux bracelets à crémaillère, boîtier à double verrou + trou de clé, chaîne de 4 maillons croisés.
- PoliceBadge (insigne bouclier avec liseré, étoile, anneau), TownBadge (médaille + ruban tricolore), PoliceNotepad (spirale, élastique, stylo),
  TownFolder (dossier à sangles), ArmoryCatalog (classeur à anneaux, couverture illustrée), MedicalKit (mallette rigide, fermoirs, poignée, croix des deux côtés),
  Defibrillator (écran, bouton de choc, électrodes + câble), BankTerminal (écran, clavier, NFC), ShopScanner, TrashGrabber (pince à gâchette et mâchoires), BusinessTablet.
- Cannabis (Tools/GenerateModelsV5.py) : plants refaits en 3 stades (voir 5.1.1).
- Textures ajoutées : Terminal_UI, Nfc_Icon, Catalog_Cover, Folder_Cover, Weed_Leaf.

# 5.1.1 — cannabis refait (modèles seulement ; aucun code C# modifié)
- WeedPot1/2/3 régénérés (Tools/GenerateModelsV5.py) : pot de pépinière noir + soucoupe + cailloux de drainage, tuteur, tige effilée,
  feuilles palmées à folioles dentées et nervurées (texture Weed_Leaf.png), colas avec calices, pistils orangés et cristaux.
  Stade 3 : 4 526 triangles (6 680 avant), stade 2 : 2 298, stade 1 : 1 094. Mêmes noms de modèles : remplacement direct.
- Aucune modification des véhicules ni du code : base = archive 5.1.0.

# 5.1.0 (non compilé ici — aucun SDK .NET dans le bac à sable ; à valider par GitHub Actions puis en jeu)

## Correctifs de compilation (build #12)
- GameItemService.cs : `using LabFusion.RPC;` (NetworkAssetSpawner introuvable, lignes 202-203 / 269-270).
- PlacementPreview.cs : champ inutilisé `notifyAt` supprimé.

## Missions : plus jamais de mission « fantôme »
- Une mission non terminée n'est JAMAIS reprise après un redémarrage (MissionRuntime.Load les abandonne).
- Fin de session / hôte qui quitte : toutes les missions en cours sont abandonnées et leurs objets/balises détruits.
- Balayage toutes les 0,4 s : mission abandonnée si le joueur est parti, a changé de métier, ou dépasse 40 min.
- Nouveau bouton « Abandonner la mission » (BoneMenu) + requête hôte `mission.abandon`.

## Véhicules par métier (vrais noms : Clio, TMAX, Mégane, Trafic, Master…)
- Nouveau catalogue RP/JobVehicleCatalog.cs : police (Clio, Mégane, 308, TMAX, Trafic), médecins (ambulance, Master, TMAX),
  éboueurs (camion-benne, Kangoo), mairie, banque, épicerie, armurerie, concessionnaire (démonstration), gangs, civils (achat).
- BONELAB ne contient pas ces modèles : ils sont cherchés par titre parmi les mods de véhicules installés.
  Liste modifiable sans recompiler : UserData/FusionDarkRP/vehicles.json (créé au premier lancement) ; titres disponibles
  dans UserData/FusionDarkRP/spawnable-titles.txt.
- Le véhicule doit exister AVANT tout paiement ; menu « Véhicules de mon métier » (requête `veh.list`), [non installé] signalé.

## Coffre mural PHYSIQUE (coffre de la ville et coffres de banque inchangés)
- Nouveaux modèles (Tools/GenerateModelsV4.py) : WallSafe (corps creux, étagère, clavier) + WallSafeDoor (porte sur charnière).
- Clavier 3D : visez la touche avec la main, gâchette = appui. 0-9, « * » efface (maintenu 2,5 s coffre déverrouillé : libère le
  coffre), « # » valide. Code de 4 à 6 chiffres ; premier code saisi = réservation du coffre.
- Poignée : saisir (gâchette) puis tirer/pousser ; la porte suit la main, est synchronisée, se reverrouille 2 s après fermeture.
- 3 erreurs de code = clavier bloqué 60 s + alerte. Code haché (SHA-256 + sel) ; propriétaire et code sauvegardés dans
  UserData/FusionDarkRP/wall-safes.json (clé = position arrondie à 25 cm).
- Stockage RÉEL : les objets posés dans la cavité y restent (la porte a une collision). L'ancien solde virtuel d'un coffre est
  versé en liquide à son propriétaire au premier déverrouillage. Plus de panneau laser sur ce coffre.
- Réglage : MelonPreferences « SafeDoorDirection » (1 ou -1) si la porte s'ouvre du mauvais côté.

## Téléphone : écran noir (photos, aperçu, appels vidéo)
- PhoneCamera : RenderTextures sans MSAA, contrôle d'image noire et repli automatique (stratégies 0/1/2), rendu refait.
  Le journal indique « Caméra téléphone : stratégie N active » ou « image noire, bascule vers la stratégie N ».

## Cartes bancaires nominatives
- Une carte est dédiée à un personnage (titulaire + nom). Délivrer une nouvelle carte invalide l'ancienne du même titulaire.
- ATM : message explicite « Carte nominative : elle appartient à X » quand ce n'est pas la bonne personne.

# 3.0.1 — compatibilité LabFusion EOS (non compilé ici)
- PlatformIds.cs : Steam (ulong) et EOS (string) gérés sans recompiler ; 31 fichiers adaptés.
- Pistolet Eder22 masqué sur toute la hiérarchie + ré-imposé en continu (CarrierVisualAdapter.Tick).
- Carte bancaire : puce intégrée au modèle (objet unique), BankCardChip supprimé.
- PlacementPreview : aperçu avant pose (A poser, B annuler, clic stick gauche tourner).
- Menu catalogue : catégories regroupées (Banque+cartes, Argent, Équipements).
# 3.1.1 (non compilé ici)
- Portefeuille : rig du joueur local résolu via BoneLib ; recherche d'avant-bras mise en cache et limitée (cause de lag quand l'écran ne s'attachait pas).
- Carte bancaire : holster du joueur local résolu via RigData.
- Matériaux : cache des shaders (plus de FindObjectsOfTypeAll à chaque objet).
- Coffres/ATM : le serveur répond à toute demande invalide ; délai porté à 12 s.
# 3.2.0 (non compilé ici) — d'après le journal du 14/11
- Coffres/ATM/PC : l'hôte recevait sa propre réponse AVANT d'enregistrer sa demande et l'ignorait -> « le serveur ne répond pas » systématique. Corrigé (jeton -1 pendant l'émission).
- Carte bancaire : si le 1er emplacement refuse le support Eder22, les autres emplacements libres sont essayés.
- Portefeuille : repli sur le parent de la main gauche ; journal « Portefeuille attaché à » ou liste des os du bras si introuvable.
# 3.3.0 (non compilé ici) — d'après le journal du 14/11 (00:33)
- Portefeuille : il s'attachait à « PhysicsRig » (racine) donc invisible ; il suit maintenant le poignet gauche (position estimée, tourné vers la tête).
- Carte bancaire : pistolet Eder22 masqué en continu aussi sur la carte (+ tir désactivé).
- Distributeur : collisions carte/ATM ignorées pour supprimer le recul ; la détection du lecteur reste active.
# 3.4.0 (non compilé ici) — d'après les journaux du 14/11 (00:55)
- Distributeur : la carte est avalée par la fente (lâchée par la main, centrée, immobilisée) ; bouton « EJECTER MA CARTE » pour la reprendre. Les deux mains restent libres pour l'argent.
- Portefeuille : fond d'écran = ta photo « LA RUE RP » (opaque, recadrée 8:3, Assets/file_0000…png) + voile sombre derrière le texte ; l'image n'est plus perdue à la 2e création de l'écran.
# 3.5.0 (non compilé ici)
- Portefeuille : main droite près du poignet -> A change le billet (5/10/20/50/100 €), gâchette sort un billet physique débité du liquide (sans carte). Ranger : saisir le billet (retour automatique au portefeuille, existant). Résultats affichés en notification ; aide à l'écran.
# 3.5.1 (non compilé ici)
- Portefeuille : tout billet apporté à moins de 25 cm est rangé (demande de rangement journalisée « Billet près du portefeuille »).
# 3.5.1 (non compilé ici)
- Billet encaissé mais resté visible : l'hôte journalise le retrait et supprime de force (Poolee.Despawn puis Destroy) tout porteur encore présent 1,2 s après le despawn LabFusion.
# 3.5.2 — correction de compilation (erreur GitHub Actions : WalletCardId hors de portée)

# 3.6.0 (non compilé ici — aucun SDK .NET dans le bac à sable)
- Billets/pièces : saisir de l'argent physique ne l'encaisse plus (le pont saisie→encaissement est neutralisé) ; le rangement automatique à moins de 25 cm du poignet est supprimé. Ranger = geste volontaire : argent à moins de 22 cm du portefeuille + bouton B.
- Portefeuille : la sortie d'espèces couvre maintenant 5 billets et 8 pièces (A fait défiler, gâchette sort, B range).
- Carte bancaire : plus de disparition silencieuse. Si aucun holster n'est libre ou si l'insertion est refusée, la carte (toujours enregistrée en banque) est posée devant le joueur et reste saisissable ; le lecteur du distributeur la reconnaît comme avant.
- Objets de métier : 6 modèles manquants créés (PoliceNotepad, ShopScanner, TownFolder, WorkGloves, ArmoryCatalog, BankTerminal) et déclarés au catalogue ; 18 équipements apparaissables au total, contrôle OBJ/MTL/PNG/catalogue à 0 écart.
- Journal fourni (14/11 13:22) : il s'arrête avant le chargement des mods (erreur MelonLoader/Il2CppInterop) et ne contient aucune ligne du mod ; voir le message de livraison.

# 3.7.0 (non compilé ici — aucun SDK .NET dans le bac à sable)
- Billets/pièces apparaissaient au point de départ du niveau : le point d'apparition était la racine du RigManager (qui ne suit pas le joueur). Nouveau PhysicalCashRigAnchorResolver.ResolveBody (tête du rig physique) utilisé pour les apparitions d'argent et le repli de la carte.
- Retrait au distributeur : la distance au distributeur était mesurée depuis ce même point de départ (toujours "trop loin") ; corrigé par le même changement.
- Personnage poussé quand un objet est rangé au holster : le collider ajouté par le mod était sur le calque par défaut et entrait en collision avec le corps physique du joueur. Le visuel prend maintenant le calque des colliders d'origine de l'arme (CarrierVisualAdapter.MatchLayerToWeapon), pour les objets porteurs et la carte.
- Diagnostic des refus carte au distributeur : le journal indique désormais la raison exacte (carte absente du registre, révoquée, propriétaire différent, requête trop rapprochée).

# 3.8.0 (non compilé ici — aucun SDK .NET dans le bac à sable ; corrections tirées du journal 26-11-14_19-4-30)
- Rangement des billets ("Billet trop éloigné") et dépôt ATM ("Vous êtes trop loin du distributeur") : le point de référence "avant-bras" était l'objet PhysicsRig, resté au point de départ du niveau. PhysicalCashRigAnchorResolver.Resolve utilise maintenant la tête du joueur pour toutes les vérifications de distance.
- Dépôt ATM : après un refus, la demande n'est plus renvoyée toutes les 0,12 s (≈ 8 s d'attente) ; fin de la rafale de "Patientez avant un nouveau dépôt".
- Écran du distributeur : le bouton EJECTER MA CARTE était exactement superposé au bouton RETIRER 100 EUR (impossible de reprendre la carte). Mise en page refaite sans chevauchement.
- Carte insérée : remise droite (face parallèle à la plaque du lecteur, grand côté horizontal) et plaquée à 6 mm devant la plaque.
- Portefeuille : la sortie d'espèces demande maintenant de maintenir la gâchette ~0,7 s (avec pourcentage affiché), zone réduite à 13 cm ; plus de billets sortis par accident.

# 3.8.1 — correction de compilation
- AtmPhysicalInteraction : la constante CardInsertedOffset utilisée par LockCard n'était pas déclarée (erreur CS0103 ligne 444 du build GitHub Actions n°5). Déclarée.

# 3.9.0 (non compilé ici ; corrections tirées du journal 26-11-14_19-26-54)
- Carte bancaire qui n'apparaissait plus : le rappel de création arrivait avant que LabFusion attache le composant d'arme (WeaponSlotExtender) ; la carte était alors détruite avec "Composant WeaponSlot absent". La création est maintenant mise en attente et relancée toutes les 50 ms pendant 5 s (BankCardHolsterSpawner.Update, branché sur EconomyLifecycleMod.Update). L'enregistrement bancaire n'a lieu qu'après l'insertion physique, donc aucun compte n'est modifié si la création échoue.
- Note : les messages LabFusion "Handler ... was already registered" sont bénins (module enregistré deux fois) et n'empêchent rien.

# 3.9.1 (non compilé ici ; d'après le journal 26-11-14_19-47-42)
- Dépôt d'argent au distributeur : l'hôte répondait « Insérez votre carte bancaire dans le lecteur » alors que la carte était dans la fente. Sa recherche de la carte ne remontait pas aux parents de l'objet (le joueur, lui, le faisait) et ne comparait pas avec la carte annoncée. Recherche alignée sur celle du joueur (collider, rigidbody, 8 parents) avec préférence pour la carte demandée.
- Carte bancaire visible des deux côtés : suppression des faces arrière désactivée (cull off) sur ses matériaux.
- Création de la carte : attente du composant d'arme portée de 5 à 15 s (un échec « WeaponSlot absent » apparaissait au premier essai).

# 3.10.0 (non compilé ici ; d'après le journal 26-11-15_1-51-27 et les captures)
- Carte bancaire : création fiable — si l'extender LabFusion tarde, le composant d'arme du jeu est lu directement sur l'objet créé (les 2 premiers essais du journal échouaient après 15 s).
- Carte au distributeur : insérée par la tranche, à plat, dans l'axe de la fente (plus de carte plaquée de face).
- Distributeur surélevé : piédestal de 45 cm sous l'origine du modèle, posé 45 cm plus haut (fente carte et plateau d'espèces à hauteur de hanche). Aperçu de placement décalé de même. Les distributeurs déjà posés restent à l'ancienne hauteur.
- Poussée au holster : colliders des visuels (objets porteurs et carte) passés en déclencheurs.
- Selfie : MSAA 4x activé sur la caméra et ses textures (cheveux blancs).
- Téléphone : fond d'écran par défaut = image « LA RUE RP » (accueil et écran verrouillé) tant qu'aucune photo n'est choisie.

# 4.0.0 (non compilé ici — aucun SDK .NET dans le bac à sable ; d'après le journal 26-11-15_2-21-55)
- **Carte et espèces qui ne s'insèrent plus** : depuis la 3.10.0 les colliders de la carte sont des déclencheurs, donc la fente du distributeur ne la « voyait » plus (et sans carte, le dépôt d'espèces est refusé). La détection se fait maintenant PAR POSITION (BankCardHolsterMetadataNetwork.TryFindCardInBox) côté joueur ET côté hôte, avec repli par colliders en `QueryTriggerInteraction.Collide`.
- **Liasses de 50 billets** (au lieu de 100) : retrait au distributeur (bouton « MODE : LIASSES x50 »), sortie par le portefeuille (A fait défiler jusqu'aux liasses 5/10/20/50/100 €), dépôt/rangement comme les billets.
- **Missions réelles** (Integration/MissionWorld.cs) : les 33 missions de tous les métiers ont un objectif dans le monde (balise à rejoindre, poste de travail, rencontre d'un autre joueur, objets à ramasser, briefing automatique). La validation manuelle est refusée quand le suivi est actif.
  - Éboueur : une pince et un sac apparaissent ; des petits tas (modèle `PoopPile`) apparaissent ; les toucher avec la pince ou le sac les ramasse ; **5 € tous les 5**.
- **Imprimante à billets** (modèle `MoneyPrinter`, Integration/MoneyPrinterService.cs) : imprime 25 € / 20 s dans son bac (1 000 € max) ; rester près du bac 1,5 s = récupérer ; un policier saisit (prime de 20 %).
- **Modèles refaits** : radio de police (antenne, molettes, PTT, écran, clavier, clip), menottes (anneaux, serrures à cliquet, chaîne), porte de sécurité (volant, pênes, charnières, hublot), + `PoopPile`, `MissionMarker`, `MoneyPrinter`. Générés par `Tools/GenerateModelsV2.py`.

# 5.0.0 (non compilé ici — aucun SDK .NET dans le bac à sable)
- **Carte au distributeur** : enfoncée de ~4,4 cm de plus dans la fente (CardInsertedOffset z = +0,035).
- **Salaires réalistes** (toutes les 15 min, 1er grade -> +palier par grade) : éboueurs 35 €, épicerie 38 €, concession 42 €, armurerie 45 €, police 50 €, mairie 52 €, banque 55 €, médecins 55 €, gangs 15 € (+10–12 €/grade). Une entrée dans economy-config.json garde la priorité.
- **Missions** : récompenses ramenées à 40–500 € (pondérées par métier), délai de 10 min ; étapes plus longues (travail 25 s, rencontre 12 s, balises à 40–75 m, déchets par 10 avec vagues de 5, 5 € tous les 5).
- **Coffres** : le coffre-fort de la banque n'est accessible (ouvrir, verrouiller, déposer, retirer, consulter) qu'au directeur adjoint et au-dessus ; nouveau **Trésor municipal** (`TownTreasury`, modèle bleu nuit/or, portefeuille `system:town_treasury`) réservé au premier adjoint et au maire.
- **Nouveau métier : Concessionnaire** (5 grades, 3 missions, salaire, kit). Achat de véhicule : 400 € par n'importe quel joueur si un concessionnaire est connecté (15 % de commission au vendeur, le reste au trésor municipal).
- **Kits d'embauche/débauche** (Integration/JobPerks.cs) : à l'embauche, l'équipement apparaît devant le joueur ; à la débauche, au changement de métier ou de grade, il est retiré/remplacé. Police : radio, menottes, insigne, carnet + armes du jeu (pistolet, arme légère dès le grade 2, fusil au grade 4). Gangs : armes du jeu selon le grade. Menu « Mon métier : équipement » pour redemander son kit.
- **Armes et véhicules du jeu** (Integration/GameItemService.cs) : recherche par titre parmi les crates réellement chargées (aucun code-barres supposé). La liste complète est écrite dans UserData/FusionDarkRP/spawnable-titles.txt pour ajuster les mots-clés. Véhicules : police (service), gangs (25 €, grade >= 1), concessionnaire (démo), autres (achat).
- **Culture du cannabis pour les gangs** (Integration/DrugFarmService.cs) : planter (40 €, 4 plants max), 3 stades visibles (`WeedPot1/2/3`), récolte = 3 sachets physiques (`WeedBag`), vente au receleur (40–70 €/sachet, +20 % gangs), saisie par la police avec prime, fanaison après 15 min. Menu « Gangs : culture et trafic ».
- Modèles : trésor municipal, plants, sachet (Tools/GenerateModelsV3.py).


# 5.7.0 (non compilé ici — aucun SDK .NET dans le bac à sable)
- Concessionnaire : catalogue dynamique lu dans l'AssetWarehouse (RP/VehicleInventory.cs) remplaçant la liste figée ; vente par code-barres exact (GameItemService.TryGiveBarcode) ; menu paginé par catégorie (veh.list) ; vehicles.json passe en version 4 (ancien fichier sauvegardé en .ancien.bak).
- Vente : remboursement automatique si l'apparition échoue ; cooldown après succès seulement ; message si l'ancien véhicule est retiré.
- Corrigé : GitHub Actions régénérait les modèles avec GenerateModels.py seul (écrasait 49 fichiers V2–V7) -> contrôle Tools/VerifyAssets.py ; Tools/GenerateAll.py ajouté ; version du BUILD_INFO lue dans PROJECT_VERSION.txt.
- Corrigé : menu cannabis (60 € / 15 min), imprimante (10 € / 30 s, 600 € max), salaire citoyen 50 -> 20 € (migration unique), code mort supprimé (VehicleInfo, IsAvailable, VehicleWords).
- Prix de chaque véhicule réglable dans le menu concessionnaire (veh.price, RP/VehicleInventory.TrySetPrice) ; les règles Overrides s'appliquent désormais toutes (premier nom/prix/catégorie trouvé).
