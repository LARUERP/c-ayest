using System;
using System.Collections.Generic;
using System.Text.Json;
using FusionDarkRP.Economy;
using LabFusion.Network;
using LabFusion.Network.Serialization;
using LabFusion.Player;
using LabFusion.SDK.Modules;
using MelonLoader;

namespace FusionDarkRP.Integration
{
    public sealed class PhysicalCashSpawnPacket : INetSerializable
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

    public sealed class PhysicalCashSpawnPayload
    {
        public string RequestId { get; set; }
        public string ItemId { get; set; }
        public string CardId { get; set; }
    }

    public sealed class PhysicalCashSpawnResultPayload
    {
        public byte TargetId { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public string CashId { get; set; }
    }

    public sealed class PhysicalCashSpawnRequestHandler
        : ModuleMessageHandler
    {
        private const int MaximumPacketSize = 1024;
        private const float RequestCooldown = 0.5f;

        private static readonly object Sync = new object();

        private static readonly Dictionary<byte, float> LastRequests =
            new Dictionary<byte, float>();

        public override ExpectedReceiverType ExpectedReceiver =>
            ExpectedReceiverType.ServerOnly;

        protected override bool OnPreRelayMessage(
            ReceivedMessage message)
        {
            return message.Bytes != null
                && message.Bytes.Length > 0
                && message.Bytes.Length <= MaximumPacketSize + 4;
        }

        protected override void OnHandleMessage(
            ReceivedMessage message)
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
                if (!CanProcessLocalRequest(player.SmallID))
                {
                    SendResult(
                        player,
                        false,
                        "Patientez avant de retirer un autre billet.",
                        null
                    );

                    return;
                }

                PhysicalCashSpawnPacket packet =
                    message.ReadData<PhysicalCashSpawnPacket>();

                if (packet == null ||
                    packet.Payload == null ||
                    packet.Payload.Length == 0 ||
                    packet.Payload.Length > MaximumPacketSize)
                {
                    SendResult(
                        player,
                        false,
                        "Demande de retrait invalide.",
                        null
                    );

                    return;
                }

                PhysicalCashSpawnPayload data =
                    JsonSerializer.Deserialize<PhysicalCashSpawnPayload>(
                        packet.Payload
                    );

                if (!ValidatePayload(data))
                {
                    SendResult(
                        player,
                        false,
                        "Dénomination ou identifiant invalide.",
                        null
                    );

                    return;
                }

                if (!IsCardAuthorized(player, data.CardId))
                {
                    SendResult(
                        player,
                        false,
                        "Carte bancaire invalide ou non autorisée.",
                        null
                    );

                    MelonLogger.Warning(
                        "[FusionDarkRP Cash] Carte non autorisée pour "
                        + player.SmallID
                    );

                    return;
                }

                ProcessLocalSpawn(player, data);
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Cash] Erreur demande de retrait : "
                    + exception
                );

