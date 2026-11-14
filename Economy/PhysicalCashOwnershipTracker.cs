using System;
using System.Collections.Generic;

namespace FusionDarkRP.Economy
{
    /// <summary>
    /// Suit le propriétaire courant de chaque billet physique présent
    /// dans le monde. Cette table est une aide mémoire côté serveur :
    /// le registre économique persistant reste la source de vérité.
    /// </summary>
    public static class PhysicalCashOwnershipTracker
    {
        private static readonly object Sync = new object();

        private static readonly Dictionary<string, string> Owners =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Enregistre le propriétaire initial d'un billet.
        /// Une répétition avec le même propriétaire est acceptée.
        /// </summary>
        public static bool TryRegister(
            string cashId,
            string ownerPlayerId,
            out string error)
        {
            error = null;

            if (!IsValidCashId(cashId))
            {
                error = "Identifiant du billet invalide.";
                return false;
            }

            if (!IsValidPlayerId(ownerPlayerId))
            {
                error = "Identifiant du propriétaire invalide.";
                return false;
            }

            lock (Sync)
            {
                if (Owners.TryGetValue(
                    cashId,
                    out string existingOwner))
                {
                    if (string.Equals(
                        existingOwner,
                        ownerPlayerId,
                        StringComparison.Ordinal))
                    {
                        return true;
                    }

                    error = "Ce billet possède déjà un propriétaire différent.";
                    return false;
                }

                Owners.Add(cashId, ownerPlayerId);
                return true;
            }
        }

        /// <summary>
        /// Récupère le propriétaire actuellement suivi.
        /// </summary>
        public static bool TryGetOwner(
            string cashId,
            out string ownerPlayerId)
        {
            ownerPlayerId = null;

            if (!IsValidCashId(cashId))
                return false;

            lock (Sync)
            {
                return Owners.TryGetValue(
                    cashId,
                    out ownerPlayerId);
            }
        }

        /// <summary>
        /// Modifie le propriétaire uniquement si le propriétaire attendu
        /// correspond encore à celui enregistré.
        /// </summary>
        public static bool TryChangeOwner(
            string cashId,
            string expectedOwnerPlayerId,
            string newOwnerPlayerId,
            out string error)
        {
            error = null;

            if (!IsValidCashId(cashId))
            {
                error = "Identifiant du billet invalide.";
                return false;
            }

            if (!IsValidPlayerId(expectedOwnerPlayerId) ||
                !IsValidPlayerId(newOwnerPlayerId))
            {
                error = "Identifiant de joueur invalide.";
                return false;
            }

            if (string.Equals(
                expectedOwnerPlayerId,
                newOwnerPlayerId,
                StringComparison.Ordinal))
            {
                error = "Le nouveau propriétaire est déjà le propriétaire actuel.";
                return false;
            }

            lock (Sync)
            {
                if (!Owners.TryGetValue(
                    cashId,
                    out string currentOwner))
                {
                    error = "Propriétaire du billet introuvable.";
                    return false;
                }

                if (!string.Equals(
                    currentOwner,
                    expectedOwnerPlayerId,
                    StringComparison.Ordinal))
                {
                    error = "Le propriétaire du billet a changé.";
                    return false;
                }

                Owners[cashId] = newOwnerPlayerId;
                return true;
            }
        }

        /// <summary>
        /// Retire le suivi d'un billet détruit ou définitivement consommé.
        /// </summary>
        public static bool TryForget(
            string cashId,
            string expectedOwnerPlayerId)
        {
            if (!IsValidCashId(cashId) ||
                !IsValidPlayerId(expectedOwnerPlayerId))
            {
                return false;
            }

            lock (Sync)
            {
                if (!Owners.TryGetValue(
                    cashId,
                    out string currentOwner))
                {
                    return false;
                }

                if (!string.Equals(
                    currentOwner,
                    expectedOwnerPlayerId,
                    StringComparison.Ordinal))
                {
                    return false;
                }

                return Owners.Remove(cashId);
            }
        }

        /// <summary>
        /// Nombre de billets actuellement suivis en mémoire.
        /// </summary>
        public static int Count
        {
            get
            {
                lock (Sync)
                    return Owners.Count;
            }
        }

        /// <summary>
        /// Nettoyage à l'arrêt du gestionnaire.
        /// Ne modifie pas les données économiques persistantes.
        /// </summary>
        public static void Clear()
        {
            lock (Sync)
                Owners.Clear();
        }

        private static bool IsValidCashId(string cashId)
        {
            if (string.IsNullOrWhiteSpace(cashId))
                return false;

            return Guid.TryParseExact(
                cashId,
                "N",
                out Guid parsedId)
                && parsedId != Guid.Empty;
        }

        private static bool IsValidPlayerId(string playerId)
        {
            return !string.IsNullOrWhiteSpace(playerId)
                && playerId.Length <= 256;
        }
    }
}
