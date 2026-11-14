using System;
using System.Collections.Generic;
using System.Text.Json;
using FusionDarkRP.Economy;
using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.Interaction;
using LabFusion.Data;
using LabFusion.Entities;
using LabFusion.Marrow.Extenders;
using LabFusion.Network;
using LabFusion.Network.Serialization;
using LabFusion.Player;
using LabFusion.RPC;
using LabFusion.SDK.Modules;
using LabFusion.Utilities;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    public sealed class CarrierObjectPacket : INetSerializable
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

    public sealed class CarrierObjectPayload
    {
        public ushort EntityId { get; set; }
        public string ObjectId { get; set; }
        public string ModelId { get; set; }
        public string Name { get; set; }
    }

    public sealed class CarrierObjectHandler : ModuleMessageHandler
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
                CarrierObjectPacket packet =
                    message.ReadData<CarrierObjectPacket>();

                if (packet == null
                    || packet.Payload == null
                    || packet.Payload.Length == 0
                    || packet.Payload.Length > MaximumPacketSize)
                {
                    return;
                }

                CarrierObjectPayload data =
                    JsonSerializer.Deserialize<CarrierObjectPayload>(
                        packet.Payload);

                CarrierObjects.RegisterReplica(data);
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Carrier] Erreur réception : "
                    + exception);
            }
        }
    }

    /// <summary>
    /// Objets réellement saisissables : un vrai objet BONELAB
    /// (SpawnableCrate chargé par le jeu, par défaut l'Eder 22) est créé
    /// par LabFusion (synchronisation, propriété et saisie natives), puis
    /// habillé du modèle OBJ du catalogue chez tous les joueurs.
    /// Si aucun porteur n'est disponible, l'ancien objet simple est conservé.
    /// </summary>
    public static class CarrierObjects
    {
        private const string DefaultCarrierTitle = "Eder22";

        private sealed class CarrierEntry
        {
            public string ObjectId;
            public string ModelId;
            public string Name;
            public ushort EntityId;
            public GameObject Root;
            public NetworkEntity Entity;
            public bool Ready;
            public bool Cancelled;
        }

        private static readonly object Sync = new object();

        private static readonly Dictionary<string, CarrierEntry> Entries =
            new Dictionary<string, CarrierEntry>(StringComparer.Ordinal);

        private static readonly HashSet<ushort> ReplicaEntities =
            new HashSet<ushort>();

        private static MelonPreferences_Entry<bool> enabledEntry;
        private static MelonPreferences_Entry<string> carrierEntry;

        private static bool initialized;
        private static bool warnedUnavailable;

        // ------------------------------------------------------------
        // Cycle de vie
        // ------------------------------------------------------------

        public static bool Initialize()
        {
            lock (Sync)
            {
                if (initialized)
                    return true;

                try
                {
                    MelonPreferences_Category category =
                        MelonPreferences.CreateCategory("FusionDarkRP");

                    enabledEntry = category.CreateEntry<bool>(
                        "GrabbableObjects", true);

                    carrierEntry = category.CreateEntry<string>(
                        "GrabbableCarrierTitle", DefaultCarrierTitle);

                    MultiplayerHooking.OnPlayerJoined += OnPlayerJoined;

                    initialized = true;

                    MelonLogger.Msg(
                        "[FusionDarkRP Carrier] Objets saisissables prêts "
                        + "(porteur : " + carrierEntry.Value + ").");

                    return true;
                }
                catch (Exception exception)
                {
                    initialized = false;

                    MelonLogger.Error(
                        "[FusionDarkRP Carrier] Initialisation impossible : "
                        + exception);

                    return false;
                }
            }
        }

        public static void Shutdown()
        {
            lock (Sync)
            {
                if (initialized)
                {
                    try
                    {
                        MultiplayerHooking.OnPlayerJoined -= OnPlayerJoined;
                    }
                    catch (Exception exception)
                    {
                        MelonLogger.Warning(
                            "[FusionDarkRP Carrier] Nettoyage : "
                            + exception.Message);
                    }
                }

                initialized = false;
                Entries.Clear();
                ReplicaEntities.Clear();
            }

            CarrierVisualAdapter.Cleanup();
        }

        // ------------------------------------------------------------
        // Hôte : création
        // ------------------------------------------------------------

        public static bool IsCarrier(string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId))
                return false;

            lock (Sync)
                return Entries.ContainsKey(objectId);
        }

        public static bool IsReady(string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId))
                return false;

            lock (Sync)
            {
                CarrierEntry entry;

                return Entries.TryGetValue(objectId, out entry) &&
                       entry != null &&
                       entry.Ready;
            }
        }

        /// <summary>
        /// Tente de remplacer l'objet simple déjà enregistré par un vrai
        /// objet saisissable. Retourne false si le mode est indisponible
        /// (l'objet simple est alors conservé et synchronisé comme avant).
        /// </summary>
        public static bool TryBegin(
            string objectId,
            string modelId,
            Vector3 position,
            Quaternion rotation,
            RuntimeObjWorldModelSpawner spawner)
        {
            try
            {
                if (!NetworkInfo.HasServer || !NetworkInfo.IsHost)
                    return false;

                if (string.IsNullOrWhiteSpace(objectId) ||
                    string.IsNullOrWhiteSpace(modelId) ||
                    spawner == null)
                {
                    return false;
                }

                Initialize();

                if (enabledEntry == null || !enabledEntry.Value)
                    return false;

                WorldModelDefinition definition;

                if (!RuntimeObjWorldModelCatalog.TryGet(
                        modelId, out definition) ||
                    definition == null ||
                    !definition.EnablePhysics ||
                    string.Equals(
                        modelId, "BankCard",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                Il2CppSLZ.Marrow.Data.Spawnable spawnable;
                string barcode;
                string error;

                if (!BankCardSupportCrateResolver.TryResolve(
                    carrierEntry.Value,
                    out spawnable,
                    out barcode,
                    out error))
                {
                    if (!warnedUnavailable)
                    {
                        warnedUnavailable = true;

                        MelonLogger.Warning(
                            "[FusionDarkRP Carrier] Porteur indisponible, "
                            + "objets non saisissables : " + error);
                    }

                    return false;
                }

                CarrierEntry entry = new CarrierEntry
                {
                    ObjectId = objectId,
                    ModelId = modelId
                };

                lock (Sync)
                    Entries[objectId] = entry;

                float scale = definition.Scale;

                NetworkAssetSpawner.Spawn(
                    new NetworkAssetSpawner.SpawnRequestInfo
                    {
                        Spawnable = spawnable,
                        Position = position,
                        Rotation = rotation,
                        SpawnEffect = false,
                        SpawnSource = EntitySource.Player,
                        SpawnCallback = info =>
                            OnSpawned(entry, info, scale, spawner)
                    });

                return true;
            }
            catch (Exception exception)
            {
                lock (Sync)
                    Entries.Remove(objectId);

                MelonLogger.Error(
                    "[FusionDarkRP Carrier] Création impossible : "
                    + exception);

                return false;
            }
        }

        /// <summary>
        /// Nom définitif de l'objet (ex. FusionDarkRP_Cash_...), utilisé
        /// par la détection de saisie de l'argent physique.
        /// </summary>
        public static void ApplyName(string objectId, string name)
        {
            if (string.IsNullOrWhiteSpace(objectId) ||
                string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            CarrierEntry entry;

            lock (Sync)
            {
                if (!Entries.TryGetValue(objectId, out entry))
                    return;

                entry.Name = name;
            }

            if (entry.Ready && entry.Root != null)
            {
                RenameAll(entry.Root, entry.Entity, name);
                Broadcast(entry);
            }
        }

        private static void OnSpawned(
            CarrierEntry entry,
            NetworkAssetSpawner.SpawnCallbackInfo info,
            float scale,
            RuntimeObjWorldModelSpawner spawner)
        {
            NetworkEntity entity = info.Entity;

            try
            {
                if (entry == null)
                    return;

                if (entry.Cancelled)
                {
                    Despawn(entity);
                    return;
                }

                if (info.Spawned == null || entity == null)
                    throw new InvalidOperationException(
                        "Le porteur n'a pas été créé.");

                GameObject root = ResolveRoot(entity, info.Spawned);

                string error;

                if (!CarrierVisualAdapter.TryApply(
                    root, entry.ModelId, scale, out error))
                {
                    throw new InvalidOperationException(error);
                }

                string name = entry.Name;

                if (string.IsNullOrWhiteSpace(name))
                {
                    GameObject placeholder;

                    if (spawner.TryGet(entry.ObjectId, out placeholder) &&
                        placeholder != null)
                    {
                        name = placeholder.name;
                    }
                }

                if (string.IsNullOrWhiteSpace(name))
                    name = "FusionDarkRP_" + entry.ModelId;

                entry.Name = name;
                entry.Root = root;
                entry.Entity = entity;
                entry.EntityId = entity.ID;

                RenameAll(root, entity, name);

                if (!spawner.ReplaceObject(entry.ObjectId, root))
                    throw new InvalidOperationException(
                        "Objet d'origine introuvable dans le registre.");

                entry.Ready = true;

                Broadcast(entry);

                MelonLogger.Msg(
                    "[FusionDarkRP Carrier] Objet saisissable prêt : "
                    + entry.ModelId + " | entité " + entry.EntityId);
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Carrier] Échec, repli sur objet simple : "
                    + exception.Message);

                Despawn(entity);

                if (entry != null)
                    Fallback(entry, spawner);
            }
        }

        private static void Fallback(
            CarrierEntry entry,
            RuntimeObjWorldModelSpawner spawner)
        {
            lock (Sync)
                Entries.Remove(entry.ObjectId);

            GameObject placeholder;

            if (spawner != null &&
                spawner.TryGet(entry.ObjectId, out placeholder) &&
                placeholder != null)
            {
                // L'objet simple devient la source de vérité :
                // on le diffuse comme avant aux clients.
                WorldModelNetwork.BroadcastSpawn(
                    entry.ObjectId,
                    entry.ModelId,
                    placeholder.transform.position,
                    placeholder.transform.rotation);
            }
        }

        private static GameObject ResolveRoot(
            NetworkEntity entity, GameObject spawned)
        {
            try
            {
                WeaponSlotExtender extender =
                    entity.GetExtender<WeaponSlotExtender>();

                if (extender != null &&
                    extender.Component != null &&
                    extender.Component.gameObject != null)
                {
                    return extender.Component.gameObject;
                }
            }
            catch
            {
                // Repli sur l'objet créé.
            }

            return spawned;
        }

        private static void RenameAll(
            GameObject root, NetworkEntity entity, string name)
        {
            if (root != null)
                root.name = name;

            try
            {
                WeaponSlotExtender extender =
                    entity?.GetExtender<WeaponSlotExtender>();

                if (extender != null && extender.Component != null)
                {
                    extender.Component.gameObject.name = name;

                    if (extender.Component.interactableHost != null)
                    {
                        extender.Component.interactableHost
                            .gameObject.name = name;
                    }
                }
            }
            catch
            {
                // La racine est déjà renommée.
            }
        }

        // ------------------------------------------------------------
        // Hôte : suppression
        // ------------------------------------------------------------

        /// <summary>
        /// Despawn réseau de l'objet saisissable. Retourne true si
        /// l'objet était un porteur (le registre doit alors le retirer
        /// sans détruire lui-même les ressources du jeu).
        /// </summary>
        public static bool Release(string objectId)
        {
            CarrierEntry entry;

            lock (Sync)
            {
                if (string.IsNullOrWhiteSpace(objectId) ||
                    !Entries.TryGetValue(objectId, out entry))
                {
                    return false;
                }

                Entries.Remove(objectId);
            }

            entry.Cancelled = true;

            MelonLogger.Msg(
                "[FusionDarkRP Carrier] Retrait de l'objet "
                + objectId
                + " | entité "
                + entry.EntityId
                + " | enregistrée : "
                + (entry.Entity != null && entry.Entity.IsRegistered));

            Despawn(entry.Entity);

            // Filet de sécurité : si le despawn LabFusion n'a rien retiré
            // (objet encore en main, entité non enregistrée...), l'objet
            // est supprimé de force un instant plus tard.
            if (entry.Root != null)
            {
                lock (Sync)
                {
                    PendingRemovals.Add(new PendingRemoval
                    {
                        Root = entry.Root,
                        At = Time.unscaledTime + 1.2f
                    });
                }
            }

            return true;
        }

        private sealed class PendingRemoval
        {
            public GameObject Root;
            public float At;
        }

        private static readonly List<PendingRemoval> PendingRemovals =
            new List<PendingRemoval>();

        /// <summary>À appeler à chaque image (hôte).</summary>
        public static void TickRemovals()
        {
            if (PendingRemovals.Count == 0)
                return;

            float now = Time.unscaledTime;
            List<GameObject> toRemove = null;

            lock (Sync)
            {
                for (int i = PendingRemovals.Count - 1; i >= 0; i--)
                {
                    PendingRemoval pending = PendingRemovals[i];

                    if (pending.Root == null)
                    {
                        PendingRemovals.RemoveAt(i);
                        continue;
                    }

                    if (now < pending.At)
                        continue;

                    if (toRemove == null)
                        toRemove = new List<GameObject>();

                    toRemove.Add(pending.Root);
                    PendingRemovals.RemoveAt(i);
                }
            }

            if (toRemove == null)
                return;

            foreach (GameObject root in toRemove)
            {
                try
                {
                    if (root == null)
                        continue;

                    MelonLogger.Warning(
                        "[FusionDarkRP Carrier] Objet encore présent après "
                        + "le despawn : suppression forcée.");

                    Il2CppSLZ.Marrow.Pool.Poolee poolee =
                        root.GetComponentInParent<
                            Il2CppSLZ.Marrow.Pool.Poolee>();

                    if (poolee != null)
                        poolee.Despawn();

                    if (root != null)
                        UnityEngine.Object.Destroy(root);
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Carrier] Suppression forcée : "
                        + exception.Message);
                }
            }
        }

        private static void Despawn(NetworkEntity entity)
        {
            if (entity == null ||
                !entity.IsRegistered ||
                !NetworkInfo.HasServer ||
                !NetworkInfo.IsHost)
            {
                return;
            }

            try
            {
                NetworkAssetSpawner.Despawn(
                    new NetworkAssetSpawner.DespawnRequestInfo
                    {
                        EntityID = entity.ID,
                        DespawnEffect = false
                    });
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Carrier] Despawn impossible : "
                    + exception);
            }
        }

        // ------------------------------------------------------------
        // Réseau
        // ------------------------------------------------------------

        private static CarrierObjectPayload ToPayload(CarrierEntry entry)
        {
            return new CarrierObjectPayload
            {
                EntityId = entry.EntityId,
                ObjectId = entry.ObjectId,
                ModelId = entry.ModelId,
                Name = entry.Name
            };
        }

        private static void Broadcast(CarrierEntry entry)
        {
            if (entry == null || !entry.Ready ||
                !NetworkInfo.HasServer || !NetworkInfo.IsHost)
            {
                return;
            }

            try
            {
                MessageRelay.RelayModule<
                    CarrierObjectHandler,
                    CarrierObjectPacket>(
                    new CarrierObjectPacket
                    {
                        Payload = JsonSerializer.SerializeToUtf8Bytes(
                            ToPayload(entry))
                    },
                    CommonMessageRoutes.ReliableToClients);
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Carrier] Diffusion impossible : "
                    + exception);
            }
        }

        private static void OnPlayerJoined(PlayerID playerId)
        {
            if (!NetworkInfo.HasServer ||
                !NetworkInfo.IsHost ||
                playerId == null ||
                !playerId.IsValid ||
                playerId.IsMe)
            {
                return;
            }

            List<CarrierEntry> snapshot;

            lock (Sync)
            {
                snapshot = new List<CarrierEntry>();

                foreach (CarrierEntry entry in Entries.Values)
                {
                    if (entry != null && entry.Ready)
                        snapshot.Add(entry);
                }
            }

            foreach (CarrierEntry entry in snapshot)
            {
                try
                {
                    MessageRelay.RelayModule<
                        CarrierObjectHandler,
                        CarrierObjectPacket>(
                        new CarrierObjectPacket
                        {
                            Payload = JsonSerializer.SerializeToUtf8Bytes(
                                ToPayload(entry))
                        },
                        new MessageRoute(
                            playerId.SmallID,
                            NetworkChannel.Reliable));
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Carrier] Envoi au nouveau joueur : "
                        + exception.Message);
                }
            }
        }

        // ------------------------------------------------------------
        // Client : application du visuel
        // ------------------------------------------------------------

        internal static void RegisterReplica(CarrierObjectPayload data)
        {
            if (data == null ||
                data.EntityId == 0 ||
                string.IsNullOrWhiteSpace(data.ModelId) ||
                data.ModelId.Length > 64)
            {
                return;
            }

            WorldModelDefinition definition;

            if (!RuntimeObjWorldModelCatalog.TryGet(
                    data.ModelId, out definition) ||
                definition == null)
            {
                return;
            }

            string name = string.IsNullOrWhiteSpace(data.Name) ||
                          data.Name.Length > 128
                ? "FusionDarkRP_" + definition.Id
                : data.Name;

            ushort entityId = data.EntityId;
            float scale = definition.Scale;
            string modelId = definition.Id;

            NetworkEntityManager.HookEntityRegistered(
                entityId,
                entity =>
                {
                    if (entity == null)
                        return;

                    try
                    {
                        GameObject root = ResolveRoot(entity, null);

                        if (root == null)
                            return;

                        string error;

                        if (!CarrierVisualAdapter.TryApply(
                            root, modelId, scale, out error))
                        {
                            MelonLogger.Warning(
                                "[FusionDarkRP Carrier] Visuel distant "
                                + "indisponible : " + error);
                            return;
                        }

                        RenameAll(root, entity, name);

                        lock (Sync)
                            ReplicaEntities.Add(entityId);
                    }
                    catch (Exception exception)
                    {
                        MelonLogger.Warning(
                            "[FusionDarkRP Carrier] Application distante : "
                            + exception.Message);
                    }
                });
        }
    }
}
