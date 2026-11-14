using System;

namespace FusionDarkRP.Economy
{
    public sealed class AtmService
    {
        public const int AtmPropId = 6050015;

        private readonly BankOperationsService bankOperations;

        public AtmService(BankOperationsService bankOperations)
        {
            this.bankOperations = bankOperations
                ?? throw new ArgumentNullException(
                    nameof(bankOperations));
        }

        public static bool IsAtmProp(int propId)
        {
            return propId == AtmPropId;
        }

        public bool Deposit(
            bool isAuthority,
            string requestId,
            string playerId,
            long amountCents,
            out string error)
        {
            if (!isAuthority)
            {
                error = "Seul l'hôte peut effectuer cette opération.";
                return false;
            }

            if (!IsValidAmount(amountCents, out error))
                return false;

            return bankOperations.Deposit(
                isAuthority,
                requestId,
                playerId,
                amountCents,
                out error);
        }

        public bool Withdraw(
            bool isAuthority,
            string requestId,
            string playerId,
            long amountCents,
            out string error)
        {
            if (!isAuthority)
            {
                error = "Seul l'hôte peut effectuer cette opération.";
                return false;
            }

            if (!IsValidAmount(amountCents, out error))
                return false;

            return bankOperations.Withdraw(
                isAuthority,
                requestId,
                playerId,
                amountCents,
                out error);
        }

        public bool Transfer(
            bool isAuthority,
            string requestId,
            string senderPlayerId,
            string receiverPlayerId,
            long amountCents,
            out string error)
        {
            if (!isAuthority)
            {
                error = "Seul l'hôte peut effectuer cette opération.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(receiverPlayerId))
            {
                error = "Destinataire invalide.";
                return false;
            }

            if (!IsValidAmount(amountCents, out error))
                return false;

            return bankOperations.Transfer(
                isAuthority,
                requestId,
                senderPlayerId,
                receiverPlayerId,
                amountCents,
                out error);
        }

        private static bool IsValidAmount(
            long amountCents,
            out string error)
        {
            if (amountCents <= 0)
            {
                error = "Le montant doit être supérieur à zéro.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
