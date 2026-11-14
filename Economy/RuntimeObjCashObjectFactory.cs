
using System;
using UnityEngine;

namespace FusionDarkRP.Economy
{
    public static class RuntimeObjCashObjectFactory
    {
        public static bool TryCreate(
            string modelName,
            Material material,
            out GameObject cashObject,
            out string error)
        {
            cashObject = null;
            error = null;

            if (string.IsNullOrWhiteSpace(modelName))
            {
                error = "Nom du modèle invalide.";
                return false;
            }

            if (material == null)
            {
                error = "Le matériau fourni est invalide.";
                return false;
            }

            RuntimeObjMeshData meshData;

            if (!RuntimeObjMeshLoader.TryLoad(
                modelName,
                out meshData,
                out error))
            {
                return false;
            }

            Mesh mesh;

            if (!RuntimeObjUnityMeshBuilder.TryBuild(
                meshData,
                out mesh,
                out error))
            {
                return false;
            }

            GameObject createdObject = null;

            try
            {
                createdObject = new GameObject(
                    "FusionDarkRP_Cash_" + modelName);

                MeshFilter meshFilter =
                    createdObject.AddComponent<MeshFilter>();

                MeshRenderer meshRenderer =
                    createdObject.AddComponent<MeshRenderer>();

                MeshCollider meshCollider =
                    createdObject.AddComponent<MeshCollider>();

                Rigidbody rigidbody =
                    createdObject.AddComponent<Rigidbody>();

                meshFilter.sharedMesh = mesh;

                meshRenderer.sharedMaterial = material;

                meshCollider.sharedMesh = mesh;
                meshCollider.convex = true;

                rigidbody.useGravity = true;
                rigidbody.isKinematic = false;

                cashObject = createdObject;
                return true;
            }
            catch (Exception exception)
            {
                if (createdObject != null)
                {
                    UnityEngine.Object.Destroy(createdObject);
                }

                if (mesh != null)
                {
                    UnityEngine.Object.Destroy(mesh);
                }

                cashObject = null;

                error =
                    "Erreur de création de l'objet physique : " +
                    exception.Message;

                return false;
            }
        }

        public static void Destroy(GameObject cashObject)
        {
            if (cashObject != null)
            {
                UnityEngine.Object.Destroy(cashObject);
            }
        }
    }
}
