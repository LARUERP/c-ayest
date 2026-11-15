
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Telephone;

public sealed class PhoneCamera : IDisposable
{
    private GameObject go;
    private Camera camera;
    private RenderTexture preview;
    private RenderTexture live;
    private RenderTexture photo;
    private Texture2D smallPixels;
    private Texture2D largePixels;
    private bool disposed;
    private readonly Action<string> warn;

    public bool Busy { get; private set; }
    public RenderTexture Preview => preview;
    public RenderTexture Live => live;

    public PhoneCamera(Action<string> warn)
    {
        this.warn = warn;
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
            camera.stereoTargetEye = (StereoTargetEyeMask)0;
            camera.fieldOfView = 70f;
            camera.nearClipPlane = 0.04f;
            camera.farClipPlane = 200f;
            camera.cullingMask = -33;
            camera.clearFlags = (CameraClearFlags)1;
            camera.allowHDR = false;
            camera.allowMSAA = true;

            preview = new RenderTexture(320, 240, 24, (RenderTextureFormat)0);
            preview.antiAliasing = 4;
            preview.Create();

            live = new RenderTexture(320, 240, 24, (RenderTextureFormat)0);
            live.antiAliasing = 4;
            live.Create();

            smallPixels = new Texture2D(320, 240, (TextureFormat)3, false);
        }

        if (large && photo == null)
        {
            photo = new RenderTexture(640, 480, 24, (RenderTextureFormat)0);
            photo.antiAliasing = 4;
            photo.Create();

            largePixels = new Texture2D(640, 480, (TextureFormat)3, false);
        }

        Transform cameraPose = screen.CameraPose;
        Vector3 direction = front ? -cameraPose.forward : cameraPose.forward;

        camera.transform.position = cameraPose.position + direction * 0.05f;
        camera.transform.rotation = Quaternion.LookRotation(direction, cameraPose.up);

        camera.targetTexture = large ? photo : (photoPreview ? preview : live);
        camera.aspect = 1.3333334f;

        Busy = true;
        camera.enabled = true;

        try
        {
            MelonCoroutines.Start(
                ReadFrame(large, encode, completed, camera.targetTexture));
        }
        catch
        {
            camera.enabled = false;
            Busy = false;
            throw;
        }

        return true;
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

        camera = null;
        go = null;
        preview = null;
        live = null;
        photo = null;
        smallPixels = null;
        largePixels = null;
    }
}
