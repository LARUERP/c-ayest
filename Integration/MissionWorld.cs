using System;
using System.Collections.Generic;
using FusionDarkRP.Economy;
using FusionDarkRP.RP;
using LabFusion.Network;
using LabFusion.Player;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Missions « réelles » (hôte autoritaire). Chaque étape a un vrai
    /// objectif dans le monde :
    ///  A = briefing (validation automatique après quelques secondes),
    ///  G = aller à une balise,
    ///  W = travailler à un poste (rester près de la balise ~10 s),
    ///  M = rencontrer un autre joueur (repli en W si on est seul),
    ///  C = ramasser des objets (déchets, caisses, dossiers, trousses...).
    /// Éboueur : des petits tas (caca) apparaissent ; la pince ou le sac
    /// les ramasse au contact ; 5 € tous les 5 déchets.
    /// </summary>
    public static class MissionWorld
    {
        private const float TickInterval = 0.4f;
        private const float AutoSeconds = 4f;
        private const float WorkSeconds = 10f;
        private const float MeetSeconds = 6f;
        private const float MeetDistance = 3f;
        private const float MarkerRadius = 2.4f;
        private const float GotoMinDistance = 22f;
        private const float GotoMaxDistance = 32f;
        private const long PoopBonusCents = 500L;
        private const float ToolReach = 0.45f;

        private enum Kind { Auto, Goto, Work, Meet, Collect }

        private sealed class Spec
        {
            public Kind Kind;
            public string Model;
            public int Count;
        }

        private sealed class Prop
        {
            public string Id;
            public Vector3 Start;
        }

        private sealed class Session
        {
            public string PlayerKey;
            public string MissionId;
            public byte SmallId;
            public int Step = -1;
            public Spec Spec;
            public readonly List<Prop> Props = new List<Prop>();
            public readonly List<string> Tools = new List<string>();
            public string MarkerId;
            public Vector3 MarkerPos;
            public float StartedAt;
            public float Progress;
            public float LastTick;
            public float NextNotice;
            public int Collected;
        }

        private static readonly Dictionary<string, Session> Sessions =
            new Dictionary<string, Session>(StringComparer.Ordinal);

        private static float nextTick;

        // --------------------------------------------------------------
        // Table des objectifs : mission -> un objectif par étape.
        // --------------------------------------------------------------

        private static readonly Dictionary<string, string[]> Plans =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "citizen_errand", new[] { "A", "C:ShopCrate:3", "G" } },
                { "citizen_delivery", new[] { "C:ShopCrate:1", "G", "M" } },
                { "citizen_social", new[] { "M", "W", "A" } },
                { "eboueurs_route", new[] { "G", "C:PoopPile:5", "C:PoopPile:5", "G" } },
                { "eboueurs_sort", new[] { "C:TrashBag:2", "W", "W" } },
                { "eboueurs_incident", new[] { "A", "C:PoopPile:5", "W" } },
                { "banque_customer", new[] { "A", "M", "W", "A" } },
                { "banque_audit", new[] { "W", "W", "A" } },
                { "banque_card", new[] { "M", "W", "A" } },
                { "mairie_dossier", new[] { "C:TownFolder:2", "W", "A" } },
                { "mairie_event", new[] { "W", "C:ShopCrate:2", "M" } },
                { "mairie_permit", new[] { "A", "G", "A" } },
                { "epicerie_stock", new[] { "C:ShopCrate:3", "C:ShopCrate:3", "W" } },
                { "epicerie_till", new[] { "A", "M", "A" } },
                { "epicerie_order", new[] { "W", "W", "C:ShopCrate:2" } },
                { "police_patrol", new[] { "A", "G", "W" } },
                { "police_traffic", new[] { "A", "G", "W" } },
                { "police_inquiry", new[] { "A", "M", "W" } },
                { "police_emergency", new[] { "A", "G", "W" } },
                { "medecins_call", new[] { "A", "M", "W" } },
                { "medecins_round", new[] { "C:MedicalKit:1", "G", "G" } },
                { "medecins_transport", new[] { "A", "W", "G" } },
                { "marseillais_contact", new[] { "M", "W", "A" } },
                { "marseillais_event", new[] { "W", "C:ShopCrate:2", "M" } },
                { "marseillais_trade", new[] { "A", "M", "W" } },
                { "parisiens_meet", new[] { "A", "M", "W" } },
                { "parisiens_network", new[] { "A", "M", "W" } },
                { "parisiens_business", new[] { "W", "W", "A" } },
                { "gens_du_voyage_trade", new[] { "W", "M", "C:ShopCrate:3" } },
                { "gens_du_voyage_help", new[] { "A", "M", "W" } },
                { "gens_du_voyage_route", new[] { "C:ShopCrate:2", "G", "A" } },
                { "armurerie_counter", new[] { "A", "W", "M" } },
                { "armurerie_stock", new[] { "W", "W", "A" } },
                { "armurerie_order", new[] { "A", "W", "C:ShopCrate:2" } }
            };

        private static Spec ParseSpec(string missionId, int step)
        {
            string[] plan;

            if (!Plans.TryGetValue(missionId, out plan) ||
                step < 0 || step >= plan.Length)
            {
                return new Spec { Kind = Kind.Auto };
            }

            string[] part = plan[step].Split(':');

            switch (part[0])
            {
                case "G": return new Spec { Kind = Kind.Goto };
                case "W": return new Spec { Kind = Kind.Work };
                case "M": return new Spec { Kind = Kind.Meet };
                case "C":
                    int count;
                    if (part.Length < 3 || !int.TryParse(part[2], out count))
                        count = 3;
                    return new Spec
                    {
                        Kind = Kind.Collect,
                        Model = part[1],
                        Count = Mathf.Clamp(count, 1, 6)
                    };
                default: return new Spec { Kind = Kind.Auto };
            }
        }

        /// <summary>
        /// Vrai si l'étape courante est suivie automatiquement dans le
        /// monde (la validation manuelle est alors refusée).
        /// </summary>
        public static bool IsAutomatic(string playerKey, string missionId)
        {
            lock (Sessions)
                return Sessions.ContainsKey(playerKey + "|" + missionId);
        }

        public static void OnMissionStarted(
            PlayerID player, string playerKey, string missionId)
        {
            if (player == null || !player.IsValid)
                return;

            lock (Sessions)
            {
                string key = playerKey + "|" + missionId;

                Session old;

                if (Sessions.TryGetValue(key, out old))
                    Cleanup(old, true);

                Sessions[key] = new Session
                {
                    PlayerKey = playerKey,
                    MissionId = missionId,
                    SmallId = player.SmallID
                };
            }
        }

        public static void Reset()
        {
            lock (Sessions)
            {
                foreach (Session session in Sessions.Values)
                    Cleanup(session, true);

                Sessions.Clear();
            }
        }

        // --------------------------------------------------------------
        // Boucle (appelée à chaque image par EconomyLifecycleMod)
        // --------------------------------------------------------------

        public static void Update()
        {
            if (!NetworkInfo.HasServer || !NetworkInfo.IsHost)
                return;

            float now = Time.unscaledTime;

            if (now < nextTick)
                return;

            nextTick = now + TickInterval;

            if (!EconomyRuntimeBootstrap.IsInitialized)
                return;

            EconomyRuntime runtime = EconomyRuntimeBootstrap.Runtime;

            if (runtime == null || runtime.Missions == null ||
                runtime.Wallets == null)
            {
                return;
            }

            // 1) Crée les sessions des missions actives (reprise après relance).
            foreach (PlayerID player in PlayerIDManager.PlayerIDs)
            {
                if (player == null || !player.IsValid)
                    continue;

                string pid;

                if (!PlayerIdentityService.TryGetPersistentId(player, out pid))
                    continue;

                IReadOnlyList<MissionRun> runs =
                    runtime.Missions.GetPlayerRuns(pid);

                foreach (MissionRun run in runs)
                {
                    if (run == null || run.Completed)
                        continue;

                    string key = pid + "|" + run.MissionId;

                    lock (Sessions)
                    {
                        if (!Sessions.ContainsKey(key))
                        {
                            Sessions[key] = new Session
                            {
                                PlayerKey = pid,
                                MissionId = run.MissionId,
                                SmallId = player.SmallID
                            };
                        }
                        else
                        {
                            Sessions[key].SmallId = player.SmallID;
                        }
                    }
                }
            }

            // 2) Fait avancer chaque session.
            List<Session> all;

            lock (Sessions)
                all = new List<Session>(Sessions.Values);

            foreach (Session session in all)
            {
                try
                {
                    TickSession(runtime, session);
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Missions] Suivi : "
                        + exception.Message);
                }
            }
        }

        private static void TickSession(
            EconomyRuntime runtime, Session session)
        {
            string key = session.PlayerKey + "|" + session.MissionId;

            PlayerID player = PlayerIDManager.GetPlayerID(session.SmallId);
            MissionRun run = runtime.Missions.GetActive(
                session.PlayerKey, session.MissionId);

            if (player == null || !player.IsValid ||
                run == null || run.Completed)
            {
                Cleanup(session, true);

                lock (Sessions)
                    Sessions.Remove(key);

                return;
            }

            JobMissionDefinition definition =
                JobMissionCatalog.Get(session.MissionId);

            if (definition == null ||
                run.CurrentStep < 0 ||
                run.CurrentStep >= definition.Steps.Length)
            {
                return;
            }

            Transform body = PhysicalCashRigAnchorResolver.ResolveBody(player);

            if (body == null)
                return;

            if (session.Step != run.CurrentStep)
            {
                Cleanup(session, false);
                BeginStep(session, player, body, definition, run.CurrentStep);
            }

            float now = Time.unscaledTime;
            float dt = Mathf.Clamp(now - session.LastTick, 0f, 1f);
            session.LastTick = now;

            bool done = false;

            switch (session.Spec.Kind)
            {
                case Kind.Auto:
                    done = now - session.StartedAt >= AutoSeconds;
                    break;

                case Kind.Goto:
                    done = TickGoto(session, player, body);
                    break;

                case Kind.Work:
                    done = TickWork(session, player, body, dt);
                    break;

                case Kind.Meet:
                    done = TickMeet(session, player, body, dt);
                    break;

                case Kind.Collect:
                    done = TickCollect(runtime, session, player, body);
                    break;
            }

            if (!done)
                return;

            string stepId = definition.Steps[run.CurrentStep].Id;

            try
            {
                MissionRun result = runtime.Missions.Advance(
                    true, session.PlayerKey, session.MissionId, stepId);

                Cleanup(session, false);
                session.Step = -1;

                if (result != null && result.Completed)
                {
                    Say(player,
                        "Mission terminée ! Récompense : "
                        + BankService.FormatAmount(definition.RewardCents)
                        + " (en espèces).", 1);

                    Cleanup(session, true);

                    lock (Sessions)
                        Sessions.Remove(key);
                }
                else if (result != null)
                {
                    Say(player,
                        "Étape validée (" + result.CurrentStep + "/"
                        + definition.Steps.Length + ").", 1);
                }
            }
            catch (Exception exception)
            {
                Say(player, "Mission : " + exception.Message, 2);
            }
        }

        // --------------------------------------------------------------
        // Début d'étape
        // --------------------------------------------------------------

        private static void BeginStep(
            Session session, PlayerID player, Transform body,
            JobMissionDefinition definition, int step)
        {
            session.Step = step;
            session.Spec = ParseSpec(session.MissionId, step);
            session.StartedAt = Time.unscaledTime;
            session.LastTick = Time.unscaledTime;
            session.Progress = 0f;
            session.Collected = 0;
            session.NextNotice = 0f;

            MissionStepDefinition info = definition.Steps[step];

            if (session.Spec.Kind == Kind.Meet && !AnyOtherPlayer(player))
                session.Spec.Kind = Kind.Work;

            string header = "Étape " + (step + 1) + "/"
                + definition.Steps.Length + " — " + info.Title + " : ";

            switch (session.Spec.Kind)
            {
                case Kind.Auto:
                    Say(player, header + info.Instruction
                        + " (validation automatique)", 0);
                    break;

                case Kind.Goto:
                    session.MarkerPos = PickPoint(
                        body,
                        GotoMinDistance + UnityEngine.Random.value
                            * (GotoMaxDistance - GotoMinDistance),
                        UnityEngine.Random.Range(-35f, 35f));

                    session.MarkerId = SpawnProp(
                        "MissionMarker", session.MarkerPos);

                    Say(player, header + "rejoignez la balise jaune.", 0);
                    break;

                case Kind.Work:
                    session.MarkerPos = PickPoint(body, 5f, 0f);
                    session.MarkerId = SpawnProp(
                        "MissionMarker", session.MarkerPos);

                    Say(player, header + "restez près de la balise "
                        + (int)WorkSeconds + " s pour travailler.", 0);
                    break;

                case Kind.Meet:
                    Say(player, header + "restez à moins de "
                        + (int)MeetDistance + " m d'un autre joueur "
                        + (int)MeetSeconds + " s.", 0);
                    break;

                case Kind.Collect:
                    BeginCollect(session, player, body, header);
                    break;
            }
        }

        private static void BeginCollect(
            Session session, PlayerID player, Transform body, string header)
        {
            bool trash = session.Spec.Model == "PoopPile";

            if (trash && session.Tools.Count == 0)
            {
                // Équipement de service : pince + sac devant le joueur.
                string grabber = SpawnProp("TrashGrabber",
                    PickPoint(body, 1.2f, -20f) + Vector3.up * 0.15f);

                string bag = SpawnProp("TrashBag",
                    PickPoint(body, 1.3f, 25f) + Vector3.up * 0.15f);

                if (grabber != null)
                    session.Tools.Add(grabber);

                if (bag != null)
                    session.Tools.Add(bag);
            }

            for (int i = 0; i < session.Spec.Count; i++)
            {
                Vector3 position = PickPoint(
                    body,
                    3.5f + UnityEngine.Random.value * 4.5f,
                    UnityEngine.Random.Range(-70f, 70f))
                    + Vector3.up * 0.12f;

                string id = SpawnProp(session.Spec.Model, position);

                if (id != null)
                {
                    session.Props.Add(new Prop
                    {
                        Id = id,
                        Start = position
                    });
                }
            }

            if (session.Props.Count == 0)
            {
                Say(player, header + "aucun objet n'a pu apparaître, "
                    + "l'étape est validée.", 2);
                session.Spec = new Spec { Kind = Kind.Auto };
                session.StartedAt = Time.unscaledTime - AutoSeconds;
                return;
            }

            Say(player, header + (trash
                ? "des déchets sont apparus : touchez-les avec la pince "
                    + "ou le sac (5 € tous les 5)."
                : "récupérez et déplacez " + session.Props.Count
                    + " objet(s) apparu(s) près de vous."), 0);
        }

        // --------------------------------------------------------------
        // Suivi par type d'objectif
        // --------------------------------------------------------------

        private static bool TickGoto(
            Session session, PlayerID player, Transform body)
        {
            Vector3 delta = body.position - session.MarkerPos;
            float flat = new Vector2(delta.x, delta.z).magnitude;

            if (flat <= MarkerRadius && Mathf.Abs(delta.y) < 4f)
                return true;

            Remind(session, player, "Balise à " + Mathf.RoundToInt(flat) + " m.");
            return false;
        }

        private static bool TickWork(
            Session session, PlayerID player, Transform body, float dt)
        {
            Vector3 delta = body.position - session.MarkerPos;
            float flat = new Vector2(delta.x, delta.z).magnitude;

            if (flat <= MarkerRadius && Mathf.Abs(delta.y) < 4f)
                session.Progress += dt;

            if (session.Progress >= WorkSeconds)
                return true;

            Remind(session, player, flat <= MarkerRadius
                ? "Travail en cours : " + Mathf.RoundToInt(
                    session.Progress / WorkSeconds * 100f) + " %"
                : "Retournez près de la balise (" + Mathf.RoundToInt(flat) + " m).");

            return false;
        }

        private static bool TickMeet(
            Session session, PlayerID player, Transform body, float dt)
        {
            bool near = false;

            foreach (PlayerID other in PlayerIDManager.PlayerIDs)
            {
                if (other == null || !other.IsValid ||
                    other.SmallID == player.SmallID)
                {
                    continue;
                }

                Transform otherBody =
                    PhysicalCashRigAnchorResolver.ResolveBody(other);

                if (otherBody == null)
                    continue;

                if ((otherBody.position - body.position).magnitude
                    <= MeetDistance)
                {
                    near = true;
                    break;
                }
            }

            if (near)
                session.Progress += dt;

            if (session.Progress >= MeetSeconds)
                return true;

            Remind(session, player, near
                ? "Échange en cours : " + Mathf.RoundToInt(
                    session.Progress / MeetSeconds * 100f) + " %"
                : "Approchez-vous d'un autre joueur.");

            return false;
        }

        private static bool TickCollect(
            EconomyRuntime runtime, Session session,
            PlayerID player, Transform body)
        {
            bool trash = session.Spec.Model == "PoopPile";

            List<Vector3> tools = trash ? ToolPositions() : null;

            for (int i = session.Props.Count - 1; i >= 0; i--)
            {
                Prop prop = session.Props[i];
                GameObject go;

                bool collected = false;

                if (!WorldModelRuntimeManager.TryGet(prop.Id, out go) ||
                    go == null)
                {
                    collected = true;
                }
                else
                {
                    Vector3 position = go.transform.position;
                    float moved = (position - prop.Start).magnitude;

                    if (trash)
                    {
                        foreach (Vector3 tool in tools)
                        {
                            if ((tool - position).magnitude <= ToolReach)
                            {
                                collected = true;
                                break;
                            }
                        }

                        // Sans outil à portée : l'emporter loin suffit.
                        if (!collected && moved > 3.5f)
                            collected = true;
                    }
                    else if (moved > 1.5f)
                    {
                        collected = true;
                    }
                }

                if (!collected)
                    continue;

                DestroyProp(prop.Id);
                session.Props.RemoveAt(i);
                session.Collected++;

                Say(player, (trash ? "Déchets ramassés : " : "Objets : ")
                    + session.Collected + "/" + session.Spec.Count, 0);
            }

            if (session.Collected < session.Spec.Count)
                return false;

            if (trash)
            {
                string error;

                if (runtime.Wallets.TryGiveCash(
                        true,
                        "poop:" + session.PlayerKey + ":" + Guid.NewGuid().ToString("N"),
                        session.PlayerKey,
                        PoopBonusCents,
                        out error))
                {
                    Say(player, "+" + BankService.FormatAmount(PoopBonusCents)
                        + " : " + session.Spec.Count
                        + " déchets ramassés.", 1);
                }
                else
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Missions] Prime déchets : " + error);
                }
            }

            return true;
        }

        // --------------------------------------------------------------
        // Utilitaires monde
        // --------------------------------------------------------------

        private static List<Vector3> ToolPositions()
        {
            List<Vector3> result = new List<Vector3>();

            foreach (string id in WorldModelRuntimeManager.GetSpawnedObjectIds())
            {
                string model;

                if (!WorldModelRuntimeManager.TryGetModelId(id, out model))
                    continue;

                if (model != "TrashGrabber" && model != "TrashBag")
                    continue;

                GameObject go;

                if (WorldModelRuntimeManager.TryGet(id, out go) && go != null)
                {
                    // La pince : on teste son extrémité (haut du modèle).
                    Vector3 tip = model == "TrashGrabber"
                        ? go.transform.position + go.transform.up * 0.35f
                        : go.transform.position;

                    result.Add(tip);

                    if (model == "TrashGrabber")
                        result.Add(go.transform.position);
                }
            }

            return result;
        }

        private static bool AnyOtherPlayer(PlayerID me)
        {
            foreach (PlayerID other in PlayerIDManager.PlayerIDs)
            {
                if (other != null && other.IsValid &&
                    other.SmallID != me.SmallID)
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector3 PickPoint(
            Transform body, float distance, float yawDegrees)
        {
            Vector3 forward = body.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;

            forward.Normalize();

            Vector3 direction =
                Quaternion.AngleAxis(yawDegrees, Vector3.up) * forward;

            Vector3 point = body.position + direction * distance;

            RaycastHit hit;

            if (Physics.Raycast(
                    point + Vector3.up * 4f,
                    Vector3.down,
                    out hit,
                    12f,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }

            point.y = body.position.y - 1.2f;
            return point;
        }

        private static string SpawnProp(string modelId, Vector3 position)
        {
            try
            {
                string id;
                string error;

                if (WorldModelPlacementService.TrySpawnAtPose(
                        modelId, position,
                        Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f),
                        out id, out error))
                {
                    return id;
                }

                MelonLogger.Warning(
                    "[FusionDarkRP Missions] Apparition " + modelId
                    + " : " + error);
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Missions] Apparition " + modelId
                    + " : " + exception.Message);
            }

            return null;
        }

        private static void DestroyProp(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            try
            {
                string error;

                if (WorldModelRuntimeManager.TryDestroy(id, out error))
                    WorldModelNetwork.BroadcastDestroy(id);
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Missions] Suppression : "
                    + exception.Message);
            }
        }

        private static void Cleanup(Session session, bool includeTools)
        {
            foreach (Prop prop in session.Props)
                DestroyProp(prop.Id);

            session.Props.Clear();

            if (session.MarkerId != null)
            {
                DestroyProp(session.MarkerId);
                session.MarkerId = null;
            }

            if (includeTools)
            {
                foreach (string id in session.Tools)
                    DestroyProp(id);

                session.Tools.Clear();
            }
        }

        private static void Remind(
            Session session, PlayerID player, string text)
        {
            float now = Time.unscaledTime;

            if (now < session.NextNotice)
                return;

            session.NextNotice = now + 6f;
            Say(player, text, 0);
        }

        private static void Say(PlayerID player, string text, byte level)
        {
            try
            {
                if (player == null || !player.IsValid)
                    return;

                if (player.IsMe)
                    RpHub.Notify(text, level);
                else
                    RpHub.PushNotice(player, text, level);
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Missions] Message : " + exception.Message);
            }
        }
    }
}
