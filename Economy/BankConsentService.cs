using System;
using System.Collections.Generic;

namespace FusionDarkRP.Economy
{
    public enum BankConsentStatus
    {
        Pending,
        Accepted,
        Refused,
        Expired
    }

    public enum BankOperationType
    {
        Deposit,
        Withdraw
    }

    public sealed class BankConsentRequest
    {
        public string RequestId { get; set; }
        public string BankerId { get; set; }
        public string CustomerId { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public BankConsentStatus Status { get; set; }
    }

    public sealed class BankOperationRequest
    {
        public string RequestId { get; set; }
        public string ConsentRequestId { get; set; }
        public string BankerId { get; set; }
        public string CustomerId { get; set; }
        public BankOperationType Operation { get; set; }
        public long AmountCents { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public bool Confirmed { get; set; }
        public bool Completed { get; set; }
    }

    /// <summary>
    /// Gère les autorisations bancaires et les confirmations
    /// individuelles des opérations.
    /// Toutes les validations et opérations sont effectuées
    /// côté hôte.
    /// </summary>
    public sealed class BankConsentService
    {
        private readonly object sync = new object();

        private readonly Dictionary<string, BankConsentRequest>
            consents =
                new Dictionary<string, BankConsentRequest>();

        private readonly Dictionary<string, BankOperationRequest>
            operations =
                new Dictionary<string, BankOperationRequest>();

        private readonly TimeSpan consentDuration =
            TimeSpan.FromSeconds(60);

        private readonly TimeSpan operationDuration =
            TimeSpan.FromSeconds(60);

        private const long MaximumOperationCents =
            100000000L;

        public bool CreateConsentRequest(
            string bankerId,
            string customerId,
            out BankConsentRequest request,
            out string error)
        {
            request = null;
            error = null;

            if (string.IsNullOrWhiteSpace(bankerId) ||
                string.IsNullOrWhiteSpace(customerId))
            {
                error = "Identifiant joueur invalide.";
                return false;
            }

            if (string.Equals(
                bankerId,
                customerId,
                StringComparison.Ordinal))
            {
                error = "Un banquier ne peut pas consulter son propre compte.";
                return false;
            }

            lock (sync)
            {
                CleanupExpiredUnsafe();

                foreach (BankConsentRequest existing in consents.Values)
                {
                    if (existing.BankerId != bankerId ||
                        existing.CustomerId != customerId)
                        continue;

                    if (existing.Status == BankConsentStatus.Accepted &&
                        DateTime.UtcNow < existing.ExpiresAtUtc)
                    {
                        error = "Une autorisation existe déjà.";
                        return false;
                    }

                    if (existing.Status == BankConsentStatus.Pending &&
                        DateTime.UtcNow < existing.ExpiresAtUtc)
                    {
                        error = "Une demande est déjà en attente.";
                        return false;
                    }
                }

                request = new BankConsentRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    BankerId = bankerId,
                    CustomerId = customerId,
                    ExpiresAtUtc = DateTime.UtcNow.Add(consentDuration),
                    Status = BankConsentStatus.Pending
                };

                consents.Add(request.RequestId, request);
                return true;
            }
        }

        public bool ResolveConsent(
            string requestId,
            string customerId,
            bool accepted,
            out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(requestId) ||
                string.IsNullOrWhiteSpace(customerId))
            {
                error = "Demande invalide.";
                return false;
            }

            lock (sync)
            {
                CleanupExpiredUnsafe();

                BankConsentRequest request;

                if (!consents.TryGetValue(requestId, out request))
                {
                    error = "Demande introuvable.";
                    return false;
                }

                if (request.CustomerId != customerId)
                {
                    error = "Cette demande ne t'est pas destinée.";
                    return false;
                }

                if (request.Status != BankConsentStatus.Pending)
                {
                    error = "Cette demande a déjà été traitée.";
                    return false;
                }

                if (DateTime.UtcNow >= request.ExpiresAtUtc)
                {
                    request.Status = BankConsentStatus.Expired;
                    error = "La demande a expiré.";
                    return false;
                }

                request.Status = accepted
                    ? BankConsentStatus.Accepted
                    : BankConsentStatus.Refused;

                return true;
            }
        }

        public bool HasActiveConsent(
            string consentRequestId,
            string bankerId,
            string customerId)
        {
            if (string.IsNullOrWhiteSpace(consentRequestId) ||
                string.IsNullOrWhiteSpace(bankerId) ||
                string.IsNullOrWhiteSpace(customerId))
                return false;

            lock (sync)
            {
                CleanupExpiredUnsafe();

                return HasActiveConsentUnsafe(
                    consentRequestId,
                    bankerId,
                    customerId);
            }
        }

