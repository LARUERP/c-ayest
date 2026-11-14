using System;

namespace FusionDarkRP.Economy
{
    public sealed class PhysicalCashResult
    {
        public bool Success { get; private set; }
        public string Message { get; private set; }
        public string CashId { get; private set; }
        public long AmountCents { get; private set; }

        private PhysicalCashResult(
            bool success,
            string message,
            string cashId,
            long amountCents)
        {
            Success = success;
            Message = message;
            CashId = cashId;
            AmountCents = amountCents;
        }

        public static PhysicalCashResult Ok(
            string message,
            string cashId,
            long amountCents)
        {
            return new PhysicalCashResult(
                true,
                message,
                cashId,
                amountCents);
        }

        public static PhysicalCashResult Fail(string message)
        {
            return new PhysicalCashResult(
                false,
                message,
                null,
                0);
        }
    }

    public sealed class PhysicalCashService
    {
        private readonly WalletService wallets;
        private readonly PhysicalCashSpawnValidator validator;

        public PhysicalCashService(WalletService walletService)
        {
            wallets = walletService
                ?? throw new ArgumentNullException(nameof(walletService));

            validator = new PhysicalCashSpawnValidator(1000000L);
        }

        public bool IsValidDenomination(string itemId)
        {
            return PhysicalCashCatalog.TryGetValue(
                itemId,
                out long amountCents)
                && amountCents > 0
                && amountCents <= 1000000L;
        }

        public bool TryGetAmount(
            string itemId,
            out long amountCents)
        {
            amountCents = 0;

            if (!IsValidDenomination(itemId))
                return false;

            return PhysicalCashCatalog.TryGetValue(
                itemId,
                out amountCents);
        }

        public PhysicalCashResult RegisterCash(
            bool isHost,
            string requestId,
            string playerId,
            string itemId)
        {
            if (!PhysicalCashRequestId.IsValid(requestId))
            {
                return PhysicalCashResult.Fail(
                    "Identifiant de requête invalide.");
            }

            PhysicalCashSpawnValidation validation =
                validator.Validate(isHost, playerId, itemId);

            if (!validation.IsValid)
                return PhysicalCashResult.Fail(validation.Error);

            PhysicalCashData cash = wallets.TryCreatePhysicalCash(
                true,
                requestId,
                playerId,
                validation.AmountCents,
                out string error);

            if (cash == null)
                return PhysicalCashResult.Fail(error);

            return PhysicalCashResult.Ok(
                "Argent physique enregistré.",
                cash.CashId,
                cash.AmountCents);
        }

        [Obsolete("Utilisez RegisterCash avec un requestId réutilisable.")]
        public PhysicalCashResult RegisterCash(
            bool isHost,
            string playerId,
            string itemId)
        {
            return RegisterCash(
                isHost,
                PhysicalCashRequestId.Create(),
                playerId,
                itemId);
        }

        public PhysicalCashResult RedeemCash(
            bool isHost,
            string requestId,
            string playerId,
            string cashId)
        {
            if (!isHost)
            {
                return PhysicalCashResult.Fail(
                    "Seul l'hôte peut valider cet argent.");
            }

            if (!PhysicalCashRequestId.IsValid(requestId))
            {
                return PhysicalCashResult.Fail(
                    "Identifiant de requête invalide.");
            }

            if (string.IsNullOrWhiteSpace(playerId) ||
                string.IsNullOrWhiteSpace(cashId))
            {
                return PhysicalCashResult.Fail(
                    "Données du billet invalides.");
            }

            if (!wallets.TryRedeemPhysicalCash(
                true,
                requestId,
                playerId,
                cashId,
                out string error))
            {
                return PhysicalCashResult.Fail(error);
            }

            return PhysicalCashResult.Ok(
                "Argent physique échangé.",
                cashId,
                0);
        }

        // Transfert sécurisé d'un billet physique vers un autre joueur.
        // Le billet reste physique : le solde liquide n'est pas crédité.
        public PhysicalCashResult TransferCash(
            bool isHost,
            string requestId,
            string fromPlayerId,
            string toPlayerId,
            string cashId)
        {
            if (!isHost)
            {
                return PhysicalCashResult.Fail(
                    "Seul l'hôte peut valider ce transfert.");
            }

            if (!PhysicalCashRequestId.IsValid(requestId))
            {
                return PhysicalCashResult.Fail(
                    "Identifiant de requête invalide.");
            }

            if (string.IsNullOrWhiteSpace(fromPlayerId) ||
                string.IsNullOrWhiteSpace(toPlayerId) ||
                string.IsNullOrWhiteSpace(cashId))
            {
                return PhysicalCashResult.Fail(
                    "Données du transfert invalides.");
            }

            if (string.Equals(
                fromPlayerId,
                toPlayerId,
                StringComparison.Ordinal))
            {
                return PhysicalCashResult.Fail(
                    "Impossible de transférer un billet à soi-même.");
            }

            if (!wallets.TryTransferPhysicalCash(
                true,
                requestId,
                fromPlayerId,
                toPlayerId,
                cashId,
                out string error))
            {
                return PhysicalCashResult.Fail(error);
            }

            return PhysicalCashResult.Ok(
                "Billet transféré au joueur.",
                cashId,
                0);
        }

        public string FormatAmount(long amountCents)
        {
            return PhysicalCashCatalog.FormatMoney(amountCents);
        }
    }
}
