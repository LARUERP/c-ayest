
using System;
using FusionDarkRP.Economy;

namespace FusionDarkRP.Integration
{
    public sealed class RuntimeEconomyBackend : IEconomyBackend
    {
        private readonly EconomyRuntime runtime;
        private readonly Func<bool> authorityProvider;

        public RuntimeEconomyBackend(
            EconomyRuntime runtime,
            Func<bool> authorityProvider)
        {
            this.runtime = runtime
                ?? throw new ArgumentNullException(nameof(runtime));

            this.authorityProvider = authorityProvider
                ?? throw new ArgumentNullException(nameof(authorityProvider));
        }

        public WalletSnapshot GetWallet(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return null;

            WalletData wallet = runtime.Bank.GetAccount(playerId);

            if (wallet == null)
                return null;

            return CreateSnapshot(wallet);
        }

        public bool TryExecute(
            EconomyRequest request,
            out WalletSnapshot sourceAfter,
            out WalletSnapshot targetAfter,
            out string error)
        {
            sourceAfter = null;
            targetAfter = null;
            error = null;

            if (request == null)
            {
                error = "Requête économique invalide.";
                return false;
            }

            bool isAuthority;

            try
            {
                isAuthority = authorityProvider();
            }
            catch (Exception exception)
            {
                error = "Impossible de vérifier l'autorité : "
                    + exception.Message;
                return false;
            }

            if (!isAuthority)
            {
                error = "Seul l'hôte peut effectuer cette opération.";
                return false;
            }

            bool success;

            switch (request.Operation)
            {
                case EconomyOperation.Deposit:
                    success = runtime.Atm.Deposit(
                        true,
                        request.RequestId,
                        request.SourcePlayerId,
                        request.AmountCents,
                        out error
                    );
                    break;

                case EconomyOperation.Withdraw:
                    success = runtime.Atm.Withdraw(
                        true,
                        request.RequestId,
                        request.SourcePlayerId,
                        request.AmountCents,
                        out error
                    );
                    break;

                case EconomyOperation.Transfer:
                    if (string.IsNullOrWhiteSpace(
                        request.TargetPlayerId))
                    {
                        error = "Destinataire invalide.";
                        return false;
                    }

                    success = runtime.Atm.Transfer(
                        true,
                        request.RequestId,
                        request.SourcePlayerId,
                        request.TargetPlayerId,
                        request.AmountCents,
                        out error
                    );
                    break;

                default:
                    error = "Cette opération n'est pas encore prise en charge.";
                    return false;
            }

            if (!success)
                return false;

            sourceAfter = GetWallet(request.SourcePlayerId);

            if (request.Operation == EconomyOperation.Transfer)
            {
                targetAfter = GetWallet(request.TargetPlayerId);
            }

            if (sourceAfter == null)
            {
                error = "Opération effectuée, mais compte source introuvable.";
                return false;
            }

            return true;
        }

        private static WalletSnapshot CreateSnapshot(WalletData wallet)
        {
            return new WalletSnapshot(
                wallet.PlayerId,
                wallet.CashCents,
                wallet.BankCents,
                wallet.Revision
            );
        }
    }
}
