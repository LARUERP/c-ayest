
using System;
using FusionDarkRP.Economy;
using LabFusion.Network;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Point d'accès central aux objets 3D.
    /// Gère les créations de l'hôte et les copies locales des clients.
    /// Conserve les références aux objets dont la destruction a échoué.
    /// </summary>
    public static class WorldModelRuntimeManager
    {
        private static readonly object Sync = new object();

        private static RuntimeObjWorldModelSpawner spawner;

        public static bool IsInitialized
        {
            get
            {
                lock (Sync)
                    return spawner != null;
            }
        }

        public static int SpawnedCount
        {
            get
            {
                lock (Sync)
                    return spawner != null
                        ? spawner.SpawnedCount
                        : 0;
            }
        }

        public static bool Initialize()
        {
            lock (Sync)
            {
                if (spawner != null)
                    return true;

                try
                {
                    spawner = new RuntimeObjWorldModelSpawner();

                    MelonLogger.Msg(
                        "[FusionDarkRP World] Gestionnaire 3D initialise."
                    );

                    return true;
                }
                catch (Exception exception)
                {
                    spawner = null;

                    MelonLogger.Error(
                        "[FusionDarkRP World] Initialisation impossible : "
                        + exception
                    );

                    return false;
                }
            }
        }

        /// <summary>
        /// Création autorisée uniquement sur l'hôte.
        /// </summary>
        public static bool TrySpawn(
            string modelId,
            Vector3 position,
            Quaternion rotation,
            Transform parent,
            out string objectId,
            out string error)
        {
            objectId = null;
            error = null;

            if (!IsHost())
            {
                error = "Seul l'hôte peut créer un objet 3D.";
                return false;
            }

            RuntimeObjWorldModelSpawner currentSpawner;

            lock (Sync)
            {
                currentSpawner = spawner;
            }

            if (currentSpawner == null)
            {
                error = "Gestionnaire des objets 3D non initialise.";
                return false;
            }

            try
            {
                bool created = currentSpawner.TrySpawn(
                    modelId,
                    position,
                    rotation,
                    parent,
                    out objectId,
                    out error
                );

                // Objets mobiles : remplacés par un vrai objet BONELAB
                // saisissable (repli automatique si indisponible).
                if (created && parent == null)
                {
                    CarrierObjects.TryBegin(
                        objectId,
                        modelId,
                        position,
                        rotation,
                        currentSpawner);
                }

                return created;
            }
            catch (Exception exception)
            {
                objectId = null;
                error =
                    "Erreur de création de l'objet 3D : "
                    + exception.Message;

                MelonLogger.Error(
                    "[FusionDarkRP World] " + error
                );

                return false;
            }
        }

        /// <summary>
        /// Crée une copie locale d'un objet reçu de l'hôte.
        /// Réservé aux clients connectés.
        /// </summary>
        public static bool TrySpawnReplica(
            string objectId,
            string modelId,
            Vector3 position,
            Quaternion rotation,
            out string error)
        {
            error = null;

            if (!NetworkInfo.HasServer)
            {
                error = "Aucune connexion Fusion active.";
                return false;
            }

            if (NetworkInfo.IsHost)
            {
                error = "L'hôte ne doit pas créer une copie de son propre objet.";
                return false;
            }

            RuntimeObjWorldModelSpawner currentSpawner;

            lock (Sync)
            {
                currentSpawner = spawner;
            }

            if (currentSpawner == null)
            {
                error = "Gestionnaire des objets 3D non initialise.";
                return false;
            }

            try
            {
                string createdId;

                bool success = currentSpawner.TrySpawnWithId(
                    objectId,
                    modelId,
                    position,
                    rotation,
                    null,
                    out createdId,
                    out error
                );

                if (!success)
                    return false;

                MelonLogger.Msg(
                    "[FusionDarkRP World] Copie distante creee : "
                    + modelId
                    + " | "
                    + createdId
                );

                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Erreur de création de la copie distante : "
                    + exception.Message;

                MelonLogger.Error(
                    "[FusionDarkRP World] " + error
                );

                return false;
            }
        }

        public static bool TryGet(
            string objectId,
            out GameObject target)
        {
            target = null;

            RuntimeObjWorldModelSpawner currentSpawner;

            lock (Sync)
                currentSpawner = spawner;

            if (currentSpawner == null)
                return false;

            return currentSpawner.TryGet(
                objectId,
                out target
            );
        }

        public static bool TryGetModelId(
            string objectId,
            out string modelId)
        {
            modelId = null;

            RuntimeObjWorldModelSpawner currentSpawner;

            lock (Sync)
                currentSpawner = spawner;

            if (currentSpawner == null)
                return false;

            return currentSpawner.TryGetModelId(
                objectId,
                out modelId
            );
        }

        /// <summary>
        /// Suppression autorisée uniquement sur l'hôte.
        /// </summary>
        public static bool TryDestroy(
            string objectId,
            out string error)
        {
            error = null;

            if (!IsHost())
            {
                error = "Seul l'hôte peut supprimer un objet 3D.";
                return false;
            }

            RuntimeObjWorldModelSpawner currentSpawner;

            lock (Sync)
                currentSpawner = spawner;

            if (currentSpawner == null)
            {
                error = "Gestionnaire des objets 3D indisponible.";
                return false;
            }

            return currentSpawner.TryDestroy(
                objectId,
                out error
            );
        }

        /// <summary>
        /// Suppression locale d'une copie reçue du réseau.
        /// Réservée aux clients connectés.
        /// </summary>
        public static bool TryDestroyReplica(
            string objectId,
            out string error)
        {
            error = null;

            if (!NetworkInfo.HasServer || NetworkInfo.IsHost)
            {
                error = "Cette opération est réservée aux clients connectés.";
                return false;
            }

            RuntimeObjWorldModelSpawner currentSpawner;

            lock (Sync)
                currentSpawner = spawner;

            if (currentSpawner == null)
            {
                error = "Gestionnaire des objets 3D indisponible.";
                return false;
            }

            return currentSpawner.TryDestroy(
                objectId,
                out error
            );
        }

        public static string[] GetSpawnedObjectIds()
        {
            RuntimeObjWorldModelSpawner currentSpawner;

            lock (Sync)
                currentSpawner = spawner;

            return currentSpawner != null
                ? currentSpawner.GetSpawnedObjectIds()
                : new string[0];
        }

        /// <summary>
        /// Nettoie tous les objets.
        /// Si certains objets ne peuvent pas être détruits,
        /// conserve le gestionnaire pour permettre une nouvelle tentative.
        /// </summary>
        public static void Shutdown()
        {
            lock (Sync)
            {
                if (spawner == null)
                    return;

                RuntimeObjWorldModelSpawner currentSpawner = spawner;

                try
                {
                    currentSpawner.DestroyAll();

                    int remainingObjects =
                        currentSpawner.SpawnedCount;

                    if (remainingObjects == 0)
                    {
                        spawner = null;

                        MelonLogger.Msg(
                            "[FusionDarkRP World] Tous les objets 3D ont ete nettoyes."
                        );
                    }
                    else
                    {
                        // Ne pas abandonner les références restantes.
                        // Un prochain Shutdown pourra retenter leur destruction.
                        MelonLogger.Error(
                            "[FusionDarkRP World] "
                            + remainingObjects
                            + " objet(s) restent a nettoyer. "
                            + "Le gestionnaire conserve leurs references."
                        );
                    }
                }
                catch (Exception exception)
                {
                    // En cas d'erreur, conserver le gestionnaire.
                    // Cela évite de perdre les références aux objets restants.
                    MelonLogger.Error(
                        "[FusionDarkRP World] Erreur nettoyage : "
                        + exception
                    );
                }
            }
        }

        private static bool IsHost()
        {
            try
            {
                return NetworkInfo.HasServer
                    && NetworkInfo.IsHost;
            }
            catch
            {
                return false;
            }
        }
    }
}
