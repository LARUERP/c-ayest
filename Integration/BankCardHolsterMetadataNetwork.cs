
using LabFusion.Utilities;
using System;
using System.Collections.Generic;
using Il2CppSLZ.Marrow;
using LabFusion.Entities;
using LabFusion.Marrow.Extenders;
using LabFusion.Network;
using LabFusion.Network.Serialization;
using LabFusion.Player;
using LabFusion.SDK.Modules;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    public sealed class BankCardHolsterMetadataData : INetSerializable
    {
        public ushort EntityId;
        public long CardIdPart1;
        public long CardIdPart2;

        public int? GetSize()
        {
            return 18;
        }

        public void Serialize(INetSerializer serializer)
        {
            serializer.SerializeValue(ref EntityId);
            serializer.SerializeValue(ref CardIdPart1);
            serializer.SerializeValue(ref CardIdPart2);
        }
    }

    public sealed class BankCardHolsterMetadataHandler
        : ModuleMessageHandler
    {
        public override ExpectedReceiverType ExpectedReceiver =>
            ExpectedReceiverType.ClientsOnly;

        protected override bool OnPreRelayMessage(
            ReceivedMessage message)
        {
            return message.Bytes != null &&
                   message.Bytes.Length == 18;
        }

        protected override void OnHandleMessage(
            ReceivedMessage message)
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
                BankCardHolsterMetadataData data =
                    message.ReadData<BankCardHolsterMetadataData>();

                if (data == null || data.EntityId == 0)
                    return;

                string cardId;

                if (!BankCardHolsterMetadataNetwork.TryDecodeCardId(
                    data.CardIdPart1,
                    data.CardIdPart2,
                    out cardId))
                {
                    return;
                }

                BankCardHolsterMetadataNetwork.RegisterReplica(
                    data.EntityId,
                    cardId
                );
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Card] Erreur reception metadata : "
                    + exception
                );
            }
        }
    }

    public static class BankCardHolsterMetadataNetwork
    {
        private sealed class CardMetadata
        {
            public ushort EntityId;
            public string CardId;
        }

        private static readonly object Sync = new object();

        private static readonly Dictionary<ushort, CardMetadata>
            Cards = new Dictionary<ushort, CardMetadata>();

        private static readonly Dictionary<int, string>
            ObjectCardIds = new Dictionary<int, string>();

        private static readonly Dictionary<ushort, int>
            EntityObjectIds = new Dictionary<ushort, int>();

        // Racines des supports de carte vivants (détection par position,
        // indépendante des colliders / déclencheurs / calques).
        private static readonly Dictionary<int, GameObject>
            ObjectRoots = new Dictionary<int, GameObject>();

        private static bool initialized;

        public static bool IsInitialized
        {
            get
            {
                lock (Sync)
                    return initialized;
            }
        }

        public static bool Initialize()
        {
            lock (Sync)
            {
                if (initialized)
                    return true;

                try
                {
                    MultiplayerHooking.OnPlayerJoined += OnPlayerJoined;
                    initialized = true;

                    MelonLogger.Msg(
                        "[FusionDarkRP Card] Synchronisation des cartes holster initialisee."
                    );

                    return true;
                }
                catch (Exception exception)
                {
                    initialized = false;

                    MelonLogger.Error(
                        "[FusionDarkRP Card] Initialisation reseau impossible : "
                        + exception
                    );

                    return false;
                }
            }
        }

        public static void Shutdown()
        {
            lock (Sync)
            {
                if (!initialized &&
                    Cards.Count == 0 &&
                    ObjectCardIds.Count == 0 &&
                    EntityObjectIds.Count == 0)
                {
                    return;
                }

                if (initialized)
                {
                    try
                    {
                        MultiplayerHooking.OnPlayerJoined -= OnPlayerJoined;
                    }
                    catch (Exception exception)
                    {
                        MelonLogger.Warning(
                            "[FusionDarkRP Card] Nettoyage reseau : "
                            + exception.Message
                        );
                    }
                }

                initialized = false;
                Cards.Clear();
                ObjectCardIds.Clear();
                EntityObjectIds.Clear();
                ObjectRoots.Clear();
            }
        }

        public static bool RegisterHost(
            NetworkEntity entity,
            string cardId,
            out string error)
        {
            error = null;

            if (!NetworkInfo.HasServer || !NetworkInfo.IsHost)
            {
                error = "Seul l'hote peut enregistrer une carte.";
                return false;
            }

            if (entity == null || !entity.IsRegistered)
            {
                error = "Entite reseau de la carte invalide.";
                return false;
            }

            if (entity.ID == 0)
            {
                error = "Identifiant reseau invalide.";
                return false;
            }

            if (!IsValidCardId(cardId))
            {
                error = "Identifiant bancaire invalide.";
                return false;
            }

            if (!Initialize())
            {
                error = "Synchronisation des cartes indisponible.";
                return false;
            }

            try
            {
                CardMetadata metadata = new CardMetadata
                {
                    EntityId = entity.ID,
                    CardId = cardId
                };

                lock (Sync)
                {
                    RemoveObjectMappingLocked(entity.ID);
                    Cards[entity.ID] = metadata;
                }

                if (!TryApplyVisual(entity, cardId, out error))
                {
                    lock (Sync)
                    {
                        RemoveObjectMappingLocked(entity.ID);
                        Cards.Remove(entity.ID);
                    }

                    return false;
                }

                Broadcast(metadata);

                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Enregistrement de la carte impossible : "
                    + exception.Message;

                return false;
            }
        }

        internal static void RegisterReplica(
            ushort entityId,
            string cardId)
        {
            if (entityId == 0 || !IsValidCardId(cardId))
                return;

            lock (Sync)
            {
                RemoveObjectMappingLocked(entityId);

                Cards[entityId] = new CardMetadata
                {
                    EntityId = entityId,
                    CardId = cardId
                };
            }

            NetworkEntityManager.HookEntityRegistered(
                entityId,
                entity =>
                {
                    if (entity == null)
                        return;

                    lock (Sync)
                    {
                        CardMetadata current;

                        if (!Cards.TryGetValue(
                            entityId,
                            out current) ||
                            current == null ||
                            !string.Equals(
                                current.CardId,
                                cardId,
                                StringComparison.Ordinal))
                        {
                            return;
                        }
                    }

                    string error;

                    if (!TryApplyVisual(entity, cardId, out error))
                    {
                        MelonLogger.Warning(
                            "[FusionDarkRP Card] Visuel distant indisponible : "
                            + error
                        );
                    }
                }
            );
        }

        private static bool TryApplyVisual(
            NetworkEntity entity,
            string cardId,
            out string error)
        {
            error = null;

            if (entity == null)
            {
                error = "Entite reseau absente.";
                return false;
            }

            if (!IsValidCardId(cardId))
            {
                error = "Identifiant bancaire invalide.";
                return false;
            }

            try
            {
                WeaponSlotExtender weaponExtender =
                    entity.GetExtender<WeaponSlotExtender>();

                if (weaponExtender == null ||
                    weaponExtender.Component == null)
                {
                    error = "Support Eder 22 non disponible.";
                    return false;
                }

                GameObject supportRoot =
                    weaponExtender.Component.gameObject;

                if (supportRoot == null)
                {
                    error = "Objet support introuvable.";
                    return false;
                }

                if (!BankCardWeaponVisualAdapter.HasVisual(supportRoot))
                {
                    if (!BankCardWeaponVisualAdapter.TryApply(
                        supportRoot,
                        out error))
                    {
                        return false;
                    }
                }

                int instanceId = supportRoot.GetInstanceID();

                lock (Sync)
                {
                    CardMetadata current;

                    if (!Cards.TryGetValue(
                        entity.ID,
                        out current) ||
                        current == null ||
                        !string.Equals(
                            current.CardId,
                            cardId,
                            StringComparison.Ordinal))
                    {
                        error = "Les metadonnees de cette carte ont change.";
                        return false;
                    }

                    RemoveObjectMappingLocked(entity.ID);

                    ObjectCardIds[instanceId] = cardId;
                    EntityObjectIds[entity.ID] = instanceId;
                    ObjectRoots[instanceId] = supportRoot;
                }

                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Application du visuel impossible : "
                    + exception.Message;

                return false;
            }
        }

        private static void RemoveObjectMappingLocked(
            ushort entityId)
        {
            int instanceId;

            if (!EntityObjectIds.TryGetValue(
                entityId,
                out instanceId))
            {
                return;
            }

            EntityObjectIds.Remove(entityId);

            string existingCardId;

            if (ObjectCardIds.TryGetValue(
                instanceId,
                out existingCardId))
            {
                ObjectCardIds.Remove(instanceId);
            }

            ObjectRoots.Remove(instanceId);
        }

        private static void Broadcast(CardMetadata metadata)
        {
            if (metadata == null ||
                !NetworkInfo.HasServer ||
                !NetworkInfo.IsHost)
            {
                return;
            }

            long part1;
            long part2;

            if (!TryEncodeCardId(
                metadata.CardId,
                out part1,
                out part2))
            {
                return;
            }

            try
            {
                BankCardHolsterMetadataData data =
                    new BankCardHolsterMetadataData
                    {
                        EntityId = metadata.EntityId,
                        CardIdPart1 = part1,
                        CardIdPart2 = part2
                    };

                MessageRelay.RelayModule<
                    BankCardHolsterMetadataHandler,
                    BankCardHolsterMetadataData>(
                    data,
                    CommonMessageRoutes.ReliableToClients
                );
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Card] Diffusion metadata impossible : "
                    + exception
                );
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

            List<CardMetadata> snapshot;

            lock (Sync)
            {
                snapshot = new List<CardMetadata>(Cards.Values);
            }

            foreach (CardMetadata metadata in snapshot)
            {
                if (metadata == null)
                    continue;

                SendToPlayer(playerId, metadata);
            }
        }

        private static void SendToPlayer(
            PlayerID playerId,
            CardMetadata metadata)
        {
            if (playerId == null ||
                !playerId.IsValid ||
                metadata == null)
            {
                return;
            }

            long part1;
            long part2;

            if (!TryEncodeCardId(
                metadata.CardId,
                out part1,
                out part2))
            {
                return;
            }

            try
            {
                BankCardHolsterMetadataData data =
                    new BankCardHolsterMetadataData
                    {
                        EntityId = metadata.EntityId,
                        CardIdPart1 = part1,
                        CardIdPart2 = part2
                    };

                MessageRelay.RelayModule<
                    BankCardHolsterMetadataHandler,
                    BankCardHolsterMetadataData>(
                    data,
                    new MessageRoute(
                        playerId.SmallID,
                        NetworkChannel.Reliable
                    )
                );
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Card] Envoi au nouveau joueur impossible : "
                    + exception.Message
                );
            }
        }

        /// <summary>
        /// Cherche une carte bancaire dans une boîte orientée (fente du
        /// distributeur) PAR POSITION : fonctionne même si les colliders de
        /// la carte sont des déclencheurs, désactivés ou sur un autre calque.
        /// </summary>
        public static bool TryFindCardInBox(
            Vector3 center,
            Quaternion rotation,
            Vector3 halfExtents,
            string expectedCardId,
            out string cardId,
            out Collider cardCollider)
        {
            cardId = null;
            cardCollider = null;

            string firstId = null;
            Collider firstCollider = null;

            List<KeyValuePair<int, GameObject>> snapshot;

            lock (Sync)
                snapshot = new List<KeyValuePair<int, GameObject>>(ObjectRoots);

            Quaternion inverse = Quaternion.Inverse(rotation);
            Vector3 margin = new Vector3(0.03f, 0.03f, 0.03f);

            foreach (KeyValuePair<int, GameObject> pair in snapshot)
            {
                GameObject root = pair.Value;

                if (root == null)
                    continue;

                string id;

                lock (Sync)
                {
                    if (!ObjectCardIds.TryGetValue(pair.Key, out id))
                        continue;
                }

                Vector3 position = root.transform.position;
                Collider found = null;

                try
                {
                    Renderer[] renderers =
                        root.GetComponentsInChildren<Renderer>(true);

                    bool has = false;
                    Bounds bounds = new Bounds(position, Vector3.zero);

                    foreach (Renderer renderer in renderers)
                    {
                        if (renderer == null ||
                            renderer.gameObject.name !=
                            CarrierVisualAdapter.VisualObjectName)
                        {
                            continue;
                        }

                        if (!has)
                        {
                            bounds = renderer.bounds;
                            has = true;
                        }
                        else
                        {
                            bounds.Encapsulate(renderer.bounds);
                        }
                    }

                    if (has)
                        position = bounds.center;

                    found = root.GetComponentInChildren<Collider>(true);
                }
                catch
                {
                    // Utilise la position de la racine.
                }

                Vector3 local = inverse * (position - center);

                if (Mathf.Abs(local.x) > halfExtents.x + margin.x ||
                    Mathf.Abs(local.y) > halfExtents.y + margin.y ||
                    Mathf.Abs(local.z) > halfExtents.z + margin.z)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(expectedCardId) &&
                    string.Equals(id, expectedCardId,
                        StringComparison.Ordinal))
                {
                    cardId = id;
                    cardCollider = found;
                    return true;
                }

                if (firstId == null)
                {
                    firstId = id;
                    firstCollider = found;
                }
            }

            cardId = firstId;
            cardCollider = firstCollider;
            return firstId != null;
        }

        public static bool TryGetCardId(
            GameObject candidate,
            out string cardId)
        {
            cardId = null;

            if (candidate == null)
                return false;

            try
            {
                Transform current = candidate.transform;

                while (current != null)
                {
                    int instanceId = current.gameObject.GetInstanceID();

                    lock (Sync)
                    {
                        if (ObjectCardIds.TryGetValue(
                            instanceId,
                            out cardId))
                        {
                            return IsValidCardId(cardId);
                        }
                    }

                    current = current.parent;
                }
            }
            catch
            {
                cardId = null;
            }

            return false;
        }

        public static bool TryGetCardId(
            ushort entityId,
            out string cardId)
        {
            cardId = null;

            if (entityId == 0)
                return false;

            lock (Sync)
            {
                CardMetadata metadata;

                if (!Cards.TryGetValue(entityId, out metadata) ||
                    metadata == null)
                {
                    return false;
                }

                cardId = metadata.CardId;
                return IsValidCardId(cardId);
            }
        }

        public static bool TryRemove(
            ushort entityId,
            out string cardId)
        {
            cardId = null;

            if (entityId == 0)
                return false;

            lock (Sync)
            {
                CardMetadata metadata;

                if (!Cards.TryGetValue(entityId, out metadata) ||
                    metadata == null)
                {
                    RemoveObjectMappingLocked(entityId);
                    return false;
                }

                cardId = metadata.CardId;

                Cards.Remove(entityId);
                RemoveObjectMappingLocked(entityId);

                return true;
            }
        }

        private static bool TryEncodeCardId(
            string cardId,
            out long part1,
            out long part2)
        {
            part1 = 0;
            part2 = 0;

            Guid guid;

            if (!Guid.TryParseExact(cardId, "N", out guid) ||
                guid == Guid.Empty)
            {
                return false;
            }

            byte[] bytes = guid.ToByteArray();

            part1 = BitConverter.ToInt64(bytes, 0);
            part2 = BitConverter.ToInt64(bytes, 8);

            return true;
        }

        internal static bool TryDecodeCardId(
            long part1,
            long part2,
            out string cardId)
        {
            cardId = null;

            if (part1 == 0 && part2 == 0)
                return false;

            try
            {
                byte[] bytes = new byte[16];

                Buffer.BlockCopy(
                    BitConverter.GetBytes(part1),
                    0,
                    bytes,
                    0,
                    8
                );

                Buffer.BlockCopy(
                    BitConverter.GetBytes(part2),
                    0,
                    bytes,
                    8,
                    8
                );

                Guid guid = new Guid(bytes);

                if (guid == Guid.Empty)
                    return false;

                cardId = guid.ToString("N");
                return true;
            }
            catch
            {
                cardId = null;
                return false;
            }
        }

        private static bool IsValidCardId(string cardId)
        {
            return !string.IsNullOrWhiteSpace(cardId) &&
                   Guid.TryParseExact(cardId, "N", out Guid guid) &&
                   guid != Guid.Empty;
        }
    }
}
