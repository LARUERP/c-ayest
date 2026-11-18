using System;
using System.Collections.Generic;
using System.Linq;

namespace FusionDarkRP.RP
{
    public sealed class EquipmentDefinition
    {
        public string Id { get; set; }
        public string JobId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string ModelResource { get; set; }
        public int MinimumGrade { get; set; }
        public bool CosmeticOnly { get; set; }
    }

    /// <summary>Catalogue d'équipement RP. Les droits de spawn doivent rester validés par l'hôte.</summary>
    public static class JobEquipmentCatalog
    {
        private static readonly EquipmentDefinition[] items =
        {
            E("citizen_phone","citizen","Téléphone","Communication, photos et services RP.","Phone",0,true),
            E("citizen_wallet","citizen","Portefeuille de poignet","Affiche uniquement les espèces disponibles.","WristWallet",0,true),
            E("trash_gloves","eboueurs","Gants de collecte","Équipement de protection RP.","WorkGloves",0,true),
            E("trash_grabber","eboueurs","Pince de collecte","Outil de ramassage des déchets.","TrashGrabber",0,false),
            E("trash_bag","eboueurs","Sac de collecte","Sac de collecte visuel.","TrashBag",0,false),
            E("bank_card","banque","Carte bancaire","Carte personnelle à insérer et retirer des lecteurs.","BankCard",0,true),
            E("bank_terminal","banque","Terminal bancaire","Terminal de consultation avec consentement.","BankTerminal",0,true),
            E("town_badge","mairie","Badge municipal","Identification du personnel municipal.","TownBadge",0,true),
            E("town_folder","mairie","Dossier administratif","Documents de démarches RP.","TownFolder",0,true),
            E("shop_scanner","epicerie","Scanner de caisse","Outil de gestion du passage en caisse.","ShopScanner",0,false),
            E("shop_crate","epicerie","Caisse de marchandises","Caisse de réapprovisionnement.","ShopCrate",0,false),
            E("police_radio","police","Radio de service","Communications de patrouille.","PoliceRadio",0,true),
            E("police_badge","police","Insigne de police","Identification professionnelle.","PoliceBadge",0,true),
            E("police_cuffs","police","Menottes RP","Accessoire d'interaction RP contrôlé par permissions.","PoliceCuffs",0,false),
            E("police_notepad","police","Carnet de procès-verbal","Rédaction des rapports et amendes RP.","PoliceNotepad",0,true),
            E("medical_kit","medecins","Trousse médicale","Matériel de soin RP.","MedicalKit",0,false),
            E("medical_defib","medecins","Défibrillateur RP","Accessoire de prise en charge RP.","Defibrillator",2,false),
            E("district_phone","marseillais","Téléphone de quartier","Communication et contacts RP.","Phone",0,true),
            E("business_tablet","parisiens","Tablette professionnelle","Gestion des contacts et contrats RP.","BusinessTablet",1,true),
            E("mobile_stand","gens_du_voyage","Stand itinérant","Présentoir pliable de commerce RP.","MobileStand",0,false),
            E("mobile_crate","gens_du_voyage","Caisse de transport","Transport de marchandises RP.","ShopCrate",0,false),
            E("armory_catalog","armurerie","Catalogue armurerie","Catalogue de vente et autorisations RP.","ArmoryCatalog",0,true),
            E("armory_case","armurerie","Valise de transport","Valise d'équipement visuelle.","ArmoryCase",1,false),
            E("armory_stock_tablet","armurerie","Terminal de stock","Gestion des inventaires de l'armurerie.","BusinessTablet",2,true),
            E("dealer_tablet","concessionnaire","Tablette de vente","Catalogue du parc et suivi des ventes RP.","BusinessTablet",0,true),
            E("dealer_keys","concessionnaire","Clés de voiture","Remise des clés au client après la vente.","CarKeys",0,false)
        };

        private static EquipmentDefinition E(string id, string job, string name, string description,
            string model, int grade, bool cosmetic)
        {
            return new EquipmentDefinition
            {
                Id = id, JobId = job, Name = name, Description = description,
                ModelResource = model, MinimumGrade = grade, CosmeticOnly = cosmetic
            };
        }

        public static IReadOnlyList<EquipmentDefinition> All { get { return items; } }
        public static IReadOnlyList<EquipmentDefinition> ForJob(string jobId, int gradeIndex)
        {
            return items.Where(x => string.Equals(x.JobId, jobId, StringComparison.OrdinalIgnoreCase)
                && gradeIndex >= x.MinimumGrade).ToArray();
        }
    }
}
