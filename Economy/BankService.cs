
using System;
using System.Globalization;

namespace FusionDarkRP.Economy
{
    public sealed class BankService
    {
        private readonly WalletService walletService;

        private static readonly CultureInfo FrenchCulture =
            CultureInfo.GetCultureInfo("fr-FR");

        public BankService(WalletService walletService)
        {
            this.walletService = walletService
                ?? throw new ArgumentNullException(nameof(walletService));
        }

        public WalletData GetAccount(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return null;

            return walletService.GetWallet(playerId);
        }

        public bool Deposit(
            bool isAuthority,
            string requestId,
            string playerId,
            long amountCents,
            out string error)
        {
            error = null;

            if (!ValidateOperation(
                isAuthority,
                requestId,
                playerId,
                amountCents,
                out error))
                return false;

            return walletService.TryDeposit(
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
            error = null;

            if (!ValidateOperation(
                isAuthority,
                requestId,
                playerId,
                amountCents,
                out error))
                return false;

            return walletService.TryWithdraw(
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
            error = null;

            if (!isAuthority)
            {
                error = "Seul l'hôte peut effectuer un virement.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(requestId))
            {
                error = "Identifiant de transaction invalide.";
                return false;
            }

            if (!ValidatePlayer(senderPlayerId, out error))
                return false;

            if (!ValidatePlayer(receiverPlayerId, out error))
                return false;

            if (string.Equals(
                senderPlayerId,
                receiverPlayerId,
                StringComparison.Ordinal))
            {
                error = "Tu ne peux pas te faire un virement à toi-même.";
                return false;
            }

            if (amountCents <= 0)
            {
                error = "Le montant doit être supérieur à zéro.";
                return false;
            }

            return walletService.TryTransfer(
                isAuthority,
                requestId,
                senderPlayerId,
                receiverPlayerId,
                amountCents,
                out error);
        }

        public bool GetBalances(
            string playerId,
            out long cashCents,
            out long bankCents)
        {
            cashCents = 0;
            bankCents = 0;

            if (string.IsNullOrWhiteSpace(playerId))
                return false;

            try
            {
                WalletData wallet = walletService.GetWallet(playerId);

                if (wallet == null)
                    return false;

                if (wallet.CashCents < 0 || wallet.BankCents < 0)
                    return false;

                cashCents = wallet.CashCents;
                bankCents = wallet.BankCents;

                return true;
            }
            catch
            {
                cashCents = 0;
                bankCents = 0;
                return false;
            }
        }

        public static string FormatAmount(long amountCents)
        {
            decimal amount = amountCents / 100m;

            return amount.ToString(
                "#,##0.00",
                FrenchCulture) + " €";
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

            if (!ValidatePlayer(playerId, out error))
                return false;

            if (amountCents <= 0)
            {
                error = "Le montant doit être supérieur à zéro.";
                return false;
            }

            return true;
        }

        private static bool ValidatePlayer(
            string playerId,
            out string error)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                error = "Identifiant du joueur invalide.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
