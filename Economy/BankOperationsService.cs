
using System;
using System.Collections.Generic;
using MelonLoader;

namespace FusionDarkRP.Economy
{
    public static class BankTransactionTypes
    {
        public const string Deposit = "DEPOSIT";
        public const string Withdrawal = "WITHDRAWAL";
        public const string Transfer = "TRANSFER";
    }

    public sealed class BankOperationsService
    {
        private readonly BankService bankService;
        private readonly TransactionHistoryService historyService;

        public BankOperationsService(
            BankService bankService,
            TransactionHistoryService historyService)
        {
            this.bankService = bankService
                ?? throw new ArgumentNullException(nameof(bankService));

            this.historyService = historyService
                ?? throw new ArgumentNullException(nameof(historyService));
        }

        public bool Deposit(
            bool isAuthority,
            string requestId,
            string playerId,
            long amountCents,
            out string error)
        {
            if (!ValidateOperation(
                isAuthority,
                requestId,
                playerId,
                amountCents,
                out error))
                return false;

            if (!bankService.Deposit(
                isAuthority,
                requestId,
                playerId,
                amountCents,
                out error))
            {
                return false;
            }

            RecordHistory(
                isAuthority,
                requestId,
                playerId,
                "",
                BankTransactionTypes.Deposit,
                amountCents,
                "Dépôt bancaire");

            return true;
        }

        public bool Withdraw(
            bool isAuthority,
            string requestId,
            string playerId,
            long amountCents,
            out string error)
        {
            if (!ValidateOperation(
                isAuthority,
                requestId,
                playerId,
                amountCents,
                out error))
                return false;

            if (!bankService.Withdraw(
                isAuthority,
                requestId,
                playerId,
                amountCents,
                out error))
            {
                return false;
            }

            RecordHistory(
                isAuthority,
                requestId,
                playerId,
                "",
                BankTransactionTypes.Withdrawal,
                amountCents,
                "Retrait bancaire");

            return true;
        }

        public bool Transfer(
            bool isAuthority,
            string requestId,
            string senderPlayerId,
            string receiverPlayerId,
            long amountCents,
            out string error)
        {
            error = null;

            if (!isAuthority)
            {
                error = "Seul l'hôte peut effectuer un virement.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(requestId) ||
                string.IsNullOrWhiteSpace(senderPlayerId) ||
                string.IsNullOrWhiteSpace(receiverPlayerId))
            {
                error = "Identifiants de virement invalides.";
                return false;
            }

            if (amountCents <= 0)
            {
                error = "Le montant doit être supérieur à zéro.";
                return false;
            }

            if (string.Equals(
                senderPlayerId,
                receiverPlayerId,
                StringComparison.Ordinal))
            {
                error = "Tu ne peux pas te faire un virement à toi-même.";
                return false;
            }

            if (!bankService.Transfer(
                isAuthority,
                requestId,
                senderPlayerId,
                receiverPlayerId,
                amountCents,
                out error))
            {
                return false;
            }

            RecordHistory(
                isAuthority,
                requestId,
                senderPlayerId,
                receiverPlayerId,
                BankTransactionTypes.Transfer,
                amountCents,
                "Virement bancaire");

            return true;
        }

        public List<BankTransaction> GetPlayerHistory(
            string playerId,
            int maximumResults = 50)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return new List<BankTransaction>();

            if (maximumResults <= 0)
                maximumResults = 50;

            if (maximumResults > 500)
                maximumResults = 500;

            try
            {
                List<BankTransaction> history =
                    historyService.GetPlayerHistory(
                        playerId,
                        maximumResults);

                return history ?? new List<BankTransaction>();
            }
            catch (Exception ex)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Banque] Erreur lecture historique : "
                    + ex);

                return new List<BankTransaction>();
            }
        }

        private static bool ValidateOperation(
            bool isAuthority,
            string requestId,
            string playerId,
            long amountCents,
            out string error)
        {
            error = null;

            if (!isAuthority)
            {
                error = "Seul l'hôte peut effectuer une opération bancaire.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(requestId))
            {
                error = "Identifiant de transaction invalide.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(playerId))
            {
                error = "Identifiant du joueur invalide.";
                return false;
            }

            if (amountCents <= 0)
            {
                error = "Le montant doit être supérieur à zéro.";
                return false;
            }

            return true;
        }

        private void RecordHistory(
            bool isAuthority,
            string requestId,
            string playerId,
            string otherPlayerId,
            string type,
            long amountCents,
            string description)
        {
            if (!isAuthority)
                return;

            try
            {
                string historyError;

                bool recorded = historyService.Record(
                    isAuthority,
                    requestId,
                    playerId,
                    otherPlayerId,
                    type,
                    amountCents,
                    description,
                    out historyError);

                if (!recorded)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Banque] Opération bancaire réussie, "
                        + "mais historique non enregistré : "
                        + historyError);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Banque] Erreur historique après transaction : "
                    + ex);
            }
        }
    }
}
