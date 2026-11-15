
using System;
using System.Collections.Generic;
using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.Interaction;
using LabFusion.Data;
using LabFusion.Entities;
using LabFusion.Marrow.Extenders;
using LabFusion.Network;
using LabFusion.Player;
using LabFusion.RPC;
using MelonLoader;

namespace FusionDarkRP.Integration
{
    public static class BankCardHolsterSpawner
    {
        private static bool spawning;
        private static PlayerID targetPlayerForSlots;

        // Le composant d'arme (WeaponSlotExtender) est parfois attaché par
        // LabFusion quelques images APRÈS le rappel de création : au lieu
        // d'abandonner (et de détruire la carte), on réessaie jusqu'à 15 s.
        private sealed class PendingCard
        {
            public NetworkAssetSpawner.SpawnCallbackInfo Info;
            public InventorySlotReceiver Receiver;
            public NetworkEntity SlotEntity;
            public byte SlotIndex;
            public string CardId;
            public Func<bool> OnInserted;
            public Action OnRollback;
            public float Deadline;
        }

        private static readonly List<PendingCard> Pending =
            new List<PendingCard>();

        private static float nextPendingCheck;

        /// <summary>
        /// Appelée à chaque image : relance les créations de carte dont le
        /// composant d'arme n'était pas encore prêt.
        /// </summary>
        public static void Update()
        {
            if (Pending.Count == 0)
                return;

            if (UnityEngine.Time.realtimeSinceStartup < nextPendingCheck)
                return;

            nextPendingCheck = UnityEngine.Time.realtimeSinceStartup + 0.05f;

            PendingCard[] snapshot = Pending.ToArray();
            Pending.Clear();

            foreach (PendingCard pending in snapshot)
            {
                try
                {
                    HandleSpawnCompleted(
                        pending.Info,
                        pending.Receiver,
                        pending.SlotEntity,
                        pending.SlotIndex,
                        pending.CardId,
                        pending.OnInserted,
                        pending.OnRollback,
                        pending);
                }
                catch (Exception exception)
                {
                    MelonLogger.Error(
                        "[FusionDarkRP Card] Nouvelle tentative impossible : "
                        + exception);
                }
            }
        }

        public static bool TrySpawnForPlayer(
            PlayerID targetPlayer,
            string cardId,
            Func<bool> onInserted,
            Action onRollback,
            out string error)
        {
            error = null;

            if (!NetworkInfo.HasServer || !NetworkInfo.IsHost)
            {
                error = "Seul l'hôte peut créer une carte bancaire.";
                return false;
            }

            if (spawning)
            {
                error = "Une création de carte est déjà en cours.";
                return false;
            }

            if (targetPlayer == null || !targetPlayer.IsValid)
            {
                error = "Le joueur destinataire est invalide.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(cardId) ||
                !Guid.TryParseExact(cardId, "N", out Guid parsedCardId) ||
                parsedCardId == Guid.Empty)
            {
                error = "Identifiant bancaire invalide.";
                return false;
            }

            if (onInserted == null || onRollback == null)
            {
                error = "Les validations bancaires sont absentes.";
                return false;
            }

            InventorySlotReceiver receiver;
            NetworkEntity slotEntity;
            byte slotIndex;
            string slotError;

            // Aucun emplacement libre n'est plus bloquant : la carte apparaît
            // alors devant le joueur (objet saisissable) au lieu de disparaître.
            if (!BankCardHolsterSlotResolver.TryResolveEmptySlot(
                targetPlayer,
                out receiver,
                out slotEntity,
                out slotIndex,
                out slotError))
            {
                receiver = null;
                slotEntity = null;
                slotIndex = 0;

                MelonLogger.Warning(
                    "[FusionDarkRP Card] Holster indisponible (" + slotError
                    + ") : la carte sera posée devant le joueur."
                );
            }

            if (!BankCardSupportCrateResolver.TryResolve(
                out Il2CppSLZ.Marrow.Data.Spawnable spawnable,
                out string barcode,
                out error))
            {
                return false;
            }

            spawning = true;
            targetPlayerForSlots = targetPlayer;

            try
            {
                UnityEngine.Vector3 spawnPosition;
                UnityEngine.Quaternion spawnRotation;

                if (receiver != null)
                {
                    spawnPosition = receiver.transform.position;
                    spawnRotation = receiver.transform.rotation;
                }
                else
                {
                    UnityEngine.Transform rig =
                        PhysicalCashRigAnchorResolver.ResolveBody(
                            targetPlayer);

                    if (rig == null)
                    {
                        spawning = false;
                        error = "Position du joueur introuvable pour poser la carte.";
                        return false;
                    }

                    // rig = tête du joueur : la carte apparaît devant lui,
                    // un peu sous le niveau des yeux.
                    spawnPosition =
                        rig.position
                        + rig.forward * 0.5f
                        + UnityEngine.Vector3.down * 0.2f;

                    spawnRotation = rig.rotation;
                }

                NetworkAssetSpawner.Spawn(
                    new NetworkAssetSpawner.SpawnRequestInfo
                    {
                        Spawnable = spawnable,
                        Position = spawnPosition,
                        Rotation = spawnRotation,
                        SpawnEffect = false,
                        SpawnSource = EntitySource.Player,

                        SpawnCallback = info =>
                        {
                            spawning = false;

                            HandleSpawnCompleted(
                                info,
                                receiver,
                                slotEntity,
                                slotIndex,
                                cardId,
                                onInserted,
                                onRollback
                            );
                        }
                    }
                );

                MelonLogger.Msg(
                    "[FusionDarkRP Card] Création du support Eder 22 demandée."
                );

                return true;
            }
            catch (Exception exception)
            {
                spawning = false;

                error =
                    "Impossible de créer le support : "
                    + exception.Message;

                MelonLogger.Error(
                    "[FusionDarkRP Card] " + error
                );

                return false;
            }
        }

