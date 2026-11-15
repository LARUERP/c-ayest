
using System;
using System.Collections.Generic;

namespace FusionDarkRP.Economy
{
    public enum WorldModelCategory
    {
        BankMachine,
        BankFurniture,
        BankSecurity,
        BankStorage,
        BankCard,
        CashNote,
        CashCoin,
        CashBundle,
        RpEquipment
    }

    public sealed class WorldModelDefinition
    {
        public string Id { get; private set; }
        public string DisplayName { get; private set; }
        public WorldModelCategory Category { get; private set; }
        public bool EnablePhysics { get; private set; }
        public bool IsInteractable { get; private set; }
        public float Scale { get; private set; }

        public WorldModelDefinition(
            string id,
            string displayName,
            WorldModelCategory category,
            bool enablePhysics,
            bool isInteractable,
            float scale = 1f)
        {
            Id = id;
            DisplayName = displayName;
            Category = category;
            EnablePhysics = enablePhysics;
            IsInteractable = isInteractable;
            Scale = scale > 0f ? scale : 1f;
        }
    }

    public static class RuntimeObjWorldModelCatalog
    {
        private static readonly Dictionary<string, WorldModelDefinition> Models =
            new Dictionary<string, WorldModelDefinition>(
                StringComparer.OrdinalIgnoreCase)
            {
                {
                    "ATM",
                    new WorldModelDefinition(
                        "ATM", "Distributeur automatique",
                        WorldModelCategory.BankMachine, false, true)
                },
                {
                    "BankCard",
                    new WorldModelDefinition(
                        "BankCard", "Carte bancaire",
                        WorldModelCategory.BankCard, true, true)
                },
                {
                    "BankCounter",
                    new WorldModelDefinition(
                        "BankCounter", "Comptoir bancaire",
                        WorldModelCategory.BankFurniture, false, false)
                },
                {
                    "BankOfficeChair",
                    new WorldModelDefinition(
                        "BankOfficeChair", "Chaise de bureau bancaire",
                        WorldModelCategory.BankFurniture, false, false)
                },
                {
                    "BankOfficeDesk",
                    new WorldModelDefinition(
                        "BankOfficeDesk", "Bureau bancaire",
                        WorldModelCategory.BankFurniture, false, false)
                },
                {
                    "BankSecurityDoor",
                    new WorldModelDefinition(
                        "BankSecurityDoor", "Porte de sécurité bancaire",
                        WorldModelCategory.BankSecurity, false, true)
                },
                {
                    "PoliceBadge",
                    new WorldModelDefinition(
                        "PoliceBadge", "Insigne de police",
                        WorldModelCategory.RpEquipment, true, false)
                },
                {
                    "PoliceCuffs",
                    new WorldModelDefinition(
                        "PoliceCuffs", "Menottes",
                        WorldModelCategory.RpEquipment, true, false)
                },
                {
                    "PoliceRadio",
                    new WorldModelDefinition(
                        "PoliceRadio", "Radio de police",
                        WorldModelCategory.RpEquipment, true, false)
                },
                {
                    "MedicalKit",
                    new WorldModelDefinition(
                        "MedicalKit", "Trousse médicale",
                        WorldModelCategory.RpEquipment, true, false)
                },
                {
                    "Defibrillator",
                    new WorldModelDefinition(
                        "Defibrillator", "Défibrillateur",
                        WorldModelCategory.RpEquipment, true, false)
                },
                {
                    "TownBadge",
                    new WorldModelDefinition(
                        "TownBadge", "Insigne municipal",
                        WorldModelCategory.RpEquipment, true, false)
                },
                {
                    "ArmoryCase",
                    new WorldModelDefinition(
                        "ArmoryCase", "Mallette d'armurerie",
                        WorldModelCategory.RpEquipment, true, false)
                },
                {
                    "ShopCrate",
                    new WorldModelDefinition(
                        "ShopCrate", "Caisse de marchandises",
                        WorldModelCategory.RpEquipment, true, false)
                },
                {
                    "BusinessTablet",
                    new WorldModelDefinition(
                        "BusinessTablet", "Tablette de commerce",
                        WorldModelCategory.RpEquipment, true, false)
                },
                {
                    "MobileStand",
                    new WorldModelDefinition(
                        "MobileStand", "Étal mobile",
                        WorldModelCategory.RpEquipment, true, false)
                },
                {
                    "TrashBag",
                    new WorldModelDefinition(
                        "TrashBag", "Sac poubelle",
                        WorldModelCategory.RpEquipment, true, false)
                },
                {
                    "TrashGrabber",
                    new WorldModelDefinition(
                        "TrashGrabber", "Pince de ramassage",
                        WorldModelCategory.RpEquipment, true, false)
                },
                {
                    "PoliceNotepad",
                    new WorldModelDefinition(
                        "PoliceNotepad", "Carnet de procès-verbal",
                        WorldModelCategory.RpEquipment, true, false)
                },
                {
                    "ShopScanner",
                    new WorldModelDefinition(
                        "ShopScanner", "Scanner de caisse",
                        WorldModelCategory.RpEquipment, true, false)
                },
                {
                    "TownFolder",
                    new WorldModelDefinition(
                        "TownFolder", "Dossier administratif",
                        WorldModelCategory.RpEquipment, true, false)
                },
                {
                    "WorkGloves",
                    new WorldModelDefinition(
                        "WorkGloves", "Gants de collecte",
                        WorldModelCategory.RpEquipment, true, false)
                },
                {
                    "ArmoryCatalog",
                    new WorldModelDefinition(
                        "ArmoryCatalog", "Catalogue d'armurerie",
                        WorldModelCategory.RpEquipment, true, false)
                },
                {
                    "BankTerminal",
                    new WorldModelDefinition(
                        "BankTerminal", "Terminal bancaire",
                        WorldModelCategory.RpEquipment, true, false)
                },
                {
                    "PoopPile",
                    new WorldModelDefinition(
                        "PoopPile", "Déchet de rue (mission)",
                        WorldModelCategory.RpEquipment, true, false)
                },
                {
                    "MissionMarker",
                    new WorldModelDefinition(
                        "MissionMarker", "Balise de mission",
                        WorldModelCategory.RpEquipment, false, false)
                },
                {
                    "MoneyPrinter",
                    new WorldModelDefinition(
                        "MoneyPrinter", "Imprimante à billets",
                        WorldModelCategory.RpEquipment, false, true)
                },
                {
                    "WeedPot1",
                    new WorldModelDefinition(
                        "WeedPot1", "Semis de cannabis",
                        WorldModelCategory.RpEquipment, false, false)
                },
                {
                    "WeedPot2",
                    new WorldModelDefinition(
                        "WeedPot2", "Cannabis en croissance",
                        WorldModelCategory.RpEquipment, false, false)
                },
                {
                    "WeedPot3",
                    new WorldModelDefinition(
                        "WeedPot3", "Cannabis mûr",
                        WorldModelCategory.RpEquipment, false, false)
                },
                {
                    "WeedBag",
                    new WorldModelDefinition(
                        "WeedBag", "Sachet de récolte",
                        WorldModelCategory.RpEquipment, true, false)
                },
                {
                    "TownTreasury",
                    new WorldModelDefinition(
                        "TownTreasury", "Trésor municipal",
                        WorldModelCategory.BankStorage, false, true)
                },
                {
                    "BankSecurityDoorFrame",
                    new WorldModelDefinition(
                        "BankSecurityDoorFrame",
                        "Cadre de porte de sécurité",
                        WorldModelCategory.BankSecurity, false, false)
                },
                {
                    "BankVault",
                    new WorldModelDefinition(
                        "BankVault", "Coffre-fort bancaire",
                        WorldModelCategory.BankStorage, false, true)
                },
                {
                    "WallSafe",
                    new WorldModelDefinition(
                        "WallSafe", "Coffre mural",
                        WorldModelCategory.BankStorage, false, true)
                },

                {
                    "Note_5EUR",
                    new WorldModelDefinition(
                        "Note_5EUR", "Billet de 5 €",
                        WorldModelCategory.CashNote, true, true)
                },
                {
                    "Note_10EUR",
                    new WorldModelDefinition(
                        "Note_10EUR", "Billet de 10 €",
                        WorldModelCategory.CashNote, true, true)
                },
                {
                    "Note_20EUR",
                    new WorldModelDefinition(
                        "Note_20EUR", "Billet de 20 €",
                        WorldModelCategory.CashNote, true, true)
                },
                {
                    "Note_50EUR",
                    new WorldModelDefinition(
                        "Note_50EUR", "Billet de 50 €",
                        WorldModelCategory.CashNote, true, true)
                },
                {
                    "Note_100EUR",
                    new WorldModelDefinition(
                        "Note_100EUR", "Billet de 100 €",
                        WorldModelCategory.CashNote, true, true)
                },

                {
                    "Coin_1Cent",
                    new WorldModelDefinition(
                        "Coin_1Cent", "Pièce de 1 centime",
                        WorldModelCategory.CashCoin, true, true)
                },
                {
                    "Coin_2Cent",
                    new WorldModelDefinition(
                        "Coin_2Cent", "Pièce de 2 centimes",
                        WorldModelCategory.CashCoin, true, true)
                },
                {
                    "Coin_5Cent",
                    new WorldModelDefinition(
                        "Coin_5Cent", "Pièce de 5 centimes",
                        WorldModelCategory.CashCoin, true, true)
                },
                {
                    "Coin_10Cent",
                    new WorldModelDefinition(
                        "Coin_10Cent", "Pièce de 10 centimes",
                        WorldModelCategory.CashCoin, true, true)
                },
                {
                    "Coin_20Cent",
                    new WorldModelDefinition(
                        "Coin_20Cent", "Pièce de 20 centimes",
                        WorldModelCategory.CashCoin, true, true)
                },
                {
                    "Coin_50Cent",
                    new WorldModelDefinition(
                        "Coin_50Cent", "Pièce de 50 centimes",
                        WorldModelCategory.CashCoin, true, true)
                },
                {
                    "Coin_1EUR",
                    new WorldModelDefinition(
                        "Coin_1EUR", "Pièce de 1 €",
                        WorldModelCategory.CashCoin, true, true)
                },
                {
                    "Coin_2EUR",
                    new WorldModelDefinition(
                        "Coin_2EUR", "Pièce de 2 €",
                        WorldModelCategory.CashCoin, true, true)
                },

                {
                    "Band_5EUR",
                    new WorldModelDefinition(
                        "Band_5EUR", "Liasse de billets de 5 €",
                        WorldModelCategory.CashBundle, true, true)
                },
                {
                    "Band_10EUR",
                    new WorldModelDefinition(
                        "Band_10EUR", "Liasse de billets de 10 €",
                        WorldModelCategory.CashBundle, true, true)
                },
                {
                    "Band_20EUR",
                    new WorldModelDefinition(
                        "Band_20EUR", "Liasse de billets de 20 €",
                        WorldModelCategory.CashBundle, true, true)
                },
                {
                    "Band_50EUR",
                    new WorldModelDefinition(
                        "Band_50EUR", "Liasse de billets de 50 €",
                        WorldModelCategory.CashBundle, true, true)
                },
                {
                    "Band_100EUR",
                    new WorldModelDefinition(
                        "Band_100EUR", "Liasse de billets de 100 €",
                        WorldModelCategory.CashBundle, true, true)
                }
            };

        public static bool TryGet(
            string modelId,
            out WorldModelDefinition definition)
        {
            definition = null;

            if (string.IsNullOrWhiteSpace(modelId))
                return false;

            string normalized = modelId.Trim();

            if (normalized.EndsWith(
                ".obj",
                StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(
                    0,
                    normalized.Length - 4);
            }

            return Models.TryGetValue(normalized, out definition);
        }

        public static IReadOnlyCollection<WorldModelDefinition> GetAll()
        {
            return Models.Values;
        }

        public static string[] GetAllIds()
        {
            string[] ids = new string[Models.Count];
            Models.Keys.CopyTo(ids, 0);
            Array.Sort(ids, StringComparer.OrdinalIgnoreCase);
            return ids;
        }

        public static bool IsPhysicalCash(string modelId)
        {
            WorldModelDefinition definition;

            if (!TryGet(modelId, out definition))
                return false;

            return definition.Category == WorldModelCategory.CashNote
                || definition.Category == WorldModelCategory.CashCoin
                || definition.Category == WorldModelCategory.CashBundle;
        }
    }
}
