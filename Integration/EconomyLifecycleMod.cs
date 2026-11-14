using System;
using System.Runtime.CompilerServices;
using FusionDarkRP.Economy;
using LabFusion.SDK.Modules;
using MelonLoader;

namespace FusionDarkRP.Integration
{
    public static class EconomyLifecycleMod
    {
        private static EconomyPlayerTracker playerTracker;
        private static bool initialized;
        private static bool fusionModuleRegistered;
        private static bool lateJoinSyncReady;
        private static bool worldInteractionsInitialized;

        public static bool IsInitialized
        {
            get { return initialized && playerTracker != null; }
        }

        public static void Initialize()
        {
            if (initialized)
                return;

            try
            {
                bool worldModelsReady =
                    WorldModelRuntimeManager.Initialize();

                if (worldModelsReady)
                {
                    MelonLogger.Msg(
                        "[FusionDarkRP World] Gestionnaire 3D initialise independamment."
                    );
                }
                else
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP World] Initialisation 3D en attente."
                    );
                }

                RegisterFusionModule();

                try
                {
                    WristCashDisplay.Initialize();

                    MelonLogger.Msg(
                        "[FusionDarkRP Wrist] Affichage du poignet initialisé indépendamment."
                    );
                }
                catch (Exception wristException)
                {
                    MelonLogger.Error(
                        "[FusionDarkRP Wrist] Erreur initialisation : "
                        + wristException
                    );
                }

                if (!EconomyRuntimeBootstrap.Initialize())
                {
                    MelonLogger.Error(
                        "[FusionDarkRP Economy] Initialisation impossible."
                    );
                    return;
                }

                EconomyRuntime runtime =
                    EconomyRuntimeBootstrap.Runtime;

                if (runtime == null)
                {
                    MelonLogger.Error(
                        "[FusionDarkRP Economy] Runtime indisponible."
                    );

                    EconomyRuntimeBootstrap.Shutdown();
                    return;
                }

                BankNetwork.Initialize(runtime);

                AtmNetwork.Shutdown();
                BankCardIssueNetwork.Shutdown();

                playerTracker = new EconomyPlayerTracker(runtime);
                initialized = true;

                // Première tentative. Si les modèles 3D ne sont pas
                // encore prêts, Update() réessaiera automatiquement.
                TryInitializeWorldInteractions();

                bool physicalCashReady =
                    PhysicalCashSessionManager.Initialize();

