using System;
using System.Collections.Generic;
using BoneLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSLZ.Bonelab;
using Il2CppSLZ.Marrow;
using Il2CppTMPro;
using LabFusion.UI;
using LabFusion.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace FusionDarkRP.Telephone;

public sealed class PhoneSurface : IDisposable
{
    public readonly GameObject Body;

    private readonly GameObject root;
    private readonly GameObject canvasObject;
    private GameObject content;
    private LaserCursor cursor;
    private bool disposed;
    private readonly Action<string> warn;
    private Texture2D roundedTexture;
    private Sprite roundedSprite;
    private readonly float created = Time.realtimeSinceStartup;
    private bool cursorWarning;
    private readonly ScreenSettings settings;
    private bool wasHeld;
    private bool placed;
    private bool faceChosen;
    private bool firstPickup = true;
    private Vector3 fixedNormal;
    private Vector3 previousMin;
    private Vector3 previousMax;
    private Vector3 previousScale;
    private int stableSamples;
    private float nextPlacement;
    private float placementUntil;
    private Vector3 fitPosition;
    private Vector3 fitWorldScale;
    private Quaternion fitRotation;
    private readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();

    public bool HoldsLeft => !disposed && (Object)(object)Body != (Object)null && Held(Player.LeftHand);

    public bool HoldsRight => !disposed && (Object)(object)Body != (Object)null && Held(Player.RightHand);

    public bool IsHeld => !disposed && (Object)(object)Body != (Object)null && (Held(Player.LeftHand) || Held(Player.RightHand));

    public bool PlacementReady => placed;

    public Transform CameraPose => root.transform;

    public string PlacementInfo { get; private set; } = "";

    public Color TextColor { get; set; } = Color.white;

