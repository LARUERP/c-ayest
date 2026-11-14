using System;
using System.Text.Json;
using FusionDarkRP.Economy;
using LabFusion.Network;
using LabFusion.Network.Serialization;
using LabFusion.Player;
using LabFusion.SDK.Modules;
using MelonLoader;

namespace FusionDarkRP.Integration
{
    public sealed class PhysicalCashPickupPacket : INetSerializable
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

    public sealed class PhysicalCashPickupHandler : ModuleMessageHandler
    {
        private const int MaximumPacketSize = 1024;

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
            if (!NetworkInfo.HasServer || !NetworkInfo.IsHost)
                return;

            if (!message.IsServerHandled || !message.Sender.HasValue)
                return;

            try
            {
                PhysicalCashPickupPacket packet =
                    message.ReadData<PhysicalCashPickupPacket>();

                if (packet == null
                    || packet.Payload == null
                    || packet.Payload.Length == 0
                    || packet.Payload.Length > MaximumPacketSize)
                {
                    return;
                }

                PhysicalCashPickupPayload data =
                    JsonSerializer.Deserialize<PhysicalCashPickupPayload>(
                        packet.Payload);

                if (data == null
                    || !PhysicalCashRequestId.IsValid(data.RequestId)
                    || !IsValidCashId(data.CashId))
                {
                    return;
                }

                PlayerID player =
                    PlatformIds.GetPlayerID(message.Sender.Value);

                if (player == null || !player.IsValid)
                    return;

                string error;

                bool success =
                    PhysicalCashPickupService.TryPickup(
                        player,
                        data.RequestId,
                        data.CashId,
                        out error);

                if (!success)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Cash] Récupération refusée pour "
                        + message.Sender.Value
                        + " : "
                        + error);
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Cash] Erreur traitement réseau : "
                    + exception);
            }
        }

        private static bool IsValidCashId(string cashId)
        {
            return !string.IsNullOrWhiteSpace(cashId)
                && Guid.TryParseExact(cashId, "N", out Guid parsed)
                && parsed != Guid.Empty;
        }
    }

    public sealed class PhysicalCashPickupPayload
    {
        public string RequestId { get; set; }
        public string CashId { get; set; }
    }

    public static class PhysicalCashPickupNetwork
    {
        private const int MaximumPacketSize = 1024;

        public static bool RequestPickup(string cashId)
        {
            if (string.IsNullOrWhiteSpace(cashId)
                || !Guid.TryParseExact(
                    cashId,
                    "N",
                    out Guid parsedCashId)
                || parsedCashId == Guid.Empty)
            {
                return false;
            }

            if (!NetworkInfo.HasServer)
                return false;

            string requestId = PhysicalCashRequestId.Create();
            string normalizedCashId = parsedCashId.ToString("N");

            try
            {
                if (NetworkInfo.IsHost)
                {
                    PlayerID localPlayer = PlayerIDManager.LocalID;

                    if (localPlayer == null || !localPlayer.IsValid)
                        return false;

                    bool success =
                        PhysicalCashPickupService.TryPickup(
                            localPlayer,
                            requestId,
                            normalizedCashId,
                            out string error);

                    if (!success)
                    {
                        MelonLogger.Warning(
                            "[FusionDarkRP Cash] Récupération locale refusée : "
                            + error);
                    }

                    return success;
                }

                PhysicalCashPickupPayload data =
                    new PhysicalCashPickupPayload
                    {
                        RequestId = requestId,
                        CashId = normalizedCashId
                    };

                PhysicalCashPickupPacket packet =
                    new PhysicalCashPickupPacket
                    {
                        Payload = JsonSerializer.SerializeToUtf8Bytes(data)
                    };

                if (packet.Payload == null
                    || packet.Payload.Length == 0
                    || packet.Payload.Length > MaximumPacketSize)
                {
                    return false;
                }

                MessageRelay.RelayModule<
                    PhysicalCashPickupHandler,
                    PhysicalCashPickupPacket>(
                    packet,
                    CommonMessageRoutes.ReliableToServer);

                return true;
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Cash] Envoi de récupération impossible : "
                    + exception);

                return false;
            }
        }
    }
}
