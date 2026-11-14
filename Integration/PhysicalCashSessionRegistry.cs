using System;
using System.Collections.Generic;
using LabFusion.Player;
using FusionDarkRP.Economy;

namespace FusionDarkRP.Integration
{
    public sealed class PhysicalCashSessionRegistry
    {
        private readonly EconomyRuntime runtime;
        private readonly Func<bool> authorityProvider;

        private readonly Dictionary<string, PlayerPhysicalCashSession> sessions =
            new Dictionary<string, PlayerPhysicalCashSession>(
                StringComparer.Ordinal
            );

        private readonly object sync = new object();

        public PhysicalCashSessionRegistry(
            EconomyRuntime runtime,
            Func<bool> authorityProvider)
        {
            this.runtime = runtime
                ?? throw new ArgumentNullException(nameof(runtime));

            this.authorityProvider = authorityProvider
                ?? throw new ArgumentNullException(nameof(authorityProvider));
        }

        public bool TryGetOrCreate(
            PlayerID player,
            out PlayerPhysicalCashSession session,
            out string error)
        {
            session = null;
            error = null;

            if (!PlayerIdentityService.TryGetPersistentId(
                player,
                out string persistentId))
            {
                error = "Identité persistante du joueur invalide.";
                return false;
            }

            lock (sync)
            {
                if (sessions.TryGetValue(
                    persistentId,
                    out session))
                {
                    return true;
                }

                if (!PhysicalCashSessionFactory.TryCreate(
                    runtime,
                    player,
                    authorityProvider,
                    out session,
                    out error))
                {
                    return false;
                }

                sessions[persistentId] = session;
                return true;
            }
        }

        public bool TryGet(
            string persistentId,
            out PlayerPhysicalCashSession session)
        {
            session = null;

            if (string.IsNullOrWhiteSpace(persistentId))
                return false;

            lock (sync)
            {
                return sessions.TryGetValue(
                    persistentId,
                    out session);
            }
        }

        public bool Remove(string persistentId)
        {
            if (string.IsNullOrWhiteSpace(persistentId))
                return false;

            lock (sync)
            {
                return sessions.Remove(persistentId);
            }
        }

        public void Clear()
        {
            lock (sync)
            {
                sessions.Clear();
            }
        }

        public int Count
        {
            get
            {
                lock (sync)
                {
                    return sessions.Count;
                }
            }
        }
    }
}
