
using System;
using System.Collections.Generic;
using UnityEngine;
using FusionDarkRP.Economy;

namespace FusionDarkRP.Integration
{
    public static class BankCardWeaponVisualAdapter
    {
        private const string VisualObjectName = "FusionDarkRP_BankCardVisual";
        private const string ModelName = "BankCard";

        private sealed class AppliedVisual
        {
            public GameObject VisualObject;
            public Mesh Mesh;
            public Material Material;
            public readonly List<Renderer> HiddenRenderers = new List<Renderer>();
            public readonly List<bool> PreviousRendererStates = new List<bool>();
        }

        private static readonly Dictionary<int, AppliedVisual> AppliedObjects =
            new Dictionary<int, AppliedVisual>();

        public static bool TryApply(GameObject supportRoot, out string error)
        {
            return TryApply(
                supportRoot,
                Vector3.zero,
                Quaternion.identity,
                Vector3.one,
                out error
            );
        }

        public static bool TryApply(
            GameObject supportRoot,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale,
            out string error)
        {
            error = null;

            if (supportRoot == null)
            {
                error = "Objet support inexistant.";
                return false;
            }

            if (!IsFinite(localPosition) ||
                !IsFinite(localScale) ||
                !IsFinite(localRotation))
            {
                error = "Transform de la carte invalide.";
                return false;
            }

            int instanceId = supportRoot.GetInstanceID();

            if (AppliedObjects.ContainsKey(instanceId))
            {
                error = "La carte est déjà appliquée à cet objet.";
                return false;
            }

            if (supportRoot.transform.Find(VisualObjectName) != null)
            {
                error = "Un visuel de carte existe déjà sur ce support.";
                return false;
            }

            RuntimeObjMeshData meshData;

            if (!RuntimeObjMeshLoader.TryLoad(ModelName, out meshData, out error))
                return false;

            Mesh mesh = null;
            Material material = null;
            GameObject visualObject = null;
            AppliedVisual applied = null;

            try
            {
                if (!RuntimeObjUnityMeshBuilder.TryBuild(
                    meshData,
                    out mesh,
                    out error))
                {
                    return false;
                }

                if (mesh == null)
                {
                    error = "Le mesh de la carte est vide.";
                    return false;
                }

                if (!RuntimeObjMaterialFactory.TryCreate(
                    ModelName,
                    out material,
                    out error))
                {
                    return false;
                }

                if (material == null)
                {
                    error = "Le matériau de la carte est invalide.";
                    return false;
                }

                visualObject = new GameObject(VisualObjectName);
                visualObject.SetActive(false);

                visualObject.transform.SetParent(supportRoot.transform, false);
                visualObject.transform.localPosition = localPosition;
                visualObject.transform.localRotation = localRotation;
                visualObject.transform.localScale = localScale;

                MeshFilter filter = visualObject.AddComponent<MeshFilter>();
                MeshRenderer meshRenderer = visualObject.AddComponent<MeshRenderer>();

                if (filter == null || meshRenderer == null)
                {
                    error = "Impossible de créer les composants visuels.";
                    return false;
                }

                filter.sharedMesh = mesh;
                meshRenderer.sharedMaterial = material;
                meshRenderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.On;
                meshRenderer.receiveShadows = true;

                BoxCollider cardCollider = visualObject.AddComponent<BoxCollider>();

                if (cardCollider != null)
                    cardCollider.isTrigger = true;

                if (cardCollider == null)
                {
                    error = "Impossible de créer le collider de la carte.";
                    return false;
                }

                // Dimensions approximatives de BankCard.obj :
                // 85,6 x 54 x 0,9 mm.
                cardCollider.center = new Vector3(
                    0.0428f,
                    0.027f,
                    0.00045f
                );

                cardCollider.size = new Vector3(
                    0.0856f,
                    0.054f,
                    0.0009f
                );

                applied = new AppliedVisual
                {
                    VisualObject = visualObject,
                    Mesh = mesh,
                    Material = material
                };

                Renderer[] originalRenderers =
                    supportRoot.GetComponentsInChildren<Renderer>(true);

                foreach (Renderer original in originalRenderers)
                {
                    if (original == null ||
                        original.gameObject == visualObject)
                    {
                        continue;
                    }

                    applied.HiddenRenderers.Add(original);
                    applied.PreviousRendererStates.Add(original.enabled);
                }

                foreach (Renderer original in applied.HiddenRenderers)
                {
                    if (original != null)
                        original.enabled = false;
                }

                CarrierVisualAdapter.MatchLayerToWeapon(
                    supportRoot, visualObject);

                visualObject.SetActive(true);

                // Le pistolet d'origine ne doit plus jamais réapparaître.
                CarrierVisualAdapter.KeepHidden(supportRoot);

                AppliedObjects.Add(instanceId, applied);

                mesh = null;
                material = null;
                visualObject = null;
                applied = null;

                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Impossible d'appliquer l'apparence de la carte : " +
                    exception.Message;

                if (applied != null)
                    RestoreRenderers(applied);

                return false;
            }
            finally
            {
                if (visualObject != null)
                    UnityEngine.Object.Destroy(visualObject);

                if (mesh != null)
                    UnityEngine.Object.Destroy(mesh);

                if (material != null)
                    RuntimeObjMaterialFactory.Destroy(material);
            }
        }

        public static bool TryRemove(GameObject supportRoot, out string error)
        {
            error = null;

            if (supportRoot == null)
            {
                error = "Objet support inexistant.";
                return false;
            }

            int instanceId = supportRoot.GetInstanceID();
            AppliedVisual applied;

            if (!AppliedObjects.TryGetValue(instanceId, out applied))
            {
                error = "Aucune carte appliquée à cet objet.";
                return false;
            }

            try
            {
                RestoreRenderers(applied);

                if (applied.VisualObject != null)
                    UnityEngine.Object.Destroy(applied.VisualObject);

                if (applied.Mesh != null)
                    UnityEngine.Object.Destroy(applied.Mesh);

                if (applied.Material != null)
                    RuntimeObjMaterialFactory.Destroy(applied.Material);

                AppliedObjects.Remove(instanceId);
                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Erreur pendant le retrait de la carte : " +
                    exception.Message;

                return false;
            }
        }

        public static bool HasVisual(GameObject supportRoot)
        {
            return supportRoot != null &&
                   AppliedObjects.ContainsKey(supportRoot.GetInstanceID());
        }

        private static void RestoreRenderers(AppliedVisual applied)
        {
            if (applied == null)
                return;

            int count = Math.Min(
                applied.HiddenRenderers.Count,
                applied.PreviousRendererStates.Count
            );

            for (int i = 0; i < count; i++)
            {
                Renderer target = applied.HiddenRenderers[i];

                if (target != null)
                    target.enabled = applied.PreviousRendererStates[i];
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
