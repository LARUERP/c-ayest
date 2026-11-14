using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FusionDarkRP.Economy
{
    public sealed class SalaryService
    {
        private readonly object sync = new object();
        private readonly WalletService walletService;
        private readonly EconomyConfig config;
        private readonly string schedulePath;

        // Les échéances sont conservées entre les reconnexions et redémarrages.
        private readonly Dictionary<string, DateTime> nextPayment =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);

        private readonly HashSet<string> paymentsInProgress =
            new HashSet<string>(StringComparer.Ordinal);

        private sealed class SalaryScheduleData
        {
            public Dictionary<string, long> NextPaymentUtcTicks { get; set; } =
                new Dictionary<string, long>(StringComparer.Ordinal);
        }

        public SalaryService(
            WalletService walletService,
            EconomyConfig config)
            : this(walletService, config, null)
        {
        }

        public SalaryService(
            WalletService walletService,
            EconomyConfig config,
            string schedulePath)
        {
            this.walletService = walletService
                ?? throw new ArgumentNullException(nameof(walletService));
            this.config = config
                ?? throw new ArgumentNullException(nameof(config));
            this.schedulePath = schedulePath;

            LoadSchedule();
        }

        public bool RegisterPlayer(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return false;

            lock (sync)
            {
                if (nextPayment.ContainsKey(playerId))
                    return false;

                nextPayment[playerId] =
                    DateTime.UtcNow.AddMinutes(GetInterval());

                SaveScheduleLocked();
                return true;
            }
        }

        public void UnregisterPlayer(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return;

            // On conserve l'échéance : quitter le lobby ne doit pas réinitialiser
            // le délai de salaire et permettre de contourner le rythme prévu.
            lock (sync)
            {
                paymentsInProgress.Remove(playerId);
                SaveScheduleLocked();
            }
        }

        public bool TryPayDueSalary(
            string playerId,
            string jobId,
            bool isHost,
            out long amountPaidCents)
        {
            return TryPayDueSalary(
                playerId,
                jobId,
                0,
                isHost,
                out amountPaidCents);
        }

        public bool TryPayDueSalary(
            string playerId,
            string jobId,
            int gradeIndex,
            bool isHost,
            out long amountPaidCents)
        {
            amountPaidCents = 0;

            if (!isHost || string.IsNullOrWhiteSpace(playerId))
                return false;

            DateTime paymentTime;

            lock (sync)
            {
                if (paymentsInProgress.Contains(playerId))
                    return false;

                if (!nextPayment.TryGetValue(playerId, out paymentTime))
                {
                    nextPayment[playerId] =
                        DateTime.UtcNow.AddMinutes(GetInterval());
                    SaveScheduleLocked();
                    return false;
                }

                if (DateTime.UtcNow < paymentTime)
                    return false;

                paymentsInProgress.Add(playerId);
            }

            try
            {
                long salary = config.GetSalary(jobId, gradeIndex);

                if (salary <= 0)
                {
                    ScheduleNextPayment(playerId);
                    return false;
                }

                // L'identifiant stable rend le versement idempotent côté portefeuille.
                string requestId =
                    "salary:" + playerId + ":" + paymentTime.Ticks.ToString();

                bool paid = walletService.TryGiveCash(
                    true,
                    requestId,
                    playerId,
                    salary,
                    out string error);

                if (!paid)
                {
                    MelonLoader.MelonLogger.Warning(
                        "[FusionDarkRP] Salaire non verse a "
                        + playerId + " : " + error);
                    ScheduleNextPayment(playerId);
                    return false;
                }

                amountPaidCents = salary;
                ScheduleNextPayment(playerId);
                return true;
            }
            catch (Exception exception)
            {
                MelonLoader.MelonLogger.Error(
                    "[FusionDarkRP] Erreur paiement salaire : " + exception);
                ScheduleNextPayment(playerId);
                return false;
            }
            finally
            {
                lock (sync)
                {
                    paymentsInProgress.Remove(playerId);
                }
            }
        }

        public void ResetPlayerTimer(string playerId)
        {
            if (!string.IsNullOrWhiteSpace(playerId))
                ScheduleNextPayment(playerId);
        }

        public void ResetAllTimers()
        {
            lock (sync)
            {
                nextPayment.Clear();
                SaveScheduleLocked();
            }
        }

        private int GetInterval()
        {
            int interval = config.GetSnapshot().SalaryIntervalMinutes;
            return Math.Max(1, Math.Min(interval, 1440));
        }

        private void ScheduleNextPayment(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return;

            lock (sync)
            {
                nextPayment[playerId] =
                    DateTime.UtcNow.AddMinutes(GetInterval());
                SaveScheduleLocked();
            }
        }

        private void LoadSchedule()
        {
            if (string.IsNullOrWhiteSpace(schedulePath) ||
                !File.Exists(schedulePath))
                return;

            try
            {
                string json = File.ReadAllText(schedulePath);
                SalaryScheduleData data =
                    JsonSerializer.Deserialize<SalaryScheduleData>(json);

                if (data?.NextPaymentUtcTicks == null)
                    return;

                lock (sync)
                {
                    foreach (KeyValuePair<string, long> entry
                        in data.NextPaymentUtcTicks)
                    {
                        if (string.IsNullOrWhiteSpace(entry.Key) ||
                            entry.Value <= DateTime.MinValue.Ticks ||
                            entry.Value > DateTime.MaxValue.Ticks)
                            continue;

                        nextPayment[entry.Key] =
                            new DateTime(entry.Value, DateTimeKind.Utc);
                    }
                }
            }
            catch (Exception exception)
            {
                MelonLoader.MelonLogger.Warning(
                    "[FusionDarkRP] Planning des salaires illisible, "
                    + "un nouveau planning sera créé : " + exception.Message);
            }
        }

        // Appelé uniquement avec sync verrouillé.
        private void SaveScheduleLocked()
        {
            if (string.IsNullOrWhiteSpace(schedulePath))
                return;

            string temporaryPath = schedulePath + ".tmp";

            try
            {
                string directory = Path.GetDirectoryName(schedulePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                SalaryScheduleData data = new SalaryScheduleData();

                foreach (KeyValuePair<string, DateTime> entry in nextPayment)
                    data.NextPaymentUtcTicks[entry.Key] = entry.Value.ToUniversalTime().Ticks;

                string json = JsonSerializer.Serialize(data);
                File.WriteAllText(temporaryPath, json);

                File.Move(temporaryPath, schedulePath, true);
            }
            catch (Exception exception)
            {
                MelonLoader.MelonLogger.Warning(
                    "[FusionDarkRP] Sauvegarde du planning des salaires "
                    + "impossible : " + exception.Message);

                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                    // Ne jamais interrompre le serveur pour un fichier temporaire.
                }
            }
        }
    }
}
