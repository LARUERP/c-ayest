
using System;
using System.Collections.Generic;
using LabFusion.Player;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Detecte les especes placees dans la fente basse
    /// d'un distributeur et declenche leur depot physique.
    /// </summary>
    public static class PhysicalCashAtmDepositInteraction
    {
        private const float ScanInterval = 0.12f;
        private const float RetryDelay = 2f;

        // Doit correspondre exactement au lecteur de carte
        // utilise par AtmPhysicalInteraction.
        private static readonly Vector3 CardSlotCenter =
            new Vector3(-0.065f, 0.3675f, -0.205f);

        private static readonly Vector3 CardSlotHalfExtents =
            new Vector3(0.075f, 0.045f, 0.055f);

        private static readonly Vector3 CashSlotCenter =
            new Vector3(0f, 0.28f, -0.183f);

        private static readonly Vector3 CashSlotSize =
            new Vector3(0.30f, 0.12f, 0.20f);

        private static readonly Dictionary<string, float> Requests =
            new Dictionary<string, float>(
                StringComparer.Ordinal
            );

        private static bool initialized;
        private static float nextScan;

        public static bool IsInitialized => initialized;

        public static void Initialize()
        {
            if (initialized)
                return;

            Requests.Clear();

            PhysicalCashAtmDepositNetwork.ResultReceived -=
                OnDepositResult;

            PhysicalCashAtmDepositNetwork.ResultReceived +=
                OnDepositResult;

            initialized = true;
            nextScan = 0f;

            MelonLogger.Msg(
                "[FusionDarkRP ATM] Detection des depots physiques initialisee."
            );
        }

        public static void Update()
        {
            if (!initialized)
                return;

            if (!WorldModelRuntimeManager.IsInitialized)
                return;

            if (Time.realtimeSinceStartup < nextScan)
                return;

            nextScan =
                Time.realtimeSinceStartup + ScanInterval;

            try
            {
                ScanAtms();
                CleanupExpiredRequests();
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP ATM] Detection depot : "
                    + exception
                );
            }
        }

        private static void ScanAtms()
        {
            string[] objectIds =
                WorldModelRuntimeManager.GetSpawnedObjectIds();

            if (objectIds == null || objectIds.Length == 0)
                return;

            PlayerID localPlayer =
                PlayerIDManager.LocalID;

            if (localPlayer == null || !localPlayer.IsValid)
                return;

            foreach (string atmObjectId in objectIds)
            {
                if (string.IsNullOrWhiteSpace(atmObjectId))
                    continue;

                if (!WorldModelRuntimeManager.TryGetModelId(
                    atmObjectId,
                    out string modelId))
                    continue;

                if (!string.Equals(
                    modelId,
                    "ATM",
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!WorldModelRuntimeManager.TryGet(
                    atmObjectId,
                    out GameObject atm) ||
                    atm == null)
                    continue;

                if (!TryGetBankCard(
                    atm,
                    out string cardId))
                    continue;

                if (!TryGetCashInDepositSlot(
                    atm,
                    objectIds,
                    out string cashId))
                    continue;

                string requestKey =
                    atmObjectId + ":" + cashId;

                if (Requests.TryGetValue(
                    requestKey,
                    out float previousRequest))
                {
                    if (Time.realtimeSinceStartup - previousRequest
                        < RetryDelay)
                    {
                        continue;
                    }
                }

                Requests[requestKey] =
                    Time.realtimeSinceStartup;

                bool sent =
                    PhysicalCashAtmDepositNetwork.RequestDeposit(
                        atmObjectId,
                        cashId,
                        cardId
                    );

                if (!sent)
                {
                    Requests.Remove(requestKey);

                    MelonLogger.Warning(
                        "[FusionDarkRP ATM] Demande de depot non envoyee."
                    );
                }
                else
                {
                    MelonLogger.Msg(
                        "[FusionDarkRP ATM] Depot detecte : "
                        + cashId
                    );
                }
            }
        }

        /// <summary>
        /// Recupere le CardId depuis les metadonnees synchronisees
        /// du support physique de la carte.
        /// </summary>
        private static bool TryGetBankCard(
            GameObject atm,
            out string cardId)
        {
            cardId = null;

            if (atm == null)
                return false;

            Vector3 center =
                atm.transform.TransformPoint(
                    CardSlotCenter
                );

            Vector3 scale =
                atm.transform.lossyScale;

            Vector3 halfExtents =
                Vector3.Scale(
                    CardSlotHalfExtents,
                    new Vector3(
                        Mathf.Abs(scale.x),
                        Mathf.Abs(scale.y),
                        Mathf.Abs(scale.z)
                    )
                );

            Collider[] colliders =
                Physics.OverlapBox(
                    center,
                    halfExtents,
                    atm.transform.rotation
                );

            if (colliders == null)
                return false;

            foreach (Collider collider in colliders)
            {
                if (collider == null)
                    continue;

                if (BankCardHolsterMetadataNetwork.TryGetCardId(
                    collider.gameObject,
                    out cardId))
                {
                    return true;
                }

                Transform candidate =
                    collider.attachedRigidbody != null
                        ? collider.attachedRigidbody.transform
                        : collider.transform;

                while (candidate != null)
                {
                    if (BankCardHolsterMetadataNetwork.TryGetCardId(
                        candidate.gameObject,
                        out cardId))
                    {
                        return true;
                    }

                    if (candidate == atm.transform)
                        break;

                    candidate = candidate.parent;
                }
            }

            cardId = null;
            return false;
        }

        private static bool TryGetCashInDepositSlot(
            GameObject atm,
            string[] objectIds,
            out string cashId)
        {
            cashId = null;

            if (atm == null || objectIds == null)
                return false;

            Bounds slotBounds =
                new Bounds(
                    CashSlotCenter,
                    CashSlotSize
                );

            foreach (string objectId in objectIds)
            {
                if (string.IsNullOrWhiteSpace(objectId))
                    continue;

                if (!WorldModelRuntimeManager.TryGet(
                    objectId,
                    out GameObject candidate) ||
                    candidate == null)
                    continue;

                if (!PhysicalCashGrabDetector.TryExtractCashId(
                    candidate.name,
                    out string detectedCashId))
                    continue;

                Vector3 localPosition =
                    atm.transform.InverseTransformPoint(
                        candidate.transform.position
                    );

                if (!slotBounds.Contains(localPosition))
                    continue;

                cashId = detectedCashId;
                return true;
            }

            return false;
        }

        private static void OnDepositResult(
            PhysicalCashAtmDepositResultPayload result)
        {
            if (result == null)
                return;

            if (!string.IsNullOrWhiteSpace(result.CashId))
            {
                List<string> completed =
                    new List<string>();

                foreach (string key in Requests.Keys)
                {
                    if (key.EndsWith(
                        ":" + result.CashId,
                        StringComparison.Ordinal))
                    {
                        completed.Add(key);
                    }
                }

                foreach (string key in completed)
                    Requests.Remove(key);
            }

            if (result.Success)
            {
                MelonLogger.Msg(
                    "[FusionDarkRP ATM] "
                    + (result.Message ?? "Depot accepte.")
                );
            }
            else
            {
                MelonLogger.Warning(
                    "[FusionDarkRP ATM] "
                    + (result.Message ?? "Depot refuse.")
                );
            }
        }

        private static void CleanupExpiredRequests()
        {
            if (Requests.Count == 0)
                return;

            List<string> expired =
                new List<string>();

            float now = Time.realtimeSinceStartup;

            foreach (KeyValuePair<string, float> entry in Requests)
            {
                if (now - entry.Value > 30f)
                    expired.Add(entry.Key);
            }

            foreach (string key in expired)
                Requests.Remove(key);
        }

        public static void Shutdown()
        {
            PhysicalCashAtmDepositNetwork.ResultReceived -=
                OnDepositResult;

            Requests.Clear();

            initialized = false;
            nextScan = 0f;

            MelonLogger.Msg(
                "[FusionDarkRP ATM] Detection des depots physiques arretee."
            );
        }
    }
}
