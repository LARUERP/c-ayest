using System;
using System.Text.Json;
using FusionDarkRP.Economy;
using LabFusion.Network;
using LabFusion.Player;
using LabFusion.SDK.Modules;
using LabFusion.Utilities;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Synchronise les objets 3D déjà présents lorsqu'un joueur rejoint
    /// une partie en cours.
    /// </summary>
    public static class WorldModelLateJoinSync
    {
        private static readonly object Sync = new object();

        private static bool initialized;

        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                MaxDepth = 8
            };

        public static bool IsInitialized
        {
            get
            {
                lock (Sync)
                    return initialized;
            }
        }

        public static void Initialize()
        {
            lock (Sync)
            {
                if (initialized)
                    return;

                try
                {
                    MultiplayerHooking.OnPlayerJoined += OnPlayerJoined;

                    initialized = true;

                    MelonLogger.Msg(
                        "[FusionDarkRP World] Synchronisation des nouveaux joueurs initialisee."
                    );
                }
                catch (Exception exception)
                {
                    initialized = false;

                    MelonLogger.Error(
                        "[FusionDarkRP World] Initialisation synchronisation : "
                        + exception
                    );
                }
            }
        }

        public static void Shutdown()
        {
            lock (Sync)
            {
                if (!initialized)
                    return;

                try
                {
                    MultiplayerHooking.OnPlayerJoined -= OnPlayerJoined;
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP World] Nettoyage synchronisation : "
                        + exception.Message
                    );
                }

                initialized = false;
            }
        }

        private static void OnPlayerJoined(PlayerID playerId)
        {
            if (!IsHost())
                return;

            if (playerId == null ||
                !playerId.IsValid ||
                playerId.IsMe)
            {
                return;
            }

            try
            {
                string[] objectIds =
                    WorldModelRuntimeManager.GetSpawnedObjectIds();

                if (objectIds == null || objectIds.Length == 0)
                {
                    MelonLogger.Msg(
                        "[FusionDarkRP World] Aucun objet a synchroniser pour le nouveau joueur."
                    );

                    return;
                }

                int sent = 0;
                int failed = 0;

                foreach (string objectId in objectIds)
                {
                    if (string.IsNullOrWhiteSpace(objectId))
                        continue;

                    if (TrySendObjectToPlayer(playerId, objectId))
                        sent++;
                    else
                        failed++;
                }

                MelonLogger.Msg(
                    "[FusionDarkRP World] Synchronisation nouveau joueur : "
                    + sent
                    + " objet(s) envoye(s), "
                    + failed
                    + " echec(s)."
                );
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP World] Erreur synchronisation nouveau joueur : "
                    + exception
                );
            }
        }

        private static bool TrySendObjectToPlayer(
            PlayerID playerId,
            string objectId)
        {
            if (playerId == null ||
                !playerId.IsValid ||
                playerId.IsMe)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(objectId) ||
                objectId.Length > 64)
            {
                return false;
            }

            try
            {
                if (!Guid.TryParseExact(objectId, "N", out Guid parsedId))
                    return false;

                string normalizedObjectId = parsedId.ToString("N");

                // Porteurs saisissables : renvoyés par CarrierObjects.
                if (CarrierObjects.IsCarrier(normalizedObjectId))
                    return true;

                if (!WorldModelRuntimeManager.TryGet(
                    normalizedObjectId,
                    out GameObject target) ||
                    target == null)
                {
                    return false;
                }

                if (!WorldModelRuntimeManager.TryGetModelId(
                    normalizedObjectId,
                    out string modelId) ||
                    string.IsNullOrWhiteSpace(modelId))
                {
                    return false;
                }

                if (!RuntimeObjWorldModelCatalog.TryGet(
                    modelId,
                    out WorldModelDefinition definition) ||
                    definition == null)
                {
                    return false;
                }

                Transform objectTransform = target.transform;

                if (objectTransform == null)
                    return false;

                Vector3 position = objectTransform.position;
                Quaternion rotation = objectTransform.rotation;

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

                rotation = rotation.normalized;

                WorldModelSpawnPayload payload =
                    new WorldModelSpawnPayload
                    {
                        ObjectId = normalizedObjectId,
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
                            payload,
                            JsonOptions)
                    };

                if (packet.Payload == null ||
                    packet.Payload.Length == 0 ||
                    packet.Payload.Length > 4000)
                {
                    return false;
                }

                MessageRoute route = new MessageRoute(
                    playerId.SmallID,
                    NetworkChannel.Reliable
                );

                // 1. Envoyer d'abord la création de l'objet 3D.
                MessageRelay.RelayModule<
                    WorldModelSpawnHandler,
                    WorldModelNetworkPacket>(
                    packet,
                    route
                );

                // 2. Si cet objet est un billet, transmettre son
                // identifiant métier au même joueur.
                if (PhysicalCashGrabDetector.TryExtractCashId(
                    target.name,
                    out string cashId))
                {
                    bool metadataSent =
                        PhysicalCashWorldModelMetadata.Send(
                            normalizedObjectId,
                            cashId,
                            route);

                    if (!metadataSent)
                    {
                        MelonLogger.Warning(
                            "[FusionDarkRP Cash] Métadonnées non envoyées "
                            + "au nouveau joueur : "
                            + cashId);

                        return false;
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP World] Envoi objet au nouveau joueur impossible : "
                    + objectId
                    + " | "
                    + exception.Message
                );

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
