
using System;
using FusionDarkRP.Economy;
using LabFusion.Network;
using LabFusion.Player;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    public static class PhysicalCashPickupService
    {
        private static readonly object Sync = new object();

        private const float MaximumPickupDistance = 2.5f;

        public static bool TryPickup(
            PlayerID player,
            string requestId,
            string cashId,
            out string error)
        {
            error = null;

            lock (Sync)
            {
                if (!IsHost())
                {
                    error = "Action réservée à l'hôte.";
                    return false;
                }

                if (player == null || !player.IsValid)
                {
                    error = "Joueur invalide.";
                    return false;
                }

                if (!PhysicalCashRequestId.IsValid(requestId))
                {
                    error = "Identifiant de requête invalide.";
                    return false;
                }

                if (!Guid.TryParseExact(
                    cashId,
                    "N",
                    out Guid parsedCashId)
                    || parsedCashId == Guid.Empty)
                {
                    error = "Identifiant du billet invalide.";
                    return false;
                }

                cashId = parsedCashId.ToString("N");

                if (!PlayerIdentityService.TryGetPersistentId(
                    player,
                    out string playerId))
                {
                    error = "Identité persistante indisponible.";
                    return false;
                }

                if (!PhysicalCashOwnershipTracker.TryGetOwner(
                    cashId,
                    out string ownerId))
                {
                    error = "Propriétaire du billet introuvable.";
                    return false;
                }

                if (!TryFindCashObject(
                    cashId,
                    out GameObject cashObject))
                {
                    error = "Billet physique absent du monde.";
                    return false;
                }

                Transform playerRig =
                    PhysicalCashRigAnchorResolver.Resolve(player);

                if (playerRig == null)
                {
                    error = "Position du joueur indisponible.";
                    return false;
                }

                if (!IsCloseEnough(
                    playerRig.position,
                    cashObject.transform.position))
                {
                    error = "Billet trop éloigné.";
                    return false;
                }

                bool transferred = false;
                PlayerID ownerPlayer = null;

                if (!string.Equals(
                    ownerId,
                    playerId,
                    StringComparison.Ordinal))
                {
                    if (!TryFindPlayer(ownerId, out ownerPlayer))
                    {
                        error =
                            "Le propriétaire initial n'est plus connecté. "
                            + "Transfert impossible pour le moment.";

                        return false;
                    }

                    if (!PhysicalCashWorldManager.TryTransferCash(
                        ownerPlayer,
                        player,
                        requestId,
                        cashId,
                        out _,
                        out error))
                    {
                        return false;
                    }

                    transferred = true;
                }

                // Un transfert économique marque déjà son requestId
                // comme traité dans WalletService.
                // L'encaissement doit donc recevoir un nouvel identifiant.
                string redemptionRequestId = transferred
                    ? PhysicalCashRequestId.Create()
                    : requestId;

                PhysicalCashResult redemption =
                    PhysicalCashSessionManager.RedeemCash(
                        player,
                        redemptionRequestId,
                        cashId);

                if (redemption == null || !redemption.Success)
                {
                    string redemptionError =
                        redemption == null
                            ? "Réponse du portefeuille vide."
                            : redemption.Message;

                    if (transferred)
                    {
                        bool restored =
                            PhysicalCashWorldManager.TryTransferCash(
                                player,
                                ownerPlayer,
                                PhysicalCashRequestId.Create(),
                                cashId,
                                out _,
                                out string rollbackError);

                        if (!restored)
                        {
                            error =
                                "Encaissement refusé : "
                                + redemptionError
                                + ". Restauration impossible : "
                                + rollbackError;

                            return false;
                        }
                    }

                    error = redemptionError;
                    return false;
                }

                string destroyError;

                if (!PhysicalCashWorldManager.TryDestroy(
                    cashId,
                    out destroyError))
                {
                    MelonLogger.Error(
                        "[FusionDarkRP Cash] Billet encaissé, "
                        + "mais destruction impossible : "
                        + cashId
                        + " | "
                        + destroyError);
                }
                else
                {
                    PhysicalCashOwnershipTracker.TryForget(
                        cashId,
                        playerId);
                }

                MelonLogger.Msg(
                    "[FusionDarkRP Cash] Billet encaissé : "
                    + cashId
                    + " | Joueur : "
                    + playerId);

                return true;
            }
        }

        private static bool TryFindPlayer(
            string persistentId,
            out PlayerID result)
        {
            result = null;

            if (string.IsNullOrWhiteSpace(persistentId))
                return false;

            foreach (PlayerID candidate in PlayerIDManager.PlayerIDs)
            {
                if (candidate == null || !candidate.IsValid)
                    continue;

                if (!PlayerIdentityService.TryGetPersistentId(
                    candidate,
                    out string candidateId))
                {
                    continue;
                }

                if (!string.Equals(
                    candidateId,
                    persistentId,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                result = candidate;
                return true;
            }

            return false;
        }

        private static bool TryFindCashObject(
            string cashId,
            out GameObject cashObject)
        {
            cashObject = null;

            string[] objectIds =
                WorldModelRuntimeManager.GetSpawnedObjectIds();

            if (objectIds == null)
                return false;

            foreach (string objectId in objectIds)
            {
                if (string.IsNullOrWhiteSpace(objectId))
                    continue;

                if (!WorldModelRuntimeManager.TryGet(
                    objectId,
                    out GameObject candidate)
                    || candidate == null)
                {
                    continue;
                }

                if (!PhysicalCashGrabDetector.TryExtractCashId(
                    candidate.name,
                    out string candidateCashId))
                {
                    continue;
                }

                if (!string.Equals(
                    candidateCashId,
                    cashId,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                cashObject = candidate;
                return true;
            }

            return false;
        }

        private static bool IsCloseEnough(
            Vector3 playerPosition,
            Vector3 cashPosition)
        {
            if (!IsFinite(playerPosition)
                || !IsFinite(cashPosition))
                return false;

            float distanceSquared =
                (playerPosition - cashPosition).sqrMagnitude;

            return !float.IsNaN(distanceSquared)
                && !float.IsInfinity(distanceSquared)
                && distanceSquared <=
                    MaximumPickupDistance * MaximumPickupDistance;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x)
                && IsFinite(value.y)
                && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value)
                && !float.IsInfinity(value);
        }

        private static bool IsHost()
        {
            try
            {
                return NetworkInfo.HasServer
                    && NetworkInfo.IsHost;
            }
            catch
            {
                return false;
            }
        }
    }
}
