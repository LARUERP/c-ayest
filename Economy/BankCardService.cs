using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FusionDarkRP.Economy
{
    public sealed class BankCardData
    {
        public string CardId { get; set; }
        public string OwnerPlayerId { get; set; }
        public string IssuedByPlayerId { get; set; }
        public long CreatedAtUtcTicks { get; set; }
        public bool Revoked { get; set; }
    }

    internal sealed class BankCardSaveData
    {
        public int SchemaVersion { get; set; } = 1;

        public List<BankCardData> Cards { get; set; } =
            new List<BankCardData>();
    }

    /// <summary>
    /// Registre persistant des cartes bancaires physiques.
    ///
    /// Une carte est identifiée par l'identifiant unique de son
    /// objet physique synchronisé entre les joueurs.
    ///
    /// Toutes les modifications doivent être effectuées par l'hôte.
    /// </summary>
    public sealed class BankCardService
    {
        private readonly object sync = new object();

        private readonly Dictionary<string, BankCardData> cards =
            new Dictionary<string, BankCardData>(
                StringComparer.Ordinal);

        private readonly string savePath;

        private const int MaximumCards = 10000;

        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions
            {
                WriteIndented = true
            };

        public BankCardService(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException(
                    "Dossier des cartes bancaires invalide.",
                    nameof(directory));
            }

            Directory.CreateDirectory(directory);

            savePath = Path.Combine(
                directory,
                "bank-cards.json");

            Load();
        }

        public int Count
        {
            get
            {
                lock (sync)
                    return cards.Count;
            }
        }

        public int ActiveCount
        {
            get
            {
                lock (sync)
                    return cards.Values.Count(card =>
                        card != null && !card.Revoked);
            }
        }

        /// <summary>
        /// Enregistre une nouvelle carte et son propriétaire.
        ///
        /// CardId correspond à l'identifiant de l'objet 3D BankCard.
        /// OwnerPlayerId correspond au PlatformID persistant du joueur.
        /// IssuedByPlayerId correspond au banquier qui délivre la carte.
        /// </summary>
        public bool IssueCard(
            bool isAuthority,
            string cardId,
            string ownerPlayerId,
            string issuedByPlayerId,
            out string error)
        {
            error = null;

            if (!isAuthority)
            {
                error = "Seul l'hôte peut délivrer une carte bancaire.";
                return false;
            }

            if (!IsValidCardId(cardId))
            {
                error = "Identifiant de carte invalide.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(ownerPlayerId) ||
                string.IsNullOrWhiteSpace(issuedByPlayerId))
            {
                error = "Identité du propriétaire ou du banquier invalide.";
                return false;
            }

            lock (sync)
            {
                if (cards.ContainsKey(cardId))
                {
                    error = "Cette carte est déjà enregistrée.";
                    return false;
                }

                if (cards.Count >= MaximumCards)
                {
                    error = "La limite de cartes bancaires est atteinte.";
                    return false;
                }

                BankCardData card = new BankCardData
                {
                    CardId = cardId,
                    OwnerPlayerId = ownerPlayerId,
                    IssuedByPlayerId = issuedByPlayerId,
                    CreatedAtUtcTicks = DateTime.UtcNow.Ticks,
                    Revoked = false
                };

                cards.Add(cardId, card);

                if (!SaveInternal())
                {
                    cards.Remove(cardId);

                    error =
                        "Impossible de sauvegarder la nouvelle carte.";

                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Vérifie qu'une carte existe, qu'elle est active
        /// et qu'elle appartient bien au joueur demandé.
        /// </summary>
        public bool IsOwnedBy(
            string cardId,
            string playerId)
        {
            if (!IsValidCardId(cardId) ||
                string.IsNullOrWhiteSpace(playerId))
            {
                return false;
            }

            lock (sync)
            {
                BankCardData card;

                if (!cards.TryGetValue(cardId, out card))
                    return false;

                return card != null
                    && !card.Revoked
                    && string.Equals(
                        card.OwnerPlayerId,
                        playerId,
                        StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Retourne les informations d'une carte active.
        /// Une copie est retournée pour empêcher toute modification
        /// directe des données internes.
        /// </summary>
        public bool TryGetActiveCard(
            string cardId,
            out BankCardData card)
        {
            card = null;

            if (!IsValidCardId(cardId))
                return false;

            lock (sync)
            {
                BankCardData existing;

                if (!cards.TryGetValue(cardId, out existing) ||
                    existing == null ||
                    existing.Revoked)
                {
                    return false;
                }

                card = Copy(existing);
                return true;
            }
        }

        /// <summary>
        /// Révoque définitivement une carte.
        /// Une carte révoquée ne peut plus être réactivée.
        /// </summary>
        public bool RevokeCard(
            bool isAuthority,
            string cardId,
            string requestedByPlayerId,
            out string error)
        {
            error = null;

            if (!isAuthority)
            {
                error = "Seul l'hôte peut révoquer une carte.";
                return false;
            }

            if (!IsValidCardId(cardId) ||
                string.IsNullOrWhiteSpace(requestedByPlayerId))
            {
                error = "Informations de révocation invalides.";
                return false;
            }

            lock (sync)
            {
                BankCardData card;

                if (!cards.TryGetValue(cardId, out card) ||
                    card == null)
                {
                    error = "Carte bancaire introuvable.";
                    return false;
                }

                if (card.Revoked)
                {
                    error = "Cette carte est déjà révoquée.";
                    return false;
                }

                // Seul le propriétaire ou le banquier émetteur
                // peut demander la révocation.
                if (!string.Equals(
                        card.OwnerPlayerId,
                        requestedByPlayerId,
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        card.IssuedByPlayerId,
                        requestedByPlayerId,
                        StringComparison.Ordinal))
                {
                    error = "Tu n'es pas autorisé à révoquer cette carte.";
                    return false;
                }

                card.Revoked = true;

                if (!SaveInternal())
                {
                    card.Revoked = false;

                    error =
                        "Impossible de sauvegarder la révocation.";

                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Retourne toutes les cartes actives d'un joueur.
        /// </summary>
        public List<BankCardData> GetPlayerCards(
            string playerId)
        {
            List<BankCardData> result =
                new List<BankCardData>();

            if (string.IsNullOrWhiteSpace(playerId))
                return result;

            lock (sync)
            {
                foreach (BankCardData card in cards.Values)
                {
                    if (card == null ||
                        card.Revoked ||
                        !string.Equals(
                            card.OwnerPlayerId,
                            playerId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    result.Add(Copy(card));
                }
            }

            return result;
        }

        /// <summary>
        /// Sauvegarde explicite du registre.
        /// </summary>
        public void Save()
        {
            lock (sync)
            {
                if (!SaveInternal())
                {
                    MelonLoader.MelonLogger.Error(
                        "[FusionDarkRP Banque] "
                        + "Impossible de sauvegarder les cartes.");
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
                        cards.Clear();

                        MelonLoader.MelonLogger.Warning(
                            "[FusionDarkRP Banque] "
                            + "Sauvegarde des cartes invalide : "
                            + exception.Message);
                    }
                }

                string backupPath = savePath + ".bak";

                if (!File.Exists(backupPath))
                    return;

                try
                {
                    LoadFromFile(backupPath);

                    MelonLoader.MelonLogger.Warning(
                        "[FusionDarkRP Banque] "
                        + "Registre restauré depuis la sauvegarde.");

                    SaveInternal();
                }
                catch (Exception exception)
                {
                    cards.Clear();

                    MelonLoader.MelonLogger.Error(
                        "[FusionDarkRP Banque] "
                        + "Restauration des cartes impossible : "
                        + exception);
                }
            }
        }

        private void LoadFromFile(string path)
        {
            BankCardSaveData data =
                JsonSerializer.Deserialize<BankCardSaveData>(
                    File.ReadAllText(path),
                    JsonOptions);

            if (data == null || data.SchemaVersion != 1)
            {
                throw new InvalidDataException(
                    "Version du registre de cartes invalide.");
            }

            cards.Clear();

            foreach (BankCardData card in
                data.Cards ?? new List<BankCardData>())
            {
                if (card == null ||
                    !IsValidCardId(card.CardId) ||
                    string.IsNullOrWhiteSpace(card.OwnerPlayerId) ||
                    string.IsNullOrWhiteSpace(card.IssuedByPlayerId) ||
                    card.CreatedAtUtcTicks <= 0)
                {
                    continue;
                }

                if (cards.Count >= MaximumCards)
                    break;

                if (cards.ContainsKey(card.CardId))
                    continue;

                cards.Add(
                    card.CardId,
                    Copy(card));
            }
        }

        private bool SaveInternal()
        {
            string temporaryPath = savePath + ".tmp";
            string backupPath = savePath + ".bak";

            try
            {
                BankCardSaveData data =
                    new BankCardSaveData
                    {
                        SchemaVersion = 1,
                        Cards = cards.Values
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
                        File.Copy(
                            savePath,
                            backupPath,
                            true);

                        File.Move(
                            temporaryPath,
                            savePath,
                            true);
                    }
                }
                else
                {
                    File.Move(
                        temporaryPath,
                        savePath);
                }

                return true;
            }
            catch (Exception exception)
            {
                MelonLoader.MelonLogger.Error(
                    "[FusionDarkRP Banque] "
                    + "Erreur de sauvegarde des cartes : "
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

        private static bool IsValidCardId(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId))
                return false;

            Guid parsedId;

            return Guid.TryParseExact(
                cardId,
                "N",
                out parsedId);
        }

        private static BankCardData Copy(BankCardData source)
        {
            if (source == null)
                return null;

            return new BankCardData
            {
                CardId = source.CardId,
                OwnerPlayerId = source.OwnerPlayerId,
                IssuedByPlayerId = source.IssuedByPlayerId,
                CreatedAtUtcTicks = source.CreatedAtUtcTicks,
                Revoked = source.Revoked
            };
        }
    }
}
