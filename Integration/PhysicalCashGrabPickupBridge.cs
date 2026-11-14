using System;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Relie la saisie physique d'un billet à sa demande
    /// de récupération dans le système économique.
    /// </summary>
    public static class PhysicalCashGrabPickupBridge
    {
        private static readonly object Sync = new object();

        private static bool initialized;

        public static bool IsInitialized
        {
            get
            {
                lock (Sync)
                    return initialized;
            }
        }

        public static bool Initialize()
        {
            lock (Sync)
            {
                if (initialized)
                    return true;

                try
                {
                    PhysicalCashGrabDetector.CashGrabbed -= OnCashGrabbed;
                    PhysicalCashGrabDetector.CashGrabbed += OnCashGrabbed;

                    initialized = true;

                    MelonLogger.Msg(
                        "[FusionDarkRP Cash] Liaison saisie et récupération initialisée."
                    );

                    return true;
                }
                catch (Exception exception)
                {
                    initialized = false;

                    MelonLogger.Error(
                        "[FusionDarkRP Cash] Liaison de récupération impossible : "
                        + exception
                    );

                    return false;
                }
            }
        }

        public static void Shutdown()
        {
            lock (Sync)
            {
                if (!initialized)
                    return;

                try
                {
                    PhysicalCashGrabDetector.CashGrabbed -= OnCashGrabbed;
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Cash] Nettoyage de la liaison : "
                        + exception.Message
                    );
                }

                initialized = false;
            }
        }

        private static void OnCashGrabbed(
            string cashId,
            GameObject grabbedObject)
        {
            if (grabbedObject == null ||
                string.IsNullOrWhiteSpace(cashId))
            {
                return;
            }

            try
            {
                if (!PhysicalCashGrabDetector.TryExtractCashId(
                    grabbedObject.name,
                    out string verifiedCashId))
                {
                    return;
                }

                if (!string.Equals(
                    cashId,
                    verifiedCashId,
                    StringComparison.Ordinal))
                {
                    return;
                }

                bool requested =
                    PhysicalCashPickupNetwork.RequestPickup(
                        verifiedCashId);

                if (!requested)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Cash] Demande de récupération non envoyée : "
                        + verifiedCashId
                    );
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP Cash] Erreur liaison saisie : "
                    + exception
                );
            }
        }
    }
}
