
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using FusionDarkRP.Economy;
using LabFusion.Network;
using LabFusion.Network.Serialization;
using LabFusion.Player;
using LabFusion.SDK.Modules;
using LabFusion.Senders;
using MelonLoader;

namespace FusionDarkRP.Integration
{
    public enum AtmNetworkAction : byte
    {
        RequestBalances = 1,
        RequestOperation = 2
    }

    public static class AtmNetworkResults
    {
        public const byte BalancesAvailable = 8;
        public const byte OperationCompleted = 9;
        public const byte Failed = 10;
    }

    public sealed class AtmNetworkRequestData : INetSerializable
    {
        public byte Action;
        public byte TargetId;
        public long Token;
        public byte Operation;
        public long AmountCents;
        public byte Reserved;
        public long CardIdPart1;
        public long CardIdPart2;

        public int? GetSize() => 36;

        public void Serialize(INetSerializer serializer)
        {
            serializer.SerializeValue(ref Action);
            serializer.SerializeValue(ref TargetId);
            serializer.SerializeValue(ref Token);
            serializer.SerializeValue(ref Operation);
            serializer.SerializeValue(ref AmountCents);
            serializer.SerializeValue(ref Reserved);
            serializer.SerializeValue(ref CardIdPart1);
            serializer.SerializeValue(ref CardIdPart2);
        }
    }

    public sealed class AtmNetworkResponseData : INetSerializable
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

    public sealed class AtmNetworkRequestHandler : ModuleMessageHandler
    {
        public override ExpectedReceiverType ExpectedReceiver =>
            ExpectedReceiverType.ServerOnly;

        protected override bool OnPreRelayMessage(ReceivedMessage received)
        {
            return received.Bytes != null &&
                   received.Bytes.Length == 36;
        }

