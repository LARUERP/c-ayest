
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Telephone;

/// <summary>
/// Caméra du téléphone (photo, aperçu, visio).
///
/// Correctif « écran noir » : sur Quest, une caméra secondaire rendue dans une
/// RenderTexture MSAA, ou créée « à froid » sans les réglages de la caméra du
/// joueur, donne parfois une image entièrement noire. Cette version :
///  - n'utilise plus de MSAA sur les RenderTextures ;
///  - contrôle chaque image (échantillons de pixels) et, si elle est noire,
///    bascule automatiquement vers une stratégie de repli puis REFAIT le rendu ;
///  - journalise la stratégie retenue (« Caméra téléphone : stratégie N ») pour
///    qu'on sache laquelle fonctionne sur le casque.
/// Stratégies : 0 = caméra activée (rendu normal de l'image) ;
///              1 = Render() explicite avec les réglages de la caméra du joueur ;
///              2 = Render() explicite, fond uni, tous les calques.
/// </summary>
public sealed class PhoneCamera : IDisposable
{
    private GameObject go;
    private Camera camera;
    private RenderTexture preview;
    private RenderTexture live;
    private RenderTexture photo;
    private Texture2D smallPixels;
    private Texture2D largePixels;
    private Texture2D probePixels;
    private bool disposed;
    private readonly Action<string> warn;

    private int strategy;
    private int probes;

    public bool Busy { get; private set; }
    public RenderTexture Preview => preview;
    public RenderTexture Live => live;

    public PhoneCamera(Action<string> warn)
    {
        this.warn = warn;
    }

    private static RenderTexture NewTexture(int width, int height)
    {
        RenderTexture texture = new RenderTexture(width, height, 24, (RenderTextureFormat)0);
        texture.antiAliasing = 1;
        texture.useMipMap = false;
        texture.autoGenerateMips = false;
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.Create();
        return texture;
    }

    private void ApplyStrategy()
    {
        camera.stereoTargetEye = (StereoTargetEyeMask)0;
        camera.fieldOfView = 70f;
        camera.nearClipPlane = 0.04f;
        camera.farClipPlane = 200f;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.useOcclusionCulling = false;
        camera.depth = -50f;
        camera.rect = new Rect(0f, 0f, 1f, 1f);

        Camera main = Camera.main;

        if (strategy == 1 && main != null)
        {
            // Reprend skybox, fond et paramètres de la caméra du joueur.
            camera.CopyFrom(main);

            camera.stereoTargetEye = (StereoTargetEyeMask)0;
            camera.fieldOfView = 70f;
            camera.nearClipPlane = 0.04f;
            camera.farClipPlane = 200f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.depth = -50f;
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.cullingMask = main.cullingMask & ~(1 << 5);
        }
        else if (strategy >= 2)
        {
            camera.clearFlags = (CameraClearFlags)2;
            camera.backgroundColor = new Color(0.18f, 0.2f, 0.24f, 1f);
            camera.cullingMask = -1 & ~(1 << 5);
        }
        else
        {
            camera.cullingMask = -33;
            camera.clearFlags = (CameraClearFlags)1;
        }
    }

    public bool Capture(
        PhoneSurface screen,
        bool large,
        bool front,
        bool encode,
        Action<byte[]> completed,
        bool photoPreview = false,
        bool allowUnheld = false)
    {
        if (disposed || Busy || screen == null
            || screen.Body == null
            || (!screen.IsHeld && !allowUnheld)
            || !screen.PlacementReady)
        {
            return false;
        }

        if (camera == null)
        {
            go = new GameObject("RP_LocalPhoneCamera");
            camera = go.AddComponent<Camera>();
            camera.enabled = false;

            preview = NewTexture(320, 240);
            live = NewTexture(320, 240);

            smallPixels = new Texture2D(320, 240, (TextureFormat)3, false);
            probePixels = new Texture2D(16, 16, (TextureFormat)3, false);
        }

        if (large && photo == null)
        {
            photo = NewTexture(640, 480);
            largePixels = new Texture2D(640, 480, (TextureFormat)3, false);
        }

        RenderTexture target = large ? photo : (photoPreview ? preview : live);

        if (target != null && !target.IsCreated())
        {
            target.Create();
        }

        Transform cameraPose = screen.CameraPose;
        Vector3 direction = front ? -cameraPose.forward : cameraPose.forward;

        ApplyStrategy();

        camera.transform.position = cameraPose.position + direction * 0.05f;
        camera.transform.rotation = Quaternion.LookRotation(direction, cameraPose.up);

        camera.targetTexture = target;
        camera.aspect = 1.3333334f;

        Busy = true;

        // Stratégie 0 : la caméra est rendue par le moteur à la fin de cette image.
        // Stratégies 1 et 2 : rendu explicite dans ReadFrame.
        camera.enabled = strategy == 0;

        try
        {
            MelonCoroutines.Start(ReadFrame(large, encode, completed, target));
        }
        catch
        {
            camera.enabled = false;
            Busy = false;
            throw;
        }

        return true;
    }

