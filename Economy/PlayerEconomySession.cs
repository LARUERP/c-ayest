using System;
using System.Collections.Generic;

namespace FusionDarkRP.Economy
{
    public sealed class PlayerWalletSnapshot
    {
        public string PlayerId { get; private set; }
        public long CashCents { get; private set; }
        public long BankCents { get; private set; }
        public long Revision { get; private set; }

        public PlayerWalletSnapshot(
            string playerId,
            long cashCents,
            long bankCents,
            long revision)
        {
            PlayerId = playerId;
            CashCents = cashCents;
            BankCents = bankCents;
            Revision = revision;
        }
    }

    public sealed class PlayerEconomySession
    {
        private readonly EconomyRuntime runtime;

        public string PlayerId { get; private set; }

        public PlayerEconomySession(
            EconomyRuntime runtime,
            string playerId)
        {
            this.runtime = runtime
                ?? throw new ArgumentNullException(nameof(runtime));

            if (string.IsNullOrWhiteSpace(playerId))
                throw new ArgumentException(
                    "Identifiant du joueur invalide.",
                    nameof(playerId));

            PlayerId = playerId;
            this.runtime.InitializePlayer(PlayerId);
        }

        public PlayerWalletSnapshot GetWalletSnapshot()
        {
            WalletData wallet = runtime.Bank.GetAccount(PlayerId);

            if (wallet == null)
                return null;

            return new PlayerWalletSnapshot(
                wallet.PlayerId,
                wallet.CashCents,
                wallet.BankCents,
                wallet.Revision);
        }

        public List<BankTransaction> GetHistory(
            int maximumResults = 50)
        {
            return runtime.BankOperations.GetPlayerHistory(
                PlayerId,
                maximumResults);
        }

        public bool Deposit(
            bool isHost,
            string requestId,
            long amountCents,
            out string error)
        {
            return runtime.Atm.Deposit(
                isHost,
                requestId,
                PlayerId,
                amountCents,
                out error);
        }

        public bool Withdraw(
            bool isHost,
            string requestId,
            long amountCents,
            out string error)
        {
            return runtime.Atm.Withdraw(
                isHost,
                requestId,
                PlayerId,
                amountCents,
                out error);
        }

        public bool Transfer(
            bool isHost,
            string requestId,
            string receiverPlayerId,
            long amountCents,
            out string error)
        {
            return runtime.Atm.Transfer(
                isHost,
                requestId,
                PlayerId,
                receiverPlayerId,
                amountCents,
                out error);
        }

        public bool TryGetBalances(
            out long cashCents,
            out long bankCents)
        {
            return runtime.Bank.GetBalances(
                PlayerId,
                out cashCents,
                out bankCents);
        }
    }
}
