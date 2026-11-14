
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FusionDarkRP.Economy
{
    public sealed class WalletData
    {
        public string PlayerId { get; set; }
        public long CashCents { get; set; }
        public long BankCents { get; set; }
        public long Revision { get; set; }
    }

    public sealed class PhysicalCashData
    {
        public string CashId { get; set; }
        public string OwnerPlayerId { get; set; }
        public long AmountCents { get; set; }
        public bool Consumed { get; set; }
    }

    internal sealed class EconomySaveData
    {
        public int SchemaVersion { get; set; } = 1;
        public List<WalletData> Wallets { get; set; } = new List<WalletData>();
        public List<PhysicalCashData> PhysicalCash { get; set; } = new List<PhysicalCashData>();
        public List<string> ProcessedRequests { get; set; } = new List<string>();
    }

    public sealed class WalletService
    {
        private readonly object sync = new object();

        private readonly Dictionary<string, WalletData> wallets =
            new Dictionary<string, WalletData>(StringComparer.Ordinal);

        private readonly Dictionary<string, PhysicalCashData> physicalCash =
            new Dictionary<string, PhysicalCashData>(StringComparer.Ordinal);

        private readonly HashSet<string> processedRequests =
            new HashSet<string>(StringComparer.Ordinal);

        private readonly string savePath;
        private readonly long maxBalanceCents;

        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions
            {
                WriteIndented = true
            };

        public WalletService(string directory, long maxBalanceCents)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Dossier de sauvegarde invalide.");

            if (maxBalanceCents <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBalanceCents));

            this.maxBalanceCents = maxBalanceCents;

            Directory.CreateDirectory(directory);

            savePath = Path.Combine(
                directory,
                "fusiondarkrp-economy.json"
            );

            Load();
        }

        public WalletData GetWallet(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return null;

            lock (sync)
            {
                WalletData wallet;

                if (!wallets.TryGetValue(playerId, out wallet))
                    return null;

                return Copy(wallet);
            }
        }

        public WalletData GetOrCreateWallet(
            string playerId,
            long startingBankCents)
        {
            if (string.IsNullOrWhiteSpace(playerId) ||
                startingBankCents < 0)
                return null;

            lock (sync)
            {
                WalletData wallet;

                if (wallets.TryGetValue(playerId, out wallet))
                    return Copy(wallet);

                wallet = new WalletData
                {
                    PlayerId = playerId,
                    CashCents = 0,
                    BankCents = Math.Min(
                        startingBankCents,
                        maxBalanceCents
                    ),
                    Revision = 0
                };

                wallets.Add(playerId, wallet);

                try
                {
                    Save();
                    return Copy(wallet);
                }
                catch
                {
                    wallets.Remove(playerId);
                    throw;
                }
            }
        }

        public bool TryGiveCash(
            bool isAuthority,
            string requestId,
            string playerId,
            long amountCents,
            out string error)
        {
            lock (sync)
            {
                error = Validate(isAuthority, requestId, amountCents);

                if (error != null)
                    return false;

                if (string.IsNullOrWhiteSpace(playerId))
                {
                    error = "Identifiant joueur invalide.";
                    return false;
                }

                WalletData wallet = GetOrCreateUnsafe(playerId);

                if (!CanAdd(wallet.CashCents, amountCents))
                {
                    error = "Plafond liquide atteint.";
                    return false;
                }

                if (!MarkRequest(requestId))
                {
                    error = "Transaction déjà effectuée.";
                    return false;
                }

                wallet.CashCents += amountCents;
                wallet.Revision++;

                try
                {
                    Save();
                    return true;
                }
                catch (Exception ex)
                {
                    wallet.CashCents -= amountCents;
                    wallet.Revision--;
                    processedRequests.Remove(requestId);

                    error = "Impossible de sauvegarder la transaction : " + ex.Message;
                    return false;
                }
            }
        }

        public bool TryDeposit(
            bool isAuthority,
            string requestId,
            string playerId,
            long amountCents,
            out string error)
        {
            lock (sync)
            {
                error = Validate(isAuthority, requestId, amountCents);

                if (error != null)
                    return false;

                if (string.IsNullOrWhiteSpace(playerId))
                {
                    error = "Identifiant joueur invalide.";
                    return false;
                }

                WalletData wallet = GetOrCreateUnsafe(playerId);

                if (wallet.CashCents < amountCents)
                {
                    error = "Argent liquide insuffisant.";
                    return false;
                }

                if (!CanAdd(wallet.BankCents, amountCents))
                {
                    error = "Plafond bancaire atteint.";
                    return false;
                }

                if (!MarkRequest(requestId))
                {
                    error = "Transaction déjà effectuée.";
                    return false;
                }

                wallet.CashCents -= amountCents;
                wallet.BankCents += amountCents;
                wallet.Revision++;

                try
                {
                    Save();
                    return true;
                }
                catch (Exception ex)
                {
                    wallet.CashCents += amountCents;
                    wallet.BankCents -= amountCents;
                    wallet.Revision--;
                    processedRequests.Remove(requestId);

                    error = "Impossible de sauvegarder le dépôt : " + ex.Message;
                    return false;
                }
            }
        }

        public bool TryWithdraw(
            bool isAuthority,
            string requestId,
            string playerId,
            long amountCents,
            out string error)
        {
            lock (sync)
            {
                error = Validate(isAuthority, requestId, amountCents);

                if (error != null)
                    return false;

                if (string.IsNullOrWhiteSpace(playerId))
                {
                    error = "Identifiant joueur invalide.";
                    return false;
                }

                WalletData wallet = GetOrCreateUnsafe(playerId);

                if (wallet.BankCents < amountCents)
                {
                    error = "Solde bancaire insuffisant.";
                    return false;
                }

                if (!CanAdd(wallet.CashCents, amountCents))
                {
                    error = "Plafond du portefeuille atteint.";
                    return false;
                }

                if (!MarkRequest(requestId))
                {
                    error = "Transaction déjà effectuée.";
                    return false;
                }

                wallet.BankCents -= amountCents;
                wallet.CashCents += amountCents;
                wallet.Revision++;

                try
                {
                    Save();
                    return true;
                }
                catch (Exception ex)
                {
                    wallet.BankCents += amountCents;
                    wallet.CashCents -= amountCents;
                    wallet.Revision--;
                    processedRequests.Remove(requestId);

                    error = "Impossible de sauvegarder le retrait : " + ex.Message;
                    return false;
                }
            }
        }

        public bool TryTransfer(
            bool isAuthority,
            string requestId,
            string fromPlayerId,
            string toPlayerId,
            long amountCents,
            out string error)
        {
            lock (sync)
            {
                error = Validate(isAuthority, requestId, amountCents);

                if (error != null)
                    return false;

                if (string.IsNullOrWhiteSpace(fromPlayerId) ||
                    string.IsNullOrWhiteSpace(toPlayerId) ||
                    string.Equals(fromPlayerId, toPlayerId, StringComparison.Ordinal))
                {
                    error = "Destinataire invalide.";
                    return false;
                }

                WalletData from = GetOrCreateUnsafe(fromPlayerId);
                WalletData to = GetOrCreateUnsafe(toPlayerId);

                if (from.BankCents < amountCents)
                {
                    error = "Solde bancaire insuffisant.";
                    return false;
                }

                if (!CanAdd(to.BankCents, amountCents))
                {
                    error = "Plafond du destinataire atteint.";
                    return false;
                }

                if (!MarkRequest(requestId))
                {
                    error = "Virement déjà effectué.";
                    return false;
                }

                from.BankCents -= amountCents;
                to.BankCents += amountCents;

                from.Revision++;
                to.Revision++;

                try
                {
                    Save();
                    return true;
                }
                catch (Exception ex)
                {
                    from.BankCents += amountCents;
                    to.BankCents -= amountCents;

                    from.Revision--;
                    to.Revision--;

                    processedRequests.Remove(requestId);

                    error = "Impossible de sauvegarder le virement : " + ex.Message;
                    return false;
                }
            }
        }

        /// <summary>
        /// Déplace des espèces du joueur vers le solde bancaire d'un
        /// portefeuille système (coffre). Atomique, idempotent par requestId.
        /// </summary>
        public bool TryMoveCashToWalletBank(
            bool isAuthority,
            string requestId,
            string fromPlayerId,
            string toWalletId,
            long amountCents,
            out string error)
        {
            lock (sync)
            {
                error = Validate(isAuthority, requestId, amountCents);

                if (error != null)
                    return false;

                if (string.IsNullOrWhiteSpace(fromPlayerId) ||
                    string.IsNullOrWhiteSpace(toWalletId) ||
                    string.Equals(
                        fromPlayerId, toWalletId, StringComparison.Ordinal))
                {
                    error = "Compte invalide.";
                    return false;
                }

                WalletData from = GetOrCreateUnsafe(fromPlayerId);
                WalletData to = GetOrCreateUnsafe(toWalletId);

                if (from.CashCents < amountCents)
                {
                    error = "Espèces insuffisantes.";
                    return false;
                }

                if (!CanAdd(to.BankCents, amountCents))
                {
                    error = "Capacité du coffre atteinte.";
                    return false;
                }

                if (!MarkRequest(requestId))
                {
                    error = "Opération déjà effectuée.";
                    return false;
                }

                from.CashCents -= amountCents;
                to.BankCents += amountCents;

                from.Revision++;
                to.Revision++;

                try
                {
                    Save();
                    return true;
                }
                catch (Exception ex)
                {
                    from.CashCents += amountCents;
                    to.BankCents -= amountCents;

                    from.Revision--;
                    to.Revision--;

                    processedRequests.Remove(requestId);

                    error = "Sauvegarde impossible : " + ex.Message;
                    return false;
                }
            }
        }

        /// <summary>
        /// Déplace le solde bancaire d'un portefeuille système (coffre)
        /// vers les espèces d'un joueur. Atomique, idempotent par requestId.
        /// </summary>
        public bool TryMoveBankToWalletCash(
            bool isAuthority,
            string requestId,
            string fromWalletId,
            string toPlayerId,
            long amountCents,
            out string error)
        {
            lock (sync)
            {
                error = Validate(isAuthority, requestId, amountCents);

                if (error != null)
                    return false;

                if (string.IsNullOrWhiteSpace(fromWalletId) ||
                    string.IsNullOrWhiteSpace(toPlayerId) ||
                    string.Equals(
                        fromWalletId, toPlayerId, StringComparison.Ordinal))
                {
                    error = "Compte invalide.";
                    return false;
                }

                WalletData from = GetOrCreateUnsafe(fromWalletId);
                WalletData to = GetOrCreateUnsafe(toPlayerId);

                if (from.BankCents < amountCents)
                {
                    error = "Le coffre ne contient pas assez d'argent.";
                    return false;
                }

                if (!CanAdd(to.CashCents, amountCents))
                {
                    error = "Plafond d'espèces du joueur atteint.";
                    return false;
                }

                if (!MarkRequest(requestId))
                {
                    error = "Opération déjà effectuée.";
                    return false;
                }

                from.BankCents -= amountCents;
                to.CashCents += amountCents;

                from.Revision++;
                to.Revision++;

                try
                {
                    Save();
                    return true;
                }
                catch (Exception ex)
                {
                    from.BankCents += amountCents;
                    to.CashCents -= amountCents;

                    from.Revision--;
                    to.Revision--;

                    processedRequests.Remove(requestId);

                    error = "Sauvegarde impossible : " + ex.Message;
                    return false;
                }
            }
        }

        public PhysicalCashData TryCreatePhysicalCash(
            bool isAuthority,
            string requestId,
            string playerId,
            long amountCents,
            out string error)
        {
            lock (sync)
            {
                error = null;

                if (!isAuthority)
                {
                    error = "Action réservée à l'hôte.";
                    return null;
                }

                if (string.IsNullOrWhiteSpace(requestId) ||
                    string.IsNullOrWhiteSpace(playerId) ||
                    amountCents <= 0 ||
                    amountCents > maxBalanceCents)
                {
                    error = "Demande de création d'argent invalide.";
                    return null;
                }

                if (processedRequests.Contains(requestId))
                {
                    error = "Demande déjà traitée.";
                    return null;
                }

                WalletData wallet = GetOrCreateUnsafe(playerId);

                if (wallet.CashCents < amountCents)
                {
                    error = "Argent liquide insuffisant.";
                    return null;
                }

                PhysicalCashData cash = new PhysicalCashData
                {
                    CashId = Guid.NewGuid().ToString("N"),
                    OwnerPlayerId = playerId,
                    AmountCents = amountCents,
                    Consumed = false
                };

                wallet.CashCents -= amountCents;
                wallet.Revision++;

                physicalCash.Add(cash.CashId, cash);

                if (!MarkRequest(requestId))
                {
                    wallet.CashCents += amountCents;
                    wallet.Revision--;
                    physicalCash.Remove(cash.CashId);

                    error = "Demande déjà traitée.";
                    return null;
                }

                try
                {
                    Save();
                    return Copy(cash);
                }
                catch (Exception ex)
                {
                    wallet.CashCents += amountCents;
                    wallet.Revision--;
                    physicalCash.Remove(cash.CashId);
                    processedRequests.Remove(requestId);

                    error = "Impossible de sauvegarder le billet : " + ex.Message;
                    return null;
                }
            }
        }

        // Ancienne méthode conservée pour compatibilité.
        // Elle enregistre un objet sans débiter le portefeuille.
        public PhysicalCashData RegisterPhysicalCash(
            bool isAuthority,
            string playerId,
            long amountCents,
            out string error)
        {
            lock (sync)
            {
                error = null;

                if (!isAuthority)
                {
                    error = "Action réservée à l'hôte.";
                    return null;
                }

                if (string.IsNullOrWhiteSpace(playerId) ||
                    amountCents <= 0 ||
                    amountCents > maxBalanceCents)
                {
                    error = "Billet invalide.";
                    return null;
                }

                PhysicalCashData cash = new PhysicalCashData
                {
                    CashId = Guid.NewGuid().ToString("N"),
                    OwnerPlayerId = playerId,
                    AmountCents = amountCents,
                    Consumed = false
                };

                physicalCash.Add(cash.CashId, cash);

                try
                {
                    Save();
                    return Copy(cash);
                }
                catch
                {
                    physicalCash.Remove(cash.CashId);
                    throw;
                }
            }
        }

        public bool TryRedeemPhysicalCash(
            bool isAuthority,
            string requestId,
            string playerId,
            string cashId,
            out string error)
        {
            lock (sync)
            {
                error = null;

                if (!isAuthority)
                {
                    error = "Action réservée à l'hôte.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(requestId) ||
                    string.IsNullOrWhiteSpace(playerId) ||
                    string.IsNullOrWhiteSpace(cashId))
                {
                    error = "Données invalides.";
                    return false;
                }

                if (processedRequests.Contains(requestId))
                {
                    error = "Demande déjà traitée.";
                    return false;
                }

                PhysicalCashData cash;

                if (!physicalCash.TryGetValue(cashId, out cash) ||
                    cash.Consumed ||
                    cash.OwnerPlayerId != playerId ||
                    cash.AmountCents <= 0 ||
                    cash.AmountCents > maxBalanceCents)
                {
                    error = "Billet invalide ou déjà utilisé.";
                    return false;
                }

                WalletData wallet = GetOrCreateUnsafe(playerId);

                if (!CanAdd(wallet.CashCents, cash.AmountCents))
                {
                    error = "Plafond du portefeuille atteint.";
                    return false;
                }

                cash.Consumed = true;

                wallet.CashCents += cash.AmountCents;
                wallet.Revision++;

                if (!MarkRequest(requestId))
                {
                    cash.Consumed = false;
                    wallet.CashCents -= cash.AmountCents;
                    wallet.Revision--;

                    error = "Demande déjà traitée.";
                    return false;
                }

                try
                {
                    Save();
                    return true;
                }
                catch (Exception ex)
                {
                    cash.Consumed = false;
                    wallet.CashCents -= cash.AmountCents;
                    wallet.Revision--;
                    processedRequests.Remove(requestId);

                    error = "Impossible de sauvegarder l'encaissement : " + ex.Message;
                    return false;
                }
            }
        }

        // Dépôt atomique d'un billet physique directement sur le compte bancaire.
        // Le billet est consommé et le compte crédité dans la même transaction sauvegardée.
        public bool TryDepositPhysicalCashToBank(
            bool isAuthority,
            string requestId,
            string playerId,
            string cashId,
            out string error)
        {
            lock (sync)
            {
                error = null;

                if (!isAuthority)
                {
                    error = "Action réservée à l'hôte.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(requestId) ||
                    string.IsNullOrWhiteSpace(playerId) ||
                    string.IsNullOrWhiteSpace(cashId))
                {
                    error = "Données de dépôt invalides.";
                    return false;
                }

                if (processedRequests.Contains(requestId))
                {
                    error = "Dépôt déjà traité.";
                    return false;
                }

                PhysicalCashData cash;

                if (!physicalCash.TryGetValue(cashId, out cash) ||
                    cash == null ||
                    cash.Consumed ||
                    !string.Equals(
                        cash.OwnerPlayerId,
                        playerId,
                        StringComparison.Ordinal) ||
                    cash.AmountCents <= 0 ||
                    cash.AmountCents > maxBalanceCents)
                {
                    error = "Billet invalide, déjà utilisé ou ne vous appartenant pas.";
                    return false;
                }

                WalletData wallet = GetOrCreateUnsafe(playerId);

                if (!CanAdd(wallet.BankCents, cash.AmountCents))
                {
                    error = "Plafond bancaire atteint.";
                    return false;
                }

                if (!MarkRequest(requestId))
                {
                    error = "Dépôt déjà traité.";
                    return false;
                }

                cash.Consumed = true;
                wallet.BankCents += cash.AmountCents;
                wallet.Revision++;

                try
                {
                    Save();
                    return true;
                }
                catch (Exception ex)
                {
                    cash.Consumed = false;
                    wallet.BankCents -= cash.AmountCents;
                    wallet.Revision--;
                    processedRequests.Remove(requestId);

                    error = "Impossible de sauvegarder le dépôt bancaire : " + ex.Message;
                    return false;
                }
            }
        }

        // Transfert persistant d'un billet physique entre deux joueurs.
        // Le billet reste un objet physique : aucun solde n'est crédité ici.
        public bool TryTransferPhysicalCash(
            bool isAuthority,
            string requestId,
            string fromPlayerId,
            string toPlayerId,
            string cashId,
            out string error)
        {
            lock (sync)
            {
                error = null;

                if (!isAuthority)
                {
                    error = "Action réservée à l'hôte.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(requestId) ||
                    string.IsNullOrWhiteSpace(fromPlayerId) ||
                    string.IsNullOrWhiteSpace(toPlayerId) ||
                    string.IsNullOrWhiteSpace(cashId))
                {
                    error = "Données de transfert invalides.";
                    return false;
                }

                if (string.Equals(
                    fromPlayerId,
                    toPlayerId,
                    StringComparison.Ordinal))
                {
                    error = "Impossible de transférer un billet à soi-même.";
                    return false;
                }

                if (processedRequests.Contains(requestId))
                {
                    error = "Demande de transfert déjà traitée.";
                    return false;
                }

                PhysicalCashData cash;

                if (!physicalCash.TryGetValue(cashId, out cash) ||
                    cash == null ||
                    cash.Consumed ||
                    cash.AmountCents <= 0 ||
                    cash.AmountCents > maxBalanceCents)
                {
                    error = "Billet introuvable, invalide ou déjà utilisé.";
                    return false;
                }

                if (!string.Equals(
                    cash.OwnerPlayerId,
                    fromPlayerId,
                    StringComparison.Ordinal))
                {
                    error = "Ce billet ne vous appartient pas.";
                    return false;
                }

                string previousOwner = cash.OwnerPlayerId;

                if (!MarkRequest(requestId))
                {
                    error = "Demande de transfert déjà traitée.";
                    return false;
                }

                cash.OwnerPlayerId = toPlayerId;

                try
                {
                    Save();
                    return true;
                }
                catch (Exception ex)
                {
                    cash.OwnerPlayerId = previousOwner;
                    processedRequests.Remove(requestId);

                    error = "Impossible de sauvegarder le transfert : " + ex.Message;
                    return false;
                }
            }
        }

        public void Save()
        {
            lock (sync)
            {
                EconomySaveData data = new EconomySaveData
                {
                    SchemaVersion = 1,
                    Wallets = wallets.Values.Select(Copy).ToList(),
                    PhysicalCash = physicalCash.Values.Select(Copy).ToList(),
                    ProcessedRequests = processedRequests.ToList()
                };

                string json = JsonSerializer.Serialize(data, JsonOptions);

                string temp = savePath + ".tmp";
                string backup = savePath + ".bak";

                File.WriteAllText(temp, json);

                if (!File.Exists(savePath))
                {
                    File.Move(temp, savePath);
                    return;
                }

                try
                {
                    File.Replace(temp, savePath, backup, true);
                }
                catch
                {
                    File.Copy(savePath, backup, true);

                    if (File.Exists(savePath))
                        File.Delete(savePath);

                    File.Move(temp, savePath);
                }
            }
        }

        private void Load()
        {
            lock (sync)
            {
                string backup = savePath + ".bak";

                if (!File.Exists(savePath))
                {
                    if (!File.Exists(backup))
                        return;

                    RestoreBackup(backup);
                    return;
                }

                try
                {
                    LoadFromFile(savePath);
                    return;
                }
                catch (Exception primaryError)
                {
                    wallets.Clear();
                    physicalCash.Clear();
                    processedRequests.Clear();

                    if (!File.Exists(backup))
                    {
                        throw new InvalidDataException(
                            "La sauvegarde économique principale est invalide et aucune sauvegarde de secours n'existe. " +
                            "Le système refuse de démarrer avec des soldes vides.",
                            primaryError
                        );
                    }

                    try
                    {
                        RestoreBackup(backup);
                    }
                    catch (Exception backupError)
                    {
                        wallets.Clear();
                        physicalCash.Clear();
                        processedRequests.Clear();

                        throw new InvalidDataException(
                            "La sauvegarde principale et la sauvegarde de secours sont invalides. " +
                            "Le système refuse de démarrer afin de protéger les données économiques.",
                            new AggregateException(primaryError, backupError)
                        );
                    }
                }
            }
        }

        private void RestoreBackup(string backup)
        {
            LoadFromFile(backup);

            string restoreTemp = savePath + ".restore.tmp";

            try
            {
                File.Copy(backup, restoreTemp, true);

                if (File.Exists(savePath))
                    File.Delete(savePath);

                File.Move(restoreTemp, savePath);
            }
            catch
            {
                if (File.Exists(restoreTemp))
                {
                    try
                    {
                        File.Delete(restoreTemp);
                    }
                    catch
                    {
                        // Ne pas masquer l'erreur initiale.
                    }
                }

                throw;
            }
        }

        private void LoadFromFile(string path)
        {
            EconomySaveData data =
                JsonSerializer.Deserialize<EconomySaveData>(
                    File.ReadAllText(path),
                    JsonOptions
                );

            if (data == null || data.SchemaVersion != 1)
            {
                throw new InvalidDataException(
                    "Version de sauvegarde économique invalide."
                );
            }

            if (data.Wallets == null ||
                data.PhysicalCash == null ||
                data.ProcessedRequests == null)
            {
                throw new InvalidDataException(
                    "La sauvegarde économique contient une collection manquante."
                );
            }

            Dictionary<string, WalletData> loadedWallets =
                new Dictionary<string, WalletData>(StringComparer.Ordinal);

            Dictionary<string, PhysicalCashData> loadedCash =
                new Dictionary<string, PhysicalCashData>(StringComparer.Ordinal);

            HashSet<string> loadedRequests =
                new HashSet<string>(StringComparer.Ordinal);

            foreach (WalletData wallet in data.Wallets)
            {
                if (wallet == null ||
                    string.IsNullOrWhiteSpace(wallet.PlayerId) ||
                    wallet.CashCents < 0 ||
                    wallet.BankCents < 0 ||
                    wallet.CashCents > maxBalanceCents ||
                    wallet.BankCents > maxBalanceCents ||
                    wallet.Revision < 0)
                {
                    throw new InvalidDataException(
                        "Un portefeuille de la sauvegarde est invalide."
                    );
                }

                if (loadedWallets.ContainsKey(wallet.PlayerId))
                {
                    throw new InvalidDataException(
                        "Identifiant joueur en double dans la sauvegarde."
                    );
                }

                loadedWallets.Add(
                    wallet.PlayerId,
                    Copy(wallet)
                );
            }

            foreach (PhysicalCashData cash in data.PhysicalCash)
            {
                if (cash == null ||
                    string.IsNullOrWhiteSpace(cash.CashId) ||
                    string.IsNullOrWhiteSpace(cash.OwnerPlayerId) ||
                    cash.AmountCents <= 0 ||
                    cash.AmountCents > maxBalanceCents)
                {
                    throw new InvalidDataException(
                        "Un billet physique de la sauvegarde est invalide."
                    );
                }

                if (loadedCash.ContainsKey(cash.CashId))
                {
                    throw new InvalidDataException(
                        "Identifiant de billet en double dans la sauvegarde."
                    );
                }

                loadedCash.Add(
                    cash.CashId,
                    Copy(cash)
                );
            }

            foreach (string id in data.ProcessedRequests)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    throw new InvalidDataException(
                        "Un identifiant de transaction sauvegardé est invalide."
                    );
                }

                loadedRequests.Add(id);
            }

            wallets.Clear();
            physicalCash.Clear();
            processedRequests.Clear();

            foreach (KeyValuePair<string, WalletData> entry in loadedWallets)
                wallets.Add(entry.Key, entry.Value);

            foreach (KeyValuePair<string, PhysicalCashData> entry in loadedCash)
                physicalCash.Add(entry.Key, entry.Value);

            foreach (string id in loadedRequests)
                processedRequests.Add(id);
        }

        private string Validate(
            bool isAuthority,
            string requestId,
            long amount)
        {
            if (!isAuthority)
                return "Action réservée à l'hôte.";

            if (string.IsNullOrWhiteSpace(requestId))
                return "Identifiant de transaction manquant.";

            if (amount <= 0 || amount > maxBalanceCents)
                return "Montant invalide.";

            if (processedRequests.Contains(requestId))
                return "Demande déjà traitée.";

            return null;
        }

        private bool MarkRequest(string requestId)
        {
            return processedRequests.Add(requestId);
        }

        private bool CanAdd(long current, long amount)
        {
            return amount > 0 &&
                   amount <= maxBalanceCents &&
                   current >= 0 &&
                   current <= maxBalanceCents - amount;
        }

        private WalletData GetOrCreateUnsafe(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                throw new ArgumentException(
                    "Identifiant joueur invalide."
                );
            }

            WalletData wallet;

            if (!wallets.TryGetValue(playerId, out wallet))
            {
                wallet = new WalletData
                {
                    PlayerId = playerId,
                    CashCents = 0,
                    BankCents = 0,
                    Revision = 0
                };

                wallets.Add(playerId, wallet);
            }

            return wallet;
        }

        private static WalletData Copy(WalletData x)
        {
            return new WalletData
            {
                PlayerId = x.PlayerId,
                CashCents = x.CashCents,
                BankCents = x.BankCents,
                Revision = x.Revision
            };
        }

        private static PhysicalCashData Copy(PhysicalCashData x)
        {
            return new PhysicalCashData
            {
                CashId = x.CashId,
                OwnerPlayerId = x.OwnerPlayerId,
                AmountCents = x.AmountCents,
                Consumed = x.Consumed
            };
        }
    }
}
