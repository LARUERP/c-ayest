using System;
using System.Collections.Generic;
using FusionDarkRP.Economy;
using Il2CppSLZ.Marrow.Pool;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Applique un modèle OBJ du catalogue sur un objet BONELAB réel
    /// (porteur saisissable, synchronisé par LabFusion).
    /// Les renderers d'origine sont masqués ; les colliders et grips
    /// d'origine sont conservés afin que la saisie reste native.
    /// Le visuel est centré sur l'emplacement visuel du porteur.
    /// </summary>
    public static class CarrierVisualAdapter
    {
        public const string VisualObjectName = "FusionDarkRP_CarrierVisual";

        private static readonly object Sync = new object();

        private static readonly List<Mesh> OwnedMeshes = new List<Mesh>();
        private static readonly List<Material> OwnedMaterials =
            new List<Material>();

        private sealed class TrackedRoot
        {
            public GameObject Root;
            public float Next;
            public float Step;
        }

        private static readonly List<TrackedRoot> Tracked =
            new List<TrackedRoot>();

        private static float nextTick;

        /// <summary>
        /// Racine à parcourir pour masquer le porteur : l'objet BONELAB
        /// complet (arme + chargeur + pièces), pas seulement le composant.
        /// </summary>
        private static GameObject ScanRoot(GameObject root)
        {
            try
            {
                Poolee poolee = root.GetComponentInParent<Poolee>();

                if (poolee != null && poolee.gameObject != null)
                    return poolee.gameObject;
            }
            catch
            {
                // Repli sur la racine fournie.
            }

            return root;
        }

        /// <summary>
        /// Masque tous les rendus du porteur d'origine (pistolet Eder 22,
        /// chargeur, culasse, effets). forceRenderingOff est utilisé en plus
        /// de enabled : le jeu réactive volontiers enabled (armes, LOD).
        /// </summary>
        private static void HideOriginals(GameObject root)
        {
            if (root == null)
                return;

            GameObject scan = ScanRoot(root);

            Renderer[] renderers =
                scan.GetComponentsInChildren<Renderer>(true);

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                GameObject owner = renderer.gameObject;

                if (owner == null ||
                    owner.name == VisualObjectName ||
                    owner.name.StartsWith("FusionDarkRP_"))
                {
                    continue;
                }

                if (renderer.enabled)
                    renderer.enabled = false;

                if (!renderer.forceRenderingOff)
                    renderer.forceRenderingOff = true;
            }
        }

        /// <summary>Garde le support (carte bancaire) masqué en continu.</summary>
        public static void KeepHidden(GameObject root)
        {
            Track(root);
        }

        private static void Track(GameObject root)
        {
            if (root == null)
                return;

            lock (Sync)
            {
                foreach (TrackedRoot tracked in Tracked)
                {
                    if (tracked.Root == root)
                        return;
                }

                try
                {
                    // Le porteur ne doit jamais tirer ni éjecter de douille.
                    Il2CppSLZ.Marrow.Gun gun =
                        ScanRoot(root)
                            .GetComponentInChildren<Il2CppSLZ.Marrow.Gun>(true);

                    if (gun != null)
                        gun.enabled = false;
                }
                catch
                {
                    // Sans conséquence : l'objet reste saisissable.
                }

                Tracked.Add(new TrackedRoot
                {
                    Root = root,
                    Next = Time.unscaledTime + 0.25f,
                    Step = 0.5f
                });
            }
        }

        /// <summary>
        /// À appeler à chaque image : ré-impose le masquage du porteur
        /// (intervalles croissants, 3 objets au plus par passage, afin de
        /// ne pas coûter de performances sur Quest).
        /// </summary>
        public static void Tick()
        {
            float now = Time.unscaledTime;

            if (now < nextTick)
                return;

            nextTick = now + 0.2f;

            List<TrackedRoot> due = null;

            lock (Sync)
            {
                for (int i = Tracked.Count - 1; i >= 0; i--)
                {
                    if (Tracked[i].Root == null)
                        Tracked.RemoveAt(i);
                }

                foreach (TrackedRoot tracked in Tracked)
                {
                    if (tracked.Next > now)
                        continue;

                    if (due == null)
                        due = new List<TrackedRoot>();

                    due.Add(tracked);

                    if (due.Count >= 3)
                        break;
                }
            }

            if (due == null)
                return;

            foreach (TrackedRoot tracked in due)
            {
                try
                {
                    HideOriginals(tracked.Root);
                }
                catch
                {
                    // Objet en cours de destruction.
                }

                tracked.Step = Mathf.Min(tracked.Step * 1.6f, 6f);
                tracked.Next = now + tracked.Step;
            }
        }

        public static bool HasVisual(GameObject root)
        {
            return root != null &&
                   root.transform.Find(VisualObjectName) != null;
        }

        public static bool TryApply(
            GameObject root,
            string modelId,
            float scale,
            out string error)
        {
            error = null;

            if (root == null)
            {
                error = "Objet porteur inexistant.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(modelId))
            {
                error = "Modèle manquant.";
                return false;
            }

            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f)
                scale = 1f;

            if (HasVisual(root))
            {
                Track(root);
                return true;
            }

            RuntimeObjMeshData meshData;

            if (!RuntimeObjMeshLoader.TryLoad(
                modelId, out meshData, out error))
            {
                return false;
            }

            if (meshData == null || !meshData.IsValid)
            {
                error = "Données OBJ invalides pour " + modelId + ".";
                return false;
            }

            Mesh mesh = null;
            List<Material> materials = new List<Material>();
            GameObject visual = null;
            bool done = false;

            try
            {
                if (!RuntimeObjUnityMeshBuilder.TryBuild(
                    meshData, out mesh, out error) || mesh == null)
                {
                    if (string.IsNullOrEmpty(error))
                        error = "Construction du mesh impossible.";

                    return false;
                }

                int count =
                    meshData.Submeshes != null && meshData.Submeshes.Count > 0
                        ? meshData.Submeshes.Count
                        : 1;

                if (mesh.subMeshCount != count)
                {
                    error = "Sous-meshes et matériaux incohérents.";
                    return false;
                }

                for (int i = 0; i < count; i++)
                {
                    string materialName = null;

                    if (meshData.Submeshes != null &&
                        meshData.Submeshes.Count > 0)
                    {
                        RuntimeObjSubmeshData submesh = meshData.Submeshes[i];

                        if (submesh == null ||
                            string.IsNullOrWhiteSpace(submesh.MaterialName))
                        {
                            error = "Matériau invalide (sous-mesh " + i + ").";
                            return false;
                        }

                        materialName = submesh.MaterialName;
                    }

                    Material material;

                    if (!RuntimeObjMaterialFactory.TryCreate(
                        meshData.ModelName,
                        materialName,
                        out material,
                        out error) || material == null)
                    {
                        if (string.IsNullOrEmpty(error))
                            error = "Matériau introuvable.";

                        return false;
                    }

                    materials.Add(material);
                }

                // Emplacement visuel du porteur (avant masquage).
                Renderer[] originals =
                    root.GetComponentsInChildren<Renderer>(true);

                bool hasBounds = false;
                Bounds originalBounds = new Bounds(
                    root.transform.position, Vector3.zero);

                foreach (Renderer original in originals)
                {
                    if (original == null)
                        continue;

                    if (!hasBounds)
                    {
                        originalBounds = original.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        originalBounds.Encapsulate(original.bounds);
                    }
                }

                visual = new GameObject(VisualObjectName);
                visual.SetActive(false);
                visual.transform.SetParent(root.transform, false);
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one * scale;

                Vector3 anchorLocal = hasBounds
                    ? root.transform.InverseTransformPoint(
                        originalBounds.center)
                    : Vector3.zero;

                visual.transform.localPosition =
                    anchorLocal - mesh.bounds.center * scale;

                MeshFilter filter = visual.AddComponent<MeshFilter>();
                MeshRenderer renderer = visual.AddComponent<MeshRenderer>();

                filter.sharedMesh = mesh;
                renderer.sharedMaterials = materials.ToArray();
                renderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage =
                    UnityEngine.Rendering.LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage =
                    UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;

                // Collider léger calé sur la géométrie réelle.
                BoxCollider box = visual.AddComponent<BoxCollider>();
                box.center = mesh.bounds.center;
                box.size = new Vector3(
                    Mathf.Max(mesh.bounds.size.x, 0.005f),
                    Mathf.Max(mesh.bounds.size.y, 0.005f),
                    Mathf.Max(mesh.bounds.size.z, 0.005f));

                MatchLayerToWeapon(root, visual);

                visual.SetActive(true);

                // Masque l'arme d'origine et tout ce qui l'accompagne,
                // puis surveille l'objet (le jeu peut réactiver des rendus).
                try
                {
                    HideOriginals(root);
                }
                catch
                {
                    // Le masquage est ré-imposé par Tick().
                }

                Track(root);

                lock (Sync)
                {
                    OwnedMeshes.Add(mesh);
                    OwnedMaterials.AddRange(materials);
                }

                done = true;
                return true;
            }
            catch (Exception exception)
            {
                error = "Application du visuel impossible : "
                    + exception.Message;

                return false;
            }
            finally
            {
                if (!done)
                {
                    if (visual != null)
                        UnityEngine.Object.Destroy(visual);

                    if (mesh != null)
                        UnityEngine.Object.Destroy(mesh);

                    foreach (Material material in materials)
                    {
                        if (material != null)
                            UnityEngine.Object.Destroy(material);
                    }
                }
            }
        }

        /// <summary>
        /// Donne au visuel (et à son collider) le même calque physique que les
        /// colliders d'origine de l'arme. Sur le calque par défaut, le collider
        /// ajouté entre en collision avec le corps physique du joueur dès que
        /// l'objet est rangé au holster : le personnage est poussé.
        /// </summary>
        public static void MatchLayerToWeapon(
            GameObject root, GameObject visual)
        {
            if (root == null || visual == null)
                return;

            try
            {
                int layer = root.layer;
                Collider[] colliders =
                    root.GetComponentsInChildren<Collider>(true);

                if (colliders != null)
                {
                    foreach (Collider candidate in colliders)
                    {
                        if (candidate == null ||
                            candidate.isTrigger ||
                            candidate.gameObject == visual ||
                            candidate.transform.IsChildOf(visual.transform))
                        {
                            continue;
                        }

                        layer = candidate.gameObject.layer;
                        break;
                    }
                }

                visual.layer = layer;
            }
            catch
            {
                // Le calque d'origine du visuel est conservé en cas d'échec.
            }
        }

        /// <summary>
        /// Libère les meshes et matériaux créés (arrêt du mod).
        /// </summary>
        public static void Cleanup()
        {
            lock (Sync)
            {
                foreach (Mesh mesh in OwnedMeshes)
                {
                    if (mesh != null)
                        UnityEngine.Object.Destroy(mesh);
                }

                foreach (Material material in OwnedMaterials)
                {
                    if (material != null)
                        UnityEngine.Object.Destroy(material);
                }

                OwnedMeshes.Clear();
                OwnedMaterials.Clear();
                Tracked.Clear();
            }
        }
    }
}
