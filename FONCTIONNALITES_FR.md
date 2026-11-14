# Fonctionnalités de la version 3.0.0

Toutes sont implémentées dans le code source ; aucune n'a été compilée ni testée en jeu par l'auteur de cette archive (voir AUDIT_FINAL_FR.md).

| Domaine | Contenu |
|---|---|
| Économie | Euros ; espèces + banque ; salaires par métier/grade ; transferts atomiques ; historique ; sauvegarde persistante ; anti-duplication (identifiants de requête) |
| Argent physique | Billets 5/10/20/50/100, pièces, liasses ; objets saisissables via porteur BONELAB synchronisé ; ramassage et dépôt ATM validés par l'hôte |
| ATM | Écran VR orienté vers le joueur ; carte insérable ; solde ; dépôt d'espèces physiques ; retraits 5/10/20/50/100 € |
| Poste bancaire | Interface réservée aux banquiers avec consentement du client |
| Carte bancaire | Objet physique, holster, insertion/retrait automatique |
| Porte de sécurité | Ouverture animée synchronisée ; verrouillage (banque/mairie/hôte) ; crochetage chronométré avec alerte |
| Coffre-fort bancaire | Réserve persistante de la banque ; dépôt/retrait en espèces (banquiers) ; crochetage avec butin plafonné et alerte |
| Coffre mural | Coffre personnel (réservation par le premier joueur) ; contenu persistant par propriétaire ; crochetage |
| Téléphone | Contacts, SMS, appels, médias, notes vocales, haut-parleur, PIN, application bancaire (intégré à la DLL) |
| Métiers | 10 métiers, grades, recrutement/promotion/licenciement, équipements par métier |
| Missions | 33 missions : démarrer, valider les étapes, récompense en espèces, délai de recharge |
| Factures | Création par n'importe quel joueur (proximité), paiement banque/espèces, refus, expiration |
| Police | Amendes (trésor municipal), recherchés, arrestation avec amende et prime |
| Soins | Soin du patient par un médecin (santé complète appliquée sur le client du patient) |
| Administration | Hôte : donner de l'argent, voir un solde, voir le trésor ; placement libre des objets |
| Modèles 3D | 40 modèles originaux texturés (argent, banque, mobilier, 12 équipements de métiers apparaissables), optimisés Quest |
| Réseau | Autorité de l'hôte, validation serveur, cadence limitée, arrivée tardive, nettoyage des déconnexions |

## Non fourni (nécessite des ressources externes)
- Prefabs Marrow personnalisés (l'objet est saisi via le porteur BONELAB choisi).
- Mobilier fixe (ATM, coffres) non saisissable par conception.
- Images/vidéos d'appel vidéo : dépend des capacités du téléphone existant.
