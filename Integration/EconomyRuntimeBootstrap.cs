
using System;
using System.IO;
using FusionDarkRP.Economy;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Initialise et conserve les services économiques du mod.
    /// Les sauvegardes sont placées dans le dossier persistant
    /// accessible en écriture sur Quest.
    /// </summary>
    public static class EconomyRuntimeBootstrap
    {
        private const long MaxBalanceCents = 9000000000000000L;

        private static EconomyRuntime runtime;
        private static bool initialized;

        public static bool IsInitialized
        {
            get { return initialized && runtime != null; }
        }

        public static EconomyRuntime Runtime
        {
            get { return runtime; }
        }

        public static bool Initialize()
        {
            if (IsInitialized)
                return true;

            try
            {
                string persistentDirectory =
                    Application.persistentDataPath;

                if (string.IsNullOrWhiteSpace(persistentDirectory))
                {
                    throw new InvalidOperationException(
                        "Le dossier persistant Unity est indisponible."
                    );
                }

                string dataDirectory = Path.Combine(
                    persistentDirectory,
                    "FusionDarkRP",
                    "Economy"
                );

                Directory.CreateDirectory(dataDirectory);

                // Vérifie réellement que le dossier accepte l'écriture.
                string testFile = Path.Combine(
                    dataDirectory,
                    "write_test.tmp"
                );

                using (FileStream stream = new FileStream(
                    testFile,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                {
                    stream.WriteByte(1);
                    stream.Flush();
                }

                File.Delete(testFile);

                runtime = new EconomyRuntime(
                    dataDirectory,
                    MaxBalanceCents
                );

                initialized = true;

                MelonLogger.Msg(
                    "[FusionDarkRP] Systeme economique demarre."
                );

                MelonLogger.Msg(
                    "[FusionDarkRP] Sauvegardes accessibles : "
                    + dataDirectory
                );

                return true;
            }
            catch (Exception exception)
            {
                runtime = null;
                initialized = false;

                MelonLogger.Error(
                    "[FusionDarkRP] Echec initialisation economique : "
                    + exception
                );

                return false;
            }
        }

        public static void Save()
        {
            if (!IsInitialized)
                return;

            try
            {
                runtime.SaveAll();
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP] Erreur sauvegarde : "
                    + exception
                );
            }
        }

        public static void Shutdown()
        {
            if (!IsInitialized)
                return;

            try
            {
                runtime.SaveAll();

                MelonLogger.Msg(
                    "[FusionDarkRP] Systeme economique sauvegarde."
                );
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP] Erreur fermeture economique : "
                    + exception
                );
            }
            finally
            {
                runtime = null;
                initialized = false;
            }
        }
    }
}
