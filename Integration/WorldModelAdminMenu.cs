
using System;
using System.Collections.Generic;
using FusionDarkRP.Economy;
using FusionDarkRP.Integration;
using LabFusion.Network;
using BoneLib.BoneMenu;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Catalogue d'objets 3D Fusion DarkRP intégré à BoneLib.
    /// Les créations et suppressions sont synchronisées depuis l'hôte.
    /// </summary>
    public static class WorldModelAdminMenu
    {
        private static Page rootPage;
        private static Page catalogPage;
        private static Page managementPage;

        private static Color white;
        private static Color yellow;
        private static Color green;
        private static Color red;
        private static Color cyan;

        private static bool initialized;

        private static readonly List<string> createdObjects =
            new List<string>();

        private static readonly object Sync = new object();

        public static bool IsInitialized
        {
            get
            {
                lock (Sync)
                    return initialized;
            }
        }

        public static void Initialize(Page mainPage)
        {
            lock (Sync)
            {
                if (initialized)
                    return;

                if (mainPage == null)
                    return;

                try
                {
                    rootPage = mainPage;

                    white = Color.white;
                    yellow = Color.yellow;
                    green = Color.green;
                    red = Color.red;
                    cyan = Color.cyan;

                    catalogPage = NewPage(
                        rootPage,
                        "Catalogue Fusion DarkRP",
                        yellow
                    );

                    if (catalogPage == null)
                        throw new InvalidOperationException(
                            "Impossible de créer le catalogue Fusion DarkRP."
                        );

                    AddInfo(
                        catalogPage,
                        "CATALOGUE D'OBJETS 3D",
                        yellow
                    );

                    AddInfo(
                        catalogPage,
                        "Banque, distributeurs et argent physique.",
                        white
                    );

                    AddInfo(
                        catalogPage,
                        "Placement libre : voir Réglages de placement.",
                        white
                    );

                    CreatePlacementPage(catalogPage);

                    AddInfo(
                        catalogPage,
                        "Les créations sont synchronisées avec Fusion.",
                        green
                    );

                    CreateCategoryPage(
                        catalogPage,
                        "Banque, distributeurs et cartes",
                        WorldModelCategory.BankMachine,
                        WorldModelCategory.BankFurniture,
                        WorldModelCategory.BankSecurity,
                        WorldModelCategory.BankStorage,
                        WorldModelCategory.BankCard
                    );

                    CreateCategoryPage(
                        catalogPage,
                        "Argent (billets, pièces, liasses)",
                        WorldModelCategory.CashNote,
                        WorldModelCategory.CashCoin,
                        WorldModelCategory.CashBundle
                    );

                    CreateCategoryPage(
                        catalogPage,
                        "Équipements des métiers",
                        WorldModelCategory.RpEquipment
                    );

                    managementPage = NewPage(
                        catalogPage,
                        "Gestion des objets",
                        cyan
                    );

                    if (managementPage == null)
                        throw new InvalidOperationException(
                            "Impossible de créer la page de gestion."
                        );

                    AddInfo(
                        managementPage,
                        "GESTION DES OBJETS FUSION DARKRP",
                        yellow
                    );

                    AddFunction(
                        managementPage,
                        "Actualiser les informations",
                        cyan,
                        RefreshCatalog
                    );

                    AddFunction(
                        managementPage,
                        "Diagnostic 3D des objets placés",
                        yellow,
                        DiagnoseSpawnedObjects
                    );

                    AddFunction(
                        managementPage,
                        "Supprimer le dernier objet créé",
                        red,
                        DestroyLastObject
                    );

                    AddFunction(
                        managementPage,
                        "Supprimer tous les objets de ce catalogue",
                        red,
                        DestroyAllObjects
                    );

                    AddInfo(
                        managementPage,
                        "Les autres objets du monde ne seront pas touchés.",
                        green
                    );

                    initialized = true;

                    MelonLogger.Msg(
                        "[FusionDarkRP World] Catalogue BoneLib initialise."
                    );

                    MelonLogger.Msg(
                        "[FusionDarkRP World] Categories : banque, "
                        + "cartes, billets, pieces et liasses."
                    );
                }
                catch (Exception exception)
                {
                    initialized = false;

                    MelonLogger.Error(
                        "[FusionDarkRP World] Initialisation du catalogue : "
                        + exception
                    );
                }
            }
        }

        public static void Shutdown()
        {
            lock (Sync)
            {
                createdObjects.Clear();

                rootPage = null;
                catalogPage = null;
                managementPage = null;



                initialized = false;
            }
        }

        private static void CreateCategoryPage(
            Page parent,
            string title,
            params WorldModelCategory[] categories)
        {
            Page page = NewPage(
                parent,
                title,
                cyan
            );

            if (page == null)
                throw new InvalidOperationException(
                    "Impossible de créer la catégorie : " + title
                );

            int count = 0;

            foreach (WorldModelDefinition definition
                in RuntimeObjWorldModelCatalog.GetAll())
            {
                if (definition == null ||
                    string.IsNullOrWhiteSpace(definition.Id) ||
                    !ContainsCategory(
                        definition.Category,
                        categories))
                    continue;

                string modelId = definition.Id;
                string displayName = definition.DisplayName;

                AddFunction(
                    page,
                    "Faire apparaître : " + displayName,
                    green,
                    () => SpawnModel(modelId, page)
                );

                count++;
            }

            AddInfo(
                page,
                "Objets disponibles : " + count,
                yellow
            );

            AddInfo(
                page,
                "Placement selon les Réglages de placement.",
                white
            );
        }

        private static void CreatePlacementPage(Page parent)
        {
            Page page = NewPage(parent, "Réglages de placement", cyan);

            if (page == null)
                return;

            AddFunction(page, "Mode : où je regarde (sol, table, mur)",
                green, () => SetMode(PlacementMode.Gaze));
            AddFunction(page, "Mode : devant moi",
                white, () => SetMode(PlacementMode.InFront));

            AddFunction(page, "Distance + 0,5 m", yellow,
                () => AdjustDistance(0.5f));
            AddFunction(page, "Distance - 0,5 m", yellow,
                () => AdjustDistance(-0.5f));

            AddFunction(page, "Hauteur + 10 cm", yellow,
                () => AdjustHeight(0.1f));
            AddFunction(page, "Hauteur - 10 cm", yellow,
                () => AdjustHeight(-0.1f));
            AddFunction(page, "Hauteur + 1 m (étage, comptoir)", yellow,
                () => AdjustHeight(1f));
            AddFunction(page, "Hauteur - 1 m", yellow,
                () => AdjustHeight(-1f));

            AddFunction(page, "Rotation + 15°", cyan,
                () => AdjustYaw(15f));
            AddFunction(page, "Rotation - 15°", cyan,
                () => AdjustYaw(-15f));
            AddFunction(page, "Rotation + 90°", cyan,
                () => AdjustYaw(90f));

            AddFunction(page, "Réinitialiser les réglages", red,
                ResetPlacement);
            AddFunction(page, "Afficher les réglages actuels (journal)",
                white, LogPlacement);

            AddInfo(page,
                "Les billets, pièces et liasses sont saisissables et se "
                + "déplacent à la main.",
                green);
        }

        private static void SetMode(PlacementMode mode)
        {
            PlacementSettings.Mode = mode;
            LogPlacement();
        }

        private static void AdjustDistance(float delta)
        {
            PlacementSettings.Distance =
                Mathf.Clamp(PlacementSettings.Distance + delta, 0.5f, 10f);
            LogPlacement();
        }

        private static void AdjustHeight(float delta)
        {
            PlacementSettings.Height =
                Mathf.Clamp(PlacementSettings.Height + delta, -2f, 5f);
            LogPlacement();
        }

        private static void AdjustYaw(float delta)
        {
            float yaw = (PlacementSettings.YawOffset + delta) % 360f;

            PlacementSettings.YawOffset = yaw;
            LogPlacement();
        }

        private static void ResetPlacement()
        {
            PlacementSettings.Mode = PlacementMode.Gaze;
            PlacementSettings.Distance = 2.5f;
            PlacementSettings.Height = 0f;
            PlacementSettings.YawOffset = 0f;
            LogPlacement();
        }

        private static void LogPlacement()
        {
            MelonLogger.Msg(
                "[FusionDarkRP Spawn] Réglages : "
                + PlacementSettings.Describe());
        }

        private static bool ContainsCategory(
            WorldModelCategory category,
            WorldModelCategory[] categories)
        {
            if (categories == null)
                return false;

            for (int i = 0; i < categories.Length; i++)
            {
                if (categories[i] == category)
                    return true;
            }

            return false;
        }

        private static void RefreshCatalog()
        {
            if (!initialized)
                return;

            if (!IsHost())
            {
                AddInfo(
                    managementPage,
                    "Consultation disponible, gestion réservée à l'hôte.",
                    red
                );

                return;
            }

            int count = 0;

            foreach (WorldModelDefinition definition
                in RuntimeObjWorldModelCatalog.GetAll())
            {
                if (definition != null)
                    count++;
            }

            AddInfo(
                managementPage,
                "Modèles déclarés : " + count,
                green
            );

            AddInfo(
                managementPage,
                "Objets actuellement placés : "
                + WorldModelRuntimeManager.SpawnedCount,
                cyan
            );

            AddInfo(
                managementPage,
                "Objets créés depuis ce catalogue : "
                + GetCreatedObjectCount(),
                white
            );

            MelonLogger.Msg(
                "[FusionDarkRP World] Catalogue actualise : "
                + count
                + " modeles."
            );
        }

        private static void DiagnoseSpawnedObjects()
        {
            if (!IsHost())
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Diagnostic] Diagnostic reserve a l'hote."
                );

                return;
            }

            string[] ids =
                WorldModelRuntimeManager.GetSpawnedObjectIds();

            MelonLogger.Msg(
                "[FusionDarkRP Diagnostic] Nombre d'objets : "
                + ids.Length
            );

            AddInfo(
                managementPage,
                "Diagnostic lancé : " + ids.Length + " objet(s).",
                cyan
            );

            if (ids.Length == 0)
            {
                AddInfo(
                    managementPage,
                    "DIAGNOSTIC : aucun objet enregistré.",
                    red
                );

                return;
            }

            foreach (string id in ids)
            {
                GameObject target;

                if (!WorldModelRuntimeManager.TryGet(
                    id,
                    out target) || target == null)
                {
                    MelonLogger.Error(
                        "[FusionDarkRP Diagnostic] Objet introuvable : "
                        + id
                    );

                    continue;
                }

                MeshFilter filter =
                    target.GetComponent<MeshFilter>();

                MeshRenderer renderer =
                    target.GetComponent<MeshRenderer>();

                MeshCollider collider =
                    target.GetComponent<MeshCollider>();

                Mesh mesh =
                    filter != null ? filter.sharedMesh : null;

                string modelId;
                WorldModelRuntimeManager.TryGetModelId(
                    id,
                    out modelId
                );

                string details =
                    "ID=" + id
                    + " | Modele=" + modelId
                    + " | Actif=" + target.activeSelf
                    + " | ActifHierarchy=" + target.activeInHierarchy
                    + " | Position=" + target.transform.position
                    + " | Echelle=" + target.transform.lossyScale
                    + " | Renderer=" + (renderer != null)
                    + " | RendererActive="
                    + (renderer != null && renderer.enabled)
                    + " | VisibleCamera="
                    + (renderer != null && renderer.isVisible)
                    + " | Collider=" + (collider != null);

                if (mesh != null)
                {
                    details +=
                        " | Sommets=" + mesh.vertexCount
                        + " | Bounds=" + mesh.bounds.size
                        + " | BoundsMonde="
                        + (renderer != null
                            ? renderer.bounds.size.ToString()
                            : "indisponible");
                }
                else
                {
                    details += " | MESH ABSENT";
                }

                if (renderer != null &&
                    renderer.sharedMaterial != null)
                {
                    details +=
                        " | Shader="
                        + renderer.sharedMaterial.shader.name;
                }
                else
                {
                    details += " | MATERIAU ABSENT";
                }

                MelonLogger.Msg(
                    "[FusionDarkRP Diagnostic] " + details
                );
            }

            MelonLogger.Msg(
                "[FusionDarkRP Diagnostic] Fin du diagnostic."
            );
        }

        private static int GetCreatedObjectCount()
        {
            lock (Sync)
                return createdObjects.Count;
        }

        private static void SpawnModel(
            string modelId,
            Page feedbackPage)
        {
            if (!IsHost())
            {
                ShowSpawnFeedback(
                    feedbackPage,
                    "ÉCHEC : l'hôte LabFusion n'est pas détecté.",
                    red
                );

                MelonLogger.Warning(
                    "[FusionDarkRP World] Apparition refusee : "
                    + "seul l'hote peut creer les objets."
                );

                return;
            }

            if (!initialized ||
                string.IsNullOrWhiteSpace(modelId))
            {
                ShowSpawnFeedback(
                    feedbackPage,
                    "ÉCHEC : catalogue ou identifiant de modèle invalide.",
                    red
                );

                MelonLogger.Warning(
                    "[FusionDarkRP World] Catalogue ou modele invalide."
                );

                return;
            }

            if (!RuntimeObjWorldModelCatalog.TryGet(
                modelId,
                out WorldModelDefinition definition) ||
                definition == null)
            {
                ShowSpawnFeedback(
                    feedbackPage,
                    "ÉCHEC : modèle absent du catalogue : " + modelId,
                    red
                );

                MelonLogger.Warning(
                    "[FusionDarkRP World] Modele inconnu : "
                    + modelId
                );

                return;
            }

            if (!WorldModelRuntimeManager.IsInitialized)
            {
                ShowSpawnFeedback(
                    feedbackPage,
                    "ÉCHEC : gestionnaire 3D non initialisé.",
                    red
                );

                MelonLogger.Warning(
                    "[FusionDarkRP World] Gestionnaire 3D indisponible."
                );

                return;
            }

            if (!string.Equals(
                    definition.Id, "BankCard",
                    StringComparison.OrdinalIgnoreCase))
            {
                string previewError;

                if (PlacementPreview.Begin(
                        definition.Id,
                        placedId =>
                        {
                            lock (Sync)
                            {
                                if (!createdObjects.Contains(placedId))
                                    createdObjects.Add(placedId);
                            }
                        },
                        out previewError))
                {
                    ShowSpawnFeedback(
                        feedbackPage,
                        "Aperçu actif : regarde où poser, A = poser, B = annuler.",
                        green
                    );

                    return;
                }

                ShowSpawnFeedback(
                    feedbackPage,
                    "Aperçu indisponible : " + previewError,
                    yellow
                );
            }

            string objectId;
            string error;

            bool success =
                WorldModelPlacementService.TrySpawnInFrontOfHost(
                    definition.Id,
                    out objectId,
                    out error
                );

            if (!success)
            {
                string failure = string.IsNullOrWhiteSpace(error)
                    ? "Erreur inconnue pendant la création."
                    : error;

                ShowSpawnFeedback(
                    feedbackPage,
                    "ÉCHEC SPAWN : " + failure,
                    red
                );

                MelonLogger.Error(
                    "[FusionDarkRP World] Apparition impossible : "
                    + failure
                    + " | Modèle : "
                    + modelId
                );

                return;
            }

            if (string.IsNullOrWhiteSpace(objectId))
            {
                ShowSpawnFeedback(
                    feedbackPage,
                    "ÉCHEC : aucun identifiant retourné après création.",
                    red
                );

                MelonLogger.Error(
                    "[FusionDarkRP World] Identifiant de creation absent."
                );

                return;
            }

            lock (Sync)
            {
                if (!createdObjects.Contains(objectId))
                    createdObjects.Add(objectId);
            }

            ShowSpawnFeedback(
                feedbackPage,
                "SUCCÈS : " + definition.DisplayName + " créé.",
                green
            );

            MelonLogger.Msg(
                "[FusionDarkRP World] Objet apparu : "
                + definition.DisplayName
                + " | ID : "
                + objectId
            );
        }

        private static void ShowSpawnFeedback(
            Page page,
            string message,
            Color color)
        {
            AddInfo(page, message, color);

            MelonLogger.Msg(
                "[FusionDarkRP Spawn] " + message
            );
        }

        private static void DestroyLastObject()
        {
            if (!IsHost())
            {
                MelonLogger.Warning(
                    "[FusionDarkRP World] Suppression reservee a l'hote."
                );

                return;
            }

            string objectId;

            lock (Sync)
            {
                if (createdObjects.Count == 0)
                {
                    MelonLogger.Msg(
                        "[FusionDarkRP World] Aucun objet a supprimer."
                    );

                    return;
                }

                objectId = createdObjects[
                    createdObjects.Count - 1
                ];
            }

            string error;

            if (!WorldModelRuntimeManager.TryDestroy(
                objectId,
                out error))
            {
                MelonLogger.Warning(
                    "[FusionDarkRP World] Suppression impossible : "
                    + error
                );

                return;
            }

            lock (Sync)
                createdObjects.Remove(objectId);

            if (!WorldModelNetwork.BroadcastDestroy(objectId))
            {
                MelonLogger.Warning(
                    "[FusionDarkRP World] Objet supprime localement, "
                    + "mais notification reseau echouee : "
                    + objectId
                );
            }

            MelonLogger.Msg(
                "[FusionDarkRP World] Dernier objet supprime."
            );
        }

        private static void DestroyAllObjects()
        {
            if (!IsHost())
            {
                MelonLogger.Warning(
                    "[FusionDarkRP World] Nettoyage reserve a l'hote."
                );

                return;
            }

            string[] ids;

            lock (Sync)
                ids = createdObjects.ToArray();

            if (ids.Length == 0)
            {
                MelonLogger.Msg(
                    "[FusionDarkRP World] Aucun objet cree par ce catalogue."
                );

                return;
            }

            int destroyed = 0;
            int networkFailures = 0;
            int localFailures = 0;

            for (int i = ids.Length - 1; i >= 0; i--)
            {
                string objectId = ids[i];

                if (string.IsNullOrWhiteSpace(objectId))
                {
                    lock (Sync)
                        createdObjects.Remove(objectId);

                    continue;
                }

                string error;

                if (!WorldModelRuntimeManager.TryDestroy(
                    objectId,
                    out error))
                {
                    localFailures++;

                    MelonLogger.Warning(
                        "[FusionDarkRP World] Suppression impossible : "
                        + objectId
                        + " | "
                        + error
                    );

                    continue;
                }

                destroyed++;

                lock (Sync)
                    createdObjects.Remove(objectId);

                if (!WorldModelNetwork.BroadcastDestroy(objectId))
                {
                    networkFailures++;

                    MelonLogger.Warning(
                        "[FusionDarkRP World] Notification de suppression "
                        + "echouee : "
                        + objectId
                    );
                }
            }

            MelonLogger.Msg(
                "[FusionDarkRP World] Nettoyage termine : "
                + destroyed
                + " supprime(s), "
                + localFailures
                + " echec(s) local(aux), "
                + networkFailures
                + " echec(s) reseau."
            );
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

        private static Page NewPage(Page parent, string name, Color color)
        {
            return MenuKit.NewPage(parent, name, color);
        }

        private static void AddInfo(Page page, string label, Color color)
        {
            MenuKit.AddInfo(page, label, color, "[Fusion DarkRP Modeles]");
        }

        private static void AddFunction(
            Page page, string label, Color color, Action action)
        {
            MenuKit.AddFunction(page, label, color, action);
        }
    }
}
