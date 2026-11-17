using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MelonLoader;
using MelonLoader.Utils;

namespace FusionDarkRP.RP
{
    /// <summary>Mode d'obtention d'un véhicule.</summary>
    public static class VehicleAccessMode
    {
        public const int Service = 0;   // véhicule de fonction gratuit
        public const int Demo = 1;      // démonstration (concessionnaire)
        public const int Gang = 2;      // véhicule du gang (payant, associé+)
        public const int Purchase = 3;  // achat chez un concessionnaire en service
    }

    public sealed class VehicleDefinition
    {
        public string Id { get; set; }
        /// <summary>Identifiant du métier concerné ; "*" = disponible pour tous.</summary>
        public string Job { get; set; }
        public string Name { get; set; }
        /// <summary>
        /// Mots du titre du véhicule recherché parmi les objets chargés dans
        /// BONELAB (un mot EXACT, sans casse : « clio », « tmax »...).
        /// </summary>
        public string[] Keywords { get; set; }
        public int Access { get; set; }
        public int MinGrade { get; set; }
        /// <summary>Prix en centimes ; 0 = tarif par défaut du mode d'accès.</summary>
        public long PriceCents { get; set; }
        /// <summary>Repli : accepte n'importe quel véhicule BONELAB si rien d'autre n'est installé.</summary>
        public bool Fallback { get; set; }
    }

    /// <summary>
    /// Véhicules par métier, avec de VRAIS noms de véhicules (Clio, TMAX...).
    /// BONELAB ne fournit pas ces modèles : ils sont cherchés par titre parmi les
    /// mods de véhicules installés. La liste est modifiable sans recompiler :
    /// UserData/FusionDarkRP/vehicles.json (créé au premier lancement).
    /// </summary>
    public static class JobVehicleCatalog
    {
        public const long DefaultGangCents = 25000L;
        public const long DefaultPurchaseCents = 40000L;

        private static readonly object Sync = new object();
        private static List<VehicleDefinition> loaded;

        private static VehicleDefinition V(string id, string job, string name,
            int access, int grade, long price, params string[] keywords)
        {
            return new VehicleDefinition
            {
                Id = id, Job = job, Name = name, Access = access,
                MinGrade = grade, PriceCents = price, Keywords = keywords
            };
        }

        private static VehicleDefinition Fallback(string id, string job, string name, int access, int grade)
        {
            return new VehicleDefinition
            {
                Id = id, Job = job, Name = name, Access = access, MinGrade = grade,
                Keywords = new[] { "jalopy", "buggy", "truck", "van", "car", "cart", "scooter", "bike", "vehicle" },
                Fallback = true
            };
        }

        private static List<VehicleDefinition> Defaults()
        {
            const int P = VehicleAccessMode.Purchase;

            return new List<VehicleDefinition>
            {
                // Aucun véhicule de métier : seuls les véhicules à l'achat existent, et
                // uniquement lorsqu'un concessionnaire est en service (commission versée).
                // ---- Achat chez un concessionnaire en service ----
                V("civ_twingo", "*", "Renault Twingo", P, 0, 30000, "twingo"),
                V("civ_clio", "*", "Renault Clio", P, 0, 40000, "clio"),
                V("civ_208", "*", "Peugeot 208", P, 0, 42000, "208"),
                V("civ_golf", "*", "Volkswagen Golf", P, 0, 50000, "golf"),
                V("civ_tmax", "*", "Yamaha TMAX", P, 0, 60000, "tmax"),
                V("civ_scooter", "*", "Scooter", P, 0, 15000, "scooter"),
                V("civ_bike", "*", "Vélo", P, 0, 5000, "bike", "bicycle", "velo"),
                Fallback("civ_any", "*", "Véhicule standard (BONELAB)", P, 0)
            };
        }

        private static string FilePath()
        {
            return Path.Combine(MelonEnvironment.UserDataDirectory, "FusionDarkRP", "vehicles.json");
        }

        private static List<VehicleDefinition> Load()
        {
            lock (Sync)
            {
                if (loaded != null) return loaded;

                List<VehicleDefinition> list = null;
                string path = FilePath();
                JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };

                try
                {
                    if (File.Exists(path))
                    {
                        list = JsonSerializer.Deserialize<List<VehicleDefinition>>(File.ReadAllText(path), options);

                        if (list != null)
                            list = list.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id)
                                && !string.IsNullOrWhiteSpace(x.Job) && x.Keywords != null
                                && x.Keywords.Length > 0).ToList();

                        if (list == null || list.Count == 0)
                        {
                            MelonLogger.Warning("[FusionDarkRP Véhicules] vehicles.json vide ou invalide : valeurs par défaut.");
                            list = null;
                        }
                    }
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning("[FusionDarkRP Véhicules] vehicles.json illisible : " + exception.Message);
                    list = null;
                }

                if (list == null)
                {
                    list = Defaults();

                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path));

                        if (!File.Exists(path))
                            File.WriteAllText(path, JsonSerializer.Serialize(list, options));
                    }
                    catch (Exception exception)
                    {
                        MelonLogger.Warning("[FusionDarkRP Véhicules] Écriture vehicles.json : " + exception.Message);
                    }
                }

                loaded = list;
                return loaded;
            }
        }

        public static long EffectivePrice(VehicleDefinition definition)
        {
            if (definition == null) return 0;
            if (definition.PriceCents > 0) return definition.PriceCents;

            switch (definition.Access)
            {
                case VehicleAccessMode.Gang: return DefaultGangCents;
                case VehicleAccessMode.Purchase: return DefaultPurchaseCents;
                default: return 0;
            }
        }

        /// <summary>Véhicules à l'achat (les entrées « * » sont pour tous ; aucun véhicule de métier).</summary>
        public static IReadOnlyList<VehicleDefinition> ForJob(string jobId, int gradeIndex)
        {
            // Seuls les véhicules À L'ACHAT sont proposés (même si un ancien vehicles.json
            // contient encore des véhicules de service, de gang ou de démonstration).
            return Load().Where(x =>
                    x.Access == VehicleAccessMode.Purchase
                    && (string.Equals(x.Job, jobId, StringComparison.OrdinalIgnoreCase) || x.Job == "*")
                    && gradeIndex >= x.MinGrade)
                .ToArray();
        }

        public static VehicleDefinition Find(string jobId, int gradeIndex, string id)
        {
            IReadOnlyList<VehicleDefinition> list = ForJob(jobId, gradeIndex);

            if (!string.IsNullOrWhiteSpace(id))
                return list.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

            return list.FirstOrDefault();
        }
    }
}