        protected override void OnHandleMessage(ReceivedMessage received)
        {
            if (!NetworkInfo.IsHost || !received.Sender.HasValue)
                return;

            try
            {
                AtmNetworkRequestData data =
                    received.ReadData<AtmNetworkRequestData>();

                PlayerID sender =
                    PlatformIds.GetPlayerID(received.Sender.Value);

                if (sender == null || !sender.IsValid)
                    return;

                AtmNetwork.HandleHostRequest(sender, data);
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP ATM] Erreur reception : " + exception);
            }
        }
    }

    public sealed class AtmNetworkResponseHandler : ModuleMessageHandler
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
                AtmNetworkResponseData data =
                    received.ReadData<AtmNetworkResponseData>();

                PlayerID local = PlayerIDManager.LocalID;

                if (local == null ||
                    !local.IsValid ||
                    local.SmallID != data.TargetId)
                    return;

                AtmNetwork.NotifyResponse(data);
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP ATM] Erreur reception reponse : "
                    + exception);
            }
        }
    }

    public static class AtmNetwork
    {
        private const long MaximumAtmOperationCents = 100000000L;
        private const int MaximumRememberedRequests = 2048;

        private static readonly object Sync = new object();

        private static long nextToken = DateTime.UtcNow.Ticks;

        private static readonly Dictionary<string, DateTime>
            LastRequests =
                new Dictionary<string, DateTime>(StringComparer.Ordinal);

        private static readonly HashSet<string>
            ProcessedRequests =
                new HashSet<string>(StringComparer.Ordinal);

        private static readonly Queue<string>
            ProcessedOrder =
                new Queue<string>();

        private static readonly TimeSpan RequestCooldown =
            TimeSpan.FromMilliseconds(300);

        public static event Action<AtmNetworkResponseData> ResponseReceived;

        public static bool IsReady
        {
            get
            {
                return EconomyRuntimeBootstrap.IsInitialized &&
                       EconomyRuntimeBootstrap.Runtime != null &&
                       NetworkInfo.HasServer;
            }
        }

        private static long NewToken()
        {
            return Interlocked.Increment(ref nextToken);
        }

        private static bool TryEncodeCardId(
            string cardId,
            out long part1,
            out long part2)
        {
            part1 = 0;
            part2 = 0;

            Guid guid;

            if (string.IsNullOrWhiteSpace(cardId) ||
                !Guid.TryParseExact(cardId, "N", out guid) ||
                guid == Guid.Empty)
                return false;

            byte[] bytes = guid.ToByteArray();

            part1 = BitConverter.ToInt64(bytes, 0);
            part2 = BitConverter.ToInt64(bytes, 8);

            return true;
        }

        private static bool TryDecodeCardId(
            long part1,
            long part2,
            out string cardId)
        {
            cardId = null;

            if (part1 == 0 && part2 == 0)
                return false;

            try
            {
                byte[] bytes = new byte[16];

                Buffer.BlockCopy(
                    BitConverter.GetBytes(part1), 0, bytes, 0, 8);

                Buffer.BlockCopy(
                    BitConverter.GetBytes(part2), 0, bytes, 8, 8);

                Guid guid = new Guid(bytes);

                if (guid == Guid.Empty)
                    return false;

                cardId = guid.ToString("N");
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetWithdrawalItemId(
            long amountCents,
            out string itemId)
        {
            itemId = null;

            switch (amountCents)
            {
                case 500:
                    itemId = "Note_5EUR";
                    return true;

                case 1000:
                    itemId = "Note_10EUR";
                    return true;

                case 2000:
                    itemId = "Note_20EUR";
                    return true;

                case 5000:
                    itemId = "Note_50EUR";
                    return true;

                case 10000:
                    itemId = "Note_100EUR";
                    return true;

                // Liasses de 50 billets.
                case 25000:
                    itemId = "Band_5EUR";
                    return true;

                case 50000:
                    itemId = "Band_10EUR";
                    return true;

                case 100000:
                    itemId = "Band_20EUR";
                    return true;

                case 250000:
                    itemId = "Band_50EUR";
                    return true;

                case 500000:
                    itemId = "Band_100EUR";
                    return true;

                default:
                    return false;
            }
        }

        public static void RequestBalances()
        {
            RequestBalances((string)null);
        }

        public static long RequestBalances(string cardId)
        {
            if (!IsReady)
                return 0;

            PlayerID local = PlayerIDManager.LocalID;

            if (local == null || !local.IsValid)
                return 0;

            long part1;
            long part2;

            if (!TryEncodeCardId(cardId, out part1, out part2))
                return 0;

            long token = NewToken();

            AtmNetworkRequestData data =
                new AtmNetworkRequestData
                {
                    Action = (byte)AtmNetworkAction.RequestBalances,
                    TargetId = local.SmallID,
                    Token = token,
                    CardIdPart1 = part1,
                    CardIdPart2 = part2
                };

            return SendRequest(data) ? token : 0;
        }

        public static void RequestOperation(
            BankOperationType operation,
            long amountCents)
        {
            RequestOperation(operation, amountCents, null);
        }

        public static long RequestOperation(
            BankOperationType operation,
            long amountCents,
            string cardId)
        {
            if (!IsReady ||
                amountCents <= 0 ||
                amountCents > MaximumAtmOperationCents)
                return 0;

            // L'ATM réseau ne réalise plus de dépôt numérique.
            // Les dépôts doivent passer par le système sécurisé
            // de dépôt de billets physiques.
            if (operation != BankOperationType.Withdraw)
                return 0;

            if (!TryGetWithdrawalItemId(amountCents, out _))
                return 0;

            PlayerID local = PlayerIDManager.LocalID;

            if (local == null || !local.IsValid)
                return 0;

            long part1;
            long part2;

            if (!TryEncodeCardId(cardId, out part1, out part2))
                return 0;

            long token = NewToken();

            AtmNetworkRequestData data =
                new AtmNetworkRequestData
                {
                    Action = (byte)AtmNetworkAction.RequestOperation,
                    TargetId = local.SmallID,
                    Token = token,
                    Operation = (byte)operation,
                    AmountCents = amountCents,
                    CardIdPart1 = part1,
                    CardIdPart2 = part2
                };

            return SendRequest(data) ? token : 0;
        }

        private static bool SendRequest(AtmNetworkRequestData data)
        {
            if (data == null || !NetworkInfo.HasServer)
                return false;

            try
            {
                if (NetworkInfo.IsHost)
                {
                    PlayerID local = PlayerIDManager.LocalID;

                    if (local == null || !local.IsValid)
                        return false;

                    MelonCoroutines.Start(
                        HandleHostRequestNextFrame(local, data));

                    return true;
                }

                MessageRelay.RelayModule<
                    AtmNetworkRequestHandler,
                    AtmNetworkRequestData>(
                    data,
                    CommonMessageRoutes.ReliableToServer);

                return true;
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP ATM] Envoi impossible : " + exception);

                return false;
            }
        }

        private static IEnumerator HandleHostRequestNextFrame(
            PlayerID sender,
            AtmNetworkRequestData data)
        {
            yield return null;

            try
            {
                if (sender == null ||
                    !sender.IsValid ||
                    data == null)
                    yield break;

                HandleHostRequest(sender, data);
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP ATM] Traitement hote impossible : "
                    + exception);
            }
        }

        internal static void HandleHostRequest(
            PlayerID sender,
            AtmNetworkRequestData data)
        {
            if (!NetworkInfo.IsHost ||
                sender == null ||
                !sender.IsValid ||
                data == null ||
                !EconomyRuntimeBootstrap.IsInitialized)
                return;

            if (data.TargetId != sender.SmallID ||
                data.Token <= 0 ||
                data.AmountCents < 0)
                return;

            AtmNetworkAction action =
                (AtmNetworkAction)data.Action;

            if (action == AtmNetworkAction.RequestBalances)
            {
                if (data.AmountCents != 0 || data.Operation != 0)
                    return;
            }
            else if (action == AtmNetworkAction.RequestOperation)
            {
                // Refus serveur des dépôts numériques.
                // Seul un retrait physique est autorisé ici.
                BankOperationType operation =
                    (BankOperationType)data.Operation;

                if (operation != BankOperationType.Withdraw ||
                    data.AmountCents <= 0 ||
                    data.AmountCents > MaximumAtmOperationCents ||
                    !TryGetWithdrawalItemId(data.AmountCents, out _))
                {
                    SendResponse(
                        sender.SmallID,
                        AtmNetworkResults.Failed,
                        data.Token,
                        data.Operation,
                        data.AmountCents,
                        0,
                        0,
                        false);
                    return;
                }
            }
            else
            {
                return;
            }

            string persistentId;

            if (!PlayerIdentityService.TryGetPersistentId(
                    sender,
                    out persistentId) ||
                string.IsNullOrWhiteSpace(persistentId))
                return;

            EconomyRuntime runtime =
                EconomyRuntimeBootstrap.Runtime;

            if (runtime == null || runtime.BankCards == null)
                return;

            string cardId;

            if (!TryDecodeCardId(
                    data.CardIdPart1,
                    data.CardIdPart2,
                    out cardId) ||
                !runtime.BankCards.IsOwnedBy(cardId, persistentId))
            {
                SendResponse(
                    sender.SmallID,
                    AtmNetworkResults.Failed,
                    data.Token,
                    data.Operation,
                    data.AmountCents,
                    0,
                    0,
                    false);

                string reason =
                    cardId == null
                        ? "identifiant de carte illisible dans la requête"
                        : runtime.BankCards.DescribeOwnership(
                            cardId, persistentId);

                try
                {
                    string holder = cardId == null
                        ? null
                        : runtime.BankCards.GetHolderName(cardId);

                    string notice = holder != null
                        ? "Carte nominative : elle appartient à " + holder
                            + ". Vous ne pouvez pas l'utiliser."
                        : "Carte invalide ou remplacée par une nouvelle carte.";

                    if (sender.IsMe)
                        RpHub.Notify(notice, 2);
                    else
                        RpHub.PushNotice(sender, notice, 2);
                }
                catch (Exception noticeException)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP ATM] Message de refus : "
                        + noticeException.Message);
                }

                MelonLogger.Warning(
                    "[FusionDarkRP ATM] Carte refusée pour "
                    + persistentId + " : " + reason);

                return;
            }

            bool duplicate;
            bool throttled;

            if (!AllowRequest(
                    persistentId,
                    data.Token,
                    out duplicate,
                    out throttled))
            {
                if (throttled && !duplicate)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP ATM] Requête trop rapprochée, "
                        + "refusée pour " + persistentId);

                    SendResponse(
                        sender.SmallID,
                        AtmNetworkResults.Failed,
                        data.Token,
                        data.Operation,
                        data.AmountCents,
                        0,
                        0,
                        false);
                }

                return;
            }

            switch (action)
            {
                case AtmNetworkAction.RequestBalances:
                    HandleBalanceRequest(
                        sender, persistentId, data, runtime);
                    break;

                case AtmNetworkAction.RequestOperation:
                    HandleOperationRequest(
                        sender, persistentId, data, runtime);
                    break;
            }
        }

        private static bool AllowRequest(
            string persistentId,
            long token,
            out bool duplicate,
            out bool throttled)
        {
            duplicate = false;
            throttled = false;

            string key = persistentId + ":" +
                token.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);

            lock (Sync)
            {
                DateTime now = DateTime.UtcNow;

                if (ProcessedRequests.Contains(key))
                {
                    duplicate = true;
                    return false;
                }

                DateTime previous;

                if (LastRequests.TryGetValue(
                        persistentId,
                        out previous) &&
                    now - previous < RequestCooldown)
                {
                    throttled = true;
                    return false;
                }

                LastRequests[persistentId] = now;

                ProcessedRequests.Add(key);
                ProcessedOrder.Enqueue(key);

                while (ProcessedOrder.Count > MaximumRememberedRequests)
                {
                    string oldest = ProcessedOrder.Dequeue();
                    ProcessedRequests.Remove(oldest);
                }

                return true;
            }
        }

        private static void HandleBalanceRequest(
            PlayerID sender,
            string persistentId,
            AtmNetworkRequestData data,
            EconomyRuntime runtime)
        {
            long cash;
            long bank;

            if (!runtime.Bank.GetBalances(
                    persistentId,
                    out cash,
                    out bank))
            {
                SendResponse(
                    sender.SmallID,
                    AtmNetworkResults.Failed,
                    data.Token,
                    0,
                    0,
                    0,
                    0,
                    false);

                return;
            }

            SendResponse(
                sender.SmallID,
                AtmNetworkResults.BalancesAvailable,
                data.Token,
                0,
                0,
                cash,
                bank,
                true);
        }

        private static void HandleOperationRequest(
            PlayerID sender,
            string persistentId,
            AtmNetworkRequestData data,
            EconomyRuntime runtime)
        {
            // La validation de HandleHostRequest garantit
            // que seule l'opération Withdraw arrive ici.
            string requestId =
                "atm:"
                + persistentId
                + ":"
                + data.Token.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);

            string error;

            bool success = ProcessPhysicalWithdrawal(
                sender,
                persistentId,
                requestId,
                data.AmountCents,
                runtime,
                out error);

            if (!success)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP ATM] Operation refusee pour "
                    + persistentId + " : " + error);

                long failedCash = 0;
                long failedBank = 0;

                runtime.Bank.GetBalances(
                    persistentId,
                    out failedCash,
                    out failedBank);

                SendResponse(
                    sender.SmallID,
                    AtmNetworkResults.Failed,
                    data.Token,
                    data.Operation,
                    data.AmountCents,
                    failedCash,
                    failedBank,
                    false);

                return;
            }

            long cash = 0;
            long bank = 0;

            bool balancesAvailable =
                runtime.Bank.GetBalances(
                    persistentId,
                    out cash,
                    out bank);

            SendResponse(
                sender.SmallID,
                AtmNetworkResults.OperationCompleted,
                data.Token,
                data.Operation,
                data.AmountCents,
                balancesAvailable ? cash : 0,
                balancesAvailable ? bank : 0,
                true);

            MelonLogger.Msg(
                "[FusionDarkRP ATM] Retrait physique effectue pour "
                + persistentId);
        }

        private static bool ProcessPhysicalWithdrawal(
            PlayerID player,
            string persistentId,
            string requestId,
            long amountCents,
            EconomyRuntime runtime,
            out string error)
        {
            error = null;

            if (player == null || !player.IsValid)
            {
                error = "Joueur invalide.";
                return false;
            }

            if (!TryGetWithdrawalItemId(
                amountCents,
                out string itemId))
            {
                error = "Montant de retrait physique non disponible.";
                return false;
            }

            if (!PhysicalCashWorldManager.IsInitialized ||
                !PhysicalCashWorldManager.HasSpawnAnchorProvider)
            {
                error =
                    "Le système d'apparition des billets n'est pas prêt.";
                return false;
            }

            // Première étape : débit bancaire et crédit temporaire
            // du portefeuille liquide.
            if (!runtime.Atm.Withdraw(
                true,
                requestId,
                persistentId,
                amountCents,
                out error))
            {
                return false;
            }

            string cashId;
            string spawnedObjectId;
            string spawnError;

            // Le billet apparaît à la sortie du distributeur
            // le plus proche du joueur.
            bool spawned =
                PhysicalCashWorldManager.TryCreateAndSpawnAtNearestAtm(
                    player,
                    PhysicalCashRequestId.Create(),
                    itemId,
                    out cashId,
                    out spawnedObjectId,
                    out spawnError);

            if (spawned)
            {
                MelonLogger.Msg(
                    "[FusionDarkRP ATM] Billet physique distribue : "
                    + itemId
                    + " | CashId : "
                    + cashId
                    + " | Objet : "
                    + spawnedObjectId
                    + " | Joueur : "
                    + persistentId);

                return true;
            }

            // Si le billet ne peut pas sortir, annuler le retrait.
            string rollbackError;

            bool rolledBack =
                runtime.Atm.Deposit(
                    true,
                    requestId + ":spawn-rollback",
                    persistentId,
                    amountCents,
                    out rollbackError);

            if (rolledBack)
            {
                error =
                    "Le billet n'a pas pu apparaître. "
                    + "Le retrait bancaire a été annulé. "
                    + spawnError;

                MelonLogger.Warning(
                    "[FusionDarkRP ATM] Retrait annule apres echec "
                    + "d'apparition : "
                    + persistentId
                    + " | "
                    + spawnError);

                return false;
            }

            error =
                "ATTENTION : le billet n'a pas pu apparaître et "
                + "l'annulation bancaire n'a pas été confirmée. "
                + "Apparition : "
                + spawnError
                + " | Annulation : "
                + rollbackError;

            MelonLogger.Error(
                "[FusionDarkRP ATM] Echec critique du retrait : "
                + persistentId
                + " | Apparition : "
                + spawnError
                + " | Annulation : "
                + rollbackError);

            return false;
        }

        private static void SendResponse(
            byte targetId,
            byte result,
            long token,
            byte operation,
            long amount,
            long cash,
            long bank,
            bool success)
        {
            try
            {
                AtmNetworkResponseData response =
                    new AtmNetworkResponseData
                    {
                        TargetId = targetId,
                        Result = result,
                        Token = token,
                        Operation = operation,
                        AmountCents = amount,
                        CashCents = cash,
                        BankCents = bank,
                        Success = success ? (byte)1 : (byte)0
                    };

                PlayerID local = PlayerIDManager.LocalID;

                if (local != null &&
                    local.IsValid &&
                    local.SmallID == targetId)
                {
                    NotifyResponse(response);
                    return;
                }

                MessageRelay.RelayModule<
                    AtmNetworkResponseHandler,
                    AtmNetworkResponseData>(
                    response,
                    CommonMessageRoutes.ReliableToClients);
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP ATM] Envoi reponse impossible : "
                    + exception);
            }
        }

        internal static void NotifyResponse(
            AtmNetworkResponseData data)
        {
            if (data == null)
                return;

            Action<AtmNetworkResponseData> callback =
                ResponseReceived;

            if (callback == null)
                return;

            try
            {
                callback(data);
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP ATM] Erreur interface : "
                    + exception);
            }
        }

        public static void RemovePlayer(string persistentId)
        {
            if (string.IsNullOrWhiteSpace(persistentId))
                return;

            lock (Sync)
            {
                LastRequests.Remove(persistentId);

                string prefix = persistentId + ":";

                List<string> matching = new List<string>();

                foreach (string key in ProcessedRequests)
                {
                    if (key.StartsWith(
                            prefix,
                            StringComparison.Ordinal))
                        matching.Add(key);
                }

                foreach (string key in matching)
                    ProcessedRequests.Remove(key);
            }
        }

        public static void Shutdown()
        {
            lock (Sync)
            {
                LastRequests.Clear();
                ProcessedRequests.Clear();
                ProcessedOrder.Clear();
            }

            ResponseReceived = null;
        }
    }
}
