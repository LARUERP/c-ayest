
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FusionDarkRP.Economy
{
    /// <summary>
    /// Gestionnaire central des objets 3D Fusion DarkRP.
    /// Prend en charge les identifiants synchronisables entre joueurs.
    /// </summary>
    public sealed class RuntimeObjWorldModelSpawner
    {
        private const int MaxSpawnedObjects = 256;

        private readonly object sync = new object();

        private readonly Dictionary<string, GameObject> spawnedObjects =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);

        private readonly Dictionary<string, string> objectModels =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public int SpawnedCount
        {
            get
            {
                lock (sync)
                {
                    RemoveDestroyedEntries();
                    return spawnedObjects.Count;
                }
            }
        }

        public bool TrySpawn(
            string modelId,
            Vector3 position,
            Quaternion rotation,
            Transform parent,
            out string objectId,
            out string error)
        {
            return TrySpawnWithId(
                Guid.NewGuid().ToString("N"),
                modelId,
                position,
                rotation,
                parent,
                out objectId,
                out error);
        }

        public bool TrySpawnWithId(
            string requestedObjectId,
            string modelId,
            Vector3 position,
            Quaternion rotation,
            Transform parent,
            out string objectId,
            out string error)
        {
            objectId = null;
            error = null;

            if (string.IsNullOrWhiteSpace(requestedObjectId))
            {
                error = "Identifiant de l'objet manquant.";
                return false;
            }

            if (!Guid.TryParseExact(
                requestedObjectId,
                "N",
                out Guid parsedId))
            {
                error = "Identifiant de synchronisation invalide.";
                return false;
            }

            string normalizedObjectId = parsedId.ToString("N");

            if (string.IsNullOrWhiteSpace(modelId))
            {
                error = "Identifiant du modèle manquant.";
                return false;
            }

            if (!IsFinite(position))
            {
                error = "Position de création invalide.";
                return false;
            }

            if (!IsFinite(rotation))
            {
                error = "Rotation de création invalide.";
                return false;
            }

            float rotationMagnitude =
                rotation.x * rotation.x
                + rotation.y * rotation.y
                + rotation.z * rotation.z
                + rotation.w * rotation.w;

            if (!IsFinite(rotationMagnitude)
                || rotationMagnitude < 0.0001f
                || rotationMagnitude > 100f)
            {
                error = "Rotation nulle ou hors limites.";
                return false;
            }

            if (!RuntimeObjWorldModelCatalog.TryGet(
                modelId,
                out WorldModelDefinition definition)
                || definition == null
                || string.IsNullOrWhiteSpace(definition.Id))
            {
                error = "Modèle absent du catalogue : " + modelId;
                return false;
            }

            if (!IsFinite(definition.Scale)
                || definition.Scale <= 0f
                || definition.Scale > 10f)
            {
                error = "Échelle invalide pour le modèle : "
                    + definition.Id;
                return false;
            }

            lock (sync)
            {
                RemoveDestroyedEntries();

                if (spawnedObjects.ContainsKey(normalizedObjectId)
                    || objectModels.ContainsKey(normalizedObjectId))
                {
                    error = "Cet objet existe déjà.";
                    return false;
                }

                if (spawnedObjects.Count >= MaxSpawnedObjects)
                {
                    error =
                        "Limite de "
                        + MaxSpawnedObjects
                        + " objets 3D atteinte.";

                    return false;
                }

                GameObject createdObject = null;
                bool objectRegistered = false;
                bool modelRegistered = false;

                try
                {
                    if (!RuntimeObjWorldObjectFactory.TryCreate(
                        definition.Id,
                        definition.EnablePhysics,
                        out createdObject,
                        out error))
                    {
                        return false;
                    }

                    if (createdObject == null)
                    {
                        error =
                            "La fabrique 3D n'a retourné aucun objet.";

                        return false;
                    }

                    MeshFilter filter =
                        createdObject.GetComponent<MeshFilter>();

                    MeshRenderer renderer =
                        createdObject.GetComponent<MeshRenderer>();

                    MeshCollider collider =
                        createdObject.GetComponent<MeshCollider>();

                    if (filter == null || filter.sharedMesh == null)
                    {
                        error = "Le MeshFilter ou son mesh est absent.";
                        return false;
                    }

                    if (renderer == null)
                    {
                        error = "Le MeshRenderer est absent.";
                        return false;
                    }

                    if (collider == null || collider.sharedMesh == null)
                    {
                        error = "Le MeshCollider ou son mesh est absent.";
                        return false;
                    }

                    Mesh mesh = filter.sharedMesh;

                    if (mesh.vertexCount <= 0 || mesh.subMeshCount <= 0)
                    {
                        error = "Le mesh ne contient aucune géométrie.";
                        return false;
                    }

                    Material[] materials = renderer.sharedMaterials;

                    if (materials == null
                        || materials.Length != mesh.subMeshCount)
                    {
                        error =
                            "Nombre de matériaux incorrect : "
                            + (materials == null ? 0 : materials.Length)
                            + " matériau(x) pour "
                            + mesh.subMeshCount
                            + " sous-mesh(es).";

                        return false;
                    }

                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] == null)
                        {
                            error =
                                "Matériau manquant sur le sous-mesh "
                                + i + ".";

                            return false;
                        }
                    }

                    Bounds meshBounds = mesh.bounds;

                    if (!IsFinite(meshBounds.center)
                        || !IsFinite(meshBounds.size)
                        || meshBounds.size.sqrMagnitude < 0.0000001f)
                    {
                        error =
                            "Les dimensions de la géométrie du modèle "
                            + "sont nulles ou invalides.";

                        return false;
                    }

                    createdObject.transform.SetParent(parent, true);

                    createdObject.transform.SetPositionAndRotation(
                        position,
                        rotation.normalized);

                    // Échelle définie dans le catalogue.
                    // Les billets, pièces, cartes et ATM restent à 1.
                    createdObject.transform.localScale =
                        Vector3.one * definition.Scale;

                    createdObject.name =
                        "FusionDarkRP_World_"
                        + definition.Id
                        + "_"
                        + normalizedObjectId;

                    renderer.enabled = true;
                    collider.enabled = true;

                    if (!createdObject.activeSelf)
                    {
                        createdObject.SetActive(true);
                    }

                    if (!createdObject.activeInHierarchy)
                    {
                        error =
                            "Le modèle reste inactif dans sa hiérarchie.";

                        return false;
                    }

                    if (!renderer.enabled)
                    {
                        error =
                            "Le MeshRenderer reste désactivé.";

                        return false;
                    }

                    Bounds worldBounds = renderer.bounds;

                    if (!IsFinite(worldBounds.center)
                        || !IsFinite(worldBounds.size)
                        || worldBounds.size.sqrMagnitude < 0.0000001f)
                    {
                        error =
                            "Le modèle est activé mais ses limites de "
                            + "rendu sont invalides.";

                        return false;
                    }

                    spawnedObjects.Add(
                        normalizedObjectId,
                        createdObject);

                    objectRegistered = true;

                    objectModels.Add(
                        normalizedObjectId,
                        definition.Id);

                    modelRegistered = true;

                    objectId = normalizedObjectId;

                    Debug.Log(
                        "[FusionDarkRP World] Objet enregistré : "
                        + definition.Id
                        + " | Sommets : "
                        + mesh.vertexCount
                        + " | Sous-meshes : "
                        + mesh.subMeshCount
                        + " | Matériaux : "
                        + materials.Length
                        + " | Échelle : "
                        + definition.Scale
                        + " | Position : "
                        + createdObject.transform.position
                        + " | Bounds mesh : "
                        + meshBounds.size
                        + " | Bounds monde : "
                        + worldBounds.size
                        + " | Actif : "
                        + createdObject.activeInHierarchy
                        + " | Renderer : "
                        + renderer.enabled);

                    createdObject = null;

                    return true;
                }
                catch (Exception exception)
                {
                    if (modelRegistered)
                        objectModels.Remove(normalizedObjectId);

                    if (objectRegistered)
                        spawnedObjects.Remove(normalizedObjectId);

                    objectId = null;

                    error =
                        "Impossible de créer "
                        + definition.DisplayName
                        + " : "
                        + exception.Message;

                    Debug.LogError(
                        "[FusionDarkRP World] " + error);

                    return false;
                }
                finally
                {
                    if (createdObject != null)
                    {
                        try
                        {
                            RuntimeObjWorldObjectFactory.Destroy(
                                createdObject);
                        }
                        catch (Exception cleanupException)
                        {
                            Debug.LogError(
                                "[FusionDarkRP World] Nettoyage après "
                                + "échec de création impossible : "
                                + cleanupException);
                        }
                    }
                }
            }
        }

        public bool TryGet(
            string objectId,
            out GameObject target)
        {
            target = null;

            if (string.IsNullOrWhiteSpace(objectId))
                return false;

            lock (sync)
            {
                RemoveDestroyedEntries();

                if (!spawnedObjects.TryGetValue(objectId, out target))
                    return false;

                return target != null;
            }
        }

        /// <summary>
        /// Remplace l'objet simple d'un identifiant par un objet porteur
        /// saisissable (réseau). L'objet simple est détruit proprement.
        /// </summary>
        public bool ReplaceObject(string objectId, GameObject replacement)
        {
            if (string.IsNullOrWhiteSpace(objectId) || replacement == null)
                return false;

            lock (sync)
            {
                if (!spawnedObjects.TryGetValue(objectId, out GameObject old))
                    return false;

                spawnedObjects[objectId] = replacement;

                if (old != null && old != replacement)
                {
                    try
                    {
                        RuntimeObjWorldObjectFactory.Destroy(old);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning(
                            "[FusionDarkRP World] Ancien objet non détruit : "
                            + exception.Message);
                    }
                }

                return true;
            }
        }

        public bool TryGetModelId(
            string objectId,
            out string modelId)
        {
            modelId = null;

            if (string.IsNullOrWhiteSpace(objectId))
                return false;

            lock (sync)
            {
                RemoveDestroyedEntries();

                return objectModels.TryGetValue(objectId, out modelId);
            }
        }

        public bool TryDestroy(
            string objectId,
            out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(objectId))
            {
                error = "Identifiant de l'objet invalide.";
                return false;
            }

            lock (sync)
            {
                if (!spawnedObjects.TryGetValue(
                    objectId,
                    out GameObject target))
                {
                    error = "Objet 3D introuvable.";
                    return false;
                }

                bool carrierReady =
                    FusionDarkRP.Integration.CarrierObjects.IsReady(objectId);

                if (FusionDarkRP.Integration.CarrierObjects.Release(objectId)
                    && carrierReady)
                {
                    // Objet réseau : le despawn LabFusion suffit.
                    spawnedObjects.Remove(objectId);
                    objectModels.Remove(objectId);

                    return true;
                }

                if (target != null)
                {
                    try
                    {
                        RuntimeObjWorldObjectFactory.Destroy(target);
                    }
                    catch (Exception exception)
                    {
                        error =
                            "Erreur pendant la destruction de l'objet : "
                            + exception.Message;

                        return false;
                    }
                }

                spawnedObjects.Remove(objectId);
                objectModels.Remove(objectId);

                return true;
            }
        }

        public void DestroyAll()
        {
            lock (sync)
            {
                List<string> ids =
                    new List<string>(spawnedObjects.Keys);

                foreach (string id in ids)
                {
                    if (!spawnedObjects.TryGetValue(
                        id,
                        out GameObject target))
                    {
                        objectModels.Remove(id);
                        continue;
                    }

                    if (target == null)
                    {
                        spawnedObjects.Remove(id);
                        objectModels.Remove(id);
                        continue;
                    }

                    try
                    {
                        bool ready =
                            FusionDarkRP.Integration.CarrierObjects.IsReady(id);

                        bool carrier =
                            FusionDarkRP.Integration.CarrierObjects.Release(id);

                        if (!(carrier && ready))
                            RuntimeObjWorldObjectFactory.Destroy(target);

                        spawnedObjects.Remove(id);
                        objectModels.Remove(id);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning(
                            "[FusionDarkRP World] Erreur de nettoyage "
                            + id + " : " + exception);
                    }
                }
            }
        }

        public string[] GetSpawnedObjectIds()
        {
            lock (sync)
            {
                RemoveDestroyedEntries();

                string[] ids =
                    new string[spawnedObjects.Count];

                spawnedObjects.Keys.CopyTo(ids, 0);

                Array.Sort(ids, StringComparer.Ordinal);

                return ids;
            }
        }

        private void RemoveDestroyedEntries()
        {
            List<string> destroyedIds = null;

            foreach (KeyValuePair<string, GameObject> entry
                in spawnedObjects)
            {
                if (entry.Value == null)
                {
                    if (destroyedIds == null)
                        destroyedIds = new List<string>();

                    destroyedIds.Add(entry.Key);
                }
            }

            if (destroyedIds == null)
                return;

            foreach (string id in destroyedIds)
            {
                spawnedObjects.Remove(id);
                objectModels.Remove(id);
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
