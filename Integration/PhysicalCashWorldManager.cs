using System;
using FusionDarkRP.Economy;
using LabFusion.Network;
using LabFusion.Player;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    public static class PhysicalCashWorldManager
    {
        private static readonly object Sync = new object();

        private static EconomyRuntime runtime;
        private static PhysicalCashSpawnRegistry ticketRegistry;
        private static PhysicalCashSpawnCoordinator coordinator;
        private static PhysicalCashWorldService worldService;
        private static RuntimeObjPhysicalCashWorldSpawner spawner;

        private static Func<PlayerID, Transform> anchorProvider;
        private static Transform activeSpawnAnchor;

        // Position de la sortie des billets sur le modèle ATM.obj.
        private static readonly Vector3 AtmCashOutputLocalPosition =
            new Vector3(0f, 0.12f, -0.22f);

        // Le spawner standard ajoute ces distances à son point d'ancrage.
        // On les compense pour placer le billet juste devant la sortie ATM.
        private const float StandardForwardDistance = 0.8f;
        private const float StandardUpwardOffset = 0.15f;

        // Petit dégagement devant la fente pour éviter que le billet
        // apparaisse à l'intérieur du modèle de l'ATM.
        private const float AtmCashForwardOffset = 0.12f;
        private const float AtmCashUpwardOffset = 0.03f;

        private const float MaximumAtmDistance = 3.5f;

        public static bool IsInitialized
        {
            get
            {
                lock (Sync)
                {
                    return runtime != null
                        && ticketRegistry != null
                        && coordinator != null
                        && worldService != null
                        && spawner != null;
                }
            }
        }

        public static bool HasSpawnAnchorProvider
        {
            get
            {
                lock (Sync)
                    return anchorProvider != null;
            }
        }

        public static bool Initialize(EconomyRuntime economyRuntime)
        {
            if (economyRuntime == null)
                return false;

            lock (Sync)
            {
                if (IsInitialized)
                    return true;

                try
                {
                    runtime = economyRuntime;
                    ticketRegistry = new PhysicalCashSpawnRegistry();

                    coordinator = new PhysicalCashSpawnCoordinator(
                        runtime.PhysicalCash,
                        ticketRegistry,
                        IsHost);

                    spawner = new RuntimeObjPhysicalCashWorldSpawner(
                        GetSpawnAnchor);

                    worldService = new PhysicalCashWorldService(
                        coordinator,
                        spawner,
                        IsHost);

                    MelonLogger.Msg(
                        "[FusionDarkRP Cash] Monde physique initialise.");

                    return true;
                }
                catch (Exception exception)
                {
                    ClearReferences();

                    MelonLogger.Error(
                        "[FusionDarkRP Cash] Initialisation impossible : "
                        + exception);

                    return false;
                }
            }
        }

        public static bool SetSpawnAnchorProvider(
            Func<PlayerID, Transform> provider)
        {
            if (provider == null)
                return false;

            lock (Sync)
            {
                if (!IsInitialized)
                    return false;

                anchorProvider = provider;
                return true;
            }
        }

        public static bool TryCreateAndSpawn(
            PlayerID player,
            string requestId,
            string itemId,
            out string cashId,
            out string spawnedObjectId,
            out string error)
        {
            return TryCreateAndSpawnWithAnchor(
                player,
                requestId,
                itemId,
                null,
                out cashId,
                out spawnedObjectId,
                out error);
        }

        public static bool TryCreateAndSpawnAtAnchor(
            PlayerID player,
            string requestId,
            string itemId,
            Transform spawnAnchor,
            out string cashId,
            out string spawnedObjectId,
            out string error)
        {
            if (spawnAnchor == null)
            {
                cashId = null;
                spawnedObjectId = null;
                error = "Point d'apparition physique invalide.";
                return false;
            }

            return TryCreateAndSpawnWithAnchor(
                player,
                requestId,
                itemId,
                spawnAnchor,
                out cashId,
                out spawnedObjectId,
                out error);
        }

        /// <summary>
        /// Recherche l'ATM le plus proche du joueur et fait apparaître
        /// le billet juste devant sa sortie basse.
        /// Cette méthode est exécutée uniquement par l'hôte.
        /// </summary>
        public static bool TryCreateAndSpawnAtNearestAtm(
            PlayerID player,
            string requestId,
            string itemId,
            out string cashId,
            out string spawnedObjectId,
            out string error)
        {
            cashId = null;
            spawnedObjectId = null;
            error = null;

            if (player == null || !player.IsValid)
            {
                error = "Joueur invalide.";
                return false;
            }

            if (!IsHost())
            {
                error = "Seul l'hôte peut distribuer les billets.";
                return false;
            }

            Func<PlayerID, Transform> currentAnchorProvider;

            lock (Sync)
                currentAnchorProvider = anchorProvider;

            if (currentAnchorProvider == null)
            {
                error = "Position du joueur indisponible.";
                return false;
            }

            Transform playerAnchor;

            try
            {
                playerAnchor = currentAnchorProvider(player);
            }
            catch (Exception exception)
            {
                error = "Lecture de la position du joueur impossible : "
                    + exception.Message;
                return false;
            }

            if (playerAnchor == null)
            {
                error = "Position réelle du joueur introuvable.";
                return false;
            }

            if (!WorldModelRuntimeManager.IsInitialized)
            {
                error = "Gestionnaire des ATM indisponible.";
                return false;
            }

            string[] objectIds =
                WorldModelRuntimeManager.GetSpawnedObjectIds();

            GameObject nearestAtm = null;
            float nearestDistance = float.MaxValue;

            if (objectIds != null)
            {
                foreach (string objectId in objectIds)
                {
                    if (string.IsNullOrWhiteSpace(objectId))
                        continue;

                    string modelId;

                    if (!WorldModelRuntimeManager.TryGetModelId(
                        objectId,
                        out modelId))
                        continue;

                    if (!string.Equals(
                        modelId,
                        "ATM",
                        StringComparison.OrdinalIgnoreCase))
                        continue;

                    GameObject atmObject;

                    if (!WorldModelRuntimeManager.TryGet(
                        objectId,
                        out atmObject))
                        continue;

                    if (atmObject == null)
                        continue;

                    float distance = Vector3.Distance(
                        playerAnchor.position,
                        atmObject.transform.position);

                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestAtm = atmObject;
                    }
                }
            }

            if (nearestAtm == null ||
                nearestDistance > MaximumAtmDistance)
            {
                error =
                    "Approchez-vous d'un distributeur pour retirer "
                    + "des billets.";
                return false;
            }

            GameObject temporaryAnchor = null;

            try
            {
                Transform atmTransform = nearestAtm.transform;

                Vector3 outputPosition =
                    atmTransform.TransformPoint(
                        AtmCashOutputLocalPosition);

                Quaternion outputRotation =
                    atmTransform.rotation;

                /*
                 * Le spawner standard ajoute :
                 * - 0,8 m vers l'avant ;
                 * - 0,15 m vers le haut.
                 *
                 * On compense ces deux déplacements ici.
                 * Le résultat final place donc le billet à environ
                 * 12 cm devant et 3 cm au-dessus de la sortie ATM.
                 *
                 * Cela ne modifie pas le placement normal des billets
                 * créés depuis le joueur.
                 */
                Vector3 compensatedAnchorPosition =
                    outputPosition
                    + atmTransform.forward * AtmCashForwardOffset
                    + atmTransform.up * AtmCashUpwardOffset
                    - atmTransform.forward * StandardForwardDistance
                    - atmTransform.up * StandardUpwardOffset;

                if (!IsFinite(outputPosition)
                    || !IsFinite(compensatedAnchorPosition)
                    || !IsFinite(outputRotation))
                {
                    error = "Position de sortie ATM invalide.";
                    return false;
                }

                temporaryAnchor = new GameObject(
                    "FusionDarkRP_ATM_CashOutput");

                temporaryAnchor.transform.position =
                    compensatedAnchorPosition;

                temporaryAnchor.transform.rotation =
                    outputRotation;

                MelonLogger.Msg(
                    "[FusionDarkRP ATM] Sortie de billets utilisee : "
                    + nearestAtm.name
                    + " | Distance : "
                    + nearestDistance.ToString("F2")
                    + " m"
                    + " | Position : "
                    + outputPosition);

                return TryCreateAndSpawnAtAnchor(
                    player,
                    requestId,
                    itemId,
                    temporaryAnchor.transform,
                    out cashId,
                    out spawnedObjectId,
                    out error);
            }
            catch (Exception exception)
            {
                cashId = null;
                spawnedObjectId = null;
                error = "Sortie ATM impossible : " + exception.Message;

                MelonLogger.Error(
                    "[FusionDarkRP ATM] " + error);

                return false;
            }
            finally
            {
                if (temporaryAnchor != null)
                    UnityEngine.Object.Destroy(temporaryAnchor);
            }
        }

        private static bool TryCreateAndSpawnWithAnchor(
            PlayerID player,
            string requestId,
            string itemId,
            Transform explicitAnchor,
            out string cashId,
            out string spawnedObjectId,
            out string error)
        {
            cashId = null;
            spawnedObjectId = null;
            error = null;

            if (player == null || !player.IsValid)
            {
                error = "Joueur invalide.";
                return false;
            }

            if (!IsHost())
            {
                error = "Seul l'hôte peut créer de l'argent physique.";
                return false;
            }

            if (!PlayerIdentityService.TryGetPersistentId(
                player,
                out string playerId))
            {
                error = "Identité persistante du joueur indisponible.";
                return false;
            }

            PhysicalCashSpawnCoordinator currentCoordinator;
            PhysicalCashWorldService currentWorldService;
            Func<PlayerID, Transform> currentAnchorProvider;

            lock (Sync)
            {
                if (!IsInitialized)
                {
                    error = "Gestionnaire du monde physique non initialisé.";
                    return false;
                }

                currentCoordinator = coordinator;
                currentWorldService = worldService;
                currentAnchorProvider = anchorProvider;
            }

            Transform anchor = explicitAnchor;

            if (anchor == null)
            {
                if (currentAnchorProvider == null)
                {
                    error = "Le rig du joueur n'est pas encore raccordé.";
                    return false;
                }

                try
                {
                    anchor = currentAnchorProvider(player);
                }
                catch (Exception exception)
                {
                    error = "Lecture du rig impossible : "
                        + exception.Message;
                    return false;
                }
            }

            if (anchor == null)
            {
                error = "Position réelle d'apparition introuvable.";
                return false;
            }

            PhysicalCashSpawnTicket ticket;

            if (!currentCoordinator.TryCreateTicket(
                playerId,
                requestId,
                itemId,
                out ticket,
                out error))
            {
                return false;
            }

            cashId = ticket.CashId;

            if (!PhysicalCashOwnershipTracker.TryRegister(
                ticket.CashId,
                playerId,
                out error))
            {
                string trackingError = error;
                string refundError;

                bool refunded = currentCoordinator.TryCancelAndRefund(
                    ticket.CashId,
                    playerId,
                    out refundError);

                if (refunded)
                {
                    cashId = null;
                    error = "Suivi du propriétaire impossible. "
                        + "Le billet a été remboursé. "
                        + trackingError;
                }
                else
                {
                    error = "Suivi du propriétaire et remboursement "
                        + "non confirmés. Identifiant conservé : "
                        + ticket.CashId
                        + ". Suivi : " + trackingError
                        + ". Remboursement : " + refundError;
                }

                return false;
            }

            string objectId;
            bool spawned;

            lock (Sync)
            {
                activeSpawnAnchor = anchor;

                try
                {
                    spawned = currentWorldService.TrySpawn(
                        ticket.CashId,
                        playerId,
                        out objectId,
                        out error);
                }
                finally
                {
                    activeSpawnAnchor = null;
                }
            }

            if (!spawned)
            {
                string spawnError = error;
                string refundError;

                bool refunded = currentCoordinator.TryCancelAndRefund(
                    ticket.CashId,
                    playerId,
                    out refundError);

                if (refunded)
                {
                    PhysicalCashOwnershipTracker.TryForget(
                        ticket.CashId,
                        playerId);

                    cashId = null;
                    error = "Apparition impossible. Billet remboursé. "
                        + spawnError;
                }
                else
                {
                    error = "Apparition impossible et remboursement "
                        + "non confirmé. Identifiant conservé : "
                        + ticket.CashId
                        + ". Apparition : " + spawnError
                        + ". Remboursement : " + refundError;
                }

                return false;
            }

            spawnedObjectId = objectId;

            bool metadataSent =
                PhysicalCashWorldModelMetadata.Send(
                    objectId,
                    ticket.CashId,
                    CommonMessageRoutes.ReliableToClients);

            if (!metadataSent)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Cash] Identifiant métier non diffusé "
                    + "aux clients pour le billet : "
                    + ticket.CashId);
            }

            return true;
        }

        public static bool TryTransferCash(
            PlayerID fromPlayer,
            PlayerID toPlayer,
            string requestId,
            string cashId,
            out PhysicalCashSpawnTicket updatedTicket,
            out string error)
        {
            updatedTicket = null;
            error = null;

            if (!IsHost())
            {
                error = "Seul l'hôte peut effectuer un transfert.";
                return false;
            }

            if (fromPlayer == null || !fromPlayer.IsValid ||
                toPlayer == null || !toPlayer.IsValid)
            {
                error = "Joueur expéditeur ou destinataire invalide.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(cashId))
            {
                error = "Identifiant du billet invalide.";
                return false;
            }

            if (!PlayerIdentityService.TryGetPersistentId(
                fromPlayer,
                out string fromPlayerId))
            {
                error = "Identité persistante de l'expéditeur indisponible.";
                return false;
            }

            if (!PlayerIdentityService.TryGetPersistentId(
                toPlayer,
                out string toPlayerId))
            {
                error = "Identité persistante du destinataire indisponible.";
                return false;
            }

            if (string.Equals(
                fromPlayerId,
                toPlayerId,
                StringComparison.Ordinal))
            {
                error = "Impossible de transférer un billet à soi-même.";
                return false;
            }

            PhysicalCashSpawnCoordinator currentCoordinator;

            lock (Sync)
            {
                if (!IsInitialized)
                {
                    error = "Gestionnaire de billets non initialisé.";
                    return false;
                }

                currentCoordinator = coordinator;
            }

            if (!IsHost())
            {
                error = "L'autorité de l'hôte a été perdue.";
                return false;
            }

            if (!PhysicalCashOwnershipTracker.TryChangeOwner(
                cashId,
                fromPlayerId,
                toPlayerId,
                out error))
            {
                return false;
            }

            if (currentCoordinator.TryTransferCash(
                requestId,
                fromPlayerId,
                toPlayerId,
                cashId,
                out updatedTicket,
                out error))
            {
                return true;
            }

            if (!PhysicalCashOwnershipTracker.TryChangeOwner(
                cashId,
                toPlayerId,
                fromPlayerId,
                out string rollbackError))
            {
                error = "Transfert économique échoué : "
                    + error
                    + ". Restauration du suivi impossible : "
                    + rollbackError;
            }

            updatedTicket = null;
            return false;
        }

        public static bool TryDestroy(
            string cashId,
            out string error)
        {
            error = null;

            if (!IsHost())
            {
                error = "Action réservée à l'hôte.";
                return false;
            }

            lock (Sync)
            {
                if (worldService == null)
                {
                    error = "Monde physique indisponible.";
                    return false;
                }

                return worldService.TryDestroy(cashId, out error);
            }
        }

        public static void Shutdown()
        {
            lock (Sync)
            {
                try
                {
                    if (spawner != null)
                        spawner.DestroyAll();

                    if (ticketRegistry != null)
                        ticketRegistry.Clear();

                    PhysicalCashOwnershipTracker.Clear();
                }
                catch (Exception exception)
                {
                    MelonLogger.Error(
                        "[FusionDarkRP Cash] Erreur fermeture : "
                        + exception);
                }
                finally
                {
                    ClearReferences();
                }
            }
        }

        private static Transform GetSpawnAnchor()
        {
            lock (Sync)
                return activeSpawnAnchor;
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

        private static void ClearReferences()
        {
            runtime = null;
            ticketRegistry = null;
            coordinator = null;
            worldService = null;
            spawner = null;
            anchorProvider = null;
            activeSpawnAnchor = null;
        }
    }
}
