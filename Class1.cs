using System;
using System.Collections.Generic;
using System.Globalization;
using BoneLib.BoneMenu;
using FusionDarkRP.Economy;
using FusionDarkRP.Integration;
using FusionDarkRP.RP;
using FusionDarkRP.Telephone;
using LabFusion.Network;
using LabFusion.Player;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP
{
    /// <summary>
    /// Point d'entrée principal : réseau DarkRP, économie, menu BoneMenu.
    /// Toutes les API BoneMenu / LabFusion sont appelées de façon typée
    /// (plus de réflexion), sur le modèle déjà utilisé par PhoneMod.
    /// </summary>
    public class MainMod : MelonMod
    {
        private PhoneMod phone;

        private const string TEAM_KEY = "FusionDarkRP.Team";
        private const string GRADE_KEY = "FusionDarkRP.Grade";

        private static bool menuCree;
        private static bool demandeInitialeEnvoyee;
        private static DateTime prochaineTentative;

        private static readonly Dictionary<byte, Page> pagesJoueurs =
            new Dictionary<byte, Page>();

        private static readonly HashSet<byte> pagesGestionOuvertes =
            new HashSet<byte>();

        private static Page pageJoueursRacine;

        public override void OnInitializeMelon()
        {
            try
            {
                DarkRPNetwork.Register();
                MelonLogger.Msg("[Fusion DarkRP] Reseau initialise.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Fusion DarkRP] Erreur reseau : " + ex);
            }

            try
            {
                // Patches Harmony (voix du téléphone) : un seul PatchAll
                // pour l'unique DLL.
                HarmonyInstance.PatchAll(typeof(MainMod).Assembly);
                MelonLogger.Msg("[Fusion DarkRP] Patches Harmony appliques.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Fusion DarkRP] Patches Harmony : " + ex);
            }

            try
            {
                // Phone is hosted by the single Melon entry point so the
                // complete project is delivered as one DLL.
                phone = new PhoneMod();
                phone.Initialize();
                MelonLogger.Msg("[Fusion DarkRP] Telephone integre initialise.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Fusion DarkRP] Initialisation telephone : " + ex);
            }

            MelonLogger.Msg(PlatformIds.IsTextPlatform
                ? "[Fusion DarkRP] Build EOS : identifiants Epic détectés."
                : "[Fusion DarkRP] Attention : ce build vise LabFusion EOS (Steam détecté).");

            EconomyLifecycleMod.Initialize();
            RpHub.Initialize();
        }

        public override void OnUpdate()
        {
            try
            {
                phone?.Tick();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Fusion DarkRP] Mise a jour telephone : " + ex.Message);
            }

            EconomyLifecycleMod.Update();

            if (DateTime.UtcNow < prochaineTentative)
                return;

            prochaineTentative = DateTime.UtcNow.AddSeconds(3);

            if (!demandeInitialeEnvoyee)
                InitialiserCitoyen();

            if (!menuCree)
                CreerMenu();
        }

        public override void OnDeinitializeMelon()
        {
            try
            {
                BankMenu.Shutdown();
                RpMenus.Reset();
                RpHub.Shutdown();
                EconomyLifecycleMod.Shutdown();
                RuntimeObjTextureLoader.Cleanup();
                phone?.Shutdown();
                phone = null;
            }
            catch (Exception ex)
            {
                MelonLogger.Error(
                    "[Fusion DarkRP] Fermeture du systeme economique : " + ex
                );
            }
        }

        // ------------------------------------------------------------
        // Joueur local
        // ------------------------------------------------------------

        private static PlayerID LocalPlayer()
        {
            PlayerID local = PlayerIDManager.LocalID;

            return local != null && local.IsValid ? local : null;
        }

        private static void InitialiserCitoyen()
        {
            try
            {
                PlayerID local = LocalPlayer();

                if (local == null)
                    return;

                string equipe = DarkRPNetwork.GetMetadata(local, TEAM_KEY);
                string grade = DarkRPNetwork.GetMetadata(local, GRADE_KEY);

                if (string.IsNullOrEmpty(equipe) ||
                    string.IsNullOrEmpty(grade))
                {
                    DarkRPNetwork.Request(local.SmallID, 0, 0);
                }

                demandeInitialeEnvoyee = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(
                    "[Fusion DarkRP] Initialisation citoyen : " + ex.Message
                );
            }
        }

        private static void RequestSelf(byte team, byte grade)
        {
            try
            {
                PlayerID local = LocalPlayer();

                if (local == null)
                {
                    MelonLogger.Warning(
                        "[Fusion DarkRP] Joueur local introuvable."
                    );
                    return;
                }

                DarkRPNetwork.Request(local.SmallID, team, grade);

                MelonLogger.Msg(
                    "[Fusion DarkRP] Demande envoyee au serveur."
                );
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Fusion DarkRP] Demande : " + ex);
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

        private static string MyTeam()
        {
            return DarkRPNetwork.GetMetadata(LocalPlayer(), TEAM_KEY);
        }

        private static string MyGrade()
        {
            return DarkRPNetwork.GetMetadata(LocalPlayer(), GRADE_KEY);
        }

        // ------------------------------------------------------------
        // Menu
        // ------------------------------------------------------------

        private static void CreerMenu()
        {
            try
            {
                Page main = MenuKit.NewPage(
                    Page.Root, "Fusion DarkRP", Color.yellow
                );

                if (main == null)
                    return;

                BankMenu.Initialize(main);
                RpMenus.Build(main);

                Page jobs = MenuKit.NewPage(main, "Jobs publics", Color.white);
                Page factions = MenuKit.NewPage(main, "Factions", Color.gray);
                Page hierarchy = MenuKit.NewPage(main, "Hierarchies", Color.white);
                pageJoueursRacine = MenuKit.NewPage(
                    main, "Joueurs connectes", Color.cyan
                );
                Page character = MenuKit.NewPage(
                    main, "Mon personnage", Color.green
                );


                AddFunction(jobs, "Redevenir citoyen", Color.white,
                    () => RequestSelf(0, 0));
                AddFunction(jobs, "Devenir eboueur", Color.green,
                    () => RequestSelf(1, 1));

                AddInfo(factions, "Banque : recrutement obligatoire", Color.yellow);
                AddInfo(factions, "Mairie : recrutement obligatoire", Color.cyan);
                AddInfo(factions, "Epicerie : recrutement obligatoire", Color.green);
                AddInfo(factions, "Police : recrutement obligatoire", Color.blue);
                AddInfo(factions, "Medecins : recrutement obligatoire", Color.green);
                AddInfo(factions, "Les Marseillais : recrutement obligatoire", Color.red);
                AddInfo(factions, "Les Parisiens : recrutement obligatoire", Color.blue);
                AddInfo(factions, "Les Gens du Voyage : recrutement obligatoire", Color.yellow);
                AddInfo(factions, "Armurerie : recrutement obligatoire", Color.green);
                AddInfo(factions, "Concession automobile : recrutement obligatoire", Color.cyan);

                AddHierarchy(hierarchy, "Mairie", 3, Color.yellow);
                AddHierarchy(hierarchy, "Police", 5, Color.blue);
                AddHierarchy(hierarchy, "Medecins", 6, Color.green);
                AddHierarchy(hierarchy, "Eboueurs", 1, Color.green);
                AddHierarchy(hierarchy, "Banque", 2, Color.yellow);
                AddHierarchy(hierarchy, "Epicerie", 4, Color.green);
                AddHierarchy(hierarchy, "Marseillais", 7, Color.red);
                AddHierarchy(hierarchy, "Parisiens", 8, Color.blue);
                AddHierarchy(hierarchy, "Gens du Voyage", 9, Color.yellow);
                AddHierarchy(hierarchy, "Armurerie", 10, Color.green);
                AddHierarchy(hierarchy, "Concession automobile", 11, Color.cyan);

                AddFunction(pageJoueursRacine, "Actualiser les joueurs",
                    Color.cyan, RefreshPlayers);

                AddFunction(character, "Consulter mon metier et mon grade",
                    Color.white, ShowStatus);
                AddFunction(character, "Demissionner", Color.green,
                    () => RequestSelf(0, 0));

                menuCree = true;

                MelonLogger.Msg("[Fusion DarkRP] Menu cree.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Fusion DarkRP] Creation menu : " + ex);
            }
        }

        /// <summary>
        /// Catalogue consultable des missions et équipements par métier.
        /// Lecture seule : le service de missions reste hôte-autoritaire
        /// et n'est pas encore relié à une interface joueur.
        /// </summary>
        private static void AddCatalogPages(Page main)
        {
            Page missions = MenuKit.NewPage(
                main, "Missions (catalogue)", Color.cyan
            );

            string lastJob = null;
            Page jobPage = null;

            foreach (JobMissionDefinition mission in JobMissionCatalog.AllMissions)
            {
                if (mission == null)
                    continue;

                if (jobPage == null || mission.JobId != lastJob)
                {
                    lastJob = mission.JobId;
                    jobPage = MenuKit.NewPage(
                        missions, mission.JobId ?? "?", Color.white
                    );
                }

                string reward = (mission.RewardCents / 100.0).ToString(
                    "0.00", CultureInfo.InvariantCulture
                );

                Page page = MenuKit.NewPage(jobPage, mission.Title, Color.yellow);

                AddInfo(page, mission.Briefing ?? "", Color.white);
                AddInfo(page, "Recompense : " + reward + " EUR", Color.green);

                if (mission.Steps != null)
                {
                    for (int i = 0; i < mission.Steps.Length; i++)
                    {
                        AddInfo(
                            page,
                            (i + 1) + ". " + mission.Steps[i].Title,
                            Color.white
                        );
                    }
                }
            }

            Page equipment = MenuKit.NewPage(
                main, "Equipement (catalogue)", Color.cyan
            );

            foreach (EquipmentDefinition item in JobEquipmentCatalog.All)
            {
                if (item == null)
                    continue;

                AddInfo(
                    equipment,
                    item.Name + " [" + item.JobId + ", grade " +
                    item.MinimumGrade + "+]",
                    Color.white
                );
            }
        }

        private static void AddHierarchy(
            Page parent, string name, byte team, Color color)
        {
            Page page = MenuKit.NewPage(parent, name, color);

            string[] ranks = Ranks(team);

            for (int i = 0; i < ranks.Length; i++)
            {
                byte gradeId = DarkRPJobs.GradeId(ranks[i]);
                string displayName = DarkRPJobs.DisplayGrade(team, gradeId);

                if (string.IsNullOrEmpty(displayName))
                    displayName = ranks[i];

                AddInfo(page, (i + 1) + ". " + displayName, Color.white);
            }

            AddInfo(
                page,
                "Pour recruter ou promouvoir : Joueurs connectes > joueur > gestion",
                Color.white
            );
        }

        private static void RefreshPlayers()
        {
            try
            {
                if (pageJoueursRacine == null)
                    return;

                foreach (Page old in pagesJoueurs.Values)
                {
                    if (old != null)
                        pageJoueursRacine.RemovePage(old);
                }

                pagesJoueurs.Clear();
                pagesGestionOuvertes.Clear();

                foreach (PlayerID player in PlayerIDManager.PlayerIDs)
                {
                    if (player == null || !player.IsValid)
                        continue;

                    byte id = player.SmallID;

                    if (pagesJoueurs.ContainsKey(id))
                        continue;

                    string name = DarkRPNetwork.GetPlayerName(id);

                    if (string.IsNullOrWhiteSpace(name))
                        name = "Joueur ID " + id;

                    Page playerPage = MenuKit.NewPage(
                        pageJoueursRacine,
                        name + " [ID " + id + "]",
                        Color.cyan
                    );

                    string currentTeam =
                        DarkRPNetwork.GetMetadata(player, TEAM_KEY) ?? "Citoyen";
                    string currentGrade =
                        DarkRPNetwork.GetMetadata(player, GRADE_KEY) ?? "Citoyen";
                    byte currentTeamId = TeamId(currentTeam);

                    AddInfo(playerPage,
                        "METIER : " + DarkRPJobs.TeamDisplay(currentTeamId),
                        Color.yellow);
                    AddInfo(playerPage,
                        "GRADE : " + DisplayGrade(currentGrade, currentTeamId),
                        Color.white);

                    if (player.IsMe)
                    {
                        Page personal = MenuKit.NewPage(
                            playerPage, "Mon metier", Color.white
                        );

                        AddFunction(personal, "Demissionner", Color.white,
                            () => RequestSelf(0, 0));
                        AddFunction(personal, "Rejoindre les eboueurs",
                            Color.green, () => RequestSelf(1, 1));
                    }

                    byte target = id;

                    AddFunction(playerPage, "Ouvrir gestion / recrutement",
                        Color.yellow,
                        () => OpenPlayerManagement(playerPage, target));

                    RpMenus.AddPlayerActions(playerPage, id, player.IsMe);

                    pagesJoueurs[id] = playerPage;
                }

                MelonLogger.Msg("[Fusion DarkRP] Liste des joueurs actualisee.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error(
                    "[Fusion DarkRP] Actualisation joueurs : " + ex
                );
            }
        }

        private static void OpenPlayerManagement(Page playerPage, byte target)
        {
            try
            {
                bool host = IsHost();
                bool manager = CanManageOwnTeam();

                if (!host && !manager)
                {
                    MelonLogger.Warning(
                        "[Fusion DarkRP] Acces refuse : seuls l'hote et les chefs peuvent recruter."
                    );
                    return;
                }

                if (pagesGestionOuvertes.Contains(target))
                {
                    MelonLogger.Msg(
                        "[Fusion DarkRP] La page de gestion de ce joueur existe deja."
                    );
                    return;
                }

                Page management = MenuKit.NewPage(
                    playerPage, "Recrutement / gestion", Color.yellow
                );

                if (host)
                {
                    AddTargetTeam(management, target, 1, Color.green, true);
                    AddTargetTeam(management, target, 2, Color.yellow, true);
                    AddTargetTeam(management, target, 3, Color.cyan, true);
                    AddTargetTeam(management, target, 4, Color.green, true);
                    AddTargetTeam(management, target, 5, Color.blue, true);
                    AddTargetTeam(management, target, 6, Color.green, true);
                    AddTargetTeam(management, target, 7, Color.red, true);
                    AddTargetTeam(management, target, 8, Color.blue, true);
                    AddTargetTeam(management, target, 9, Color.yellow, true);
                    AddTargetTeam(management, target, 10, Color.green, true);
                    AddTargetTeam(management, target, 11, Color.cyan, true);

                    AddFunction(management, "Renvoyer citoyen", Color.white,
                        () => DarkRPNetwork.Request(target, 0, 0));

                    AddInfo(management,
                        "Droits hote : toutes les equipes et tous les grades",
                        Color.yellow);
                }
                else
                {
                    byte teamId = TeamId(MyTeam());

                    if (teamId != 0)
                    {
                        AddTargetTeam(
                            management, target, teamId,
                            TeamColor(teamId), false
                        );
                    }

                    AddFunction(management, "Renvoyer citoyen", Color.white,
                        () => DarkRPNetwork.Request(target, 0, 0));

                    AddInfo(management,
                        "Tu peux gerer uniquement ta propre equipe",
                        Color.yellow);
                }

                pagesGestionOuvertes.Add(target);

                MelonLogger.Msg(
                    "[Fusion DarkRP] Page de gestion ouverte pour ID " + target
                );
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Fusion DarkRP] Ouverture gestion : " + ex);
            }
        }

        private static Color TeamColor(byte team)
        {
            switch (team)
            {
                case 2:
                case 3:
                case 9:
                    return Color.yellow;
                case 5:
                case 8:
                    return Color.blue;
                case 7:
                    return Color.red;
                case 11:
                    return Color.cyan;
                default:
                    return Color.green;
            }
        }

        private static void AddTargetTeam(
            Page parent, byte target, byte team, Color color,
            bool includeTopRank)
        {
            Page page = MenuKit.NewPage(
                parent, DarkRPJobs.TeamDisplay(team), color
            );

            string[] ranks = Ranks(team);

            for (int i = 0; i < ranks.Length; i++)
            {
                if (!includeTopRank && i == ranks.Length - 1)
                    continue;

                byte grade = DarkRPJobs.GradeId(ranks[i]);

                if (!DarkRPJobs.IsValidAssignment(team, grade))
                    continue;

                string label = DarkRPJobs.DisplayGrade(team, grade);
                byte t = team;
                byte g = grade;

                AddFunction(page, label, color,
                    () => DarkRPNetwork.Request(target, t, g));
            }
        }

        private static bool CanManageOwnTeam()
        {
            string team = MyTeam();
            string grade = MyGrade();

            if (string.IsNullOrEmpty(team) || string.IsNullOrEmpty(grade))
                return false;

            byte teamId = TeamId(team);
            byte gradeId = DarkRPJobs.GradeId(grade);

            if (!DarkRPJobs.IsValidAssignment(teamId, gradeId))
                return false;

            DarkRPJob job = DarkRPJobs.GetJob(teamId);

            return job != null &&
                   DarkRPJobs.RankIndex(teamId, gradeId) == job.Grades.Length - 1;
        }

        private static void ShowStatus()
        {
            try
            {
                byte teamId = DarkRPJobs.TeamId(MyTeam());
                byte gradeId = DarkRPJobs.GradeId(MyGrade());

                if (teamId == 255 || gradeId == 255 ||
                    !DarkRPJobs.IsValidAssignment(teamId, gradeId))
                {
                    teamId = 0;
                    gradeId = 0;
                }

                DarkRPNetwork.ShowNotification(teamId, gradeId);

                MelonLogger.Msg(
                    "[Fusion DarkRP] Statut notifie : " +
                    DarkRPJobs.TeamDisplay(teamId) + " / " +
                    DarkRPJobs.DisplayGrade(teamId, gradeId)
                );
            }
            catch (Exception ex)
            {
                MelonLogger.Error(
                    "[Fusion DarkRP] Affichage du statut : " + ex
                );
            }
        }

        // ------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------

        private static void AddInfo(Page page, string label, Color color)
        {
            MenuKit.AddInfo(page, label, color, "[Fusion DarkRP]");
        }

        private static void AddFunction(
            Page page, string label, Color color, Action action)
        {
            MenuKit.AddFunction(page, label, color, action);
        }

        private static string[] Ranks(byte team)
        {
            DarkRPJob job = DarkRPJobs.GetJob(team);

            return job == null ? new string[0] : job.Grades;
        }

        private static byte TeamId(string team)
        {
            DarkRPJob job = DarkRPJobs.GetJob(team);

            return job == null ? (byte)0 : job.Id;
        }

        private static string DisplayGrade(string grade, byte team)
        {
            if (string.IsNullOrEmpty(grade))
                return "Citoyen";

            byte gradeId = DarkRPJobs.GradeId(grade);

            if (gradeId == 255)
                return grade;

            string display = DarkRPJobs.DisplayGrade(team, gradeId);

            return string.IsNullOrEmpty(display) ? grade : display;
        }
    }
}
