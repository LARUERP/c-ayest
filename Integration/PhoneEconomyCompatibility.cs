using System;
using System.Collections.Generic;
using FusionDarkRP.Economy;
using LabFusion.Player;
using LabFusion.SDK.Metadata;
using FusionDarkRP.Telephone;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Accès du téléphone à l'économie unique Fusion DarkRP.
    /// Aucun portefeuille ni historique bancaire secondaire.
    /// Tous les montants sont exprimés en centimes.
    /// </summary>
    public static class FusionPhoneEconomy
    {
        public static bool IsHosting
        {
            get { return PhoneEconomyBridge.IsReady; }
        }

        /// <summary>
        /// Retourne le solde bancaire central en centimes.
        /// Exemple : 150 correspond à 1,50 €.
        /// </summary>
        public static long GetBalance(ulong platformId)
        {
            long balanceCents;
            string error;

            if (!PhoneEconomyBridge.TryGetPhoneBalance(
                platformId,
                out balanceCents,
                out error))
            {
                return 0L;
            }

            return balanceCents;
        }

        /// <summary>
        /// Récupère l'historique bancaire central.
        /// Les montants de l'historique restent en centimes.
        /// Aucun historique local du téléphone n'est utilisé.
        /// </summary>
        public static bool TryGetHistory(
            ulong platformId,
            out List<PhoneBankEntry> entries,
            out string error)
        {
            entries = new List<PhoneBankEntry>();
            error = null;

            string playerId;

            if (!PhoneEconomyBridge.TryGetPersistentId(
                platformId,
                out playerId,
                out error))
            {
                return false;
            }

            List<BankTransaction> transactions;

            if (!PhoneEconomyBridge.TryGetHistory(
                platformId,
                50,
                out transactions,
                out error))
            {
                return false;
            }

            if (transactions == null)
                return true;

            foreach (BankTransaction transaction in transactions)
            {
                if (transaction == null ||
                    transaction.AmountCents <= 0)
                {
                    continue;
                }

                long amountCents = transaction.AmountCents;

                bool incoming = string.Equals(
                    transaction.OtherPlayerId,
                    playerId,
                    StringComparison.Ordinal);

                bool outgoing = string.Equals(
                    transaction.PlayerId,
                    playerId,
                    StringComparison.Ordinal);

                if (!incoming && !outgoing)
                    continue;

                long signedAmountCents;

                switch (transaction.Type)
                {
                    case BankTransactionTypes.Deposit:
                        signedAmountCents = amountCents;
                        break;

                    case BankTransactionTypes.Withdrawal:
                        signedAmountCents = -amountCents;
                        break;

                    case BankTransactionTypes.Transfer:
                        signedAmountCents = incoming
                            ? amountCents
                            : -amountCents;
                        break;

                    default:
                        signedAmountCents = incoming
                            ? amountCents
                            : -amountCents;
                        break;
                }

                DateTime utc;

                try
                {
                    utc = new DateTime(
                        transaction.TimestampUtcTicks,
                        DateTimeKind.Utc);
                }
                catch
                {
                    utc = DateTime.UtcNow;
                }

                entries.Add(new PhoneBankEntry
                {
                    Id = transaction.TransactionId ?? "",
                    Amount = signedAmountCents,
                    Label = BuildLabel(
                        transaction,
                        incoming,
                        outgoing),
                    Utc = utc
                });
            }

            return true;
        }

        private static string BuildLabel(
            BankTransaction transaction,
            bool incoming,
            bool outgoing)
        {
            string description =
                transaction.Description ?? "";

            switch (transaction.Type)
            {
                case BankTransactionTypes.Deposit:
                    return "Dépôt bancaire";

                case BankTransactionTypes.Withdrawal:
                    return "Retrait bancaire";

                case BankTransactionTypes.Transfer:
                    return incoming
                        ? "Virement reçu"
                        : "Virement envoyé";

                default:
                    if (!string.IsNullOrWhiteSpace(description))
                        return description;

                    return incoming
                        ? "Crédit bancaire"
                        : outgoing
                            ? "Débit bancaire"
                            : "Opération bancaire";
            }
        }

        /// <summary>
        /// Récupère le véritable nom Fusion du joueur.
        /// </summary>
        public static string ResolveName(PlayerID player)
        {
            if (player == null || !player.IsValid)
                return "Joueur";

            try
            {
                PlayerMetadata metadata = player.Metadata;

                if (metadata != null)
                {
                    MetadataVariable username =
                        metadata.Username;

                    if (username != null)
                    {
                        string name = username.GetValue();

                        if (!string.IsNullOrWhiteSpace(name))
                            return name.Trim();
                    }
                }
            }
            catch (Exception exception)
            {
                MelonLoader.MelonLogger.Warning(
                    "[FusionDarkRP Phone] Lecture du nom impossible : "
                    + exception.Message);
            }

            return "Joueur " + player.SmallID;
        }
    }
}
