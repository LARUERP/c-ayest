using System;

namespace FusionDarkRP.Economy
{
    public sealed class PhysicalCashSpawnTicket
    {
        public string CashId { get; private set; }
        public string OwnerPlayerId { get; private set; }
        public string ItemId { get; private set; }
        public string RequestId { get; private set; }
        public long AmountCents { get; private set; }

        private PhysicalCashSpawnTicket(
            string cashId,
            string ownerPlayerId,
            string itemId,
            string requestId,
            long amountCents)
        {
            CashId = cashId;
            OwnerPlayerId = ownerPlayerId;
            ItemId = itemId;
            RequestId = requestId;
            AmountCents = amountCents;
        }

        public static bool TryCreate(
            PhysicalCashResult result,
            string ownerPlayerId,
            string itemId,
            string requestId,
            out PhysicalCashSpawnTicket ticket,
            out string error)
        {
            ticket = null;
            error = null;

            if (result == null || !result.Success)
            {
                error = "La création de l'argent a échoué.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(result.CashId))
            {
                error = "Identifiant du billet manquant.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(ownerPlayerId))
            {
                error = "Identifiant du propriétaire invalide.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(itemId) ||
                !PhysicalCashCatalog.TryGetValue(
                    itemId,
                    out long expectedAmount))
            {
                error = "Type d'argent physique invalide.";
                return false;
            }

            if (!PhysicalCashRequestId.IsValid(requestId))
            {
                error = "Identifiant de requête invalide.";
                return false;
            }

            if (result.AmountCents <= 0 ||
                result.AmountCents != expectedAmount)
            {
                error = "La valeur du billet ne correspond pas.";
                return false;
            }

            ticket = new PhysicalCashSpawnTicket(
                result.CashId,
                ownerPlayerId,
                itemId,
                requestId,
                result.AmountCents
            );

            return true;
        }

        /// <summary>
        /// Prépare un changement de propriétaire sans modifier
        /// l'identité, la dénomination ou la valeur du billet.
        /// Le registre et le portefeuille doivent valider
        /// le transfert avant d'utiliser ce nouveau ticket.
        /// </summary>
        public bool TryChangeOwner(
            string currentOwnerPlayerId,
            string newOwnerPlayerId,
            out PhysicalCashSpawnTicket updatedTicket,
            out string error)
        {
            updatedTicket = null;
            error = null;

            if (string.IsNullOrWhiteSpace(currentOwnerPlayerId) ||
                string.IsNullOrWhiteSpace(newOwnerPlayerId))
            {
                error = "Identifiant de propriétaire invalide.";
                return false;
            }

            if (!string.Equals(
                OwnerPlayerId,
                currentOwnerPlayerId,
                StringComparison.Ordinal))
            {
                error = "Le billet n'appartient pas à ce joueur.";
                return false;
            }

            if (string.Equals(
                currentOwnerPlayerId,
                newOwnerPlayerId,
                StringComparison.Ordinal))
            {
                error = "Le destinataire est déjà propriétaire du billet.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(CashId) ||
                string.IsNullOrWhiteSpace(ItemId) ||
                !PhysicalCashRequestId.IsValid(RequestId) ||
                AmountCents <= 0)
            {
                error = "Les données du billet sont invalides.";
                return false;
            }

            updatedTicket = new PhysicalCashSpawnTicket(
                CashId,
                newOwnerPlayerId,
                ItemId,
                RequestId,
                AmountCents
            );

            return true;
        }
    }
}
