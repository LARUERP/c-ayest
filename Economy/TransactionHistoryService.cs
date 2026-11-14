
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FusionDarkRP.Economy
{
    public sealed class BankTransaction
    {
        public string TransactionId { get; set; } = "";
        public string RequestId { get; set; } = "";
        public string PlayerId { get; set; } = "";
        public string OtherPlayerId { get; set; } = "";
        public string Type { get; set; } = "";
        public long AmountCents { get; set; }
        public long TimestampUtcTicks { get; set; }
        public string Description { get; set; } = "";
    }

    internal sealed class TransactionHistorySaveData
    {
        public int SchemaVersion { get; set; } = 1;

        public List<BankTransaction> Transactions { get; set; } =
            new List<BankTransaction>();
    }

    public sealed class TransactionHistoryService
    {
        private readonly object sync = new object();
        private readonly string savePath;

        private readonly List<BankTransaction> transactions =
            new List<BankTransaction>();

        private readonly HashSet<string> requestIds =
            new HashSet<string>(StringComparer.Ordinal);

        private const int MaximumTransactions = 10000;

        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions
            {
                WriteIndented = true
            };

        public TransactionHistoryService(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException(
                    "Dossier d'historique invalide.");

            Directory.CreateDirectory(directory);

            savePath = Path.Combine(
                directory,
                "bank-transactions.json");

            Load();
        }

        public bool Record(
            bool isAuthority,
            string requestId,
            string playerId,
            string otherPlayerId,
            string type,
            long amountCents,
            string description,
            out string error)
        {
            error = null;

            if (!isAuthority)
            {
                error = "Action réservée à l'hôte.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(requestId) ||
                string.IsNullOrWhiteSpace(playerId) ||
                string.IsNullOrWhiteSpace(type) ||
                amountCents <= 0)
            {
                error = "Informations de transaction invalides.";
                return false;
            }

            lock (sync)
            {
                if (requestIds.Contains(requestId))
                {
                    error = "Transaction déjà enregistrée.";
                    return false;
                }

                BankTransaction transaction =
                    new BankTransaction
                    {
                        TransactionId = Guid.NewGuid().ToString("N"),
                        RequestId = requestId,
                        PlayerId = playerId,
                        OtherPlayerId = otherPlayerId ?? "",
                        Type = type,
                        AmountCents = amountCents,
                        TimestampUtcTicks = DateTime.UtcNow.Ticks,
                        Description = description ?? ""
                    };

                transactions.Add(transaction);
                requestIds.Add(requestId);

                List<BankTransaction> removedTransactions =
                    new List<BankTransaction>();

                while (transactions.Count > MaximumTransactions)
                {
                    BankTransaction oldest = transactions[0];

                    transactions.RemoveAt(0);
                    requestIds.Remove(oldest.RequestId);
                    removedTransactions.Add(oldest);
                }

                if (!SaveInternal())
                {
                    transactions.Remove(transaction);
                    requestIds.Remove(requestId);

                    foreach (BankTransaction removed in removedTransactions)
                    {
                        transactions.Add(removed);
                        requestIds.Add(removed.RequestId);
                    }

                    transactions.Sort(
                        (a, b) => a.TimestampUtcTicks.CompareTo(
                            b.TimestampUtcTicks));

                    error = "Impossible d'enregistrer l'historique sur le disque.";
                    return false;
                }

                return true;
            }
        }

        public List<BankTransaction> GetPlayerHistory(
            string playerId,
            int maximumResults = 50)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return new List<BankTransaction>();

            maximumResults = Math.Max(
                1,
                Math.Min(maximumResults, 500));

            lock (sync)
            {
                return transactions
                    .Where(t =>
                        t.PlayerId == playerId ||
                        t.OtherPlayerId == playerId)
                    .OrderByDescending(
                        t => t.TimestampUtcTicks)
                    .Take(maximumResults)
                    .Select(Copy)
                    .ToList();
            }
        }

        public List<BankTransaction> GetAllHistory(
            bool isAuthority,
            int maximumResults = 100)
        {
            if (!isAuthority)
                return new List<BankTransaction>();

            maximumResults = Math.Max(
                1,
                Math.Min(maximumResults, 1000));

            lock (sync)
            {
                return transactions
                    .OrderByDescending(
                        t => t.TimestampUtcTicks)
                    .Take(maximumResults)
                    .Select(Copy)
                    .ToList();
            }
        }

        public void Save()
        {
            lock (sync)
            {
                if (!SaveInternal())
                {
                    MelonLoader.MelonLogger.Error(
                        "[FusionDarkRP] Impossible de sauvegarder l'historique bancaire.");
                }
            }
        }

        private void Load()
        {
            lock (sync)
            {
                if (File.Exists(savePath))
                {
                    try
                    {
                        LoadFromFile(savePath);
                        return;
                    }
                    catch (Exception exception)
                    {
                        MelonLoader.MelonLogger.Warning(
                            "[FusionDarkRP] Sauvegarde principale de l'historique invalide : "
                            + exception.Message);

                        transactions.Clear();
                        requestIds.Clear();
                    }
                }

                string backupPath = savePath + ".bak";

                if (!File.Exists(backupPath))
                    return;

                try
                {
                    LoadFromFile(backupPath);

                    MelonLoader.MelonLogger.Warning(
                        "[FusionDarkRP] Historique bancaire restauré depuis la sauvegarde.");

                    SaveInternal();
                }
                catch (Exception exception)
                {
                    transactions.Clear();
                    requestIds.Clear();

                    MelonLoader.MelonLogger.Error(
                        "[FusionDarkRP] Impossible de restaurer l'historique bancaire : "
                        + exception);
                }
            }
        }

        private void LoadFromFile(string path)
        {
            TransactionHistorySaveData data =
                JsonSerializer.Deserialize<TransactionHistorySaveData>(
                    File.ReadAllText(path),
                    JsonOptions);

            if (data == null || data.SchemaVersion != 1)
                throw new InvalidDataException(
                    "Version d'historique invalide.");

            transactions.Clear();
            requestIds.Clear();

            foreach (BankTransaction transaction in
                data.Transactions ?? new List<BankTransaction>())
            {
                if (transaction == null ||
                    string.IsNullOrWhiteSpace(transaction.RequestId) ||
                    string.IsNullOrWhiteSpace(transaction.PlayerId) ||
                    string.IsNullOrWhiteSpace(transaction.Type) ||
                    transaction.AmountCents <= 0 ||
                    transaction.TimestampUtcTicks <= 0)
                    continue;

                if (!requestIds.Add(transaction.RequestId))
                    continue;

                transactions.Add(Copy(transaction));
            }

            transactions.Sort(
                (a, b) => a.TimestampUtcTicks.CompareTo(
                    b.TimestampUtcTicks));

            while (transactions.Count > MaximumTransactions)
            {
                requestIds.Remove(transactions[0].RequestId);
                transactions.RemoveAt(0);
            }
        }

        private bool SaveInternal()
        {
            string temporaryPath = savePath + ".tmp";
            string backupPath = savePath + ".bak";

            try
            {
                TransactionHistorySaveData data =
                    new TransactionHistorySaveData
                    {
                        Transactions = transactions
                            .Select(Copy)
                            .ToList()
                    };

                string json = JsonSerializer.Serialize(
                    data,
                    JsonOptions);

                File.WriteAllText(temporaryPath, json);

                if (File.Exists(savePath))
                {
                    try
                    {
                        File.Replace(
                            temporaryPath,
                            savePath,
                            backupPath,
                            true);
                    }
                    catch
                    {
                        // Créer la sauvegarde avant de remplacer
                        // le fichier principal.
                        File.Copy(savePath, backupPath, true);
                        File.Move(temporaryPath, savePath, true);
                    }
                }
                else
                {
                    File.Move(temporaryPath, savePath);
                }

                return true;
            }
            catch (Exception exception)
            {
                MelonLoader.MelonLogger.Error(
                    "[FusionDarkRP] Erreur historique bancaire : "
                    + exception);

                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                    // Ne pas masquer l'erreur principale.
                }

                return false;
            }
        }

        private static BankTransaction Copy(
            BankTransaction source)
        {
            return new BankTransaction
            {
                TransactionId = source.TransactionId,
                RequestId = source.RequestId,
                PlayerId = source.PlayerId,
                OtherPlayerId = source.OtherPlayerId,
                Type = source.Type,
                AmountCents = source.AmountCents,
                TimestampUtcTicks = source.TimestampUtcTicks,
                Description = source.Description
            };
        }
    }
}