        public bool CreateOperationRequest(
            string consentRequestId,
            string bankerId,
            string customerId,
            BankOperationType operation,
            long amountCents,
            out BankOperationRequest request,
            out string error)
        {
            request = null;
            error = null;

            if (string.IsNullOrWhiteSpace(consentRequestId) ||
                string.IsNullOrWhiteSpace(bankerId) ||
                string.IsNullOrWhiteSpace(customerId))
            {
                error = "Identifiants bancaires invalides.";
                return false;
            }

            if (amountCents <= 0 ||
                amountCents > MaximumOperationCents)
            {
                error = "Montant invalide ou supérieur à la limite autorisée.";
                return false;
            }

            if (operation != BankOperationType.Deposit &&
                operation != BankOperationType.Withdraw)
            {
                error = "Type d'opération invalide.";
                return false;
            }

            lock (sync)
            {
                CleanupExpiredUnsafe();

                if (!HasActiveConsentUnsafe(
                    consentRequestId,
                    bankerId,
                    customerId))
                {
                    error = "Le client n'a pas autorisé cette consultation.";
                    return false;
                }

                // Une seule opération en attente par autorisation.
                foreach (BankOperationRequest existing in operations.Values)
                {
                    if (existing.ConsentRequestId == consentRequestId &&
                        !existing.Completed &&
                        DateTime.UtcNow < existing.ExpiresAtUtc)
                    {
                        error = "Une opération est déjà en attente de confirmation.";
                        return false;
                    }
                }

                request = new BankOperationRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    ConsentRequestId = consentRequestId,
                    BankerId = bankerId,
                    CustomerId = customerId,
                    Operation = operation,
                    AmountCents = amountCents,
                    ExpiresAtUtc = DateTime.UtcNow.Add(operationDuration),
                    Confirmed = false,
                    Completed = false
                };

                operations.Add(request.RequestId, request);
                return true;
            }
        }

        public bool ConfirmOperation(
            string operationRequestId,
            string customerId,
            bool confirmed,
            out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(operationRequestId) ||
                string.IsNullOrWhiteSpace(customerId))
            {
                error = "Confirmation invalide.";
                return false;
            }

