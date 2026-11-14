using System;
using System.Collections.Generic;
using System.Text.Json;
using FusionDarkRP.Economy;
using LabFusion.Network;
using LabFusion.Network.Serialization;
using LabFusion.Player;
using LabFusion.SDK.Modules;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    public sealed class PhysicalCashAtmDepositPacket : INetSerializable
    {
        public byte[] Payload = Array.Empty<byte>();

        public int? GetSize()
        {
            return 4 + (Payload != null ? Payload.Length : 0);
        }

        public void Serialize(INetSerializer serializer)
        {
            serializer.SerializeValue(ref Payload);
        }
    }

    public sealed class PhysicalCashAtmDepositPayload
    {
        public string RequestId { get; set; }
        public string AtmObjectId { get; set; }
        public string CashId { get; set; }
        public string CardId { get; set; }
    }

    public sealed class PhysicalCashAtmDepositResultPayload
    {
        public byte TargetId { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public string CashId { get; set; }
    }

    public sealed class PhysicalCashAtmDepositHandler : ModuleMessageHandler
    {
        private const int MaximumPacketSize = 1024;
        private const float MaximumAtmDistance = 3f;
        private const float RequestCooldown = 0.5f;

        private static readonly Vector3 CashSlotCenter =
            new Vector3(0f, 0.28f, -0.183f);

        private static readonly Vector3 CashSlotSize =
            new Vector3(0.30f, 0.12f, 0.20f);

        // Coordonnées identiques à AtmPhysicalInteraction
        // et PhysicalCashAtmDepositInteraction.
        private static readonly Vector3 CardReaderCenter =
            new Vector3(-0.065f, 0.3675f, -0.205f);

        private static readonly Vector3 CardReaderHalfExtents =
            new Vector3(0.075f, 0.045f, 0.055f);

        private static readonly object Sync = new object();

        private static readonly Dictionary<byte, float> LastRequests =
            new Dictionary<byte, float>();

        public override ExpectedReceiverType ExpectedReceiver =>
            ExpectedReceiverType.ServerOnly;

        protected override bool OnPreRelayMessage(ReceivedMessage message)
        {
            return message.Bytes != null
                && message.Bytes.Length > 0
                && message.Bytes.Length <= MaximumPacketSize + 4;
        }

        protected override void OnHandleMessage(ReceivedMessage message)
        {
            if (!NetworkInfo.HasServer ||
                !NetworkInfo.IsHost ||
                !message.IsServerHandled ||
                !message.Sender.HasValue)
                return;

            PlayerID player =
                PlatformIds.GetPlayerID(message.Sender.Value);

            if (player == null || !player.IsValid)
                return;

            try
            {
                if (!CanProcessRequest(player.SmallID))
                {
                    SendResult(
                        player,
                        false,
                        "Patientez avant de déposer d'autres espèces.",
                        null);
                    return;
                }

                PhysicalCashAtmDepositPacket packet =
                    message.ReadData<PhysicalCashAtmDepositPacket>();

                if (packet == null ||
                    packet.Payload == null ||
                    packet.Payload.Length == 0 ||
                    packet.Payload.Length > MaximumPacketSize)
                {
                    SendResult(
                        player,
                        false,
                        "Demande de dépôt invalide.",
                        null);
                    return;
                }

                PhysicalCashAtmDepositPayload data =
                    JsonSerializer.Deserialize<PhysicalCashAtmDepositPayload>(
                        packet.Payload);

                if (!ValidatePayload(data))
                {
                    SendResult(
                        player,
                        false,
                        "Informations de dépôt invalides.",
                        null);
                    return;
                }

                ProcessLocalDeposit(player, data);
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP ATM] Erreur dépôt physique : "
                    + exception);

                SendResult(
                    player,
                    false,
                    "Erreur interne pendant le dépôt.",
                    null);
            }
        }

        private static bool ValidatePayload(
            PhysicalCashAtmDepositPayload data)
        {
            return data != null
                && PhysicalCashRequestId.IsValid(data.RequestId)
                && IsGuid(data.AtmObjectId)
                && IsGuid(data.CashId)
                && IsGuid(data.CardId);
        }

        private static bool IsGuid(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && Guid.TryParseExact(value, "N", out Guid parsed)
                && parsed != Guid.Empty;
        }

        public static bool CanProcessRequest(byte playerId)
        {
            lock (Sync)
            {
                float now = Time.realtimeSinceStartup;

                if (LastRequests.TryGetValue(
                    playerId,
                    out float previous) &&
                    now - previous < RequestCooldown)
                {
                    return false;
                }

                LastRequests[playerId] = now;
                return true;
            }
        }

        public static void ProcessLocalDeposit(
            PlayerID player,
            PhysicalCashAtmDepositPayload data)
        {
            if (player == null || !player.IsValid)
                return;

            if (!NetworkInfo.HasServer || !NetworkInfo.IsHost)
            {
                SendResult(
                    player,
                    false,
                    "L'hôte n'est pas disponible.",
                    data != null ? data.CashId : null);
                return;
            }

            if (!ValidatePayload(data))
            {
                SendResult(
                    player,
                    false,
                    "Informations de dépôt invalides.",
                    null);
                return;
            }

            try
            {
                ProcessDeposit(player, data);
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP ATM] Traitement du dépôt impossible : "
                    + exception);

                SendResult(
                    player,
                    false,
                    "Erreur pendant le dépôt des espèces.",
                    data.CashId);
            }
        }

        private static void ProcessDeposit(
            PlayerID player,
            PhysicalCashAtmDepositPayload data)
        {
            if (!TryGetAtm(data.AtmObjectId, out GameObject atm))
            {
                SendResult(
                    player,
                    false,
                    "Distributeur introuvable.",
                    data.CashId);
                return;
            }

            if (!IsPlayerNearAtm(player, atm))
            {
                SendResult(
                    player,
                    false,
                    "Vous êtes trop loin du distributeur.",
                    data.CashId);
                return;
            }

            if (!TryGetCardInReader(
                    atm, data.CardId, out string detectedCardId) ||
                !string.Equals(
                    detectedCardId,
                    data.CardId,
                    StringComparison.Ordinal))
            {
                SendResult(
                    player,
                    false,
                    "Insérez votre carte bancaire dans le lecteur.",
                    data.CashId);
                return;
            }

            if (!IsCardAuthorized(player, detectedCardId))
            {
                SendResult(
                    player,
                    false,
                    "Carte bancaire invalide ou non autorisée.",
                    data.CashId);
                return;
            }

            if (!TryGetCashObject(
                data.CashId,
                out GameObject cashObject))
            {
                SendResult(
                    player,
                    false,
                    "Billet ou pièce introuvable dans le monde.",
                    data.CashId);
                return;
            }

            if (!PhysicalCashGrabDetector.TryExtractCashId(
                cashObject.name,
                out string verifiedCashId) ||
                !string.Equals(
                    verifiedCashId,
                    data.CashId,
                    StringComparison.Ordinal))
            {
                SendResult(
                    player,
                    false,
                    "Identifiant des espèces invalide.",
                    data.CashId);
                return;
            }

            if (!IsCashInsideDepositSlot(atm, cashObject))
            {
                SendResult(
                    player,
                    false,
                    "Placez les espèces dans la fente à billets.",
                    data.CashId);
                return;
            }

            if (!PlayerIdentityService.TryGetPersistentId(
                player,
                out string persistentPlayerId) ||
                string.IsNullOrWhiteSpace(persistentPlayerId))
            {
                SendResult(
                    player,
                    false,
                    "Impossible de vérifier votre identité.",
                    data.CashId);
                return;
            }

            if (!PhysicalCashOwnershipTracker.TryGetOwner(
                data.CashId,
                out string trackedOwnerId) ||
                !string.Equals(
                    trackedOwnerId,
                    persistentPlayerId,
                    StringComparison.Ordinal))
            {
                SendResult(
                    player,
                    false,
                    "Ce billet ne vous appartient pas.",
                    data.CashId);
                return;
            }

            EconomyRuntime runtime =
                EconomyRuntimeBootstrap.Runtime;

            if (runtime == null || runtime.Wallets == null)
            {
                SendResult(
                    player,
                    false,
                    "Le service bancaire est indisponible.",
                    data.CashId);
                return;
            }

            if (!runtime.Wallets.TryDepositPhysicalCashToBank(
                true,
                data.RequestId,
                persistentPlayerId,
                data.CashId,
                out string depositError))
            {
                SendResult(
                    player,
                    false,
                    string.IsNullOrWhiteSpace(depositError)
                        ? "Le dépôt bancaire a été refusé."
                        : depositError,
                    data.CashId);
                return;
            }

            string confirmation =
                GetDepositConfirmation(cashObject.name);

            if (!PhysicalCashWorldManager.TryDestroy(
                data.CashId,
                out string destroyError))
            {
                MelonLogger.Error(
                    "[FusionDarkRP ATM] Dépôt crédité mais suppression "
                    + "du billet impossible : "
                    + data.CashId
                    + " | "
                    + destroyError);
            }
            else
            {
                PhysicalCashOwnershipTracker.TryForget(
                    data.CashId,
                    persistentPlayerId);
            }

            SendResult(
                player,
                true,
                confirmation,
                data.CashId);

            MelonLogger.Msg(
                "[FusionDarkRP ATM] Dépôt bancaire confirmé : "
                + data.CashId
                + " | Joueur : "
                + persistentPlayerId);
        }

        private static bool TryGetCardInReader(
            GameObject atm,
            string expectedCardId,
            out string cardId)
        {
            cardId = null;

            if (atm == null)
                return false;

            Vector3 center =
                atm.transform.TransformPoint(CardReaderCenter);

            Vector3 scale = atm.transform.lossyScale;

            Vector3 halfExtents =
                Vector3.Scale(
                    CardReaderHalfExtents,
                    new Vector3(
                        Mathf.Abs(scale.x),
                        Mathf.Abs(scale.y),
                        Mathf.Abs(scale.z)));

            Collider[] colliders = Physics.OverlapBox(
                center,
                halfExtents,
                atm.transform.rotation);

            if (colliders == null || colliders.Length == 0)
                return false;

            string firstCard = null;

            foreach (Collider collider in colliders)
            {
                if (collider == null)
                    continue;

                // Même recherche que côté joueur : l'objet du collider,
                // celui de son rigidbody, puis les parents (les métadonnées
                // de la carte peuvent être sur la racine de l'entité).
                Transform candidate = collider.transform;

                if (collider.attachedRigidbody != null)
                {
                    string viaBody;

                    if (BankCardHolsterMetadataNetwork.TryGetCardId(
                            collider.attachedRigidbody.gameObject,
                            out viaBody) &&
                        IsGuid(viaBody))
                    {
                        if (string.Equals(
                                viaBody,
                                expectedCardId,
                                StringComparison.Ordinal))
                        {
                            cardId = viaBody;
                            return true;
                        }

                        if (firstCard == null)
                            firstCard = viaBody;
                    }
                }

                int depth = 0;

                while (candidate != null && depth < 8)
                {
                    string found;

                    if (BankCardHolsterMetadataNetwork.TryGetCardId(
                            candidate.gameObject,
                            out found) &&
                        IsGuid(found))
                    {
                        if (string.Equals(
                                found,
                                expectedCardId,
                                StringComparison.Ordinal))
                        {
                            cardId = found;
                            return true;
                        }

                        if (firstCard == null)
                            firstCard = found;

                        break;
                    }

                    if (candidate == atm.transform)
                        break;

                    candidate = candidate.parent;
                    depth++;
                }
            }

            cardId = firstCard;
            return firstCard != null;
        }

        private static bool IsCardAuthorized(
            PlayerID player,
            string cardId)
        {
            if (player == null ||
                !player.IsValid ||
                !IsGuid(cardId))
                return false;

            if (!PlayerIdentityService.TryGetPersistentId(
                player,
                out string persistentId) ||
                string.IsNullOrWhiteSpace(persistentId))
                return false;

            EconomyRuntime runtime =
                EconomyRuntimeBootstrap.Runtime;

            if (runtime == null || runtime.BankCards == null)
                return false;

            return runtime.BankCards.IsOwnedBy(
                Guid.ParseExact(cardId, "N").ToString("N"),
                persistentId);
        }

        private static bool TryGetAtm(
            string atmObjectId,
            out GameObject atm)
        {
            atm = null;

            if (!IsGuid(atmObjectId))
                return false;

            if (!WorldModelRuntimeManager.TryGetModelId(
                atmObjectId,
                out string modelId))
                return false;

            if (!string.Equals(
                modelId,
                "ATM",
                StringComparison.OrdinalIgnoreCase))
                return false;

            return WorldModelRuntimeManager.TryGet(
                atmObjectId,
                out atm) && atm != null;
        }

        private static bool IsPlayerNearAtm(
            PlayerID player,
            GameObject atm)
        {
            if (player == null ||
                !player.IsValid ||
                atm == null)
                return false;

            Transform rig =
                PhysicalCashRigAnchorResolver.Resolve(player);

            if (rig == null)
                return false;

            return Vector3.Distance(
                rig.position,
                atm.transform.position) <= MaximumAtmDistance;
        }

        private static bool TryGetCashObject(
            string cashId,
            out GameObject cashObject)
        {
            cashObject = null;

            if (!IsGuid(cashId))
                return false;

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
                    out GameObject candidate) ||
                    candidate == null)
                    continue;

                if (!PhysicalCashGrabDetector.TryExtractCashId(
                    candidate.name,
                    out string candidateCashId))
                    continue;

                if (!string.Equals(
                    candidateCashId,
                    cashId,
                    StringComparison.Ordinal))
                    continue;

                cashObject = candidate;
                return true;
            }

            return false;
        }

        private static bool IsCashInsideDepositSlot(
            GameObject atm,
            GameObject cashObject)
        {
            if (atm == null || cashObject == null)
                return false;

            Vector3 localPosition =
                atm.transform.InverseTransformPoint(
                    cashObject.transform.position);

            Bounds slotBounds =
                new Bounds(CashSlotCenter, CashSlotSize);

            return slotBounds.Contains(localPosition);
        }

        private static string GetDepositConfirmation(
            string objectName)
        {
            const string prefix = "FusionDarkRP_Cash_";

            if (string.IsNullOrWhiteSpace(objectName) ||
                !objectName.StartsWith(
                    prefix,
                    StringComparison.Ordinal))
            {
                return "Dépôt accepté : espèces créditées sur votre compte bancaire.";
            }

            int separator = objectName.LastIndexOf('_');

            if (separator <= prefix.Length)
            {
                return "Dépôt accepté : espèces créditées sur votre compte bancaire.";
            }

            string itemId =
                objectName.Substring(
                    prefix.Length,
                    separator - prefix.Length);

            if (!PhysicalCashCatalog.TryGet(
                itemId,
                out PhysicalCashItem item) ||
                item == null)
            {
                return "Dépôt accepté : espèces créditées sur votre compte bancaire.";
            }

            return "Dépôt accepté : +"
                + PhysicalCashCatalog.FormatMoney(item.ValueCents)
                + " crédités sur votre compte bancaire.";
        }

        private static void SendResult(
            PlayerID player,
            bool success,
            string message,
            string cashId)
        {
            if (player == null || !player.IsValid)
                return;

            PhysicalCashAtmDepositResultPayload result =
                new PhysicalCashAtmDepositResultPayload
                {
                    TargetId = player.SmallID,
                    Success = success,
                    Message = message ?? string.Empty,
                    CashId = cashId ?? string.Empty
                };

            if (NetworkInfo.IsHost &&
                PlayerIDManager.LocalID != null &&
                PlayerIDManager.LocalID.IsValid &&
                PlayerIDManager.LocalID.SmallID == player.SmallID)
            {
                PhysicalCashAtmDepositNetwork.NotifyResult(result);
                return;
            }

            try
            {
                PhysicalCashAtmDepositPacket packet =
                    new PhysicalCashAtmDepositPacket
                    {
                        Payload =
                            JsonSerializer.SerializeToUtf8Bytes(result)
                    };

                if (packet.Payload == null ||
                    packet.Payload.Length == 0 ||
                    packet.Payload.Length > MaximumPacketSize)
                    return;

                MessageRoute route = new MessageRoute(
                    player.SmallID,
                    NetworkChannel.Reliable);

                MessageRelay.RelayModule<
                    PhysicalCashAtmDepositResultHandler,
                    PhysicalCashAtmDepositPacket>(
                        packet,
                        route);
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP ATM] Envoi du résultat impossible : "
                    + exception);
            }
        }

        public static void ClearRateLimits()
        {
            lock (Sync)
                LastRequests.Clear();
        }
    }

    public sealed class PhysicalCashAtmDepositResultHandler
        : ModuleMessageHandler
    {
        private const int MaximumPacketSize = 1024;

        public override ExpectedReceiverType ExpectedReceiver =>
            ExpectedReceiverType.ClientsOnly;

        protected override bool OnPreRelayMessage(ReceivedMessage message)
        {
            return message.Bytes != null
                && message.Bytes.Length > 0
                && message.Bytes.Length <= MaximumPacketSize + 4;
        }

        protected override void OnHandleMessage(ReceivedMessage message)
        {
            try
            {
                if (!message.Sender.HasValue ||
                    message.Sender.Value != 0)
                    return;

                PhysicalCashAtmDepositPacket packet =
                    message.ReadData<PhysicalCashAtmDepositPacket>();

                if (packet == null ||
                    packet.Payload == null ||
                    packet.Payload.Length == 0 ||
                    packet.Payload.Length > MaximumPacketSize)
                    return;

                PhysicalCashAtmDepositResultPayload result =
                    JsonSerializer.Deserialize<
                        PhysicalCashAtmDepositResultPayload>(
                            packet.Payload);

                if (result == null)
                    return;

                PlayerID local = PlayerIDManager.LocalID;

                if (local == null ||
                    !local.IsValid ||
                    local.SmallID != result.TargetId)
                    return;

                PhysicalCashAtmDepositNetwork.NotifyResult(result);
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP ATM] Réception du résultat impossible : "
                    + exception);
            }
        }
    }

    public static class PhysicalCashAtmDepositNetwork
    {
        private const int MaximumPacketSize = 1024;

        public static event Action<PhysicalCashAtmDepositResultPayload>
            ResultReceived;

        public static bool RequestDeposit(
            string atmObjectId,
            string cashId,
            string cardId)
        {
            if (!IsGuid(atmObjectId) ||
                !IsGuid(cashId) ||
                !IsGuid(cardId) ||
                !NetworkInfo.HasServer)
                return false;

            PhysicalCashAtmDepositPayload data =
                new PhysicalCashAtmDepositPayload
                {
                    RequestId = PhysicalCashRequestId.Create(),
                    AtmObjectId = Guid.ParseExact(atmObjectId, "N").ToString("N"),
                    CashId = Guid.ParseExact(cashId, "N").ToString("N"),
                    CardId = Guid.ParseExact(cardId, "N").ToString("N")
                };

            try
            {
                if (NetworkInfo.IsHost)
                {
                    PlayerID local = PlayerIDManager.LocalID;

                    if (local == null || !local.IsValid)
                        return false;

                    if (!PhysicalCashAtmDepositHandler.CanProcessRequest(
                        local.SmallID))
                    {
                        NotifyResult(
                            new PhysicalCashAtmDepositResultPayload
                            {
                                TargetId = local.SmallID,
                                Success = false,
                                Message = "Patientez avant un nouveau dépôt.",
                                CashId = data.CashId
                            });

                        return true;
                    }

                    PhysicalCashAtmDepositHandler.ProcessLocalDeposit(
                        local,
                        data);

                    return true;
                }

                PhysicalCashAtmDepositPacket packet =
                    new PhysicalCashAtmDepositPacket
                    {
                        Payload =
                            JsonSerializer.SerializeToUtf8Bytes(data)
                    };

                if (packet.Payload == null ||
                    packet.Payload.Length == 0 ||
                    packet.Payload.Length > MaximumPacketSize)
                    return false;

                MessageRelay.RelayModule<
                    PhysicalCashAtmDepositHandler,
                    PhysicalCashAtmDepositPacket>(
                    packet,
                    CommonMessageRoutes.ReliableToServer);

                return true;
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP ATM] Envoi du dépôt impossible : "
                    + exception);

                return false;
            }
        }

        private static bool IsGuid(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && Guid.TryParseExact(value, "N", out Guid parsed)
                && parsed != Guid.Empty;
        }

        public static void NotifyResult(
            PhysicalCashAtmDepositResultPayload result)
        {
            if (result == null)
                return;

            ResultReceived?.Invoke(result);
        }
    }
}
