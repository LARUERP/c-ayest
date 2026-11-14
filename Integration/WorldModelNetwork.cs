
using System;
using System.Text.Json;
using FusionDarkRP.Economy;
using LabFusion.Network;
using LabFusion.Network.Serialization;
using LabFusion.SDK.Modules;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    public sealed class WorldModelNetworkPacket : INetSerializable
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

    public sealed class WorldModelSpawnPayload
    {
        public string ObjectId { get; set; }
        public string ModelId { get; set; }

        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }

        public float RotationX { get; set; }
        public float RotationY { get; set; }
        public float RotationZ { get; set; }
        public float RotationW { get; set; }
    }

    public sealed class WorldModelDestroyPayload
    {
        public string ObjectId { get; set; }
    }

    public sealed class WorldModelSpawnHandler : ModuleMessageHandler
    {
        private const int MaxPacketSize = 4000;
        private const int MaxObjectIdLength = 64;
        private const int MaxModelIdLength = 64;

        public override ExpectedReceiverType ExpectedReceiver =>
            ExpectedReceiverType.ClientsOnly;

        protected override bool OnPreRelayMessage(ReceivedMessage message)
        {
            return message.Bytes != null
                && message.Bytes.Length > 0
                && message.Bytes.Length <= MaxPacketSize + 4;
        }

        protected override void OnHandleMessage(ReceivedMessage message)
        {
            if (NetworkInfo.IsHost ||
                message.IsServerHandled ||
                message.Sender != 0 ||
                !NetworkInfo.HasServer)
            {
                return;
            }

            try
            {
                WorldModelNetworkPacket packet =
                    message.ReadData<WorldModelNetworkPacket>();

                if (packet == null ||
                    packet.Payload == null ||
                    packet.Payload.Length == 0 ||
                    packet.Payload.Length > MaxPacketSize)
                {
                    return;
                }

                WorldModelSpawnPayload data =
                    JsonSerializer.Deserialize<WorldModelSpawnPayload>(
                        packet.Payload,
                        WorldModelNetwork.JsonOptions);

                if (!IsValidSpawnPayload(data))
                    return;

                if (!RuntimeObjWorldModelCatalog.TryGet(
                    data.ModelId,
                    out WorldModelDefinition definition) ||
                    definition == null)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP World] Modele reseau inconnu : "
                        + data.ModelId);

                    return;
                }

                Vector3 position = new Vector3(
                    data.PositionX,
                    data.PositionY,
                    data.PositionZ);

                Quaternion rotation = new Quaternion(
                    data.RotationX,
                    data.RotationY,
                    data.RotationZ,
                    data.RotationW);

                if (!IsFinite(position) || !IsFinite(rotation))
                    return;

                float rotationLength =
                    rotation.x * rotation.x +
                    rotation.y * rotation.y +
                    rotation.z * rotation.z +
                    rotation.w * rotation.w;

                if (!IsFinite(rotationLength) ||
                    rotationLength < 0.0001f ||
                    rotationLength > 100f)
                {
                    return;
                }

                rotation = rotation.normalized;

                string error;

                bool success =
                    WorldModelRuntimeManager.TrySpawnReplica(
                        data.ObjectId,
                        definition.Id,
                        position,
                        rotation,
                        out error);

                if (!success)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP World] Creation distante impossible : "
                        + error);
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP World] Erreur reception creation : "
                    + exception);
            }
        }

        private static bool IsValidSpawnPayload(
            WorldModelSpawnPayload data)
        {
            if (data == null ||
                string.IsNullOrWhiteSpace(data.ObjectId) ||
                string.IsNullOrWhiteSpace(data.ModelId))
            {
                return false;
            }

            if (data.ObjectId.Length > MaxObjectIdLength ||
                data.ModelId.Length > MaxModelIdLength)
            {
                return false;
            }

            Guid parsedId;

            if (!Guid.TryParseExact(data.ObjectId, "N", out parsedId))
                return false;

            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x)
                && IsFinite(value.y)
                && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x)
                && IsFinite(value.y)
                && IsFinite(value.z)
                && IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value)
                && !float.IsInfinity(value);
        }
    }

    public sealed class WorldModelDestroyHandler : ModuleMessageHandler
    {
        private const int MaxPacketSize = 1024;
        private const int MaxObjectIdLength = 64;

        public override ExpectedReceiverType ExpectedReceiver =>
            ExpectedReceiverType.ClientsOnly;

        protected override bool OnPreRelayMessage(ReceivedMessage message)
        {
            return message.Bytes != null
                && message.Bytes.Length > 0
                && message.Bytes.Length <= MaxPacketSize + 4;
        }

        protected override void OnHandleMessage(ReceivedMessage message)
        {
            if (NetworkInfo.IsHost ||
                message.IsServerHandled ||
                message.Sender != 0 ||
                !NetworkInfo.HasServer)
            {
                return;
            }

            try
            {
                WorldModelNetworkPacket packet =
                    message.ReadData<WorldModelNetworkPacket>();

                if (packet == null ||
                    packet.Payload == null ||
                    packet.Payload.Length == 0 ||
                    packet.Payload.Length > MaxPacketSize)
                {
                    return;
                }

                WorldModelDestroyPayload data =
                    JsonSerializer.Deserialize<WorldModelDestroyPayload>(
                        packet.Payload,
                        WorldModelNetwork.JsonOptions);

                if (data == null ||
                    string.IsNullOrWhiteSpace(data.ObjectId) ||
                    data.ObjectId.Length > MaxObjectIdLength)
                {
                    return;
                }

                Guid parsedId;

                if (!Guid.TryParseExact(data.ObjectId, "N", out parsedId))
                    return;

                string error;

                bool success =
                    WorldModelRuntimeManager.TryDestroyReplica(
                        parsedId.ToString("N"),
                        out error);

                if (!success)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP World] Suppression distante impossible : "
                        + error);
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP World] Erreur reception suppression : "
                    + exception);
            }
        }
    }

    public static class WorldModelNetwork
    {
        internal static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                MaxDepth = 8
            };

        public static bool BroadcastSpawn(
            string objectId,
            string modelId,
            Vector3 position,
            Quaternion rotation)
        {
            if (!IsHost())
                return false;

            if (string.IsNullOrWhiteSpace(objectId) ||
                string.IsNullOrWhiteSpace(modelId))
            {
                return false;
            }

            Guid parsedId;

            if (!Guid.TryParseExact(objectId, "N", out parsedId))
                return false;

            // Les objets saisissables sont synchronisés par LabFusion
            // (entité réseau) : pas de copie locale chez les clients.
            if (CarrierObjects.IsCarrier(objectId))
                return true;

            if (!RuntimeObjWorldModelCatalog.TryGet(
                modelId,
                out WorldModelDefinition definition) ||
                definition == null)
            {
                return false;
            }

            if (!IsFinite(position) || !IsFinite(rotation))
                return false;

            float rotationLength =
                rotation.x * rotation.x +
                rotation.y * rotation.y +
                rotation.z * rotation.z +
                rotation.w * rotation.w;

            if (!IsFinite(rotationLength) ||
                rotationLength < 0.0001f ||
                rotationLength > 100f)
            {
                return false;
            }

            try
            {
                rotation = rotation.normalized;

                WorldModelSpawnPayload data =
                    new WorldModelSpawnPayload
                    {
                        ObjectId = parsedId.ToString("N"),
                        ModelId = definition.Id,

                        PositionX = position.x,
                        PositionY = position.y,
                        PositionZ = position.z,

                        RotationX = rotation.x,
                        RotationY = rotation.y,
                        RotationZ = rotation.z,
                        RotationW = rotation.w
                    };

                WorldModelNetworkPacket packet =
                    new WorldModelNetworkPacket
                    {
                        Payload = JsonSerializer.SerializeToUtf8Bytes(
                            data,
                            JsonOptions)
                    };

                if (packet.Payload == null ||
                    packet.Payload.Length == 0 ||
                    packet.Payload.Length > 4000)
                {
                    return false;
                }

                MessageRelay.RelayModule<
                    WorldModelSpawnHandler,
                    WorldModelNetworkPacket>(
                    packet,
                    CommonMessageRoutes.ReliableToClients);

                return true;
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP World] Envoi creation impossible : "
                    + exception);

                return false;
            }
        }

        public static bool BroadcastDestroy(string objectId)
        {
            if (!IsHost() ||
                string.IsNullOrWhiteSpace(objectId))
            {
                return false;
            }

            Guid parsedId;

            if (!Guid.TryParseExact(objectId, "N", out parsedId))
                return false;

            try
            {
                WorldModelDestroyPayload data =
                    new WorldModelDestroyPayload
                    {
                        ObjectId = parsedId.ToString("N")
                    };

                WorldModelNetworkPacket packet =
                    new WorldModelNetworkPacket
                    {
                        Payload = JsonSerializer.SerializeToUtf8Bytes(
                            data,
                            JsonOptions)
                    };

                if (packet.Payload == null ||
                    packet.Payload.Length == 0 ||
                    packet.Payload.Length > 1024)
                {
                    return false;
                }

                MessageRelay.RelayModule<
                    WorldModelDestroyHandler,
                    WorldModelNetworkPacket>(
                    packet,
                    CommonMessageRoutes.ReliableToClients);

                return true;
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP World] Envoi suppression impossible : "
                    + exception);

                return false;
            }
        }

        private static bool IsHost()
        {
            try
            {
                return NetworkInfo.HasServer &&
                    NetworkInfo.IsHost;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x)
                && IsFinite(value.y)
                && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x)
                && IsFinite(value.y)
                && IsFinite(value.z)
                && IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value)
                && !float.IsInfinity(value);
        }
    }
}
