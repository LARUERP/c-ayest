using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using LabFusion.Player;
using MelonLoader;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Couche de compatibilité des identifiants de plateforme.
    ///
    /// LabFusion Steam expose PlayerID.PlatformID en ulong (SteamID64).
    /// LabFusion EOS l'expose en string (identifiant Epic / ProductUserId).
    /// Le mod manipule des identifiants ulong partout (téléphone, banque,
    /// réseau). Cette classe lit la valeur par réflexion (donc compatible
    /// avec les deux versions sans recompiler) et la convertit en ulong :
    ///  - Steam : la valeur d'origine est conservée telle quelle ;
    ///  - EOS   : empreinte FNV-1a 64 bits de la chaîne, déterministe, donc
    ///            identique chez tous les joueurs et à chaque session.
    /// Une table inverse permet de retrouver le PlayerID et la chaîne
    /// d'origine (sauvegardes) depuis l'empreinte.
    /// </summary>
    public static class PlatformIds
    {
        private static readonly PropertyInfo PlayerProperty =
            typeof(PlayerID).GetProperty(
                "PlatformID",
                BindingFlags.Public | BindingFlags.Instance);

        private static readonly PropertyInfo LocalProperty =
            typeof(PlayerIDManager).GetProperty(
                "LocalPlatformID",
                BindingFlags.Public | BindingFlags.Static);

        private static readonly ConditionalWeakTable<PlayerID, object> Cache =
            new ConditionalWeakTable<PlayerID, object>();

        private static readonly Dictionary<ulong, string> RawById =
            new Dictionary<ulong, string>();

        private static readonly object Sync = new object();

        private static ulong localId;
        private static bool warned;

        /// <summary>
        /// True si LabFusion expose les identifiants sous forme de texte (EOS).
        /// </summary>
        public static bool IsTextPlatform
        {
            get
            {
                return PlayerProperty != null &&
                       PlayerProperty.PropertyType == typeof(string);
            }
        }

        /// <summary>
        /// Identifiant ulong stable du joueur (0 si inconnu).
        /// </summary>
        public static ulong Of(PlayerID player)
        {
            if (player == null)
                return 0UL;

            object boxed;

            if (Cache.TryGetValue(player, out boxed) && boxed is ulong)
                return (ulong)boxed;

            ulong id = ToId(ReadPlayer(player));

            if (id != 0UL)
                Cache.AddOrUpdate(player, id);

            return id;
        }

        /// <summary>
        /// Identifiant ulong du joueur local (0 tant que LabFusion n'est pas prêt).
        /// </summary>
        public static ulong Local
        {
            get
            {
                if (localId != 0UL)
                    return localId;

                ulong id = ToId(ReadLocal());

                if (id != 0UL)
                    localId = id;

                return id;
            }
        }

        /// <summary>
        /// Texte d'origine (SteamID64 ou identifiant Epic) d'un joueur,
        /// utilisable pour les clés de sauvegarde. Null si inconnu.
        /// </summary>
        public static string RawOf(PlayerID player)
        {
            if (player == null)
                return null;

            string raw = ToText(ReadPlayer(player));

            if (!string.IsNullOrEmpty(raw))
                ToId(raw);

            return string.IsNullOrEmpty(raw) ? null : raw;
        }

        /// <summary>
        /// Retrouve le texte d'origine d'une empreinte déjà rencontrée.
        /// </summary>
        public static string RawFor(ulong id)
        {
            lock (Sync)
            {
                string raw;

                return RawById.TryGetValue(id, out raw)
                    ? raw
                    : id.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Remplace PlayerIDManager.GetPlayerID(ulong).
        /// </summary>
        public static PlayerID GetPlayerID(ulong id)
        {
            if (id == 0UL)
                return null;

            try
            {
                PlayerID local = PlayerIDManager.LocalID;

                if (local != null && Of(local) == id)
                    return local;

                foreach (PlayerID player in PlayerIDManager.PlayerIDs)
                {
                    if (player != null && Of(player) == id)
                        return player;
                }
            }
            catch (Exception exception)
            {
                Warn(exception);
            }

            return null;
        }

        /// <summary>
        /// Remplace PlayerIDManager.GetPlayerID(byte) (SmallID).
        /// </summary>
        public static PlayerID GetPlayerID(byte smallId)
        {
            return PlayerIDManager.GetPlayerID(smallId);
        }

        // ------------------------------------------------------------
        // Interne
        // ------------------------------------------------------------

        private static object ReadPlayer(PlayerID player)
        {
            try
            {
                return PlayerProperty != null
                    ? PlayerProperty.GetValue(player, null)
                    : null;
            }
            catch (Exception exception)
            {
                Warn(exception);
                return null;
            }
        }

        private static object ReadLocal()
        {
            try
            {
                return LocalProperty != null
                    ? LocalProperty.GetValue(null, null)
                    : null;
            }
            catch (Exception exception)
            {
                Warn(exception);
                return null;
            }
        }

        private static string ToText(object value)
        {
            if (value == null)
                return null;

            string text = value as string;

            if (text != null)
                return text.Trim();

            IFormattable formattable = value as IFormattable;

            return formattable != null
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value.ToString();
        }

        private static ulong ToId(object value)
        {
            if (value == null)
                return 0UL;

            if (value is ulong)
                return (ulong)value;

            string text = ToText(value);

            if (string.IsNullOrEmpty(text))
                return 0UL;

            return ToId(text);
        }

        private static ulong ToId(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0UL;

            ulong id;

            if (!IsTextPlatform &&
                ulong.TryParse(
                    text,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out id))
            {
                return id;
            }

            id = Fnv1a(text);

            lock (Sync)
                RawById[id] = text;

            return id;
        }

        private static ulong Fnv1a(string text)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            ulong hash = offset;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                hash ^= (byte)(c & 0xFF);
                hash *= prime;
                hash ^= (byte)((c >> 8) & 0xFF);
                hash *= prime;
            }

            return hash == 0UL ? 1UL : hash;
        }

        private static void Warn(Exception exception)
        {
            if (warned)
                return;

            warned = true;

            MelonLogger.Warning(
                "[FusionDarkRP Platform] Lecture de l'identifiant joueur : "
                + exception.Message);
        }
    }
}
