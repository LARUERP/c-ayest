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
