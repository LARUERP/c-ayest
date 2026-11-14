
using System;
using System.Collections.Generic;
using FusionDarkRP.Integration;
using LabFusion.Network;
using LabFusion.Network.Serialization;
using LabFusion.Player;
using LabFusion.SDK.Modules;
using LabFusion.Senders;
using MelonLoader;

namespace FusionDarkRP.Economy
{
    public sealed class BankCardIssueRequestData : INetSerializable
    {
        public byte TargetId;

        public int? GetSize()
        {
            return 1;
        }

        public void Serialize(INetSerializer serializer)
        {
            serializer.SerializeValue(ref TargetId);
        }
    }

    public sealed class BankCardIssueRequestHandler : ModuleMessageHandler
    {
        public override ExpectedReceiverType ExpectedReceiver =>
            ExpectedReceiverType.ServerOnly;

        protected override bool OnPreRelayMessage(ReceivedMessage received)
        {
            return received.Bytes != null &&
                   received.Bytes.Length == 1;
        }

        protected override void OnHandleMessage(ReceivedMessage received)
        {
            if (!NetworkInfo.IsHost || !received.Sender.HasValue)
                return;

            try
            {
                BankCardIssueRequestData data =
                    received.ReadData<BankCardIssueRequestData>();

                if (data == null)
                    return;

                PlayerID banker =
                    PlatformIds.GetPlayerID(received.Sender.Value);

                if (banker == null || !banker.IsValid)
                    return;

                BankCardIssueNetwork.HandleHostRequest(
                    banker,
                    data.TargetId
                );
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Banque] Erreur requête carte : "
                    + exception
                );
            }
        }
    }

    public static class BankCardIssueNetwork
    {
        private static readonly object Sync = new object();

        private static readonly Dictionary<byte, DateTime> LastRequests =
            new Dictionary<byte, DateTime>();

        private static readonly TimeSpan RequestCooldown =
            TimeSpan.FromSeconds(3);

        private static bool IsBanker(PlayerID player)
        {
            if (player == null || !player.IsValid)
                return false;

            string team = DarkRPNetwork.GetMetadata(
                player,
                DarkRPNetwork.TeamKey
            );

            string grade = DarkRPNetwork.GetMetadata(
                player,
                DarkRPNetwork.GradeKey
            );

            byte teamId = DarkRPJobs.TeamId(team);
            byte gradeId = DarkRPJobs.GradeId(grade);

            return teamId == 2 &&
                   (
                       gradeId == 4 ||
                       gradeId == 5 ||
                       gradeId == 6 ||
                       gradeId == 7 ||
                       gradeId == 32
                   );
        }

        /// <summary>
        /// Demande la délivrance d'une carte bancaire.
        /// Si le joueur est l'hôte, le traitement est immédiat.
        /// Sinon, la demande est envoyée au serveur.
        /// </summary>
        public static bool RequestIssue(byte targetSmallId)
        {
            try
            {
                if (!NetworkInfo.HasServer)
                    return false;

                if (!EconomyRuntimeBootstrap.IsInitialized ||
                    !BankNetwork.IsInitialized)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Banque] Système bancaire non initialisé."
                    );

                    return false;
                }

                BankCardIssueRequestData data =
                    new BankCardIssueRequestData
                    {
                        TargetId = targetSmallId
                    };

                if (NetworkInfo.IsHost)
                {
                    PlayerID local = PlayerIDManager.LocalID;

                    if (local == null || !local.IsValid)
                        return false;

                    HandleHostRequest(local, targetSmallId);
                    return true;
                }

                MessageRelay.RelayModule<
                    BankCardIssueRequestHandler,
                    BankCardIssueRequestData>(
                        data,
                        CommonMessageRoutes.ReliableToServer
                    );

                return true;
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Banque] Envoi de demande impossible : "
                    + exception
                );

                return false;
            }
        }

        internal static void HandleHostRequest(
            PlayerID banker,
            byte targetSmallId)
        {
            if (!NetworkInfo.IsHost ||
                !NetworkInfo.HasServer)
            {
                return;
            }

            if (banker == null || !banker.IsValid)
                return;

            if (!IsBanker(banker))
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Banque] Demande de carte refusée : "
                    + "expéditeur non autorisé."
                );

                return;
            }

            if (targetSmallId == banker.SmallID)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Banque] Un banquier ne peut pas "
                    + "se délivrer une carte via cette demande."
                );

                return;
            }

            PlayerID target =
                PlatformIds.GetPlayerID(targetSmallId);

            if (target == null || !target.IsValid)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Banque] Destinataire absent ou invalide."
                );

                return;
            }

            if (!CanSendRequest(banker.SmallID))
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Banque] Demande de carte trop rapide."
                );

                return;
            }

            string objectId;
            string error;

            bool success =
                WorldModelPlacementService.TrySpawnBankCardForPlayer(
                    target,
                    banker,
                    out objectId,
                    out error
                );

            if (!success)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Banque] Délivrance refusée : "
                    + (error ?? "Erreur inconnue.")
                );

                return;
            }

            MelonLogger.Msg(
                "[FusionDarkRP Banque] Carte délivrée par le banquier "
                + banker.SmallID
                + " au joueur "
                + target.SmallID
                + " | Objet : "
                + objectId
            );
        }

        private static bool CanSendRequest(byte bankerSmallId)
        {
            lock (Sync)
            {
                DateTime now = DateTime.UtcNow;
                DateTime lastRequest;

                if (LastRequests.TryGetValue(
                    bankerSmallId,
                    out lastRequest))
                {
                    if (now - lastRequest < RequestCooldown)
                        return false;
                }

                LastRequests[bankerSmallId] = now;

                return true;
            }
        }

        public static void RemovePlayer(byte smallId)
        {
            lock (Sync)
            {
                LastRequests.Remove(smallId);
            }
        }

        public static void Shutdown()
        {
            lock (Sync)
            {
                LastRequests.Clear();
            }
        }
    }
}
