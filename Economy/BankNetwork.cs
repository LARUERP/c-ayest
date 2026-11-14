
using System;
using System.Collections.Generic;
using System.Threading;
using FusionDarkRP.Integration;
using LabFusion.Network;
using LabFusion.Network.Serialization;
using LabFusion.Player;
using LabFusion.SDK.Modules;
using LabFusion.Senders;
using MelonLoader;

namespace FusionDarkRP.Economy
{
    public enum BankNetworkAction : byte
    {
        RequestConsent = 1,
        ResolveConsent = 2,
        RequestBalances = 3,
        RequestOperation = 4,
        ResolveOperation = 5
    }

    public enum BankNetworkResult : byte
    {
        ConsentRequested = 1,
        ConsentAccepted = 2,
        ConsentRefused = 3,
        BalancesAvailable = 4,
        OperationRequested = 5,
        OperationCompleted = 6,
        OperationRefused = 7,
        Failed = 255
    }

    public sealed class BankNetworkRequestData : INetSerializable
    {
        public byte Action;
        public byte TargetId;
        public long Token;
        public byte Operation;
        public long AmountCents;
        public byte Accepted;

        public int? GetSize() => 20;

        public void Serialize(INetSerializer serializer)
        {
            serializer.SerializeValue(ref Action);
            serializer.SerializeValue(ref TargetId);
            serializer.SerializeValue(ref Token);
            serializer.SerializeValue(ref Operation);
            serializer.SerializeValue(ref AmountCents);
            serializer.SerializeValue(ref Accepted);
        }
    }

    public sealed class BankNetworkResponseData : INetSerializable
    {
        public byte TargetId;
        public byte Result;
        public long Token;
        public byte Operation;
        public long AmountCents;
        public long CashCents;
        public long BankCents;
        public byte Success;

        public int? GetSize() => 36;

        public void Serialize(INetSerializer serializer)
        {
            serializer.SerializeValue(ref TargetId);
            serializer.SerializeValue(ref Result);
            serializer.SerializeValue(ref Token);
            serializer.SerializeValue(ref Operation);
            serializer.SerializeValue(ref AmountCents);
            serializer.SerializeValue(ref CashCents);
            serializer.SerializeValue(ref BankCents);
            serializer.SerializeValue(ref Success);
        }
    }

    public sealed class BankNetworkRequestHandler : ModuleMessageHandler
    {
        public override ExpectedReceiverType ExpectedReceiver =>
            ExpectedReceiverType.ServerOnly;

        protected override bool OnPreRelayMessage(ReceivedMessage received)
        {
            return received.Bytes != null &&
                   received.Bytes.Length == 20;
        }

        protected override void OnHandleMessage(ReceivedMessage received)
        {
            if (!NetworkInfo.IsHost || !received.Sender.HasValue)
                return;

            try
            {
                BankNetworkRequestData data =
                    received.ReadData<BankNetworkRequestData>();

                PlayerID sender =
                    PlatformIds.GetPlayerID(received.Sender.Value);

                if (sender == null || !sender.IsValid)
                    return;

                BankNetwork.HandleHostRequest(sender, data);
            }
            catch (Exception ex)
            {
                MelonLogger.Error(
                    "[Fusion DarkRP Banque] Erreur requête : " + ex);
            }
        }
    }

    public sealed class BankNetworkResponseHandler : ModuleMessageHandler
    {
        public override ExpectedReceiverType ExpectedReceiver =>
            ExpectedReceiverType.ClientsOnly;

        protected override bool OnPreRelayMessage(ReceivedMessage received)
        {
            return received.Bytes != null &&
                   received.Bytes.Length == 36;
        }

