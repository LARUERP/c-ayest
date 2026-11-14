using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FusionDarkRP.Economy;

namespace FusionDarkRP.RP
{
    public sealed class MissionRun
    {
        public string RunId { get; set; }
        public string PlayerId { get; set; }
        public string MissionId { get; set; }
        public int CurrentStep { get; set; }
        public bool Completed { get; set; }
        public DateTime StartedUtc { get; set; }
        public DateTime? CompletedUtc { get; set; }
        public int ReputationAwarded { get; set; }
    }

    public sealed class MissionRuntime
    {
        private readonly object sync = new object();
        private readonly WalletService wallets;
        private readonly string savePath;
        private readonly Dictionary<string, MissionRun> active = new Dictionary<string, MissionRun>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> cooldowns = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static readonly JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };

        public sealed class SaveData
        {
            public List<MissionRun> Runs { get; set; } = new List<MissionRun>();
            public Dictionary<string, DateTime> Cooldowns { get; set; } = new Dictionary<string, DateTime>();
        }

        public MissionRuntime(WalletService walletService, string directory)
        {
            wallets = walletService ?? throw new ArgumentNullException(nameof(walletService));
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Dossier de missions invalide.");
            Directory.CreateDirectory(directory);
            savePath = Path.Combine(directory, "mission-progress.json");
            Load();
        }

        public MissionRun Start(bool isHost, string playerId, string jobId, string missionId)
        {
            if (!isHost) throw new UnauthorizedAccessException("Seul l'hôte peut démarrer une mission.");
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("Joueur invalide.");
            JobMissionDefinition definition = JobMissionCatalog.Get(missionId);
            if (definition == null) throw new InvalidOperationException("Mission inconnue.");
            if (!string.Equals(definition.JobId, jobId, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Mission non autorisée pour ce métier.");

            lock (sync)
            {
                string key = Key(playerId, missionId);
                MissionRun current;
                if (active.TryGetValue(key, out current) && !current.Completed)
                    throw new InvalidOperationException("Cette mission est déjà active.");

                DateTime until;
                if (cooldowns.TryGetValue(key, out until) && DateTime.UtcNow < until)
                    throw new InvalidOperationException("Mission en recharge jusqu'à " + until.ToString("HH:mm:ss") + " UTC.");

                MissionRun run = new MissionRun
                {
                    RunId = Guid.NewGuid().ToString("N"),
                    PlayerId = playerId,
                    MissionId = missionId,
                    CurrentStep = 0,
                    Completed = false,
                    StartedUtc = DateTime.UtcNow
                };
                active[key] = run;
                Save();
                return Clone(run);
            }
        }

        public MissionRun Advance(bool isHost, string playerId, string missionId, string expectedStepId)
        {
            if (!isHost) throw new UnauthorizedAccessException("Progression réservée à l'hôte.");
            if (string.IsNullOrWhiteSpace(expectedStepId)) throw new ArgumentException("Étape invalide.");
            lock (sync)
            {
                string key = Key(playerId, missionId);
                MissionRun run;
                if (!active.TryGetValue(key, out run) || run.Completed)
                    throw new InvalidOperationException("Aucune mission active.");
                JobMissionDefinition definition = JobMissionCatalog.Get(missionId);
                if (definition == null || run.CurrentStep >= definition.Steps.Length)
                    throw new InvalidOperationException("Progression incohérente.");
                if (!string.Equals(definition.Steps[run.CurrentStep].Id, expectedStepId, StringComparison.Ordinal))
                    throw new InvalidOperationException("Cette étape n'est pas la prochaine étape attendue.");

                run.CurrentStep++;
                if (run.CurrentStep >= definition.Steps.Length)
                {
                    string requestId = "mission-reward:" + run.PlayerId + ":" + run.RunId;
                    string error;
                    if (!wallets.TryGiveCash(true, requestId, run.PlayerId, definition.RewardCents, out error))
                    {
                        run.CurrentStep--;
                        throw new InvalidOperationException("Récompense non versée : " + error);
                    }
                    run.Completed = true;
                    run.CompletedUtc = DateTime.UtcNow;
                    run.ReputationAwarded = definition.ReputationReward;
                    cooldowns[key] = DateTime.UtcNow.AddMinutes(Math.Max(0, definition.CooldownMinutes));
                }
                Save();
                return Clone(run);
            }
        }

        public MissionRun GetActive(string playerId, string missionId)
        {
            lock (sync)
            {
                MissionRun run;
                return active.TryGetValue(Key(playerId, missionId), out run) ? Clone(run) : null;
            }
        }

        public IReadOnlyList<MissionRun> GetPlayerRuns(string playerId)
        {
            lock (sync)
            {
                return active.Values.Where(x => x.PlayerId == playerId).Select(Clone).ToArray();
            }
        }

        private static string Key(string playerId, string missionId) { return playerId + "|" + missionId; }
        private static MissionRun Clone(MissionRun x)
        {
            return new MissionRun
            {
                RunId = x.RunId, PlayerId = x.PlayerId, MissionId = x.MissionId,
                CurrentStep = x.CurrentStep, Completed = x.Completed, StartedUtc = x.StartedUtc,
                CompletedUtc = x.CompletedUtc, ReputationAwarded = x.ReputationAwarded
            };
        }

        private void Load()
        {
            if (!File.Exists(savePath)) return;
            try
            {
                SaveData data = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(savePath), options);
                if (data == null) return;
                lock (sync)
                {
                    foreach (MissionRun run in data.Runs ?? new List<MissionRun>())
                    {
                        if (run == null || string.IsNullOrWhiteSpace(run.PlayerId) ||
                            string.IsNullOrWhiteSpace(run.MissionId) || string.IsNullOrWhiteSpace(run.RunId)) continue;
                        JobMissionDefinition mission = JobMissionCatalog.Get(run.MissionId);
                        if (mission == null) continue;
                        run.CurrentStep = Math.Max(0, Math.Min(run.CurrentStep, mission.Steps.Length));
                        active[Key(run.PlayerId, run.MissionId)] = run;
                    }
                    if (data.Cooldowns != null)
                        foreach (KeyValuePair<string, DateTime> item in data.Cooldowns)
                            if (!string.IsNullOrWhiteSpace(item.Key)) cooldowns[item.Key] = item.Value;
                }
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning("[FusionDarkRP Missions] Sauvegarde illisible : " + ex.Message);
            }
        }

        private void Save()
        {
            SaveData data = new SaveData
            {
                Runs = active.Values.Select(Clone).ToList(),
                Cooldowns = new Dictionary<string, DateTime>(cooldowns, StringComparer.Ordinal)
            };
            string temp = savePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(data, options));
            File.Move(temp, savePath, true);
        }
    }
}
