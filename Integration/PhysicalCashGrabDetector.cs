using System;
using BoneLib;
using Il2CppSLZ.Marrow;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    public static class PhysicalCashGrabDetector
    {
        private const string CashObjectPrefix = "FusionDarkRP_Cash_";

        private static readonly object Sync = new object();

        private static bool initialized;
        private static bool enabled;

        public static event Action<string, GameObject> CashGrabbed;

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
                {
                    enabled = true;
                    return true;
                }

                try
                {
                    Hooking.OnGrabObject -= HandleGrabObject;
                    Hooking.OnGrabObject += HandleGrabObject;

                    initialized = true;
                    enabled = true;

                    Debug.Log(
                        "[FusionDarkRP Cash] Detection de saisie active."
                    );

                    return true;
                }
                catch (Exception exception)
                {
                    initialized = false;
                    enabled = false;

                    Debug.LogError(
                        "[FusionDarkRP Cash] Initialisation impossible : "
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
                enabled = false;

                if (initialized)
                {
                    try
                    {
                        Hooking.OnGrabObject -= HandleGrabObject;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError(
                            "[FusionDarkRP Cash] Désactivation du détecteur : "
                            + exception
                        );
                    }
                }

                initialized = false;
                CashGrabbed = null;

                Debug.Log(
                    "[FusionDarkRP Cash] Detection de saisie arretee."
                );
            }
        }

        private static void HandleGrabObject(
            GameObject grabbedObject,
            Hand hand)
        {
            if (grabbedObject == null)
                return;

            lock (Sync)
            {
                if (!enabled)
                    return;
            }

            try
            {
                // BoneLib peut transmettre le GameObject d'un collider enfant
                // plutôt que la racine de l'objet tenu. On remonte donc la
                // hiérarchie pour retrouver l'identifiant porté par la racine.
                if (!TryFindCashRoot(
                    grabbedObject,
                    out GameObject cashRoot,
                    out string cashId))
                {
                    return;
                }

                Action<string, GameObject> callback;

                lock (Sync)
                {
                    if (!enabled)
                        return;

                    callback = CashGrabbed;
                }

                callback?.Invoke(cashId, cashRoot);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[FusionDarkRP Cash] Erreur de saisie : "
                    + exception
                );
            }
        }


        private static bool TryFindCashRoot(
            GameObject grabbedObject,
            out GameObject cashRoot,
            out string cashId)
        {
            cashRoot = null;
            cashId = null;

            if (grabbedObject == null)
                return false;

            Transform current = grabbedObject.transform;
            int depth = 0;

            // Limite défensive : évite de parcourir une hiérarchie anormale.
            while (current != null && depth++ < 32)
            {
                if (TryExtractCashId(current.gameObject.name, out cashId))
                {
                    cashRoot = current.gameObject;
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        public static bool TryExtractCashId(
            string objectName,
            out string cashId)
        {
            cashId = null;

            if (string.IsNullOrWhiteSpace(objectName))
                return false;

            if (!objectName.StartsWith(
                CashObjectPrefix,
                StringComparison.Ordinal))
            {
                return false;
            }

            int separatorIndex = objectName.LastIndexOf('_');

            if (separatorIndex < CashObjectPrefix.Length
                || separatorIndex >= objectName.Length - 1)
            {
                return false;
            }

            string candidate =
                objectName.Substring(separatorIndex + 1);

            if (!Guid.TryParseExact(candidate, "N", out Guid parsedId))
                return false;

            if (parsedId == Guid.Empty)
                return false;

            cashId = parsedId.ToString("N");

            return true;
        }
    }
}