        protected override void OnHandleMessage(ReceivedMessage received)
        {
            try
            {
                BankNetworkResponseData data =
                    received.ReadData<BankNetworkResponseData>();

                PlayerID local = PlayerIDManager.LocalID;

                if (local == null || !local.IsValid ||
                    local.SmallID != data.TargetId)
                    return;

                BankNetwork.NotifyResponse(data);
            }
            catch (Exception ex)
            {
                MelonLogger.Error(
                    "[Fusion DarkRP Banque] Erreur réponse : " + ex);
            }
        }
    }

    public static class BankNetwork
    {
        private sealed class ConsentRoute
        {
            public string RequestId;
            public string BankerId;
            public string CustomerId;
            public byte BankerSmallId;
            public byte CustomerSmallId;
        }

        private sealed class OperationRoute
        {
            public string RequestId;
            public string BankerId;
            public string CustomerId;
            public long ConsentToken;
            public byte BankerSmallId;
            public byte CustomerSmallId;
            public byte Operation;
            public long AmountCents;
            public bool Resolving;
        }

        private static readonly object Sync = new object();

        private static EconomyRuntime runtime;
        private static BankConsentService consentService;

        private static long nextToken = 1000;

        private static readonly Dictionary<long, ConsentRoute>
            ConsentRoutes = new Dictionary<long, ConsentRoute>();

        private static readonly Dictionary<long, OperationRoute>
            OperationRoutes = new Dictionary<long, OperationRoute>();

        private static readonly TimeSpan RouteLifetime =
            TimeSpan.FromMinutes(2);

        private static readonly Dictionary<long, DateTime>
            ConsentRouteCreated = new Dictionary<long, DateTime>();

        private static readonly Dictionary<long, DateTime>
            OperationRouteCreated = new Dictionary<long, DateTime>();

        public static bool IsInitialized
        {
            get
            {
                lock (Sync)
                    return runtime != null && consentService != null;
            }
        }

        public static event Action<BankNetworkResponseData> ResponseReceived;

        public static void Initialize(EconomyRuntime economyRuntime)
        {
            if (economyRuntime == null)
                throw new ArgumentNullException(nameof(economyRuntime));

            lock (Sync)
            {
                runtime = economyRuntime;
                consentService = new BankConsentService();

                ConsentRoutes.Clear();
                OperationRoutes.Clear();
                ConsentRouteCreated.Clear();
                OperationRouteCreated.Clear();

                nextToken = 1000;
            }

            MelonLogger.Msg(
                "[Fusion DarkRP Banque] Réseau bancaire initialisé.");
        }

        public static void Shutdown()
        {
            lock (Sync)
            {
                runtime = null;
                consentService = null;

                ConsentRoutes.Clear();
                OperationRoutes.Clear();
                ConsentRouteCreated.Clear();
                OperationRouteCreated.Clear();
            }

            ResponseReceived = null;
        }

        private static long NewToken()
        {
            return Interlocked.Increment(ref nextToken);
        }

        private static bool IsBanker(PlayerID player)
        {
            if (player == null || !player.IsValid)
                return false;

            string team = DarkRPNetwork.GetMetadata(
                player, DarkRPNetwork.TeamKey);

            string grade = DarkRPNetwork.GetMetadata(
                player, DarkRPNetwork.GradeKey);

            byte teamId = DarkRPJobs.TeamId(team);
            byte gradeId = DarkRPJobs.GradeId(grade);

            return teamId == 2 &&
                   (gradeId == 4 ||
                    gradeId == 5 ||
                    gradeId == 6 ||
                    gradeId == 7 ||
                    gradeId == 32);
        }

        private static bool GetIdentity(
            PlayerID player,
            out string persistentId)
        {
            persistentId = null;

            return player != null &&
                   player.IsValid &&
                   PlayerIdentityService.TryGetPersistentId(
                       player, out persistentId) &&
                   !string.IsNullOrWhiteSpace(persistentId);
        }

