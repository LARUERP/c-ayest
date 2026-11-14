using System;
using System.Collections.Generic;

namespace FusionDarkRP.Economy
{
    public sealed class PhysicalCashSpawnRegistry
    {
        private readonly object sync = new object();

        private readonly Dictionary<string, PhysicalCashSpawnTicket> tickets =
            new Dictionary<string, PhysicalCashSpawnTicket>(
                StringComparer.Ordinal);

        private readonly HashSet<string> processedRequests =
            new HashSet<string>(StringComparer.Ordinal);

        public int Count
        {
            get
            {
                lock (sync)
                {
                    return tickets.Count;
                }
            }
        }

        public bool TryRegister(
            PhysicalCashSpawnTicket ticket,
            out string error)
        {
            error = null;

            if (!IsValidTicket(ticket))
            {
                error = "Ticket de création invalide.";
                return false;
            }

            lock (sync)
            {
                if (tickets.ContainsKey(ticket.CashId))
                {
                    error = "Ce billet est déjà enregistré.";
                    return false;
                }

                if (processedRequests.Contains(ticket.RequestId))
                {
                    error = "Cette demande a déjà été traitée.";
                    return false;
                }

                tickets.Add(ticket.CashId, ticket);
                processedRequests.Add(ticket.RequestId);

                return true;
            }
        }

        public bool TryGet(
            string cashId,
            out PhysicalCashSpawnTicket ticket)
        {
            ticket = null;

            if (string.IsNullOrWhiteSpace(cashId))
                return false;

            lock (sync)
            {
                return tickets.TryGetValue(cashId, out ticket);
            }
        }

        /// <summary>
        /// Change le propriétaire d'un billet dans le registre.
        /// L'identifiant et la valeur du billet restent inchangés.
        /// </summary>
        public bool TryChangeOwner(
            string cashId,
            string currentOwnerPlayerId,
            string newOwnerPlayerId,
            out PhysicalCashSpawnTicket updatedTicket,
            out string error)
        {
            updatedTicket = null;
            error = null;

            if (string.IsNullOrWhiteSpace(cashId) ||
                string.IsNullOrWhiteSpace(currentOwnerPlayerId) ||
                string.IsNullOrWhiteSpace(newOwnerPlayerId))
            {
                error = "Informations de transfert invalides.";
                return false;
            }

            lock (sync)
            {
                if (!tickets.TryGetValue(
                    cashId,
                    out PhysicalCashSpawnTicket existing))
                {
                    error = "Billet introuvable dans le registre.";
                    return false;
                }

                if (existing == null)
                {
                    error = "Ticket de billet invalide.";
                    return false;
                }

                if (!string.Equals(
                    existing.OwnerPlayerId,
                    currentOwnerPlayerId,
                    StringComparison.Ordinal))
                {
                    error = "Le joueur actuel n'est pas propriétaire du billet.";
                    return false;
                }

                if (string.Equals(
                    currentOwnerPlayerId,
                    newOwnerPlayerId,
                    StringComparison.Ordinal))
                {
                    error = "Le destinataire est déjà propriétaire du billet.";
                    return false;
                }

                if (!existing.TryChangeOwner(
                    currentOwnerPlayerId,
                    newOwnerPlayerId,
                    out updatedTicket,
                    out error))
                {
                    updatedTicket = null;
                    return false;
                }

                tickets[cashId] = updatedTicket;

                return true;
            }
        }

        /// <summary>
        /// Retire un ticket précis uniquement si le propriétaire correspond.
        /// Ne touche jamais aux autres tickets du joueur.
        /// </summary>
        public bool TryTake(
            string cashId,
            string ownerPlayerId,
            out PhysicalCashSpawnTicket ticket,
            out string error)
        {
            ticket = null;
            error = null;

            if (string.IsNullOrWhiteSpace(cashId) ||
                string.IsNullOrWhiteSpace(ownerPlayerId))
            {
                error = "Informations du ticket invalides.";
                return false;
            }

            lock (sync)
            {
                if (!tickets.TryGetValue(
                    cashId,
                    out PhysicalCashSpawnTicket existing))
                {
                    error = "Ticket introuvable.";
                    return false;
                }

                if (!string.Equals(
                    existing.OwnerPlayerId,
                    ownerPlayerId,
                    StringComparison.Ordinal))
                {
                    error = "Ce ticket appartient à un autre joueur.";
                    return false;
                }

                tickets.Remove(cashId);
                ticket = existing;

                return true;
            }
        }

        public bool TryConsume(
            string cashId,
            string ownerPlayerId,
            out PhysicalCashSpawnTicket ticket,
            out string error)
        {
            return TryTake(
                cashId,
                ownerPlayerId,
                out ticket,
                out error);
        }

        public List<PhysicalCashSpawnTicket> TakePlayerTickets(
            string ownerPlayerId)
        {
            List<PhysicalCashSpawnTicket> removed =
                new List<PhysicalCashSpawnTicket>();

            if (string.IsNullOrWhiteSpace(ownerPlayerId))
                return removed;

            lock (sync)
            {
                List<string> toRemove = new List<string>();

                foreach (KeyValuePair<string, PhysicalCashSpawnTicket> entry
                    in tickets)
                {
                    if (entry.Value != null &&
                        string.Equals(
                            entry.Value.OwnerPlayerId,
                            ownerPlayerId,
                            StringComparison.Ordinal))
                    {
                        toRemove.Add(entry.Key);
                        removed.Add(entry.Value);
                    }
                }

                foreach (string cashId in toRemove)
                    tickets.Remove(cashId);
            }

            return removed;
        }

        public bool TryRestore(
            PhysicalCashSpawnTicket ticket,
            out string error)
        {
            error = null;

            if (!IsValidTicket(ticket))
            {
                error = "Impossible de restaurer un ticket invalide.";
                return false;
            }

            lock (sync)
            {
                if (tickets.ContainsKey(ticket.CashId))
                {
                    error = "Le ticket existe déjà dans le registre.";
                    return false;
                }

                tickets.Add(ticket.CashId, ticket);
                processedRequests.Add(ticket.RequestId);

                return true;
            }
        }

        // Ancienne méthode conservée pour compatibilité.
        public int RemovePlayer(string ownerPlayerId)
        {
            return TakePlayerTickets(ownerPlayerId).Count;
        }

        public void Clear()
        {
            lock (sync)
            {
                tickets.Clear();
                processedRequests.Clear();
            }
        }

        private static bool IsValidTicket(
            PhysicalCashSpawnTicket ticket)
        {
            return ticket != null &&
                   !string.IsNullOrWhiteSpace(ticket.CashId) &&
                   !string.IsNullOrWhiteSpace(ticket.OwnerPlayerId) &&
                   !string.IsNullOrWhiteSpace(ticket.ItemId) &&
                   PhysicalCashRequestId.IsValid(ticket.RequestId) &&
                   ticket.AmountCents > 0;
        }
    }
}
