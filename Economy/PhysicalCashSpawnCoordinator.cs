using System;
using System.Collections.Generic;

namespace FusionDarkRP.Economy
{
    public sealed class PhysicalCashSpawnCoordinator
    {
        private readonly PhysicalCashService cashService;
        private readonly PhysicalCashSpawnRegistry registry;
        private readonly Func<bool> authorityProvider;

        public PhysicalCashSpawnCoordinator(
            PhysicalCashService cashService,
            PhysicalCashSpawnRegistry registry,
            Func<bool> authorityProvider)
        {
            this.cashService = cashService
                ?? throw new ArgumentNullException(nameof(cashService));

            this.registry = registry
                ?? throw new ArgumentNullException(nameof(registry));

            this.authorityProvider = authorityProvider
                ?? throw new ArgumentNullException(nameof(authorityProvider));
        }

        public bool TryCreateTicket(
            string playerId,
            string requestId,
            string itemId,
            out PhysicalCashSpawnTicket ticket,
            out string error)
        {
            ticket = null;
            error = null;

            if (!IsAuthority())
            {
                error = "Seul l'hôte peut créer de l'argent physique.";
                return false;
            }

            if (!PhysicalCashRequestId.IsValid(requestId))
            {
                error = "Identifiant de requête invalide.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(playerId))
            {
                error = "Identifiant joueur invalide.";
                return false;
            }

            if (!cashService.IsValidDenomination(itemId))
            {
                error = "Dénomination d'argent invalide.";
                return false;
            }

            PhysicalCashResult result = cashService.RegisterCash(
                true,
                requestId,
                playerId,
                itemId);

            if (result == null || !result.Success)
            {
                error = result == null
                    ? "Erreur inconnue pendant la création."
                    : result.Message;

                return false;
            }

            if (!PhysicalCashSpawnTicket.TryCreate(
                result,
                playerId,
                itemId,
                requestId,
                out ticket,
                out error))
            {
                string ticketError = error;
                ticket = null;

                bool refunded = TryRefundRegisteredCash(
                    result,
                    playerId,
                    out string refundError);

                error = refunded
                    ? "Création du ticket impossible. "
                        + "Le montant a été restitué au portefeuille. "
                        + ticketError
                    : "Création du ticket impossible et restitution "
                        + "non confirmée. Billet : "
                        + result.CashId
                        + ". Erreur ticket : "
                        + ticketError
                        + ". Erreur restitution : "
                        + refundError;

                return false;
            }

            if (!registry.TryRegister(ticket, out error))
            {
                string registryError = error;
                ticket = null;

                bool refunded = TryRefundRegisteredCash(
                    result,
                    playerId,
                    out string refundError);

                error = refunded
                    ? "Enregistrement du ticket impossible. "
                        + "Le montant a été restitué au portefeuille. "
                        + registryError
                    : "Enregistrement du ticket impossible et "
                        + "restitution non confirmée. Billet : "
                        + result.CashId
                        + ". Erreur registre : "
                        + registryError
                        + ". Erreur restitution : "
                        + refundError;

                return false;
            }

            return true;
        }

        private bool TryRefundRegisteredCash(
            PhysicalCashResult result,
            string playerId,
            out string error)
        {
            error = null;

            if (!IsAuthority())
            {
                error = "Autorité hôte indisponible.";
                return false;
            }

            if (result == null ||
                !result.Success ||
                string.IsNullOrWhiteSpace(result.CashId) ||
                string.IsNullOrWhiteSpace(playerId))
            {
                error = "Données du billet invalides pour restitution.";
                return false;
            }

            try
            {
                string refundRequestId = PhysicalCashRequestId.Create();

                PhysicalCashResult refund = cashService.RedeemCash(
                    true,
                    refundRequestId,
                    playerId,
                    result.CashId);

                if (refund == null || !refund.Success)
                {
                    error = refund == null
                        ? "Erreur inconnue pendant la restitution."
                        : refund.Message;

                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = "Exception pendant la restitution : "
                    + exception.Message;
                return false;
            }
        }

        public bool TryGetTicket(
            string cashId,
            out PhysicalCashSpawnTicket ticket)
        {
            ticket = null;

            if (!IsAuthority())
                return false;

            return registry.TryGet(cashId, out ticket);
        }

        /// <summary>
        /// Transfère un billet physique à un autre joueur.
        /// Le propriétaire est mis à jour dans la sauvegarde économique.
        /// Si le billet attend encore son apparition, son ticket est
        /// également mis à jour dans le registre.
        /// </summary>
        public bool TryTransferCash(
            string requestId,
            string fromPlayerId,
            string toPlayerId,
            string cashId,
            out PhysicalCashSpawnTicket updatedTicket,
            out string error)
        {
            updatedTicket = null;
            error = null;

            if (!IsAuthority())
            {
                error = "Action réservée à l'hôte.";
                return false;
            }

            if (!PhysicalCashRequestId.IsValid(requestId))
            {
                error = "Identifiant de requête invalide.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(fromPlayerId) ||
                string.IsNullOrWhiteSpace(toPlayerId) ||
                string.IsNullOrWhiteSpace(cashId))
            {
                error = "Données du transfert invalides.";
                return false;
            }

            if (string.Equals(
                fromPlayerId,
                toPlayerId,
                StringComparison.Ordinal))
            {
                error = "Impossible de transférer un billet à soi-même.";
                return false;
            }

            if (!IsAuthority())
            {
                error = "Autorité hôte indisponible.";
                return false;
            }

            PhysicalCashSpawnTicket pendingTicket = null;

            bool hasPendingTicket = registry.TryGet(
                cashId,
                out pendingTicket);

            if (hasPendingTicket)
            {
                if (pendingTicket == null ||
                    !string.Equals(
                        pendingTicket.OwnerPlayerId,
                        fromPlayerId,
                        StringComparison.Ordinal))
                {
                    error = "Le ticket ne correspond pas au propriétaire actuel.";
                    return false;
                }

                if (!registry.TryChangeOwner(
                    cashId,
                    fromPlayerId,
                    toPlayerId,
                    out updatedTicket,
                    out error))
                {
                    updatedTicket = null;

                    if (string.IsNullOrWhiteSpace(error))
                        error = "Impossible de mettre à jour le ticket.";

                    return false;
                }
            }

            PhysicalCashResult transferResult;

            try
            {
                transferResult = cashService.TransferCash(
                    true,
                    requestId,
                    fromPlayerId,
                    toPlayerId,
                    cashId);
            }
            catch (Exception exception)
            {
                transferResult = PhysicalCashResult.Fail(
                    "Exception pendant le transfert : "
                    + exception.Message);
            }

            if (transferResult == null || !transferResult.Success)
            {
                string transferError = transferResult == null
                    ? "Réponse du service de transfert vide."
                    : transferResult.Message;

                if (hasPendingTicket)
                {
                    bool restored = registry.TryChangeOwner(
                        cashId,
                        toPlayerId,
                        fromPlayerId,
                        out _,
                        out string restoreError);

                    updatedTicket = null;

                    if (!restored)
                    {
                        error = "ÉCHEC CRITIQUE : le transfert économique "
                            + "a échoué et le ticket n'a pas pu être restauré. "
                            + "Billet : "
                            + cashId
                            + ". Erreur transfert : "
                            + transferError
                            + ". Erreur restauration : "
                            + restoreError;

                        return false;
                    }
                }

                error = transferError;
                return false;
            }

            if (hasPendingTicket)
            {
                if (!registry.TryGet(cashId, out updatedTicket))
                {
                    error = "Le transfert économique est enregistré, "
                        + "mais le ticket est introuvable dans le registre. "
                        + "Billet : "
                        + cashId;

                    return false;
                }
            }

            return true;
        }

        public bool TryConsumeTicket(
            string cashId,
            string playerId,
            out PhysicalCashSpawnTicket ticket,
            out string error)
        {
            ticket = null;
            error = null;

            if (!IsAuthority())
            {
                error = "Action réservée à l'hôte.";
                return false;
            }

            return registry.TryConsume(
                cashId,
                playerId,
                out ticket,
                out error);
        }

        /// <summary>
        /// Annule et rembourse un seul billet précis.
        /// Les autres tickets du joueur restent intacts.
        /// </summary>
        public bool TryCancelAndRefund(
            string cashId,
            string playerId,
            out string error)
        {
            error = null;

            if (!IsAuthority())
            {
                error = "Action réservée à l'hôte.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(cashId))
            {
                error = "Identifiant du billet invalide.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(playerId))
            {
                error = "Identifiant joueur invalide.";
                return false;
            }

            PhysicalCashSpawnTicket ticket;

            if (!registry.TryTake(
                cashId,
                playerId,
                out ticket,
                out error))
            {
                if (string.IsNullOrWhiteSpace(error))
                    error = "Billet introuvable ou déjà traité.";

                return false;
            }

            if (ticket == null ||
                string.IsNullOrWhiteSpace(ticket.CashId))
            {
                error = "Ticket récupéré invalide. "
                    + "Vérification manuelle nécessaire.";
                return false;
            }

            string refundError = null;
            bool refunded = false;

            try
            {
                if (!IsAuthority())
                {
                    refundError = "Autorité hôte indisponible.";
                }
                else
                {
                    PhysicalCashResult result = cashService.RedeemCash(
                        true,
                        PhysicalCashRequestId.Create(),
                        playerId,
                        ticket.CashId);

                    if (result != null && result.Success)
                    {
                        refunded = true;
                    }
                    else
                    {
                        refundError = result == null
                            ? "Réponse de remboursement vide."
                            : result.Message;
                    }
                }
            }
            catch (Exception exception)
            {
                refundError = exception.Message;
            }

            if (refunded)
                return true;

            string restoreError = null;
            bool restored = false;

            try
            {
                restored = registry.TryRestore(
                    ticket,
                    out restoreError);
            }
            catch (Exception exception)
            {
                restoreError = exception.Message;
            }

            if (restored)
            {
                error = "Remboursement impossible. "
                    + "Le ticket a été restauré pour une nouvelle tentative. "
                    + refundError;

                return false;
            }

            error = "ÉCHEC CRITIQUE : remboursement impossible et "
                + "restauration du ticket impossible. Billet : "
                + ticket.CashId
                + ". Erreur remboursement : "
                + refundError
                + ". Erreur restauration : "
                + restoreError;

            return false;
        }

        public int RemovePlayer(string playerId)
        {
            return RemovePlayer(playerId, out _);
        }

        /// <summary>
        /// Restitue les tickets encore en attente lors d'une déconnexion.
        /// </summary>
        public int RemovePlayer(
            string playerId,
            out string error)
        {
            error = null;

            if (!IsAuthority())
            {
                error = "Action réservée à l'hôte.";
                return 0;
            }

            if (string.IsNullOrWhiteSpace(playerId))
            {
                error = "Identifiant joueur invalide.";
                return 0;
            }

            List<PhysicalCashSpawnTicket> pendingTickets =
                registry.TakePlayerTickets(playerId);

            if (pendingTickets.Count == 0)
                return 0;

            int refundedCount = 0;
            List<string> errors = new List<string>();

            foreach (PhysicalCashSpawnTicket ticket in pendingTickets)
            {
                if (ticket == null)
                    continue;

                bool refunded = false;
                string refundError = null;

                try
                {
                    if (!IsAuthority())
                    {
                        refundError = "Autorité hôte indisponible.";
                    }
                    else
                    {
                        PhysicalCashResult result = cashService.RedeemCash(
                            true,
                            PhysicalCashRequestId.Create(),
                            playerId,
                            ticket.CashId);

                        if (result != null && result.Success)
                        {
                            refunded = true;
                        }
                        else
                        {
                            refundError = result == null
                                ? "Réponse de remboursement vide."
                                : result.Message;
                        }
                    }
                }
                catch (Exception exception)
                {
                    refundError = exception.Message;
                }

                if (refunded)
                {
                    refundedCount++;
                    continue;
                }

                string restoreError = null;
                bool restored = false;

                try
                {
                    restored = registry.TryRestore(
                        ticket,
                        out restoreError);
                }
                catch (Exception exception)
                {
                    restoreError = exception.Message;
                }

                if (!restored)
                {
                    errors.Add(
                        "Billet "
                        + ticket.CashId
                        + " : remboursement échoué ("
                        + refundError
                        + ") et restauration du ticket échouée ("
                        + restoreError
                        + ").");
                }
                else
                {
                    errors.Add(
                        "Billet "
                        + ticket.CashId
                        + " : remboursement échoué, ticket conservé "
                        + "pour une nouvelle tentative. "
                        + refundError);
                }
            }

            if (errors.Count > 0)
                error = string.Join(" | ", errors);

            return refundedCount;
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
