using System;
using System.Collections.Generic;
using System.Globalization;
using FusionDarkRP.Economy;
using LabFusion.Network;
using LabFusion.Player;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Pont bancaire entre WindyPhone et l'économie Fusion DarkRP.
    /// Tous les montants sont exprimés en centimes.
    /// Le téléphone utilise directement le portefeuille central.
    /// </summary>
    public static class PhoneEconomyBridge
    {
        private const long MaxTransferCents = 999999999999L;

        public static bool IsReady
        {
            get
            {
                return NetworkInfo.IsHost
                    && EconomyRuntimeBootstrap.IsInitialized
                    && EconomyRuntimeBootstrap.Runtime != null;
            }
        }

        public static bool TryGetPersistentId(
            ulong platformId,
            out string persistentId,
            out string error)
        {
            persistentId = null;
            error = null;

            if (!IsReady)
            {
                error = "La banque du serveur est indisponible.";
                return false;
            }

            if (platformId == 0)
            {
                error = "Identifiant du joueur invalide.";
                return false;
            }

            try
            {
                PlayerID player =
                    PlatformIds.GetPlayerID(platformId);

                if (player == null || !player.IsValid)
                {
                    error = "Le joueur doit être connecté.";
                    return false;
                }

                persistentId =
                    PlayerIdentityService.GetPersistentIdOrThrow(player);

                if (string.IsNullOrWhiteSpace(persistentId))
                {
                    error = "Identité bancaire introuvable.";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = "Lecture de l'identité impossible : "
                    + exception.Message;

                return false;
            }
        }

        /// <summary>
        /// Lit les deux soldes du portefeuille central.
        /// Les montants sont toujours exprimés en centimes.
        /// </summary>
        public static bool TryGetBalance(
            ulong platformId,
            out long cashCents,
            out long bankCents,
            out string error)
        {
            cashCents = 0;
            bankCents = 0;
            error = null;

            string playerId;

            if (!TryGetPersistentId(
                platformId,
                out playerId,
                out error))
            {
                return false;
            }

            try
            {
                EconomyRuntime runtime =
                    EconomyRuntimeBootstrap.Runtime;

                if (runtime == null || runtime.Wallets == null)
                {
                    error = "Le portefeuille central est indisponible.";
                    return false;
                }

                WalletData wallet =
                    runtime.Wallets.GetWallet(playerId);

                if (wallet == null)
                {
                    error = "Compte bancaire introuvable.";
                    return false;
                }

                if (wallet.CashCents < 0 || wallet.BankCents < 0)
                {
                    error = "Le compte contient un solde invalide.";
                    return false;
                }

                cashCents = wallet.CashCents;
                bankCents = wallet.BankCents;

                return true;
            }
            catch (Exception exception)
            {
                error = "Lecture du compte impossible : "
                    + exception.Message;

                return false;
            }
        }

        /// <summary>
        /// Lit le solde bancaire destiné au téléphone.
        /// La valeur retournée est exprimée en centimes.
        /// Exemple : 150 = 1,50 €.
        /// </summary>
        public static bool TryGetPhoneBalance(
            ulong platformId,
            out long balanceCents,
            out string error)
        {
            balanceCents = 0L;

            long cashCents;
            long bankCents;

            if (!TryGetBalance(
                platformId,
                out cashCents,
                out bankCents,
                out error))
            {
                return false;
            }

            balanceCents = bankCents;
            return true;
        }

        /// <summary>
        /// Récupère l'historique bancaire central du joueur.
        /// Cette méthode ne lit jamais l'historique local du téléphone.
        /// </summary>
        public static bool TryGetHistory(
            ulong platformId,
            int maximumResults,
            out List<BankTransaction> transactions,
            out string error)
        {
            transactions = new List<BankTransaction>();
            error = null;

            string playerId;

            if (!TryGetPersistentId(
                platformId,
                out playerId,
                out error))
            {
                return false;
            }

            try
            {
                EconomyRuntime runtime =
                    EconomyRuntimeBootstrap.Runtime;

                if (runtime == null ||
                    runtime.BankOperations == null)
                {
                    error = "Historique bancaire indisponible.";
                    return false;
                }

                if (maximumResults <= 0)
                    maximumResults = 50;

                if (maximumResults > 500)
                    maximumResults = 500;

                List<BankTransaction> history =
                    runtime.BankOperations.GetPlayerHistory(
                        playerId,
                        maximumResults);

                transactions = history
                    ?? new List<BankTransaction>();

                return true;
            }
            catch (Exception exception)
            {
                error = "Lecture de l'historique impossible : "
                    + exception.Message;

                transactions = new List<BankTransaction>();
                return false;
            }
        }

        /// <summary>
        /// Virement central en centimes.
        /// La banque centrale contrôle les fonds, l'identité,
        /// l'autorité et l'identifiant anti-doublon.
        /// </summary>
        public static bool TryTransfer(
            ulong sourcePlatformId,
            ulong targetPlatformId,
            long amountCents,
            string nonce,
            out string error)
        {
            error = null;

            if (!IsReady)
            {
                error = "La banque du serveur est indisponible.";
                return false;
            }

            if (sourcePlatformId == 0 ||
                targetPlatformId == 0 ||
                sourcePlatformId == targetPlatformId)
            {
                error = "Les comptes du virement sont invalides.";
                return false;
            }

            if (amountCents <= 0 ||
                amountCents > MaxTransferCents)
            {
                error = "Le montant du virement est invalide.";
                return false;
            }

            Guid nonceGuid;

            if (string.IsNullOrWhiteSpace(nonce) ||
                !Guid.TryParseExact(nonce, "N", out nonceGuid))
            {
                error = "Identifiant de transaction invalide.";
                return false;
            }

            string sourceId;
            string targetId;

            if (!TryGetPersistentId(
                sourcePlatformId,
                out sourceId,
                out error))
            {
                return false;
            }

            if (!TryGetPersistentId(
                targetPlatformId,
                out targetId,
                out error))
            {
                return false;
            }

            try
            {
                EconomyRuntime runtime =
                    EconomyRuntimeBootstrap.Runtime;

                if (runtime == null ||
                    runtime.Wallets == null ||
                    runtime.BankOperations == null)
                {
                    error = "Le système économique est indisponible.";
                    return false;
                }

                if (runtime.Wallets.GetWallet(sourceId) == null ||
                    runtime.Wallets.GetWallet(targetId) == null)
                {
                    error = "Un des comptes bancaires est introuvable.";
                    return false;
                }

                string requestId =
                    "phone-transfer:"
                    + sourcePlatformId.ToString(
                        CultureInfo.InvariantCulture)
                    + ":"
                    + nonceGuid.ToString("N");

                return runtime.BankOperations.Transfer(
                    true,
                    requestId,
                    sourceId,
                    targetId,
                    amountCents,
                    out error);
            }
            catch (Exception exception)
            {
                error = "Virement impossible : "
                    + exception.Message;

                return false;
            }
        }

        /// <summary>
        /// Virement WindyPhone en centimes.
        /// Aucune conversion supplémentaire n'est effectuée.
        /// Exemple : 150 correspond directement à 1,50 €.
        /// </summary>
        public static bool TryTransferPhoneAmount(
            ulong sourcePlatformId,
            ulong targetPlatformId,
            long amountCents,
            string nonce,
            out string error)
        {
            error = null;

            if (amountCents <= 0 ||
                amountCents > MaxTransferCents)
            {
                error = "Le montant du virement est invalide.";
                return false;
            }

            return TryTransfer(
                sourcePlatformId,
                targetPlatformId,
                amountCents,
                nonce,
                out error);
        }
    }
}
