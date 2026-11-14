
using System;

namespace FusionDarkRP.Integration
{
    // Integration contracts for the future unified mod.
    public sealed class WalletSnapshot
    {
        public string PlayerId { get; private set; }
        public long CashCents { get; private set; }
        public long BankCents { get; private set; }
        public long Revision { get; private set; }

        public WalletSnapshot(string playerId, long cashCents, long bankCents, long revision)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                throw new ArgumentException("Player ID is required.", "playerId");

            if (cashCents < 0 || bankCents < 0)
                throw new ArgumentOutOfRangeException("Balances cannot be negative.");

            if (revision < 0)
                throw new ArgumentOutOfRangeException("revision");

            PlayerId = playerId;
            CashCents = cashCents;
            BankCents = bankCents;
            Revision = revision;
        }
    }

    public enum EconomyOperation
    {
        Deposit,
        Withdraw,
        Transfer,
        Paycheck,
        PhysicalCashPickup,
        PhysicalCashDrop
    }

    public sealed class EconomyRequest
    {
        public EconomyOperation Operation { get; private set; }
        public string SourcePlayerId { get; private set; }
        public string TargetPlayerId { get; private set; }
        public long AmountCents { get; private set; }
        public string RequestId { get; private set; }

        public EconomyRequest(
            EconomyOperation operation,
            string sourcePlayerId,
            string targetPlayerId,
            long amountCents,
            string requestId)
        {
            if (string.IsNullOrWhiteSpace(sourcePlayerId))
                throw new ArgumentException("Source player is required.");

            if (amountCents <= 0)
                throw new ArgumentOutOfRangeException("amountCents");

            if (string.IsNullOrWhiteSpace(requestId))
                throw new ArgumentException("Request ID is required.");

            Operation = operation;
            SourcePlayerId = sourcePlayerId;
            TargetPlayerId = targetPlayerId;
            AmountCents = amountCents;
            RequestId = requestId;
        }
    }

    public interface IEconomyBackend
    {
        WalletSnapshot GetWallet(string playerId);

        bool TryExecute(
            EconomyRequest request,
            out WalletSnapshot sourceAfter,
            out WalletSnapshot targetAfter,
            out string error);
    }
}