    public PhoneSurface(GameObject body, ScreenSettings settings, Action<string> warn)
    {
        Body = body;
        this.warn = warn;
        this.settings = settings;
        placementUntil = created + 3f;

        root = new GameObject("RP_PrivatePhoneScreen");
        root.transform.SetParent(body.transform, false);

        try
        {
            canvasObject = new GameObject("CANVAS");
            canvasObject.layer = 5;

            RectTransform rect = canvasObject.AddComponent<RectTransform>();
            ((Transform)rect).SetParent(root.transform, false);
            rect.sizeDelta = new Vector2(600f, 1000f);

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = (RenderMode)2;

            canvasObject.AddComponent<GraphicRaycaster>();

            CreateRoundedSprite();

            canvasObject.SetActive(false);
            root.SetActive(false);

            LaserCursorUtilities.CreateLaserCursor((Action<LaserCursor>)delegate(LaserCursor ready)
            {
                if (disposed || (Object)(object)Body == (Object)null)
                {
                    Object.Destroy((Object)(object)((Component)ready).gameObject);
                }
                else
                {
                    cursor = ready;
                    ((Component)cursor).transform.SetParent(root.transform, false);
                    ((Component)cursor).transform.localPosition = Vector3.zero;
                    ((Component)cursor).transform.localRotation = Quaternion.identity;
                    UpdateCursorRegion();
                    ((Component)cursor).gameObject.SetActive(true);
                }
            });
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private bool Place(ScreenSettings s)
    {
        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        bool found = false;
        int meshCount = 0;

        Vector3 difference;
        Vector3 corner;

        foreach (Renderer renderer in Body.GetComponentsInChildren<Renderer>())
        {
            if ((Object)(object)renderer == (Object)null ||
                ((Component)renderer).transform.IsChildOf(root.transform))
            {
                continue;
            }

            MeshFilter meshFilter = ((Component)renderer).GetComponent<MeshFilter>();
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;

            if ((Object)(object)mesh == (Object)null)
            {
                SkinnedMeshRenderer skinned =
                    ((Component)renderer).GetComponent<SkinnedMeshRenderer>();

                mesh = skinned != null ? skinned.sharedMesh : null;
            }

            if ((Object)(object)mesh == (Object)null)
            {
                continue;
            }

            Bounds bounds = mesh.bounds;
            difference = bounds.size;

            if (!(difference.sqrMagnitude < 1E-06f))
            {
                meshCount++;

                for (int i = 0; i < 8; i++)
                {
                    corner = new Vector3(
                        ((i & 1) == 0) ? bounds.min.x : bounds.max.x,
                        ((i & 2) == 0) ? bounds.min.y : bounds.max.y,
                        ((i & 4) == 0) ? bounds.min.z : bounds.max.z
                    );

                    Vector3 localPoint = Body.transform.InverseTransformPoint(
                        ((Component)renderer).transform.TransformPoint(corner)
                    );

                    min = Vector3.Min(min, localPoint);
                    max = Vector3.Max(max, localPoint);
                    found = true;
                }
            }
        }

        if (!found)
        {
            stableSamples = 0;
            return false;
        }

        Vector3 lossyScale = Body.transform.lossyScale;

        difference = min - previousMin;

        if (difference.sqrMagnitude < 1E-08f)
        {
            difference = max - previousMax;

            if (difference.sqrMagnitude < 1E-08f)
            {
                difference = lossyScale - previousScale;

                if (difference.sqrMagnitude < 1E-08f)
                {
                    stableSamples++;
                    goto PlacementStable;
                }
            }
        }

        stableSamples = 0;

    PlacementStable:

        previousMin = min;
        previousMax = max;
        previousScale = lossyScale;

        if (stableSamples < 2)
        {
            return false;
        }

        Vector3 dimensions = max - min;
        Vector3 center = (min + max) * 0.5f;

        int shortestAxis =
            (!(dimensions.x <= dimensions.y) || !(dimensions.x <= dimensions.z))
                ? ((dimensions.y <= dimensions.z) ? 1 : 2)
                : 0;

        Vector3 normal = shortestAxis switch
        {
            1 => Vector3.up,
            0 => Vector3.right,
            _ => Vector3.forward,
        };

        Vector3 up =
            (shortestAxis != 1)
                ? ((dimensions.y >= ((shortestAxis == 0) ? dimensions.z : dimensions.x))
                    ? Vector3.up
                    : ((shortestAxis == 0) ? Vector3.forward : Vector3.right))
                : ((dimensions.z >= dimensions.x) ? Vector3.forward : Vector3.right);

        Transform head = Player.Head;

        if (!faceChosen && (Object)(object)head != (Object)null)
        {
            Vector3 worldNormal = Body.transform.TransformDirection(normal);
            Vector3 toHead = head.position - Body.transform.TransformPoint(center);

            if (Vector3.Dot(worldNormal, toHead) < 0f)
            {
                normal = -normal;
            }
        }

        if (!faceChosen)
        {
            if (s.ReverseFace)
            {
                normal = -normal;
            }

            fixedNormal = normal;
            faceChosen = true;
        }
        else
        {
            normal = fixedNormal;
        }

        float thickness = shortestAxis switch
        {
            1 => dimensions.y,
            0 => dimensions.x,
            _ => dimensions.z,
        };

        float width = Mathf.Max(
            1E-06f,
            Mathf.Min(
                shortestAxis switch
                {
                    1 => Mathf.Min(dimensions.x, dimensions.z),
                    0 => Mathf.Min(dimensions.y, dimensions.z),
                    _ => Mathf.Min(dimensions.x, dimensions.y),
                } * 0.78f,
                shortestAxis switch
                {
                    1 => Mathf.Max(dimensions.x, dimensions.z),
                    0 => Mathf.Max(dimensions.y, dimensions.z),
                    _ => Mathf.Max(dimensions.x, dimensions.y),
                } * 0.78f * 0.6f
            )
        ) * s.SizeMultiplier;

        root.transform.localScale = Vector3.one;

        root.transform.localPosition =
            center +
            normal * (thickness * 0.5f + 0.002f) +
            new Vector3(s.X, s.Y, s.Z);

        root.transform.localRotation =
            Quaternion.LookRotation(-normal, up) *
            Quaternion.Euler(s.RotationX, s.RotationY, s.RotationZ);

        canvasObject.transform.localPosition = Vector3.zero;
        canvasObject.transform.localRotation = Quaternion.identity;
        canvasObject.transform.localScale = Vector3.one * (width / 600f);

        fitPosition = root.transform.localPosition;
        fitRotation = root.transform.localRotation;
        fitWorldScale = canvasObject.transform.lossyScale;

        PlacementInfo =
            $"maillages={meshCount}, centre local={center}, dimensions={dimensions}, largeur UI={width:F4} m, face={normal}";

        UpdateCursorRegion();

        return true;
    }

    private void UpdateCursorRegion()
    {
        if (!((Object)(object)cursor == (Object)null))
        {
            cursor.regions = new LaserCursor.CursorRegion[]
            {
                new LaserCursor.CursorRegion
                {
                    bounds = new Bounds(
                        Vector3.zero,
                        Vector3.Scale(
                            new Vector3(600f, 1000f, 80f),
                            canvasObject.transform.lossyScale
                        )
                    ),
                    center = canvasObject.transform
                }
            };
        }
    }

    private void CreateRoundedSprite()
    {
        roundedTexture = new Texture2D(128, 128, (TextureFormat)4, false);

        ((Object)roundedTexture).name = "RP_PhoneRoundedCorners";
        ((Texture)roundedTexture).filterMode = (FilterMode)1;
        ((Texture)roundedTexture).wrapMode = (TextureWrapMode)1;

        Color[] pixels = new Color[16384];

        for (int i = 0; i < 128; i++)
        {
            for (int j = 0; j < 128; j++)
            {
                float x = MathF.Max(MathF.Abs((float)j + 0.5f - 64f) - 36f, 0f);
                float y = MathF.Max(MathF.Abs((float)i + 0.5f - 64f) - 36f, 0f);

                float alpha = Math.Clamp(
                    0.5f - (MathF.Sqrt(x * x + y * y) - 28f),
                    0f,
                    1f
                );

                pixels[i * 128 + j] = new Color(1f, 1f, 1f, alpha);
            }
        }

        roundedTexture.SetPixels(pixels);
        roundedTexture.Apply(false, false);

        roundedSprite = Sprite.Create(
            roundedTexture,
            new Rect(0f, 0f, 128f, 128f),
            new Vector2(0.5f, 0.5f),
            100f,
            0u,
            (SpriteMeshType)0,
            new Vector4(28f, 28f, 28f, 28f)
        );
    }

    public void Clear(Color? background = null)
    {
        if ((Object)(object)content != (Object)null)
        {
            content.SetActive(false);
            Object.Destroy((Object)(object)content);
        }

        content = new GameObject("PAGE");
        content.layer = 5;

        ((Transform)content.AddComponent<RectTransform>())
            .SetParent(canvasObject.transform, false);

        Image image = Element("Background", 0f, 0f, 600f, 1000f)
            .AddComponent<Image>();

        image.sprite = roundedSprite;
        image.type = Image.Type.Sliced;

        ((Graphic)image).color =
            background ?? new Color(0.025f, 0.04f, 0.065f, 1f);

        ((Graphic)image).raycastTarget = false;
    }

    private GameObject Element(
        string name,
        float x,
        float y,
        float width,
        float height)
    {
        GameObject element = new GameObject(name);
        element.layer = 5;

        RectTransform rect = element.AddComponent<RectTransform>();
        ((Transform)rect).SetParent(content.transform, false);

        Vector2 pivot = new Vector2(0.5f, 0.5f);

        rect.pivot = pivot;

        Vector2 anchorMin = (rect.anchorMax = pivot);
        rect.anchorMin = anchorMin;

        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);

        return element;
    }

    public void Text(string text, float y, float height = 60f, float size = 30f)
    {
        TextMeshProUGUI label =
            Element("Text", 0f, y, 550f, height)
                .AddComponent<TextMeshProUGUI>();

        ((TMP_Text)label).font = TMP_Settings.defaultFontAsset;
        ((TMP_Text)label).text = text;
        ((TMP_Text)label).fontSize = size;

        ((Graphic)label).color = TextColor;

        ((TMP_Text)label).richText = false;
        ((TMP_Text)label).enableWordWrapping = true;
        ((TMP_Text)label).alignment = (TextAlignmentOptions)514;

        ((Graphic)label).raycastTarget = false;
    }

    public void Panel(float x, float y, float width, float height, Color color)
    {
        Image image =
            Element("Card", x, y, width, height)
                .AddComponent<Image>();

        image.sprite = roundedSprite;
        image.type = Image.Type.Sliced;

        ((Graphic)image).color = color;
        ((Graphic)image).raycastTarget = false;
    }

    public void Label(
        string text,
        float x,
        float y,
        float width,
        float height,
        float size,
        Color color,
        bool left = false)
    {
        TextMeshProUGUI label =
            Element("Label", x, y, width, height)
                .AddComponent<TextMeshProUGUI>();

        ((TMP_Text)label).font = TMP_Settings.defaultFontAsset;
        ((TMP_Text)label).text = text;
        ((TMP_Text)label).fontSize = size;

        ((Graphic)label).color = color;

        ((TMP_Text)label).richText = false;
        ((TMP_Text)label).enableWordWrapping = true;
        ((TMP_Text)label).alignment =
            (TextAlignmentOptions)(left ? 513 : 514);

        ((Graphic)label).raycastTarget = false;
    }

    public void PhotoTile(
        Texture texture,
        float x,
        float y,
        float width,
        float height,
        Action click)
    {
        Button(
            "",
            x,
            y,
            width,
            height,
            click,
            (Color?)new Color(0.16f, 0.19f, 0.24f, 1f)
        );

        if ((Object)(object)texture != (Object)null)
        {
            Picture(texture, x, y, width - 4f, height - 4f);
        }
        else
        {
            Label("…", x, y, width, height, 28f, Color.white);
        }
    }

    public void Button(
        string text,
        float x,
        float y,
        float width,
        float height,
        Action click,
        Color? tint = null)
    {
        GameObject buttonObject = Element("Button", x, y, width, height);

        Image image = buttonObject.AddComponent<Image>();
        image.sprite = roundedSprite;
        image.type = Image.Type.Sliced;

        ((Graphic)image).color =
            tint ?? new Color(0.12f, 0.17f, 0.28f, 1f);

        ((Graphic)image).raycastTarget = false;

        Button button = buttonObject.AddComponent<Button>();

        ((Selectable)button).targetGraphic = (Graphic)(object)image;

        UIMachineUtilities.AddClickEvent(button, click);

        GameObject labelObject = new GameObject("Label");
        labelObject.layer = 5;

        RectTransform labelRect = labelObject.AddComponent<RectTransform>();
        ((Transform)labelRect).SetParent(buttonObject.transform, false);

        labelRect.sizeDelta = new Vector2(width - 8f, height - 4f);

        TextMeshProUGUI label =
            labelObject.AddComponent<TextMeshProUGUI>();

        ((TMP_Text)label).font = TMP_Settings.defaultFontAsset;
        ((TMP_Text)label).text = text;
        ((TMP_Text)label).fontSize = height < 65f ? 24 : 28;

        ((Graphic)label).color = Color.white;

        ((TMP_Text)label).richText = false;
        ((TMP_Text)label).alignment = (TextAlignmentOptions)514;

        ((Graphic)label).raycastTarget = false;

        GameObject hitObject = new GameObject("HIT");
        hitObject.layer = 5;
        hitObject.transform.SetParent(buttonObject.transform, false);

        BoxCollider collider = hitObject.AddComponent<BoxCollider>();

        collider.size = new Vector3(width, height, 12f);
        collider.center = new Vector3(0f, 0f, -6f);

        ((Collider)collider).isTrigger = true;

        hitObject.AddComponent<FusionUITrigger>().button = button;
    }

    public void Picture(
        Texture texture,
        float x,
        float y,
        float width,
        float height,
        bool crop = false)
    {
        if (!((Object)(object)texture == (Object)null))
        {
            GameObject frame = Element("PhotoFrame", x, y, width, height);

            Image image = frame.AddComponent<Image>();
            image.sprite = roundedSprite;
            image.type = Image.Type.Sliced;

            ((Graphic)image).raycastTarget = false;

            frame.AddComponent<Mask>().showMaskGraphic = false;

            GameObject pixels = new GameObject("Pixels");
            pixels.layer = 5;

            RectTransform rect = pixels.AddComponent<RectTransform>();
            ((Transform)rect).SetParent(frame.transform, false);

            rect.sizeDelta = new Vector2(width, height);

            RawImage rawImage = pixels.AddComponent<RawImage>();
            rawImage.texture = texture;

            ((Graphic)rawImage).raycastTarget = false;

            if (crop && texture.width > 0 && texture.height > 0)
            {
                float textureRatio = (float)texture.width / (float)texture.height;
                float frameRatio = width / height;

                float uvWidth =
                    textureRatio > frameRatio
                        ? frameRatio / textureRatio
                        : 1f;

                float uvHeight =
                    textureRatio > frameRatio
                        ? 1f
                        : textureRatio / frameRatio;

                rawImage.uvRect = new Rect(
                    (1f - uvWidth) * 0.5f,
                    (1f - uvHeight) * 0.5f,
                    uvWidth,
                    uvHeight
                );
            }
        }
    }

    public void MarkIcon(string key, float x, float y, float size)
    {
        if (!icons.TryGetValue(key, out var sprite))
        {
            sprite = PhoneIcons.Create(key);
            icons[key] = sprite;
        }

        Image image =
            Element("ButtonIcon", x, y, size, size)
                .AddComponent<Image>();

        image.sprite = sprite;

        ((Graphic)image).raycastTarget = false;
    }

    public void IconButton(string key, string caption, float y, Action click)
    {
        Button("     " + caption, 0f, y, 540f, 75f, click);

        if (!icons.TryGetValue(key, out var sprite))
        {
            sprite = PhoneIcons.Create(key);
            icons[key] = sprite;
        }

        Image image =
            Element("ButtonIcon", -205f, y, 40f, 40f)
                .AddComponent<Image>();

        image.sprite = sprite;

        ((Graphic)image).raycastTarget = false;
    }

    public void Tile(
        string key,
        string caption,
        float x,
        float y,
        Color color,
        Action click,
        Color? captionColor = null)
    {
        Button("", x, y, 125f, 125f, click, color);

        if (!icons.TryGetValue(key, out var sprite))
        {
            sprite = PhoneIcons.Create(key);
            icons[key] = sprite;
        }

        Image image =
            Element("Icon", x, y, 72f, 72f)
                .AddComponent<Image>();

        image.sprite = sprite;

        ((Graphic)image).raycastTarget = false;

        TextMeshProUGUI label =
            Element("Caption", x, y - 90f, 175f, 48f)
                .AddComponent<TextMeshProUGUI>();

        ((TMP_Text)label).font = TMP_Settings.defaultFontAsset;
        ((TMP_Text)label).text = caption;
        ((TMP_Text)label).fontSize = 23f;

        ((Graphic)label).color = captionColor ?? TextColor;

        ((TMP_Text)label).richText = false;
        ((TMP_Text)label).alignment = (TextAlignmentOptions)514;

        ((Graphic)label).raycastTarget = false;
    }

    public void Finish()
    {
        UIMachineUtilities.OverrideFonts(content.transform);
    }

    public void Tick()
    {
        if (disposed || (Object)(object)Body == (Object)null)
        {
            return;
        }

        if ((Object)(object)cursor == (Object)null &&
            !cursorWarning &&
            Time.realtimeSinceStartup - created > 30f)
        {
            cursorWarning = true;
            warn("Laser téléphone absent : vérifier le pallet Fusion Content.");
        }

        bool held = Held(Player.LeftHand) || Held(Player.RightHand);

        if (held && !wasHeld && firstPickup)
        {
            placed = false;
            faceChosen = false;
            canvasObject.SetActive(false);
            firstPickup = false;
            stableSamples = 0;
            nextPlacement = 0f;
        }

        wasHeld = held;

        bool visible = held || (PhoneMod.Instance?.IsCasting ?? false);

        if (root.activeSelf != visible)
        {
            root.SetActive(visible);
        }

        if (Time.realtimeSinceStartup >= nextPlacement && !placed)
        {
            nextPlacement = Time.realtimeSinceStartup + 0.15f;

            if (Place(settings))
            {
                if (!canvasObject.activeSelf)
                {
                    canvasObject.SetActive(true);
                }

                placed = true;
            }
            else if (!placed)
            {
                canvasObject.SetActive(false);
            }
        }

        if (placed)
        {
            root.transform.localPosition = fitPosition;
            root.transform.localRotation = fitRotation;
            root.transform.localScale = Vector3.one;

            canvasObject.transform.localPosition = Vector3.zero;
            canvasObject.transform.localRotation = Quaternion.identity;

            Vector3 lossyScale = root.transform.lossyScale;

            if (Mathf.Abs(lossyScale.x) > 1E-06f &&
                Mathf.Abs(lossyScale.y) > 1E-06f &&
                Mathf.Abs(lossyScale.z) > 1E-06f)
            {
                canvasObject.transform.localScale = new Vector3(
                    fitWorldScale.x / Mathf.Abs(lossyScale.x),
                    fitWorldScale.y / Mathf.Abs(lossyScale.y),
                    fitWorldScale.z / Mathf.Abs(lossyScale.z)
                );

                UpdateCursorRegion();
            }
        }
    }

    private bool Held(Hand hand)
    {
        GameObject attached =
            hand != null ? hand.m_CurrentAttachedGO : null;

        return (Object)(object)attached != (Object)null &&
            (
                (Object)(object)attached == (Object)(object)Body ||
                attached.transform.IsChildOf(Body.transform) ||
                Body.transform.IsChildOf(attached.transform)
            );
    }

    public void Dispose()
    {
        disposed = true;

        foreach (Sprite sprite in icons.Values)
        {
            Object.Destroy((Object)(object)sprite.texture);
            Object.Destroy((Object)(object)sprite);
        }

        icons.Clear();

        if ((Object)(object)cursor != (Object)null)
        {
            Object.Destroy((Object)(object)((Component)cursor).gameObject);
        }

        if ((Object)(object)root != (Object)null)
        {
            Object.Destroy((Object)(object)root);
        }

        if ((Object)(object)roundedSprite != (Object)null)
        {
            Object.Destroy((Object)(object)roundedSprite);
        }

        if ((Object)(object)roundedTexture != (Object)null)
        {
            Object.Destroy((Object)(object)roundedTexture);
        }
    }
}
