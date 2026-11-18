using System;
using System.Collections.Generic;
using FusionDarkRP.Economy;
using LabFusion.Network;
using LabFusion.Player;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Culture du cannabis (hôte autoritaire), réservée aux gangs :
    ///  planter (60 €) -> semis -> croissance -> plante mûre -> récolte
    ///  (3 sachets physiques saisissables) -> vente au receleur (menu).
    /// La police peut saisir plants et sachets (prime). Une plante mûre non
    /// récoltée fane au bout de 15 minutes.
    /// </summary>
    public static class DrugFarmService
    {
        private const float TickInterval = 2f;
        private const float Stage2After = 300f;
        private const float Stage3After = 900f;
        private const float WitherAfter = Stage3After + 900f;
        private const int MaxPlantsPerPlayer = 4;
        /// <summary>Prix d'un semis (affiché dans le menu : une seule source de vérité).</summary>
        public const long SeedCents = 6000L;

        /// <summary>Durée de croissance jusqu'à la maturité (secondes).</summary>
        public const float MatureAfterSeconds = Stage3After;
        private const int BagsPerHarvest = 3;
        private const float HarvestReach = 2.8f;
        private const float SellReach = 4f;
        private const float SeizeReach = 6f;
        private const int MaxSellPerTrip = 6;
        private const string TreasuryWallet = "system:town_treasury";

        private sealed class Plant
        {
            public string ObjectId;
            public string Owner;
            public int Stage = 1;
            public float PlantedAt;
            public Vector3 Position;
            public Quaternion Rotation;
            public bool Announced;
        }

        private static readonly List<Plant> Plants = new List<Plant>();
        private static float nextTick;

        public static void RegisterHandlers()
        {
            RpHub.RegisterHost("drug.plant", HandlePlant);
            RpHub.RegisterHost("drug.harvest", HandleHarvest);
            RpHub.RegisterHost("drug.sell", HandleSell);
            RpHub.RegisterHost("drug.seize", HandleSeize);
        }

        private static bool IsGang(string job)
        {
            return job == "marseillais" || job == "parisiens" ||
                   job == "gens_du_voyage";
        }

        private static string ModelFor(int stage)
        {
            return "WeedPot" + Mathf.Clamp(stage, 1, 3);
        }

        // --------------------------------------------------------------
        // Croissance
        // --------------------------------------------------------------

        public static void Update()
        {
            if (!NetworkInfo.HasServer || !NetworkInfo.IsHost)
                return;

            float now = Time.unscaledTime;

            if (now < nextTick)
                return;

            nextTick = now + TickInterval;

            for (int i = Plants.Count - 1; i >= 0; i--)
            {
                Plant plant = Plants[i];

                GameObject go;

                if (!WorldModelRuntimeManager.TryGet(plant.ObjectId, out go) ||
                    go == null)
                {
                    Plants.RemoveAt(i);
                    continue;
                }

                float age = now - plant.PlantedAt;

                if (age >= WitherAfter)
                {
                    Destroy(plant.ObjectId);
                    Plants.RemoveAt(i);
                    Tell(plant.Owner, "Une de vos plantes a fané faute de récolte.", 2);
                    continue;
                }

                int wanted = age >= Stage3After ? 3 : age >= Stage2After ? 2 : 1;

                if (wanted == plant.Stage)
                    continue;

                string newId;
                string error;

                Destroy(plant.ObjectId);

                if (!WorldModelPlacementService.TrySpawnAtPose(
                        ModelFor(wanted), plant.Position, plant.Rotation,
                        out newId, out error))
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Culture] Croissance : " + error);

                    Plants.RemoveAt(i);
                    continue;
                }

                plant.ObjectId = newId;
                plant.Stage = wanted;

                if (wanted == 3 && !plant.Announced)
                {
                    plant.Announced = true;
                    Tell(plant.Owner, "Une plante est mûre : récoltez-la (menu Métier).", 1);
                }
            }
        }

        public static void Reset()
        {
            foreach (Plant plant in Plants)
                Destroy(plant.ObjectId);

            Plants.Clear();
        }

        // --------------------------------------------------------------
        // Actions
        // --------------------------------------------------------------

        private static RpResponse HandlePlant(PlayerID sender, RpRequest request)
        {
            string pid;

            if (!PlayerIdentityService.TryGetPersistentId(sender, out pid))
                return Fail("Identité introuvable.");

            if (!IsGang(EconomyJobResolver.GetJobIdFromPlayer(sender)))
                return Fail("Seuls les gangs savent cultiver.");

            int own = 0;

            foreach (Plant plant in Plants)
            {
                if (plant.Owner == pid)
                    own++;
            }

            if (own >= MaxPlantsPerPlayer)
                return Fail("Vous cultivez déjà " + MaxPlantsPerPlayer + " plants.");

            EconomyRuntime runtime;

            if (!Runtime(out runtime))
                return Fail("Économie indisponible.");

            WalletData wallet = runtime.Wallets.GetWallet(pid);

            if (wallet == null || wallet.CashCents < SeedCents)
                return Fail("Il faut " + BankService.FormatAmount(SeedCents)
                    + " en liquide pour les graines.");

            Transform body = PhysicalCashRigAnchorResolver.ResolveBody(sender);

            if (body == null)
                return Fail("Position introuvable.");

            Vector3 forward = body.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;

            forward.Normalize();

            Vector3 position = body.position + forward * 1.1f;
            RaycastHit hit;

            if (Physics.Raycast(position + Vector3.up * 2f, Vector3.down,
                    out hit, 6f, ~0, QueryTriggerInteraction.Ignore))
            {
                position = hit.point;
            }
            else
            {
                position.y = body.position.y - 1.2f;
            }

            string error;

            if (!runtime.Wallets.TryMoveCashToWalletBank(
                    true, "seed:" + Guid.NewGuid().ToString("N"),
                    pid, TreasuryWallet, SeedCents, out error))
            {
                return Fail("Paiement refusé : " + error);
            }

            Quaternion rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            string id;

            if (!WorldModelPlacementService.TrySpawnAtPose(
                    ModelFor(1), position, rotation, out id, out error))
            {
                return Fail("Plantation impossible : " + error);
            }

            Plants.Add(new Plant
            {
                ObjectId = id,
                Owner = pid,
                PlantedAt = Time.unscaledTime,
                Position = position,
                Rotation = rotation
            });

            return Ok("Graine plantée (" + BankService.FormatAmount(SeedCents)
                + "). Maturité dans ~" + Mathf.RoundToInt(Stage3After / 60f)
                + " min.");
        }

        private static RpResponse HandleHarvest(PlayerID sender, RpRequest request)
        {
            string pid;

            if (!PlayerIdentityService.TryGetPersistentId(sender, out pid))
                return Fail("Identité introuvable.");

            if (!IsGang(EconomyJobResolver.GetJobIdFromPlayer(sender)))
                return Fail("Seuls les gangs savent récolter.");

            Transform body = PhysicalCashRigAnchorResolver.ResolveBody(sender);

            if (body == null)
                return Fail("Position introuvable.");

            Plant best = null;
            float bestDistance = HarvestReach;

            foreach (Plant plant in Plants)
            {
                float distance = (plant.Position - body.position).magnitude;

                if (distance < bestDistance)
                {
                    best = plant;
                    bestDistance = distance;
                }
            }

            if (best == null)
                return Fail("Aucune plante à portée (" + HarvestReach + " m).");

            if (best.Stage < 3)
            {
                float remaining = Stage3After - (Time.unscaledTime - best.PlantedAt);
                int minutes = Mathf.Max(1, Mathf.CeilToInt(remaining / 60f));

                return Fail("Cette plante n'est pas encore mûre (encore ~" + minutes + " min).");
            }

            // Rendement selon le moment de la récolte : plus on attend après la maturité,
            // moins la récolte est bonne (la plante fane au bout de 15 min).
            float sinceMature = Time.unscaledTime - best.PlantedAt - Stage3After;
            int bags = sinceMature <= 300f
                ? BagsPerHarvest
                : sinceMature <= 600f ? Mathf.Max(1, BagsPerHarvest - 1) : 1;
            string quality = sinceMature <= 300f
                ? "excellente"
                : sinceMature <= 600f ? "correcte" : "passée";

            Vector3 origin = best.Position + Vector3.up * 0.35f;
            int made = 0;

            for (int i = 0; i < bags; i++)
            {
                float angle = i * (Mathf.PI * 2f / bags);

                Vector3 position = origin + new Vector3(
                    Mathf.Cos(angle) * 0.25f, 0f, Mathf.Sin(angle) * 0.25f);

                string id;
                string error;

                if (WorldModelPlacementService.TrySpawnAtPose(
                        "WeedBag", position, Quaternion.identity, out id, out error))
                {
                    made++;
                }
            }

            if (made == 0)
                return Fail("Récolte impossible pour le moment.");

            // La plante repousse : retour au semis.
            Destroy(best.ObjectId);

            string newId;
            string spawnError;

            if (WorldModelPlacementService.TrySpawnAtPose(
                    ModelFor(1), best.Position, best.Rotation,
                    out newId, out spawnError))
            {
                best.ObjectId = newId;
                best.Stage = 1;
                best.PlantedAt = Time.unscaledTime;
                best.Announced = false;
            }
            else
            {
                Plants.Remove(best);
            }

            return Ok("Récolte " + quality + " : " + made + " sachet(s). Vendez-les au receleur.");
        }

        private static RpResponse HandleSell(PlayerID sender, RpRequest request)
        {
            string pid;

            if (!PlayerIdentityService.TryGetPersistentId(sender, out pid))
                return Fail("Identité introuvable.");

            EconomyRuntime runtime;

            if (!Runtime(out runtime))
                return Fail("Économie indisponible.");

            Transform body = PhysicalCashRigAnchorResolver.ResolveBody(sender);

            if (body == null)
                return Fail("Position introuvable.");

            List<string> bags = FindObjects("WeedBag", body.position, SellReach);

            if (bags.Count == 0)
                return Fail("Posez vos sachets à moins de " + SellReach
                    + " m de vous pour les vendre.");

            bool gang = IsGang(EconomyJobResolver.GetJobIdFromPlayer(sender));
            long total = 0;
            int sold = 0;

            foreach (string id in bags)
            {
                if (sold >= MaxSellPerTrip)
                    break;

                // Prix de rue variable ; les gangs connaissent les bons réseaux.
                long price = (long)(UnityEngine.Random.Range(25, 51) * 100);

                if (gang)
                    price = (long)(price * 1.2);

                Destroy(id);
                total += price;
                sold++;
            }

            string error;

            if (!runtime.Wallets.TryGiveCash(
                    true, "weed:" + Guid.NewGuid().ToString("N"),
                    pid, total, out error))
            {
                return Fail("Vente refusée : " + error);
            }

            return Ok(sold + " sachet(s) vendu(s) : " + BankService.FormatAmount(total)
                + " en liquide.");
        }

        private static RpResponse HandleSeize(PlayerID sender, RpRequest request)
        {
            string pid;

            if (!PlayerIdentityService.TryGetPersistentId(sender, out pid))
                return Fail("Identité introuvable.");

            if (EconomyJobResolver.GetJobIdFromPlayer(sender) != "police")
                return Fail("Réservé à la police.");

            EconomyRuntime runtime;

            if (!Runtime(out runtime))
                return Fail("Économie indisponible.");

            Transform body = PhysicalCashRigAnchorResolver.ResolveBody(sender);

            if (body == null)
                return Fail("Position introuvable.");

            long reward = 0;
            int plants = 0;
            int bags = 0;

            for (int i = Plants.Count - 1; i >= 0; i--)
            {
                if ((Plants[i].Position - body.position).magnitude > SeizeReach)
                    continue;

                Destroy(Plants[i].ObjectId);
                Plants.RemoveAt(i);
                plants++;
                reward += 8000L;
            }

            foreach (string id in FindObjects("WeedBag", body.position, SeizeReach))
            {
                Destroy(id);
                bags++;
                reward += 4000L;
            }

            if (plants + bags == 0)
                return Fail("Rien à saisir à moins de " + SeizeReach + " m.");

            if (reward > 50000L)
                reward = 50000L;

            string error;

            if (!runtime.Wallets.TryGiveCash(
                    true, "seize:" + Guid.NewGuid().ToString("N"),
                    pid, reward, out error))
            {
                return Fail("Prime refusée : " + error);
            }

            return Ok("Saisie : " + plants + " plant(s), " + bags
                + " sachet(s). Prime " + BankService.FormatAmount(reward) + ".");
        }

        // --------------------------------------------------------------

        private static List<string> FindObjects(
            string modelId, Vector3 center, float radius)
        {
            List<string> found = new List<string>();

            foreach (string id in WorldModelRuntimeManager.GetSpawnedObjectIds())
            {
                string model;

                if (!WorldModelRuntimeManager.TryGetModelId(id, out model) ||
                    !string.Equals(model, modelId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                GameObject go;

                if (WorldModelRuntimeManager.TryGet(id, out go) && go != null &&
                    (go.transform.position - center).magnitude <= radius)
                {
                    found.Add(id);
                }
            }

            return found;
        }

        private static void Destroy(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

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

        private static bool Runtime(out EconomyRuntime runtime)
        {
            runtime = null;

            if (!EconomyRuntimeBootstrap.IsInitialized)
                return false;

            runtime = EconomyRuntimeBootstrap.Runtime;
            return runtime != null && runtime.Wallets != null;
        }

        private static void Tell(string pid, string text, byte level)
        {
            foreach (PlayerID player in PlayerIDManager.PlayerIDs)
            {
                if (player == null || !player.IsValid)
                    continue;

                string id;

                if (!PlayerIdentityService.TryGetPersistentId(player, out id) ||
                    id != pid)
                {
                    continue;
                }

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

                return;
            }
        }

        private static RpResponse Ok(string message)
        {
            return new RpResponse { Ok = true, Msg = message };
        }

        private static RpResponse Fail(string message)
        {
            return new RpResponse { Ok = false, Msg = message };
        }
    }
}
