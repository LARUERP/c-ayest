using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FusionDarkRP.Economy
{
    public sealed class JobSalary
    {
        public string JobId { get; set; } = "";
        public long AmountCents { get; set; }
    }

    public sealed class EconomyConfigData
    {
        public int SchemaVersion { get; set; } = 1;
        public long StartingCashCents { get; set; } = 10000;
        public int SalaryIntervalMinutes { get; set; } = 15;
        public long DefaultSalaryCents { get; set; } = 25000;

        /// <summary>
        /// Migration unique : l'ancien salaire de citoyen (50 EUR) dépassait celui d'un éboueur
        /// (35 EUR) ou d'un gang (15 EUR) ; il passe à 20 EUR (aide sans emploi).
        /// </summary>
        public bool CitizenPayAligned { get; set; }

        public List<JobSalary> JobSalaries { get; set; } =
            new List<JobSalary>
            {
                new JobSalary
                {
                    JobId = "citizen",
                    AmountCents = 2000
                }
            };
    }

    public sealed class EconomyConfig
    {
        private readonly object sync = new object();
        private readonly string filePath;
        private EconomyConfigData data;

        private bool migratedOnLoad;

        private const long GradeBonusCents = 10000;
        private const int MaximumGradeIndex = 4;

        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions
            {
                WriteIndented = true
            };

        public EconomyConfig(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException(
                    "Le dossier de configuration est invalide.");

            Directory.CreateDirectory(directory);

            filePath = Path.Combine(
                directory,
                "economy-config.json");

            Load();

            if (migratedOnLoad)
            {
                lock (sync)
                    SaveInternal();

                migratedOnLoad = false;
            }
        }

        public EconomyConfigData GetSnapshot()
        {
            lock (sync)
            {
                return Clone(data);
            }
        }

        public long GetSalary(string jobId)
        {
            lock (sync)
            {
                return GetBaseSalaryInternal(jobId);
            }
        }

        // Grille de salaires (centimes) : base du 1er grade + palier par grade.
        // Une entrée explicite dans economy-config.json garde la priorité.
        private static readonly Dictionary<string, long[]> PayScale =
            new Dictionary<string, long[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "eboueurs",       new[] { 3500L, 800L } },
                { "epicerie",       new[] { 3800L, 1000L } },
                { "concessionnaire",new[] { 4200L, 1400L } },
                { "armurerie",      new[] { 4500L, 1300L } },
                { "police",         new[] { 5000L, 1500L } },
                { "mairie",         new[] { 5200L, 1800L } },
                { "banque",         new[] { 5500L, 2000L } },
                { "medecins",       new[] { 5500L, 2200L } },
                { "marseillais",    new[] { 1500L, 1200L } },
                { "parisiens",      new[] { 1500L, 1200L } },
                { "gens_du_voyage", new[] { 1500L, 1000L } }
            };

        public long GetSalary(string jobId, int gradeIndex)
        {
            lock (sync)
            {
                long[] scale;

                if (!string.IsNullOrWhiteSpace(jobId) &&
                    PayScale.TryGetValue(jobId, out scale) &&
                    !data.JobSalaries.Exists(item => item != null &&
                        string.Equals(item.JobId, jobId,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    int grade = Math.Max(0, Math.Min(gradeIndex, MaximumGradeIndex));
                    return scale[0] + grade * scale[1];
                }

                long baseSalary = GetBaseSalaryInternal(jobId);

                if (string.Equals(
                    jobId,
                    "citizen",
                    StringComparison.OrdinalIgnoreCase))
                    return baseSalary;

                int safeGrade = Math.Max(
                    0,
                    Math.Min(gradeIndex, MaximumGradeIndex));

                long bonus = safeGrade * GradeBonusCents;

                if (baseSalary > long.MaxValue - bonus)
                    return long.MaxValue;

                return baseSalary + bonus;
            }
        }

        private long GetBaseSalaryInternal(string jobId)
        {
            if (!string.IsNullOrWhiteSpace(jobId))
            {
                foreach (JobSalary salary in data.JobSalaries)
                {
                    if (salary == null)
                        continue;

                    if (string.Equals(
                        salary.JobId,
                        jobId,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return Math.Max(0, salary.AmountCents);
                    }
                }
            }

            return Math.Max(0, data.DefaultSalaryCents);
        }

        public bool SetSalary(string jobId, long amountCents)
        {
            if (string.IsNullOrWhiteSpace(jobId) || amountCents < 0)
                return false;

            lock (sync)
            {
                EconomyConfigData previous = Clone(data);

                JobSalary existing = data.JobSalaries.Find(
                    item => item != null &&
                    string.Equals(
                        item.JobId,
                        jobId.Trim(),
                        StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    data.JobSalaries.Add(new JobSalary
                    {
                        JobId = jobId.Trim(),
                        AmountCents = amountCents
                    });
                }
                else
                {
                    existing.AmountCents = amountCents;
                }

                if (SaveInternal())
                    return true;

                data = previous;
                return false;
            }
        }

        public bool SetDefaultSalary(long amountCents)
        {
            if (amountCents < 0)
                return false;

            lock (sync)
            {
                long previous = data.DefaultSalaryCents;
                data.DefaultSalaryCents = amountCents;

                if (SaveInternal())
                    return true;

                data.DefaultSalaryCents = previous;
                return false;
            }
        }

        public bool SetStartingCash(long amountCents)
        {
            if (amountCents < 0)
                return false;

            lock (sync)
            {
                long previous = data.StartingCashCents;
                data.StartingCashCents = amountCents;

                if (SaveInternal())
                    return true;

                data.StartingCashCents = previous;
                return false;
            }
        }

        public bool SetSalaryInterval(int minutes)
        {
            if (minutes < 1 || minutes > 1440)
                return false;

            lock (sync)
            {
                int previous = data.SalaryIntervalMinutes;
                data.SalaryIntervalMinutes = minutes;

                if (SaveInternal())
                    return true;

                data.SalaryIntervalMinutes = previous;
                return false;
            }
        }

        private void Load()
        {
            lock (sync)
            {
                string backupPath = filePath + ".bak";

                if (File.Exists(filePath))
                {
                    try
                    {
                        LoadFromFile(filePath);
                        return;
                    }
                    catch (Exception exception)
                    {
                        MelonLoader.MelonLogger.Warning(
                            "[FusionDarkRP] Configuration invalide : "
                            + exception.Message);
                    }
                }

                if (File.Exists(backupPath))
                {
                    try
                    {
                        LoadFromFile(backupPath);

                        MelonLoader.MelonLogger.Warning(
                            "[FusionDarkRP] Configuration restauree depuis la sauvegarde.");

                        SaveInternal();
                        return;
                    }
                    catch (Exception exception)
                    {
                        MelonLoader.MelonLogger.Error(
                            "[FusionDarkRP] Sauvegarde de configuration invalide : "
                            + exception.Message);
                    }
                }

                data = new EconomyConfigData();
                Validate();

                if (!SaveInternal())
                {
                    MelonLoader.MelonLogger.Error(
                        "[FusionDarkRP] Impossible de creer la configuration economique.");
                }
            }
        }

        private void LoadFromFile(string path)
        {
            EconomyConfigData loaded =
                JsonSerializer.Deserialize<EconomyConfigData>(
                    File.ReadAllText(path),
                    JsonOptions);

            if (loaded == null || loaded.SchemaVersion != 1)
                throw new InvalidDataException(
                    "Version de configuration invalide.");

            data = loaded;
            Validate();
        }

        private void Validate()
        {
            if (data == null)
                data = new EconomyConfigData();

            data.SchemaVersion = 1;

            if (data.StartingCashCents < 0)
                data.StartingCashCents = 10000;

            if (data.DefaultSalaryCents < 0)
                data.DefaultSalaryCents = 25000;

            // Migration de l'ancien salaire generique de 50 euros.
            // Le nouveau salaire de base des autres metiers est 250 euros.
            if (data.DefaultSalaryCents == 5000)
                data.DefaultSalaryCents = 25000;

            if (data.SalaryIntervalMinutes < 1 ||
                data.SalaryIntervalMinutes > 1440)
                data.SalaryIntervalMinutes = 15;

            if (data.JobSalaries == null)
                data.JobSalaries = new List<JobSalary>();

            data.JobSalaries.RemoveAll(
                item => item == null ||
                string.IsNullOrWhiteSpace(item.JobId) ||
                item.AmountCents < 0);

            HashSet<string> knownJobs =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            data.JobSalaries.RemoveAll(
                item => !knownJobs.Add(item.JobId.Trim()));

            foreach (JobSalary salary in data.JobSalaries)
                salary.JobId = salary.JobId.Trim();

            if (!data.CitizenPayAligned)
            {
                foreach (JobSalary salary in data.JobSalaries)
                {
                    if (string.Equals(salary.JobId, "citizen", StringComparison.OrdinalIgnoreCase) &&
                        salary.AmountCents == 5000)
                    {
                        salary.AmountCents = 2000;
                    }
                }

                data.CitizenPayAligned = true;
                migratedOnLoad = true;
            }
        }

        private bool SaveInternal()
        {
            string temporaryPath = filePath + ".tmp";
            string backupPath = filePath + ".bak";

            try
            {
                string json = JsonSerializer.Serialize(
                    data,
                    JsonOptions);

                File.WriteAllText(temporaryPath, json);

                if (File.Exists(filePath))
                {
                    try
                    {
                        File.Replace(
                            temporaryPath,
                            filePath,
                            backupPath,
                            true);
                    }
                    catch
                    {
                        File.Copy(filePath, backupPath, true);
                        File.Move(temporaryPath, filePath, true);
                    }
                }
                else
                {
                    File.Move(temporaryPath, filePath);
                }

                return true;
            }
            catch (Exception exception)
            {
                MelonLoader.MelonLogger.Error(
                    "[FusionDarkRP] Erreur sauvegarde configuration economie : "
                    + exception);

                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                    // Conserver l'erreur originale.
                }

                return false;
            }
        }

        private static EconomyConfigData Clone(
            EconomyConfigData source)
        {
            string json = JsonSerializer.Serialize(
                source,
                JsonOptions);

            return JsonSerializer.Deserialize<EconomyConfigData>(
                json,
                JsonOptions) ?? new EconomyConfigData();
        }
    }
}