                if (!physicalCashReady)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Economy] Sessions d'argent physique non disponibles."
                    );
                }

                bool physicalWorldReady =
                    PhysicalCashWorldManager.Initialize(runtime);

                if (!physicalWorldReady)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Cash] Monde physique non disponible."
                    );
                }
                else
                {
                    bool anchorReady =
                        PhysicalCashWorldManager.SetSpawnAnchorProvider(
                            PhysicalCashRigAnchorResolver.ResolveBody
                        );

                    if (anchorReady)
                    {
                        MelonLogger.Msg(
                            "[FusionDarkRP Cash] Tête du joueur raccordée pour les apparitions physiques."
                        );
                    }
                    else
                    {
                        MelonLogger.Warning(
                            "[FusionDarkRP Cash] Résolveur de la racine du rig non raccordé."
                        );
                    }

                    bool grabDetectorReady =
                        PhysicalCashGrabDetector.Initialize();

                    if (grabDetectorReady)
                    {
                        MelonLogger.Msg(
                            "[FusionDarkRP Cash] Détecteur de saisie raccordé au cycle de vie."
                        );

                        bool pickupBridgeReady =
                            PhysicalCashGrabPickupBridge.Initialize();

                        if (pickupBridgeReady)
                        {
                            MelonLogger.Msg(
                                "[FusionDarkRP Cash] Liaison saisie et récupération activée."
                            );
                        }
                        else
                        {
                            MelonLogger.Warning(
                                "[FusionDarkRP Cash] Liaison de récupération indisponible."
                            );
                        }
                    }
                    else
                    {
                        MelonLogger.Warning(
                            "[FusionDarkRP Cash] Détecteur de saisie indisponible."
                        );
                    }
                }

                ValidateWorldModels();

                MelonLogger.Msg(
                    "[FusionDarkRP Economy] Gestionnaire initialise."
                );

                MelonLogger.Msg(
                    "[FusionDarkRP Banque] Reseau bancaire raccorde au runtime."
                );

                MelonLogger.Msg(
                    "[FusionDarkRP ATM] Réseau des distributeurs raccordé au runtime."
                );
            }
            catch (Exception exception)
            {
                playerTracker = null;
                initialized = false;
                worldInteractionsInitialized = false;
                lateJoinSyncReady = false;

                WristCashDisplay.Shutdown();

                PhysicalCashAtmDepositInteraction.Shutdown();

                PhysicalCashGrabPickupBridge.Shutdown();
                PhysicalCashGrabDetector.Shutdown();

                BankComputerPhysicalInteraction.Shutdown();
                AtmPhysicalInteraction.Shutdown();

                BankCardHolsterMetadataNetwork.Shutdown();

                WorldModelLateJoinSync.Shutdown();
                CarrierObjects.Shutdown();
                BankSecurity.Shutdown();

                PhysicalCashWorldManager.Shutdown();

                AtmNetwork.Shutdown();
                BankNetwork.Shutdown();
                BankCardIssueNetwork.Shutdown();

                PhysicalCashSessionManager.Shutdown();
                EconomyRuntimeBootstrap.Shutdown();

                MelonLogger.Error(
                    "[FusionDarkRP Economy] Erreur initialisation : "
                    + exception
                );
            }
        }

        /// <summary>
        /// Initialise les interactions physiques dès que le gestionnaire
        /// 3D est disponible. Les appels sont protégés contre les doublons.
        /// </summary>
        private static void TryInitializeWorldInteractions()
        {
            if (worldInteractionsInitialized)
                return;

            if (!WorldModelRuntimeManager.IsInitialized)
                return;

            try
            {
                BankCardHolsterMetadataNetwork.Initialize();

                WorldModelLateJoinSync.Initialize();
                CarrierObjects.Initialize();
                BankSecurity.Initialize();

                AtmPhysicalInteraction.Initialize();

                PhysicalCashAtmDepositInteraction.Initialize();

                BankComputerPhysicalInteraction.Initialize();

                bool cardReady =
                    BankCardHolsterMetadataNetwork.IsInitialized;

                bool lateJoinReady =
                    WorldModelLateJoinSync.IsInitialized;

                bool atmReady =
                    AtmPhysicalInteraction.IsInitialized;

                bool depositReady =
                    PhysicalCashAtmDepositInteraction.IsInitialized;

                bool bankComputerReady =
                    BankComputerPhysicalInteraction.IsInitialized;

                lateJoinSyncReady = lateJoinReady;

                worldInteractionsInitialized =
                    cardReady &&
                    lateJoinReady &&
                    atmReady &&
                    depositReady &&
                    bankComputerReady;

                if (worldInteractionsInitialized)
                {
                    MelonLogger.Msg(
                        "[FusionDarkRP World] Toutes les interactions physiques sont initialisées."
                    );

                    MelonLogger.Msg(
                        "[FusionDarkRP Card] Synchronisation des cartes holster raccordée."
                    );

                    MelonLogger.Msg(
                        "[FusionDarkRP World] Synchronisation des nouveaux joueurs raccordée."
                    );

                    MelonLogger.Msg(
                        "[FusionDarkRP ATM] Interfaces physiques et lecteurs de cartes actifs."
                    );

                    MelonLogger.Msg(
                        "[FusionDarkRP ATM] Détection des dépôts physiques activée."
                    );

                    MelonLogger.Msg(
                        "[FusionDarkRP Banque] Terminaux physiques bancaires actifs."
                    );
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP World] Initialisation des interactions physiques : "
                    + exception
                );
            }
        }

        private static void ValidateWorldModels()
        {
            try
            {
                string report;

                bool success =
                    RuntimeObjWorldModelValidator.ValidateAll(
                        out report
                    );

                if (!string.IsNullOrEmpty(report))
                    MelonLogger.Msg(report);

                if (!success)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP OBJ] Certains modèles nécessitent une correction."
                    );
                }
                else
                {
                    MelonLogger.Msg(
                        "[FusionDarkRP OBJ] Tous les modèles déclarés sont valides."
                    );
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP OBJ] Erreur du validateur : "
                    + exception
                );
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RegisterFusionModule()
        {
            if (fusionModuleRegistered)
                return;

            if (MelonBase.FindMelon("LabFusion", "Lakatrazz") == null)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP] LabFusion absent : module réseau non enregistré."
                );
                return;
            }

            ModuleManager.RegisterModule<FusionDarkRPModule>();

            fusionModuleRegistered = true;

            MelonLogger.Msg(
                "[FusionDarkRP] Module réseau enregistré dans LabFusion."
            );
        }

        public static void Update()
        {
            if (!WorldModelRuntimeManager.IsInitialized)
            {
                WorldModelRuntimeManager.Initialize();
            }

            // Important : si les modèles étaient indisponibles au
            // démarrage, on retente ici jusqu'à leur disponibilité.
            if (WorldModelRuntimeManager.IsInitialized)
            {
                TryInitializeWorldInteractions();
            }

            try
            {
                PlacementPreview.Tick();
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Placement] Aperçu : "
                    + exception.Message
                );
            }

            try
            {
                CarrierObjects.TickRemovals();
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Carrier] Retraits : "
                    + exception.Message
                );
            }

            try
            {
                CarrierVisualAdapter.Tick();
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Carrier] Masquage du porteur : "
                    + exception.Message
                );
            }

            try
            {
                WristCashDisplay.Update();
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Wrist] Erreur mise à jour : "
                    + exception
                );
            }

            if (!IsInitialized)
                return;

            try
            {
                playerTracker.Update();
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Economy] Erreur mise a jour : "
                    + exception
                );
            }

            try
            {
                AtmPhysicalInteraction.Update();
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP ATM] Erreur mise a jour physique : "
                    + exception
                );
            }

            try
            {
                PhysicalCashAtmDepositInteraction.Update();
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP ATM] Erreur détection dépôt : "
                    + exception
                );
            }

            try
            {
                BankSecurity.Update();
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Securite] Erreur mise a jour : "
                    + exception
                );
            }

            try
            {
                BankComputerPhysicalInteraction.Update();
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Banque] Erreur mise a jour des terminaux : "
                    + exception
                );
            }
        }

        public static void Shutdown()
        {
            if (!initialized)
            {
                WristCashDisplay.Shutdown();
                WorldModelRuntimeManager.Shutdown();
                return;
            }

            try
            {
                WristCashDisplay.Shutdown();

                PhysicalCashAtmDepositInteraction.Shutdown();

                PhysicalCashGrabPickupBridge.Shutdown();
                PhysicalCashGrabDetector.Shutdown();

                BankComputerPhysicalInteraction.Shutdown();
                AtmPhysicalInteraction.Shutdown();

                BankCardHolsterMetadataNetwork.Shutdown();

                WorldModelLateJoinSync.Shutdown();
                CarrierObjects.Shutdown();
                BankSecurity.Shutdown();

                WorldModelRuntimeManager.Shutdown();
                PhysicalCashWorldManager.Shutdown();
                PhysicalCashSessionManager.Shutdown();

                if (playerTracker != null)
                {
                    playerTracker.Shutdown();
                    playerTracker = null;
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Economy] Erreur fermeture : "
                    + exception
                );
            }
            finally
            {
                WristCashDisplay.Shutdown();

                PhysicalCashAtmDepositInteraction.Shutdown();

                PhysicalCashGrabPickupBridge.Shutdown();
                PhysicalCashGrabDetector.Shutdown();

                BankComputerPhysicalInteraction.Shutdown();
                AtmPhysicalInteraction.Shutdown();

                BankCardHolsterMetadataNetwork.Shutdown();

                WorldModelLateJoinSync.Shutdown();
                CarrierObjects.Shutdown();
                BankSecurity.Shutdown();

                WorldModelRuntimeManager.Shutdown();
                PhysicalCashWorldManager.Shutdown();

                AtmNetwork.Shutdown();
                BankNetwork.Shutdown();
                BankCardIssueNetwork.Shutdown();

                PhysicalCashSessionManager.Shutdown();
                EconomyRuntimeBootstrap.Shutdown();

                initialized = false;
                worldInteractionsInitialized = false;
                lateJoinSyncReady = false;

                MelonLogger.Msg(
                    "[FusionDarkRP World] Objets 3D nettoyes."
                );

                MelonLogger.Msg(
                    "[FusionDarkRP Banque] Reseau arrete."
                );

                MelonLogger.Msg(
                    "[FusionDarkRP ATM] Interfaces physiques et lecteurs arretes."
                );
            }
        }
    }
}
