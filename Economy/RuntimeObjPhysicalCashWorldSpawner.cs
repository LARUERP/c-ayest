
using System;
using System.Collections.Generic;
using FusionDarkRP.Integration;
using UnityEngine;

namespace FusionDarkRP.Economy
{
    /// <summary>
    /// Crée et suit les billets, pièces et liasses dans le gestionnaire
    /// central des objets 3D.
    /// </summary>
    public sealed class RuntimeObjPhysicalCashWorldSpawner
        : IPhysicalCashWorldSpawner
    {
        private readonly object sync = new object();

        private readonly Func<Transform> spawnAnchorProvider;
        private readonly float forwardDistance;
        private readonly float upwardOffset;

        private readonly Dictionary<string, string> objectCashIds =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly Dictionary<string, string> cashObjectIds =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly HashSet<string> spawnedCashIds =
            new HashSet<string>(StringComparer.Ordinal);

        public RuntimeObjPhysicalCashWorldSpawner(
            Func<Transform> spawnAnchorProvider,
            float forwardDistance = 0.8f,
            float upwardOffset = 0.15f)
        {
            this.spawnAnchorProvider = spawnAnchorProvider
                ?? throw new ArgumentNullException(
                    nameof(spawnAnchorProvider));

            if (!IsFinite(forwardDistance)
                || forwardDistance < 0.1f
                || forwardDistance > 10f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(forwardDistance));
            }

            if (!IsFinite(upwardOffset)
                || upwardOffset < 0f
                || upwardOffset > 5f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(upwardOffset));
            }