                SendResult(
                    player,
                    false,
                    "Erreur interne pendant le retrait physique.",
                    null
                );
            }
        }

        private static bool ValidatePayload(
            PhysicalCashSpawnPayload data)
        {
            return data != null
                && PhysicalCashRequestId.IsValid(data.RequestId)
                && PhysicalCashCatalog.TryGet(
                    data.ItemId,
                    out PhysicalCashItem item
                )
                && item != null;
        }

        public static bool CanProcessLocalRequest(byte playerId)
        {
            lock (Sync)
            {
                float now =
                    UnityEngine.Time.realtimeSinceStartup;

                if (LastRequests.TryGetValue(
                    playerId,
                    out float previous)
                    && now - previous < RequestCooldown)
                {
                    return false;
                }

                LastRequests[playerId] = now;
                return true;
            }
        }

        internal const string WalletCardId = "wallet";

        private static bool IsCardAuthorized(
            PlayerID player,
            string cardId)
        {
            // Sortie d'espèce depuis son propre portefeuille : aucune carte
            // requise (l'argent est débité du liquide du joueur par l'hôte).
            if (string.Equals(
                    cardId, WalletCardId, StringComparison.Ordinal))
            {
                return player != null && player.IsValid;
            }

            if (player == null ||
                !player.IsValid ||
                string.IsNullOrWhiteSpace(cardId))
                return false;

            if (!Guid.TryParseExact(
                cardId,
                "N",
                out Guid parsed) ||
                parsed == Guid.Empty)
                return false;

            if (!PlayerIdentityService.TryGetPersistentId(
                player,
                out string persistentId) ||
                string.IsNullOrWhiteSpace(persistentId))
                return false;

            EconomyRuntime runtime =
                EconomyRuntimeBootstrap.Runtime;

            return runtime != null &&
                runtime.BankCards != null &&
                runtime.BankCards.IsOwnedBy(
                    parsed.ToString("N"),
                    persistentId
                );
        }

        public static void ProcessLocalSpawn(
            PlayerID player,
            PhysicalCashSpawnPayload data)
        {
            if (player == null || !player.IsValid)
                return;

            if (!NetworkInfo.HasServer || !NetworkInfo.IsHost)
            {
                SendResult(
                    player,
                    false,
                    "L'hôte n'est pas disponible.",
                    null
                );

                return;
            }

            if (!ValidatePayload(data))
            {
                SendResult(
                    player,
                    false,
                    "Dénomination ou identifiant invalide.",
                    null
                );

                return;
            }

            if (!IsCardAuthorized(player, data.CardId))
            {
                SendResult(
                    player,
                    false,
                    "Carte bancaire invalide ou non autorisée.",
                    null
                );

                return;
            }

            ProcessSpawn(player, data);
        }

        private static void ProcessSpawn(
            PlayerID player,
            PhysicalCashSpawnPayload data)
        {
            string cashId;
            string objectId;
            string error;

            bool success =
                PhysicalCashWorldManager.TryCreateAndSpawn(
                    player,
                    data.RequestId,
                    data.ItemId,
                    out cashId,
                    out objectId,
                    out error
                );

            if (!success)
            {
                SendResult(
                    player,
                    false,
                    string.IsNullOrWhiteSpace(error)
                        ? "Le retrait physique a échoué."
                        : error,
                    null
                );

                return;
            }

            PhysicalCashCatalog.TryGet(
                data.ItemId,
                out PhysicalCashItem item
            );

            string resultMessage =
                item != null
                    ? item.DisplayName + " créé dans le monde."
                    : "Argent physique créé.";

            SendResult(
                player,
                true,
                resultMessage,
                cashId
            );

            MelonLogger.Msg(
                "[FusionDarkRP Cash] Retrait physique confirmé : "
                + cashId
                + " | Joueur : "
                + player.SmallID
                + " | Objet : "
                + data.ItemId
            );
        }

        private static void SendResult(
            PlayerID player,
            bool success,
            string message,
            string cashId)
        {
            if (player == null || !player.IsValid)
                return;

            PhysicalCashSpawnResultPayload result =
                new PhysicalCashSpawnResultPayload
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
                PhysicalCashSpawnNetwork.NotifyResult(result);
                return;
            }

            try
            {
                PhysicalCashSpawnPacket packet =
                    new PhysicalCashSpawnPacket
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
                    NetworkChannel.Reliable
                );

                MessageRelay.RelayModule<
                    PhysicalCashSpawnResultHandler,
                    PhysicalCashSpawnPacket>(
                        packet,
                        route
                    );
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Cash] Envoi du résultat impossible : "
                    + exception
                );
            }
        }

        public static void ClearRateLimits()
        {
            lock (Sync)
                LastRequests.Clear();
        }
    }

    public sealed class PhysicalCashSpawnResultHandler
        : ModuleMessageHandler
    {
        private const int MaximumPacketSize = 1024;

        public override ExpectedReceiverType ExpectedReceiver =>
            ExpectedReceiverType.ClientsOnly;

        protected override bool OnPreRelayMessage(
            ReceivedMessage message)
        {
            return message.Bytes != null
                && message.Bytes.Length > 0
                && message.Bytes.Length <= MaximumPacketSize + 4;
        }

        protected override void OnHandleMessage(
            ReceivedMessage message)
        {
            try
            {
                if (!message.Sender.HasValue ||
                    message.Sender.Value != 0)
                    return;

                PhysicalCashSpawnPacket packet =
                    message.ReadData<PhysicalCashSpawnPacket>();

                if (packet == null ||
                    packet.Payload == null ||
                    packet.Payload.Length == 0 ||
                    packet.Payload.Length > MaximumPacketSize)
                    return;

                PhysicalCashSpawnResultPayload result =
                    JsonSerializer.Deserialize<PhysicalCashSpawnResultPayload>(
                        packet.Payload
                    );

                if (result == null)
                    return;

                PlayerID local = PlayerIDManager.LocalID;

                if (local == null ||
                    !local.IsValid ||
                    local.SmallID != result.TargetId)
                    return;

                PhysicalCashSpawnNetwork.NotifyResult(result);
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Cash] Réception du résultat impossible : "
                    + exception
                );
            }
        }
    }

    public static class PhysicalCashSpawnNetwork
    {
        private const int MaximumPacketSize = 1024;

        public static event Action<PhysicalCashSpawnResultPayload>
            ResultReceived;

        public static bool RequestSpawn(
            string itemId,
            string cardId)
        {
            Guid parsedCardId = Guid.Empty;

            bool walletRequest = string.Equals(
                cardId, WalletCardId, StringComparison.Ordinal);

            bool cardAccepted =
                walletRequest ||
                (!string.IsNullOrWhiteSpace(cardId) &&
                 Guid.TryParseExact(cardId, "N", out parsedCardId) &&
                 parsedCardId != Guid.Empty);

            if (string.IsNullOrWhiteSpace(itemId) ||
                !PhysicalCashCatalog.TryGet(
                    itemId,
                    out PhysicalCashItem item
                ) ||
                item == null ||
                !cardAccepted)
                return false;

            if (!NetworkInfo.HasServer)
                return false;

            string requestId = PhysicalCashRequestId.Create();

            try
            {
                PhysicalCashSpawnPayload data =
                    new PhysicalCashSpawnPayload
                    {
                        RequestId = requestId,
                        ItemId = itemId,
                        CardId = walletRequest
                            ? WalletCardId
                            : parsedCardId.ToString("N")
                    };

                if (NetworkInfo.IsHost)
                {
                    PlayerID local = PlayerIDManager.LocalID;

                    if (local == null || !local.IsValid)
                        return false;

                    if (!PhysicalCashSpawnRequestHandler
                        .CanProcessLocalRequest(local.SmallID))
                        return false;

                    PhysicalCashSpawnRequestHandler.ProcessLocalSpawn(
                        local,
                        data
                    );

                    return true;
                }

                PhysicalCashSpawnPacket packet =
                    new PhysicalCashSpawnPacket
                    {
                        Payload =
                            JsonSerializer.SerializeToUtf8Bytes(data)
                    };

                if (packet.Payload == null ||
                    packet.Payload.Length == 0 ||
                    packet.Payload.Length > MaximumPacketSize)
                    return false;

                MessageRelay.RelayModule<
                    PhysicalCashSpawnRequestHandler,
                    PhysicalCashSpawnPacket>(
                        packet,
                        CommonMessageRoutes.ReliableToServer
                    );

                return true;
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Cash] Demande de retrait impossible : "
                    + exception
                );

                return false;
            }
        }

        internal static void NotifyResult(
            PhysicalCashSpawnResultPayload result)
        {
            if (result == null)
                return;

            try
            {
                ResultReceived?.Invoke(result);

                if (result.Success)
                {
                    MelonLogger.Msg(
                        "[FusionDarkRP Cash] "
                        + result.Message
                    );
                }
                else
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Cash] "
                        + result.Message
                    );
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Cash] Notification impossible : "
                    + exception
                );
            }
        }
    }
}
