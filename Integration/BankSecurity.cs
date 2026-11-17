using System;
using System.Collections.Generic;
using System.Text.Json;
using FusionDarkRP.Economy;
using Il2CppSLZ.Bonelab;
using Il2CppTMPro;
using LabFusion.Network;
using LabFusion.Network.Serialization;
using LabFusion.Player;
using LabFusion.SDK.Modules;
using LabFusion.UI;
using LabFusion.UI.Popups;
using LabFusion.Utilities;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace FusionDarkRP.Integration
{
    // ====================================================================
    // Messages réseau
    // ====================================================================

    public sealed class BankSecurityPacket : INetSerializable
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

    public enum SecurityAction : byte
    {
        Toggle = 1,
        SetLock = 2,
        Claim = 3,
        Release = 4,
        Deposit = 5,
        Withdraw = 6,
        Query = 7,
        ForceStart = 8,
        ForceCancel = 9,
        // Coffre mural physique : code saisi au clavier, angle de porte, libération.
        Code = 10,
        DoorAngle = 11,
        ResetCode = 12
    }

    public enum SecurityKind : byte
    {
        Door = 1,
        Vault = 2,
        Safe = 3
    }

    public sealed class SecurityRequest
    {
        public byte A { get; set; }
        public long T { get; set; }
        public string O { get; set; }
        public long M { get; set; }
    }

    public sealed class SecurityResponse
    {
        public long T { get; set; }
        public string O { get; set; }
        public bool Ok { get; set; }
        public string Msg { get; set; }
        public long Stock { get; set; } = -1;
    }

    public sealed class SecurityState
    {
        public string O { get; set; }
        public byte K { get; set; }
        public bool Open { get; set; }
        public bool Locked { get; set; }
        public string Owner { get; set; }
        public bool Forcing { get; set; }
        public float Bx { get; set; }
        public float By { get; set; }
        public float Bz { get; set; }
        public float Qx { get; set; }
        public float Qy { get; set; }
        public float Qz { get; set; }
        public float Qw { get; set; } = 1f;
        public bool HasBase { get; set; }
        // Coffre mural physique.
        public float Ang { get; set; }
        public bool CodeSet { get; set; }
        public float Lockout { get; set; }
    }

    public sealed class SecurityAlert
    {
        public string Text { get; set; }
        public byte Level { get; set; }
    }

    internal static class BankSecurityWire
    {
        public const int MaximumPacketSize = 768;

        public static bool TryRead(
            ReceivedMessage message, out byte[] payload)
        {
            payload = null;

            BankSecurityPacket packet =
                message.ReadData<BankSecurityPacket>();

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

        public static bool SizeOk(ReceivedMessage message)
        {
            return message.Bytes != null
                && message.Bytes.Length > 0
                && message.Bytes.Length <= MaximumPacketSize + 4;
        }

        public static bool FromHost(ReceivedMessage message)
        {
            return NetworkInfo.HasServer
                && !NetworkInfo.IsHost
                && !message.IsServerHandled
                && message.Sender == 0;
        }
    }

    public sealed class BankSecurityRequestHandler : ModuleMessageHandler
    {
        public override ExpectedReceiverType ExpectedReceiver =>
            ExpectedReceiverType.ServerOnly;

        protected override bool OnPreRelayMessage(ReceivedMessage message)
        {
            return BankSecurityWire.SizeOk(message);
        }

        protected override void OnHandleMessage(ReceivedMessage message)
        {
            if (!NetworkInfo.IsHost || !message.Sender.HasValue)
                return;

            try
            {
                byte[] payload;

                if (!BankSecurityWire.TryRead(message, out payload))
                    return;

                PlayerID sender =
                    PlatformIds.GetPlayerID(message.Sender.Value);

                if (sender == null || !sender.IsValid)
                    return;

                SecurityRequest request =
                    JsonSerializer.Deserialize<SecurityRequest>(payload);

                BankSecurity.HandleHostRequest(sender, request);
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Securite] Erreur requête : " + exception);
            }
        }
    }

    public sealed class BankSecurityResponseHandler : ModuleMessageHandler
    {
        public override ExpectedReceiverType ExpectedReceiver =>
            ExpectedReceiverType.ClientsOnly;

        protected override bool OnPreRelayMessage(ReceivedMessage message)
        {
            return BankSecurityWire.SizeOk(message);
        }

        protected override void OnHandleMessage(ReceivedMessage message)
        {
            if (!BankSecurityWire.FromHost(message))
                return;

            try
            {
                byte[] payload;

                if (BankSecurityWire.TryRead(message, out payload))
                {
                    BankSecurity.OnResponse(
                        JsonSerializer.Deserialize<SecurityResponse>(payload));
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Securite] Erreur réponse : " + exception);
            }
        }
    }

    public sealed class BankSecurityStateHandler : ModuleMessageHandler
    {
        public override ExpectedReceiverType ExpectedReceiver =>
            ExpectedReceiverType.ClientsOnly;

        protected override bool OnPreRelayMessage(ReceivedMessage message)
        {
            return BankSecurityWire.SizeOk(message);
        }

        protected override void OnHandleMessage(ReceivedMessage message)
        {
            if (!BankSecurityWire.FromHost(message))
                return;

            try
            {
                byte[] payload;

                if (BankSecurityWire.TryRead(message, out payload))
                {
                    BankSecurity.OnState(
                        JsonSerializer.Deserialize<SecurityState>(payload));
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Securite] Erreur état : " + exception);
            }
        }
    }

    public sealed class BankSecurityAlertHandler : ModuleMessageHandler
    {
        public override ExpectedReceiverType ExpectedReceiver =>
            ExpectedReceiverType.ClientsOnly;

        protected override bool OnPreRelayMessage(ReceivedMessage message)
        {
            return BankSecurityWire.SizeOk(message);
        }

        protected override void OnHandleMessage(ReceivedMessage message)
        {
            if (!BankSecurityWire.FromHost(message))
                return;

            try
            {
                byte[] payload;

                if (BankSecurityWire.TryRead(message, out payload))
                {
                    BankSecurity.OnAlert(
                        JsonSerializer.Deserialize<SecurityAlert>(payload));
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Securite] Erreur alerte : " + exception);
            }
        }
    }

    // ====================================================================
    // Système
    // ====================================================================

    /// <summary>
    /// Porte de sécurité, coffre-fort bancaire et coffre mural.
    /// L'hôte est seul autorité (droits, argent, crochetage). Chaque
    /// client affiche des panneaux VR (curseur laser) sur les objets et
    /// anime la porte à partir de l'état diffusé.
    /// </summary>
    public static partial class BankSecurity
    {
        private const string DoorModel = "BankSecurityDoor";
        private const string VaultModel = "BankVault";
        private const string SafeModel = "WallSafe";

        private const string VaultWalletId = "system:bank_vault";
        private const string TreasuryModel = "TownTreasury";
        private const string TreasuryWalletId = "system:town_treasury";

        // Rang minimal (0 = premier grade) pour ouvrir les gros coffres :
        // directeur adjoint / directeur / maire et adjoints.
        private const int BossGradeIndex = 3;

        private static readonly HashSet<string> TreasuryIds =
            new HashSet<string>(StringComparer.Ordinal);

        private static bool IsTreasuryId(string objectId)
        {
            lock (Sync)
                return objectId != null && TreasuryIds.Contains(objectId);
        }

        private const float ScanInterval = 1f;
        private const float MinimumRequestInterval = 0.25f;
        private const float DoorSeconds = 0.9f;
        private const float ForceDoorSeconds = 20f;
        private const float ForceSafeSeconds = 30f;
        private const float ForceVaultSeconds = 45f;
        private const long MaximumOperationCents = 10000000L;
        private const long VaultHeistCapCents = 500000L;
        private const long SafeHeistCapCents = 100000L;

        private static readonly object Sync = new object();

        private static readonly Dictionary<string, SecurityObject> Objects =
            new Dictionary<string, SecurityObject>(StringComparer.Ordinal);

        private static readonly Dictionary<string, HostState> HostStates =
            new Dictionary<string, HostState>(StringComparer.Ordinal);

        private static readonly Dictionary<byte, float> LastRequest =
            new Dictionary<byte, float>();

        private static bool initialized;
        private static float nextScan;
        private static long tokenCounter;

        private static MelonPreferences_Entry<bool> hingeRightEntry;
        private static MelonPreferences_Entry<float> openAngleEntry;

        // ------------------------------------------------------------
        // Cycle de vie
        // ------------------------------------------------------------

        public static bool IsInitialized
        {
            get
            {
                lock (Sync)
                    return initialized;
            }
        }

        public static bool Initialize()
        {
            lock (Sync)
            {
                if (initialized)
                    return true;

                try
                {
                    MelonPreferences_Category category =
                        MelonPreferences.CreateCategory("FusionDarkRP");

                    hingeRightEntry = category.CreateEntry<bool>(
                        "SecurityDoorHingeRight", false);

                    openAngleEntry = category.CreateEntry<float>(
                        "SecurityDoorOpenAngle", 90f);

                    MultiplayerHooking.OnPlayerJoined += OnPlayerJoined;

                    initialized = true;
                    nextScan = 0f;

                    MelonLogger.Msg(
                        "[FusionDarkRP Securite] Porte, coffres et coffres "
                        + "muraux initialises.");

                    return true;
                }
                catch (Exception exception)
                {
                    initialized = false;

                    MelonLogger.Error(
                        "[FusionDarkRP Securite] Initialisation : "
                        + exception);

                    return false;
                }
            }
        }

        public static void Shutdown()
        {
            lock (Sync)
            {
                if (initialized)
                {
                    try
                    {
                        MultiplayerHooking.OnPlayerJoined -= OnPlayerJoined;
                    }
                    catch (Exception exception)
                    {
                        MelonLogger.Warning(
                            "[FusionDarkRP Securite] Arrêt : "
                            + exception.Message);
                    }
                }

                foreach (SecurityObject entry in Objects.Values)
                    entry.Dispose();

                Objects.Clear();
                HostStates.Clear();
                LastRequest.Clear();
                initialized = false;
            }
        }

        public static void Update()
        {
            if (!IsInitialized)
                return;

            if (Time.realtimeSinceStartup >= nextScan)
            {
                nextScan = Time.realtimeSinceStartup + ScanInterval;
                Scan();
            }

            List<SecurityObject> snapshot;

            lock (Sync)
                snapshot = new List<SecurityObject>(Objects.Values);

            foreach (SecurityObject entry in snapshot)
            {
                try
                {
                    entry.Animate(Time.deltaTime);
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Securite] Animation : "
                        + exception.Message);
                }
            }

            if (IsHost())
            {
                TickForcing();
                TickSafes();
            }
        }

        // ------------------------------------------------------------
        // Découverte des objets
        // ------------------------------------------------------------

        private static bool TryKind(string modelId, out SecurityKind kind)
        {
            kind = SecurityKind.Door;

            if (string.Equals(
                modelId, DoorModel, StringComparison.OrdinalIgnoreCase))
            {
                kind = SecurityKind.Door;
                return true;
            }

            if (string.Equals(
                modelId, VaultModel, StringComparison.OrdinalIgnoreCase))
            {
                kind = SecurityKind.Vault;
                return true;
            }

            if (string.Equals(
                modelId, TreasuryModel, StringComparison.OrdinalIgnoreCase))
            {
                kind = SecurityKind.Vault;
                return true;
            }

            if (string.Equals(
                modelId, SafeModel, StringComparison.OrdinalIgnoreCase))
            {
                kind = SecurityKind.Safe;
                return true;
            }

            return false;
        }

        private static void Scan()
        {
            string[] ids = WorldModelRuntimeManager.GetSpawnedObjectIds();
            HashSet<string> alive = new HashSet<string>(StringComparer.Ordinal);

            foreach (string objectId in ids)
            {
                string modelId;
                SecurityKind kind;

                if (!WorldModelRuntimeManager.TryGetModelId(
                        objectId, out modelId) ||
                    !TryKind(modelId, out kind))
                {
                    continue;
                }

                GameObject body;

                if (!WorldModelRuntimeManager.TryGet(objectId, out body) ||
                    body == null)
                {
                    continue;
                }

                alive.Add(objectId);

                lock (Sync)
                {
                    if (string.Equals(
                        modelId, TreasuryModel,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        TreasuryIds.Add(objectId);
                    }

                    if (Objects.ContainsKey(objectId))
                        continue;

                    try
                    {
                        Objects.Add(
                            objectId,
                            new SecurityObject(objectId, kind, body));
                    }
                    catch (Exception exception)
                    {
                        MelonLogger.Error(
                            "[FusionDarkRP Securite] Panneau impossible ("
                            + modelId + ") : " + exception.Message);
                    }
                }
            }

            lock (Sync)
            {
                List<string> gone = new List<string>();

                foreach (KeyValuePair<string, SecurityObject> pair in Objects)
                {
                    if (!alive.Contains(pair.Key) || pair.Value.IsDestroyed)
                        gone.Add(pair.Key);
                }

                foreach (string id in gone)
                {
                    Objects[id].Dispose();
                    Objects.Remove(id);
                    HostStates.Remove(id);
                }
            }
        }

        // ------------------------------------------------------------
        // Client : envoi de requêtes
        // ------------------------------------------------------------

        internal static long Request(
            string objectId, SecurityAction action, long amountCents)
        {
            if (!NetworkInfo.HasServer ||
                string.IsNullOrWhiteSpace(objectId))
            {
                return 0;
            }

            PlayerID local = PlayerIDManager.LocalID;

            if (local == null || !local.IsValid)
                return 0;

            long token = ++tokenCounter;

            SecurityRequest request = new SecurityRequest
            {
                A = (byte)action,
                T = token,
                O = objectId,
                M = amountCents
            };

            try
            {
                if (NetworkInfo.IsHost)
                {
                    HandleHostRequest(local, request);
                    return token;
                }

                MessageRelay.RelayModule<
                    BankSecurityRequestHandler,
                    BankSecurityPacket>(
                    new BankSecurityPacket
                    {
                        Payload =
                            JsonSerializer.SerializeToUtf8Bytes(request)
                    },
                    CommonMessageRoutes.ReliableToServer);

                return token;
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Securite] Envoi impossible : "
                    + exception);

                return 0;
            }
        }

        // ------------------------------------------------------------
        // Client : réception
        // ------------------------------------------------------------

        internal static void OnResponse(SecurityResponse response)
        {
            if (response == null || string.IsNullOrWhiteSpace(response.O))
                return;

            SecurityObject target;

            lock (Sync)
            {
                if (!Objects.TryGetValue(response.O, out target))
                    return;
            }

            target.ApplyResponse(response);
        }

        internal static void OnState(SecurityState state)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.O))
                return;

            SecurityObject target;

            lock (Sync)
            {
                if (!Objects.TryGetValue(state.O, out target))
                {
                    // L'objet n'est pas encore créé localement :
                    // l'état sera réappliqué au prochain scan.
                    PendingStates[state.O] = state;
                    return;
                }
            }

            target.ApplyState(state);
        }

        private static readonly Dictionary<string, SecurityState>
            PendingStates =
                new Dictionary<string, SecurityState>(StringComparer.Ordinal);

        internal static void OnAlert(SecurityAlert alert)
        {
            if (alert == null || string.IsNullOrWhiteSpace(alert.Text))
                return;

            try
            {
                Notifier.Send(new Notification
                {
                    Title = "Alerte banque",
                    Message = alert.Text.Length > 160
                        ? alert.Text.Substring(0, 160)
                        : alert.Text,
                    Type = alert.Level >= 2
                        ? NotificationType.WARNING
                        : NotificationType.INFORMATION,
                    PopupLength = 6f,
                    SaveToMenu = true
                });
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Securite] Notification : "
                    + exception.Message);
            }
        }

        // ------------------------------------------------------------
        // Hôte : état et droits
        // ------------------------------------------------------------

        private sealed partial class HostState
        {
            public SecurityKind Kind;
            public bool Open;
            public bool Locked;
            public string OwnerId;
            public string OwnerName;
            public byte ForceBy = 255;
            public float ForceEnd;
            public bool Forcing;
            public bool Treasury;
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

        private static HostState GetHostState(
            string objectId, SecurityKind kind)
        {
            lock (Sync)
            {
                HostState state;

                if (!HostStates.TryGetValue(objectId, out state))
                {
                    state = new HostState
                    {
                        Kind = kind,
                        Open = false,
                        Locked = kind != SecurityKind.Door,
                        Treasury = TreasuryIds.Contains(objectId)
                    };

                    HostStates.Add(objectId, state);

                    if (kind == SecurityKind.Safe)
                        LoadSafeRecord(objectId, state);
                }

                return state;
            }
        }

        private static bool IsBankStaff(PlayerID player)
        {
            string job = EconomyJobResolver.GetJobIdFromPlayer(player);

            return string.Equals(job, "banque", StringComparison.Ordinal);
        }

        private static bool CanManage(
            PlayerID player, HostState state, string persistentId)
        {
            if (player.IsMe)
                return true;

            if (state.Kind == SecurityKind.Safe)
            {
                return !string.IsNullOrEmpty(state.OwnerId) &&
                       string.Equals(
                           state.OwnerId,
                           persistentId,
                           StringComparison.Ordinal);
            }

            string job = EconomyJobResolver.GetJobIdFromPlayer(player);

            if (state.Kind == SecurityKind.Vault)
            {
                // Coffre de la banque : patron banquier uniquement.
                // Trésor municipal : maire et adjoints uniquement.
                string wanted = state.Treasury ? "mairie" : "banque";

                return string.Equals(job, wanted, StringComparison.Ordinal) &&
                       EconomyJobResolver.GetGradeIndexFromPlayer(player)
                           >= BossGradeIndex;
            }

            return string.Equals(job, "banque", StringComparison.Ordinal) ||
                   string.Equals(job, "mairie", StringComparison.Ordinal);
        }

        private static string WalletFor(HostState state)
        {
            if (state.Kind == SecurityKind.Vault)
                return state.Treasury ? TreasuryWalletId : VaultWalletId;

            if (state.Kind == SecurityKind.Safe &&
                !string.IsNullOrEmpty(state.OwnerId))
            {
                return "system:safe:" + state.OwnerId;
            }

            return null;
        }

        private static long ReadStock(string walletId)
        {
            if (string.IsNullOrEmpty(walletId) ||
                !EconomyRuntimeBootstrap.IsInitialized ||
                EconomyRuntimeBootstrap.Runtime == null)
            {
                return -1;
            }

            WalletData wallet =
                EconomyRuntimeBootstrap.Runtime.Wallets.GetWallet(walletId);

            return wallet != null ? wallet.BankCents : 0;
        }

        private static string PlayerName(PlayerID player)
        {
            string name = DarkRPNetwork.GetPlayerName(player.SmallID);

            return string.IsNullOrWhiteSpace(name)
                ? "Joueur " + player.SmallID
                : name;
        }

        internal static void HandleHostRequest(
            PlayerID sender, SecurityRequest request)
        {
            if (!IsHost() ||
                sender == null ||
                !sender.IsValid ||
                request == null ||
                request.T <= 0)
            {
                return;
            }

            // Toute demande invalide reçoit une réponse : le client ne doit
            // jamais attendre jusqu'au délai « le serveur ne répond pas ».
            if (string.IsNullOrWhiteSpace(request.O) ||
                request.O.Length > 64 ||
                request.M < 0 ||
                request.M > MaximumOperationCents ||
                !IsInitialized)
            {
                SendResponse(sender, request, false,
                    !IsInitialized
                        ? "Le serveur démarre, réessayez."
                        : "Demande invalide.", -1);
                return;
            }

            float now = Time.realtimeSinceStartup;
            bool angleRequest = request.A == (byte)SecurityAction.DoorAngle;

            lock (Sync)
            {
                float last;

                if (angleRequest)
                {
                    if (LastAngleRequest.TryGetValue(sender.SmallID, out last) &&
                        now - last < 0.06f)
                    {
                        return;
                    }

                    LastAngleRequest[sender.SmallID] = now;
                }
                else
                {
                    if (LastRequest.TryGetValue(sender.SmallID, out last) &&
                        now - last < MinimumRequestInterval)
                    {
                        SendResponse(sender, request, false,
                            "Trop de demandes, patientez.", -1);
                        return;
                    }

                    LastRequest[sender.SmallID] = now;
                }
            }

            string modelId;
            SecurityKind kind;
            GameObject body;

            if (!WorldModelRuntimeManager.TryGetModelId(
                    request.O, out modelId) ||
                !TryKind(modelId, out kind) ||
                !WorldModelRuntimeManager.TryGet(request.O, out body) ||
                body == null)
            {
                SendResponse(sender, request, false,
                    "Objet introuvable.", -1);
                return;
            }

            HostState state = GetHostState(request.O, kind);

            string persistentId;
            PlayerIdentityService.TryGetPersistentId(
                sender, out persistentId);

            bool manage = CanManage(sender, state, persistentId);

            string message;
            bool ok;
            long stock = -1;
            bool stateChanged = false;

            // Le coffre mural ne se manipule qu'au clavier et à la poignée.
            if (kind == SecurityKind.Safe &&
                !IsSafeAction((SecurityAction)request.A))
            {
                SendResponse(sender, request, false,
                    "Ce coffre se manipule physiquement (clavier et poignée).", -1);
                return;
            }

            switch ((SecurityAction)request.A)
            {
                case SecurityAction.Code:
                    ok = DoCode(
                        sender, state, persistentId, request.M,
                        out message, out stateChanged);
                    break;

                case SecurityAction.DoorAngle:
                    DoDoorAngle(state, request.M, out stateChanged);
                    ok = true;
                    message = "";
                    break;

                case SecurityAction.ResetCode:
                    ok = DoResetCode(
                        state, manage, out message, out stateChanged);
                    break;

                case SecurityAction.Toggle:
                    ok = DoToggle(state, out message, out stateChanged);
                    break;

                case SecurityAction.SetLock:
                    ok = DoSetLock(
                        state, manage, request.M == 1,
                        out message, out stateChanged);
                    break;

                case SecurityAction.Claim:
                    ok = DoClaim(
                        sender, state, persistentId,
                        out message, out stateChanged);
                    break;

                case SecurityAction.Release:
                    ok = DoRelease(
                        state, manage, out message, out stateChanged);
                    break;

                case SecurityAction.Deposit:
                case SecurityAction.Withdraw:
                    ok = DoMoney(
                        sender, state, manage, persistentId,
                        (SecurityAction)request.A, request.M,
                        out message, out stock);
                    break;

                case SecurityAction.Query:
                    ok = DoQuery(state, manage, out message, out stock);
                    break;

                case SecurityAction.ForceStart:
                    ok = DoForceStart(
                        sender, state, manage, request.O,
                        out message, out stateChanged);
                    break;

                case SecurityAction.ForceCancel:
                    ok = DoForceCancel(
                        sender, state, out message, out stateChanged);
                    break;

                default:
                    ok = false;
                    message = "Action inconnue.";
                    break;
            }

            if (stateChanged)
                BroadcastState(request.O, state);

            // Les mises à jour d'angle n'attendent aucune réponse.
            if (angleRequest)
                return;

            SendResponse(sender, request, ok, message, stock);
        }

        private static bool DoToggle(
            HostState state, out string message, out bool changed)
        {
            changed = false;

            if (state.Forcing)
            {
                message = "Crochetage en cours.";
                return false;
            }

            if (!state.Open && state.Locked)
            {
                message = "Verrouille : accès refusé.";
                return false;
            }

            state.Open = !state.Open;
            changed = true;
            message = state.Open ? "Ouvert." : "Ferme.";
            return true;
        }

        private static bool DoSetLock(
            HostState state, bool manage, bool lockIt,
            out string message, out bool changed)
        {
            changed = false;

            if (!manage)
            {
                message = "Vous n'avez pas l'autorisation.";
                return false;
            }

            if (state.Kind == SecurityKind.Safe &&
                string.IsNullOrEmpty(state.OwnerId))
            {
                message = "Coffre non réservé.";
                return false;
            }

            if (state.Forcing)
            {
                message = "Crochetage en cours.";
                return false;
            }

            state.Locked = lockIt;

            if (lockIt)
                state.Open = false;

            changed = true;
            message = lockIt ? "Verrouille." : "Deverrouille.";
            return true;
        }

        private static bool DoClaim(
            PlayerID sender, HostState state, string persistentId,
            out string message, out bool changed)
        {
            changed = false;

            if (state.Kind != SecurityKind.Safe)
            {
                message = "Action impossible ici.";
                return false;
            }

            if (!string.IsNullOrEmpty(state.OwnerId))
            {
                message = "Ce coffre a déjà un propriétaire.";
                return false;
            }

            if (string.IsNullOrEmpty(persistentId))
            {
                message = "Identité du joueur introuvable.";
                return false;
            }

            state.OwnerId = persistentId;
            state.OwnerName = PlayerName(sender);
            state.Locked = true;
            state.Open = false;
            changed = true;
            message = "Coffre réservé a votre nom.";
            return true;
        }

        private static bool DoRelease(
            HostState state, bool manage,
            out string message, out bool changed)
        {
            changed = false;

            if (state.Kind != SecurityKind.Safe || !manage)
            {
                message = "Vous n'avez pas l'autorisation.";
                return false;
            }

            if (ReadStock(WalletFor(state)) > 0)
            {
                message = "Videz le coffre avant de le libérer.";
                return false;
            }

            state.OwnerId = null;
            state.OwnerName = null;
            state.Locked = true;
            state.Open = false;
            changed = true;
            message = "Coffre libéré.";
            return true;
        }

        private static bool DoQuery(
            HostState state, bool manage,
            out string message, out long stock)
        {
            stock = -1;

            if (!manage)
            {
                message = "Vous n'avez pas l'autorisation.";
                return false;
            }

            stock = ReadStock(WalletFor(state));
            message = stock >= 0
                ? "Contenu : " + BankService.FormatAmount(stock)
                : "Service bancaire indisponible.";

            return stock >= 0;
        }

        private static bool DoMoney(
            PlayerID sender, HostState state, bool manage,
            string persistentId, SecurityAction action, long amount,
            out string message, out long stock)
        {
            stock = -1;

            if (!manage)
            {
                message = "Vous n'avez pas l'autorisation.";
                return false;
            }

            if (!state.Open || state.Locked)
            {
                message = "Ouvrez d'abord le coffre.";
                return false;
            }

            string walletId = WalletFor(state);

            if (string.IsNullOrEmpty(walletId) ||
                string.IsNullOrEmpty(persistentId) ||
                amount <= 0 ||
                !EconomyRuntimeBootstrap.IsInitialized ||
                EconomyRuntimeBootstrap.Runtime == null)
            {
                message = "Opération impossible.";
                return false;
            }

            WalletService wallets = EconomyRuntimeBootstrap.Runtime.Wallets;
            string error;
            bool moved;

            if (action == SecurityAction.Deposit)
            {
                moved = wallets.TryMoveCashToWalletBank(
                    true,
                    PhysicalCashRequestId.Create(),
                    persistentId,
                    walletId,
                    amount,
                    out error);
            }
            else
            {
                moved = wallets.TryMoveBankToWalletCash(
                    true,
                    PhysicalCashRequestId.Create(),
                    walletId,
                    persistentId,
                    amount,
                    out error);
            }

            stock = ReadStock(walletId);

            if (!moved)
            {
                message = string.IsNullOrEmpty(error)
                    ? "Opération refusée."
                    : error;
                return false;
            }

            MelonLogger.Msg(
                "[FusionDarkRP Securite] "
                + (action == SecurityAction.Deposit ? "Dépôt " : "Retrait ")
                + BankService.FormatAmount(amount)
                + " | " + walletId + " | " + persistentId);

            message = (action == SecurityAction.Deposit
                ? "Dépôt effectue : "
                : "Retrait effectue : ")
                + BankService.FormatAmount(amount);

            return true;
        }

        private static float ForceDuration(SecurityKind kind)
        {
            switch (kind)
            {
                case SecurityKind.Vault:
                    return ForceVaultSeconds;
                case SecurityKind.Safe:
                    return ForceSafeSeconds;
                default:
                    return ForceDoorSeconds;
            }
        }

        private static string KindLabel(SecurityKind kind)
        {
            switch (kind)
            {
                case SecurityKind.Vault:
                    return "coffre-fort";
                case SecurityKind.Safe:
                    return "coffre mural";
                default:
                    return "porte de sécurité";
            }
        }

        private static bool DoForceStart(
            PlayerID sender, HostState state, bool manage, string objectId,
            out string message, out bool changed)
        {
            changed = false;

            if (manage)
            {
                message = "Vous pouvez deverrouiller directement.";
                return false;
            }

            if (!state.Locked || state.Open)
            {
                message = "Rien a forcer.";
                return false;
            }

            if (state.Kind == SecurityKind.Safe &&
                string.IsNullOrEmpty(state.OwnerId))
            {
                message = "Ce coffre est vide et libre.";
                return false;
            }

            if (state.Forcing)
            {
                message = "Quelqu'un force déjà cette serrure.";
                return false;
            }

            state.Forcing = true;
            state.ForceBy = sender.SmallID;
            state.ForceEnd =
                Time.realtimeSinceStartup + ForceDuration(state.Kind);

            changed = true;

            Alert(
                PlayerName(sender) + " force un(e) "
                + KindLabel(state.Kind) + " !",
                2);

            MelonLogger.Warning(
                "[FusionDarkRP Securite] Crochetage : "
                + PlayerName(sender) + " | " + objectId);

            message = "Crochetage en cours... restez a proximité.";
            return true;
        }

        private static bool DoForceCancel(
            PlayerID sender, HostState state,
            out string message, out bool changed)
        {
            changed = false;

            if (!state.Forcing || state.ForceBy != sender.SmallID)
            {
                message = "Aucun crochetage a annuler.";
                return false;
            }

            state.Forcing = false;
            state.ForceBy = 255;
            changed = true;
            message = "Crochetage annulé.";
            return true;
        }

        private static void TickForcing()
        {
            List<KeyValuePair<string, HostState>> active =
                new List<KeyValuePair<string, HostState>>();

            lock (Sync)
            {
                foreach (KeyValuePair<string, HostState> pair in HostStates)
                {
                    if (pair.Value.Forcing)
                        active.Add(pair);
                }
            }

            foreach (KeyValuePair<string, HostState> pair in active)
            {
                HostState state = pair.Value;
                PlayerID robber = PlatformIds.GetPlayerID(state.ForceBy);

                if (robber == null || !robber.IsValid)
                {
                    state.Forcing = false;
                    state.ForceBy = 255;
                    BroadcastState(pair.Key, state);
                    continue;
                }

                if (Time.realtimeSinceStartup < state.ForceEnd)
                    continue;

                CompleteForce(pair.Key, state, robber);
            }
        }

        private static void CompleteForce(
            string objectId, HostState state, PlayerID robber)
        {
            state.Forcing = false;
            state.ForceBy = 255;
            state.Locked = false;
            state.Open = true;

            string detail = "";
            long stolen = 0;

            string walletId = WalletFor(state);
            string robberId;

            if (!string.IsNullOrEmpty(walletId) &&
                PlayerIdentityService.TryGetPersistentId(
                    robber, out robberId) &&
                EconomyRuntimeBootstrap.IsInitialized &&
                EconomyRuntimeBootstrap.Runtime != null)
            {
                long stock = ReadStock(walletId);
                long cap = state.Kind == SecurityKind.Vault
                    ? VaultHeistCapCents
                    : SafeHeistCapCents;

                long share = state.Kind == SecurityKind.Vault
                    ? stock / 5
                    : stock / 4;

                stolen = Math.Min(Math.Max(share, 0), cap);

                if (stolen > 0)
                {
                    string error;

                    if (!EconomyRuntimeBootstrap.Runtime.Wallets
                        .TryMoveBankToWalletCash(
                            true,
                            PhysicalCashRequestId.Create(),
                            walletId,
                            robberId,
                            stolen,
                            out error))
                    {
                        stolen = 0;
                    }
                }
            }

            if (stolen > 0)
                detail = " Butin : " + BankService.FormatAmount(stolen) + ".";

            Alert(
                PlayerName(robber) + " a force le(la) "
                + KindLabel(state.Kind) + " !" + detail,
                2);

            BroadcastState(objectId, state);

            SecurityResponse response = new SecurityResponse
            {
                T = 0,
                O = objectId,
                Ok = true,
                Msg = "Serrure forcée." + detail
            };

            SendToPlayer(robber, response);
        }

        // ------------------------------------------------------------
        // Hôte : diffusion
        // ------------------------------------------------------------

        private static SecurityState BuildState(
            string objectId, HostState state)
        {
            SecurityState result = new SecurityState
            {
                O = objectId,
                K = (byte)state.Kind,
                Open = state.Open,
                Locked = state.Locked,
                Owner = state.OwnerName,
                Forcing = state.Forcing
            };

            if (state.Kind == SecurityKind.Safe)
                FillSafeState(result, state);

            SecurityObject entry;

            lock (Sync)
                Objects.TryGetValue(objectId, out entry);

            if (entry != null && entry.HasBase)
            {
                result.HasBase = true;
                result.Bx = entry.BasePosition.x;
                result.By = entry.BasePosition.y;
                result.Bz = entry.BasePosition.z;
                result.Qx = entry.BaseRotation.x;
                result.Qy = entry.BaseRotation.y;
                result.Qz = entry.BaseRotation.z;
                result.Qw = entry.BaseRotation.w;
            }

            return result;
        }

        private static void BroadcastState(string objectId, HostState state)
        {
            if (!IsHost())
                return;

            SecurityState payload = BuildState(objectId, state);

            OnState(payload);

            try
            {
                MessageRelay.RelayModule<
                    BankSecurityStateHandler,
                    BankSecurityPacket>(
                    new BankSecurityPacket
                    {
                        Payload =
                            JsonSerializer.SerializeToUtf8Bytes(payload)
                    },
                    CommonMessageRoutes.ReliableToClients);
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Securite] Diffusion état : "
                    + exception);
            }
        }

        private static void Alert(string text, byte level)
        {
            if (!IsHost())
                return;

            SecurityAlert alert = new SecurityAlert
            {
                Text = text,
                Level = level
            };

            OnAlert(alert);

            try
            {
                MessageRelay.RelayModule<
                    BankSecurityAlertHandler,
                    BankSecurityPacket>(
                    new BankSecurityPacket
                    {
                        Payload =
                            JsonSerializer.SerializeToUtf8Bytes(alert)
                    },
                    CommonMessageRoutes.ReliableToClients);
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Securite] Diffusion alerte : "
                    + exception);
            }
        }

        private static void SendResponse(
            PlayerID target, SecurityRequest request, bool ok,
            string message, long stock)
        {
            SendToPlayer(target, new SecurityResponse
            {
                T = request.T,
                O = request.O,
                Ok = ok,
                Msg = message,
                Stock = stock
            });
        }

        private static void SendToPlayer(
            PlayerID target, SecurityResponse response)
        {
            if (target == null || !target.IsValid)
                return;

            if (target.IsMe)
            {
                OnResponse(response);
                return;
            }

            try
            {
                MessageRelay.RelayModule<
                    BankSecurityResponseHandler,
                    BankSecurityPacket>(
                    new BankSecurityPacket
                    {
                        Payload =
                            JsonSerializer.SerializeToUtf8Bytes(response)
                    },
                    new MessageRoute(
                        target.SmallID,
                        NetworkChannel.Reliable));
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Securite] Envoi réponse : "
                    + exception.Message);
            }
        }

        private static void OnPlayerJoined(PlayerID joined)
        {
            if (!IsHost() ||
                joined == null ||
                !joined.IsValid ||
                joined.IsMe)
            {
                return;
            }

            List<KeyValuePair<string, HostState>> snapshot;

            lock (Sync)
                snapshot = new List<KeyValuePair<string, HostState>>(
                    HostStates);

            foreach (KeyValuePair<string, HostState> pair in snapshot)
            {
                try
                {
                    MessageRelay.RelayModule<
                        BankSecurityStateHandler,
                        BankSecurityPacket>(
                        new BankSecurityPacket
                        {
                            Payload = JsonSerializer.SerializeToUtf8Bytes(
                                BuildState(pair.Key, pair.Value))
                        },
                        new MessageRoute(
                            joined.SmallID,
                            NetworkChannel.Reliable));
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Securite] Envoi au nouveau joueur : "
                        + exception.Message);
                }
            }
        }

        // ====================================================================
        // Objet côté client (panneaux + animation)
        // ====================================================================

        private sealed partial class SecurityObject
        {
            private const float ScreenWidth = 500f;
            private const float ScreenHeight = 750f;
            private const float PhysicalWidth = 0.40f;
            private const float PhysicalHeight = 0.60f;

            private readonly string objectId;
            private readonly SecurityKind kind;
            private readonly GameObject body;
            private readonly List<Panel> panels = new List<Panel>();

            private bool open;
            private bool locked;
            private bool forcing;
            private string owner;
            private string message = "";
            private long stock = -1;
            private long pendingToken;
            private float requestDeadline;

            private Bounds localBounds;
            private float openAmount;
            private bool disposed;

            public bool HasBase { get; private set; }
            public Vector3 BasePosition { get; private set; }
            public Quaternion BaseRotation { get; private set; }

            public bool IsDestroyed
            {
                get { return disposed || body == null; }
            }

            public SecurityObject(
                string objectId, SecurityKind kind, GameObject body)
            {
                this.objectId = objectId;
                this.kind = kind;
                this.body = body;

                locked = kind != SecurityKind.Door;

                localBounds = ComputeLocalBounds(body);

                BasePosition = body.transform.position;
                BaseRotation = body.transform.rotation;
                HasBase = true;

                if (kind != SecurityKind.Safe)
                    panels.Add(new Panel(this, false));

                if (kind == SecurityKind.Door)
                    panels.Add(new Panel(this, true));

                SecurityState pending;

                lock (Sync)
                {
                    if (PendingStates.TryGetValue(objectId, out pending))
                        PendingStates.Remove(objectId);
                }

                if (pending != null)
                    ApplyState(pending);
            }

            // -------- Bounds locaux (indépendants de la pose) --------

            private static Bounds ComputeLocalBounds(GameObject root)
            {
                Renderer[] renderers =
                    root.GetComponentsInChildren<Renderer>(true);

                bool has = false;
                Bounds result = new Bounds(Vector3.zero, Vector3.zero);

                foreach (Renderer renderer in renderers)
                {
                    if (renderer == null)
                        continue;

                    Bounds world = renderer.bounds;
                    Vector3 c = world.center;
                    Vector3 e = world.extents;

                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 corner = c + new Vector3(
                            (i & 1) == 0 ? -e.x : e.x,
                            (i & 2) == 0 ? -e.y : e.y,
                            (i & 4) == 0 ? -e.z : e.z);

                        Vector3 local =
                            root.transform.InverseTransformPoint(corner);

                        if (!has)
                        {
                            result = new Bounds(local, Vector3.zero);
                            has = true;
                        }
                        else
                        {
                            result.Encapsulate(local);
                        }
                    }
                }

                if (!has)
                    result = new Bounds(
                        new Vector3(0f, 1f, 0f), new Vector3(1f, 2f, 0.2f));

                return result;
            }

            // -------- Animation de la porte --------

            public void Animate(float deltaTime)
            {
                if (disposed || body == null)
                    return;

                CheckTimeout();

                if (kind == SecurityKind.Safe)
                {
                    TickSafe(deltaTime);
                    return;
                }

                if (kind != SecurityKind.Door)
                    return;

                float target = open ? 1f : 0f;

                if (Mathf.Approximately(openAmount, target))
                    return;

                openAmount = Mathf.MoveTowards(
                    openAmount, target, deltaTime / DoorSeconds);

                float eased = Mathf.SmoothStep(0f, 1f, openAmount);

                bool hingeRight = hingeRightEntry != null &&
                                  hingeRightEntry.Value;

                float angle = openAngleEntry != null
                    ? openAngleEntry.Value
                    : 90f;

                angle = Mathf.Clamp(angle, 10f, 170f);
                angle = hingeRight ? angle : -angle;

                Vector3 hingeLocal = new Vector3(
                    hingeRight ? localBounds.max.x : localBounds.min.x,
                    0f,
                    localBounds.center.z);

                Vector3 hingeWorld =
                    BasePosition +
                    BaseRotation *
                    Vector3.Scale(hingeLocal, body.transform.localScale);

                Quaternion rotation =
                    Quaternion.AngleAxis(angle * eased, Vector3.up);

                body.transform.position =
                    hingeWorld + rotation * (BasePosition - hingeWorld);

                body.transform.rotation = rotation * BaseRotation;
            }

            private void CheckTimeout()
            {
                if (pendingToken == 0 ||
                    Time.realtimeSinceStartup < requestDeadline)
                {
                    return;
                }

                pendingToken = 0;
                message = "Le serveur ne répond pas.";
                Redraw();
            }

            // -------- Application des messages --------

            public void ApplyState(SecurityState state)
            {
                if (disposed || state == null)
                    return;

                if (state.HasBase && kind == SecurityKind.Door)
                {
                    BasePosition = new Vector3(state.Bx, state.By, state.Bz);

                    BaseRotation = new Quaternion(
                        state.Qx, state.Qy, state.Qz, state.Qw);

                    if (BaseRotation.x == 0f && BaseRotation.y == 0f &&
                        BaseRotation.z == 0f && BaseRotation.w == 0f)
                    {
                        BaseRotation = Quaternion.identity;
                    }

                    BaseRotation = BaseRotation.normalized;
                }

                bool wasForcing = forcing;

                open = state.Open;
                locked = state.Locked;
                owner = state.Owner;
                forcing = state.Forcing;

                if (kind == SecurityKind.Safe)
                    ApplySafeState(state);

                if (!open)
                    stock = -1;

                if (wasForcing && !forcing && string.IsNullOrEmpty(message))
                    message = "";

                Redraw();
            }

            public void ApplyResponse(SecurityResponse response)
            {
                if (disposed || response == null)
                    return;

                if (response.T != 0 &&
                    pendingToken != -1 &&
                    response.T != pendingToken)
                    return;

                pendingToken = 0;
                message = response.Msg ?? "";

                if (message.Length > 0)
                    RpHub.Notify(message, (byte)0);

                if (response.Stock >= 0)
                    stock = response.Stock;

                Redraw();
            }

            // -------- Actions du panneau --------

            private void Send(SecurityAction action, long amountCents)
            {
                if (disposed)
                    return;

                if (pendingToken != 0)
                {
                    message = "Une demande est déjà en cours.";
                    Redraw();
                    return;
                }

                // -1 : émission en cours (l'hôte répond avant le retour).
                pendingToken = -1;

                long token = Request(objectId, action, amountCents);

                if (token == 0)
                {
                    pendingToken = 0;
                    message = "Serveur indisponible.";
                    Redraw();
                    return;
                }

                if (pendingToken == -1)
                {
                    pendingToken = token;
                    requestDeadline = Time.realtimeSinceStartup + 12f;
                    message = "Envoi...";
                    Redraw();
                }
            }

            private bool LocalCanManage()
            {
                PlayerID local = PlayerIDManager.LocalID;

                if (local == null || !local.IsValid)
                    return false;

                if (NetworkInfo.IsHost)
                    return true;

                if (kind == SecurityKind.Safe)
                {
                    string name = DarkRPNetwork.GetPlayerName(local.SmallID);

                    return !string.IsNullOrEmpty(owner) &&
                           string.Equals(
                               owner, name, StringComparison.Ordinal);
                }

                string job = EconomyJobResolver.GetJobIdFromPlayer(local);

                if (kind == SecurityKind.Vault)
                {
                    string wanted = IsTreasuryId(objectId)
                        ? "mairie" : "banque";

                    return job == wanted &&
                           EconomyJobResolver.GetGradeIndexFromPlayer(local)
                               >= BossGradeIndex;
                }

                return job == "banque" || job == "mairie";
            }

            // -------- Rendu --------

            public void Redraw()
            {
                foreach (Panel panel in panels)
                    panel.Draw();
            }

            public void Dispose()
            {
                if (disposed)
                    return;

                disposed = true;

                foreach (Panel panel in panels)
                    panel.Dispose();

                panels.Clear();

                DisposeSafe();
            }

            private string Title()
            {
                switch (kind)
                {
                    case SecurityKind.Vault:
                        return IsTreasuryId(objectId)
                            ? "TRESOR MUNICIPAL"
                            : "COFFRE-FORT BANCAIRE";
                    case SecurityKind.Safe:
                        return "COFFRE MURAL";
                    default:
                        return "PORTE DE SECURITE";
                }
            }

            // ---------------------------------------------------------

            private sealed class Panel
            {
                private readonly SecurityObject owner;
                private readonly GameObject root;
                private GameObject canvasObject;
                private GameObject content;
                private LaserCursor cursor;
                private bool disposed;

                public Panel(SecurityObject owner, bool back)
                {
                    this.owner = owner;

                    GameObject body = owner.body;
                    Bounds b = owner.localBounds;

                    root = new GameObject("FusionDarkRP_Security_Panel");
                    root.transform.SetParent(body.transform, false);

                    float inv = 1f / Mathf.Max(
                        0.0001f, Mathf.Abs(body.transform.lossyScale.x));

                    float y = Mathf.Clamp(
                        b.min.y + 1.15f,
                        b.min.y + 0.3f,
                        Mathf.Max(b.min.y + 0.3f, b.max.y - 0.35f));

                    float z = back ? b.max.z + 0.03f : b.min.z - 0.03f;

                    root.transform.localPosition =
                        new Vector3(b.center.x, y, z) ;

                    root.transform.localRotation = back
                        ? Quaternion.Euler(0f, 180f, 0f)
                        : Quaternion.identity;

                    root.transform.localScale = Vector3.one;

                    canvasObject =
                        new GameObject("FusionDarkRP_Security_Canvas");

                    canvasObject.layer = 5;

                    RectTransform rect =
                        canvasObject.AddComponent<RectTransform>();

                    rect.SetParent(root.transform, false);
                    rect.sizeDelta = new Vector2(ScreenWidth, ScreenHeight);
                    rect.localPosition = Vector3.zero;
                    rect.localRotation = Quaternion.identity;
                    rect.localScale = new Vector3(
                        PhysicalWidth / ScreenWidth * inv,
                        PhysicalHeight / ScreenHeight * inv,
                        PhysicalWidth / ScreenWidth * inv);

                    Canvas canvas = canvasObject.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.WorldSpace;
                    canvas.sortingOrder = 22;

                    canvasObject.AddComponent<GraphicRaycaster>();

                    Draw();

                    LaserCursorUtilities.CreateLaserCursor(
                        (Action<LaserCursor>)delegate(LaserCursor ready)
                        {
                            if (disposed || root == null || ready == null)
                            {
                                if (ready != null)
                                {
                                    UnityEngine.Object.Destroy(
                                        ((Component)ready).gameObject);
                                }

                                return;
                            }

                            cursor = ready;

                            Transform t = ((Component)cursor).transform;
                            t.SetParent(root.transform, false);
                            t.localPosition = Vector3.zero;
                            t.localRotation = Quaternion.identity;

                            cursor.regions =
                                new LaserCursor.CursorRegion[]
                                {
                                    new LaserCursor.CursorRegion
                                    {
                                        bounds = new Bounds(
                                            Vector3.zero,
                                            Vector3.Scale(
                                                new Vector3(
                                                    ScreenWidth,
                                                    ScreenHeight,
                                                    80f),
                                                canvasObject.transform
                                                    .lossyScale)),
                                        center = canvasObject.transform
                                    }
                                };

                            ((Component)cursor).gameObject.SetActive(true);
                        });
                }

                public void Dispose()
                {
                    if (disposed)
                        return;

                    disposed = true;

                    if (root != null)
                        UnityEngine.Object.Destroy(root);
                }

                // ---- Dessin ----

                public void Draw()
                {
                    if (disposed || canvasObject == null)
                        return;

                    if (content != null)
                    {
                        content.SetActive(false);
                        UnityEngine.Object.Destroy(content);
                    }

                    content = new GameObject("SEC_PAGE");
                    content.layer = 5;

                    RectTransform page =
                        content.AddComponent<RectTransform>();

                    page.SetParent(canvasObject.transform, false);
                    page.sizeDelta = new Vector2(ScreenWidth, ScreenHeight);

                    Image bg = Element("BG", 0f, 0f, ScreenWidth, ScreenHeight)
                        .AddComponent<Image>();

                    bg.color = new Color(0.04f, 0.05f, 0.08f, 1f);
                    bg.raycastTarget = false;

                    Label(owner.Title(), 0f, 330f, 470f, 60f, 34f,
                        new Color(0.95f, 0.8f, 0.25f));

                    string state = owner.forcing
                        ? "CROCHETAGE EN COURS"
                        : owner.locked
                            ? "VERROUILLE"
                            : owner.open ? "OUVERT" : "FERME";

                    Color stateColor = owner.forcing
                        ? new Color(1f, 0.55f, 0.1f)
                        : owner.locked
                            ? new Color(1f, 0.3f, 0.3f)
                            : new Color(0.3f, 1f, 0.45f);

                    Panel2(0f, 235f, 460f, 140f,
                        new Color(0.09f, 0.11f, 0.16f));

                    Label(state, 0f, 275f, 440f, 45f, 30f, stateColor);

                    string info = owner.message;

                    if (owner.kind == SecurityKind.Safe)
                    {
                        string who = string.IsNullOrEmpty(owner.owner)
                            ? "Libre (non réservé)"
                            : "Propriétaire : " + owner.owner;

                        info = who + (string.IsNullOrEmpty(info)
                            ? "" : "\n" + info);
                    }

                    if (owner.stock >= 0)
                    {
                        info = "Contenu : "
                            + BankService.FormatAmount(owner.stock)
                            + (string.IsNullOrEmpty(info)
                                ? "" : "\n" + info);
                    }

                    Label(info, 0f, 205f, 440f, 80f, 20f, Color.white);

                    DrawButtons();
                }

                private void DrawButtons()
                {
                    bool manage = owner.LocalCanManage();
                    bool isSafe = owner.kind == SecurityKind.Safe;
                    bool isVault = owner.kind == SecurityKind.Vault;
                    bool hasStock = isSafe || isVault;

                    float y = 105f;
                    const float step = 78f;

                    Color blue = new Color(0.12f, 0.35f, 0.65f);
                    Color green = new Color(0.1f, 0.5f, 0.25f);
                    Color red = new Color(0.65f, 0.15f, 0.15f);
                    Color grey = new Color(0.25f, 0.28f, 0.35f);

                    if (isSafe && string.IsNullOrEmpty(owner.owner))
                    {
                        Button("RESERVER CE COFFRE", 0f, y, 440f, 64f,
                            () => owner.Send(SecurityAction.Claim, 0), green);
                        y -= step;
                    }

                    Button(owner.open ? "FERMER" : "OUVRIR",
                        0f, y, 440f, 64f,
                        () => owner.Send(SecurityAction.Toggle, 0), blue);
                    y -= step;

                    if (manage)
                    {
                        Button(owner.locked ? "DEVERROUILLER" : "VERROUILLER",
                            0f, y, 440f, 64f,
                            () => owner.Send(
                                SecurityAction.SetLock,
                                owner.locked ? 0 : 1),
                            owner.locked ? green : red);
                        y -= step;
                    }

                    if (hasStock && manage)
                    {
                        Button("CONSULTER LE CONTENU", 0f, y, 440f, 64f,
                            () => owner.Send(SecurityAction.Query, 0), grey);
                        y -= step;

                        long[] amounts = isVault
                            ? new long[] { 10000, 50000, 100000 }
                            : new long[] { 5000, 10000, 50000 };

                        for (int i = 0; i < 3; i++)
                        {
                            long amount = amounts[i];
                            float x = -150f + i * 150f;

                            Button("DEPOT " + (amount / 100) + " EUR",
                                x, y, 142f, 64f,
                                () => owner.Send(
                                    SecurityAction.Deposit, amount),
                                green);
                        }

                        y -= step;

                        for (int i = 0; i < 3; i++)
                        {
                            long amount = amounts[i];
                            float x = -150f + i * 150f;

                            Button("RETRAIT " + (amount / 100) + " EUR",
                                x, y, 142f, 64f,
                                () => owner.Send(
                                    SecurityAction.Withdraw, amount),
                                red);
                        }

                        y -= step;

                        if (isSafe)
                        {
                            Button("LIBERER LE COFFRE", 0f, y, 440f, 64f,
                                () => owner.Send(SecurityAction.Release, 0),
                                grey);
                            y -= step;
                        }
                    }

                    if (!manage && owner.locked && !owner.open)
                    {
                        if (owner.forcing)
                        {
                            Button("ANNULER LE CROCHETAGE",
                                0f, y, 440f, 64f,
                                () => owner.Send(
                                    SecurityAction.ForceCancel, 0),
                                grey);
                        }
                        else
                        {
                            Button("FORCER LA SERRURE", 0f, y, 440f, 64f,
                                () => owner.Send(
                                    SecurityAction.ForceStart, 0),
                                new Color(0.75f, 0.4f, 0.05f));
                        }
                    }
                }

                // ---- Éléments UI (même rendu que l'ATM) ----

                private GameObject Element(
                    string name, float x, float y, float w, float h)
                {
                    GameObject element = new GameObject(name);
                    element.layer = 5;

                    RectTransform rect =
                        element.AddComponent<RectTransform>();

                    rect.SetParent(content.transform, false);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.anchorMin = new Vector2(0.5f, 0.5f);
                    rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.anchoredPosition = new Vector2(x, y);
                    rect.sizeDelta = new Vector2(w, h);

                    return element;
                }

                private void Panel2(
                    float x, float y, float w, float h, Color color)
                {
                    Image image = Element("SEC_Panel", x, y, w, h)
                        .AddComponent<Image>();

                    image.color = color;
                    image.raycastTarget = false;
                }

                private void Label(
                    string value, float x, float y, float w, float h,
                    float size, Color color)
                {
                    TextMeshProUGUI label =
                        Element("SEC_Label", x, y, w, h)
                            .AddComponent<TextMeshProUGUI>();

                    label.font = TMP_Settings.defaultFontAsset;
                    label.text = value ?? "";
                    label.fontSize = size;
                    label.color = color;
                    label.richText = false;
                    label.enableWordWrapping = true;
                    label.alignment = TextAlignmentOptions.Center;
                    label.raycastTarget = false;
                }

                private void Button(
                    string text, float x, float y, float w, float h,
                    Action action, Color color)
                {
                    GameObject buttonObject =
                        Element("SEC_Button", x, y, w, h);

                    Image image = buttonObject.AddComponent<Image>();
                    image.color = color;
                    image.raycastTarget = false;

                    UnityEngine.UI.Button uiButton =
                        buttonObject.AddComponent<UnityEngine.UI.Button>();

                    uiButton.targetGraphic = image;

                    UIMachineUtilities.AddClickEvent(uiButton, action);

                    GameObject labelObject =
                        new GameObject("SEC_Button_Label");

                    labelObject.layer = 5;

                    RectTransform labelRect =
                        labelObject.AddComponent<RectTransform>();

                    labelRect.SetParent(buttonObject.transform, false);
                    labelRect.sizeDelta = new Vector2(w - 10f, h - 8f);

                    TextMeshProUGUI label =
                        labelObject.AddComponent<TextMeshProUGUI>();

                    label.font = TMP_Settings.defaultFontAsset;
                    label.text = text;
                    label.fontSize = w < 200f ? 18f : 24f;
                    label.color = Color.white;
                    label.richText = false;
                    label.alignment = TextAlignmentOptions.Center;
                    label.raycastTarget = false;

                    GameObject hit = new GameObject("HIT");
                    hit.layer = 5;
                    hit.transform.SetParent(buttonObject.transform, false);

                    BoxCollider collider = hit.AddComponent<BoxCollider>();
                    collider.size = new Vector3(w, h, 12f);
                }
            }
        }
    }
}