        private static void HandleSpawnCompleted(
            NetworkAssetSpawner.SpawnCallbackInfo info,
            InventorySlotReceiver receiver,
            NetworkEntity slotEntity,
            byte slotIndex,
            string cardId,
            Func<bool> onInserted,
            Action onRollback,
            PendingCard pending = null)
        {
            if (info.Spawned == null || info.Entity == null)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Card] Création du support échouée."
                );
                return;
            }

            NetworkEntity entity = info.Entity;

            WeaponSlot weaponSlot = null;

            try
            {
                WeaponSlotExtender weaponExtender =
                    entity.GetExtender<WeaponSlotExtender>();

                if (weaponExtender != null)
                    weaponSlot = weaponExtender.Component;
            }
            catch
            {
                weaponSlot = null;
            }

            // Repli : l'extender LabFusion peut tarder ; le composant du jeu
            // est déjà présent sur l'objet créé.
            if (weaponSlot == null)
            {
                try
                {
                    weaponSlot =
                        info.Spawned.GetComponentInChildren<WeaponSlot>(true);
                }
                catch
                {
                    weaponSlot = null;
                }
            }

            if (weaponSlot == null ||
                weaponSlot.interactableHost == null)
            {
                float now = UnityEngine.Time.realtimeSinceStartup;

                if (pending == null)
                {
                    pending = new PendingCard
                    {
                        Info = info,
                        Receiver = receiver,
                        SlotEntity = slotEntity,
                        SlotIndex = slotIndex,
                        CardId = cardId,
                        OnInserted = onInserted,
                        OnRollback = onRollback,
                        Deadline = now + 15f
                    };

                    MelonLogger.Msg(
                        "[FusionDarkRP Card] Composant du support pas encore "
                        + "prêt : nouvelle tentative."
                    );
                }

                if (now < pending.Deadline)
                {
                    Pending.Add(pending);
                    return;
                }

                Despawn(entity);

                MelonLogger.Error(
                    "[FusionDarkRP Card] Composant WeaponSlot toujours absent "
                    + "après 15 s : création abandonnée."
                );
                return;
            }

            bool holsterMode =
                receiver != null &&
                slotEntity != null &&
                slotEntity.IsRegistered &&
                receiver._slottedWeapon == null;

            if (!holsterMode)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Card] Holster indisponible ou occupé : "
                    + "la carte reste dans le monde, saisissable."
                );
            }

            string visualError;

            if (!BankCardWeaponVisualAdapter.TryApply(
                weaponSlot.gameObject,
                out visualError))
            {
                Despawn(entity);

                MelonLogger.Error(
                    "[FusionDarkRP Card] Visuel impossible : "
                    + visualError
                );
                return;
            }

            bool bankRecordCreated = false;

            try
            {
                if (holsterMode)
                {
                    IGrippable grippable =
                        weaponSlot.interactableHost
                            .TryCast<IGrippable>();

                    if (grippable == null)
                    {
                        holsterMode = false;

                        MelonLogger.Warning(
                            "[FusionDarkRP Card] Support incompatible avec le holster : "
                            + "carte laissée dans le monde."
                        );
                    }
                    else
                    {
                        receiver.OnHandDrop(grippable);

                        // Le premier emplacement libre peut refuser un pistolet
                        // (type d'emplacement différent) : on essaie les autres.
                        if (receiver._slottedWeapon != weaponSlot)
                        {
                            foreach (InventorySlotReceiver other in
                                BankCardHolsterSlotResolver.EmptySlots(targetPlayerForSlots))
                            {
                                if (other == null || other == receiver ||
                                    other._slottedWeapon != null)
                                    continue;

                                try
                                {
                                    other.OnHandDrop(grippable);
                                }
                                catch
                                {
                                    continue;
                                }

                                if (other._slottedWeapon == weaponSlot)
                                {
                                    receiver = other;
                                    break;
                                }
                            }
                        }

                        if (receiver._slottedWeapon != weaponSlot)
                        {
                            // Insertion refusée : la carte reste utilisable
                            // en objet saisissable plutôt que d'être annulée.
                            holsterMode = false;

                            MelonLogger.Warning(
                                "[FusionDarkRP Card] Insertion au holster refusée : "
                                + "carte laissée dans le monde."
                            );
                        }
                    }
                }

                // Le registre bancaire est modifié
                // uniquement après insertion physique réussie.
                if (!onInserted())
                {
                    throw new InvalidOperationException(
                        "Validation de la carte bancaire refusée."
                    );
                }

                bankRecordCreated = true;

                string metadataError;

                if (!BankCardHolsterMetadataNetwork.RegisterHost(
                    entity,
                    cardId,
                    out metadataError))
                {
                    throw new InvalidOperationException(
                        "Synchronisation des métadonnées impossible : "
                        + metadataError
                    );
                }

                MelonLogger.Msg(
                    holsterMode
                        ? "[FusionDarkRP Card] Carte créée, enregistrée et insérée dans le holster. Entité : "
                            + entity.ID
                        : "[FusionDarkRP Card] Carte créée et enregistrée, posée devant le joueur. Entité : "
                            + entity.ID
                );
            }
            catch (Exception exception)
            {
                if (bankRecordCreated)
                {
                    try
                    {
                        onRollback();
                    }
                    catch (Exception rollbackException)
                    {
                        MelonLogger.Error(
                            "[FusionDarkRP Card] Annulation bancaire échouée : "
                            + rollbackException
                        );
                    }
                }

                // Ne jamais éjecter une autre arme :
                // on ne retire que le support créé par cette opération.
                try
                {
                    if (receiver != null &&
                        receiver._slottedWeapon ==
                        weaponSlot)
                    {
                        receiver.DropWeapon();
                    }
                }
                catch (Exception dropException)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Card] Ejection du support impossible : "
                        + dropException.Message
                    );
                }

                BankCardWeaponVisualAdapter.TryRemove(
                    weaponSlot.gameObject,
                    out visualError
                );

                Despawn(entity);

                MelonLogger.Error(
                    "[FusionDarkRP Card] Création annulée : "
                    + exception
                );
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
                    }
                );
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Card] Nettoyage réseau impossible : "
                    + exception
                );
            }
        }
    }
}
