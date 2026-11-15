
using System;
using System.Collections.Generic;
using System.Globalization;

namespace FusionDarkRP.Economy
{
    public sealed class PhysicalCashItem
    {
        public string Id { get; }
        public string DisplayName { get; }
        public long ValueCents { get; }
        public bool IsBundle { get; }

        public PhysicalCashItem(
            string id,
            string displayName,
            long valueCents,
            bool isBundle)
        {
            Id = id;
            DisplayName = displayName;
            ValueCents = valueCents;
            IsBundle = isBundle;
        }
    }

    public static class PhysicalCashCatalog
    {
        private static readonly Dictionary<string, PhysicalCashItem> Items =
            new Dictionary<string, PhysicalCashItem>(
                StringComparer.OrdinalIgnoreCase)
            {
                // BILLETS EURO
                {
                    "Note_5EUR",
                    new PhysicalCashItem(
                        "Note_5EUR", "Billet de 5 €", 500, false)
                },
                {
                    "Note_10EUR",
                    new PhysicalCashItem(
                        "Note_10EUR", "Billet de 10 €", 1000, false)
                },
                {
                    "Note_20EUR",
                    new PhysicalCashItem(
                        "Note_20EUR", "Billet de 20 €", 2000, false)
                },
                {
                    "Note_50EUR",
                    new PhysicalCashItem(
                        "Note_50EUR", "Billet de 50 €", 5000, false)
                },
                {
                    "Note_100EUR",
                    new PhysicalCashItem(
                        "Note_100EUR", "Billet de 100 €", 10000, false)
                },

                // LIASSES DE BILLETS
                // Chaque liasse représente 50 billets.
                {
                    "Band_5EUR",
                    new PhysicalCashItem(
                        "Band_5EUR", "Liasse de 5 € (50 billets)",
                        25000, true)
                },
                {
                    "Band_10EUR",
                    new PhysicalCashItem(
                        "Band_10EUR", "Liasse de 10 € (50 billets)",
                        50000, true)
                },
                {
                    "Band_20EUR",
                    new PhysicalCashItem(
                        "Band_20EUR", "Liasse de 20 € (50 billets)",
                        100000, true)
                },
                {
                    "Band_50EUR",
                    new PhysicalCashItem(
                        "Band_50EUR", "Liasse de 50 € (50 billets)",
                        250000, true)
                },
                {
                    "Band_100EUR",
                    new PhysicalCashItem(
                        "Band_100EUR", "Liasse de 100 € (50 billets)",
                        500000, true)
                },

                // PIECES EURO
                {
                    "Coin_1Cent",
                    new PhysicalCashItem(
                        "Coin_1Cent", "Pièce de 1 centime", 1, false)
                },
                {
                    "Coin_2Cent",
                    new PhysicalCashItem(
                        "Coin_2Cent", "Pièce de 2 centimes", 2, false)
                },
                {
                    "Coin_5Cent",
                    new PhysicalCashItem(
                        "Coin_5Cent", "Pièce de 5 centimes", 5, false)
                },
                {
                    "Coin_10Cent",
                    new PhysicalCashItem(
                        "Coin_10Cent", "Pièce de 10 centimes", 10, false)
                },
                {
                    "Coin_20Cent",
                    new PhysicalCashItem(
                        "Coin_20Cent", "Pièce de 20 centimes", 20, false)
                },
                {
                    "Coin_50Cent",
                    new PhysicalCashItem(
                        "Coin_50Cent", "Pièce de 50 centimes", 50, false)
                },
                {
                    "Coin_1EUR",
                    new PhysicalCashItem(
                        "Coin_1EUR", "Pièce de 1 €", 100, false)
                },
                {
                    "Coin_2EUR",
                    new PhysicalCashItem(
                        "Coin_2EUR", "Pièce de 2 €", 200, false)
                }
            };

        public static bool TryGet(
            string spawnableId,
            out PhysicalCashItem item)
        {
            item = null;

            if (string.IsNullOrWhiteSpace(spawnableId))
                return false;

            string normalized = spawnableId.Trim();

            int separator = normalized.LastIndexOf('.');

            if (separator >= 0 &&
                separator < normalized.Length - 1)
            {
                normalized = normalized.Substring(separator + 1);
            }

            return Items.TryGetValue(normalized, out item);
        }

        public static bool TryGetValue(
            string spawnableId,
            out long valueCents)
        {
            valueCents = 0;

            if (!TryGet(spawnableId, out PhysicalCashItem item))
                return false;

            valueCents = item.ValueCents;
            return true;
        }

        public static IReadOnlyCollection<PhysicalCashItem> GetAll()
        {
            return Items.Values;
        }

        public static string FormatMoney(long cents)
        {
            return (cents / 100m).ToString(
                "N2",
                CultureInfo.GetCultureInfo("fr-FR"))
                + " €";
        }
    }
}