        private static void SendRequest(BankNetworkRequestData data)
        {
            if (!NetworkInfo.HasServer || data == null)
                return;

            if (NetworkInfo.IsHost)
            {
                PlayerID local = PlayerIDManager.LocalID;

                if (local == null || !local.IsValid)
                    return;

                HandleHostRequest(local, data);
                return;
            }

            MessageRelay.RelayModule<
                BankNetworkRequestHandler,
                BankNetworkRequestData>(
                    data,
                    CommonMessageRoutes.ReliableToServer);
        }

        private static void SendResponse(
            byte targetId,
            BankNetworkResult result,
            long token,
            byte operation,
            long amount,
            long cash,
            long bank,
            bool success)
        {
            try
            {
                BankNetworkResponseData data =
                    new BankNetworkResponseData
                    {
                        TargetId = targetId,
                        Result = (byte)result,
                        Token = token,
                        Operation = operation,
                        AmountCents = amount,
                        CashCents = cash,
                        BankCents = bank,
                        Success = success ? (byte)1 : (byte)0
                    };

                PlayerID local = PlayerIDManager.LocalID;

                if (local != null && local.IsValid &&
                    local.SmallID == targetId)
                {
                    NotifyResponse(data);
                    return;
                }

                MessageRelay.RelayModule<
                    BankNetworkResponseHandler,
                    BankNetworkResponseData>(
                        data,
                        CommonMessageRoutes.ReliableToClients);
            }
            catch (Exception ex)
            {
                MelonLogger.Error(
                    "[Fusion DarkRP Banque] Envoi réponse impossible : " + ex);
            }
        }

        internal static void NotifyResponse(BankNetworkResponseData data)
        {
            if (data == null)
                return;

            Action<BankNetworkResponseData> callback = ResponseReceived;

            if (callback == null)
                return;

            try
            {
                callback(data);
            }
            catch (Exception ex)
            {
                MelonLogger.Error(
                    "[Fusion DarkRP Banque] Erreur interface : " + ex);
            }
        }

        // --------------------------------------------------
        // API CLIENT POUR L'INTERFACE BANCAIRE
        // --------------------------------------------------

        public static void RequestConsent(byte customerSmallId)
        {
            if (!IsInitialized)
                return;

            SendRequest(new BankNetworkRequestData
            {
                Action = (byte)BankNetworkAction.RequestConsent,
                TargetId = customerSmallId
            });
        }

        public static void ResolveConsent(long token, bool accepted)
        {
            if (!IsInitialized || token <= 0)
                return;

            SendRequest(new BankNetworkRequestData
            {
                Action = (byte)BankNetworkAction.ResolveConsent,
                Token = token,
                Accepted = accepted ? (byte)1 : (byte)0
            });
        }

        public static void RequestBalances(
            byte customerSmallId,
            long consentToken)
        {
            if (!IsInitialized || consentToken <= 0)
                return;

            SendRequest(new BankNetworkRequestData
            {
                Action = (byte)BankNetworkAction.RequestBalances,
                TargetId = customerSmallId,
                Token = consentToken
            });
        }

        public static void RequestOperation(
            byte customerSmallId,
            long consentToken,
            BankOperationType operation,
            long amountCents)
        {
            if (!IsInitialized ||
                consentToken <= 0 ||
                amountCents <= 0 ||
                (operation != BankOperationType.Deposit &&
                 operation != BankOperationType.Withdraw))
                return;

            SendRequest(new BankNetworkRequestData
            {
                Action = (byte)BankNetworkAction.RequestOperation,
                TargetId = customerSmallId,
                Token = consentToken,
                Operation = (byte)operation,
                AmountCents = amountCents
            });
        }

        public static void ResolveOperation(long token, bool accepted)
        {
            if (!IsInitialized || token <= 0)
                return;

            SendRequest(new BankNetworkRequestData
            {
                Action = (byte)BankNetworkAction.ResolveOperation,
                Token = token,
                Accepted = accepted ? (byte)1 : (byte)0
            });
        }

