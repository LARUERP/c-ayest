using System;

namespace FusionDarkRP.Economy
{
    /// <summary>
    /// Résultat de validation d'une demande de création
    /// d'argent physique.
    /// </summary>
    public sealed class PhysicalCashSpawnValidation
    {
        public bool IsValid { get; private set; }
        public string PlayerId { get; private set; }
        public string ItemId { get; private set; }
        public long AmountCents { get; private set; }
        public string Error { get; private set; }

        private PhysicalCashSpawnValidation()
        {
        }

        public static PhysicalCashSpawnValidation Accept(
            string playerId,
            string itemId,
            long amountCents)
        {
            return new PhysicalCashSpawnValidation
            {
                IsValid = true,
                PlayerId = playerId,
                ItemId = itemId,
                AmountCents = amountCents,
                Error = null
            };
        }

        public static PhysicalCashSpawnValidation Reject(
            string error)
        {
            return new PhysicalCashSpawnValidation
            {
                IsValid = false,
                PlayerId = null,
                ItemId = null,
                AmountCents = 0,
                Error = error
            };
        }
    }

    /// <summary>
    /// Vérifie les demandes de création d'argent physique.
    /// Cette classe ne crée pas elle-même les objets Unity.
    /// </summary>
    public sealed class PhysicalCashSpawnValidator
    {
        private readonly long maximumItemValueCents;

        public PhysicalCashSpawnValidator(
            long maximumItemValueCents)
        {
            if (maximumItemValueCents <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumItemValueCents)
                );
            }

            this.maximumItemValueCents =
                maximumItemValueCents;
        }

        public PhysicalCashSpawnValidation Validate(
            bool isHost,
            string playerId,
            string itemId)
        {
            if (!isHost)
            {
                return PhysicalCashSpawnValidation.Reject(
                    "Seul l'hôte peut créer de l'argent physique."
                );
            }

            if (string.IsNullOrWhiteSpace(playerId))
            {
                return PhysicalCashSpawnValidation.Reject(
                    "Identifiant du joueur invalide."
                );
            }

            if (string.IsNullOrWhiteSpace(itemId))
            {
                return PhysicalCashSpawnValidation.Reject(
                    "Identifiant de l'objet manquant."
                );
            }

            if (!PhysicalCashCatalog.TryGetValue(
                itemId,
                out long amountCents))
            {
                return PhysicalCashSpawnValidation.Reject(
                    "Cette dénomination n'existe pas dans le catalogue."
                );
            }

            if (amountCents <= 0)
            {
                return PhysicalCashSpawnValidation.Reject(
                    "Le montant doit être supérieur à zéro."
                );
            }

            if (amountCents > maximumItemValueCents)
            {
                return PhysicalCashSpawnValidation.Reject(
                    "La valeur de cet objet dépasse la limite autorisée."
                );
            }

            return PhysicalCashSpawnValidation.Accept(
                playerId,
                itemId,
                amountCents
            );
        }
    }
}
