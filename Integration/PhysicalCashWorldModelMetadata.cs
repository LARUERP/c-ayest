using System;
using System.Text.Json;
using FusionDarkRP.Economy;
using LabFusion.Network;
using LabFusion.Network.Serialization;
using LabFusion.SDK.Modules;
using MelonLoader;

namespace FusionDarkRP.Integration
{
    public sealed class PhysicalCashMetadataPacket : INetSerializable
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

    public sealed class PhysicalCashMetadataPayload
    {
        public string ObjectId { get; set; }
        public string CashId { get; set; }
    }

    public sealed class PhysicalCashMetadataHandler : ModuleMessageHandler
    {
        private const int MaximumPacketSize = 512;

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
            if (!NetworkInfo.HasServer
                || NetworkInfo.IsHost
                || message.IsServerHandled
                || message.Sender != 0)
            {
                return;
            }

            try
            {
                PhysicalCashMetadataPacket packet =
                    message.ReadData<PhysicalCashMetadataPacket>();

                if (packet == null
                    || packet.Payload == null
                    || packet.Payload.Length == 0
                    || packet.Payload.Length > MaximumPacketSize)
                {
                    return;
                }

                PhysicalCashMetadataPayload data =
                    JsonSerializer.Deserialize<PhysicalCashMetadataPayload>(
                        packet.Payload);

                if (data == null
                    || !IsGuid(data.ObjectId)
                    || !IsGuid(data.CashId))
                {
                    return;
                }

                string objectId = Guid.ParseExact(
                    data.ObjectId,
                    "N").ToString("N");

                string cashId = Guid.ParseExact(
                    data.CashId,
                    "N").ToString("N");

                if (!WorldModelRuntimeManager.TryGet(
                    objectId,
                    out UnityEngine.GameObject target)
                    || target == null)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Cash] Copie 3D introuvable pour les métadonnées : "
                        + objectId);

                    return;
                }

                target.name = "FusionDarkRP_Cash_Replica_" + cashId;
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Cash] Erreur métadonnées distantes : "
                    + exception);
            }
        }

        private static bool IsGuid(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && Guid.TryParseExact(value, "N", out Guid parsed)
                && parsed != Guid.Empty;
        }
    }

    public static class PhysicalCashWorldModelMetadata
    {
        private const int MaximumPacketSize = 512;

        public static bool Send(
            string objectId,
            string cashId,
            MessageRoute route)
        {
            if (!IsHost())
                return false;

            if (!IsGuid(objectId) || !IsGuid(cashId))
                return false;

            try
            {
                PhysicalCashMetadataPayload data =
                    new PhysicalCashMetadataPayload
                    {
                        ObjectId = Guid.ParseExact(objectId, "N").ToString("N"),
                        CashId = Guid.ParseExact(cashId, "N").ToString("N")
                    };

                PhysicalCashMetadataPacket packet =
                    new PhysicalCashMetadataPacket
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
                    PhysicalCashMetadataHandler,
                    PhysicalCashMetadataPacket>(
                    packet,
                    route);

                return true;
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Cash] Envoi métadonnées impossible : "
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

        private static bool IsHost()
        {
            try
            {
                return NetworkInfo.HasServer && NetworkInfo.IsHost;
            }
            catch
            {
                return false;
            }
        }
    }
}
