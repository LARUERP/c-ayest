using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Il2CppSLZ.Marrow.Data;
using Il2CppSLZ.Marrow.Warehouse;
using LabFusion.Entities;
using LabFusion.Network;
using LabFusion.RPC;
using LabFusion.Utilities;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Fait apparaître de VRAIS objets de BONELAB (armes, véhicules...) en
    /// les cherchant par mot dans le titre des crates réellement chargées :
    /// aucun code-barres supposé. L'hôte mémorise ce qu'il a donné à chaque
    /// joueur pour le retirer à la débauche.
    /// </summary>
    public static class GameItemService
    {
        // Mots exclus : chargeurs, munitions, caisses...
        private static readonly HashSet<string> Excluded =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "mag", "magazine", "ammo", "clip", "box", "bullets",
                "rounds", "crate", "case", "holster", "attachment"
            };

        private static readonly Dictionary<string, List<ushort>> Owned =
            new Dictionary<string, List<ushort>>(StringComparer.Ordinal);

        private static bool titlesDumped;

        private static string[] Words(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return new string[0];

            StringBuilder cleaned = new StringBuilder();

            foreach (char c in title)
                cleaned.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');

            return cleaned.ToString().Split(
                new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string Normalize(string title)
        {
            return string.Concat(Words(title));
        }

        /// <summary>
        /// Premier mot-clé qui correspond à une crate. wholeWord : le mot
        /// doit être EXACTEMENT un mot du titre (véhicules : « car » ne doit
        /// pas trouver « carbine »). Sinon : le titre normalisé contient le
        /// mot-clé (armes : « mp5 », « m1911 »...).
        /// </summary>
        public static bool TryFind(
            string[] keywords, bool wholeWord,
            out Spawnable spawnable, out string title)
        {
            spawnable = null;
            title = null;

            try
            {
                if (keywords == null || !AssetWarehouse.ready ||
                    AssetWarehouse.Instance == null)
                {
                    return false;
                }

                var crates = AssetWarehouse.Instance.GetCrates<SpawnableCrate>();

                if (crates == null)
                    return false;

                DumpTitles();

                foreach (string keyword in keywords)
                {
                    string wanted = Normalize(keyword);

                    if (wanted.Length == 0)
                        continue;

                    foreach (SpawnableCrate crate in crates)
                    {
                        if (crate == null || crate.Barcode == null ||
                            string.IsNullOrWhiteSpace(crate.Barcode.ID))
                        {
                            continue;
                        }

                        string[] words = Words(crate.Title);
                        bool excluded = false;
                        bool match = false;

                        foreach (string word in words)
                        {
                            if (Excluded.Contains(word))
                                excluded = true;

                            if (wholeWord && word == wanted)
                                match = true;
                        }

                        if (!wholeWord)
                            match = Normalize(crate.Title).Contains(wanted);

                        if (excluded || !match)
                            continue;

                        SpawnableCrateReference reference =
                            new SpawnableCrateReference(crate.Barcode.ID);

                        if (reference.Crate == null)
                            continue;

                        spawnable = new Spawnable
                        {
                            crateRef = reference,
                            policyData = null
                        };

                        title = crate.Title;
                        return true;
                    }
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Items] Recherche : " + exception.Message);
            }

            return false;
        }

        /// <summary>
        /// Écrit la liste des objets chargés dans
        /// UserData/FusionDarkRP/spawnable-titles.txt (aide au réglage des
        /// mots-clés d'armes et de véhicules).
        /// </summary>
        private static void DumpTitles()
        {
            if (titlesDumped)
                return;

            titlesDumped = true;

            try
            {
                var crates = AssetWarehouse.Instance.GetCrates<SpawnableCrate>();

                string folder = Path.Combine(
                    MelonEnvironment.UserDataDirectory, "FusionDarkRP");

                Directory.CreateDirectory(folder);

                StringBuilder text = new StringBuilder();

                foreach (SpawnableCrate crate in crates)
                {
                    if (crate != null)
                        text.AppendLine(crate.Title);
                }

                File.WriteAllText(
                    Path.Combine(folder, "spawnable-titles.txt"),
                    text.ToString());
            }
            catch
            {
                // Aide facultative.
            }
        }

        /// <summary>
        /// Vrai si la crate de ce code-barres est chargée dans BONELAB (sans rien créer).
        /// </summary>
        public static bool CanSpawn(string barcode)
        {
            Spawnable spawnable;

            return TryResolveBarcode(barcode, out spawnable);
        }

        /// <summary>Résout un code-barres EXACT (véhicules du catalogue dynamique).</summary>
        public static bool TryResolveBarcode(string barcode, out Spawnable spawnable)
        {
            spawnable = null;

            try
            {
                if (string.IsNullOrWhiteSpace(barcode) || !AssetWarehouse.ready ||
                    AssetWarehouse.Instance == null)
                {
                    return false;
                }

                SpawnableCrateReference reference = new SpawnableCrateReference(barcode);

                if (reference.Crate == null)
                    return false;

                spawnable = new Spawnable
                {
                    crateRef = reference,
                    policyData = null
                };

                return true;
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Items] Code-barres : " + exception.Message);

                return false;
            }
        }

        /// <summary>
        /// Crée l'objet devant le joueur. bucket : groupe de suivi
        /// (ex. "pid|kit", "pid|veh") pour le retirer plus tard.
        /// </summary>
        public static bool TryGive(
            string bucket, string[] keywords, bool wholeWord,
            Vector3 position, Quaternion rotation, out string title)
        {
            title = null;

            Spawnable spawnable;

            if (!TryFind(keywords, wholeWord, out spawnable, out title))
                return false;

            return SpawnTracked(bucket, spawnable, position, rotation);
        }

        /// <summary>Crée l'objet désigné par un code-barres exact (véhicules vendus).</summary>
        public static bool TryGiveBarcode(
            string bucket, string barcode,
            Vector3 position, Quaternion rotation)
        {
            Spawnable spawnable;

            if (!TryResolveBarcode(barcode, out spawnable))
                return false;

            return SpawnTracked(bucket, spawnable, position, rotation);
        }

        private static bool SpawnTracked(
            string bucket, Spawnable spawnable,
            Vector3 position, Quaternion rotation)
        {
            try
            {
                string tracked = bucket;

                NetworkAssetSpawner.Spawn(
                    new NetworkAssetSpawner.SpawnRequestInfo
                    {
                        Spawnable = spawnable,
                        Position = position,
                        Rotation = rotation,
                        SpawnEffect = false,
                        SpawnSource = EntitySource.Player,
                        SpawnCallback = info =>
                        {
                            try
                            {
                                if (info.Entity == null)
                                    return;

                                lock (Owned)
                                {
                                    List<ushort> list;

                                    if (!Owned.TryGetValue(tracked, out list))
                                    {
                                        list = new List<ushort>();
                                        Owned[tracked] = list;
                                    }

                                    list.Add(info.Entity.ID);
                                }
                            }
                            catch (Exception exception)
                            {
                                MelonLogger.Warning(
                                    "[FusionDarkRP Items] Suivi : "
                                    + exception.Message);
                            }
                        }
                    });

                return true;
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Items] Création : " + exception.Message);

                return false;
            }
        }

        /// <summary>Retire tout ce qui a été donné dans ce groupe.</summary>
        public static int Release(string bucket)
        {
            List<ushort> list;

            lock (Owned)
            {
                if (!Owned.TryGetValue(bucket, out list))
                    return 0;

                Owned.Remove(bucket);
            }

            int removed = 0;

            foreach (ushort id in list)
            {
                try
                {
                    NetworkAssetSpawner.Despawn(
                        new NetworkAssetSpawner.DespawnRequestInfo
                        {
                            EntityID = id,
                            DespawnEffect = false
                        });

                    removed++;
                }
                catch
                {
                    // Déjà disparu.
                }
            }

            return removed;
        }

        public static void ReleaseAll()
        {
            List<string> buckets;

            lock (Owned)
                buckets = new List<string>(Owned.Keys);

            foreach (string bucket in buckets)
                Release(bucket);
        }
    }
}
