using System;
using System.Collections;
using System.Collections.Generic;
using FusionDarkRP.Economy;
using LabFusion.Network;
using LabFusion.Player;
using MelonLoader;

namespace FusionDarkRP.Integration
{
    public sealed class EconomyPlayerTracker
    {
        private const double UpdateIntervalSeconds = 1.0;
        private const double SaveIntervalMinutes = 2.0;

        private readonly EconomyRuntime runtime;

        private readonly Dictionary<string, byte> connectedPlayers =
            new Dictionary<string, byte>(
                StringComparer.OrdinalIgnoreCase);

        // Collections réutilisées pour limiter les allocations mémoire.
        private readonly Dictionary<string, byte> currentlyConnected =
            new Dictionary<string, byte>(
                StringComparer.OrdinalIgnoreCase);

        private readonly List<KeyValuePair<string, byte>> disconnectedPlayers =
            new List<KeyValuePair<string, byte>>();

        private DateTime nextUpdateTime = DateTime.MinValue;
        private DateTime nextSaveTime =
            DateTime.UtcNow.AddMinutes(SaveIntervalMinutes);

        // Cache de la réflexion LabFusion.

        public EconomyPlayerTracker(EconomyRuntime runtime)
        {
            this.runtime = runtime
                ?? throw new ArgumentNullException(nameof(runtime));
        }

        public void Update()
        {
            DateTime now = DateTime.UtcNow;

            // Le suivi économique n'a pas besoin de tourner à chaque frame.
            if (now < nextUpdateTime)
                return;

            nextUpdateTime = now.AddSeconds(
                UpdateIntervalSeconds);

            if (!IsHost())
                return;

            try
            {
                IEnumerable players =
                    GetConnectedPlayerCollection();

                // Ne pas considérer tout le monde déconnecté si LabFusion
                // ne fournit temporairement pas sa collection.
                if (players != null)
                {
                    currentlyConnected.Clear();

                    foreach (object item in players)
                    {
                        PlayerID identity = item as PlayerID;

                        if (identity == null || !identity.IsValid)
                            continue;

                        if (!PlayerIdentityService.TryGetPersistentId(
                            identity,
                            out string persistentId) ||
                            string.IsNullOrWhiteSpace(persistentId))
                        {
                            continue;
                        }

                        if (!connectedPlayers.ContainsKey(persistentId))
                        {
                            if (!InitializePlayer(persistentId))
                            {
                                MelonLogger.Warning(
                                    "[FusionDarkRP] Initialisation a reessayer pour "
                                    + persistentId);

                                continue;
                            }
                        }

                        currentlyConnected[persistentId] =
                            identity.SmallID;

                        // Le service économique conserve sa propre cadence
                        // de versement et ses règles de métier/grade.
                        if (EconomyJobResolver.TryPaySalary(
                            runtime,
                            identity,
                            true,
                            out long amountPaid) &&
                            amountPaid > 0)
                        {
                            MelonLogger.Msg(
                                "[FusionDarkRP] Salaire verse : "
                                + persistentId
                                + " - "
                                + PhysicalCashCatalog.FormatMoney(
                                    amountPaid));
                        }
                    }

                    RemoveDisconnectedPlayers();
                    CopyCurrentPlayers();
                }

                if (now >= nextSaveTime)
                {
                    runtime.SaveAll();

                    nextSaveTime = now.AddMinutes(
                        SaveIntervalMinutes);

                    MelonLogger.Msg(
                        "[FusionDarkRP] Sauvegarde economique automatique.");
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP] Erreur suivi joueurs economiques : "
                    + exception);
            }
        }

        private void RemoveDisconnectedPlayers()
        {
            disconnectedPlayers.Clear();

            foreach (KeyValuePair<string, byte> oldPlayer
                in connectedPlayers)
            {
                if (!currentlyConnected.ContainsKey(oldPlayer.Key))
                    disconnectedPlayers.Add(oldPlayer);
            }

            for (int i = 0; i < disconnectedPlayers.Count; i++)
            {
                KeyValuePair<string, byte> player =
                    disconnectedPlayers[i];

                string playerId = player.Key;
                byte smallId = player.Value;

                try
                {
                    BankNetwork.RemovePlayer(playerId);
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP] Nettoyage banque : "
                        + exception.Message);
                }

                try
                {
                    BankCardIssueNetwork.RemovePlayer(smallId);
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP] Nettoyage carte bancaire : "
                        + exception.Message);
                }

                try
                {
                    PhysicalCashSessionManager.RemovePlayer(playerId);
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP] Nettoyage argent physique : "
                        + exception.Message);
                }

                try
                {
                    runtime.RemovePlayer(playerId);
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP] Nettoyage économie joueur : "
                        + exception.Message);
                }

                MelonLogger.Msg(
                    "[FusionDarkRP] Joueur deconnecte : "
                    + playerId);
            }
        }

        private void CopyCurrentPlayers()
        {
            connectedPlayers.Clear();

            foreach (KeyValuePair<string, byte> player
                in currentlyConnected)
            {
                connectedPlayers[player.Key] = player.Value;
            }
        }



      private static IEnumerable GetConnectedPlayerCollection()
        {
            try
            {
                return PlayerIDManager.PlayerIDs;
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP] Lecture de PlayerIDs impossible : "
                    + exception);

                return null;
            }
        }  private bool InitializePlayer(string persistentId)
        {
            try
            {
                WalletData wallet = runtime.InitializePlayer(
                    persistentId,
                    0);

                if (wallet == null)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP] Impossible de creer le compte : "
                        + persistentId);

                    return false;
                }

                MelonLogger.Msg(
                    "[FusionDarkRP] Compte joueur initialise : "
                    + persistentId);

                return true;
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP] Initialisation compte joueur : "
                    + exception);

                return false;
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

        public void Shutdown()
        {
            try
            {
                if (!IsHost())
                    return;

                foreach (KeyValuePair<string, byte> player
                    in connectedPlayers)
                {
                    try
                    {
                        BankNetwork.RemovePlayer(player.Key);
                        BankCardIssueNetwork.RemovePlayer(player.Value);
                        PhysicalCashSessionManager.RemovePlayer(player.Key);
                        runtime.RemovePlayer(player.Key);
                    }
                    catch (Exception exception)
                    {
                        MelonLogger.Warning(
                            "[FusionDarkRP] Nettoyage fermeture joueur : "
                            + exception.Message);
                    }
                }

                connectedPlayers.Clear();
                currentlyConnected.Clear();
                disconnectedPlayers.Clear();

                runtime.SaveAll();

                MelonLogger.Msg(
                    "[FusionDarkRP] Suivi economique ferme et sauvegarde.");
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP] Fermeture du suivi joueurs : "
                    + exception);
            }
        }




    }
}
