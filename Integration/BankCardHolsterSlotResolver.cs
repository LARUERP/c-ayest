using System;
using Il2CppSLZ.Marrow;
using LabFusion.Entities;
using LabFusion.Marrow.Extenders;
using LabFusion.Player;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Recherche un emplacement de holster réellement présent
    /// sur le rig du joueur et récupère son identifiant réseau.
    /// </summary>
    public static class BankCardHolsterSlotResolver
    {
        /// <summary>
        /// Tous les emplacements libres du joueur (local ou distant).
        /// </summary>
        public static System.Collections.Generic.List<InventorySlotReceiver>
            EmptySlots(PlayerID target)
        {
            System.Collections.Generic.List<InventorySlotReceiver> list =
                new System.Collections.Generic.List<InventorySlotReceiver>();

            try
            {
                RigRefs refs = null;

                if (target != null && target.IsMe)
                    refs = LabFusion.Data.RigData.Refs;

                if (refs == null && target != null)
                {
                    ulong id = PlatformIds.Of(target);

                    foreach (NetworkPlayer player in NetworkPlayer.Players)
                    {
                        if (player != null && player.PlayerID != null &&
                            player.HasRig &&
                            PlatformIds.Of(player.PlayerID) == id)
                        {
                            refs = player.RigRefs;
                            break;
                        }
                    }
                }

                if (refs != null && refs.RigSlots != null)
                {
                    foreach (InventorySlotReceiver slot in refs.RigSlots)
                    {
                        if (slot != null && slot._slottedWeapon == null)
                            list.Add(slot);
                    }
                }
            }
            catch
            {
                // Liste vide : seul le premier emplacement sera tenté.
            }

            return list;
        }

        public static bool TryResolveEmptySlot(
            PlayerID targetPlayer,
            out InventorySlotReceiver receiver,
            out NetworkEntity slotEntity,
            out byte slotIndex,
            out string error)
        {
            receiver = null;
            slotEntity = null;
            slotIndex = 0;
            error = null;

            if (targetPlayer == null || !targetPlayer.IsValid)
            {
                error = "Le joueur destinataire est invalide.";
                return false;
            }

            try
            {
                ulong platformId = PlatformIds.Of(targetPlayer);

                if (platformId == 0)
                {
                    error = "Identifiant du joueur invalide.";
                    return false;
                }

                NetworkPlayer matchingPlayer = null;

                foreach (NetworkPlayer player in NetworkPlayer.Players)
                {
                    if (player == null || player.PlayerID == null)
                        continue;

                    if (!player.PlayerID.IsValid)
                        continue;

                    if (PlatformIds.Of(player.PlayerID) != platformId)
                        continue;

                    matchingPlayer = player;
                    break;
                }

                RigRefs rigRefs = null;

                if (matchingPlayer != null && matchingPlayer.HasRig)
                    rigRefs = matchingPlayer.RigRefs;

                // Joueur local (hôte) : il n'est pas toujours listé dans
                // NetworkPlayer.Players, son rig vient de RigData.
                if (rigRefs == null && targetPlayer.IsMe)
                    rigRefs = LabFusion.Data.RigData.Refs;

                if (rigRefs == null)
                {
                    error = matchingPlayer == null && !targetPlayer.IsMe
                        ? "Le joueur est introuvable dans LabFusion."
                        : "Le rig du joueur n'est pas encore disponible.";
                    return false;
                }

                InventorySlotReceiver[] slots = rigRefs.RigSlots;

                if (slots == null || slots.Length == 0)
                {
                    error = "Aucun emplacement de holster trouvé sur ce rig.";
                    return false;
                }

                InventorySlotReceiverExtender extender = null;

                foreach (InventorySlotReceiver candidate in slots)
                {
                    if (candidate == null)
                        continue;

                    // Ne jamais remplacer une arme déjà rangée.
                    if (candidate._slottedWeapon != null)
                        continue;

                    NetworkEntity candidateEntity;

                    if (!InventorySlotReceiverExtender.Cache.TryGet(
                        candidate,
                        out candidateEntity))
                    {
                        continue;
                    }

                    if (candidateEntity == null)
                        continue;

                    extender =
                        candidateEntity.GetExtender<
                            InventorySlotReceiverExtender>();

                    if (extender == null)
                        continue;

                    byte? index =
                        (byte?)extender.GetIndex(candidate);

                    if (!index.HasValue)
                        continue;

                    receiver = candidate;
                    slotEntity = candidateEntity;
                    slotIndex = index.Value;

                    error = null;
                    return true;
                }

                error =
                    "Aucun emplacement libre et synchronisé n'a été trouvé.";

                return false;
            }
            catch (Exception exception)
            {
                receiver = null;
                slotEntity = null;
                slotIndex = 0;

                error =
                    "Erreur pendant la recherche du holster : "
                    + exception.Message;

                return false;
            }
        }
    }
}
