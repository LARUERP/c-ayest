using System;
using LabFusion.Network;
using LabFusion.Player;
using MelonLoader;
using FusionDarkRP.Economy;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Gestion centralisée des sessions d'argent physique.
    /// L'autorité des opérations est exclusivement déterminée
    /// par LabFusion.
    /// </summary>
    public static class PhysicalCashSessionManager
    {
        private static readonly object Sync = new object();

        private static PhysicalCashSessionRegistry registry;

        public static bool IsInitialized
        {
            get
            {
                lock (Sync)
                {
                    return registry != null;
                }
            }
        }

        public static bool Initialize()
        {
            lock (Sync)
            {
                if (registry != null)
                    return true;

                EconomyRuntime runtime =
                    EconomyRuntimeBootstrap.Runtime;

                if (runtime == null)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Cash] Economie indisponible."
                    );

                    return false;
                }

                try
                {
                    registry = new PhysicalCashSessionRegistry(
                        runtime,
                        IsHost
                    );

                    MelonLogger.Msg(
                        "[FusionDarkRP Cash] Sessions physiques initialisees."
                    );

                    return true;
                }
                catch (Exception exception)
                {
                    registry = null;

                    MelonLogger.Error(
                        "[FusionDarkRP Cash] Initialisation impossible : "
                        + exception
                    );

                    return false;
                }
            }
        }

        public static bool TryGetSession(
            PlayerID player,
            out PlayerPhysicalCashSession session,
            out string error)
        {
            session = null;
            error = null;

            if (player == null || !player.IsValid)
            {
                error = "Joueur invalide.";
                return false;
            }

            lock (Sync)
            {
                if (registry == null && !Initialize())
                {
                    error =
                        "Gestionnaire d'argent physique indisponible.";

                    return false;
                }

                return registry.TryGetOrCreate(
                    player,
                    out session,
                    out error
                );
            }
        }

        /// <summary>
        /// Enregistre un billet ou une pièce dans le portefeuille.
        /// La transaction doit posséder un identifiant unique stable.
        /// </summary>
        public static PhysicalCashResult RegisterCash(
            PlayerID player,
            string requestId,
            string itemId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return PhysicalCashResult.Fail(
                    "Identifiant de creation manquant."
                );
            }

            if (!TryGetSession(
                player,
                out PlayerPhysicalCashSession session,
                out string error))
            {
                return PhysicalCashResult.Fail(error);
            }

            return session.RegisterCash(
                requestId,
                itemId
            );
        }

        /// <summary>
        /// Encaisse un billet physique.
        /// La validation finale reste effectuée par le portefeuille hôte.
        /// </summary>
        public static PhysicalCashResult RedeemCash(
            PlayerID player,
            string requestId,
            string cashId)
        {
            if (string.IsNullOrWhiteSpace(requestId) ||
                string.IsNullOrWhiteSpace(cashId))
            {
                return PhysicalCashResult.Fail(
                    "Informations d'encaissement invalides."
                );
            }

            if (!TryGetSession(
                player,
                out PlayerPhysicalCashSession session,
                out string error))
            {
                return PhysicalCashResult.Fail(error);
            }

            return session.RedeemCash(
                requestId,
                cashId
            );
        }

        public static void RemovePlayer(string persistentId)
        {
            if (string.IsNullOrWhiteSpace(persistentId))
                return;

            lock (Sync)
            {
                if (registry == null)
                    return;

                registry.Remove(persistentId);
            }
        }

        public static void Shutdown()
        {
            lock (Sync)
            {
                if (registry == null)
                    return;

                try
                {
                    registry.Clear();

                    MelonLogger.Msg(
                        "[FusionDarkRP Cash] Sessions fermees."
                    );
                }
                catch (Exception exception)
                {
                    MelonLogger.Error(
                        "[FusionDarkRP Cash] Erreur fermeture : "
                        + exception
                    );
                }
                finally
                {
                    registry = null;
                }
            }
        }

        /// <summary>
        /// Vérification directe de l'autorité LabFusion.
        /// En cas d'absence de serveur ou d'erreur, refuse l'opération.
        /// </summary>
        private static bool IsHost()
        {
            try
            {
                return NetworkInfo.HasServer &&
                       NetworkInfo.IsHost;
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Cash] Autorite indisponible : "
                    + exception.Message
                );

                return false;
            }
        }
    }
}
