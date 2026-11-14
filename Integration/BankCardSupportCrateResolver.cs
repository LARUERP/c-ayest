
using System;
using Il2CppSLZ.Marrow.Data;
using Il2CppSLZ.Marrow.Warehouse;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Recherche le support Eder 22 dans les armes
    /// réellement chargées par BONELAB.
    /// Ne dépend pas d'un code-barres supposé.
    /// </summary>
    public static class BankCardSupportCrateResolver
    {
        private const string ExpectedCrateName = "eder22";

        public static bool TryResolve(
            out Spawnable spawnable,
            out string barcode,
            out string error)
        {
            return TryResolve(
                ExpectedCrateName,
                out spawnable,
                out barcode,
                out error);
        }

        /// <summary>
        /// Résout un SpawnableCrate réellement chargé par BONELAB
        /// à partir de son titre (comparaison normalisée).
        /// </summary>
        public static bool TryResolve(
            string crateName,
            out Spawnable spawnable,
            out string barcode,
            out string error)
        {
            string expectedName = NormalizeName(crateName);

            spawnable = null;
            barcode = null;
            error = null;

            try
            {
                if (!AssetWarehouse.ready ||
                    AssetWarehouse.Instance == null)
                {
                    error =
                        "L'Asset Warehouse de BONELAB n'est pas prêt.";

                    return false;
                }

                var crates =
                    AssetWarehouse.Instance.GetCrates<SpawnableCrate>();

                if (crates == null)
                {
                    error =
                        "Impossible de récupérer les armes chargées.";

                    return false;
                }

                SpawnableCrate match = null;

                foreach (SpawnableCrate crate in crates)
                {
                    if (crate == null)
                        continue;

                    string normalizedName =
                        NormalizeName(crate.Title);

                    if (!string.Equals(
                        normalizedName,
                        expectedName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (match != null)
                    {
                        error =
                            "Plusieurs supports Eder 22 ont été trouvés. "
                            + "Sélection automatique annulée.";

                        return false;
                    }

                    match = crate;
                }

                if (match == null)
                {
                    error =
                        "Eder 22 introuvable dans les armes chargées. "
                        + "Aucun code-barres inventé ne sera utilisé.";

                    return false;
                }

                if (match.Barcode == null ||
                    string.IsNullOrWhiteSpace(match.Barcode.ID))
                {
                    error =
                        "Le code-barres réel de l'Eder 22 est invalide.";

                    return false;
                }

                barcode = match.Barcode.ID;

                SpawnableCrateReference reference =
                    new SpawnableCrateReference(barcode);

                if (reference.Crate == null)
                {
                    spawnable = null;
                    barcode = null;

                    error =
                        "BONELAB n'a pas réussi à résoudre "
                        + "la référence de l'Eder 22.";

                    return false;
                }

                spawnable = new Spawnable
                {
                    crateRef = reference,
                    policyData = null
                };

                error = null;
                return true;
            }
            catch (Exception exception)
            {
                spawnable = null;
                barcode = null;

                error =
                    "Erreur de recherche du support Eder 22 : "
                    + exception.Message;

                return false;
            }
        }

        private static string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            System.Text.StringBuilder result =
                new System.Text.StringBuilder();

            foreach (char character in value)
            {
                if (char.IsLetterOrDigit(character))
                    result.Append(char.ToLowerInvariant(character));
            }

            return result.ToString();
        }
    }
}
