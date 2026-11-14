
using System;
using System.Collections.Generic;
using System.Text;

namespace FusionDarkRP.Economy
{
    /// <summary>
    /// Vérifie la présence et la validité géométrique
    /// de tous les modèles OBJ déclarés dans le catalogue.
    /// </summary>
    public static class RuntimeObjWorldModelValidator
    {
        public static bool ValidateAll(out string report)
        {
            StringBuilder output = new StringBuilder();

            int validCount = 0;
            int failedCount = 0;

            string[] catalogIds =
                RuntimeObjWorldModelCatalog.GetAllIds();

            string[] embeddedModels =
                RuntimeObjMeshLoader.GetAvailableModels();

            if (catalogIds == null)
                catalogIds = new string[0];

            if (embeddedModels == null)
                embeddedModels = new string[0];

            HashSet<string> embedded =
                new HashSet<string>(
                    embeddedModels,
                    StringComparer.OrdinalIgnoreCase);

            output.AppendLine(
                "[FusionDarkRP OBJ] Vérification des modèles 3D."
            );

            output.AppendLine(
                "Modèles déclarés : " + catalogIds.Length
            );

            output.AppendLine(
                "Fichiers OBJ intégrés : " + embeddedModels.Length
            );

            for (int i = 0; i < catalogIds.Length; i++)
            {
                string modelId = catalogIds[i];

                if (string.IsNullOrWhiteSpace(modelId))
                {
                    failedCount++;

                    output.AppendLine(
                        "[INVALIDE] Identifiant de modèle vide."
                    );

                    continue;
                }

                try
                {
                    if (!embedded.Contains(modelId))
                    {
                        failedCount++;

                        output.AppendLine(
                            "[ABSENT] " + modelId
                        );

                        continue;
                    }

                    RuntimeObjMeshData mesh;
                    string error;

                    if (!RuntimeObjMeshLoader.TryLoad(
                        modelId,
                        out mesh,
                        out error))
                    {
                        failedCount++;

                        output.AppendLine(
                            "[ERREUR] " + modelId
                            + " : "
                            + (string.IsNullOrWhiteSpace(error)
                                ? "Chargement impossible."
                                : error)
                        );

                        continue;
                    }

                    if (mesh == null || !mesh.IsValid)
                    {
                        failedCount++;

                        output.AppendLine(
                            "[INVALIDE] " + modelId
                        );

                        continue;
                    }

                    int vertexCount =
                        mesh.Vertices != null
                            ? mesh.Vertices.Count
                            : 0;

                    int triangleCount =
                        mesh.Triangles != null
                            ? mesh.Triangles.Count / 3
                            : 0;

                    validCount++;

                    output.AppendLine(
                        "[OK] " + modelId
                        + " | Sommets : " + vertexCount
                        + " | Triangles : " + triangleCount
                    );
                }
                catch (Exception exception)
                {
                    failedCount++;

                    output.AppendLine(
                        "[EXCEPTION] " + modelId
                        + " : " + exception.Message
                    );
                }
            }

            output.AppendLine(
                "Résultat : "
                + validCount + " valide(s), "
                + failedCount + " échec(s)."
            );

            report = output.ToString();

            return failedCount == 0
                && validCount == catalogIds.Length;
        }

        public static bool TryValidateModel(
            string modelId,
            out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(modelId))
            {
                error = "Identifiant du modèle manquant.";
                return false;
            }

            try
            {
                WorldModelDefinition definition;

                if (!RuntimeObjWorldModelCatalog.TryGet(
                    modelId,
                    out definition))
                {
                    error = "Modèle absent du catalogue.";
                    return false;
                }

                if (definition == null
                    || string.IsNullOrWhiteSpace(definition.Id))
                {
                    error = "Définition du modèle invalide.";
                    return false;
                }

                RuntimeObjMeshData mesh;

                if (!RuntimeObjMeshLoader.TryLoad(
                    definition.Id,
                    out mesh,
                    out error))
                {
                    if (string.IsNullOrWhiteSpace(error))
                        error = "Chargement du modèle impossible.";

                    return false;
                }

                if (mesh == null || !mesh.IsValid)
                {
                    error = "Géométrie OBJ invalide.";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Exception pendant la validation du modèle : "
                    + exception.Message;

                return false;
            }
        }
    }
}
