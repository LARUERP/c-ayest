
using System;
using FusionDarkRP.Economy;
using LabFusion.Network;
using LabFusion.Player;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Placement des modèles 3D et délivrance des cartes bancaires.
    /// </summary>
    public enum PlacementMode
    {
        InFront = 0,
        Gaze = 1
    }

    /// <summary>
    /// Réglages de placement modifiables depuis le menu hôte.
    /// </summary>
    public static class PlacementSettings
    {
        public static PlacementMode Mode = PlacementMode.Gaze;
        public static float Distance = 2.5f;
        public static float Height = 0f;
        public static float YawOffset = 0f;

        public static string Describe()
        {
            return (Mode == PlacementMode.Gaze
                    ? "Où je regarde"
                    : "Devant moi")
                + " | dist. " + Distance.ToString("0.0")
                + " m | hauteur " + Height.ToString("0.00")
                + " m | rotation " + YawOffset.ToString("0") + "°";
        }
    }

    public static class WorldModelPlacementService
    {
        private const float DefaultDistance = 2.5f;

        // Les modèles de mobilier et les distributeurs ont
        // leur base proche de Y = 0 dans les fichiers OBJ.
        // On les place donc au niveau du sol du rig.
        private const float DefaultHeight = 0f;

        public static bool TrySpawnInFrontOfHost(
            string modelId,
            out string objectId,
            out string error)
        {
            return TrySpawnInFrontOfHost(
                modelId,
                PlacementSettings.Distance,
                PlacementSettings.Height,
                out objectId,
                out error);
        }

        public static bool TrySpawnInFrontOfHost(
            string modelId,
            float distance,
            float height,
            out string objectId,
            out string error)
        {
            objectId = null;
            error = null;

            if (string.Equals(
                modelId,
                "BankCard",
                StringComparison.OrdinalIgnoreCase))
            {
                return TrySpawnBankCardForPlayer(
                    PlayerIDManager.LocalID,
                    PlayerIDManager.LocalID,
                    out objectId,
                    out error);
            }

            return TrySpawnInternal(
                modelId,
                distance,
                height,
                null,
                out objectId,
                out error);
        }

        public static bool TrySpawnBankCardForPlayer(
            PlayerID targetPlayer,
            out string objectId,
            out string error)
        {
            return TrySpawnBankCardForPlayer(
                targetPlayer,
                PlayerIDManager.LocalID,
                out objectId,
                out error);
        }

        public static bool TrySpawnBankCardForPlayer(
            PlayerID targetPlayer,
            PlayerID issuerPlayer,
            out string objectId,
            out string error)
        {
            objectId = null;
            error = null;

            if (!IsHost())
            {
                error = "Seul l'hôte peut délivrer une carte bancaire.";
                return false;
            }

            if (targetPlayer == null || !targetPlayer.IsValid)
            {
                error = "Joueur destinataire invalide.";
                return false;
            }

            if (issuerPlayer == null || !issuerPlayer.IsValid)
            {
                error = "Banquier émetteur invalide.";
                return false;
            }

            if (!EconomyRuntimeBootstrap.IsInitialized ||
                EconomyRuntimeBootstrap.Runtime == null ||
                EconomyRuntimeBootstrap.Runtime.BankCards == null)
            {
                error = "Le système bancaire n'est pas initialisé.";
                return false;
            }

            if (!BankCardHolsterMetadataNetwork.IsInitialized)
            {
                error = "La synchronisation des cartes n'est pas initialisée.";
                return false;
            }

            string ownerPersistentId;
            string issuerPersistentId;

            if (!PlayerIdentityService.TryGetPersistentId(
                    targetPlayer,
                    out ownerPersistentId) ||
                string.IsNullOrWhiteSpace(ownerPersistentId))
            {
                error = "Impossible d'identifier le destinataire.";
                return false;
            }

            if (!PlayerIdentityService.TryGetPersistentId(
                    issuerPlayer,
                    out issuerPersistentId) ||
                string.IsNullOrWhiteSpace(issuerPersistentId))
            {
                error = "Impossible d'identifier le banquier.";
                return false;
            }

            string cardId = Guid.NewGuid().ToString("N");

            BankCardService bankCards =
                EconomyRuntimeBootstrap.Runtime.BankCards;

            Func<bool> registerCard = () =>
            {
                string registrationError;

                bool registered = bankCards.IssueCard(
                    true,
                    cardId,
                    ownerPersistentId,
                    issuerPersistentId,
                    out registrationError);

                if (!registered)
                {
                    MelonLogger.Error(
                        "[FusionDarkRP Banque] Délivrance refusée : "
                        + registrationError);
                }

                return registered;
            };

            Action rollbackCard = () =>
            {
                string revokeError;

                bool revoked = bankCards.RevokeCard(
                    true,
                    cardId,
                    issuerPersistentId,
                    out revokeError);

                if (!revoked)
                {
                    MelonLogger.Error(
                        "[FusionDarkRP Banque] Révocation impossible : "
                        + revokeError);
                }
            };

            bool requested = BankCardHolsterSpawner.TrySpawnForPlayer(
                targetPlayer,
                cardId,
                registerCard,
                rollbackCard,
                out error);

            if (!requested)
                return false;

            objectId = cardId;

            MelonLogger.Msg(
                "[FusionDarkRP Banque] Création demandée pour "
                + ownerPersistentId
                + " | Émetteur : "
                + issuerPersistentId
                + " | Carte : "
                + cardId);

            return true;
        }

        private static bool TrySpawnInternal(
            string modelId,
            float distance,
            float height,
            PlayerID placementPlayer,
            out string objectId,
            out string error)
        {
            objectId = null;
            error = null;

            if (!IsHost())
            {
                error = "Seul l'hôte peut placer les objets 3D.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(modelId))
            {
                error = "Identifiant de modèle invalide.";
                return false;
            }

            if (string.Equals(
                modelId,
                "BankCard",
                StringComparison.OrdinalIgnoreCase))
            {
                error = "Une carte doit utiliser le holster natif.";
                return false;
            }

            if (!IsFinite(distance) ||
                distance < 0.5f ||
                distance > 10f)
            {
                error = "Distance de placement invalide.";
                return false;
            }

            if (!IsFinite(height) ||
                height < -2f ||
                height > 5f)
            {
                error = "Hauteur de placement invalide.";
                return false;
            }

            if (!WorldModelRuntimeManager.IsInitialized)
            {
                error = "Le gestionnaire 3D n'est pas initialisé.";
                return false;
            }

            string createdObjectId = null;
            bool objectCreated = false;

            try
            {
                if (!RuntimeObjWorldModelCatalog.TryGet(
                        modelId,
                        out WorldModelDefinition definition) ||
                    definition == null ||
                    string.IsNullOrWhiteSpace(definition.Id))
                {
                    error = "Modèle absent ou invalide dans le catalogue.";
                    return false;
                }

                PlayerID anchorPlayer =
                    placementPlayer ?? PlayerIDManager.LocalID;

                if (anchorPlayer == null || !anchorPlayer.IsValid)
                {
                    error = "Joueur de placement introuvable.";
                    return false;
                }

                // Récupération de la racine du rig.
                // Elle sert de référence pour le niveau du sol.
                Transform rig =
                    PhysicalCashRigAnchorResolver.ResolveRigRoot(
                        anchorPlayer);

                if (rig == null)
                {
                    error = "Impossible de trouver la racine du rig.";
                    return false;
                }

                // La caméra sert uniquement à déterminer
                // la direction et la position horizontale.
                Camera placementCamera = Camera.main;

                Vector3 anchorPosition;
                Vector3 forward;
                Vector3 gazeOrigin = Vector3.zero;
                Vector3 gazeDirection = Vector3.zero;
                bool hasGaze = false;

                if (placementCamera != null &&
                    placementCamera.transform != null)
                {
                    anchorPosition =
                        placementCamera.transform.position;

                    forward =
                        placementCamera.transform.forward;

                    gazeOrigin = placementCamera.transform.position;
                    gazeDirection = placementCamera.transform.forward;
                    hasGaze = true;

                    MelonLogger.Msg(
                        "[FusionDarkRP Placement] Caméra utilisée : "
                        + placementCamera.name
                        + " | Position : "
                        + anchorPosition);
                }
                else
                {
                    anchorPosition = rig.position;
                    forward = rig.forward;

                    MelonLogger.Warning(
                        "[FusionDarkRP Placement] Caméra introuvable. "
                        + "Utilisation de la racine du rig : "
                        + rig.position);
                }

                // Correction essentielle :
                // on garde X et Z de la caméra, mais on reprend
                // Y de la racine du rig pour éviter les objets
                // qui apparaissent au niveau de la tête.
                anchorPosition.y = rig.position.y;

                // Les objets restent droits et orientés
                // horizontalement devant le joueur.
                forward.y = 0f;

                if (forward.sqrMagnitude < 0.0001f)
                {
                    forward = rig.forward;
                    forward.y = 0f;
                }

                if (forward.sqrMagnitude < 0.0001f)
                {
                    error = "Orientation du joueur invalide.";
                    return false;
                }

                forward.Normalize();

                Vector3 position =
                    anchorPosition
                    + forward * distance
                    + Vector3.up * height;

                Quaternion rotation =
                    Quaternion.LookRotation(forward, Vector3.up);

                // Placement libre : l'objet se pose là où l'hôte regarde
                // (sol, table, étage, mur), pas seulement devant lui.
                if (PlacementSettings.Mode == PlacementMode.Gaze && hasGaze)
                {
                    RaycastHit hit;

                    if (Physics.Raycast(
                        gazeOrigin,
                        gazeDirection,
                        out hit,
                        Mathf.Max(distance, 10f),
                        ~0,
                        QueryTriggerInteraction.Ignore))
                    {
                        if (hit.normal.y >= 0.5f)
                        {
                            // Surface horizontale : on pose dessus.
                            position =
                                hit.point + Vector3.up * height;
                        }
                        else
                        {
                            // Surface verticale : on décolle du mur
                            // et on fait face à la pièce.
                            Vector3 wallNormal = hit.normal;
                            wallNormal.y = 0f;

                            if (wallNormal.sqrMagnitude > 0.0001f)
                            {
                                wallNormal.Normalize();

                                position =
                                    hit.point
                                    + wallNormal * 0.05f
                                    + Vector3.up * height;

                                rotation = Quaternion.LookRotation(
                                    wallNormal, Vector3.up);
                            }
                        }
                    }
                }

                rotation = rotation *
                    Quaternion.Euler(0f, PlacementSettings.YawOffset, 0f);

                if (!IsFinite(position.x) ||
                    !IsFinite(position.y) ||
                    !IsFinite(position.z))
                {
                    error = "Position de placement invalide.";
                    return false;
                }

                if (!IsFinite(rotation.x) ||
                    !IsFinite(rotation.y) ||
                    !IsFinite(rotation.z) ||
                    !IsFinite(rotation.w))
                {
                    error = "Rotation de placement invalide.";
                    return false;
                }

                MelonLogger.Msg(
                    "[FusionDarkRP Placement] Modèle : "
                    + definition.Id
                    + " | Ancre : "
                    + anchorPosition
                    + " | Racine rig : "
                    + rig.position
                    + " | Direction : "
                    + forward
                    + " | Position finale : "
                    + position);

                bool success = WorldModelRuntimeManager.TrySpawn(
                    definition.Id,
                    position,
                    rotation,
                    null,
                    out createdObjectId,
                    out error);

                if (!success)
                    return false;

                if (string.IsNullOrWhiteSpace(createdObjectId))
                {
                    error = "La création n'a retourné aucun identifiant.";
                    return false;
                }

                objectCreated = true;

                bool networkSent = WorldModelNetwork.BroadcastSpawn(
                    createdObjectId,
                    definition.Id,
                    position,
                    rotation);

                if (!networkSent)
                {
                    error =
                        "Synchronisation réseau impossible pour "
                        + definition.DisplayName
                        + ". La création a été annulée.";

                    CleanupCreatedObject(createdObjectId);

                    objectCreated = false;
                    createdObjectId = null;

                    return false;
                }

                objectId = createdObjectId;

                MelonLogger.Msg(
                    "[FusionDarkRP World] Modèle placé et synchronisé : "
                    + definition.Id
                    + " | ID : "
                    + createdObjectId);

                return true;
            }
            catch (Exception exception)
            {
                error = "Erreur de placement : " + exception.Message;

                if (objectCreated &&
                    !string.IsNullOrWhiteSpace(createdObjectId))
                {
                    CleanupCreatedObject(createdObjectId);
                }

                objectId = null;

                MelonLogger.Error(
                    "[FusionDarkRP World] " + exception);

                return false;
            }
        }

        /// <summary>
        /// Crée un objet à une pose précise (utilisé par l'aperçu de placement).
        /// Même chemin hôte-autoritaire et synchronisé que le placement classique.
        /// </summary>
        public const float AtmHeightOffset = 0.45f;

        public static bool TrySpawnAtPose(
            string modelId,
            Vector3 position,
            Quaternion rotation,
            out string objectId,
            out string error)
        {
            objectId = null;
            error = null;

            if (!IsHost())
            {
                error = "Seul l'hôte peut placer les objets 3D.";
                return false;
            }

            if (!WorldModelRuntimeManager.IsInitialized)
            {
                error = "Le gestionnaire 3D n'est pas initialisé.";
                return false;
            }

            WorldModelDefinition definition;

            if (!RuntimeObjWorldModelCatalog.TryGet(
                    modelId, out definition) ||
                definition == null ||
                string.IsNullOrWhiteSpace(definition.Id))
            {
                error = "Modèle absent ou invalide dans le catalogue.";
                return false;
            }

            string createdId;

            // Distributeur : posé sur son piédestal (45 cm sous l'origine).
            if (string.Equals(
                    definition.Id, "ATM", StringComparison.OrdinalIgnoreCase))
            {
                position += Vector3.up * AtmHeightOffset;
            }

            if (!WorldModelRuntimeManager.TrySpawn(
                    definition.Id, position, rotation, null,
                    out createdId, out error))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(createdId))
            {
                error = "La création n'a retourné aucun identifiant.";
                return false;
            }

            if (!WorldModelNetwork.BroadcastSpawn(
                    createdId, definition.Id, position, rotation))
            {
                error = "Synchronisation réseau impossible pour "
                    + definition.DisplayName + ".";

                CleanupCreatedObject(createdId);
                return false;
            }

            objectId = createdId;
            return true;
        }

        private static void CleanupCreatedObject(string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId))
                return;

            try
            {
                string destroyError;

                bool destroyed =
                    WorldModelRuntimeManager.TryDestroy(
                        objectId,
                        out destroyError);

                if (destroyed)
                {
                    WorldModelNetwork.BroadcastDestroy(objectId);
                }
                else
                {
                    MelonLogger.Error(
                        "[FusionDarkRP World] Suppression impossible : "
                        + destroyError);
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP World] Erreur de nettoyage : "
                    + exception);
            }
        }

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

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
