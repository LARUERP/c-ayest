using System;
using System.Globalization;

namespace FusionDarkRP.Telephone
{
    public static class PhoneBankRules
    {
        private static readonly CultureInfo FrenchCulture =
            CultureInfo.GetCultureInfo("fr-FR");

        public static bool Amount(string value, out long amountCents)
        {
            amountCents = 0L;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Trim()
                .Replace("€", "")
                .Replace(" ", "")
                .Replace("\u00A0", "")
                .Replace(',', '.');

            if (normalized.Length == 0)
                return false;

            if (!decimal.TryParse(
                normalized,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out decimal euros))
            {
                return false;
            }

            if (euros <= 0m)
                return false;

            if (euros > (decimal)long.MaxValue / 100m)
                return false;

            decimal cents = euros * 100m;

            if (cents != decimal.Truncate(cents))
                return false;

            amountCents = (long)cents;
            return amountCents > 0L;
        }

        public static string FormatAmount(long amountCents)
        {
            decimal euros = amountCents / 100m;

            return euros.ToString("#,##0.00", FrenchCulture) + " €";
        }
    }
}