        // --------------------------------------------------
        // NETTOYAGE DES ROUTES EXPIRÉES
        // --------------------------------------------------

        private static void CleanupExpiredRoutes()
        {
            DateTime now = DateTime.UtcNow;

            lock (Sync)
            {
                List<long> expiredConsents = new List<long>();

                foreach (KeyValuePair<long, DateTime> item
                    in ConsentRouteCreated)
                {
                    if (now - item.Value > RouteLifetime)
                        expiredConsents.Add(item.Key);
                }

                foreach (long token in expiredConsents)
                {
                    ConsentRoutes.Remove(token);
                    ConsentRouteCreated.Remove(token);
                }

                List<long> expiredOperations = new List<long>();

                foreach (KeyValuePair<long, DateTime> item
                    in OperationRouteCreated)
                {
                    if (now - item.Value > RouteLifetime)
                        expiredOperations.Add(item.Key);
                }

                foreach (long token in expiredOperations)
                {
                    OperationRoutes.Remove(token);
                    OperationRouteCreated.Remove(token);
                }
            }
        }

        private static void AddConsentRoute(
            long token,
            ConsentRoute route)
        {
            lock (Sync)
            {
                ConsentRoutes[token] = route;
                ConsentRouteCreated[token] = DateTime.UtcNow;
            }
        }

        private static void AddOperationRoute(
            long token,
            OperationRoute route)
        {
            lock (Sync)
            {
                OperationRoutes[token] = route;
                OperationRouteCreated[token] = DateTime.UtcNow;
            }
        }

        private static void RemoveConsentRoute(long token)
        {
            lock (Sync)
            {
                ConsentRoutes.Remove(token);
                ConsentRouteCreated.Remove(token);
            }
        }

        private static void RemoveOperationRoute(long token)
        {
            lock (Sync)
            {
                OperationRoutes.Remove(token);
                OperationRouteCreated.Remove(token);
            }
        }

        // --------------------------------------------------
        // ROUTAGE ET VALIDATION CÔTÉ HÔTE
        // --------------------------------------------------

        internal static void HandleHostRequest(
            PlayerID sender,
            BankNetworkRequestData data)
        {
            if (!NetworkInfo.IsHost ||
                sender == null ||
                !sender.IsValid ||
                !IsInitialized ||
                data == null)
                return;

            CleanupExpiredRoutes();

            if (data.Token < 0 || data.AmountCents < 0)
                return;

            switch ((BankNetworkAction)data.Action)
            {
                case BankNetworkAction.RequestConsent:
                    HandleConsentRequest(sender, data);
                    break;

                case BankNetworkAction.ResolveConsent:
                    if (data.Accepted > 1)
                        return;

                    HandleConsentResolution(sender, data);
                    break;

                case BankNetworkAction.RequestBalances:
                    HandleBalanceRequest(sender, data);
                    break;

                case BankNetworkAction.RequestOperation:
                    HandleOperationRequest(sender, data);
                    break;

                case BankNetworkAction.ResolveOperation:
                    if (data.Accepted > 1)
                        return;

                    HandleOperationResolution(sender, data);
                    break;

                default:
                    return;
            }
        }

        private static bool IsCurrentPlayer(
            byte smallId,
            string expectedPersistentId,
            out PlayerID player)
        {
            player = PlatformIds.GetPlayerID(smallId);

            if (player == null || !player.IsValid ||
                string.IsNullOrWhiteSpace(expectedPersistentId))
                return false;

            string actualId;

            if (!GetIdentity(player, out actualId))
                return false;

            return string.Equals(
                actualId,
                expectedPersistentId,
                StringComparison.Ordinal);
        }

