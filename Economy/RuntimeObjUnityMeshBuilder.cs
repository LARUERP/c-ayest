
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace FusionDarkRP.Economy
{
    /// <summary>
    /// Convertit les données OBJ en Mesh Unity.
    /// Conserve les groupes de triangles par matériau.
    /// </summary>
    public static class RuntimeObjUnityMeshBuilder
    {
        public static bool TryBuild(
            RuntimeObjMeshData source,
            out Mesh mesh,
            out string error)
        {
            mesh = null;
            error = null;

            Mesh result = null;

            try
            {
                if (source == null)
                {
                    error = "Source OBJ inexistante.";
                    return false;
                }

                if (!source.IsValid)
                {
                    error = "Le modèle OBJ est invalide.";
                    return false;
                }

                if (source.Vertices == null ||
                    source.Triangles == null)
                {
                    error = "Données géométriques manquantes.";
                    return false;
                }

                int vertexCount = source.Vertices.Count;
                int triangleCount = source.Triangles.Count;

                if (vertexCount < 3)
                {
                    error = "Nombre de sommets insuffisant.";
                    return false;
                }

                if (triangleCount < 3 ||
                    triangleCount % 3 != 0)
                {
                    error = "Liste de triangles invalide.";
                    return false;
                }

                Vector3[] vertices =
                    new Vector3[vertexCount];

                for (int i = 0; i < vertexCount; i++)
                {
                    ObjVector3 vertex = source.Vertices[i];

                    if (!IsFinite(vertex.X) ||
                        !IsFinite(vertex.Y) ||
                        !IsFinite(vertex.Z))
                    {
                        error =
                            "Sommet non fini détecté : " + i;

                        return false;
                    }

                    vertices[i] = new Vector3(
                        vertex.X,
                        vertex.Y,
                        vertex.Z);
                }

                int[] globalTriangles =
                    source.Triangles.ToArray();

                for (int i = 0; i < globalTriangles.Length; i++)
                {
                    if (globalTriangles[i] < 0 ||
                        globalTriangles[i] >= vertexCount)
                    {
                        error =
                            "Indice de triangle invalide : " + i;

                        return false;
                    }
                }

                result = new Mesh();

                result.name =
                    "FusionDarkRP_" +
                    (string.IsNullOrWhiteSpace(source.ModelName)
                        ? "RuntimeOBJ"
                        : source.ModelName);

                result.indexFormat =
                    vertexCount > 65535
                        ? IndexFormat.UInt32
                        : IndexFormat.UInt16;

                result.vertices = vertices;

                // Construction des sous-meshes.
                // Chaque groupe conserve son matériau OBJ.
                if (source.Submeshes != null &&
                    source.Submeshes.Count > 0)
                {
                    int submeshCount = source.Submeshes.Count;

                    result.subMeshCount = submeshCount;

                    int totalSubmeshIndices = 0;

                    for (int submeshIndex = 0;
                         submeshIndex < submeshCount;
                         submeshIndex++)
                    {
                        RuntimeObjSubmeshData submesh =
                            source.Submeshes[submeshIndex];

                        if (submesh == null ||
                            submesh.Triangles == null ||
                            submesh.Triangles.Count < 3 ||
                            submesh.Triangles.Count % 3 != 0)
                        {
                            error =
                                "Sous-mesh invalide : "
                                + submeshIndex;

                            return false;
                        }

                        int[] triangles =
                            submesh.Triangles.ToArray();

                        totalSubmeshIndices += triangles.Length;

                        for (int i = 0; i < triangles.Length; i++)
                        {
                            if (triangles[i] < 0 ||
                                triangles[i] >= vertexCount)
                            {
                                error =
                                    "Indice invalide dans le sous-mesh "
                                    + submeshIndex;

                                return false;
                            }
                        }

                        // Même correction d'orientation
                        // que pour les anciens modèles.
                        ReverseTriangleWinding(triangles);

                        result.SetTriangles(
                            triangles,
                            submeshIndex,
                            false);
                    }

                    if (totalSubmeshIndices != triangleCount)
                    {
                        error =
                            "Les groupes de matériaux ne correspondent "
                            + "pas à la géométrie totale.";

                        return false;
                    }
                }
                else
                {
                    // Compatibilité avec les données anciennes.
                    int[] triangles =
                        (int[])globalTriangles.Clone();

                    ReverseTriangleWinding(triangles);

                    result.subMeshCount = 1;

                    result.SetTriangles(
                        triangles,
                        0,
                        false);
                }

                // Coordonnées UV.
                if (source.UVs != null &&
                    source.UVs.Count > 0)
                {
                    if (source.UVs.Count != vertexCount)
                    {
                        error =
                            "Nombre de coordonnées UV incompatible "
                            + "avec le nombre de sommets.";

                        return false;
                    }

                    Vector2[] uvs =
                        new Vector2[vertexCount];

                    for (int i = 0; i < vertexCount; i++)
                    {
                        ObjVector2 uv = source.UVs[i];

                        if (!IsFinite(uv.X) ||
                            !IsFinite(uv.Y))
                        {
                            error =
                                "Coordonnée UV invalide : " + i;

                            return false;
                        }

                        uvs[i] = new Vector2(
                            uv.X,
                            uv.Y);
                    }

                    result.uv = uvs;
                }

                // Normales OBJ.
                bool normalsApplied = false;

                if (source.HasCompleteNormals &&
                    source.Normals != null &&
                    source.Normals.Count == vertexCount)
                {
                    Vector3[] normals =
                        new Vector3[vertexCount];

                    bool validNormals = true;

                    for (int i = 0; i < vertexCount; i++)
                    {
                        ObjVector3 normal = source.Normals[i];

                        if (!IsFinite(normal.X) ||
                            !IsFinite(normal.Y) ||
                            !IsFinite(normal.Z))
                        {
                            validNormals = false;
                            break;
                        }

                        Vector3 converted = new Vector3(
                            normal.X,
                            normal.Y,
                            normal.Z);

                        if (converted.sqrMagnitude < 0.000001f)
                        {
                            validNormals = false;
                            break;
                        }

                        normals[i] = -converted.normalized;
                    }

                    if (validNormals)
                    {
                        result.normals = normals;
                        normalsApplied = true;
                    }
                }

                // Si les normales OBJ sont absentes ou invalides,
                // Unity les recalcule depuis les triangles corrigés.
                if (!normalsApplied)
                {
                    result.RecalculateNormals();
                }

                result.RecalculateBounds();

                if (result.vertexCount != vertexCount)
                {
                    error =
                        "Vérification finale du mesh échouée.";

                    return false;
                }

                if (result.triangles == null ||
                    result.triangles.Length != triangleCount)
                {
                    error =
                        "Vérification finale des triangles échouée.";

                    return false;
                }

                mesh = result;
                result = null;

                error = null;
                return true;
            }
            catch (Exception exception)
            {
                mesh = null;

                error =
                    "Erreur de construction du mesh : "
                    + exception.Message;

                return false;
            }
            finally
            {
                if (result != null)
                {
                    SafeDestroy(result);
                }
            }
        }

        private static void ReverseTriangleWinding(
            int[] triangles)
        {
            if (triangles == null)
                return;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int temporary = triangles[i + 1];

                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = temporary;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }

        private static void SafeDestroy(Mesh target)
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
                    "[FusionDarkRP] Impossible de libérer le mesh : "
                    + exception.Message);
            }
        }
    }
}
