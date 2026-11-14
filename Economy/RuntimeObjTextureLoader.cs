using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace FusionDarkRP.Economy
{
    /// <summary>
    /// Charge les textures PNG embarquées dans la DLL (map_Kd des fichiers MTL).
    /// Les textures sont mises en cache et partagées entre les matériaux.
    /// </summary>
    public static class RuntimeObjTextureLoader
    {
        private const string ResourcePrefix = "FusionDarkRP.MoneyModels.";
        private const long MaximumBytes = 4L * 1024 * 1024;

        private static readonly object Sync = new object();

        private static readonly Dictionary<string, Texture2D> Cache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> Failed =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static bool TryLoad(
            string fileName, out Texture2D texture, out string error)
        {
            texture = null;
            error = null;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = "Nom de texture vide.";
                return false;
            }

            string name = fileName.Trim();

            if (name.Contains("/") || name.Contains("\\") ||
                name.Contains("..") || name.IndexOf(':') >= 0 ||
                !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                error = "Nom de texture non autorisé : " + name;
                return false;
            }

            lock (Sync)
            {
                if (Cache.TryGetValue(name, out texture) && texture != null)
                    return true;

                if (Failed.Contains(name))
                {
                    error = "Texture indisponible : " + name;
                    return false;
                }

                try
                {
                    Assembly assembly = Assembly.GetExecutingAssembly();

                    using (Stream stream = assembly.GetManifestResourceStream(
                        ResourcePrefix + name))
                    {
                        if (stream == null)
                        {
                            Failed.Add(name);
                            error = "Texture embarquée introuvable : " + name;
                            return false;
                        }

                        if (stream.Length <= 0 || stream.Length > MaximumBytes)
                        {
                            Failed.Add(name);
                            error = "Taille de texture invalide : " + name;
                            return false;
                        }

                        byte[] bytes = new byte[stream.Length];
                        int read = 0;

                        while (read < bytes.Length)
                        {
                            int count = stream.Read(
                                bytes, read, bytes.Length - read);

                            if (count <= 0)
                                break;

                            read += count;
                        }

                        Texture2D created = new Texture2D(
                            2, 2, TextureFormat.RGBA32, true);

                        if (!ImageConversion.LoadImage(created, bytes, false))
                        {
                            UnityEngine.Object.Destroy(created);
                            Failed.Add(name);
                            error = "Décodage PNG impossible : " + name;
                            return false;
                        }

                        created.name = "FusionDarkRP_" + name;
                        created.wrapMode = TextureWrapMode.Repeat;
                        created.filterMode = FilterMode.Trilinear;
                        created.anisoLevel = 4;

                        Cache[name] = created;
                        texture = created;
                        return true;
                    }
                }
                catch (Exception exception)
                {
                    Failed.Add(name);
                    error = "Chargement texture " + name + " : "
                        + exception.Message;
                    return false;
                }
            }
        }

        public static void Cleanup()
        {
            lock (Sync)
            {
                foreach (Texture2D texture in Cache.Values)
                {
                    if (texture != null)
                        UnityEngine.Object.Destroy(texture);
                }

                Cache.Clear();
                Failed.Clear();
            }
        }
    }
}
