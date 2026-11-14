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
