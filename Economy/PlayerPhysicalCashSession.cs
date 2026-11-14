using System;

namespace FusionDarkRP.Economy
{
    public sealed class PlayerPhysicalCashSession
    {
        private readonly EconomyRuntime runtime;
        private readonly string playerId;
        private readonly Func<bool> authorityProvider;

        public string PlayerId
        {
            get { return playerId; }
        }

        public PlayerPhysicalCashSession(
            EconomyRuntime runtime,
            string playerId,
            Func<bool> authorityProvider)
        {
            this.runtime = runtime
                ?? throw new ArgumentNullException(nameof(runtime));

            if (string.IsNullOrWhiteSpace(playerId))
            {
                throw new ArgumentException(
                    "Identifiant joueur invalide.",
                    nameof(playerId)
                );
            }

            this.playerId = playerId;

            this.authorityProvider = authorityProvider
                ?? throw new ArgumentNullException(
                    nameof(authorityProvider)
                );
        }

        public bool IsValidDenomination(string itemId)
        {
            return runtime.PhysicalCash.IsValidDenomination(itemId);
        }

        public bool TryGetAmount(
            string itemId,
            out long amountCents)
        {
            return runtime.PhysicalCash.TryGetAmount(
                itemId,
                out amountCents
            );
        }

        /// <summary>
        /// Crée de l'argent physique avec un identifiant de demande.
        /// Une même demande ne peut pas être exécutée deux fois.
        /// </summary>
        public PhysicalCashResult RegisterCash(
            string requestId,
            string itemId)
        {
            if (!IsAuthority())
            {
                return PhysicalCashResult.Fail(
                    "Action réservée à l'hôte."
                );
            }

            if (string.IsNullOrWhiteSpace(requestId))
            {
                return PhysicalCashResult.Fail(
                    "Identifiant de création manquant."
                );
            }

            return runtime.PhysicalCash.RegisterCash(
                true,
                requestId,
                playerId,
                itemId
            );
        }

        /// <summary>
        /// Ancienne surcharge conservée temporairement.
        /// Préférer RegisterCash(requestId, itemId).
        /// </summary>
        [Obsolete("Utiliser RegisterCash avec un requestId.")]
        public PhysicalCashResult RegisterCash(string itemId)
        {
            return RegisterCash(
                Guid.NewGuid().ToString("N"),
                itemId
            );
        }

        public PhysicalCashResult RedeemCash(
            string requestId,
            string cashId)
        {
            if (!IsAuthority())
            {
                return PhysicalCashResult.Fail(
                    "Action réservée à l'hôte."
                );
            }

            return runtime.PhysicalCash.RedeemCash(
                true,
                requestId,
                playerId,
                cashId
            );
        }

        public string FormatAmount(long amountCents)
        {
            return runtime.PhysicalCash.FormatAmount(
                amountCents
            );
        }

        private bool IsAuthority()
        {
            try
            {
                return authorityProvider();
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
