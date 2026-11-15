using System;
using System.Collections.Generic;
using FusionDarkRP.Economy;
using LabFusion.Network;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Placement avec aperçu : le modèle est visible (clignotant) AVANT d'être
    /// posé et suit le regard de l'hôte (sol, table, mur). On valide ou on
    /// annule sur une touche, sans passer par le menu de réglages.
    ///
    /// Commandes (manette droite sauf indication) :
    ///   A  = poser l'objet        B = annuler
    ///   clic du stick gauche = tourner de 45°
    ///   clic du stick droit  = monter l'objet de 10 cm (3 clics puis retour à 0)
    ///
    /// Le fantôme n'a aucun collider et n'est visible que localement.
    /// La création réelle passe par WorldModelPlacementService.TrySpawnAtPose
    /// (même chemin hôte-autoritaire et synchronisé qu'avant).
    /// </summary>
    public static class PlacementPreview
    {
        private const string GhostName = "FusionDarkRP_PlacementGhost";
        private const float TimeoutSeconds = 180f;
        private const float InputGrace = 0.5f;

        private static readonly List<Material> GhostMaterials =
            new List<Material>();

        private static GameObject ghost;
        private static Mesh ghostMesh;
        private static MeshRenderer ghostRenderer;

        private static string activeModelId;
        private static string activeName;
        private static Action<string> placedCallback;

        private static float yawOffset;
        private static float heightOffset;
        private static float startTime;
        private static float notifyAt;

        public static bool IsActive
        {
            get { return ghost != null; }
        }

        // ------------------------------------------------------------
        // Démarrage / arrêt
        // ------------------------------------------------------------

        public static bool Begin(
            string modelId,
            Action<string> placed,
            out string error)
        {
            error = null;

            try
            {
                if (!IsHost())
                {
                    error = "Seul l'hôte peut placer les objets 3D.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(modelId))
                {
                    error = "Modèle invalide.";
                    return false;
                }

                WorldModelDefinition definition;

                if (!RuntimeObjWorldModelCatalog.TryGet(
                        modelId, out definition) || definition == null)
                {
                    error = "Modèle absent du catalogue : " + modelId;
                    return false;
                }

                Cancel(false);

                if (!BuildGhost(definition, out error))
                {
                    Destroy();
                    return false;
                }

                activeModelId = definition.Id;
                activeName = definition.DisplayName;
                placedCallback = placed;
                yawOffset = 0f;
                heightOffset = 0f;
                startTime = Time.unscaledTime;
                notifyAt = 0f;

                RpHub.Notify(
                    "Aperçu : " + activeName
                    + " — regarde où le poser. A = poser, B = annuler, "
                    + "clic stick gauche = tourner.",
                    0);

                MelonLogger.Msg(
                    "[FusionDarkRP Placement] Aperçu démarré : "
                    + definition.Id);

                return true;
            }
            catch (Exception exception)
            {
                error = "Aperçu impossible : " + exception.Message;
                Destroy();
                return false;
            }
        }

        public static void Cancel(bool announce)
        {
            bool wasActive = IsActive;

            Destroy();

            if (wasActive && announce)
                RpHub.Notify("Placement annulé.", 0);
        }

        private static void Destroy()
        {
            try
            {
                if (ghost != null)
                    UnityEngine.Object.Destroy(ghost);

                if (ghostMesh != null)
                    UnityEngine.Object.Destroy(ghostMesh);

                foreach (Material material in GhostMaterials)
                {
                    if (material != null)
                        UnityEngine.Object.Destroy(material);
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Placement] Nettoyage de l'aperçu : "
                    + exception.Message);
            }

            GhostMaterials.Clear();
            ghost = null;
            ghostMesh = null;
            ghostRenderer = null;
            placedCallback = null;
            activeModelId = null;
            activeName = null;
        }

        // ------------------------------------------------------------
        // Mise à jour (appelée à chaque image)
        // ------------------------------------------------------------

        public static void Tick()
        {
            if (ghost == null)
                return;

            try
            {
                if (!IsHost() ||
                    Time.unscaledTime - startTime > TimeoutSeconds)
                {
                    Cancel(true);
                    return;
                }

                Vector3 position;
                Quaternion rotation;

                if (!ComputePose(out position, out rotation))
                    return;

                Vector3 shownPosition = position;

                if (string.Equals(
                        activeModelId, "ATM",
                        StringComparison.OrdinalIgnoreCase))
                {
                    shownPosition +=
                        Vector3.up
                        * WorldModelPlacementService.AtmHeightOffset;
                }

                float blend = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);

                ghost.transform.position = Vector3.Lerp(
                    ghost.transform.position, shownPosition, blend);

                ghost.transform.rotation = Quaternion.Slerp(
                    ghost.transform.rotation, rotation, blend);

                if (ghostRenderer != null)
                {
                    // Clignotement : l'aperçu ne se confond pas avec un
                    // objet déjà posé.
                    ghostRenderer.enabled =
                        ((int)(Time.unscaledTime * 4f) % 4) != 0;
                }

                if (Time.unscaledTime - startTime < InputGrace)
                    return;

                ReadInput(position, rotation);
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Placement] Aperçu : "
                    + exception.Message);

                Cancel(false);
            }
        }

        private static void ReadInput(Vector3 position, Quaternion rotation)
        {
            var right = BoneLib.Player.RightController;
            var left = BoneLib.Player.LeftController;

            if (right != null && right.GetBButtonDown())
            {
                Cancel(true);
                return;
            }

            if (left != null && left.GetThumbStickDown())
                yawOffset = (yawOffset + 45f) % 360f;

            if (right != null && right.GetThumbStickDown())
            {
                heightOffset += 0.1f;

                if (heightOffset > 0.31f)
                    heightOffset = 0f;
            }

            if (right != null && right.GetAButtonDown())
                Confirm(position, rotation);
        }

        private static void Confirm(Vector3 position, Quaternion rotation)
        {
            string modelId = activeModelId;
            string name = activeName;
            Action<string> callback = placedCallback;

            string objectId;
            string error;

            bool ok = WorldModelPlacementService.TrySpawnAtPose(
                modelId, position, rotation, out objectId, out error);

            if (!ok)
            {
                RpHub.Notify(
                    "Placement impossible : "
                    + (string.IsNullOrWhiteSpace(error)
                        ? "erreur inconnue."
                        : error),
                    2);

                // L'aperçu reste actif : on peut réessayer ailleurs.
                return;
            }

            Destroy();

            RpHub.Notify(name + " posé.", 1);

            if (callback != null)
            {
                try
                {
                    callback(objectId);
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Placement] Retour de placement : "
                        + exception.Message);
                }
            }
        }

        // ------------------------------------------------------------
        // Calcul de la pose cible
        // ------------------------------------------------------------

        private static bool ComputePose(
            out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            Camera camera = Camera.main;

            if (camera == null || camera.transform == null)
                return false;

            Transform head = camera.transform;

            Vector3 flat = head.forward;
            flat.y = 0f;

            if (flat.sqrMagnitude < 0.0001f)
                flat = Vector3.forward;

            flat.Normalize();

            // Par défaut l'objet fait face au joueur.
            rotation = Quaternion.LookRotation(-flat, Vector3.up);

            Vector3 origin = head.position + head.forward * 0.3f;
            float reach = Mathf.Max(PlacementSettings.Distance, 6f);

            RaycastHit hit;

            if (Physics.Raycast(
                    origin,
                    head.forward,
                    out hit,
                    reach,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y >= 0.5f)
                {
                    position = hit.point + Vector3.up * heightOffset;
                }
                else
                {
                    Vector3 wallNormal = hit.normal;
                    wallNormal.y = 0f;

                    if (wallNormal.sqrMagnitude > 0.0001f)
                    {
                        wallNormal.Normalize();

                        position =
                            hit.point
                            + wallNormal * 0.05f
                            + Vector3.up * heightOffset;

                        rotation =
                            Quaternion.LookRotation(wallNormal, Vector3.up);
                    }
                    else
                    {
                        position = hit.point + Vector3.up * heightOffset;
                    }
                }
            }
            else
            {
                Vector3 point =
                    head.position + flat * PlacementSettings.Distance;

                RaycastHit ground;

                if (Physics.Raycast(
                        point + Vector3.up * 1.2f,
                        Vector3.down,
                        out ground,
                        4f,
                        ~0,
                        QueryTriggerInteraction.Ignore))
                {
                    point = ground.point;
                }
                else
                {
                    point.y = head.position.y - 1.6f;
                }

                position = point + Vector3.up * heightOffset;
            }

            rotation = rotation * Quaternion.Euler(
                0f, PlacementSettings.YawOffset + yawOffset, 0f);

            return IsFinite(position) && IsFinite(rotation);
        }

        // ------------------------------------------------------------
        // Construction du fantôme
        // ------------------------------------------------------------

        private static bool BuildGhost(
            WorldModelDefinition definition, out string error)
        {
            error = null;

            RuntimeObjMeshData meshData;

            if (!RuntimeObjMeshLoader.TryLoad(
                    definition.Id, out meshData, out error))
            {
                return false;
            }

            if (meshData == null || !meshData.IsValid)
            {
                error = "Données OBJ invalides pour " + definition.Id + ".";
                return false;
            }

            Mesh mesh;

            if (!RuntimeObjUnityMeshBuilder.TryBuild(
                    meshData, out mesh, out error) || mesh == null)
            {
                if (string.IsNullOrEmpty(error))
                    error = "Construction du mesh impossible.";

                return false;
            }

            ghostMesh = mesh;

            int count =
                meshData.Submeshes != null && meshData.Submeshes.Count > 0
                    ? meshData.Submeshes.Count
                    : 1;

            if (mesh.subMeshCount != count)
            {
                error = "Sous-meshes et matériaux incohérents.";
                return false;
            }

            List<Material> materials = new List<Material>();

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
                GhostMaterials.Add(material);
            }

            GameObject created = new GameObject(GhostName);

            MeshFilter filter = created.AddComponent<MeshFilter>();
            MeshRenderer renderer = created.AddComponent<MeshRenderer>();

            filter.sharedMesh = mesh;
            renderer.sharedMaterials = materials.ToArray();
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            created.transform.localScale = Vector3.one * definition.Scale;

            // Départ devant le joueur pour éviter un saut visuel.
            Camera camera = Camera.main;

            if (camera != null && camera.transform != null)
            {
                created.transform.position =
                    camera.transform.position
                    + camera.transform.forward * 1.5f;
            }

            ghost = created;
            ghostRenderer = renderer;

            return true;
        }

        // ------------------------------------------------------------
        // Utilitaires
        // ------------------------------------------------------------

        private static bool IsHost()
        {
            try
            {
                return NetworkInfo.HasServer && NetworkInfo.IsHost;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z)
                && !float.IsNaN(value.w) && !float.IsInfinity(value.w);
        }
    }
}
