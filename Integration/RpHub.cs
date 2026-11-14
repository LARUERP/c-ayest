using System;
using System.Collections.Generic;
using System.Text.Json;
using LabFusion.Network;
using LabFusion.Network.Serialization;
using LabFusion.Player;
using LabFusion.SDK.Modules;
using LabFusion.UI.Popups;
using LabFusion.Utilities;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    public sealed class RpPacket : INetSerializable
    {
        public byte[] Payload = Array.Empty<byte>();

        public int? GetSize()
        {
            return 4 + (Payload != null ? Payload.Length : 0);
        }

        public void Serialize(INetSerializer serializer)
        {
            serializer.SerializeValue(ref Payload);
        }
    }

    /// <summary>Requête client → hôte.</summary>
    public sealed class RpRequest
    {
        /// <summary>Type d'action (ex. "mission.start").</summary>
        public string K { get; set; }
        public long T { get; set; }
        /// <summary>Argument texte.</summary>
        public string A { get; set; }
        /// <summary>Argument numérique (montant en centimes...).</summary>
        public long N { get; set; }
        /// <summary>Joueur cible (SmallID), 255 = aucun.</summary>
        public byte S { get; set; } = 255;
    }

    /// <summary>Réponse ou notification hôte → client.</summary>
    public sealed class RpResponse
    {
        public string K { get; set; }
        /// <summary>Jeton de la requête, 0 pour une notification spontanée.</summary>
        public long T { get; set; }
        public bool Ok { get; set; }
        public string Msg { get; set; }
        /// <summary>Données JSON (listes).</summary>
        public string D { get; set; }
    }

    public sealed class RpRequestHandler : ModuleMessageHandler
    {
        public override ExpectedReceiverType ExpectedReceiver =>
            ExpectedReceiverType.ServerOnly;

        protected override bool OnPreRelayMessage(ReceivedMessage message)
        {
            return RpHub.SizeOk(message);
        }

        protected override void OnHandleMessage(ReceivedMessage message)
        {
            if (!NetworkInfo.IsHost || !message.Sender.HasValue)
                return;

            try
            {
                byte[] payload;

                if (!RpHub.TryRead(message, out payload))
                    return;

                PlayerID sender =
                    PlatformIds.GetPlayerID(message.Sender.Value);

                if (sender == null || !sender.IsValid)
                    return;

                RpHub.HandleHostRequest(
                    sender,
                    JsonSerializer.Deserialize<RpRequest>(payload));
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP RP] Erreur requête : " + exception);
            }
        }
    }

    public sealed class RpResponseHandler : ModuleMessageHandler
    {
        public override ExpectedReceiverType ExpectedReceiver =>
            ExpectedReceiverType.ClientsOnly;

        protected override bool OnPreRelayMessage(ReceivedMessage message)
        {
            return RpHub.SizeOk(message);
        }

        protected override void OnHandleMessage(ReceivedMessage message)
        {
            if (!NetworkInfo.HasServer ||
                NetworkInfo.IsHost ||
                message.IsServerHandled ||
                message.Sender != 0)
            {
                return;
            }

            try
            {
                byte[] payload;

                if (!RpHub.TryRead(message, out payload))
                    return;

                RpHub.DeliverToClient(
                    JsonSerializer.Deserialize<RpResponse>(payload));
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP RP] Erreur réponse : " + exception);
            }
        }
    }

    /// <summary>
    /// Canal unique et générique des services RP (missions, factures,
    /// amendes, recherchés, soins, administration). L'hôte est seul
    /// autorité : chaque action est validée côté hôte (métier, distance,
    /// cadence, montants) avant d'être exécutée.
    /// </summary>
    public static class RpHub
    {
        public const int MaximumPacketSize = 6144;
        private const float MinimumRequestInterval = 0.2f;

        public delegate RpResponse HostHandler(
            PlayerID sender, RpRequest request);

        private static readonly object Sync = new object();

        private static readonly Dictionary<string, HostHandler> Handlers =
            new Dictionary<string, HostHandler>(StringComparer.Ordinal);

        private static readonly Dictionary<byte, float> LastRequest =
            new Dictionary<byte, float>();

        private static bool initialized;
        private static long tokenCounter;

        /// <summary>Réponses et notifications reçues (côté client/hôte local).</summary>
        public static event Action<RpResponse> ResponseReceived;

        public static bool IsInitialized
        {
            get
            {
                lock (Sync)
                    return initialized;
            }
        }

        public static void Initialize()
        {
            lock (Sync)
            {
                if (initialized)
                    return;

                initialized = true;
            }

            try
            {
                RpServices.Initialize();

                MelonLogger.Msg(
                    "[FusionDarkRP RP] Services RP initialises ("
                    + Handlers.Count + " actions).");
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP RP] Initialisation : " + exception);
            }
        }

        public static void Shutdown()
        {
            try
            {
                RpServices.Shutdown();
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP RP] Arrêt : " + exception.Message);
            }

            lock (Sync)
            {
                Handlers.Clear();
                LastRequest.Clear();
                initialized = false;
            }

            ResponseReceived = null;
        }

        public static void RegisterHost(string kind, HostHandler handler)
        {
            if (string.IsNullOrWhiteSpace(kind) || handler == null)
                return;

            lock (Sync)
                Handlers[kind] = handler;
        }

        internal static bool SizeOk(ReceivedMessage message)
        {
            return message.Bytes != null
                && message.Bytes.Length > 0
                && message.Bytes.Length <= MaximumPacketSize + 4;
        }

        internal static bool TryRead(
            ReceivedMessage message, out byte[] payload)
        {
            payload = null;

            RpPacket packet = message.ReadData<RpPacket>();

            if (packet == null ||
                packet.Payload == null ||
                packet.Payload.Length == 0 ||
                packet.Payload.Length > MaximumPacketSize)
            {
                return false;
            }

            payload = packet.Payload;
            return true;
        }

        // ------------------------------------------------------------
        // Client
        // ------------------------------------------------------------

        /// <summary>
        /// Envoie une requête à l'hôte. Retourne le jeton, ou 0 en cas
        /// d'échec d'envoi.
        /// </summary>
        public static long Send(
            string kind, string argument = null,
            long number = 0, byte target = 255)
        {
            if (!NetworkInfo.HasServer || string.IsNullOrWhiteSpace(kind))
                return 0;

            PlayerID local = PlayerIDManager.LocalID;

            if (local == null || !local.IsValid)
                return 0;

            RpRequest request = new RpRequest
            {
                K = kind,
                T = ++tokenCounter,
                A = argument,
                N = number,
                S = target
            };

            try
            {
                if (NetworkInfo.IsHost)
                {
                    HandleHostRequest(local, request);
                    return request.T;
                }

                MessageRelay.RelayModule<RpRequestHandler, RpPacket>(
                    new RpPacket
                    {
                        Payload =
                            JsonSerializer.SerializeToUtf8Bytes(request)
                    },
                    CommonMessageRoutes.ReliableToServer);

                return request.T;
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP RP] Envoi impossible : " + exception);

                return 0;
            }
        }

        internal static void DeliverToClient(RpResponse response)
        {
            if (response == null)
                return;

            try
            {
                Action<RpResponse> callback = ResponseReceived;

                if (callback != null)
                    callback(response);
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP RP] Traitement réponse : " + exception);
            }
        }

        /// <summary>Notification locale (fenêtre LabFusion).</summary>
        public static void Notify(string text, byte level)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            try
            {
                Notifier.Send(new Notification
                {
                    Title = "Fusion DarkRP",
                    Message = text.Length > 200
                        ? text.Substring(0, 200)
                        : text,
                    Type = level >= 2
                        ? NotificationType.WARNING
                        : level == 1
                            ? NotificationType.SUCCESS
                            : NotificationType.INFORMATION,
                    PopupLength = 5f,
                    SaveToMenu = true
                });
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP RP] Notification : " + exception.Message);
            }
        }

        // ------------------------------------------------------------
        // Hôte
        // ------------------------------------------------------------

        internal static void HandleHostRequest(
            PlayerID sender, RpRequest request)
        {
            if (!NetworkInfo.HasServer ||
                !NetworkInfo.IsHost ||
                sender == null ||
                !sender.IsValid ||
                request == null ||
                string.IsNullOrWhiteSpace(request.K) ||
                request.K.Length > 48 ||
                request.T <= 0 ||
                request.N < 0 ||
                (request.A != null && request.A.Length > 160))
            {
                return;
            }

            float now = Time.realtimeSinceStartup;

            HostHandler handler;

            lock (Sync)
            {
                float last;

                if (LastRequest.TryGetValue(sender.SmallID, out last) &&
                    now - last < MinimumRequestInterval)
                {
                    Respond(sender, request, false,
                        "Trop de demandes, patientez.", null);
                    return;
                }

                LastRequest[sender.SmallID] = now;

                if (!Handlers.TryGetValue(request.K, out handler))
                    handler = null;
            }

            if (handler == null)
            {
                Respond(sender, request, false, "Action inconnue.", null);
                return;
            }

            RpResponse response;

            try
            {
                response = handler(sender, request);
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP RP] Action " + request.K
                    + " : " + exception);

                response = new RpResponse
                {
                    Ok = false,
                    Msg = "Erreur interne de l'hôte."
                };
            }

            if (response == null)
                return;

            response.K = request.K;
            response.T = request.T;

            Push(sender, response);
        }

        private static void Respond(
            PlayerID target, RpRequest request, bool ok,
            string message, string data)
        {
            Push(target, new RpResponse
            {
                K = request.K,
                T = request.T,
                Ok = ok,
                Msg = message,
                D = data
            });
        }

        /// <summary>Envoie une réponse ou notification à un joueur.</summary>
        public static void Push(PlayerID target, RpResponse response)
        {
            if (target == null || !target.IsValid || response == null)
                return;

            if (target.IsMe)
            {
                DeliverToClient(response);
                return;
            }

            try
            {
                byte[] payload =
                    JsonSerializer.SerializeToUtf8Bytes(response);

                if (payload.Length > MaximumPacketSize)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP RP] Réponse trop volumineuse ("
                        + response.K + ").");

                    return;
                }

                MessageRelay.RelayModule<RpResponseHandler, RpPacket>(
                    new RpPacket { Payload = payload },
                    new MessageRoute(
                        target.SmallID,
                        NetworkChannel.Reliable));
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP RP] Envoi vers joueur : "
                    + exception.Message);
            }
        }

        /// <summary>Notification spontanée hôte → joueur.</summary>
        public static void PushNotice(
            PlayerID target, string text, byte level)
        {
            Push(target, new RpResponse
            {
                K = "notify",
                T = 0,
                Ok = true,
                Msg = text,
                D = level.ToString()
            });
        }
    }
}
