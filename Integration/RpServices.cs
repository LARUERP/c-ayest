using System;
using System.Collections.Generic;
using System.Text.Json;
using FusionDarkRP.Economy;
using FusionDarkRP.RP;
using LabFusion.Entities;
using LabFusion.Network;
using LabFusion.Player;
using LabFusion.Utilities;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    public sealed class MissionInfo
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Briefing { get; set; }
        public long Reward { get; set; }
        public string[] Steps { get; set; }
        public int Step { get; set; }
        public bool Done { get; set; }
        public string Instruction { get; set; }
    }

    public sealed class VehicleInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public long Price { get; set; }
        public int Access { get; set; }
        public bool Installed { get; set; }
    }

    public sealed class InvoiceInfo
    {
        public string Id { get; set; }
        public string From { get; set; }
        public long Amount { get; set; }
        public string Label { get; set; }
        public int Secs { get; set; }
    }

    public sealed class WantedInfo
    {
        public byte Id { get; set; }
        public string Name { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Services RP côté hôte. Toutes les actions sont validées ici :
    /// métier, distance entre joueurs, cadence, montants et solde.
    /// </summary>
    public static class RpServices
    {
        public const string TreasuryWallet = "system:town_treasury";

        private const float InvoiceLifetime = 180f;
        private const float MissionStepDelay = 20f;
        private const float NearInvoice = 8f;
        private const float NearPolice = 6f;
        private const float NearCare = 4f;
        private const long MinInvoiceCents = 100L;
        private const long MaxInvoiceCents = 100000L;
        private const long ArrestFineCents = 50000L;
        private const long ArrestBountyCents = 10000L;
        private const long CareRewardCents = 1000L;
        private const int MaxInvoicesPerIssuer = 5;

        private static readonly long[] AllowedFines =
            { 5000L, 10000L, 25000L, 50000L, 100000L };

        private sealed class Invoice
        {
            public string Id;
            public byte FromId;
            public string FromPid;
            public string FromName;
            public byte ToId;
            public long Amount;
            public string Label;
            public float Created;
            public string VehicleId;     // vente de véhicule : livré au paiement
        }

        private sealed class Wanted
        {
            public string Name;
            public string Reason;
            public byte By;
        }

        private static readonly object Sync = new object();

        private static readonly Dictionary<string, Invoice> Invoices =
            new Dictionary<string, Invoice>(StringComparer.Ordinal);

        private static readonly Dictionary<byte, Wanted> WantedList =
            new Dictionary<byte, Wanted>();

        private static readonly Dictionary<string, float> Cooldowns =
            new Dictionary<string, float>(StringComparer.Ordinal);

        private static bool hooked;

        // ------------------------------------------------------------
        // Cycle de vie
        // ------------------------------------------------------------

        public static void Initialize()
        {
            RpHub.RegisterHost("mission.list", MissionList);
            RpHub.RegisterHost("mission.start", MissionStart);
            RpHub.RegisterHost("mission.advance", MissionAdvance);
            RpHub.RegisterHost("mission.abandon", MissionAbandon);

            RpHub.RegisterHost("veh.sell", VehicleSell);
            RpHub.RegisterHost("invoice.create", InvoiceCreate);
            RpHub.RegisterHost("invoice.list", InvoiceList);
            RpHub.RegisterHost("invoice.pay", InvoicePay);
            RpHub.RegisterHost("invoice.refuse", InvoiceRefuse);

            RpHub.RegisterHost("fine", Fine);
            RpHub.RegisterHost("wanted.set", WantedSet);
            RpHub.RegisterHost("wanted.list", WantedListAction);
            RpHub.RegisterHost("wanted.arrest", WantedArrest);

            RpHub.RegisterHost("care.heal", CareHeal);

            JobPerks.RegisterHandlers();
            DrugFarmService.RegisterHandlers();

            RpHub.RegisterHost("admin.give", AdminGive);
            RpHub.RegisterHost("admin.balance", AdminBalance);
            RpHub.RegisterHost("admin.treasury", AdminTreasury);

            if (!hooked)
            {
                MultiplayerHooking.OnPlayerLeft += OnPlayerLeft;
                MultiplayerHooking.OnDisconnected += OnDisconnected;
                hooked = true;
            }
        }

        public static void Shutdown()
        {
            if (hooked)
            {
                try
                {
                    MultiplayerHooking.OnPlayerLeft -= OnPlayerLeft;
                    MultiplayerHooking.OnDisconnected -= OnDisconnected;
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP RP] Nettoyage hooks : "
                        + exception.Message);
                }

                hooked = false;
            }

            Clear();
        }

        private static void Clear()
        {
            lock (Sync)
            {
                Invoices.Clear();
                WantedList.Clear();
                Cooldowns.Clear();
            }
        }

        private static void OnDisconnected()
        {
            Clear();
        }

        private static void OnPlayerLeft(PlayerID player)
        {
            if (player == null)
                return;

            byte id = player.SmallID;

            lock (Sync)
            {
                WantedList.Remove(id);

                List<string> remove = new List<string>();

                foreach (KeyValuePair<string, Invoice> pair in Invoices)
                {
                    if (pair.Value.FromId == id || pair.Value.ToId == id)
                        remove.Add(pair.Key);
                }

                foreach (string key in remove)
                    Invoices.Remove(key);
            }
        }

        // ------------------------------------------------------------
        // Utilitaires
        // ------------------------------------------------------------

        private static RpResponse Fail(string message)
        {
            return new RpResponse { Ok = false, Msg = message };
        }

        private static RpResponse Ok(string message, string data = null)
        {
            return new RpResponse { Ok = true, Msg = message, D = data };
        }

        private static string Job(PlayerID player)
        {
            return EconomyJobResolver.GetJobIdFromPlayer(player);
        }

        private static string Name(PlayerID player)
        {
            string name = DarkRPNetwork.GetPlayerName(player.SmallID);

            return string.IsNullOrWhiteSpace(name)
                ? "Joueur " + player.SmallID
                : name;
        }

        private static string Money(long cents)
        {
            return BankService.FormatAmount(cents);
        }

        private static bool Runtime(out EconomyRuntime runtime)
        {
            runtime = null;

            if (!EconomyRuntimeBootstrap.IsInitialized)
                return false;

            runtime = EconomyRuntimeBootstrap.Runtime;

            return runtime != null && runtime.Wallets != null;
        }

        private static bool Pid(PlayerID player, out string persistentId)
        {
            return PlayerIdentityService.TryGetPersistentId(
                player, out persistentId);
        }

        private static PlayerID Target(RpRequest request)
        {
            if (request.S == 255)
                return null;

            PlayerID target = PlatformIds.GetPlayerID(request.S);

            return target != null && target.IsValid ? target : null;
        }

        private static bool Head(PlayerID player, out Vector3 position)
        {
            position = Vector3.zero;

            try
            {
                if (player.IsMe)
                {
                    Transform head = BoneLib.Player.Head;

                    if (head == null)
                        return false;

                    position = head.position;
                    return true;
                }

                NetworkPlayer networkPlayer;

                if (NetworkPlayerManager.TryGetPlayer(
                        player.SmallID, out networkPlayer) &&
                    networkPlayer != null &&
                    networkPlayer.HasRig &&
                    networkPlayer.RigRefs.Head != null)
                {
                    position = networkPlayer.RigRefs.Head.position;
                    return true;
                }
            }
            catch
            {
                // Position inconnue.
            }

            return false;
        }

        /// <summary>
        /// Contrôle de proximité. Si une position est inconnue, l'action
        /// n'est pas bloquée (la position d'un joueur peut être
        /// temporairement indisponible).
        /// </summary>
        private static bool Near(PlayerID a, PlayerID b, float meters)
        {
            Vector3 pa;
            Vector3 pb;

            if (!Head(a, out pa) || !Head(b, out pb))
                return true;

            return Vector3.Distance(pa, pb) <= meters;
        }

        private static bool Cooldown(string key, float seconds)
        {
            float now = Time.realtimeSinceStartup;

            lock (Sync)
            {
                float until;

                if (Cooldowns.TryGetValue(key, out until) && now < until)
                    return false;

                Cooldowns[key] = now + seconds;

                if (Cooldowns.Count > 512)
                {
                    List<string> old = new List<string>();

                    foreach (KeyValuePair<string, float> pair in Cooldowns)
                    {
                        if (pair.Value < now)
                            old.Add(pair.Key);
                    }

                    foreach (string k in old)
                        Cooldowns.Remove(k);
                }

                return true;
            }
        }

        private static bool IsPolice(PlayerID player)
        {
            return player.IsMe || Job(player) == "police";
        }

        private static string CleanText(string text, int max)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            StringBuilderLite builder = new StringBuilderLite(max);

            foreach (char c in text.Trim())
            {
                if (char.IsControl(c))
                    continue;

                if (!builder.Add(c))
                    break;
            }

            return builder.ToString();
        }

        private sealed class StringBuilderLite
        {
            private readonly char[] buffer;
            private int length;

            public StringBuilderLite(int max)
            {
                buffer = new char[max];
            }

            public bool Add(char c)
            {
                if (length >= buffer.Length)
                    return false;

                buffer[length++] = c;
                return true;
            }

            public override string ToString()
            {
                return new string(buffer, 0, length);
            }
        }

        // ------------------------------------------------------------
        // Argent : débit citoyen vers le trésor municipal
        // ------------------------------------------------------------

        /// <summary>
        /// Prélève jusqu'à "cents" (banque d'abord, puis espèces) et verse
        /// au trésor. Retourne le montant réellement prélevé.
        /// </summary>
        private static long DebitToTreasury(
            EconomyRuntime runtime, string pid, long cents)
        {
            WalletData wallet = runtime.Wallets.GetWallet(pid);

            if (wallet == null || cents <= 0)
                return 0;

            long fromBank = Math.Min(wallet.BankCents, cents);
            long fromCash = Math.Min(wallet.CashCents, cents - fromBank);
            long taken = 0;
            string error;

            if (fromBank > 0 &&
                runtime.Wallets.TryTransfer(
                    true,
                    PhysicalCashRequestId.Create(),
                    pid,
                    TreasuryWallet,
                    fromBank,
                    out error))
            {
                taken += fromBank;
            }

            if (fromCash > 0 &&
                runtime.Wallets.TryMoveCashToWalletBank(
                    true,
                    PhysicalCashRequestId.Create(),
                    pid,
                    TreasuryWallet,
                    fromCash,
                    out error))
            {
                taken += fromCash;
            }

            return taken;
        }

        // ------------------------------------------------------------
        // Missions
        // ------------------------------------------------------------

        private static RpResponse MissionList(
            PlayerID sender, RpRequest request)
        {
            EconomyRuntime runtime;
            string pid;

            if (!Runtime(out runtime) || runtime.Missions == null ||
                !Pid(sender, out pid))
            {
                return Fail("Service de missions indisponible.");
            }

            string job = Job(sender);
            List<MissionInfo> list = new List<MissionInfo>();

            foreach (JobMissionDefinition mission in
                     JobMissionCatalog.ForJob(job))
            {
                MissionRun run = runtime.Missions.GetActive(pid, mission.Id);

                string[] steps = new string[mission.Steps.Length];

                for (int i = 0; i < steps.Length; i++)
                    steps[i] = mission.Steps[i].Title;

                int step = -1;
                bool done = false;
                string instruction = null;

                if (run != null)
                {
                    if (run.Completed)
                    {
                        done = true;
                        step = steps.Length;
                    }
                    else
                    {
                        step = run.CurrentStep;

                        if (step >= 0 && step < mission.Steps.Length)
                            instruction = mission.Steps[step].Instruction;
                    }
                }

                list.Add(new MissionInfo
                {
                    Id = mission.Id,
                    Title = mission.Title,
                    Briefing = mission.Briefing,
                    Reward = mission.RewardCents,
                    Steps = steps,
                    Step = step,
                    Done = done,
                    Instruction = instruction
                });
            }

            return Ok(
                list.Count + " mission(s) disponible(s).",
                JsonSerializer.Serialize(list));
        }

        private static RpResponse MissionStart(
            PlayerID sender, RpRequest request)
        {
            EconomyRuntime runtime;
            string pid;

            if (!Runtime(out runtime) || runtime.Missions == null ||
                !Pid(sender, out pid) ||
                string.IsNullOrWhiteSpace(request.A))
            {
                return Fail("Service de missions indisponible.");
            }

            try
            {
                runtime.Missions.Start(true, pid, Job(sender), request.A);

                Cooldown("mission|" + pid + "|" + request.A, MissionStepDelay);

                MissionWorld.OnMissionStarted(sender, pid, request.A);

                return Ok("Mission démarrée : suivez l'objectif affiché "
                    + "dans les notifications (balises, objets à ramasser).");
            }
            catch (Exception exception)
            {
                return Fail(exception.Message);
            }
        }

        private static RpResponse MissionAbandon(
            PlayerID sender, RpRequest request)
        {
            EconomyRuntime runtime;
            string pid;

            if (!Runtime(out runtime) || runtime.Missions == null ||
                !Pid(sender, out pid) ||
                string.IsNullOrWhiteSpace(request.A))
            {
                return Fail("Service de missions indisponible.");
            }

            MissionRun run = runtime.Missions.GetActive(pid, request.A);

            if (run == null || run.Completed)
                return Fail("Aucune mission en cours à abandonner.");

            MissionWorld.Abandon(pid, request.A);

            return Ok("Mission abandonnée. Vous pourrez la relancer.");
        }

        private static RpResponse MissionAdvance(
            PlayerID sender, RpRequest request)
        {
            EconomyRuntime runtime;
            string pid;

            if (!Runtime(out runtime) || runtime.Missions == null ||
                !Pid(sender, out pid) ||
                string.IsNullOrWhiteSpace(request.A))
            {
                return Fail("Service de missions indisponible.");
            }

            MissionRun run = runtime.Missions.GetActive(pid, request.A);
            JobMissionDefinition definition = JobMissionCatalog.Get(request.A);

            if (run == null || run.Completed || definition == null ||
                run.CurrentStep < 0 ||
                run.CurrentStep >= definition.Steps.Length)
            {
                return Fail("Aucune mission active.");
            }

            if (!string.Equals(
                    definition.JobId, Job(sender),
                    StringComparison.OrdinalIgnoreCase))
            {
                return Fail("Cette mission n'est pas pour votre métier.");
            }

            // Les étapes sont suivies dans le monde (balises, objets) :
            // plus de validation manuelle quand le suivi est actif.
            if (MissionWorld.IsAutomatic(pid, request.A))
            {
                return Fail("Cette étape se valide en jeu : "
                    + "suivez l'objectif (balise ou objets).");
            }

            // Anti-farm : un délai minimal entre deux validations.
            if (!Cooldown("mission|" + pid + "|" + request.A, MissionStepDelay))
            {
                return Fail("Terminez réellement l'étape avant de valider.");
            }

            try
            {
                string stepId = definition.Steps[run.CurrentStep].Id;

                MissionRun result = runtime.Missions.Advance(
                    true, pid, request.A, stepId);

                if (result != null && result.Completed)
                {
                    return Ok(
                        "Mission terminée ! Récompense : "
                        + Money(definition.RewardCents) + ".");
                }

                return Ok(
                    "Étape validée (" + result.CurrentStep + "/"
                    + definition.Steps.Length + ").");
            }
            catch (Exception exception)
            {
                return Fail(exception.Message);
            }
        }

        // ------------------------------------------------------------
        // Factures et ventes
        // ------------------------------------------------------------

        private static void PurgeInvoices()
        {
            float now = Time.realtimeSinceStartup;

            lock (Sync)
            {
                List<string> remove = new List<string>();

                foreach (KeyValuePair<string, Invoice> pair in Invoices)
                {
                    if (now - pair.Value.Created > InvoiceLifetime)
                        remove.Add(pair.Key);
                }

                foreach (string key in remove)
                    Invoices.Remove(key);
            }
        }

        /// <summary>
        /// Vente de véhicule : réservée au concessionnaire, à un client proche. Une facture est
        /// envoyée ; le véhicule est livré devant le client quand il la paie.
        /// </summary>
        private static RpResponse VehicleSell(PlayerID sender, RpRequest request)
        {
            string pid;
            PlayerID target = Target(request);

            if (!string.Equals(Job(sender), "concessionnaire", StringComparison.OrdinalIgnoreCase))
                return Fail("Seul un concessionnaire peut vendre des véhicules.");

            if (target == null || target.SmallID == sender.SmallID)
                return Fail("Client invalide.");

            if (!Pid(sender, out pid))
                return Fail("Identité introuvable.");

            if (!Near(sender, target, NearInvoice))
                return Fail("Le client est trop loin (8 m maximum).");

            VehicleDefinition definition = JobVehicleCatalog.Find(
                "concessionnaire", EconomyJobResolver.GetGradeIndexFromPlayer(sender), request.A);

            if (definition == null)
                return Fail("Véhicule inconnu du catalogue.");

            if (!GameItemService.IsAvailable(definition.Keywords, true))
            {
                return Fail("« " + definition.Name + " » n'est pas installé dans BONELAB "
                    + "(vehicles.json / spawnable-titles.txt). Aucune facture créée.");
            }

            PurgeInvoices();

            lock (Sync)
            {
                int pending = 0;

                foreach (Invoice item in Invoices.Values)
                {
                    if (item.FromId == sender.SmallID)
                        pending++;
                }

                if (pending >= MaxInvoicesPerIssuer)
                    return Fail("Trop de factures en attente.");

                Invoice invoice = new Invoice
                {
                    Id = Guid.NewGuid().ToString("N").Substring(0, 8),
                    FromId = sender.SmallID,
                    FromPid = pid,
                    FromName = Name(sender),
                    ToId = target.SmallID,
                    Amount = JobVehicleCatalog.EffectivePrice(definition),
                    Label = CleanText(definition.Name, 40),
                    Created = Time.realtimeSinceStartup,
                    VehicleId = definition.Id
                };

                Invoices[invoice.Id] = invoice;

                RpHub.Push(target, new RpResponse
                {
                    K = "invoice.new",
                    T = 0,
                    Ok = true,
                    Msg = Name(sender) + " vous propose : " + invoice.Label + " pour "
                        + Money(invoice.Amount) + ". Menu Factures pour accepter."
                });

                return Ok("Offre envoyée à " + Name(target) + " : " + invoice.Label
                    + " (" + Money(invoice.Amount) + ").");
            }
        }

        private static RpResponse InvoiceCreate(
            PlayerID sender, RpRequest request)
        {
            PlayerID target = Target(request);
            string pid;

            if (target == null || target.SmallID == sender.SmallID)
                return Fail("Destinataire invalide.");

            if (request.N < MinInvoiceCents || request.N > MaxInvoiceCents)
                return Fail("Montant invalide (1 à 1000 EUR).");

            if (!Pid(sender, out pid))
                return Fail("Identité introuvable.");

            if (Job(sender) == "citizen" && !sender.IsMe)
            {
                // Un citoyen peut facturer un autre joueur (vente entre
                // particuliers) : aucune restriction de métier.
            }

            if (!Near(sender, target, NearInvoice))
                return Fail("Le client est trop loin.");

            PurgeInvoices();

            lock (Sync)
            {
                int pending = 0;

                foreach (Invoice item in Invoices.Values)
                {
                    if (item.FromId == sender.SmallID)
                        pending++;
                }

                if (pending >= MaxInvoicesPerIssuer)
                    return Fail("Trop de factures en attente.");

                string label = CleanText(request.A, 40);

                if (label.Length == 0)
                    label = "Prestation";

                Invoice invoice = new Invoice
                {
                    Id = Guid.NewGuid().ToString("N").Substring(0, 8),
                    FromId = sender.SmallID,
                    FromPid = pid,
                    FromName = Name(sender),
                    ToId = target.SmallID,
                    Amount = request.N,
                    Label = label,
                    Created = Time.realtimeSinceStartup
                };

                Invoices[invoice.Id] = invoice;

                RpHub.Push(target, new RpResponse
                {
                    K = "invoice.new",
                    T = 0,
                    Ok = true,
                    Msg = "Facture de " + Money(invoice.Amount) + " ("
                        + invoice.Label + ") de " + invoice.FromName
                        + ". Menu Factures pour payer."
                });

                return Ok("Facture envoyée à " + Name(target) + ".");
            }
        }

        private static RpResponse InvoiceList(
            PlayerID sender, RpRequest request)
        {
            PurgeInvoices();

            List<InvoiceInfo> list = new List<InvoiceInfo>();
            float now = Time.realtimeSinceStartup;

            lock (Sync)
            {
                foreach (Invoice item in Invoices.Values)
                {
                    if (item.ToId != sender.SmallID)
                        continue;

                    list.Add(new InvoiceInfo
                    {
                        Id = item.Id,
                        From = item.FromName,
                        Amount = item.Amount,
                        Label = item.Label,
                        Secs = Mathf.Max(
                            0,
                            (int)(InvoiceLifetime - (now - item.Created)))
                    });
                }
            }

            return Ok(
                list.Count + " facture(s) en attente.",
                JsonSerializer.Serialize(list));
        }

        private static bool TakeInvoice(
            PlayerID sender, string id, out Invoice invoice)
        {
            invoice = null;

            lock (Sync)
            {
                Invoice found;

                if (string.IsNullOrWhiteSpace(id) ||
                    !Invoices.TryGetValue(id, out found) ||
                    found.ToId != sender.SmallID)
                {
                    return false;
                }

                invoice = found;
                Invoices.Remove(id);
                return true;
            }
        }

        private static RpResponse InvoicePay(
            PlayerID sender, RpRequest request)
        {
            EconomyRuntime runtime;
            string payerPid;
            Invoice invoice;

            if (!Runtime(out runtime) || !Pid(sender, out payerPid))
                return Fail("Service bancaire indisponible.");

            PurgeInvoices();

            if (!TakeInvoice(sender, request.A, out invoice))
                return Fail("Facture introuvable ou expirée.");

            if (!string.IsNullOrEmpty(invoice.VehicleId))
            {
                string saleMessage;
                PlayerID dealer = PlatformIds.GetPlayerID(invoice.FromId);

                if (!JobPerks.CompleteVehicleSale(
                        sender, payerPid, invoice.VehicleId, dealer, invoice.FromPid,
                        out saleMessage))
                {
                    // Remet l'offre pour un nouvel essai (fonds, cadence...).
                    lock (Sync)
                        Invoices[invoice.Id] = invoice;

                    return Fail(saleMessage);
                }

                if (dealer != null)
                {
                    RpHub.PushNotice(dealer,
                        Name(sender) + " a acheté : " + invoice.Label + ".", 1);
                }

                return Ok(saleMessage);
            }

            WalletData wallet = runtime.Wallets.GetWallet(payerPid);

            if (wallet == null ||
                wallet.BankCents + wallet.CashCents < invoice.Amount)
            {
                return Fail("Fonds insuffisants.");
            }

            string error;
            bool paid;

            if (wallet.BankCents >= invoice.Amount)
            {
                paid = runtime.Wallets.TryTransfer(
                    true,
                    PhysicalCashRequestId.Create(),
                    payerPid,
                    invoice.FromPid,
                    invoice.Amount,
                    out error);
            }
            else
            {
                paid = runtime.Wallets.TryMoveCashToWalletBank(
                    true,
                    PhysicalCashRequestId.Create(),
                    payerPid,
                    invoice.FromPid,
                    invoice.Amount,
                    out error);
            }

            if (!paid)
            {
                // Remet la facture pour un nouvel essai.
                lock (Sync)
                    Invoices[invoice.Id] = invoice;

                return Fail(string.IsNullOrEmpty(error)
                    ? "Paiement refusé."
                    : error);
            }

            PlayerID issuer = PlatformIds.GetPlayerID(invoice.FromId);

            if (issuer != null)
            {
                RpHub.PushNotice(
                    issuer,
                    Name(sender) + " a payé " + Money(invoice.Amount)
                    + " (" + invoice.Label + ").",
                    1);
            }

            MelonLogger.Msg(
                "[FusionDarkRP RP] Facture payée : " + Money(invoice.Amount)
                + " | " + payerPid + " -> " + invoice.FromPid);

            return Ok("Facture payée : " + Money(invoice.Amount) + ".");
        }

        private static RpResponse InvoiceRefuse(
            PlayerID sender, RpRequest request)
        {
            Invoice invoice;

            if (!TakeInvoice(sender, request.A, out invoice))
                return Fail("Facture introuvable ou expirée.");

            PlayerID issuer = PlatformIds.GetPlayerID(invoice.FromId);

            if (issuer != null)
            {
                RpHub.PushNotice(
                    issuer,
                    Name(sender) + " a refusé votre facture ("
                    + invoice.Label + ").",
                    2);
            }

            return Ok("Facture refusée.");
        }

        // ------------------------------------------------------------
        // Police : amendes, recherchés, arrestations
        // ------------------------------------------------------------

        private static RpResponse Fine(PlayerID sender, RpRequest request)
        {
            EconomyRuntime runtime;
            string targetPid;
            PlayerID target = Target(request);

            if (!IsPolice(sender))
                return Fail("Réservé à la police.");

            if (target == null || target.SmallID == sender.SmallID)
                return Fail("Cible invalide.");

            if (Array.IndexOf(AllowedFines, request.N) < 0)
                return Fail("Montant d'amende non autorisé.");

            if (!Runtime(out runtime) || !Pid(target, out targetPid))
                return Fail("Service bancaire indisponible.");

            if (!Near(sender, target, NearPolice))
                return Fail("Le suspect est trop loin.");

            if (!Cooldown(
                    "fine|" + sender.SmallID + "|" + target.SmallID, 20f))
            {
                return Fail("Amende déjà infligée à l'instant.");
            }

            long taken = DebitToTreasury(runtime, targetPid, request.N);

            if (taken <= 0)
                return Fail(Name(target) + " ne peut pas payer.");

            string reason = CleanText(request.A, 50);

            RpHub.PushNotice(
                target,
                "Amende de " + Money(taken)
                + (reason.Length > 0 ? " : " + reason : "") + ".",
                2);

            MelonLogger.Msg(
                "[FusionDarkRP RP] Amende " + Money(taken) + " | "
                + Name(sender) + " -> " + Name(target));

            return Ok("Amende de " + Money(taken) + " prélevée sur "
                + Name(target) + ".");
        }

        private static RpResponse WantedSet(
            PlayerID sender, RpRequest request)
        {
            PlayerID target = Target(request);

            if (!IsPolice(sender))
                return Fail("Réservé à la police.");

            if (target == null || target.SmallID == sender.SmallID)
                return Fail("Cible invalide.");

            string reason = CleanText(request.A, 60);

            if (reason.Length == 0)
                reason = "Infraction";

            lock (Sync)
            {
                WantedList[target.SmallID] = new Wanted
                {
                    Name = Name(target),
                    Reason = reason,
                    By = sender.SmallID
                };
            }

            foreach (PlayerID other in PlayerIDManager.PlayerIDs)
            {
                if (other != null && other.IsValid && IsPolice(other))
                {
                    RpHub.PushNotice(
                        other,
                        Name(target) + " est recherché : " + reason + ".",
                        2);
                }
            }

            RpHub.PushNotice(
                target, "Vous êtes recherché : " + reason + ".", 2);

            return Ok(Name(target) + " est désormais recherché.");
        }

        private static RpResponse WantedListAction(
            PlayerID sender, RpRequest request)
        {
            if (!IsPolice(sender))
                return Fail("Réservé à la police.");

            List<WantedInfo> list = new List<WantedInfo>();

            lock (Sync)
            {
                List<byte> gone = new List<byte>();

                foreach (KeyValuePair<byte, Wanted> pair in WantedList)
                {
                    PlayerID player = PlatformIds.GetPlayerID(pair.Key);

                    if (player == null || !player.IsValid)
                    {
                        gone.Add(pair.Key);
                        continue;
                    }

                    list.Add(new WantedInfo
                    {
                        Id = pair.Key,
                        Name = Name(player),
                        Reason = pair.Value.Reason
                    });
                }

                foreach (byte id in gone)
                    WantedList.Remove(id);
            }

            return Ok(
                list.Count + " personne(s) recherchée(s).",
                JsonSerializer.Serialize(list));
        }

        private static RpResponse WantedArrest(
            PlayerID sender, RpRequest request)
        {
            EconomyRuntime runtime;
            string targetPid;
            string policePid;
            PlayerID target = Target(request);

            if (!IsPolice(sender))
                return Fail("Réservé à la police.");

            if (target == null || target.SmallID == sender.SmallID)
                return Fail("Cible invalide.");

            Wanted wanted;

            lock (Sync)
            {
                if (!WantedList.TryGetValue(target.SmallID, out wanted))
                    return Fail("Cette personne n'est pas recherchée.");
            }

            if (!Runtime(out runtime) ||
                !Pid(target, out targetPid) ||
                !Pid(sender, out policePid))
            {
                return Fail("Service bancaire indisponible.");
            }

            if (!Near(sender, target, NearPolice))
                return Fail("Le suspect est trop loin.");

            long fine = DebitToTreasury(runtime, targetPid, ArrestFineCents);

            lock (Sync)
                WantedList.Remove(target.SmallID);

            string bountyText = "";
            string error;

            WalletData treasury = runtime.Wallets.GetWallet(TreasuryWallet);
            long bounty = treasury == null
                ? 0
                : Math.Min(ArrestBountyCents, treasury.BankCents);

            if (bounty > 0 &&
                runtime.Wallets.TryMoveBankToWalletCash(
                    true,
                    PhysicalCashRequestId.Create(),
                    TreasuryWallet,
                    policePid,
                    bounty,
                    out error))
            {
                bountyText = " Prime d'arrestation : " + Money(bounty) + ".";
            }

            RpHub.PushNotice(
                target,
                "Vous avez été arrêté. Amende : " + Money(fine) + ".",
                2);

            MelonLogger.Msg(
                "[FusionDarkRP RP] Arrestation : " + Name(sender)
                + " -> " + Name(target) + " | motif : " + wanted.Reason);

            return Ok(
                Name(target) + " arrêté. Amende : " + Money(fine) + "."
                + bountyText);
        }

        // ------------------------------------------------------------
        // Soins
        // ------------------------------------------------------------

        private static RpResponse CareHeal(
            PlayerID sender, RpRequest request)
        {
            PlayerID target = Target(request);

            if (!sender.IsMe && Job(sender) != "medecins")
                return Fail("Réservé aux médecins.");

            if (target == null)
                return Fail("Patient invalide.");

            if (target.SmallID != sender.SmallID &&
                !Near(sender, target, NearCare))
            {
                return Fail("Le patient est trop loin.");
            }

            if (!Cooldown("care|" + sender.SmallID + "|" + target.SmallID, 30f))
                return Fail("Patient déjà soigné à l'instant.");

            RpHub.Push(target, new RpResponse
            {
                K = "heal.apply",
                T = 0,
                Ok = true,
                Msg = "Vous avez été soigné par " + Name(sender) + "."
            });

            string bonus = "";
            EconomyRuntime runtime;
            string medicPid;
            string error;

            if (target.SmallID != sender.SmallID &&
                Runtime(out runtime) &&
                Pid(sender, out medicPid))
            {
                WalletData treasury =
                    runtime.Wallets.GetWallet(TreasuryWallet);

                long reward = treasury == null
                    ? 0
                    : Math.Min(CareRewardCents, treasury.BankCents);

                if (reward > 0 &&
                    runtime.Wallets.TryMoveBankToWalletCash(
                        true,
                        PhysicalCashRequestId.Create(),
                        TreasuryWallet,
                        medicPid,
                        reward,
                        out error))
                {
                    bonus = " Prime de soin : " + Money(reward) + ".";
                }
            }

            return Ok(Name(target) + " a été soigné." + bonus);
        }

        // ------------------------------------------------------------
        // Administration (hôte uniquement)
        // ------------------------------------------------------------

        private static bool IsAdmin(PlayerID sender)
        {
            return sender.IsMe;
        }

        private static RpResponse AdminGive(
            PlayerID sender, RpRequest request)
        {
            EconomyRuntime runtime;
            string targetPid;
            string error;
            PlayerID target = Target(request);

            if (!IsAdmin(sender))
                return Fail("Réservé à l'hôte.");

            if (target == null || request.N <= 0 ||
                request.N > 100000000L)
            {
                return Fail("Paramètres invalides.");
            }

            if (!Runtime(out runtime) || !Pid(target, out targetPid))
                return Fail("Service bancaire indisponible.");

            if (!runtime.Wallets.TryGiveCash(
                true,
                PhysicalCashRequestId.Create(),
                targetPid,
                request.N,
                out error))
            {
                return Fail(error);
            }

            RpHub.PushNotice(
                target, "Vous recevez " + Money(request.N) + " en espèces.", 1);

            MelonLogger.Msg(
                "[FusionDarkRP RP] Don admin " + Money(request.N)
                + " -> " + targetPid);

            return Ok(Money(request.N) + " donnés à " + Name(target) + ".");
        }

        private static RpResponse AdminBalance(
            PlayerID sender, RpRequest request)
        {
            EconomyRuntime runtime;
            string targetPid;
            PlayerID target = Target(request);

            if (!IsAdmin(sender))
                return Fail("Réservé à l'hôte.");

            if (target == null ||
                !Runtime(out runtime) ||
                !Pid(target, out targetPid))
            {
                return Fail("Joueur ou service indisponible.");
            }

            WalletData wallet = runtime.Wallets.GetWallet(targetPid);

            if (wallet == null)
                return Fail("Aucun compte pour ce joueur.");

            return Ok(
                Name(target) + " : espèces " + Money(wallet.CashCents)
                + " | banque " + Money(wallet.BankCents) + ".");
        }

        private static RpResponse AdminTreasury(
            PlayerID sender, RpRequest request)
        {
            EconomyRuntime runtime;

            if (!IsAdmin(sender))
                return Fail("Réservé à l'hôte.");

            if (!Runtime(out runtime))
                return Fail("Service bancaire indisponible.");

            WalletData treasury = runtime.Wallets.GetWallet(TreasuryWallet);

            return Ok(
                "Trésor municipal : "
                + Money(treasury != null ? treasury.BankCents : 0) + ".");
        }
    }
}