        private static void HandleConsentRequest(
            PlayerID sender,
            BankNetworkRequestData data)
        {
            if (!IsBanker(sender))
                return;

            PlayerID customer =
                PlatformIds.GetPlayerID(data.TargetId);

            if (customer == null ||
                !customer.IsValid ||
                customer.SmallID == sender.SmallID)
                return;

            string bankerId;
            string customerId;

            if (!GetIdentity(sender, out bankerId) ||
                !GetIdentity(customer, out customerId))
                return;

            BankConsentRequest request;
            string error;

            if (!consentService.CreateConsentRequest(
                bankerId,
                customerId,
                out request,
                out error))
            {
                SendResponse(
                    sender.SmallID,
                    BankNetworkResult.Failed,
                    0, 0, 0, 0, 0, false);
                return;
            }

            long token = NewToken();

            ConsentRoute route = new ConsentRoute
            {
                RequestId = request.RequestId,
                BankerId = bankerId,
                CustomerId = customerId,
                BankerSmallId = sender.SmallID,
                CustomerSmallId = customer.SmallID
            };

            AddConsentRoute(token, route);

            SendResponse(
                sender.SmallID,
                BankNetworkResult.ConsentRequested,
                token, 0, 0, 0, 0, true);

            SendResponse(
                customer.SmallID,
                BankNetworkResult.ConsentRequested,
                token, 0, 0, 0, 0, true);
        }

        private static void HandleConsentResolution(
            PlayerID sender,
            BankNetworkRequestData data)
        {
            if (data.Token <= 0)
                return;

            string customerId;

            if (!GetIdentity(sender, out customerId))
                return;

            ConsentRoute route;

            lock (Sync)
            {
                if (!ConsentRoutes.TryGetValue(data.Token, out route))
                    return;
            }

            if (route.CustomerId != customerId ||
                route.CustomerSmallId != sender.SmallID)
                return;

            string error;
            bool accepted = data.Accepted == 1;

            bool resolved = consentService.ResolveConsent(
                route.RequestId,
                customerId,
                accepted,
                out error);

            if (!resolved)
            {
                SendResponse(
                    sender.SmallID,
                    BankNetworkResult.Failed,
                    data.Token, 0, 0, 0, 0, false);
                return;
            }

            BankNetworkResult result = accepted
                ? BankNetworkResult.ConsentAccepted
                : BankNetworkResult.ConsentRefused;

            if (IsCurrentPlayer(
                route.BankerSmallId,
                route.BankerId,
                out PlayerID banker))
            {
                SendResponse(
                    banker.SmallID,
                    result,
                    data.Token, 0, 0, 0, 0, true);
            }

            SendResponse(
                sender.SmallID,
                result,
                data.Token, 0, 0, 0, 0, true);

            if (!accepted)
                RemoveConsentRoute(data.Token);
        }

        private static void HandleBalanceRequest(
            PlayerID sender,
            BankNetworkRequestData data)
        {
            if (!IsBanker(sender) || data.Token <= 0)
                return;

            string bankerId;

            if (!GetIdentity(sender, out bankerId))
                return;

            ConsentRoute route;

            lock (Sync)
            {
                if (!ConsentRoutes.TryGetValue(data.Token, out route))
                    return;
            }

            if (route.BankerId != bankerId ||
                route.BankerSmallId != sender.SmallID ||
                route.CustomerSmallId != data.TargetId)
                return;

            if (!IsCurrentPlayer(
                route.CustomerSmallId,
                route.CustomerId,
                out PlayerID customer))
                return;

            long cash;
            long bank;
            string error;

            if (!consentService.GetBalances(
                route.RequestId,
                bankerId,
                route.CustomerId,
                runtime,
                out cash,
                out bank,
                out error))
            {
                SendResponse(
                    sender.SmallID,
                    BankNetworkResult.Failed,
                    data.Token, 0, 0, 0, 0, false);
                return;
            }

            SendResponse(
                sender.SmallID,
                BankNetworkResult.BalancesAvailable,
                data.Token, 0, 0, cash, bank, true);
        }