    /// <summary>Vrai si l'image contient du « vrai » contenu (pas uniquement du noir).</summary>
    private bool HasContent(RenderTexture target)
    {
        if (target == null || probePixels == null)
        {
            return true;
        }

        RenderTexture previous = RenderTexture.active;

        try
        {
            RenderTexture.active = target;

            float[] xs = { 0.5f, 0.25f, 0.75f, 0.5f, 0.5f };
            float[] ys = { 0.5f, 0.5f, 0.5f, 0.25f, 0.75f };

            for (int i = 0; i < xs.Length; i++)
            {
                float x = Mathf.Clamp(target.width * xs[i] - 8f, 0f, target.width - 16f);
                float y = Mathf.Clamp(target.height * ys[i] - 8f, 0f, target.height - 16f);

                probePixels.ReadPixels(new Rect(x, y, 16f, 16f), 0, 0, false);

                var pixels = probePixels.GetPixels32();

                for (int p = 0; p < pixels.Length; p += 3)
                {
                    if (pixels[p].r > 6 || pixels[p].g > 6 || pixels[p].b > 6)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        finally
        {
            RenderTexture.active = previous;
        }
    }

    private IEnumerator ReadFrame(
        bool large,
        bool encode,
        Action<byte[]> completed,
        RenderTexture target)
    {
        yield return new WaitForEndOfFrame();

        if (disposed)
        {
            Busy = false;
            yield break;
        }

        try
        {
            camera.enabled = false;
            camera.targetTexture = target;

            if (strategy >= 1)
            {
                camera.Render();
            }

            // Contrôle d'image noire : toujours pour une photo, puis sur les
            // premières images de l'aperçu / de la visio.
            if (strategy < 2 && (encode || probes < 8))
            {
                probes++;

                if (!HasContent(target))
                {
                    strategy++;

                    warn?.Invoke(
                        "Caméra téléphone : image noire, bascule vers la stratégie "
                        + strategy + ".");

                    ApplyStrategy();
                    camera.targetTexture = target;
                    camera.Render();
                }
                else if (probes == 1)
                {
                    warn?.Invoke("Caméra téléphone : stratégie " + strategy + " active.");
                }
            }

            if (!encode)
            {
                completed?.Invoke(null);
                yield break;
            }

            Texture2D pixels = large ? largePixels : smallPixels;
            RenderTexture previous = RenderTexture.active;

            try
            {
                RenderTexture.active = target;

                pixels.ReadPixels(
                    new Rect(0f, 0f, large ? 640f : 320f, large ? 480f : 240f),
                    0,
                    0,
                    false);

                pixels.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = previous;
            }

            byte[] bytes = null;
            int cap = large ? 65536 : 16384;

            int[] qualities = large
                ? new int[] { 70, 45, 25 }
                : new int[] { 30, 20, 12 };

            foreach (int quality in qualities)
            {
                bytes = ImageConversion.EncodeToJPG(pixels, quality);

                if (bytes.Length <= cap)
                {
                    break;
                }
            }

            if (bytes.Length > cap
                || !PhoneMediaRules.Jpeg(
                    bytes,
                    large ? 640 : 320,
                    large ? 480 : 240))
            {
                throw new InvalidOperationException("Image trop volumineuse.");
            }

            completed?.Invoke(bytes);
        }
        catch (Exception ex)
        {
            warn?.Invoke("Caméra téléphone : " + ex.Message);

            PhoneMod.Instance?.LocalStatus(
                "Capture impossible : " + ex.Message);
        }
        finally
        {
            if (camera != null)
            {
                camera.enabled = false;
            }

            Busy = false;
        }
    }

    public void Dispose()
    {
        disposed = true;

        if (camera != null)
        {
            camera.enabled = false;
        }

        if (go != null)
        {
            UnityEngine.Object.Destroy(go);
        }

        RenderTexture[] textures = { preview, live, photo };

        foreach (RenderTexture texture in textures)
        {
            if (texture != null)
            {
                texture.Release();
                UnityEngine.Object.Destroy(texture);
            }
        }

        if (smallPixels != null)
        {
            UnityEngine.Object.Destroy(smallPixels);
        }

        if (largePixels != null)
        {
            UnityEngine.Object.Destroy(largePixels);
        }

        if (probePixels != null)
        {
            UnityEngine.Object.Destroy(probePixels);
        }

        camera = null;
        go = null;
        preview = null;
        live = null;
        photo = null;
        smallPixels = null;
        largePixels = null;
        probePixels = null;
    }
}
