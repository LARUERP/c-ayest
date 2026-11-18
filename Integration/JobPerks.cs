using System;
using System.Collections.Generic;
using System.Text.Json;
using FusionDarkRP.Economy;
using FusionDarkRP.RP;
using LabFusion.Network;
using LabFusion.Player;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Avantages de métier (hôte autoritaire) :
    ///  - à l'embauche : kit d'équipement (objets du mod + vrais objets BONELAB
    ///    comme armes de service) qui apparaît devant le joueur ;
    ///  - à la débauche / changement de métier : tout ce qui avait été donné
    ///    est retiré ;
    ///  - véhicules : vendus UNIQUEMENT par un concessionnaire à un client proche
    ///    (facture, puis livraison au paiement ; commission au concessionnaire).
    /// </summary>
    public static class JobPerks
    {
        private const float TickInterval = 1.5f;
        private const float KitCooldown = 120f;
        private const float VehicleCooldown = 90f;
        private const double DealerCommission = 0.15;
        private const string TreasuryWallet = "system:town_treasury";   // même portefeuille que RpServices, DrugFarmService et BankSecurity

        private sealed class Kit
        {
            public string[] Models = new string[0];
            public string[] Pistol;
            public string[] Smg;
            public string[] Rifle;
            public string[] Shotgun;
            public int SmgGrade = 2;
            public int RifleGrade = 4;
        }

        private static readonly string[] PistolWords = { "m1911", "eder22", "glock", "pistol" };
        private static readonly string[] SmgWords = { "mp5", "uzi", "mac10", "smg" };
        private static readonly string[] RifleWords = { "mk18", "akm", "m4", "rifle" };
        private static readonly string[] ShotgunWords = { "m590", "shotgun", "mossberg" };

        private static Kit KitFor(string job, int grade)
        {
            Kit kit = new Kit();

            switch (job)
            {
                case "eboueurs":
                    kit.Models = new[] { "TrashGrabber", "WorkGloves", "TrashBag" };
                    break;

                case "banque":
                    kit.Models = new[] { "BankTerminal" };
                    break;

                case "mairie":
                    kit.Models = new[] { "TownBadge", "TownFolder" };
                    break;

                case "epicerie":
                    kit.Models = new[] { "ShopScanner" };
                    break;

                case "police":
                    kit.Models = new[] { "PoliceRadio", "PoliceCuffs", "PoliceBadge", "PoliceNotepad" };
                    kit.Pistol = PistolWords;
                    kit.Smg = SmgWords;
                    kit.Rifle = RifleWords;
                    kit.SmgGrade = 2;
                    kit.RifleGrade = 4;
                    break;

                case "medecins":
                    kit.Models = grade >= 2
                        ? new[] { "MedicalKit", "Defibrillator" }
                        : new[] { "MedicalKit" };
                    break;

                case "marseillais":
                case "parisiens":
                case "gens_du_voyage":
                    kit.Pistol = PistolWords;
                    kit.Smg = SmgWords;
                    kit.Shotgun = ShotgunWords;
                    kit.SmgGrade = 2;
                    kit.RifleGrade = 4;
                    break;

                case "armurerie":
                    kit.Models = new[] { "ArmoryCatalog", "ArmoryCase" };
                    break;

                case "concessionnaire":
                    kit.Models = new[] { "BusinessTablet", "CarKeys" };
                    break;
            }

            // Les objets suivent le catalogue d'équipement : grade minimum respecté.
            List<string> allowed = new List<string>();

            foreach (string model in kit.Models)
            {
                bool permitted = true;

                foreach (EquipmentDefinition item in JobEquipmentCatalog.All)
                {
                    if (string.Equals(item.JobId, job, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(item.ModelResource, model, StringComparison.Ordinal))
                    {
                        permitted = grade >= item.MinimumGrade;
                        break;
                    }
                }

                if (permitted)
                    allowed.Add(model);
            }

            kit.Models = allowed.ToArray();

            return kit;
        }

        private static readonly Dictionary<byte, string> LastState =
            new Dictionary<byte, string>();

        private static readonly Dictionary<string, List<string>> Models =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        private static readonly Dictionary<string, float> Cooldowns =
            new Dictionary<string, float>(StringComparer.Ordinal);

        private static float nextTick;

        public static void RegisterHandlers()
        {
            RpHub.RegisterHost("perk.kit", HandleKit);
        }

        // --------------------------------------------------------------
        // Détection embauche / débauche
        // --------------------------------------------------------------

        public static void Update()
        {
            if (!NetworkInfo.HasServer || !NetworkInfo.IsHost)
                return;

            float now = Time.unscaledTime;

            if (now < nextTick)
                return;

            nextTick = now + TickInterval;

            HashSet<byte> present = new HashSet<byte>();

            foreach (PlayerID player in PlayerIDManager.PlayerIDs)
            {
                if (player == null || !player.IsValid)
                    continue;

                present.Add(player.SmallID);

                string pid;

                if (!PlayerIdentityService.TryGetPersistentId(player, out pid))
                    continue;

                string job = EconomyJobResolver.GetJobIdFromPlayer(player);
                int grade = EconomyJobResolver.GetGradeIndexFromPlayer(player);
                string state = job + ":" + grade;

                string previous;

                if (!LastState.TryGetValue(player.SmallID, out previous))
                {
                    // Première vue du joueur : on mémorise sans rien donner.
                    LastState[player.SmallID] = state;
                    continue;
                }

                if (previous == state)
                    continue;

                LastState[player.SmallID] = state;

                string oldJob = previous.Substring(0, previous.IndexOf(':'));

                try
                {
                    if (oldJob != job)
                    {
                        Revoke(pid);
                        Say(player, "Vous quittez votre poste : équipement restitué.", 0);

                        if (job != "citizen")
                        {
                            Grant(player, pid, job, grade, true);
                            Say(player, "Bienvenue ! Votre équipement de service est devant vous.", 1);
                        }
                    }
                    else
                    {
                        // Promotion / rétrogradation : on remplace le kit.
                        Revoke(pid);
                        Grant(player, pid, job, grade, true);
                        Say(player, "Nouveau grade : équipement mis à jour.", 1);
                    }
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Metiers] " + exception.Message);
                }
            }

            List<byte> gone = null;

            foreach (byte id in LastState.Keys)
            {
                if (!present.Contains(id))
                {
                    if (gone == null)
                        gone = new List<byte>();

                    gone.Add(id);
                }
            }

            if (gone != null)
            {
                foreach (byte id in gone)
                    LastState.Remove(id);
            }
        }

        public static void Reset()
        {
            LastState.Clear();
            Cooldowns.Clear();

            foreach (List<string> list in Models.Values)
                foreach (string id in list)
                    Destroy(id);

            Models.Clear();
            GameItemService.ReleaseAll();
        }

        // --------------------------------------------------------------
        // Kits
        // --------------------------------------------------------------

        private static void Grant(
            PlayerID player, string pid, string job, int grade, bool withWeapons)
        {
            Transform body = PhysicalCashRigAnchorResolver.ResolveBody(player);

            if (body == null)
                return;

            Kit kit = KitFor(job, grade);

            Vector3 forward = body.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;

            forward.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 origin = body.position + forward * 0.9f - Vector3.up * 0.6f;

            List<string> ids = new List<string>();
            int slot = 0;

            foreach (string model in kit.Models)
            {
                Vector3 position = origin + right * (0.25f * (slot - 1)) + Vector3.up * 0.15f;
                slot++;

                string id;
                string error;

                if (WorldModelPlacementService.TrySpawnAtPose(
                        model, position, Quaternion.identity, out id, out error))
                {
                    ids.Add(id);
                }
                else
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Metiers] " + model + " : " + error);
                }
            }

            lock (Models)
                Models[pid] = ids;

            if (!withWeapons)
                return;

            string bucket = pid + "|kit";
            string title;
            Vector3 weaponPos = origin + Vector3.up * 0.4f;

            if (kit.Pistol != null &&
                GameItemService.TryGive(bucket, kit.Pistol, false, weaponPos + right * 0.35f,
                    Quaternion.identity, out title))
            {
                MelonLogger.Msg("[FusionDarkRP Metiers] Arme de poing : " + title);
            }

            if (kit.Smg != null && grade >= kit.SmgGrade &&
                GameItemService.TryGive(bucket, kit.Smg, false, weaponPos + right * 0.55f,
                    Quaternion.identity, out title))
            {
                MelonLogger.Msg("[FusionDarkRP Metiers] Arme légère : " + title);
            }

            if (kit.Rifle != null && grade >= kit.RifleGrade &&
                GameItemService.TryGive(bucket, kit.Rifle, false, weaponPos + right * 0.75f,
                    Quaternion.identity, out title))
            {
                MelonLogger.Msg("[FusionDarkRP Metiers] Fusil : " + title);
            }

            if (kit.Shotgun != null && grade >= kit.RifleGrade &&
                GameItemService.TryGive(bucket, kit.Shotgun, false, weaponPos + right * 0.75f,
                    Quaternion.identity, out title))
            {
                MelonLogger.Msg("[FusionDarkRP Metiers] Fusil à pompe : " + title);
            }
        }

        private static void Revoke(string pid)
        {
            List<string> ids = null;

            lock (Models)
            {
                if (Models.TryGetValue(pid, out ids))
                    Models.Remove(pid);
            }

            if (ids != null)
            {
                foreach (string id in ids)
                    Destroy(id);
            }

            GameItemService.Release(pid + "|kit");
            GameItemService.Release(pid + "|veh");
        }

        private static void Destroy(string id)
        {
            try
            {
                string error;

                if (WorldModelRuntimeManager.TryDestroy(id, out error))
                    WorldModelNetwork.BroadcastDestroy(id);
            }
            catch
            {
                // Déjà supprimé.
            }
        }

        private static RpResponse HandleKit(PlayerID sender, RpRequest request)
        {
            string pid;

            if (!PlayerIdentityService.TryGetPersistentId(sender, out pid))
                return Fail("Identité introuvable.");

            string job = EconomyJobResolver.GetJobIdFromPlayer(sender);

            if (job == "citizen")
                return Fail("Les citoyens n'ont pas d'équipement de service.");

            if (!Cooldown("kit|" + pid, KitCooldown))
                return Fail("Équipement déjà délivré récemment, patientez.");

            Revoke(pid);

            Grant(sender, pid, job,
                EconomyJobResolver.GetGradeIndexFromPlayer(sender), true);

            return Ok("Équipement de service délivré devant vous.");
        }

        // --------------------------------------------------------------
        // Véhicules
        // --------------------------------------------------------------

        /// <summary>
        /// Finalise une vente de véhicule au paiement de la facture : contrôles, paiement
        /// (commission au concessionnaire, reste au trésor), puis apparition du véhicule
        /// devant l'acheteur. Retourne faux sans rien débiter si un contrôle échoue, et
        /// REMBOURSE automatiquement si l'apparition échoue après le paiement.
        /// </summary>
        public static bool CompleteVehicleSale(
            PlayerID buyer, string buyerPid, string vehicleId,
            PlayerID dealer, string dealerPid, out string message)
        {
            message = null;

            if (!EconomyRuntimeBootstrap.IsInitialized ||
                EconomyRuntimeBootstrap.Runtime == null)
            {
                message = "Économie indisponible.";
                return false;
            }

            EconomyRuntime runtime = EconomyRuntimeBootstrap.Runtime;

            if (dealer == null || !dealer.IsValid ||
                EconomyJobResolver.GetJobIdFromPlayer(dealer) != "concessionnaire")
            {
                message = "Le concessionnaire n'est plus en service : vente annulée.";
                return false;
            }

            VehicleDefinition definition;

            if (!VehicleInventory.TryGet(vehicleId, out definition))
            {
                message = "Ce véhicule n'est plus au catalogue.";
                return false;
            }

            // Le véhicule doit exister dans le jeu AVANT tout paiement.
            if (!VehicleInventory.IsInstalled(definition))
            {
                VehicleInventory.Refresh(true);

                message = "« " + definition.Name + " » n'est plus chargé dans BONELAB. "
                    + "Aucun paiement effectué.";
                return false;
            }

            Transform body = PhysicalCashRigAnchorResolver.ResolveBody(buyer);

            if (body == null)
            {
                message = "Position de l'acheteur introuvable. Aucun paiement effectué.";
                return false;
            }

            long price = VehicleInventory.EffectivePrice(definition);
            WalletData wallet = runtime.Wallets.GetWallet(buyerPid);

            if (wallet == null ||
                (wallet.BankCents < price && wallet.CashCents < price))
            {
                message = "Il faut " + BankService.FormatAmount(price)
                    + " (en banque ou en liquide). Aucun paiement effectué.";
                return false;
            }

            string cooldownKey = "veh|" + buyerPid;
            float until;

            if (Cooldowns.TryGetValue(cooldownKey, out until) && Time.unscaledTime < until)
            {
                message = "Un véhicule vient d'être livré, patientez avant le suivant.";
                return false;
            }

            bool fromBank = wallet.BankCents >= price;
            long commission = (long)(price * DealerCommission);
            long toTreasury = price - commission;
            string error;

            // Le plus gros montant d'abord : en cas d'échec, rien n'est débité.
            bool paid = fromBank
                ? runtime.Wallets.TryTransfer(true, PhysicalCashRequestId.Create(),
                    buyerPid, TreasuryWallet, toTreasury, out error)
                : runtime.Wallets.TryMoveCashToWalletBank(true, PhysicalCashRequestId.Create(),
                    buyerPid, TreasuryWallet, toTreasury, out error);

            if (!paid)
            {
                message = "Paiement refusé : " + error;
                return false;
            }

            bool commissionPaid = false;

            if (commission > 0)
            {
                commissionPaid = fromBank
                    ? runtime.Wallets.TryTransfer(true, PhysicalCashRequestId.Create(),
                        buyerPid, dealerPid, commission, out error)
                    : runtime.Wallets.TryMoveCashToWalletBank(true, PhysicalCashRequestId.Create(),
                        buyerPid, dealerPid, commission, out error);

                if (!commissionPaid)
                    MelonLogger.Warning("[FusionDarkRP Véhicules] Commission non versée : " + error);
            }

            int replaced = GameItemService.Release(buyerPid + "|veh");

            Vector3 forward = body.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;

            forward.Normalize();

            float distance = definition.SpawnDistance > 0f ? definition.SpawnDistance : 3.5f;

            bool ok = GameItemService.TryGiveBarcode(
                buyerPid + "|veh", definition.Barcode,
                body.position + forward * distance - Vector3.up * 0.8f,
                Quaternion.LookRotation(forward, Vector3.up));

            if (!ok)
            {
                MelonLogger.Warning("[FusionDarkRP Véhicules] Apparition échouée après paiement : "
                    + buyerPid + " / " + definition.Name);

                // Remboursement automatique (sur le compte bancaire de l'acheteur).
                bool refunded = runtime.Wallets.TryTransfer(true, PhysicalCashRequestId.Create(),
                    TreasuryWallet, buyerPid, toTreasury, out error);

                if (refunded && commissionPaid)
                {
                    refunded = runtime.Wallets.TryTransfer(true, PhysicalCashRequestId.Create(),
                        dealerPid, buyerPid, commission, out error);
                }

                if (refunded)
                {
                    message = "Le véhicule n'a pas pu apparaître : vous avez été remboursé "
                        + "sur votre compte. Réessayez dans un instant.";
                    return false;
                }

                message = "Paiement effectué mais le véhicule n'a pas pu apparaître et le "
                    + "remboursement automatique a échoué : demandez à l'hôte de vous rembourser.";
                return true;
            }

            Cooldowns[cooldownKey] = Time.unscaledTime + VehicleCooldown;

            message = definition.Name + " livré (" + BankService.FormatAmount(price) + ")."
                + (replaced > 0 ? " Votre ancien véhicule a été retiré." : string.Empty);

            return true;
        }

        // --------------------------------------------------------------

        private static bool Cooldown(string key, float seconds)
        {
            float now = Time.unscaledTime;
            float until;

            if (Cooldowns.TryGetValue(key, out until) && now < until)
                return false;

            Cooldowns[key] = now + seconds;
            return true;
        }

        private static RpResponse Ok(string message)
        {
            return new RpResponse { Ok = true, Msg = message };
        }

        private static RpResponse Fail(string message)
        {
            return new RpResponse { Ok = false, Msg = message };
        }

        private static void Say(PlayerID player, string text, byte level)
        {
            try
            {
                if (player.IsMe)
                    RpHub.Notify(text, level);
                else
                    RpHub.PushNotice(player, text, level);
            }
            catch
            {
                // Notification facultative.
            }
        }
    }
}