            this.forwardDistance = forwardDistance;
            this.upwardOffset = upwardOffset;
        }

        public int SpawnedCount
        {
            get
            {
                lock (sync)
                {
                    RemoveDestroyedEntries();
                    return objectCashIds.Count;
                }
            }
        }

        public bool TrySpawn(
            PhysicalCashSpawnTicket ticket,
            out string spawnedObjectId,
            out string error)
        {
            spawnedObjectId = null;
            error = null;

            if (ticket == null)
            {
                error = "Ticket d'argent physique manquant.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(ticket.CashId)
                || string.IsNullOrWhiteSpace(ticket.OwnerPlayerId)
                || !PhysicalCashRequestId.IsValid(ticket.RequestId))
            {
                error = "Identité du billet, du propriétaire "
                    + "ou de la requête invalide.";
                return false;
            }

            if (!PhysicalCashCatalog.TryGet(
                ticket.ItemId,
                out PhysicalCashItem cashItem))
            {
                error = "Dénomination inconnue : " + ticket.ItemId;
                return false;
            }

            if (cashItem == null
                || cashItem.ValueCents <= 0
                || cashItem.ValueCents != ticket.AmountCents)
            {
                error = "La valeur du modèle ne correspond pas au ticket.";
                return false;
            }

            if (!RuntimeObjWorldModelCatalog.TryGet(
                cashItem.Id,
                out WorldModelDefinition definition)
                || definition == null
                || string.IsNullOrWhiteSpace(definition.Id)
                || !RuntimeObjWorldModelCatalog.IsPhysicalCash(
                    cashItem.Id))
            {
                error = "Ce modèle n'est pas autorisé comme argent physique.";
                return false;
            }

            Transform anchor;

            try
            {
                anchor = spawnAnchorProvider();
            }
            catch (Exception exception)
            {
                error = "Impossible de récupérer le point d'apparition : "
                    + exception.Message;
                return false;
            }

            if (anchor == null)
            {
                error = "Point d'apparition du joueur indisponible.";
                return false;
            }

            lock (sync)
            {
                RemoveDestroyedEntries();

                if (spawnedCashIds.Contains(ticket.CashId)
                    || cashObjectIds.ContainsKey(ticket.CashId))
                {
                    error = "Cet argent existe déjà dans le monde.";
                    return false;
                }

                Vector3 anchorPosition = anchor.position;
                Vector3 anchorForward = anchor.forward;
                Vector3 anchorUp = anchor.up;

                if (!IsFinite(anchorPosition)
                    || !IsFinite(anchorForward)
                    || !IsFinite(anchorUp))
                {
                    error = "Position ou orientation du joueur invalide.";
                    return false;
                }

                Vector3 spawnPosition =
                    anchorPosition
                    + anchorForward * forwardDistance
                    + anchorUp * upwardOffset;

                Quaternion spawnRotation = anchor.rotation;

                if (!IsFinite(spawnPosition)
                    || !IsFinite(spawnRotation))
                {
                    error = "Transform de création invalide.";
                    return false;
                }

                float rotationMagnitude =
                    spawnRotation.x * spawnRotation.x
                    + spawnRotation.y * spawnRotation.y
                    + spawnRotation.z * spawnRotation.z
                    + spawnRotation.w * spawnRotation.w;

                if (!IsFinite(rotationMagnitude)
                    || rotationMagnitude < 0.0001f
                    || rotationMagnitude > 100f)
                {
                    error = "Rotation de création invalide.";
                    return false;
                }

                spawnRotation = spawnRotation.normalized;

                string objectId;

                if (!WorldModelRuntimeManager.TrySpawn(
                    definition.Id,
                    spawnPosition,
                    spawnRotation,
                    null,
                    out objectId,
                    out error))
                {
                    if (string.IsNullOrWhiteSpace(error))
                        error = "Impossible de créer l'objet physique.";

                    return false;
                }

                if (string.IsNullOrWhiteSpace(objectId))
                {
                    error = "Identifiant d'objet central invalide.";
                    return false;
                }

                bool objectRegistered = false;
                bool cashRegistered = false;
                bool spawnedRegistered = false;

                try
                {
                    if (!WorldModelRuntimeManager.TryGet(
                        objectId,
                        out GameObject createdObject)
                        || createdObject == null)
                    {
                        error =
                            "L'objet créé est introuvable dans "
                            + "le registre central.";

                        return false;
                    }

                    createdObject.name =
                        "FusionDarkRP_Cash_"
                        + definition.Id
                        + "_"
                        + ticket.CashId;

                    FusionDarkRP.Integration.CarrierObjects.ApplyName(
                        objectId, createdObject.name);

                    objectCashIds.Add(objectId, ticket.CashId);
                    objectRegistered = true;

                    cashObjectIds.Add(ticket.CashId, objectId);
                    cashRegistered = true;

                    spawnedCashIds.Add(ticket.CashId);
                    spawnedRegistered = true;

                    // Diffusion aux joueurs déjà connectés.
                    // Les nouveaux joueurs seront synchronisés
                    // par WorldModelLateJoinSync.
                    if (!WorldModelNetwork.BroadcastSpawn(
                        objectId,
                        definition.Id,
                        spawnPosition,
                        spawnRotation))
                    {
                        Debug.LogWarning(
                            "[FusionDarkRP Cash] Objet créé sur l'hôte, "
                            + "mais diffusion réseau non confirmée : "
                            + objectId
                        );
                    }

                    spawnedObjectId = objectId;
                    return true;
                }
                catch (Exception exception)
                {
                    if (objectRegistered)
                        objectCashIds.Remove(objectId);

                    if (cashRegistered)
                        cashObjectIds.Remove(ticket.CashId);

                    if (spawnedRegistered)
                        spawnedCashIds.Remove(ticket.CashId);

                    string cleanupError;

                    bool cleaned =
                        WorldModelRuntimeManager.TryDestroy(
                            objectId,
                            out cleanupError);

                    error =
                        "Finalisation de "
                        + definition.DisplayName
                        + " impossible : "
                        + exception.Message;

                    if (!cleaned
                        && !string.IsNullOrWhiteSpace(cleanupError))
                    {
                        error += " Nettoyage : " + cleanupError;
                    }

                    return false;
                }
                finally
                {
                    // Si la création n'a pas été validée, on tente
                    // de retirer l'objet resté dans le gestionnaire.
                    if (string.IsNullOrWhiteSpace(spawnedObjectId)
                        && WorldModelRuntimeManager.TryGet(
                            objectId,
                            out GameObject remainingObject)
                        && remainingObject != null)
                    {
                        string ignoredError;

                        WorldModelRuntimeManager.TryDestroy(
                            objectId,
                            out ignoredError);
                    }
                }
            }
        }

        public bool TryGetCashId(
            string spawnedObjectId,
            out string cashId)
        {
            cashId = null;

            if (string.IsNullOrWhiteSpace(spawnedObjectId))
                return false;

            lock (sync)
            {
                RemoveDestroyedEntries();

                return objectCashIds.TryGetValue(
                    spawnedObjectId,
                    out cashId);
            }
        }

        public bool TryGetObjectId(
            string cashId,
            out string spawnedObjectId)
        {
            spawnedObjectId = null;

            if (string.IsNullOrWhiteSpace(cashId))
                return false;

            lock (sync)
            {
                RemoveDestroyedEntries();

                return cashObjectIds.TryGetValue(
                    cashId,
                    out spawnedObjectId);
            }
        }

        /// <summary>
        /// Détruit d'abord l'objet sur l'hôte.
        /// La suppression réseau est ensuite diffusée aux clients.
        /// </summary>
        public bool TryDestroy(
            string spawnedObjectId,
            out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(spawnedObjectId))
            {
                error = "Identifiant d'objet invalide.";
                return false;
            }

            lock (sync)
            {
                if (!objectCashIds.TryGetValue(
                    spawnedObjectId,
                    out string cashId))
                {
                    error = "Objet physique introuvable.";
                    return false;
                }

                if (!WorldModelRuntimeManager.TryDestroy(
                    spawnedObjectId,
                    out error))
                {
                    return false;
                }

                // Suppression réseau après destruction locale.
                if (!WorldModelNetwork.BroadcastDestroy(
                    spawnedObjectId))
                {
                    Debug.LogWarning(
                        "[FusionDarkRP Cash] Objet supprimé sur l'hôte, "
                        + "mais diffusion réseau non confirmée : "
                        + spawnedObjectId
                    );
                }

                RemoveMappings(spawnedObjectId, cashId);

                return true;
            }
        }

        public bool Exists(string spawnedObjectId)
        {
            if (string.IsNullOrWhiteSpace(spawnedObjectId))
                return false;

            lock (sync)
            {
                RemoveDestroyedEntries();

                return objectCashIds.ContainsKey(spawnedObjectId)
                    && WorldModelRuntimeManager.TryGet(
                        spawnedObjectId,
                        out GameObject target)
                    && target != null;
            }
        }

        /// <summary>
        /// Nettoie uniquement les objets physiques suivis par ce spawner.
        /// </summary>
        public void DestroyAll()
        {
            lock (sync)
            {
                List<string> objectIds =
                    new List<string>(objectCashIds.Keys);

                foreach (string objectId in objectIds)
                {
                    string destroyError;

                    bool destroyed =
                        WorldModelRuntimeManager.TryDestroy(
                            objectId,
                            out destroyError);

                    if (destroyed)
                    {
                        if (!WorldModelNetwork.BroadcastDestroy(objectId))
                        {
                            Debug.LogWarning(
                                "[FusionDarkRP Cash] Diffusion de suppression "
                                + "non confirmée : "
                                + objectId
                            );
                        }

                        if (objectCashIds.TryGetValue(
                            objectId,
                            out string cashId))
                        {
                            RemoveMappings(objectId, cashId);
                        }
                    }
                    else
                    {
                        Debug.LogWarning(
                            "[FusionDarkRP Cash] Nettoyage impossible : "
                            + objectId
                            + " | "
                            + destroyError
                        );
                    }
                }

                RemoveDestroyedEntries();
            }
        }

        private void RemoveDestroyedEntries()
        {
            List<string> destroyedIds = null;

            foreach (KeyValuePair<string, string> entry
                in objectCashIds)
            {
                if (!WorldModelRuntimeManager.TryGet(
                    entry.Key,
                    out GameObject target)
                    || target == null)
                {
                    if (destroyedIds == null)
                        destroyedIds = new List<string>();

                    destroyedIds.Add(entry.Key);
                }
            }

            if (destroyedIds == null)
                return;

            foreach (string objectId in destroyedIds)
            {
                if (objectCashIds.TryGetValue(
                    objectId,
                    out string cashId))
                {
                    RemoveMappings(objectId, cashId);
                }
            }
        }

        private void RemoveMappings(
            string objectId,
            string cashId)
        {
            objectCashIds.Remove(objectId);

            if (!string.IsNullOrWhiteSpace(cashId))
            {
                cashObjectIds.Remove(cashId);
                spawnedCashIds.Remove(cashId);
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