            lock (sync)
            {
                CleanupExpiredUnsafe();

                BankOperationRequest request;

                if (!operations.TryGetValue(
                    operationRequestId,
                    out request))
                {
                    error = "Opération introuvable ou expirée.";
                    return false;
                }

                if (request.CustomerId != customerId)
                {
                    error = "Cette opération ne t'est pas destinée.";
                    return false;
                }

                if (request.Completed || request.Confirmed)
                {
                    error = "Cette opération a déjà été traitée.";
                    return false;
                }

                if (DateTime.UtcNow >= request.ExpiresAtUtc)
                {
                    operations.Remove(operationRequestId);
                    error = "La confirmation a expiré.";
                    return false;
                }

                if (!HasActiveConsentUnsafe(
                    request.ConsentRequestId,
                    request.BankerId,
                    request.CustomerId))
                {
                    operations.Remove(operationRequestId);
                    error = "L'autorisation bancaire n'est plus valide.";
                    return false;
                }

                if (!confirmed)
                {
                    operations.Remove(operationRequestId);
                    error = "Le client a refusé l'opération.";

                    // Le refus a bien été traité.
                    return true;
                }

                request.Confirmed = true;
                return true;
            }
        }

        public bool ExecuteConfirmedOperation(
            string operationRequestId,
            string bankerId,
            string customerId,
            EconomyRuntime runtime,
            out string error)
        {
            error = null;

            if (runtime == null)
            {
                error = "Le système économique n'est pas initialisé.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(operationRequestId) ||
                string.IsNullOrWhiteSpace(bankerId) ||
                string.IsNullOrWhiteSpace(customerId))
            {
                error = "Identifiants d'opération invalides.";
                return false;
            }

            lock (sync)
            {
                CleanupExpiredUnsafe();

                BankOperationRequest request;

                if (!operations.TryGetValue(
                    operationRequestId,
                    out request))
                {
                    error = "Opération introuvable ou expirée.";
                    return false;
                }

                if (request.BankerId != bankerId ||
                    request.CustomerId != customerId)
                {
                    error = "Identité du demandeur invalide.";
                    operations.Remove(operationRequestId);
                    return false;
                }

                if (!request.Confirmed || request.Completed)
                {
                    error = "Le client n'a pas confirmé cette opération.";
                    operations.Remove(operationRequestId);
                    return false;
                }

                if (DateTime.UtcNow >= request.ExpiresAtUtc)
                {
                    operations.Remove(operationRequestId);
                    error = "L'opération a expiré.";
                    return false;
                }

                if (!HasActiveConsentUnsafe(
                    request.ConsentRequestId,
                    bankerId,
                    customerId))
                {
                    operations.Remove(operationRequestId);
                    error = "L'autorisation bancaire n'est plus valide.";
                    return false;
                }

                bool success;

                if (request.Operation == BankOperationType.Deposit)
                {
                    success = runtime.BankOperations.Deposit(
                        true,
                        request.RequestId,
                        customerId,
                        request.AmountCents,
                        out error);
                }
                else if (request.Operation == BankOperationType.Withdraw)
                {
                    success = runtime.BankOperations.Withdraw(
                        true,
                        request.RequestId,
                        customerId,
                        request.AmountCents,
                        out error);
                }
                else
                {
                    error = "Type d'opération invalide.";
                    operations.Remove(operationRequestId);
                    return false;
                }

                // Important : une opération échouée ne doit pas
                // rester bloquée dans la liste des opérations actives.
                if (!success)
                {
                    operations.Remove(operationRequestId);

                    if (string.IsNullOrWhiteSpace(error))
                        error = "L'opération bancaire a échoué.";

                    return false;
                }

                request.Completed = true;
                operations.Remove(operationRequestId);

                return true;
            }
        }

        public bool GetBalances(
            string consentRequestId,
            string bankerId,
            string customerId,
            EconomyRuntime runtime,
            out long cashCents,
            out long bankCents,
            out string error)
        {
            cashCents = 0;
            bankCents = 0;
            error = null;

            if (runtime == null)
            {
                error = "Le système économique n'est pas initialisé.";
                return false;
            }

            lock (sync)
            {
                CleanupExpiredUnsafe();

                if (!HasActiveConsentUnsafe(
                    consentRequestId,
                    bankerId,
                    customerId))
                {
                    error = "Consultation non autorisée.";
                    return false;
                }

                if (!runtime.Bank.GetBalances(
                    customerId,
                    out cashCents,
                    out bankCents))
                {
                    error = "Compte bancaire introuvable.";
                    return false;
                }

                return true;
            }
        }

        public void RemovePlayer(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return;

            lock (sync)
            {
                List<string> consentIds = new List<string>();

                foreach (KeyValuePair<string, BankConsentRequest> item
                    in consents)
                {
                    if (item.Value.BankerId == playerId ||
                        item.Value.CustomerId == playerId)
                    {
                        consentIds.Add(item.Key);
                    }
                }

                foreach (string id in consentIds)
                    consents.Remove(id);

                List<string> operationIds = new List<string>();

                foreach (KeyValuePair<string, BankOperationRequest> item
                    in operations)
                {
                    if (item.Value.BankerId == playerId ||
                        item.Value.CustomerId == playerId)
                    {
                        operationIds.Add(item.Key);
                    }
                }

                foreach (string id in operationIds)
                    operations.Remove(id);
            }
        }

        private bool HasActiveConsentUnsafe(
            string consentRequestId,
            string bankerId,
            string customerId)
        {
            if (string.IsNullOrWhiteSpace(consentRequestId) ||
                string.IsNullOrWhiteSpace(bankerId) ||
                string.IsNullOrWhiteSpace(customerId))
                return false;

            BankConsentRequest request;

            if (!consents.TryGetValue(
                consentRequestId,
                out request))
                return false;

            if (request.Status != BankConsentStatus.Accepted)
                return false;

            if (DateTime.UtcNow >= request.ExpiresAtUtc)
            {
                request.Status = BankConsentStatus.Expired;
                return false;
            }

            return request.BankerId == bankerId &&
                   request.CustomerId == customerId;
        }

        private void CleanupExpiredUnsafe()
        {
            DateTime now = DateTime.UtcNow;

            List<string> expiredOperations = new List<string>();

            foreach (KeyValuePair<string, BankOperationRequest> item
                in operations)
            {
                if (now >= item.Value.ExpiresAtUtc)
                    expiredOperations.Add(item.Key);
            }

            foreach (string id in expiredOperations)
                operations.Remove(id);

            foreach (BankConsentRequest consent in consents.Values)
            {
                if (consent.Status == BankConsentStatus.Pending &&
                    now >= consent.ExpiresAtUtc)
                {
                    consent.Status = BankConsentStatus.Expired;
                }

                if (consent.Status == BankConsentStatus.Accepted &&
                    now >= consent.ExpiresAtUtc)
                {
                    consent.Status = BankConsentStatus.Expired;
                }
            }
        }
    }
}
