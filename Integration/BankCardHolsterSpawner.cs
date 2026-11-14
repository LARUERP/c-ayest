
using System;
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

            if (!BankCardHolsterSlotResolver.TryResolveEmptySlot(
                targetPlayer,
                out InventorySlotReceiver receiver,
                out NetworkEntity slotEntity,
                out byte slotIndex,
                out error))
            {
                return false;
            }

            if (!BankCardSupportCrateResolver.TryResolve(
                out Il2CppSLZ.Marrow.Data.Spawnable spawnable,
                out string barcode,
                out error))
            {
                return false;
            }

            if (receiver == null || slotEntity == null)
            {
                error = "L'emplacement de holster est invalide.";
                return false;
            }

            spawning = true;
            targetPlayerForSlots = targetPlayer;

            try
            {
                UnityEngine.Transform slotTransform =
                    receiver.transform;

                NetworkAssetSpawner.Spawn(
                    new NetworkAssetSpawner.SpawnRequestInfo
                    {
                        Spawnable = spawnable,
                        Position = slotTransform.position,
                        Rotation = slotTransform.rotation,
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
            Action onRollback)
        {
            if (info.Spawned == null || info.Entity == null)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Card] Création du support échouée."
                );
                return;
            }

            NetworkEntity entity = info.Entity;

            WeaponSlotExtender weaponExtender =
                entity.GetExtender<WeaponSlotExtender>();

            if (weaponExtender == null ||
                weaponExtender.Component == null ||
                weaponExtender.Component.interactableHost == null)
            {
                Despawn(entity);

                MelonLogger.Error(
                    "[FusionDarkRP Card] Composant WeaponSlot absent."
                );
                return;
            }

            if (receiver == null ||
                slotEntity == null ||
                !slotEntity.IsRegistered ||
                receiver._slottedWeapon != null)
            {
                Despawn(entity);

                MelonLogger.Warning(
                    "[FusionDarkRP Card] Holster indisponible ou occupé."
                );
                return;
            }

            string visualError;

            if (!BankCardWeaponVisualAdapter.TryApply(
                weaponExtender.Component.gameObject,
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
                IGrippable grippable =
                    weaponExtender.Component.interactableHost
                        .TryCast<IGrippable>();

                if (grippable == null)
                {
                    throw new InvalidOperationException(
                        "Support incompatible avec le holster."
                    );
                }

                receiver.OnHandDrop(grippable);

                // Le premier emplacement libre peut refuser un pistolet
                // (type d'emplacement différent) : on essaie les autres.
                if (receiver._slottedWeapon != weaponExtender.Component)
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

                        if (other._slottedWeapon == weaponExtender.Component)
                        {
                            receiver = other;
                            break;
                        }
                    }
                }

                // Vérifie que c'est bien notre support
                // qui a été inséré dans cet emplacement.
                if (receiver._slottedWeapon !=
                    weaponExtender.Component)
                {
                    throw new InvalidOperationException(
                        "Insertion du support Eder 22 échouée."
                    );
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
                    "[FusionDarkRP Card] Carte créée, enregistrée "
                    + "et insérée dans le holster. Entité : "
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
                        weaponExtender.Component)
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
                    weaponExtender.Component.gameObject,
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
