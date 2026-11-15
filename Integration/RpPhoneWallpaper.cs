using System;
using System.IO;
using System.Reflection;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Fond d'écran par défaut du téléphone : l'image « LA RUE RP »
    /// intégrée à la DLL (utilisée tant qu'aucune photo n'est choisie).
    /// </summary>
    public static class RpPhoneWallpaper
    {
        private const string ResourceName =
            "FusionDarkRP.Assets.PhoneWallpaper.png";

        private static Texture2D texture;
        private static bool failed;

        public static Texture2D Get()
        {
            if (texture != null)
                return texture;

            if (failed && texture == null && loadedOnce == false)
                return null;

            try
            {
                using (Stream stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream(ResourceName))
                {
                    if (stream == null)
                    {
                        failed = true;

                        MelonLogger.Warning(
                            "[FusionDarkRP Phone] Fond d'écran absent : "
                            + ResourceName);

                        return null;
                    }

                    using (MemoryStream memory = new MemoryStream())
                    {
                        stream.CopyTo(memory);

                        Texture2D created = new Texture2D(
                            2, 2, TextureFormat.RGBA32, false);

                        if (!ImageConversion.LoadImage(
                                created, memory.ToArray(), false))
                        {
                            failed = true;
                            UnityEngine.Object.Destroy(created);
                            return null;
                        }

                        created.name = "FusionDarkRP_PhoneWallpaper";
                        created.wrapMode = TextureWrapMode.Clamp;
                        created.filterMode = FilterMode.Bilinear;

                        texture = created;
                        loadedOnce = true;

                        return texture;
                    }
                }
            }
            catch (Exception exception)
            {
                failed = true;

                MelonLogger.Warning(
                    "[FusionDarkRP Phone] Fond d'écran : "
                    + exception.Message);

                return null;
            }
        }

        private static bool loadedOnce;
    }
}
