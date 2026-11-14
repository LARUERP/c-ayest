using System;
using System.Collections.Generic;
using System.Linq;

namespace FusionDarkRP.RP
{
    public sealed class MissionStepDefinition
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Instruction { get; set; }
    }

    public sealed class JobMissionDefinition
    {
        public string Id { get; set; }
        public string JobId { get; set; }
        public string Title { get; set; }
        public string Briefing { get; set; }
        public long RewardCents { get; set; }
        public int ReputationReward { get; set; }
        public int CooldownMinutes { get; set; }
        public MissionStepDefinition[] Steps { get; set; }
    }

    public sealed class JobEquipmentDefinition
    {
        public string Id { get; set; }
        public string JobId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string ModelResource { get; set; }
        public bool Required { get; set; }
    }

    /// <summary>Catalogue de contenu RP, indépendant des objets Unity et exploitable par l'hôte.</summary>
    public static class JobMissionCatalog
    {
        private static MissionStepDefinition[] Steps(params (string id, string title, string instruction)[] steps)
        {
            return steps.Select(x => new MissionStepDefinition
            {
                Id = x.id, Title = x.title, Instruction = x.instruction
            }).ToArray();
        }

        private static JobMissionDefinition Mission(string id, string job, string title,
            string briefing, int reward, int rep, params (string, string, string)[] steps)
        {
            return new JobMissionDefinition
            {
                Id = id, JobId = job, Title = title, Briefing = briefing,
                RewardCents = checked((long)reward * 100L),
                ReputationReward = rep, CooldownMinutes = 5, Steps = Steps(steps)
            };
        }

        private static readonly JobMissionDefinition[] missions =
        {
            Mission("citizen_errand","citizen","Course de quartier","Réaliser une course et remettre les achats au demandeur.",3500,3,
                ("accept","Accepter la demande","Prendre connaissance de la liste."),
                ("collect","Récupérer les articles","Récupérer les articles demandés."),
                ("deliver","Livrer","Remettre les achats au destinataire.")),
            Mission("citizen_delivery","citizen","Livraison express","Acheminer un colis sans le perdre.",4500,4,
                ("pickup","Prendre le colis","Récupérer le colis auprès du commerçant."),
                ("route","Effectuer le trajet","Rejoindre le point de livraison."),
                ("handover","Confirmer la remise","Faire confirmer la remise par le destinataire.")),
            Mission("citizen_social","citizen","Vie de quartier","Participer à une activité collective.",2500,2,
                ("meet","Rencontrer un habitant","Échanger avec un autre joueur."),
                ("help","Rendre service","Aider à une tâche convenue."),
                ("report","Clore l'activité","Enregistrer la fin de l'activité.")),

            Mission("eboueurs_route","eboueurs","Tournée de collecte","Collecter les déchets de plusieurs zones.",9500,6,
                ("truck","Prendre le véhicule","Prendre le véhicule de service."),
                ("collect_a","Zone de collecte A","Collecter les déchets du premier point."),
                ("collect_b","Zone de collecte B","Collecter le second point."),
                ("depot","Centre de tri","Décharger au centre de tri.")),
            Mission("eboueurs_sort","eboueurs","Tri sélectif","Séparer les déchets valorisables.",7000,5,
                ("bags","Récupérer les sacs","Récupérer les sacs de collecte."),
                ("sort","Trier","Séparer les matières."),
                ("weigh","Peser","Enregistrer le poids trié.")),
            Mission("eboueurs_incident","eboueurs","Intervention propreté","Traiter un signalement urgent.",8000,5,
                ("dispatch","Prendre le signalement","Lire le signalement municipal."),
                ("secure","Sécuriser la zone","Balisage RP de la zone."),
                ("clean","Nettoyer","Terminer le nettoyage et clôturer.")),

            Mission("banque_customer","banque","Accueil bancaire","Traiter une opération avec consentement explicite du client.",8000,5,
                ("welcome","Accueillir le client","Ouvrir la demande du client."),
                ("consent","Consentement","Obtenir le consentement explicite du client."),
                ("operation","Opération","Effectuer uniquement l'opération autorisée."),
                ("receipt","Reçu","Remettre le reçu et clôturer.")),
            Mission("banque_audit","banque","Clôture de caisse","Vérifier le journal et clôturer la caisse.",11000,7,
                ("journal","Ouvrir le journal","Consulter les mouvements de la journée."),
                ("verify","Contrôler","Vérifier les écarts et justificatifs."),
                ("close","Clôturer","Valider la clôture de caisse.")),
            Mission("banque_card","banque","Émission de carte","Accompagner un client dans l'émission de sa carte.",9000,6,
                ("identity","Vérifier l'identité RP","Vérifier l'identité du personnage."),
                ("account","Vérifier le compte","Vérifier que le compte est actif."),
                ("issue","Remettre la carte","Émettre et remettre la carte au titulaire.")),

            Mission("mairie_dossier","mairie","Dossier citoyen","Traiter une demande administrative RP.",7500,5,
                ("receive","Recevoir le dossier","Enregistrer la demande."),
                ("check","Vérifier les pièces","Contrôler les pièces RP."),
                ("decision","Décision","Notifier la décision au demandeur.")),
            Mission("mairie_event","mairie","Événement municipal","Organiser une activité publique.",10000,7,
                ("plan","Planifier","Définir lieu et horaire."),
                ("approve","Faire valider","Obtenir la validation municipale."),
                ("host","Accueillir","Ouvrir et clôturer l'événement.")),
            Mission("mairie_permit","mairie","Autorisation commerciale","Instruire une demande d'autorisation.",8500,6,
                ("request","Recevoir la demande","Enregistrer l'établissement."),
                ("inspect","Inspection RP","Effectuer l'inspection administrative."),
                ("permit","Délivrer","Délivrer ou motiver le refus.")),

            Mission("epicerie_stock","epicerie","Réapprovisionnement","Réapprovisionner les rayons.",7000,4,
                ("stock","Vérifier le stock","Consulter les besoins."),
                ("boxes","Récupérer les cartons","Récupérer les marchandises."),
                ("shelves","Remplir les rayons","Mettre les produits en rayon.")),
            Mission("epicerie_till","epicerie","Service en caisse","Assurer un service client complet.",8000,5,
                ("open","Ouvrir la caisse","Démarrer la vacation."),
                ("serve","Servir","Enregistrer les achats."),
                ("close","Clôturer","Clôturer et vérifier la caisse.")),
            Mission("epicerie_order","epicerie","Commande fournisseur","Préparer une commande de marchandises.",9500,6,
                ("list","Préparer la liste","Établir les quantités."),
                ("order","Passer commande","Valider la commande."),
                ("receive","Réceptionner","Contrôler la livraison.")),

            Mission("police_patrol","police","Patrouille de secteur","Effectuer une patrouille et rédiger un rapport.",10000,6,
                ("brief","Prise de service","Prendre les consignes."),
                ("route","Patrouiller","Visiter les points du secteur."),
                ("report","Rapport","Enregistrer les observations.")),
            Mission("police_traffic","police","Contrôle routier","Réaliser un contrôle RP proportionné.",9000,5,
                ("safe","Choisir une zone sûre","Se placer dans une zone adaptée."),
                ("control","Contrôler","Effectuer le contrôle RP."),
                ("record","Compte-rendu","Documenter le résultat.")),
            Mission("police_inquiry","police","Enquête de proximité","Recueillir et recouper des témoignages.",13000,8,
                ("open","Ouvrir l'enquête","Créer le dossier."),
                ("witness","Témoignages","Recueillir deux témoignages."),
                ("submit","Transmettre","Transmettre le dossier au supérieur.")),
            Mission("police_emergency","police","Intervention urgente","Coordonner une intervention en équipe.",12000,8,
                ("dispatch","Recevoir l'appel","Lire les informations de l'appel."),
                ("coordinate","Coordonner","Informer les coéquipiers."),
                ("resolve","Résoudre","Sécuriser et clôturer l'intervention.")),

            Mission("medecins_call","medecins","Intervention médicale","Prendre en charge un appel de soin RP.",10000,6,
                ("call","Recevoir l'appel","Prendre les informations."),
                ("patient","Rejoindre le patient","Se rendre auprès du patient."),
                ("care","Effectuer les soins RP","Effectuer les soins et consigner l'intervention.")),
            Mission("medecins_round","medecins","Visite médicale","Effectuer une tournée de suivi.",8000,5,
                ("prepare","Préparer le matériel","Préparer la trousse."),
                ("visit_a","Premier patient","Effectuer le suivi RP."),
                ("visit_b","Second patient","Terminer la tournée.")),
            Mission("medecins_transport","medecins","Transport sanitaire","Organiser un transport sécurisé.",11000,7,
                ("dispatch","Prendre la mission","Confirmer la demande."),
                ("stabilize","Stabiliser","Préparer le patient au transport."),
                ("handoff","Transmettre","Remettre le patient à l'équipe d'accueil.")),

            Mission("marseillais_contact","marseillais","Réseau de quartier","Développer les relations locales.",7000,5,
                ("meet","Rencontre","Rencontrer un habitant."),
                ("help","Entraide","Rendre un service."),
                ("close","Retour","Confirmer la fin de l'activité.")),
            Mission("marseillais_event","marseillais","Événement de quartier","Organiser un rassemblement RP.",10000,7,
                ("place","Choisir le lieu","Définir un lieu adapté."),
                ("prepare","Préparer","Préparer le matériel."),
                ("host","Accueillir","Accueillir les participants.")),
            Mission("marseillais_trade","marseillais","Commerce local","Réaliser un échange commercial RP.",8500,6,
                ("offer","Préparer l'offre","Définir les biens et prix."),
                ("negotiate","Négocier","Convenir d'un accord."),
                ("record","Enregistrer","Confirmer l'échange.")),

            Mission("parisiens_meet","parisiens","Rendez-vous professionnel","Conclure une transaction RP.",9000,6,
                ("schedule","Planifier","Fixer un rendez-vous."),
                ("negotiate","Négocier","Présenter et discuter l'offre."),
                ("sign","Conclure","Enregistrer l'accord.")),
            Mission("parisiens_network","parisiens","Réseautage citadin","Participer à une activité professionnelle.",7500,5,
                ("join","Rejoindre","Participer à l'événement."),
                ("connect","Rencontrer","Rencontrer deux contacts."),
                ("summary","Bilan","Enregistrer les contacts RP.")),
            Mission("parisiens_business","parisiens","Développement commercial","Développer une activité commerciale.",12000,8,
                ("audit","Étudier le besoin","Identifier un besoin."),
                ("proposal","Proposer","Présenter une offre."),
                ("contract","Contrat","Valider le contrat RP.")),

            Mission("gens_du_voyage_trade","gens_du_voyage","Commerce itinérant","Installer un stand et tenir une vente.",9500,6,
                ("setup","Installer le stand","Installer le matériel."),
                ("sell","Présenter les articles","Présenter les articles aux clients."),
                ("pack","Ranger","Ranger le stand.")),
            Mission("gens_du_voyage_help","gens_du_voyage","Entraide communautaire","Aider un membre de la communauté.",7500,5,
                ("need","Identifier le besoin","Évaluer la demande."),
                ("assist","Apporter l'aide","Réaliser l'aide convenue."),
                ("confirm","Confirmer","Faire confirmer le service.")),
            Mission("gens_du_voyage_route","gens_du_voyage","Tournée itinérante","Effectuer une tournée de commerce.",10000,7,
                ("prepare","Préparer","Préparer les marchandises."),
                ("travel","Effectuer la tournée","Visiter les points prévus."),
                ("report","Bilan","Enregistrer les ventes.")),

            Mission("armurerie_counter","armurerie","Gestion du comptoir","Gérer une vente d'équipement RP autorisée.",10000,6,
                ("open","Ouvrir le comptoir","Ouvrir la boutique."),
                ("verify","Vérifier les autorisations RP","Contrôler les règles du serveur."),
                ("sale","Enregistrer la vente","Enregistrer la transaction.")),
            Mission("armurerie_stock","armurerie","Inventaire armurerie","Vérifier le stock et les écarts.",12000,8,
                ("count","Compter","Compter les articles."),
                ("compare","Comparer","Comparer avec le registre."),
                ("close","Valider","Valider l'inventaire.")),
            Mission("armurerie_order","armurerie","Commande fournisseur","Préparer et réceptionner une commande.",11000,7,
                ("catalog","Consulter le catalogue","Vérifier les références."),
                ("order","Commander","Créer la commande."),
                ("receive","Réceptionner","Contrôler la livraison."))
        };

        public static IReadOnlyList<JobMissionDefinition> AllMissions { get { return missions; } }

        public static IReadOnlyList<JobMissionDefinition> ForJob(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId)) return Array.Empty<JobMissionDefinition>();
            return missions.Where(x => string.Equals(x.JobId, jobId, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        public static JobMissionDefinition Get(string missionId)
        {
            return missions.FirstOrDefault(x => string.Equals(x.Id, missionId, StringComparison.OrdinalIgnoreCase));
        }
    }
}