        private static void HandleOperationRequest(
            PlayerID sender,
            BankNetworkRequestData data)
        {
            if (!IsBanker(sender) ||
                data.Token <= 0 ||
                data.AmountCents <= 0)
                return;

            string bankerId;

            if (!GetIdentity(sender, out bankerId))
                return;

            ConsentRoute consentRoute;

            lock (Sync)
            {
                if (!ConsentRoutes.TryGetValue(
                    data.Token, out consentRoute))
                    return;

                foreach (OperationRoute existing in OperationRoutes.Values)
                {
                    if (existing.ConsentToken == data.Token)
                    {
                        SendResponse(
                            sender.SmallID,
                            BankNetworkResult.Failed,
                            data.Token,
                            data.Operation,
                            data.AmountCents,
                            0, 0, false);
                        return;
                    }
                }
            }

            if (consentRoute.BankerId != bankerId ||
                consentRoute.BankerSmallId != sender.SmallID ||
                consentRoute.CustomerSmallId != data.TargetId)
                return;

            if (!IsCurrentPlayer(
                consentRoute.CustomerSmallId,
                consentRoute.CustomerId,
                out PlayerID customer))
                return;

            BankOperationType operation =
                (BankOperationType)data.Operation;

            if (operation != BankOperationType.Deposit &&
                operation != BankOperationType.Withdraw)
                return;

            BankOperationRequest request;
            string error;

            if (!consentService.CreateOperationRequest(
                consentRoute.RequestId,
                bankerId,
                consentRoute.CustomerId,
                operation,
                data.AmountCents,
                out request,
                out error))
            {
                SendResponse(
                    sender.SmallID,
                    BankNetworkResult.Failed,
                    data.Token,
                    data.Operation,
                    data.AmountCents,
                    0, 0, false);
                return;
            }

            long operationToken = NewToken();

            OperationRoute operationRoute = new OperationRoute
            {
                RequestId = request.RequestId,
                BankerId = bankerId,
                CustomerId = consentRoute.CustomerId,
                ConsentToken = data.Token,
                BankerSmallId = sender.SmallID,
                CustomerSmallId = customer.SmallID,
                Operation = data.Operation,
                AmountCents = data.AmountCents,
                Resolving = false
            };

            AddOperationRoute(operationToken, operationRoute);

            SendResponse(
                sender.SmallID,
                BankNetworkResult.OperationRequested,
                operationToken,
                data.Operation,
                data.AmountCents,
                0, 0, true);

            SendResponse(
                customer.SmallID,
                BankNetworkResult.OperationRequested,
                operationToken,
                data.Operation,
                data.AmountCents,
                0, 0, true);
        }

