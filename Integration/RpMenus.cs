using System;
using System.Collections.Generic;
using System.Text.Json;
using BoneLib.BoneMenu;
using FusionDarkRP.Economy;
using LabFusion.Network;
using LabFusion.Player;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Interface BoneMenu des services RP. Chaque bouton envoie une
    /// requête à l'hôte (RpHub) ; l'hôte valide métier, distance, montant
    /// et solde, puis les pages se mettent à jour avec la réponse.
    /// </summary>
    public static class RpMenus
    {
        private static Page missionsPage;
        private static Page invoicesPage;
        private static Page wantedPage;
        private static bool subscribed;

        private static readonly long[] InvoiceAmounts =
            { 1000, 2000, 5000, 10000, 25000, 50000 };

        private static readonly long[] FineAmounts =
            { 5000, 10000, 25000, 50000, 100000 };

        public static void Reset()
        {
            missionsPage = null;
            invoicesPage = null;
            wantedPage = null;

            if (subscribed)
            {
                RpHub.ResponseReceived -= OnResponse;
                subscribed = false;
            }
        }

        public static void Build(Page main)
        {
            if (main == null)
                return;

            if (!subscribed)
            {
                RpHub.ResponseReceived += OnResponse;
                subscribed = true;
            }

            missionsPage = MenuKit.NewPage(main, "Missions", Color.cyan);
            invoicesPage = MenuKit.NewPage(main, "Factures", Color.yellow);

            Page police = MenuKit.NewPage(main, "Police : recherches", Color.blue);
            wantedPage = police;

            ResetList(missionsPage, "Actualiser mes missions",
                () => RpHub.Send("mission.list"));
            ResetList(invoicesPage, "Actualiser mes factures",
                () => RpHub.Send("invoice.list"));
            ResetList(wantedPage, "Actualiser les recherchés",
                () => RpHub.Send("wanted.list"));

            MenuKit.AddInfo(missionsPage,
                "Missions de votre métier (voir Hiérarchies).",
                Color.white, "[Fusion DarkRP]");

            Page work = MenuKit.NewPage(main, "Mon métier : équipement", Color.green);

            MenuKit.AddFunction(work, "Récupérer mon équipement de service", Color.green,
                () => RpHub.Send("perk.kit"));
            MenuKit.AddFunction(work, "Véhicule (service / gang / achat 400 EUR)", Color.cyan,
                () => RpHub.Send("veh.spawn"));
            MenuKit.AddInfo(work,
                "Équipement et armes : donnés à l'embauche, retirés à la débauche.",
                Color.white, "[Fusion DarkRP]");

            Page farm = MenuKit.NewPage(main, "Gangs : culture et trafic", Color.magenta);

            MenuKit.AddFunction(farm, "Planter du cannabis (40 EUR)", Color.green,
                () => RpHub.Send("drug.plant"));
            MenuKit.AddFunction(farm, "Récolter la plante proche", Color.yellow,
                () => RpHub.Send("drug.harvest"));
            MenuKit.AddFunction(farm, "Vendre mes sachets (à moins de 4 m)", Color.cyan,
                () => RpHub.Send("drug.sell"));
            MenuKit.AddFunction(farm, "Police : saisir plants et sachets proches", Color.blue,
                () => RpHub.Send("drug.seize"));
            MenuKit.AddInfo(farm,
                "Semis -> croissance -> plante mûre (~5 min) -> 3 sachets à revendre.",
                Color.white, "[Fusion DarkRP]");

            Page admin = MenuKit.NewPage(main, "Administration (hôte)", Color.red);

            MenuKit.AddFunction(admin, "Voir le trésor municipal", Color.yellow,
                () => RpHub.Send("admin.treasury"));

            MenuKit.AddInfo(admin,
                "Dons et soldes : Joueurs connectés > joueur > Actions RP.",
                Color.white, "[Fusion DarkRP]");
        }

        // ------------------------------------------------------------
        // Actions par joueur
        // ------------------------------------------------------------

        public static void AddPlayerActions(
            Page playerPage, byte target, bool isMe)
        {
            if (playerPage == null)
                return;

            Page actions = MenuKit.NewPage(
                playerPage, "Actions RP", Color.yellow);

            if (!isMe)
            {
                Page bill = MenuKit.NewPage(actions, "Facturer", Color.yellow);

                foreach (long amount in InvoiceAmounts)
                {
                    long cents = amount;

                    MenuKit.AddFunction(
                        bill,
                        "Facturer " + (cents / 100) + " EUR",
                        Color.yellow,
                        () => RpHub.Send(
                            "invoice.create", InvoiceLabel(), cents, target));
                }

                Page police = MenuKit.NewPage(actions, "Police", Color.blue);

                foreach (long amount in FineAmounts)
                {
                    long cents = amount;

                    MenuKit.AddFunction(
                        police,
                        "Amende " + (cents / 100) + " EUR",
                        Color.blue,
                        () => RpHub.Send("fine", "Infraction", cents, target));
                }

                string[] reasons = { "Vol", "Agression", "Trafic", "Infraction" };

                foreach (string reason in reasons)
                {
                    string r = reason;

                    MenuKit.AddFunction(
                        police,
                        "Déclarer recherché : " + r,
                        Color.red,
                        () => RpHub.Send("wanted.set", r, 0, target));
                }

                MenuKit.AddFunction(police, "Arrêter (si recherché)",
                    Color.red,
                    () => RpHub.Send("wanted.arrest", null, 0, target));
            }

            Page care = MenuKit.NewPage(actions, "Soins", Color.green);

            MenuKit.AddFunction(
                care,
                isMe ? "Me soigner (médecin)" : "Soigner ce joueur",
                Color.green,
                () => RpHub.Send("care.heal", null, 0, target));

            if (NetworkInfo.HasServer && NetworkInfo.IsHost)
            {
                Page admin = MenuKit.NewPage(actions, "Admin (hôte)", Color.red);

                MenuKit.AddFunction(admin, "Voir le solde", Color.white,
                    () => RpHub.Send("admin.balance", null, 0, target));

                MenuKit.AddFunction(admin, "Donner 100 EUR", Color.green,
                    () => RpHub.Send("admin.give", null, 10000, target));

                MenuKit.AddFunction(admin, "Donner 1000 EUR", Color.green,
                    () => RpHub.Send("admin.give", null, 100000, target));
            }
        }

        private static string InvoiceLabel()
        {
            string job = null;

            try
            {
                PlayerID local = PlayerIDManager.LocalID;

                if (local != null && local.IsValid)
                    job = EconomyJobResolver.GetJobIdFromPlayer(local);
            }
            catch
            {
                job = null;
            }

            switch (job)
            {
                case "medecins": return "Soins medicaux";
                case "armurerie": return "Vente armurerie";
                case "epicerie": return "Achats epicerie";
                case "banque": return "Service bancaire";
                case "mairie": return "Demarche municipale";
                case "eboueurs": return "Service de collecte";
                case "gens_du_voyage": return "Commerce itinerant";
                case "concessionnaire": return "Vente de vehicules";
                default: return "Prestation";
            }
        }

        // ------------------------------------------------------------
        // Réponses
        // ------------------------------------------------------------

        private static void ResetList(
            Page page, string refreshLabel, Action refresh)
        {
            if (page == null)
                return;

            page.RemoveAll();

            MenuKit.AddFunction(page, refreshLabel, Color.cyan, refresh);
        }

        private static void OnResponse(RpResponse response)
        {
            if (response == null)
                return;

            try
            {
                switch (response.K)
                {
                    case "notify":
                        byte level;
                        byte.TryParse(response.D, out level);
                        RpHub.Notify(response.Msg, level);
                        return;

                    case "heal.apply":
                        ApplyHeal();
                        RpHub.Notify(response.Msg, 1);
                        return;

                    case "mission.list":
                        BuildMissions(response);
                        return;

                    case "invoice.list":
                        BuildInvoices(response);
                        return;

                    case "wanted.list":
                        BuildWanted(response);
                        return;

                    case "invoice.new":
                        RpHub.Notify(response.Msg, 1);
                        RpHub.Send("invoice.list");
                        return;
                }

                RpHub.Notify(response.Msg, (byte)(response.Ok ? 1 : 2));

                if (response.K == "mission.start" ||
                    response.K == "mission.advance")
                {
                    RpHub.Send("mission.list");
                }
                else if (response.K == "invoice.pay" ||
                         response.K == "invoice.refuse")
                {
                    RpHub.Send("invoice.list");
                }
                else if (response.K == "wanted.arrest")
                {
                    RpHub.Send("wanted.list");
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP RP] Interface : " + exception);
            }
        }

        private static void ApplyHeal()
        {
            try
            {
                var rig = BoneLib.Player.RigManager;

                if (rig != null && rig.health != null)
                    rig.health.SetFullHealth();
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP RP] Soin impossible : " + exception.Message);
            }
        }

        private static List<T> Parse<T>(RpResponse response)
        {
            if (string.IsNullOrWhiteSpace(response.D))
                return new List<T>();

            List<T> list = JsonSerializer.Deserialize<List<T>>(response.D);

            return list ?? new List<T>();
        }

        private static void BuildMissions(RpResponse response)
        {
            if (!response.Ok)
            {
                RpHub.Notify(response.Msg, 2);
                return;
            }

            ResetList(missionsPage, "Actualiser mes missions",
                () => RpHub.Send("mission.list"));

            List<MissionInfo> list = Parse<MissionInfo>(response);

            if (list.Count == 0)
            {
                MenuKit.AddInfo(missionsPage,
                    "Aucune mission pour votre métier.",
                    Color.white, "[Fusion DarkRP]");
                return;
            }

            foreach (MissionInfo mission in list)
            {
                MissionInfo m = mission;

                string status = m.Done
                    ? " [terminée]"
                    : m.Step >= 0
                        ? " [" + m.Step + "/" + m.Steps.Length + "]"
                        : "";

                Page page = MenuKit.NewPage(
                    missionsPage,
                    m.Title + status,
                    m.Step >= 0 && !m.Done ? Color.yellow : Color.white);

                MenuKit.AddInfo(page, m.Briefing ?? "", Color.white,
                    "[Fusion DarkRP]");
                MenuKit.AddInfo(page,
                    "Récompense : " + BankService.FormatAmount(m.Reward),
                    Color.green, "[Fusion DarkRP]");

                for (int i = 0; i < m.Steps.Length; i++)
                {
                    string mark = m.Done || i < m.Step
                        ? "[x] "
                        : i == m.Step ? "[>] " : "[ ] ";

                    MenuKit.AddInfo(page, mark + m.Steps[i],
                        i == m.Step ? Color.yellow : Color.white,
                        "[Fusion DarkRP]");
                }

                if (m.Step >= 0 && !m.Done)
                {
                    if (!string.IsNullOrEmpty(m.Instruction))
                    {
                        MenuKit.AddInfo(page, "Consigne : " + m.Instruction,
                            Color.cyan, "[Fusion DarkRP]");
                    }

                    MenuKit.AddFunction(
                        page,
                        "Valider l'étape " + (m.Step + 1) + "/" + m.Steps.Length,
                        Color.green,
                        () => RpHub.Send("mission.advance", m.Id));
                }
                else
                {
                    MenuKit.AddFunction(
                        page,
                        m.Done ? "Recommencer la mission" : "Démarrer la mission",
                        Color.green,
                        () => RpHub.Send("mission.start", m.Id));
                }
            }
        }

        private static void BuildInvoices(RpResponse response)
        {
            if (!response.Ok)
            {
                RpHub.Notify(response.Msg, 2);
                return;
            }

            ResetList(invoicesPage, "Actualiser mes factures",
                () => RpHub.Send("invoice.list"));

            List<InvoiceInfo> list = Parse<InvoiceInfo>(response);

            if (list.Count == 0)
            {
                MenuKit.AddInfo(invoicesPage, "Aucune facture en attente.",
                    Color.white, "[Fusion DarkRP]");
                return;
            }

            foreach (InvoiceInfo invoice in list)
            {
                InvoiceInfo item = invoice;

                Page page = MenuKit.NewPage(
                    invoicesPage,
                    BankService.FormatAmount(item.Amount) + " - " + item.From,
                    Color.yellow);

                MenuKit.AddInfo(page, item.Label ?? "", Color.white,
                    "[Fusion DarkRP]");
                MenuKit.AddInfo(page, "Expire dans " + item.Secs + " s",
                    Color.white, "[Fusion DarkRP]");

                MenuKit.AddFunction(page, "PAYER", Color.green,
                    () => RpHub.Send("invoice.pay", item.Id));
                MenuKit.AddFunction(page, "REFUSER", Color.red,
                    () => RpHub.Send("invoice.refuse", item.Id));
            }
        }

        private static void BuildWanted(RpResponse response)
        {
            if (!response.Ok)
            {
                RpHub.Notify(response.Msg, 2);
                return;
            }

            ResetList(wantedPage, "Actualiser les recherchés",
                () => RpHub.Send("wanted.list"));

            List<WantedInfo> list = Parse<WantedInfo>(response);

            if (list.Count == 0)
            {
                MenuKit.AddInfo(wantedPage, "Aucun recherché.",
                    Color.white, "[Fusion DarkRP]");
                return;
            }

            foreach (WantedInfo wanted in list)
            {
                WantedInfo w = wanted;

                Page page = MenuKit.NewPage(
                    wantedPage, w.Name + " : " + w.Reason, Color.red);

                MenuKit.AddFunction(page, "Arrêter", Color.red,
                    () => RpHub.Send("wanted.arrest", null, 0, w.Id));
            }
        }
    }
}
