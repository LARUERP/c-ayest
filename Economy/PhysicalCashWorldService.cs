using System;
using System.Collections.Generic;

namespace FusionDarkRP.Economy
{
    public sealed class PhysicalCashWorldService
    {
        private readonly object sync = new object();

        private readonly PhysicalCashSpawnCoordinator coordinator;
        private readonly IPhysicalCashWorldSpawner spawner;
        private readonly Func<bool> authorityProvider;

        private readonly Dictionary<string, string> spawnedObjects =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public PhysicalCashWorldService(
            PhysicalCashSpawnCoordinator coordinator,
            IPhysicalCashWorldSpawner spawner,
            Func<bool> authorityProvider)
        {
            this.coordinator = coordinator
                ?? throw new ArgumentNullException(nameof(coordinator));

            this.spawner = spawner
                ?? throw new ArgumentNullException(nameof(spawner));

            this.authorityProvider = authorityProvider
                ?? throw new ArgumentNullException(nameof(authorityProvider));
        }

        public int SpawnedCount
        {
            get
            {
                lock (sync)
                {
                    return spawnedObjects.Count;
                }
            }
        }

        public bool TrySpawn(
            string cashId,
            string playerId,
            out string spawnedObjectId,
            out string error)
        {
            spawnedObjectId = null;
            error = null;

            lock (sync)
            {
                if (!IsAuthority())
                {
                    error = "Action réservée à l'hôte.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(cashId) ||
                    string.IsNullOrWhiteSpace(playerId))
                {
                    error = "Informations du billet invalides.";
                    return false;
                }

                if (spawnedObjects.ContainsKey(cashId))
                {
                    error = "Ce billet a déjà été créé dans le monde.";
                    return false;
                }

                PhysicalCashSpawnTicket ticket;

                if (!coordinator.TryGetTicket(cashId, out ticket))
                {
                    error = "Ticket de création introuvable.";
                    return false;
                }

                if (!string.Equals(
                    ticket.OwnerPlayerId,
                    playerId,
                    StringComparison.Ordinal))
                {
                    error = "Ce billet appartient à un autre joueur.";
                    return false;
                }

                string objectId;

                if (!spawner.TrySpawn(
                    ticket,
                    out objectId,
                    out error))
                {
                    if (string.IsNullOrWhiteSpace(error))
                        error = "Impossible de créer l'objet physique.";

                    return false;
                }

                if (string.IsNullOrWhiteSpace(objectId))
                {
                    error = "Le spawner n'a pas fourni d'identifiant d'objet.";
                    return false;
                }

                PhysicalCashSpawnTicket consumedTicket;

                if (!coordinator.TryConsumeTicket(
                    cashId,
                    playerId,
                    out consumedTicket,
                    out error))
                {
                    string destroyError;

                    spawner.TryDestroy(objectId, out destroyError);

                    error = "Le ticket n'a pas pu être validé.";

                    if (!string.IsNullOrWhiteSpace(destroyError))
                        error += " Nettoyage : " + destroyError;

                    return false;
                }

                spawnedObjects.Add(cashId, objectId);
                spawnedObjectId = objectId;

                return true;
            }
        }

        public bool TryDestroy(
            string cashId,
            out string error)
        {
            error = null;

            lock (sync)
            {
                if (!IsAuthority())
                {
                    error = "Action réservée à l'hôte.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(cashId))
                {
                    error = "Identifiant du billet invalide.";
                    return false;
                }

                string objectId;

                if (!spawnedObjects.TryGetValue(cashId, out objectId))
                {
                    error = "Objet physique introuvable.";
                    return false;
                }

                if (!spawner.TryDestroy(objectId, out error))
                    return false;

                spawnedObjects.Remove(cashId);

                return true;
            }
        }

        public bool IsSpawned(string cashId)
        {
            if (string.IsNullOrWhiteSpace(cashId))
                return false;

            lock (sync)
            {
                return spawnedObjects.ContainsKey(cashId);
            }
        }

        public bool TryGetObjectId(
            string cashId,
            out string objectId)
        {
            objectId = null;

            if (string.IsNullOrWhiteSpace(cashId))
                return false;

            lock (sync)
            {
                return spawnedObjects.TryGetValue(
                    cashId,
                    out objectId);
            }
        }

        private bool IsAuthority()
        {
            try
            {
                return authorityProvider();
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
