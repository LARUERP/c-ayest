
using System;
using LabFusion.Player;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Identifie durablement un joueur avec son PlatformID LabFusion.
    /// SmallID n'est jamais utilisé pour les sauvegardes.
    /// </summary>
    public static class PlayerIdentityService
    {
        /// <summary>
        /// Construit l'identifiant persistant depuis un PlayerID valide.
        /// </summary>
        public static bool TryGetPersistentId(
            PlayerID player,
            out string persistentId)
        {
            persistentId = null;

            if (player == null || !player.IsValid)
                return false;

            // Steam : SteamID64 (identique aux anciennes sauvegardes).
            // EOS   : identifiant Epic (texte) tel que fourni par LabFusion.
            string raw = PlatformIds.RawOf(player);

            if (string.IsNullOrWhiteSpace(raw) || raw == "0")
                return false;

            persistentId = "labfusion:" + raw;

            return true;
        }

        /// <summary>
        /// Variante compatible avec les appels utilisant object.
        /// </summary>
        public static bool TryGetPersistentId(
            object player,
            object playerId,
            out string persistentId)
        {
            persistentId = null;

            PlayerID identity = playerId as PlayerID;

            if (identity == null)
                identity = player as PlayerID;

            return TryGetPersistentId(identity, out persistentId);
        }

        /// <summary>
        /// Retourne l'identifiant ou refuse l'opération.
        /// </summary>
        public static string GetPersistentIdOrThrow(
            PlayerID player)
        {
            string persistentId;

            if (!TryGetPersistentId(player, out persistentId))
            {
                throw new InvalidOperationException(
                    "Impossible d'obtenir le PlatformID LabFusion. "
                    + "Sauvegarde du joueur refusee."
                );
            }

            return persistentId;
        }

        /// <summary>
        /// Variante compatible avec les anciens appels object.
        /// </summary>
        public static string GetPersistentIdOrThrow(
            object player,
            object playerId)
        {
            string persistentId;

            if (!TryGetPersistentId(
                player,
                playerId,
                out persistentId))
            {
                throw new InvalidOperationException(
                    "Aucun PlayerID LabFusion valide. "
                    + "Sauvegarde du joueur refusee."
                );
            }

            return persistentId;
        }
    }
}
