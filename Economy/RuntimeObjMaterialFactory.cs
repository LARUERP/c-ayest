
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace FusionDarkRP.Economy
{
    /// <summary>
    /// Création des matériaux OBJ compatibles BONELAB.
    /// Chaque bloc newmtl du fichier MTL peut être chargé séparément.
    /// </summary>
    public static class RuntimeObjMaterialFactory
    {
        private const string ResourcePrefix =
            "FusionDarkRP.MoneyModels.";

        private static readonly string[] ShaderCandidates =
        {
            "SLZ/LitMAS/LitMAS Standard",
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Universal Render Pipeline/Unlit",
            "Unlit/Color"
        };

        private sealed class MaterialData
        {
            public string Name;

            public Color DiffuseColor =
                new Color(0.75f, 0.75f, 0.75f, 1f);

            public float Transparency = 1f;
            public float Shininess = 0f;
            public float Metallic = 0f;

            public string DiffuseMap;
            public Color Emission = Color.black;
            public bool HasEmission;
        }

        // Compatibilité avec les anciens appels.
        // Charge le premier matériau déclaré dans le MTL.
        public static bool TryCreate(
            string modelName,
            out Material material,
            out string error)
        {
            return TryCreate(
                modelName,
                null,
                out material,
                out error);
        }

        /// <summary>
        /// Crée le matériau correspondant au nom usemtl de l'OBJ.
        /// </summary>
        public static bool TryCreate(
            string modelName,
            string materialName,
            out Material material,
            out string error)
        {
            material = null;
            error = null;

            string normalizedName;

            if (!TryNormalizeModelName(
                modelName,
                out normalizedName,
                out error))
            {
                return false;
            }

            MaterialData data = new MaterialData();
            Material created = null;

            try
            {
                if (!TryReadMaterialData(
                    normalizedName,
                    materialName,
                    data,
                    out error))
                {
                    return false;
                }

                string shaderName;

                Shader shader = FindCompatibleShader(
                    out shaderName);

                if (shader == null)
                {
                    error =
                        "Aucun shader compatible BONELAB disponible.";

                    Debug.LogError(
                        "[FusionDarkRP Material] " + error);

                    return false;
                }

                created = new Material(shader);

                if (created == null)
                {
                    error =
                        "Impossible de créer le matériau Unity.";

                    return false;
                }

                string finalMaterialName =
                    string.IsNullOrWhiteSpace(data.Name)
                        ? "Default"
                        : data.Name;

                created.name =
                    "FusionDarkRP_" +
                    normalizedName +
                    "_" +
                    finalMaterialName;

                Color color = data.DiffuseColor;
                color.a = data.Transparency;

                ApplyColor(created, color);

                // Carte bancaire : visible des deux faces, quel que soit le
                // côté sur lequel elle tombe.
                if (string.Equals(
                        normalizedName,
                        "BankCard",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (created.HasProperty("_Cull"))
                        created.SetFloat("_Cull", 0f);

                    if (created.HasProperty("_CullMode"))
                        created.SetFloat("_CullMode", 0f);

                    created.doubleSidedGI = true;
                }

                if (created.HasProperty("_Metallic"))
                {
                    created.SetFloat(
                        "_Metallic",
                        Mathf.Clamp01(data.Metallic));
                }

                float smoothness =
                    Mathf.Clamp01(data.Shininess / 1000f);

                if (created.HasProperty("_Glossiness"))
                {
                    created.SetFloat(
                        "_Glossiness",
                        smoothness);
                }

                if (created.HasProperty("_Smoothness"))
                {
                    created.SetFloat(
                        "_Smoothness",
                        smoothness);
                }

                Texture2D diffuse = Texture2D.whiteTexture;

                if (!string.IsNullOrWhiteSpace(data.DiffuseMap))
                {
                    Texture2D loaded;
                    string textureError;

                    if (RuntimeObjTextureLoader.TryLoad(
                        data.DiffuseMap, out loaded, out textureError))
                    {
                        diffuse = loaded;
                    }
                    else
                    {
                        Debug.LogWarning(
                            "[FusionDarkRP Material] " + textureError);
                    }
                }

                if (created.HasProperty("_BaseMap"))
                    created.SetTexture("_BaseMap", diffuse);

                if (created.HasProperty("_MainTex"))
                    created.SetTexture("_MainTex", diffuse);

                if (data.HasEmission)
                {
                    string[] emissionProperties =
                    {
                        "_EmissionColor", "_EmissiveColor", "_Emission"
                    };

                    foreach (string property in emissionProperties)
                    {
                        if (created.HasProperty(property))
                        {
                            created.EnableKeyword("_EMISSION");
                            created.SetColor(property, data.Emission);
                            break;
                        }
                    }

                    if (created.HasProperty("_EmissionMap"))
                        created.SetTexture("_EmissionMap", diffuse);
                }

                ConfigureTransparency(
                    created,
                    data.Transparency);

                created.enableInstancing = false;

                Debug.Log(
                    "[FusionDarkRP Material] Créé : "
                    + normalizedName
                    + " / "
                    + finalMaterialName
                    + " | Shader : "
                    + shaderName);

                material = created;
                created = null;

                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Erreur de création du matériau : " +
                    exception.Message;

                Debug.LogError(
                    "[FusionDarkRP Material] " + error);

                return false;
            }
            finally
            {
                if (created != null)
                    SafeDestroy(created);
            }
        }

        private static bool TryNormalizeModelName(
            string modelName,
            out string normalizedName,
            out string error)
        {
            normalizedName = null;
            error = null;

            if (string.IsNullOrWhiteSpace(modelName))
            {
                error = "Nom du modèle invalide.";
                return false;
            }

            normalizedName = modelName.Trim();

            if (normalizedName.EndsWith(
                ".obj",
                StringComparison.OrdinalIgnoreCase))
            {
                normalizedName = normalizedName.Substring(
                    0,
                    normalizedName.Length - 4);
            }

            if (string.IsNullOrWhiteSpace(normalizedName) ||
                normalizedName.Contains("/") ||
                normalizedName.Contains("\\") ||
                normalizedName.Contains("..") ||
                normalizedName.IndexOf(':') >= 0)
            {
                normalizedName = null;
                error = "Nom de modèle non autorisé.";
                return false;
            }

            return true;
        }

        private static Shader FindCompatibleShader(
            out string selectedName)
        {
            selectedName = null;

            for (int i = 0; i < ShaderCandidates.Length; i++)
            {
                Shader shader =
                    FindLoadedShader(ShaderCandidates[i]);

                if (shader == null || !shader.isSupported)
                    continue;

                selectedName = shader.name;
                return shader;
            }

            return null;
        }

        private static readonly System.Collections.Generic.Dictionary<string, Shader>
            ShaderCache =
                new System.Collections.Generic.Dictionary<string, Shader>();

        private static Shader FindLoadedShader(string shaderName)
        {
            if (string.IsNullOrWhiteSpace(shaderName))
                return null;

            Shader cached;

            if (ShaderCache.TryGetValue(shaderName, out cached) &&
                cached != null)
            {
                return cached;
            }

            Shader result = FindLoadedShaderUncached(shaderName);

            if (result != null)
                ShaderCache[shaderName] = result;

            return result;
        }

        private static Shader FindLoadedShaderUncached(
            string shaderName)
        {

            try
            {
                Shader shader = Shader.Find(shaderName);

                if (shader != null && shader.isSupported)
                    return shader;
            }
            catch
            {
                // Recherche complémentaire ci-dessous.
            }

            try
            {
                Shader[] loadedShaders =
                    Resources.FindObjectsOfTypeAll<Shader>();

                if (loadedShaders == null)
                    return null;

                for (int i = 0; i < loadedShaders.Length; i++)
                {
                    Shader shader = loadedShaders[i];

                    if (shader == null)
                        continue;

                    if (!string.Equals(
                        shader.name,
                        shaderName,
                        StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (shader.isSupported)
                        return shader;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[FusionDarkRP Material] Recherche shader impossible : "
                    + exception.Message);
            }

            return null;
        }

        private static void ApplyColor(
            Material material,
            Color color)
        {
            if (material == null)
                return;

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            material.color = color;
        }

        /// <summary>
        /// Lit uniquement le bloc newmtl demandé.
        /// Si materialName est null, utilise le premier bloc.
        /// </summary>
        private static bool TryReadMaterialData(
            string modelName,
            string materialName,
            MaterialData destination,
            out string error)
        {
            error = null;

            if (destination == null)
            {
                error = "Données du matériau inexistantes.";
                return false;
            }

            try
            {
                Assembly assembly =
                    typeof(RuntimeObjMaterialFactory).Assembly;

                string resourceName =
                    ResourcePrefix + modelName + ".mtl";

                using (Stream stream =
                    assembly.GetManifestResourceStream(resourceName))
                {
                    // Un modèle sans MTL conserve une apparence neutre.
                    if (stream == null)
                    {
                        destination.Name = "Default";
                        return true;
                    }

                    using (StreamReader reader =
                        new StreamReader(stream))
                    {
                        string line;
                        int lineNumber = 0;

                        bool foundMaterial = false;
                        bool readingSelectedMaterial = false;
                        bool hasMaterialDeclaration = false;

                        while ((line = reader.ReadLine()) != null)
                        {
                            lineNumber++;

                            int commentIndex = line.IndexOf('#');

                            if (commentIndex >= 0)
                            {
                                line = line.Substring(
                                    0,
                                    commentIndex);
                            }

                            line = line.Trim();

                            if (line.Length == 0)
                                continue;

                            string[] parts = line.Split(
                                new char[] { ' ', '\t' },
                                StringSplitOptions.RemoveEmptyEntries);

                            if (parts.Length == 0)
                                continue;

                            if (string.Equals(
                                parts[0],
                                "newmtl",
                                StringComparison.OrdinalIgnoreCase))
                            {
                                if (parts.Length < 2)
                                {
                                    error =
                                        "Déclaration newmtl invalide ligne "
                                        + lineNumber;

                                    return false;
                                }

                                hasMaterialDeclaration = true;

                                string declaredName =
                                    string.Join(
                                        " ",
                                        parts,
                                        1,
                                        parts.Length - 1).Trim();

                                bool selected =
                                    string.IsNullOrWhiteSpace(materialName)
                                        ? !foundMaterial
                                        : string.Equals(
                                            declaredName,
                                            materialName.Trim(),
                                            StringComparison.OrdinalIgnoreCase);

                                if (selected && !foundMaterial)
                                {
                                    foundMaterial = true;
                                    readingSelectedMaterial = true;

                                    destination.Name = declaredName;
                                    destination.DiffuseColor =
                                        new Color(0.75f, 0.75f, 0.75f, 1f);

                                    destination.Transparency = 1f;
                                    destination.Shininess = 0f;
                                    destination.Metallic = 0f;
                                    destination.DiffuseMap = null;
                                    destination.Emission = Color.black;
                                    destination.HasEmission = false;
                                }
                                else
                                {
                                    readingSelectedMaterial = false;
                                }

                                continue;
                            }

                            // Les propriétés avant le premier newmtl
                            // ne sont pas attribuées à un matériau nommé.
                            if (!readingSelectedMaterial)
                                continue;

                            if (parts.Length < 2)
                                continue;

                            switch (parts[0])
                            {
                                case "Kd":
                                {
                                    if (parts.Length < 4)
                                    {
                                        error =
                                            "Couleur Kd invalide ligne "
                                            + lineNumber;

                                        return false;
                                    }

                                    float red;
                                    float green;
                                    float blue;

                                    if (!TryParseFloat(parts[1], out red) ||
                                        !TryParseFloat(parts[2], out green) ||
                                        !TryParseFloat(parts[3], out blue))
                                    {
                                        error =
                                            "Valeur Kd invalide ligne "
                                            + lineNumber;

                                        return false;
                                    }

                                    destination.DiffuseColor =
                                        new Color(
                                            Mathf.Clamp01(red),
                                            Mathf.Clamp01(green),
                                            Mathf.Clamp01(blue),
                                            destination.Transparency);

                                    break;
                                }

                                case "d":
                                {
                                    float alpha;

                                    if (!TryParseFloat(parts[1], out alpha))
                                    {
                                        error =
                                            "Transparence d invalide ligne "
                                            + lineNumber;

                                        return false;
                                    }

                                    destination.Transparency =
                                        Mathf.Clamp01(alpha);

                                    break;
                                }

                                case "Tr":
                                {
                                    float transparency;

                                    if (!TryParseFloat(
                                        parts[1],
                                        out transparency))
                                    {
                                        error =
                                            "Transparence Tr invalide ligne "
                                            + lineNumber;

                                        return false;
                                    }

                                    destination.Transparency =
                                        Mathf.Clamp01(1f - transparency);

                                    break;
                                }

                                case "Ns":
                                {
                                    float shininess;

                                    if (!TryParseFloat(
                                        parts[1],
                                        out shininess))
                                    {
                                        error =
                                            "Valeur Ns invalide ligne "
                                            + lineNumber;

                                        return false;
                                    }

                                    destination.Shininess =
                                        Mathf.Clamp(
                                            shininess,
                                            0f,
                                            1000f);

                                    break;
                                }

                                case "map_Kd":
                                {
                                    destination.DiffuseMap =
                                        parts[parts.Length - 1];

                                    break;
                                }

                                case "Ke":
                                {
                                    if (parts.Length >= 4)
                                    {
                                        float er;
                                        float eg;
                                        float eb;

                                        if (TryParseFloat(parts[1], out er) &&
                                            TryParseFloat(parts[2], out eg) &&
                                            TryParseFloat(parts[3], out eb))
                                        {
                                            destination.Emission = new Color(
                                                Mathf.Clamp01(er),
                                                Mathf.Clamp01(eg),
                                                Mathf.Clamp01(eb),
                                                1f);

                                            destination.HasEmission =
                                                er > 0f || eg > 0f || eb > 0f;
                                        }
                                    }

                                    break;
                                }

                                case "Pm":
                                {
                                    float metallic;

                                    if (!TryParseFloat(
                                        parts[1],
                                        out metallic))
                                    {
                                        error =
                                            "Valeur Pm invalide ligne "
                                            + lineNumber;

                                        return false;
                                    }

                                    destination.Metallic =
                                        Mathf.Clamp01(metallic);

                                    break;
                                }
                            }
                        }

                        if (string.IsNullOrWhiteSpace(materialName))
                        {
                            if (!foundMaterial && hasMaterialDeclaration)
                            {
                                error =
                                    "Aucun matériau MTL exploitable.";

                                return false;
                            }

                            if (!foundMaterial)
                                destination.Name = "Default";

                            return true;
                        }

                        if (!foundMaterial)
                        {
                            error =
                                "Matériau introuvable dans le MTL : "
                                + materialName;

                            return false;
                        }

                        return true;
                    }
                }
            }
            catch (Exception exception)
            {
                error =
                    "Erreur de lecture du matériau "
                    + modelName
                    + " : "
                    + exception.Message;

                return false;
            }
        }

        private static void ConfigureTransparency(
            Material material,
            float alpha)
        {
            if (material == null)
                return;

            bool transparent = alpha < 0.999f;

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat(
                    "_Surface",
                    transparent ? 1f : 0f);
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetInt(
                    "_SrcBlend",
                    transparent
                        ? (int)BlendMode.SrcAlpha
                        : (int)BlendMode.One);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetInt(
                    "_DstBlend",
                    transparent
                        ? (int)BlendMode.OneMinusSrcAlpha
                        : (int)BlendMode.Zero);
            }

            if (material.HasProperty("_BlendSrc"))
            {
                material.SetFloat(
                    "_BlendSrc",
                    transparent ? 5f : 1f);
            }

            if (material.HasProperty("_BlendDst"))
            {
                material.SetFloat(
                    "_BlendDst",
                    transparent ? 10f : 0f);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat(
                    "_ZWrite",
                    transparent ? 0f : 1f);
            }

            if (material.HasProperty("_Mode"))
            {
                material.SetFloat(
                    "_Mode",
                    transparent ? 3f : 0f);
            }

            if (transparent)
            {
                material.EnableKeyword(
                    "_SURFACE_TYPE_TRANSPARENT");

                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");

                material.renderQueue =
                    (int)RenderQueue.Transparent;

                material.SetOverrideTag(
                    "RenderType",
                    "Transparent");
            }
            else
            {
                material.DisableKeyword(
                    "_SURFACE_TYPE_TRANSPARENT");

                material.DisableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");

                material.renderQueue =
                    (int)RenderQueue.Geometry;

                material.SetOverrideTag(
                    "RenderType",
                    "Opaque");
            }
        }

        private static bool TryParseFloat(
            string value,
            out float result)
        {
            return float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result)
                && !float.IsNaN(result)
                && !float.IsInfinity(result);
        }

        private static void SafeDestroy(
            UnityEngine.Object target)
        {
            if (target == null)
                return;

            try
            {
                UnityEngine.Object.Destroy(target);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[FusionDarkRP] Erreur de nettoyage matériau : "
                    + exception.Message);
            }
        }

        public static void Destroy(Material material)
        {
            SafeDestroy(material);
        }
    }
}
