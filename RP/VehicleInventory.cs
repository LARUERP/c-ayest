using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using FusionDarkRP.Integration;
using Il2CppSLZ.Marrow.Warehouse;
using LabFusion.Network;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace FusionDarkRP.RP
{
    /// <summary>Catégories d'affichage du concessionnaire.</summary>
    public static class VehicleCategory
    {
        public const string Cars = "cars";
        public const string TwoWheels = "bikes";
        public const string Utility = "utility";
        public const string Other = "other";

        public static readonly string[] All = { Cars, TwoWheels, Utility, Other };

        public static string Label(string category)
        {
            switch (category)
            {
                case Cars: return "Voitures";
                case TwoWheels: return "Deux-roues";
                case Utility: return "Utilitaires et camions";
                default: return "Autres";
            }
        }
    }

    /// <summary>Un véhicule réellement installé dans le BONELAB de l'hôte.</summary>
    public sealed class VehicleDefinition
    {
        /// <summary>Identifiant court (hash du code-barres), utilisé dans les menus et factures.</summary>
        public string Id { get; set; }
        public string Barcode { get; set; }
        public string Title { get; set; }
        public string Name { get; set; }
        public string Pallet { get; set; }
        /// <summary>« BONELAB » (jeu de base) ou « Mod » (mod.io / dossier Mods).</summary>
        public string Source { get; set; }
        public string Category { get; set; }
        public long PriceCents { get; set; }
        public string Reason { get; set; }
        /// <summary>Distance d'apparition devant l'acheteur (m) : plus grande pour les gros véhicules.</summary>
        public float SpawnDistance { get; set; }
    }

    public sealed class VehicleCategoryInfo
    {
        public string K { get; set; }
        public string N { get; set; }
        public int C { get; set; }
    }

    public sealed class VehicleItemInfo
    {
        public string I { get; set; }
        public string N { get; set; }
        public long P { get; set; }
        public string S { get; set; }
    }

    /// <summary>Page du catalogue envoyée par l'hôte au concessionnaire (reste sous 6 Ko).</summary>
    public sealed class VehicleListData
    {
        public int Target { get; set; }
        public string Cat { get; set; }
        public int Page { get; set; }
        public int Pages { get; set; }
        public int Total { get; set; }
        public List<VehicleCategoryInfo> Cats { get; set; }
        public List<VehicleItemInfo> Items { get; set; }
    }

    public sealed class VehicleOverride
    {
        /// <summary>Texte cherché (sans casse) dans le titre normalisé OU le code-barres.</summary>
        public string Match { get; set; }
        public string Name { get; set; }
        /// <summary>0 = prix automatique.</summary>
        public long PriceCents { get; set; }
        /// <summary>cars, bikes, utility ou other ; vide = automatique.</summary>
        public string Category { get; set; }
    }

    /// <summary>
    /// Catalogue DYNAMIQUE des véhicules : lu dans l'AssetWarehouse de BONELAB
    /// (jeu de base + mods mod.io installés chez l'hôte). Aucun nom n'est supposé :
    /// ce qui est installé est vendable, ce qui ne l'est pas n'apparaît pas.
    /// Un véhicule téléchargé en cours de partie est détecté automatiquement
    /// (relecture toutes les 60 s, ou bouton « Actualiser » du concessionnaire).
    ///
    /// Reconnaissance : tag de la crate (Vehicle, Car, Bike...) OU mot du titre
    /// (car, truck, clio, tmax...). Réglable sans recompiler dans
    /// UserData/FusionDarkRP/vehicles.json (Include / Exclude / Overrides).
    /// </summary>
    public static class VehicleInventory
    {
        public const int CatalogVersion = 4;
        public const int PageSize = 10;

        private const float PollSeconds = 60f;
        private const float MinimumForcedGap = 2f;
        private const long MinimumPriceCents = 10000L;

        private static readonly object Sync = new object();
        private static readonly List<VehicleDefinition> Vehicles = new List<VehicleDefinition>();
        private static readonly Dictionary<string, VehicleDefinition> ById =
            new Dictionary<string, VehicleDefinition>(StringComparer.Ordinal);

        private static long signature = -1;
        private static bool scannedOnce;
        private static float lastScan = -100f;
        private static float nextPoll;

        // ------------------------------------------------------------
        // Vocabulaire de reconnaissance
        // ------------------------------------------------------------

        private static HashSet<string> Set(params string[] words)
        {
            return new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);
        }

        private static readonly HashSet<string> VehicleTagWords = Set(
            "vehicle", "vehicles", "vehicule", "vehicules", "car", "cars", "automobile",
            "motorcycle", "motorbike", "bike", "bikes", "scooter", "truck", "trucks",
            "van", "bus", "kart", "buggy", "jeep");

        private static readonly HashSet<string> ExcludedTagWords = Set(
            "avatar", "avatars", "npc", "weapon", "weapons", "gun", "guns", "ammo",
            "level", "levels", "attachment", "attachments", "magazine");

        private static readonly HashSet<string> VehicleTitleWords = Set(
            // génériques
            "car", "cars", "truck", "van", "bus", "jeep", "suv", "sedan", "coupe",
            "hatchback", "pickup", "taxi", "limo", "limousine", "ambulance", "firetruck",
            "vehicle", "tractor", "jalopy", "buggy", "kart", "gokart", "quad", "atv",
            "bike", "bicycle", "bmx", "motorcycle", "motorbike", "moped", "scooter",
            // modèles et marques courants
            "clio", "twingo", "megane", "captur", "kangoo", "trafic", "master", "berlingo",
            "ducato", "transit", "sprinter", "tmax", "nmax", "xmax", "vespa", "golf", "polo",
            "208", "308", "206", "207", "peugeot", "renault", "citroen", "yamaha", "ducati",
            "harley", "kawasaki", "suzuki", "bmw", "audi", "mercedes", "tesla", "ferrari",
            "lamborghini", "lambo", "porsche", "mclaren", "bugatti", "mustang", "camaro",
            "corvette", "civic", "corolla", "yaris", "toyota", "nissan", "skyline", "supra",
            "volkswagen", "fiat", "opel", "skoda", "dacia");

        // Mots qui indiquent une PIÈCE ou un accessoire, pas un véhicule roulant.
        private static readonly HashSet<string> ExcludedTitleWords = Set(
            "key", "keys", "tire", "tyre", "tires", "tyres", "wheel", "wheels", "engine",
            "part", "parts", "poster", "sign", "toy", "toys", "miniature", "decal", "skin",
            "avatar", "npc", "gun", "rifle", "pistol", "mag", "magazine", "ammo", "clip",
            "holster", "remote", "controller", "club", "ball", "trailer", "spawner",
            "door", "hood", "bumper", "mirror", "horn", "battery", "wreck", "wrecked");

        private static readonly HashSet<string> BikeWords = Set(
            "bike", "bicycle", "bmx", "motorcycle", "motorbike", "moped", "scooter", "quad",
            "atv", "tmax", "nmax", "xmax", "vespa", "ducati", "harley", "kawasaki", "yamaha");

        private static readonly HashSet<string> MotorWords = Set(
            "motorcycle", "motorbike", "moped", "scooter", "quad", "atv", "tmax", "nmax",
            "xmax", "vespa", "ducati", "harley", "kawasaki", "yamaha", "moto", "motor");

        private static readonly HashSet<string> UtilityWords = Set(
            "truck", "trucks", "van", "bus", "pickup", "tractor", "ambulance", "firetruck",
            "kangoo", "trafic", "master", "berlingo", "ducato", "transit", "sprinter");

        private static readonly HashSet<string> OtherWords = Set("kart", "gokart", "buggy");

        // ------------------------------------------------------------
        // Réglages (vehicles.json)
        // ------------------------------------------------------------

        private sealed class SettingsFile
        {
            public int Version { get; set; } = CatalogVersion;
            public string Notice { get; set; }
            /// <summary>Multiplie tous les prix automatiques (1.0 = tarifs par défaut).</summary>
            public double PriceMultiplier { get; set; } = 1.0;
            /// <summary>Prix d'un véhicule inconnu en centimes ; 0 = tarif automatique.</summary>
            public long DefaultPriceCents { get; set; }
            public bool AutoDetect { get; set; } = true;
            /// <summary>Titres ou codes-barres à FORCER au catalogue (extrait suffisant).</summary>
            public List<string> Include { get; set; } = new List<string>();
            /// <summary>Titres ou codes-barres à RETIRER du catalogue (extrait suffisant).</summary>
            public List<string> Exclude { get; set; } = new List<string>();
            public List<VehicleOverride> Overrides { get; set; } = new List<VehicleOverride>();
        }

        private static string Folder()
        {
            return Path.Combine(MelonEnvironment.UserDataDirectory, "FusionDarkRP");
        }

        private static string SettingsPath()
        {
            return Path.Combine(Folder(), "vehicles.json");
        }

        private static JsonSerializerOptions JsonOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
        }

        private static SettingsFile NewDefaults()
        {
            return new SettingsFile
            {
                Notice = "Les vehicules sont detectes automatiquement dans BONELAB (jeu + mods). "
                    + "Include : forcer un vehicule non reconnu (extrait du titre ou du code-barres, voir "
                    + "vehicles-detected.txt et spawnable-catalog.txt). Exclude : retirer un vehicule. "
                    + "Overrides : changer nom, prix (centimes) ou categorie (cars, bikes, utility, other). "
                    + "Apres modification, bouton Actualiser du concessionnaire."
            };
        }

        private static SettingsFile LoadSettings()
        {
            string path = SettingsPath();
            JsonSerializerOptions options = JsonOptions();

            try
            {
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path);

                    // Anciens formats : tableau (5.1.0) ou Version < 4 (liste figée de modèles).
                    bool legacy = text.TrimStart().StartsWith("[");
                    SettingsFile file = null;

                    if (!legacy)
                        file = JsonSerializer.Deserialize<SettingsFile>(text, options);

                    if (legacy || file == null || file.Version < CatalogVersion)
                    {
                        File.WriteAllText(path + ".ancien.bak", text);

                        SettingsFile fresh = NewDefaults();
                        File.WriteAllText(path, JsonSerializer.Serialize(fresh, options));

                        MelonLogger.Warning("[FusionDarkRP Véhicules] Ancien vehicles.json sauvegardé (.ancien.bak) : "
                            + "le catalogue est maintenant détecté automatiquement.");

                        return fresh;
                    }

                    if (file.Include == null) file.Include = new List<string>();
                    if (file.Exclude == null) file.Exclude = new List<string>();
                    if (file.Overrides == null) file.Overrides = new List<VehicleOverride>();
                    if (file.PriceMultiplier <= 0.0) file.PriceMultiplier = 1.0;

                    return file;
                }

                Directory.CreateDirectory(Folder());
                SettingsFile created = NewDefaults();
                File.WriteAllText(path, JsonSerializer.Serialize(created, options));
                return created;
            }
            catch (Exception exception)
            {
                MelonLogger.Warning("[FusionDarkRP Véhicules] vehicles.json illisible (valeurs par défaut) : "
                    + exception.Message);

                return NewDefaults();
            }
        }

        // ------------------------------------------------------------
        // Texte
        // ------------------------------------------------------------

        private static string[] Words(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new string[0];

            StringBuilder cleaned = new StringBuilder(text.Length);

            foreach (char c in text)
                cleaned.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');

            return cleaned.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string Squash(string text)
        {
            return string.Concat(Words(text));
        }

        private static bool AnyWord(string[] words, HashSet<string> set)
        {
            foreach (string word in words)
            {
                if (set.Contains(word))
                    return true;
            }

            return false;
        }

        private static bool Matches(string needle, string squashedTitle, string barcode)
        {
            string wanted = Squash(needle);

            if (wanted.Length == 0)
                return false;

            if (squashedTitle.Contains(wanted))
                return true;

            return barcode != null &&
                barcode.IndexOf(needle.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool MatchesAny(List<string> needles, string squashedTitle, string barcode)
        {
            if (needles == null)
                return false;

            foreach (string needle in needles)
            {
                if (!string.IsNullOrWhiteSpace(needle) && Matches(needle, squashedTitle, barcode))
                    return true;
            }

            return false;
        }

        private static string[] ReadTags(SpawnableCrate crate)
        {
            try
            {
                var tags = crate.Tags;

                if (tags == null)
                    return new string[0];

                int count = tags.Count;
                List<string> result = new List<string>(count);

                for (int i = 0; i < count; i++)
                {
                    string tag = tags[i];

                    if (!string.IsNullOrWhiteSpace(tag))
                        result.Add(tag.Trim());
                }

                return result.ToArray();
            }
            catch
            {
                return new string[0];
            }
        }

        private static string ShortHash(string text)
        {
            // FNV-1a 32 bits : stable d'une session à l'autre, sans dépendance.
            uint hash = 2166136261u;

            foreach (char c in text.ToLowerInvariant())
            {
                hash ^= c;
                hash *= 16777619u;
            }

            return hash.ToString("x8");
        }

        private static string Clean(string text, int max)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            StringBuilder builder = new StringBuilder(text.Length);

            foreach (char c in text)
                builder.Append(char.IsControl(c) ? ' ' : c);

            string result = builder.ToString().Trim();

            return result.Length > max ? result.Substring(0, max).TrimEnd() : result;
        }

        // ------------------------------------------------------------
        // Classification
        // ------------------------------------------------------------

        private static string CategoryOf(string[] words, string[] tagWords)
        {
            if (AnyWord(words, BikeWords) || AnyWord(tagWords, BikeWords))
                return VehicleCategory.TwoWheels;

            if (AnyWord(words, UtilityWords) || AnyWord(tagWords, UtilityWords))
                return VehicleCategory.Utility;

            if (AnyWord(words, OtherWords) || AnyWord(tagWords, OtherWords))
                return VehicleCategory.Other;

            return VehicleCategory.Cars;
        }

        private static long AutoPrice(string[] words, string category)
        {
            HashSet<string> w = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);

            if (w.Contains("bicycle") || w.Contains("bmx"))
                return 15000;

            if (w.Contains("bike") && !AnyWord(words, MotorWords))
                return 15000;

            if (w.Contains("scooter") || w.Contains("moped") || w.Contains("nmax") || w.Contains("vespa"))
                return 70000;

            if (w.Contains("tmax") || w.Contains("xmax"))
                return 260000;

            if (AnyWord(words, MotorWords))
                return w.Contains("quad") || w.Contains("atv") ? 200000 : 260000;

            if (w.Contains("twingo")) return 150000;
            if (w.Contains("clio")) return 200000;
            if (w.Contains("208") || w.Contains("207") || w.Contains("206")) return 220000;
            if (w.Contains("golf") || w.Contains("polo")) return 300000;

            if (w.Contains("ferrari") || w.Contains("lamborghini") || w.Contains("lambo") ||
                w.Contains("porsche") || w.Contains("mclaren") || w.Contains("bugatti"))
                return 900000;

            if (w.Contains("bmw") || w.Contains("audi") || w.Contains("mercedes") || w.Contains("tesla"))
                return 500000;

            if (category == VehicleCategory.Other)
                return 80000;

            if (w.Contains("bus"))
                return 600000;

            if (w.Contains("truck") || w.Contains("tractor") || w.Contains("firetruck"))
                return 450000;

            if (category == VehicleCategory.Utility)
                return 350000;

            return 250000;
        }

        private static float SpawnDistanceOf(string category, string[] words)
        {
            if (category == VehicleCategory.TwoWheels)
                return 2.5f;

            if (category == VehicleCategory.Utility)
            {
                foreach (string word in words)
                {
                    if (word == "bus" || word == "truck" || word == "firetruck" || word == "tractor")
                        return 6f;
                }

                return 5f;
            }

            return 3.5f;
        }

        private static bool Classify(
            SpawnableCrate crate, string[] tags, string squashedTitle, string[] words,
            SettingsFile settings, string barcode, out string reason)
        {
            reason = null;

            bool forced = MatchesAny(settings.Include, squashedTitle, barcode);

            if (MatchesAny(settings.Exclude, squashedTitle, barcode))
                return false;

            if (forced)
            {
                reason = "Include (vehicles.json)";
                return true;
            }

            if (!settings.AutoDetect)
                return false;

            if (AnyWord(words, ExcludedTitleWords))
                return false;

            List<string> tagWords = new List<string>();

            foreach (string tag in tags)
                tagWords.AddRange(Words(tag));

            string[] tagArray = tagWords.ToArray();

            if (AnyWord(tagArray, VehicleTagWords))
            {
                reason = "tag « " + string.Join(", ", tags) + " »";
                return true;
            }

            if (AnyWord(tagArray, ExcludedTagWords))
                return false;

            if (AnyWord(words, VehicleTitleWords))
            {
                reason = "mot du titre";
                return true;
            }

            return false;
        }

        // ------------------------------------------------------------
        // Lecture de l'Asset Warehouse
        // ------------------------------------------------------------

        public static bool WarehouseReady
        {
            get
            {
                try
                {
                    return AssetWarehouse.ready && AssetWarehouse.Instance != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Relit le catalogue. force = ignorer la signature et relire vehicles.json
        /// (bouton « Actualiser »). Retourne vrai si le catalogue a changé.
        /// </summary>
        public static bool Refresh(bool force)
        {
            return Refresh(force, false);
        }

        private static bool Refresh(bool force, bool ignoreGap)
        {
            lock (Sync)
            {
                float now = Time.unscaledTime;

                if (force && !ignoreGap && now - lastScan < MinimumForcedGap && scannedOnce)
                    return false;

                if (!WarehouseReady)
                    return false;

                try
                {
                    var crates = AssetWarehouse.Instance.GetCrates<SpawnableCrate>();

                    if (crates == null)
                        return false;

                    // 1re passe : signature (nombre + codes-barres) pour détecter un ajout de mod.
                    List<SpawnableCrate> list = new List<SpawnableCrate>();
                    long sig = 17;

                    foreach (SpawnableCrate crate in crates)
                    {
                        if (crate == null || crate.Barcode == null)
                            continue;

                        string id = crate.Barcode.ID;

                        if (string.IsNullOrWhiteSpace(id))
                            continue;

                        list.Add(crate);
                        sig = unchecked(sig * 31 + id.GetHashCode());
                    }

                    sig = unchecked(sig * 31 + list.Count);
                    lastScan = now;

                    if (!force && scannedOnce && sig == signature)
                        return false;

                    SettingsFile settings = LoadSettings();
                    Rebuild(list, settings);

                    bool firstTime = !scannedOnce;
                    signature = sig;
                    scannedOnce = true;

                    WriteReports(list);

                    if (firstTime)
                    {
                        MelonLogger.Msg("[FusionDarkRP Véhicules] " + Vehicles.Count
                            + " véhicule(s) détecté(s) dans BONELAB (liste : UserData/FusionDarkRP/vehicles-detected.txt).");
                    }

                    return true;
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning("[FusionDarkRP Véhicules] Détection : " + exception.Message);
                    return false;
                }
            }
        }

        private static void Rebuild(List<SpawnableCrate> crates, SettingsFile settings)
        {
            Dictionary<string, VehicleDefinition> previous =
                new Dictionary<string, VehicleDefinition>(ById, StringComparer.Ordinal);

            List<VehicleDefinition> found = new List<VehicleDefinition>();
            Dictionary<string, int> titleCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> usedIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (SpawnableCrate crate in crates)
            {
                try
                {
                    if (crate.Redacted)
                        continue;

                    string barcode = crate.Barcode.ID;
                    string title = crate.Title;

                    if (string.IsNullOrWhiteSpace(title))
                        continue;

                    string squashed = Squash(title);
                    string[] words = Words(title);
                    string[] tags = ReadTags(crate);
                    string reason;

                    if (!Classify(crate, tags, squashed, words, settings, barcode, out reason))
                        continue;

                    string palletTitle = string.Empty;
                    bool internalPallet = false;

                    try
                    {
                        Pallet pallet = crate.Pallet;

                        if (pallet != null)
                        {
                            palletTitle = pallet.Title ?? string.Empty;
                            internalPallet = pallet.Internal ||
                                string.Equals(pallet.Author, "SLZ", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    catch
                    {
                        // Pack introuvable : on garde le véhicule quand même.
                    }

                    List<string> tagWords = new List<string>();

                    foreach (string tag in tags)
                        tagWords.AddRange(Words(tag));

                    string category = CategoryOf(words, tagWords.ToArray());
                    string name = Clean(title, 40);
                    long price = 0;

                    bool nameSet = false, categorySet = false;

                    foreach (VehicleOverride rule in settings.Overrides)
                    {
                        if (rule == null || string.IsNullOrWhiteSpace(rule.Match) ||
                            !Matches(rule.Match, squashed, barcode))
                        {
                            continue;
                        }

                        if (!nameSet && !string.IsNullOrWhiteSpace(rule.Name))
                        {
                            name = Clean(rule.Name, 40);
                            nameSet = true;
                        }

                        if (price <= 0 && rule.PriceCents > 0)
                            price = rule.PriceCents;

                        if (!categorySet && !string.IsNullOrWhiteSpace(rule.Category))
                        {
                            string wanted = rule.Category.Trim().ToLowerInvariant();

                            if (Array.IndexOf(VehicleCategory.All, wanted) >= 0)
                            {
                                category = wanted;
                                categorySet = true;
                            }
                        }
                    }

                    if (price <= 0)
                    {
                        price = settings.DefaultPriceCents > 0 && !AnyWord(words, VehicleTitleWords)
                            ? settings.DefaultPriceCents
                            : AutoPrice(words, category);

                        price = (long)Math.Round(price * settings.PriceMultiplier / 100.0) * 100;
                    }

                    if (price < MinimumPriceCents)
                        price = MinimumPriceCents;

                    string id = ShortHash(barcode);
                    int salt = 0;

                    while (usedIds.Contains(id))
                        id = ShortHash(barcode + "#" + (++salt));

                    usedIds.Add(id);

                    int seen;
                    titleCount.TryGetValue(name, out seen);
                    titleCount[name] = seen + 1;

                    found.Add(new VehicleDefinition
                    {
                        Id = id,
                        Barcode = barcode,
                        Title = title,
                        Name = name,
                        Pallet = palletTitle,
                        Source = internalPallet ? "BONELAB" : "Mod",
                        Category = category,
                        PriceCents = price,
                        Reason = reason,
                        SpawnDistance = SpawnDistanceOf(category, words)
                    });
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning("[FusionDarkRP Véhicules] Crate ignorée : " + exception.Message);
                }
            }

            // Homonymes (même nom dans deux packs) : on ajoute le pack pour les distinguer.
            foreach (VehicleDefinition vehicle in found)
            {
                int count;

                if (titleCount.TryGetValue(vehicle.Name, out count) && count > 1 &&
                    !string.IsNullOrWhiteSpace(vehicle.Pallet))
                {
                    vehicle.Name = Clean(vehicle.Name + " (" + vehicle.Pallet + ")", 40);
                }
            }

            found.Sort((a, b) =>
            {
                int byCategory = Array.IndexOf(VehicleCategory.All, a.Category)
                    .CompareTo(Array.IndexOf(VehicleCategory.All, b.Category));

                return byCategory != 0
                    ? byCategory
                    : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            Vehicles.Clear();
            ById.Clear();

            foreach (VehicleDefinition vehicle in found)
            {
                Vehicles.Add(vehicle);
                ById[vehicle.Id] = vehicle;
            }

            if (previous.Count > 0)
            {
                foreach (VehicleDefinition vehicle in found)
                {
                    if (!previous.ContainsKey(vehicle.Id))
                    {
                        MelonLogger.Msg("[FusionDarkRP Véhicules] Nouveau véhicule détecté : "
                            + vehicle.Name + " (" + vehicle.Source + ") - "
                            + (vehicle.PriceCents / 100) + " EUR.");
                    }
                }

                foreach (VehicleDefinition old in previous.Values)
                {
                    if (!ById.ContainsKey(old.Id))
                    {
                        MelonLogger.Msg("[FusionDarkRP Véhicules] Véhicule retiré du catalogue : " + old.Name + ".");
                    }
                }
            }
        }

        private static void WriteReports(List<SpawnableCrate> crates)
        {
            try
            {
                Directory.CreateDirectory(Folder());

                StringBuilder detected = new StringBuilder();
                detected.AppendLine("# Véhicules vendables (détectés dans BONELAB) - " + Vehicles.Count);
                detected.AppendLine("# Catégorie | Prix EUR | Source | Nom | Pack | Code-barres | Raison");

                foreach (VehicleDefinition v in Vehicles)
                {
                    detected.AppendLine(v.Category + " | " + (v.PriceCents / 100) + " | " + v.Source + " | "
                        + v.Name + " | " + v.Pallet + " | " + v.Barcode + " | " + v.Reason);
                }

                File.WriteAllText(Path.Combine(Folder(), "vehicles-detected.txt"), detected.ToString());

                StringBuilder all = new StringBuilder();
                all.AppendLine("# Tous les objets apparaissables : Titre | Pack | Tags | Code-barres");
                all.AppendLine("# Un véhicule absent de vehicles-detected.txt ? Ajoutez un extrait de son titre dans Include (vehicles.json).");

                foreach (SpawnableCrate crate in crates)
                {
                    try
                    {
                        string pallet = string.Empty;

                        try
                        {
                            if (crate.Pallet != null)
                                pallet = crate.Pallet.Title;
                        }
                        catch
                        {
                            pallet = string.Empty;
                        }

                        all.AppendLine(crate.Title + " | " + pallet + " | "
                            + string.Join(",", ReadTags(crate)) + " | " + crate.Barcode.ID);
                    }
                    catch
                    {
                        // Ligne illisible : ignorée.
                    }
                }

                File.WriteAllText(Path.Combine(Folder(), "spawnable-catalog.txt"), all.ToString());
            }
            catch (Exception exception)
            {
                MelonLogger.Warning("[FusionDarkRP Véhicules] Rapports non écrits : " + exception.Message);
            }
        }

        // ------------------------------------------------------------
        // Accès
        // ------------------------------------------------------------

        /// <summary>Appelée chaque image : relit le catalogue toutes les 60 s côté hôte.</summary>
        public static void Update()
        {
            try
            {
                float now = Time.unscaledTime;

                if (now < nextPoll)
                    return;

                nextPoll = now + PollSeconds;

                if (!NetworkInfo.HasServer || !NetworkInfo.IsHost)
                    return;

                Refresh(false);
            }
            catch (Exception exception)
            {
                MelonLogger.Warning("[FusionDarkRP Véhicules] Relecture : " + exception.Message);
            }
        }

        public static void Reset()
        {
            lock (Sync)
            {
                Vehicles.Clear();
                ById.Clear();
                signature = -1;
                scannedOnce = false;
                lastScan = -100f;
                nextPoll = 0f;
            }
        }

        public static int Count
        {
            get
            {
                lock (Sync)
                    return Vehicles.Count;
            }
        }

        public static bool TryGet(string id, out VehicleDefinition definition)
        {
            definition = null;

            if (string.IsNullOrWhiteSpace(id))
                return false;

            lock (Sync)
            {
                if (!scannedOnce)
                    Refresh(false);

                return ById.TryGetValue(id.Trim(), out definition);
            }
        }

        public const long MaxPriceCents = 50000000L;

        /// <summary>
        /// Fixe le prix d'un véhicule (centimes ; 0 = retour au prix automatique).
        /// Enregistré dans vehicles.json (règle Overrides dont Match = code-barres).
        /// </summary>
        public static bool TrySetPrice(string id, long cents, out string message)
        {
            message = null;

            lock (Sync)
            {
                VehicleDefinition definition;

                if (!TryGet(id, out definition))
                {
                    message = "Véhicule inconnu : actualisez le catalogue.";
                    return false;
                }

                if (cents != 0 && (cents < MinimumPriceCents || cents > MaxPriceCents))
                {
                    message = "Prix invalide (" + (MinimumPriceCents / 100) + " à "
                        + (MaxPriceCents / 100) + " EUR).";
                    return false;
                }

                try
                {
                    SettingsFile settings = LoadSettings();
                    VehicleOverride rule = null;

                    foreach (VehicleOverride item in settings.Overrides)
                    {
                        if (item != null && string.Equals(item.Match, definition.Barcode,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            rule = item;
                            break;
                        }
                    }

                    if (rule == null)
                    {
                        rule = new VehicleOverride { Match = definition.Barcode };
                        settings.Overrides.Insert(0, rule);
                    }

                    rule.PriceCents = cents;

                    if (cents == 0 && string.IsNullOrWhiteSpace(rule.Name) &&
                        string.IsNullOrWhiteSpace(rule.Category))
                    {
                        settings.Overrides.Remove(rule);
                    }

                    Directory.CreateDirectory(Folder());
                    File.WriteAllText(SettingsPath(), JsonSerializer.Serialize(settings, JsonOptions()));

                    Refresh(true, true);

                    VehicleDefinition updated;
                    TryGet(id, out updated);

                    message = definition.Name + " : "
                        + (cents == 0 ? "prix automatique rétabli" : "prix fixé")
                        + (updated != null ? " (" + (updated.PriceCents / 100) + " EUR)." : ".");

                    return true;
                }
                catch (Exception exception)
                {
                    message = "Enregistrement impossible : " + exception.Message;
                    return false;
                }
            }
        }

        /// <summary>Le véhicule est-il encore chargé dans BONELAB (mod retiré en cours de partie) ?</summary>
        public static bool IsInstalled(VehicleDefinition definition)
        {
            return definition != null && GameItemService.CanSpawn(definition.Barcode);
        }

        public static long EffectivePrice(VehicleDefinition definition)
        {
            return definition == null ? 0 : definition.PriceCents;
        }

        /// <summary>Page du catalogue pour le menu (catégorie vide = première non vide).</summary>
        public static VehicleListData BuildPage(string category, int page, int target)
        {
            lock (Sync)
            {
                Refresh(false);

                VehicleListData data = new VehicleListData
                {
                    Target = target,
                    Cats = new List<VehicleCategoryInfo>(),
                    Items = new List<VehicleItemInfo>()
                };

                Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);

                foreach (VehicleDefinition vehicle in Vehicles)
                {
                    int value;
                    counts.TryGetValue(vehicle.Category, out value);
                    counts[vehicle.Category] = value + 1;
                }

                foreach (string key in VehicleCategory.All)
                {
                    int count;

                    if (counts.TryGetValue(key, out count) && count > 0)
                        data.Cats.Add(new VehicleCategoryInfo { K = key, N = VehicleCategory.Label(key), C = count });
                }

                string chosen = string.IsNullOrWhiteSpace(category) ? null : category.Trim().ToLowerInvariant();

                if (chosen == null || !counts.ContainsKey(chosen))
                    chosen = data.Cats.Count > 0 ? data.Cats[0].K : VehicleCategory.Cars;

                List<VehicleDefinition> inCategory = new List<VehicleDefinition>();

                foreach (VehicleDefinition vehicle in Vehicles)
                {
                    if (vehicle.Category == chosen)
                        inCategory.Add(vehicle);
                }

                int pages = Math.Max(1, (inCategory.Count + PageSize - 1) / PageSize);
                int index = Math.Max(0, Math.Min(page, pages - 1));

                data.Cat = chosen;
                data.Page = index;
                data.Pages = pages;
                data.Total = Vehicles.Count;

                for (int i = index * PageSize; i < Math.Min(inCategory.Count, (index + 1) * PageSize); i++)
                {
                    VehicleDefinition vehicle = inCategory[i];

                    data.Items.Add(new VehicleItemInfo
                    {
                        I = vehicle.Id,
                        N = vehicle.Name,
                        P = vehicle.PriceCents,
                        S = vehicle.Source
                    });
                }

                return data;
            }
        }
    }
}
