
using System;
using UnityEngine;

namespace FusionDarkRP.Economy
{
    /// <summary>
    /// Fabrique universelle des objets 3D Fusion DarkRP.
    /// Affecte un matériau à chaque sous-mesh OBJ.
    /// Gère également la libération des ressources Unity.
    /// </summary>
    public static class RuntimeObjWorldObjectFactory
    {
        public static bool TryCreate(
            string modelName,
            bool enablePhysics,
            out GameObject createdObject,
            out string error)
        {
            createdObject = null;
            error = null;

            if (string.IsNullOrWhiteSpace(modelName))
            {
                error = "Nom du modèle manquant.";
                return false;
            }

            RuntimeObjMeshData meshData;

            if (!RuntimeObjMeshLoader.TryLoad(
                modelName,
                out meshData,
                out error))
            {
                return false;
            }

            if (meshData == null || !meshData.IsValid)
            {
                error = "Données géométriques OBJ invalides.";
                return false;
            }

            Mesh mesh = null;
            Material[] materials = null;
            GameObject instance = null;

            bool ownershipTransferred = false;

            try
            {
                // Construction du mesh et de ses sous-meshes.
                if (!RuntimeObjUnityMeshBuilder.TryBuild(
                    meshData,
                    out mesh,
                    out error))
                {
                    return false;
                }

                if (mesh == null)
                {
                    error = "La construction du mesh a échoué.";
                    return false;
                }

                int materialCount =
                    meshData.Submeshes != null &&
                    meshData.Submeshes.Count > 0
                        ? meshData.Submeshes.Count
                        : 1;

                if (mesh.subMeshCount != materialCount)
                {
                    error =
                        "Le nombre de sous-meshes ne correspond "
                        + "pas au nombre de matériaux.";

                    return false;
                }

                materials = new Material[materialCount];

                // Création d'un matériau par sous-mesh.
                for (int i = 0; i < materialCount; i++)
                {
                    string materialName = null;

                    if (meshData.Submeshes != null &&
                        meshData.Submeshes.Count > 0)
                    {
                        RuntimeObjSubmeshData submesh =
                            meshData.Submeshes[i];

                        if (submesh == null ||
                            string.IsNullOrWhiteSpace(
                                submesh.MaterialName))
                        {
                            error =
                                "Nom de matériau invalide pour "
                                + "le sous-mesh " + i;

                            return false;
                        }

                        materialName = submesh.MaterialName;
                    }

                    if (!RuntimeObjMaterialFactory.TryCreate(
                        meshData.ModelName,
                        materialName,
                        out materials[i],
                        out error))
                    {
                        error =
                            "Impossible de créer le matériau "
                            + (materialName ?? "Default")
                            + " du modèle "
                            + meshData.ModelName
                            + " : "
                            + error;

                        return false;
                    }

                    if (materials[i] == null)
                    {
                        error =
                            "Le matériau du sous-mesh "
                            + i
                            + " est inexistant.";

                        return false;
                    }
                }

                // Création de l'objet Unity.
                instance = new GameObject(
                    "FusionDarkRP_Object_" + meshData.ModelName);

                instance.SetActive(false);

                MeshFilter filter =
                    instance.AddComponent<MeshFilter>();

                MeshRenderer renderer =
                    instance.AddComponent<MeshRenderer>();

                if (filter == null || renderer == null)
                {
                    error =
                        "Impossible de créer les composants graphiques.";

                    return false;
                }

                filter.sharedMesh = mesh;

                // L'ordre des matériaux correspond exactement
                // à l'ordre des sous-meshes construits.
                renderer.sharedMaterials = materials;

                renderer.enabled = true;

                renderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.On;

                renderer.receiveShadows = true;
                renderer.lightProbeUsage =
                    UnityEngine.Rendering.LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage =
                    UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;

                // Collider basé sur la géométrie complète.
                MeshCollider collider =
                    instance.AddComponent<MeshCollider>();

                if (collider == null)
                {
                    error =
                        "Impossible de créer le collider du modèle.";

                    return false;
                }

                collider.sharedMesh = mesh;
                collider.convex = enablePhysics;

                if (enablePhysics)
                {
                    Rigidbody body =
                        instance.AddComponent<Rigidbody>();

                    if (body == null)
                    {
                        error =
                            "Impossible de créer le Rigidbody.";

                        return false;
                    }

                    body.useGravity = true;
                    body.isKinematic = false;

                    body.interpolation =
                        RigidbodyInterpolation.Interpolate;

                    body.collisionDetectionMode =
                        CollisionDetectionMode.ContinuousSpeculative;
                }

                instance.name =
                    "FusionDarkRP_" + meshData.ModelName;

                // Activation après configuration complète.
                instance.SetActive(true);

                createdObject = instance;
                ownershipTransferred = true;

                Debug.Log(
                    "[FusionDarkRP Objects] Objet créé : "
                    + meshData.ModelName
                    + " | Sommets : "
                    + mesh.vertexCount
                    + " | Sous-meshes : "
                    + mesh.subMeshCount
                    + " | Matériaux : "
                    + materials.Length);

                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Création impossible pour "
                    + modelName
                    + " : "
                    + exception.Message;

                createdObject = null;

                Debug.LogError(
                    "[FusionDarkRP Objects] " + error);

                return false;
            }
            finally
            {
                if (!ownershipTransferred)
                {
                    SafeDestroy(instance);
                    SafeDestroy(mesh);
                    DestroyMaterials(materials);
                }
            }
        }

        /// <summary>
        /// Détruit un objet et toutes ses ressources graphiques.
        /// </summary>
        public static void Destroy(GameObject target)
        {
            if (target == null)
                return;

            Mesh mesh = null;
            Material[] materials = null;

            try
            {
                MeshFilter filter =
                    target.GetComponent<MeshFilter>();

                MeshRenderer renderer =
                    target.GetComponent<MeshRenderer>();

                if (filter != null)
                    mesh = filter.sharedMesh;

                if (renderer != null)
                    materials = renderer.sharedMaterials;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[FusionDarkRP] Erreur de récupération "
                    + "des ressources : "
                    + exception.Message);
            }
            finally
            {
                SafeDestroy(target);
                SafeDestroy(mesh);
                DestroyMaterials(materials);
            }
        }

        /// <summary>
        /// Détruit tous les matériaux d'un objet.
        /// </summary>
        private static void DestroyMaterials(
            Material[] materials)
        {
            if (materials == null)
                return;

            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null)
                {
                    SafeDestroy(materials[i]);
                }
            }
        }

        /// <summary>
        /// Détruit une ressource Unity sans interrompre
        /// le nettoyage des autres ressources.
        /// </summary>
        private static void SafeDestroy(
            UnityEngine.Object target)
        {
            if (target == null)
                return;

            try
            {
                UnityEngine.Object.Destroy(target);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[FusionDarkRP] Impossible de libérer "
                    + "une ressource Unity : "
                    + exception.Message);
            }
        }

        public static string[] GetAvailableModels()
        {
            return RuntimeObjMeshLoader.GetAvailableModels();
        }
    }
}
