
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace FusionDarkRP.Economy
{
    public struct ObjVector2
    {
        public float X;
        public float Y;

        public ObjVector2(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    public struct ObjVector3
    {
        public float X;
        public float Y;
        public float Z;

        public ObjVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// Contient les triangles associés à un matériau OBJ.
    /// </summary>
    public sealed class RuntimeObjSubmeshData
    {
        public string MaterialName { get; private set; }

        public List<int> Triangles { get; private set; }

        public RuntimeObjSubmeshData(string materialName)
        {
            MaterialName = string.IsNullOrWhiteSpace(materialName)
                ? "Default"
                : materialName;

            Triangles = new List<int>();
        }
    }

    public sealed class RuntimeObjMeshData
    {
        public string ModelName { get; private set; }

        public List<ObjVector3> Vertices { get; private set; }

        public List<ObjVector2> UVs { get; private set; }

        public List<ObjVector3> Normals { get; private set; }

        // Liste globale conservée pour compatibilité
        // avec les anciens constructeurs de mesh.
        public List<int> Triangles { get; private set; }

        // Nouveaux groupes de triangles par matériau.
        public List<RuntimeObjSubmeshData> Submeshes { get; private set; }

        public bool HasCompleteNormals { get; internal set; }

        public RuntimeObjMeshData(string modelName)
        {
            ModelName = modelName;

            Vertices = new List<ObjVector3>();
            UVs = new List<ObjVector2>();
            Normals = new List<ObjVector3>();
            Triangles = new List<int>();

            Submeshes = new List<RuntimeObjSubmeshData>();

            HasCompleteNormals = false;
        }

        public bool IsValid
        {
            get
            {
                return Vertices != null
                    && Triangles != null
                    && Vertices.Count >= 3
                    && Triangles.Count >= 3
                    && Triangles.Count % 3 == 0;
            }
        }

        public RuntimeObjSubmeshData GetOrCreateSubmesh(
            string materialName)
        {
            string normalizedName =
                string.IsNullOrWhiteSpace(materialName)
                    ? "Default"
                    : materialName.Trim();

            for (int i = 0; i < Submeshes.Count; i++)
            {
                RuntimeObjSubmeshData existing = Submeshes[i];

                if (existing != null &&
                    string.Equals(
                        existing.MaterialName,
                        normalizedName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return existing;
                }
            }

            RuntimeObjSubmeshData created =
                new RuntimeObjSubmeshData(normalizedName);

            Submeshes.Add(created);

            return created;
        }
    }

    public static class RuntimeObjMeshLoader
    {
        private const string ResourcePrefix =
            "FusionDarkRP.MoneyModels.";

        private const int MaxSourceVertices = 500000;
        private const int MaxOutputVertices = 1000000;
        private const int MaxTriangles = 3000000;
        private const int MaxFaceVertices = 4096;
        private const int MaxLineLength = 1048576;
        private const int MaxMaterialNameLength = 128;

        private static readonly CultureInfo Invariant =
            CultureInfo.InvariantCulture;

        private sealed class FaceVertex
        {
            public int Vertex;
            public int UV;
            public int Normal;
        }

        public static bool TryLoad(
            string modelName,
            out RuntimeObjMeshData mesh,
            out string error)
        {
            mesh = null;
            error = null;

            if (string.IsNullOrWhiteSpace(modelName))
            {
                error = "Nom du modèle vide.";
                return false;
            }

            string normalized = modelName.Trim();

            if (normalized.EndsWith(
                ".obj",
                StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(
                    0,
                    normalized.Length - 4);
            }

            if (string.IsNullOrWhiteSpace(normalized) ||
                normalized.Contains("/") ||
                normalized.Contains("\\") ||
                normalized.Contains("..") ||
                normalized.IndexOf(':') >= 0)
            {
                error = "Nom de modèle invalide.";
                return false;
            }

            string resourceName =
                ResourcePrefix + normalized + ".obj";

            try
            {
                Assembly assembly =
                    typeof(RuntimeObjMeshLoader).Assembly;

                using (Stream stream =
                    assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        error =
                            "Ressource OBJ introuvable : " +
                            resourceName;

                        return false;
                    }

                    using (StreamReader reader =
                        new StreamReader(stream))
                    {
                        return TryParse(
                            normalized,
                            reader,
                            out mesh,
                            out error);
                    }
                }
            }
            catch (Exception exception)
            {
                mesh = null;

                error =
                    "Erreur de lecture OBJ : " +
                    exception.Message;

                return false;
            }
        }

        private static bool TryParse(
            string modelName,
            TextReader reader,
            out RuntimeObjMeshData mesh,
            out string error)
        {
            mesh = new RuntimeObjMeshData(modelName);
            error = null;

            List<ObjVector3> sourceVertices =
                new List<ObjVector3>();

            List<ObjVector2> sourceUVs =
                new List<ObjVector2>();

            List<ObjVector3> sourceNormals =
                new List<ObjVector3>();

            List<ObjVector3> generatedNormals =
                new List<ObjVector3>();

            Dictionary<string, int> vertexLookup =
                new Dictionary<string, int>();

            bool everyVertexHasNormal = true;

            string currentMaterial = "Default";

            string line;
            int lineNumber = 0;

            try
            {
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;

                    if (line.Length > MaxLineLength)
                    {
                        return Fail(
                            "Ligne OBJ trop longue : " + lineNumber,
                            out mesh,
                            out error);
                    }

                    int commentIndex = line.IndexOf('#');

                    if (commentIndex >= 0)
                        line = line.Substring(0, commentIndex);

                    line = line.Trim();

                    if (line.Length == 0)
                        continue;

                    string[] parts = line.Split(
                        new char[] { ' ', '\t' },
                        StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length == 0)
                        continue;

                    if (parts[0] == "v")
                    {
                        if (parts.Length < 4)
                        {
                            return Fail(
                                "Sommet invalide ligne " + lineNumber,
                                out mesh,
                                out error);
                        }

                        if (sourceVertices.Count >= MaxSourceVertices)
                        {
                            return Fail(
                                "Nombre maximal de sommets source dépassé.",
                                out mesh,
                                out error);
                        }

                        sourceVertices.Add(
                            new ObjVector3(
                                ParseFloat(parts[1]),
                                ParseFloat(parts[2]),
                                ParseFloat(parts[3])));
                    }
                    else if (parts[0] == "vt")
                    {
                        if (parts.Length < 3)
                        {
                            return Fail(
                                "Coordonnée UV invalide ligne " + lineNumber,
                                out mesh,
                                out error);
                        }

                        if (sourceUVs.Count >= MaxSourceVertices)
                        {
                            return Fail(
                                "Nombre maximal de coordonnées UV dépassé.",
                                out mesh,
                                out error);
                        }

                        sourceUVs.Add(
                            new ObjVector2(
                                ParseFloat(parts[1]),
                                ParseFloat(parts[2])));
                    }
                    else if (parts[0] == "vn")
                    {
                        if (parts.Length < 4)
                        {
                            return Fail(
                                "Normale invalide ligne " + lineNumber,
                                out mesh,
                                out error);
                        }

                        if (sourceNormals.Count >= MaxSourceVertices)
                        {
                            return Fail(
                                "Nombre maximal de normales dépassé.",
                                out mesh,
                                out error);
                        }

                        sourceNormals.Add(
                            new ObjVector3(
                                ParseFloat(parts[1]),
                                ParseFloat(parts[2]),
                                ParseFloat(parts[3])));
                    }
                    else if (parts[0] == "usemtl")
                    {
                        if (parts.Length < 2)
                        {
                            return Fail(
                                "Nom de matériau absent ligne " + lineNumber,
                                out mesh,
                                out error);
                        }

                        currentMaterial = string.Join(
                            " ",
                            parts,
                            1,
                            parts.Length - 1);

                        if (string.IsNullOrWhiteSpace(currentMaterial) ||
                            currentMaterial.Length > MaxMaterialNameLength)
                        {
                            return Fail(
                                "Nom de matériau invalide ligne " + lineNumber,
                                out mesh,
                                out error);
                        }

                        currentMaterial = currentMaterial.Trim();
                    }
                    else if (parts[0] == "f")
                    {
                        if (parts.Length < 4)
                        {
                            return Fail(
                                "Face invalide ligne " + lineNumber,
                                out mesh,
                                out error);
                        }

                        int faceVertexCount = parts.Length - 1;

                        if (faceVertexCount > MaxFaceVertices)
                        {
                            return Fail(
                                "Face trop complexe ligne " + lineNumber,
                                out mesh,
                                out error);
                        }

                        long addedTriangles =
                            (long)(faceVertexCount - 2) * 3L;

                        if ((long)mesh.Triangles.Count +
                            addedTriangles > MaxTriangles * 3L)
                        {
                            return Fail(
                                "Nombre maximal de triangles dépassé.",
                                out mesh,
                                out error);
                        }

                        List<int> face =
                            new List<int>(faceVertexCount);

                        for (int i = 1; i < parts.Length; i++)
                        {
                            FaceVertex parsed;

                            if (!TryParseFaceVertex(
                                parts[i],
                                sourceVertices.Count,
                                sourceUVs.Count,
                                sourceNormals.Count,
                                out parsed))
                            {
                                return Fail(
                                    "Index de face invalide ligne " +
                                    lineNumber,
                                    out mesh,
                                    out error);
                            }

                            string key =
                                parsed.Vertex + "/" +
                                parsed.UV + "/" +
                                parsed.Normal;

                            int outputIndex;

                            if (!vertexLookup.TryGetValue(
                                key,
                                out outputIndex))
                            {
                                if (mesh.Vertices.Count >=
                                    MaxOutputVertices)
                                {
                                    return Fail(
                                        "Nombre maximal de sommets générés dépassé.",
                                        out mesh,
                                        out error);
                                }

                                outputIndex = mesh.Vertices.Count;

                                vertexLookup.Add(key, outputIndex);

                                mesh.Vertices.Add(
                                    sourceVertices[parsed.Vertex]);

                                if (parsed.UV >= 0)
                                {
                                    mesh.UVs.Add(
                                        sourceUVs[parsed.UV]);
                                }
                                else
                                {
                                    mesh.UVs.Add(
                                        new ObjVector2(0f, 0f));
                                }

                                if (parsed.Normal >= 0)
                                {
                                    generatedNormals.Add(
                                        sourceNormals[parsed.Normal]);
                                }
                                else
                                {
                                    generatedNormals.Add(
                                        new ObjVector3(0f, 0f, 0f));

                                    everyVertexHasNormal = false;
                                }
                            }

                            face.Add(outputIndex);
                        }

                        RuntimeObjSubmeshData submesh =
                            mesh.GetOrCreateSubmesh(currentMaterial);

                        for (int i = 1; i < face.Count - 1; i++)
                        {
                            int first = face[0];
                            int second = face[i];
                            int third = face[i + 1];

                            mesh.Triangles.Add(first);
                            mesh.Triangles.Add(second);
                            mesh.Triangles.Add(third);

                            submesh.Triangles.Add(first);
                            submesh.Triangles.Add(second);
                            submesh.Triangles.Add(third);
                        }
                    }
                }

                if (!mesh.IsValid)
                {
                    return Fail(
                        "Le modèle ne contient pas assez de géométrie.",
                        out mesh,
                        out error);
                }

                for (int i = 0; i < mesh.Triangles.Count; i++)
                {
                    int index = mesh.Triangles[i];

                    if (index < 0 || index >= mesh.Vertices.Count)
                    {
                        return Fail(
                            "Indice de triangle invalide.",
                            out mesh,
                            out error);
                    }
                }

                if (mesh.Submeshes.Count == 0)
                {
                    return Fail(
                        "Aucun groupe de matériau n'a été généré.",
                        out mesh,
                        out error);
                }

                for (int i = 0; i < mesh.Submeshes.Count; i++)
                {
                    RuntimeObjSubmeshData submesh = mesh.Submeshes[i];

                    if (submesh == null ||
                        submesh.Triangles == null ||
                        submesh.Triangles.Count == 0 ||
                        submesh.Triangles.Count % 3 != 0)
                    {
                        return Fail(
                            "Groupe de matériau vide ou invalide.",
                            out mesh,
                            out error);
                    }
                }

                if (everyVertexHasNormal &&
                    generatedNormals.Count == mesh.Vertices.Count)
                {
                    mesh.Normals.AddRange(generatedNormals);
                    mesh.HasCompleteNormals = true;
                }

                return true;
            }
            catch (Exception exception)
            {
                return Fail(
                    "Erreur OBJ ligne " + lineNumber +
                    " : " + exception.Message,
                    out mesh,
                    out error);
            }
        }

        private static bool TryParseFaceVertex(
            string token,
            int vertexCount,
            int uvCount,
            int normalCount,
            out FaceVertex result)
        {
            result = null;

            if (string.IsNullOrWhiteSpace(token))
                return false;

            string[] indices = token.Split('/');

            if (indices.Length < 1 || indices.Length > 3)
                return false;

            int vertex;

            if (!TryResolveIndex(
                indices[0],
                vertexCount,
                out vertex))
            {
                return false;
            }

            int uv = -1;
            int normal = -1;

            if (indices.Length >= 2 &&
                !string.IsNullOrEmpty(indices[1]))
            {
                if (!TryResolveIndex(
                    indices[1],
                    uvCount,
                    out uv))
                {
                    return false;
                }
            }

            if (indices.Length == 3 &&
                !string.IsNullOrEmpty(indices[2]))
            {
                if (!TryResolveIndex(
                    indices[2],
                    normalCount,
                    out normal))
                {
                    return false;
                }
            }

            result = new FaceVertex
            {
                Vertex = vertex,
                UV = uv,
                Normal = normal
            };

            return true;
        }

        private static bool TryResolveIndex(
            string value,
            int count,
            out int index)
        {
            index = -1;

            int parsed;

            if (!int.TryParse(
                value,
                NumberStyles.Integer,
                Invariant,
                out parsed))
            {
                return false;
            }

            if (parsed == 0)
                return false;

            long resolved = parsed > 0
                ? (long)parsed - 1L
                : (long)count + parsed;

            if (resolved < 0 || resolved >= count)
                return false;

            index = (int)resolved;
            return true;
        }

        private static float ParseFloat(string value)
        {
            float result = float.Parse(
                value,
                NumberStyles.Float,
                Invariant);

            if (float.IsNaN(result) ||
                float.IsInfinity(result))
            {
                throw new FormatException(
                    "Coordonnée non finie.");
            }

            return result;
        }

        private static bool Fail(
            string message,
            out RuntimeObjMeshData mesh,
            out string error)
        {
            mesh = null;
            error = message;
            return false;
        }

        public static string[] GetAvailableModels()
        {
            Assembly assembly =
                typeof(RuntimeObjMeshLoader).Assembly;

            string[] resources =
                assembly.GetManifestResourceNames();

            List<string> models =
                new List<string>();

            for (int i = 0; i < resources.Length; i++)
            {
                string resource = resources[i];

                if (!resource.StartsWith(
                    ResourcePrefix,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                if (!resource.EndsWith(
                    ".obj",
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string name = resource.Substring(
                    ResourcePrefix.Length,
                    resource.Length -
                    ResourcePrefix.Length - 4);

                models.Add(name);
            }

            models.Sort(StringComparer.OrdinalIgnoreCase);

            return models.ToArray();
        }
    }
}