        private static void HandleOperationResolution(
            PlayerID sender,
            BankNetworkRequestData data)
        {
            if (data.Token <= 0)
                return;

            string customerId;

            if (!GetIdentity(sender, out customerId))
                return;

            OperationRoute route;

            lock (Sync)
            {
                if (!OperationRoutes.TryGetValue(
                    data.Token, out route))
                    return;

                if (route.Resolving)
                    return;

                if (route.CustomerId != customerId ||
                    route.CustomerSmallId != sender.SmallID)
                    return;

                route.Resolving = true;
            }

            bool accepted = data.Accepted == 1;
            string error;

            bool confirmed = consentService.ConfirmOperation(
                route.RequestId,
                customerId,
                accepted,
                out error);

            if (!confirmed || !accepted)
            {
                RemoveOperationRoute(data.Token);

                SendResponse(
                    sender.SmallID,
                    BankNetworkResult.OperationRefused,
                    data.Token,
                    route.Operation,
                    route.AmountCents,
                    0, 0, false);

                if (IsCurrentPlayer(
                    route.BankerSmallId,
                    route.BankerId,
                    out PlayerID banker))
                {
                    SendResponse(
                        banker.SmallID,
                        BankNetworkResult.OperationRefused,
                        data.Token,
                        route.Operation,
                        route.AmountCents,
                        0, 0, false);
                }

                return;
            }

            if (!IsCurrentPlayer(
                route.BankerSmallId,
                route.BankerId,
                out PlayerID currentBanker) ||
                !IsBanker(currentBanker))
            {
                RemoveOperationRoute(data.Token);
                SendOperationFailure(route, data.Token);
                return;
            }

            if (!IsCurrentPlayer(
                route.CustomerSmallId,
                route.CustomerId,
                out PlayerID currentCustomer))
            {
                RemoveOperationRoute(data.Token);
                SendOperationFailure(route, data.Token);
                return;
            }

            bool executed = consentService.ExecuteConfirmedOperation(
                route.RequestId,
                route.BankerId,
                route.CustomerId,
                runtime,
                out error);

            if (!executed)
            {
                RemoveOperationRoute(data.Token);
                SendOperationFailure(route, data.Token);
                return;
            }

            RemoveOperationRoute(data.Token);

            long cash = 0;
            long bank = 0;

            ConsentRoute consentRoute;
            bool hasConsentRoute;

            lock (Sync)
            {
                hasConsentRoute = ConsentRoutes.TryGetValue(
                    route.ConsentToken,
                    out consentRoute);
            }

            bool hasBalances = hasConsentRoute &&
                consentService.GetBalances(
                    consentRoute.RequestId,
                    route.BankerId,
                    route.CustomerId,
                    runtime,
                    out cash,
                    out bank,
                    out error);

            SendResponse(
                route.BankerSmallId,
                BankNetworkResult.OperationCompleted,
                data.Token,
                route.Operation,
                route.AmountCents,
                hasBalances ? cash : 0,
                hasBalances ? bank : 0,
                true);

            SendResponse(
                route.CustomerSmallId,
                BankNetworkResult.OperationCompleted,
                data.Token,
                route.Operation,
                route.AmountCents,
                hasBalances ? cash : 0,
                hasBalances ? bank : 0,
                true);
        }

        private static void SendOperationFailure(
            OperationRoute route,
            long token)
        {
            SendResponse(
                route.CustomerSmallId,
                BankNetworkResult.Failed,
                token,
                route.Operation,
                route.AmountCents,
                0, 0, false);

            if (IsCurrentPlayer(
                route.BankerSmallId,
                route.BankerId,
                out PlayerID banker))
            {
                SendResponse(
                    banker.SmallID,
                    BankNetworkResult.Failed,
                    token,
                    route.Operation,
                    route.AmountCents,
                    0, 0, false);
            }
        }

        // --------------------------------------------------
        // NETTOYAGE À LA DÉCONNEXION
        // --------------------------------------------------

        public static void RemovePlayer(string persistentId)
        {
            if (string.IsNullOrWhiteSpace(persistentId))
                return;

            lock (Sync)
            {
                if (consentService != null)
                    consentService.RemovePlayer(persistentId);

                List<long> consentToRemove = new List<long>();

                foreach (KeyValuePair<long, ConsentRoute> item
                    in ConsentRoutes)
                {
                    if (item.Value.BankerId == persistentId ||
                        item.Value.CustomerId == persistentId)
                    {
                        consentToRemove.Add(item.Key);
                    }
                }

                foreach (long token in consentToRemove)
                {
                    ConsentRoutes.Remove(token);
                    ConsentRouteCreated.Remove(token);
                }

                List<long> operationsToRemove = new List<long>();

                foreach (KeyValuePair<long, OperationRoute> item
                    in OperationRoutes)
                {
                    if (item.Value.BankerId == persistentId ||
                        item.Value.CustomerId == persistentId)
                    {
                        operationsToRemove.Add(item.Key);
                    }
                }

                foreach (long token in operationsToRemove)
                {
                    OperationRoutes.Remove(token);
                    OperationRouteCreated.Remove(token);
                }
            }
        }
    }
}
