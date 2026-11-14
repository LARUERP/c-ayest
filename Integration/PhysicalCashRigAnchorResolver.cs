
using System;
using LabFusion.Entities;
using LabFusion.Player;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Trouve le rig, l'avant-bras gauche et la racine
    /// réelle d'un joueur LabFusion.
    /// </summary>
    public static class PhysicalCashRigAnchorResolver
    {
        public static Transform Resolve(PlayerID player)
        {
            Transform forearm = ResolveLeftForearm(player);

            if (forearm != null)
                return forearm;

            return ResolveRigRoot(player);
        }

        /// <summary>
        /// Retourne la racine réelle du rig du joueur.
        /// Ne retourne jamais un os de bras à la place du rig.
        /// </summary>
        public static Transform ResolveRigRoot(PlayerID player)
        {
            if (player == null || !player.IsValid)
                return null;

            // Joueur local : le rig vient de BoneLib (le joueur local n'est
            // pas toujours listé dans NetworkPlayer.Players avec LabFusion EOS).
            if (player.IsMe)
            {
                try
                {
                    var localRig = BoneLib.Player.RigManager;

                    if (localRig != null && localRig.transform != null)
                        return localRig.transform;
                }
                catch
                {
                    // Repli sur la recherche réseau ci-dessous.
                }
            }

            ulong platformId = PlatformIds.Of(player);

            if (platformId == 0)
                return null;

            try
            {
                foreach (NetworkPlayer networkPlayer in NetworkPlayer.Players)
                {
                    if (networkPlayer == null)
                        continue;

                    PlayerID registeredPlayer = networkPlayer.PlayerID;

                    if (registeredPlayer == null ||
                        !registeredPlayer.IsValid)
                        continue;

                    if (PlatformIds.Of(registeredPlayer) != platformId)
                        continue;

                    if (!networkPlayer.HasRig ||
                        networkPlayer.RigRefs == null)
                        return null;

                    var rigManager = networkPlayer.RigRefs.RigManager;

                    if (rigManager == null)
                        return null;

                    return rigManager.transform;
                }
            }
            catch (Exception exception)
            {
                MelonLoader.MelonLogger.Warning(
                    "[FusionDarkRP Rig] Résolution de la racine impossible : "
                    + exception.Message);
            }

            return null;
        }

        private static bool loggedMissingForearm;
        private static Transform cachedLocalRoot;
        private static Transform cachedLocalForearm;

        public static Transform ResolveLeftForearm(PlayerID player)
        {
            Transform rigRoot = ResolveRigRoot(player);

            if (rigRoot == null)
                return null;

            // Évite de reparcourir toute la hiérarchie du rig à chaque appel.
            if (player != null && player.IsMe &&
                cachedLocalRoot == rigRoot &&
                cachedLocalForearm != null)
            {
                return cachedLocalForearm;
            }

            Transform found = FindLeftForearm(rigRoot);

            if (player != null && player.IsMe)
            {
                cachedLocalRoot = rigRoot;
                cachedLocalForearm = found;
            }

            return found;
        }

        private static Transform FindLeftForearm(Transform rigRoot)
        {
            try
            {
                Transform[] transforms =
                    rigRoot.GetComponentsInChildren<Transform>(true);

                if (transforms == null)
                    return null;

                string[] preferredNames =
                {
                    "leftlowerarm",
                    "leftforearm",
                    "mixamorig:leftforearm",
                    "armature_leftforearm",
                    "left_lower_arm",
                    "forearm_l",
                    "lowerarm_l"
                };

                foreach (string preferred in preferredNames)
                {
                    foreach (Transform candidate in transforms)
                    {
                        if (candidate == null)
                            continue;

                        string candidateName =
                            Normalize(candidate.name);

                        if (candidateName == Normalize(preferred))
                            return candidate;
                    }
                }

                foreach (Transform candidate in transforms)
                {
                    if (candidate == null)
                        continue;

                    string name = Normalize(candidate.name);

                    bool isLeft =
                        name.Contains("left") ||
                        name.EndsWith("_l") ||
                        name.StartsWith("l_");

                    bool isForearm =
                        name.Contains("forearm") ||
                        name.Contains("lowerarm") ||
                        name.Contains("lower_arm");

                    if (isLeft && isForearm)
                        return candidate;
                }
            }
            catch (Exception exception)
            {
                MelonLoader.MelonLogger.Warning(
                    "[FusionDarkRP Rig] Recherche avant-bras impossible : "
                    + exception.Message);
            }

            // Repli : le parent de la main gauche physique est l'avant-bras.
            try
            {
                var leftHand = BoneLib.Player.LeftHand;

                if (leftHand != null &&
                    leftHand.transform != null &&
                    leftHand.transform.parent != null &&
                    leftHand.transform.IsChildOf(rigRoot))
                {
                    return leftHand.transform.parent;
                }
            }
            catch
            {
                // Journal ci-dessous.
            }

            if (!loggedMissingForearm)
            {
                loggedMissingForearm = true;

                try
                {
                    System.Text.StringBuilder names =
                        new System.Text.StringBuilder();

                    int count = 0;

                    foreach (Transform t in
                        rigRoot.GetComponentsInChildren<Transform>(true))
                    {
                        if (t == null || count >= 40)
                            break;

                        string n = Normalize(t.name);

                        if (n.Contains("arm") || n.Contains("hand") ||
                            n.Contains("wrist") || n.Contains("elbow"))
                        {
                            names.Append(t.name).Append(" | ");
                            count++;
                        }
                    }

                    MelonLoader.MelonLogger.Warning(
                        "[FusionDarkRP Rig] Avant-bras gauche introuvable. "
                        + "Os du bras : " + names);
                }
                catch
                {
                    // Diagnostic seulement.
                }
            }

            return null;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .ToLowerInvariant()
                .Replace(" ", "")
                .Replace("-", "")
                .Replace("_", "")
                .Replace(":", "");
        }
    }
}
